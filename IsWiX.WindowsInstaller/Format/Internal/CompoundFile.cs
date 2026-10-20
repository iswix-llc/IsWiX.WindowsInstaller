using System.Buffers.Binary;
using System.Text;
using IsWiX.WindowsInstaller.Format;

namespace IsWiX.WindowsInstaller.Format.Internal;

/// <summary>
/// Minimal reader for the OLE Compound File Binary format (MS-CFB), which is the
/// container an .msi file uses. Only what MSI needs is implemented: reading
/// streams from the directory. Storages are skipped, because MSI keeps all of
/// its streams at the root level.
/// </summary>
internal sealed class CompoundFile
{
    private const uint FreeSect = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint MaxRegSect = 0xFFFFFFFA;
    private const int HeaderSize = 512;
    private const int DirEntrySize = 128;
    private const int DifatInHeader = 109;
    private const uint MiniStreamCutoff = 4096;

    private static readonly byte[] Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    private readonly Stream _stream;
    private readonly object _sync = new();
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly ushort _majorVersion;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly byte[] _miniStream;

    /// <summary>All stream entries in the file, keyed by their raw (UTF-16) name.</summary>
    public IReadOnlyList<DirectoryEntry> Streams { get; }

    public CompoundFile(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!_stream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(stream));

        var header = ReadAt(0, HeaderSize);
        if (!header.AsSpan(0, 8).SequenceEqual(Signature))
            throw new MsiFormatException("The file is not an OLE compound file (bad signature).");

        _majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        if (_majorVersion is not (3 or 4))
            throw new MsiFormatException($"Unsupported compound file version {_majorVersion}.");

        int sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
        int miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
        if (sectorShift is not (9 or 12) || miniSectorShift != 6)
            throw new MsiFormatException("Unexpected sector sizes in compound file header.");

        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << miniSectorShift;

        uint fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44));
        uint firstDirSector = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(48));
        uint firstMiniFatSector = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(60));
        uint firstDifatSector = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68));

        // The FAT is located through the DIFAT: 109 entries live in the header,
        // and any extra entries are stored in a chain of DIFAT sectors.
        var fatSectors = new List<uint>();
        for (int i = 0; i < DifatInHeader && fatSectors.Count < fatSectorCount; i++)
        {
            uint sec = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(76 + i * 4));
            if (sec is FreeSect or EndOfChain) break;
            fatSectors.Add(sec);
        }
        uint difatSector = firstDifatSector;
        for (int guard = 0; fatSectors.Count < fatSectorCount && difatSector <= MaxRegSect; guard++)
        {
            if (guard > 100_000) throw new MsiFormatException("DIFAT chain is too long.");
            var difat = ReadSector(difatSector);
            int entries = _sectorSize / 4 - 1;
            for (int i = 0; i < entries && fatSectors.Count < fatSectorCount; i++)
            {
                uint sec = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(i * 4));
                if (sec is FreeSect or EndOfChain) break;
                fatSectors.Add(sec);
            }
            difatSector = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(entries * 4));
        }

        var fat = new List<uint>(fatSectors.Count * (_sectorSize / 4));
        foreach (uint sec in fatSectors)
            AppendUInts(fat, ReadSector(sec));
        _fat = fat.ToArray();

        // Read the directory and locate the root entry, which holds the mini stream.
        var dirBytes = ReadChain(firstDirSector, _fat, _sectorSize, long.MaxValue);
        var entries2 = new List<DirectoryEntry>();
        RootEntry? root = null;
        for (int off = 0; off + DirEntrySize <= dirBytes.Length; off += DirEntrySize)
        {
            var e = dirBytes.AsSpan(off, DirEntrySize);
            byte type = e[66];
            if (type == 0) continue; // unused slot

            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(e[64..]);
            string name = nameLen >= 2
                ? Encoding.Unicode.GetString(e[..Math.Min(nameLen - 2, 64)])
                : string.Empty;
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(e[116..]);
            long size = _majorVersion == 3
                ? BinaryPrimitives.ReadUInt32LittleEndian(e[120..])
                : (long)BinaryPrimitives.ReadUInt64LittleEndian(e[120..]);

            if (type == 5 && root is null)
                root = new RootEntry(start, size);
            else if (type == 2)
                entries2.Add(new DirectoryEntry(name, start, size));
        }

        _miniStream = root is { } r && r.Size > 0
            ? ReadChain(r.Start, _fat, _sectorSize, r.Size)
            : Array.Empty<byte>();

        var miniFat = new List<uint>();
        uint miniFatSector = firstMiniFatSector;
        for (int guard = 0; miniFatSector <= MaxRegSect; guard++)
        {
            if (guard > 1_000_000) throw new MsiFormatException("Mini FAT chain is too long.");
            AppendUInts(miniFat, ReadSector(miniFatSector));
            miniFatSector = NextSector(_fat, miniFatSector);
        }
        _miniFat = miniFat.ToArray();

        Streams = entries2;
    }

    /// <summary>Reads the full contents of a stream entry.</summary>
    public byte[] ReadStream(DirectoryEntry entry)
    {
        if (entry.Size == 0) return Array.Empty<byte>();
        lock (_sync)
        {
            if (entry.Size < MiniStreamCutoff)
                return ReadMiniChain(entry.Start, entry.Size);
            return ReadChain(entry.Start, _fat, _sectorSize, entry.Size);
        }
    }

    private byte[] ReadMiniChain(uint start, long size)
    {
        var result = new byte[size];
        uint sec = start;
        long written = 0;
        for (int guard = 0; written < size; guard++)
        {
            if (sec > MaxRegSect || guard > _miniFat.Length + 1)
                throw new MsiFormatException("Corrupt mini stream chain.");
            long offset = (long)sec * _miniSectorSize;
            if (offset + _miniSectorSize > _miniStream.Length)
                throw new MsiFormatException("Mini stream sector is out of range.");
            int n = (int)Math.Min(_miniSectorSize, size - written);
            Buffer.BlockCopy(_miniStream, (int)offset, result, (int)written, n);
            written += n;
            sec = _miniFat[sec];
        }
        return result;
    }

    private byte[] ReadChain(uint start, uint[] fat, int sectorSize, long size)
    {
        // A chain of sectors can be read without knowing its length in advance,
        // but we cap the result at `size` bytes (or the whole chain when size is unbounded).
        // A stream of known size is copied straight into an array of that size, so it is not grown and copied again.
        bool bounded = size != long.MaxValue;
        var result = bounded ? new byte[size] : null;
        var buffer = bounded ? null : new MemoryStream();
        uint sec = start;
        long remaining = size;
        long written = 0;
        int guard = 0;
        while (sec <= MaxRegSect && remaining > 0)
        {
            if (++guard > fat.Length + 1)
                throw new MsiFormatException("Cycle detected in a sector chain.");
            int take = (int)Math.Min(sectorSize, remaining);
            if (bounded)
            {
                ReadSectorInto(sec, result!, (int)written, take);
            }
            else
            {
                buffer!.Write(ReadSector(sec), 0, take);
            }
            written += take;
            remaining -= take;
            sec = NextSector(fat, sec);
        }
        if (remaining > 0 && bounded)
            throw new MsiFormatException("Sector chain is shorter than the stream size.");
        return bounded ? result! : buffer!.ToArray();
    }

    private static uint NextSector(uint[] fat, uint sec)
    {
        if (sec >= fat.Length)
            throw new MsiFormatException("Sector index is outside the FAT.");
        return fat[sec];
    }

    private byte[] ReadSector(uint sector)
    {
        var buffer = new byte[_sectorSize];
        ReadSectorInto(sector, buffer, 0, _sectorSize);
        return buffer;
    }

    /// <summary>
    /// Reads the first <paramref name="count"/> bytes of a sector into <paramref name="destination"/>. Writing
    /// straight into the destination means a long stream allocates no buffer per sector.
    /// </summary>
    private void ReadSectorInto(uint sector, byte[] destination, int destinationOffset, int count)
    {
        long offset = (long)(sector + 1) * _sectorSize;
        long length = _stream.Length;
        if (offset >= length)
        {
            throw new MsiFormatException(
                $"Sector {sector} starts at byte {offset}, but the file is only {length} bytes " +
                $"(sector size {_sectorSize}). The file is probably truncated.");
        }

        // The last sector of a file may be partial. Writers are not required to pad it,
        // so bytes past the end of the file are read as zeros.
        int available = (int)Math.Min(count, length - offset);
        _stream.Seek(offset, SeekOrigin.Begin);
        int total = 0;
        while (total < available)
        {
            int n = _stream.Read(destination, destinationOffset + total, available - total);
            if (n == 0) break;
            total += n;
        }
        Array.Clear(destination, destinationOffset + total, count - total);
    }

    private byte[] ReadAt(long offset, int count)
    {
        var buffer = new byte[count];
        _stream.Seek(offset, SeekOrigin.Begin);
        int total = 0;
        while (total < count)
        {
            int n = _stream.Read(buffer, total, count - total);
            if (n == 0) throw new MsiFormatException("Unexpected end of file.");
            total += n;
        }
        return buffer;
    }

    private static void AppendUInts(List<uint> list, byte[] data)
    {
        for (int i = 0; i + 4 <= data.Length; i += 4)
            list.Add(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)));
    }

    private readonly record struct RootEntry(uint Start, long Size);

    /// <summary>A stream entry in the compound file directory.</summary>
    public sealed record DirectoryEntry(string RawName, uint Start, long Size);
}
