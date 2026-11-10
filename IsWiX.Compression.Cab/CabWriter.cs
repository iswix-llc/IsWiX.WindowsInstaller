namespace IsWiX.Compression.Cab
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Writes cabinets: one folder holding all files, stored, MSZIP or LZX compressed, split over several cabinet
    /// files when a maximum size is given.
    /// </summary>
    internal static class CabWriter
    {
        private const int BlockSize = 32768;
        private const ushort UtfNameAttribute = 0x80;
        private const int HeaderSize = 36;

        internal static void Write(CabEngine engine, IPackStreamContext context, IEnumerable<string> files, long maxArchiveSize)
        {
            var entries = new List<FileEntry>();
            var blockStarts = new List<long>();
            var blockLengths = new List<int>();
            var blockUncompressed = new List<int>();

            var fileList = new List<string>(files);
            var compression = new FolderCompression(engine.CompressionLevel);

            long uncompressedOffset = 0;
            var buffer = new byte[BlockSize];
            var buffered = 0;

            Stream dataStream;
            string tempData = null;
            if (engine.UseTempFiles)
            {
                tempData = Path.Combine(Path.GetTempPath(), "iswix-cab-" + Guid.NewGuid().ToString("N") + ".tmp");
                dataStream = new FileStream(tempData, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            }
            else
            {
                dataStream = new MemoryStream();
            }

            try
            {
                foreach (var name in fileList)
                {
                    FileAttributes attributes;
                    DateTime lastWriteTime;
                    var input = context.OpenFileReadStream(name, out attributes, out lastWriteTime);
                    if (input == null)
                    {
                        throw new CabException("Cannot add file to cabinet: " + name);
                    }

                    try
                    {
                        var entry = new FileEntry
                        {
                            Name = name,
                            Offset = (uint)uncompressedOffset,
                            Attributes = (ushort)((int)attributes & 0x27),
                            NameBytes = Encoding.UTF8.GetBytes(name),
                        };
                        entry.IsUtf = entry.NameBytes.Length != name.Length;
                        ToDosDateTime(lastWriteTime, out entry.Date, out entry.Time);

                        long size = 0;
                        int read;
                        while ((read = input.Read(buffer, buffered, buffer.Length - buffered)) > 0)
                        {
                            buffered += read;
                            size += read;
                            uncompressedOffset += read;
                            if (uncompressedOffset > UInt32.MaxValue)
                            {
                                throw new CabException("The files are too large for a cabinet.");
                            }

                            if (buffered == buffer.Length)
                            {
                                AddBlock(dataStream, buffer, buffered, compression, blockStarts, blockLengths, blockUncompressed);
                                buffered = 0;
                            }
                        }

                        entry.Size = (uint)size;
                        entries.Add(entry);
                    }
                    finally
                    {
                        context.CloseFileReadStream(name, input);
                    }
                }

                if (buffered > 0)
                {
                    AddBlock(dataStream, buffer, buffered, compression, blockStarts, blockLengths, blockUncompressed);
                }

                var limit = maxArchiveSize > 0 ? maxArchiveSize : Int64.MaxValue;
                var ranges = PlanCabinets(entries, blockLengths, blockUncompressed, limit);
                if (ranges.Count > 676 + 1)
                {
                    throw new CabException("Cannot split a cabinet more than 676 times.");
                }

                var names = new List<string>();
                for (var k = 0; k < ranges.Count; k++)
                {
                    names.Add(context.GetArchiveName(k));
                }

                var setId = (ushort)(ranges.Count > 1 ? 0x1234 : 0);
                for (var k = 0; k < ranges.Count; k++)
                {
                    var output = context.OpenArchiveWriteStream(k, names[k], true, engine);
                    if (output == null)
                    {
                        throw new CabException("Cannot create cabinet: " + names[k]);
                    }

                    try
                    {
                        WriteCabinet(output, dataStream, entries, blockStarts, blockLengths, blockUncompressed, ranges, k, names, compression, setId);
                    }
                    finally
                    {
                        context.CloseArchiveWriteStream(k, names[k], output);
                    }
                }
            }
            finally
            {
                dataStream.Dispose();
                if (tempData != null)
                {
                    try
                    {
                        File.Delete(tempData);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static void AddBlock(Stream dataStream, byte[] buffer, int count, FolderCompression compression, List<long> starts, List<int> lengths, List<int> uncompressed)
        {
            var start = dataStream.Position;
            WriteBlock(dataStream, buffer, count, compression);
            starts.Add(start);
            lengths.Add((int)(dataStream.Position - start));
            uncompressed.Add(count);
        }

        // Returns, for each cabinet, the first block it holds and the block after its last.
        private static List<int[]> PlanCabinets(List<FileEntry> entries, List<int> blockLengths, List<int> blockUncompressed, long limit)
        {
            var ranges = new List<int[]>();
            var blockCount = blockLengths.Count;
            if (blockCount == 0)
            {
                ranges.Add(new[] { 0, 0 });
                return ranges;
            }

            var uncompressedStart = new long[blockCount + 1];
            for (var i = 0; i < blockCount; i++)
            {
                uncompressedStart[i + 1] = uncompressedStart[i] + blockUncompressed[i];
            }

            var first = 0;
            while (first < blockCount)
            {
                var end = first + 1;
                while (end < blockCount)
                {
                    if (EstimateSize(entries, blockLengths, uncompressedStart, first, end + 1, blockCount) > limit)
                    {
                        break;
                    }

                    end++;
                }

                ranges.Add(new[] { first, end });
                first = end;
            }

            return ranges;
        }

        private static long EstimateSize(List<FileEntry> entries, List<int> blockLengths, long[] uncompressedStart, int first, int end, int blockCount)
        {
            long size = HeaderSize + 256 + 8;
            for (var i = first; i < end; i++)
            {
                size += blockLengths[i];
            }

            var from = uncompressedStart[first];
            var to = uncompressedStart[end];
            foreach (var entry in entries)
            {
                if (Intersects(entry, from, to, end == blockCount))
                {
                    size += 16 + entry.NameBytes.Length + 1;
                }
            }

            return size;
        }

        private static bool Intersects(FileEntry entry, long from, long to, bool last)
        {
            long start = entry.Offset;
            long finish = start + entry.Size;
            if (entry.Size == 0)
            {
                return (start >= from && start < to) || (last && start == to);
            }

            return start < to && finish > from;
        }

        private static void WriteCabinet(Stream output, Stream dataStream, List<FileEntry> entries, List<long> blockStarts, List<int> blockLengths, List<int> blockUncompressed, List<int[]> ranges, int index, List<string> names, FolderCompression compression, ushort setId)
        {
            var firstBlock = ranges[index][0];
            var endBlock = ranges[index][1];
            var blockCount = endBlock - firstBlock;
            var hasPrevious = index > 0;
            var hasNext = index < ranges.Count - 1;
            var isLast = !hasNext;

            long from = 0;
            for (var i = 0; i < firstBlock; i++)
            {
                from += blockUncompressed[i];
            }

            long to = from;
            for (var i = firstBlock; i < endBlock; i++)
            {
                to += blockUncompressed[i];
            }

            var inCabinet = new List<FileEntry>();
            foreach (var entry in entries)
            {
                if (Intersects(entry, from, to, isLast))
                {
                    inCabinet.Add(entry);
                }
            }

            var previousName = hasPrevious ? Encoding.ASCII.GetBytes(names[index - 1]) : null;
            var nextName = hasNext ? Encoding.ASCII.GetBytes(names[index + 1]) : null;
            var disk = new byte[] { (byte)'1', 0 };

            var folderCount = blockCount > 0 ? 1 : 0;
            var flags = (hasPrevious ? 1 : 0) | (hasNext ? 2 : 0);
            var headerExtra = (hasPrevious ? previousName.Length + 1 + disk.Length : 0) + (hasNext ? nextName.Length + 1 + disk.Length : 0);
            var filesOffset = HeaderSize + headerExtra + (folderCount * 8);

            long fileBytes = 0;
            foreach (var entry in inCabinet)
            {
                fileBytes += 16 + entry.NameBytes.Length + 1;
            }

            long dataLength = 0;
            for (var i = firstBlock; i < endBlock; i++)
            {
                dataLength += blockLengths[i];
            }

            var dataOffset = filesOffset + fileBytes;
            var total = dataOffset + dataLength;
            if (total > UInt32.MaxValue)
            {
                throw new CabException("The cabinet would be larger than 4 GB.");
            }

            var writer = new BinaryWriter(output);
            writer.Write(new byte[] { (byte)'M', (byte)'S', (byte)'C', (byte)'F' });
            writer.Write(0u);                       // reserved1
            writer.Write((uint)total);              // cbCabinet
            writer.Write(0u);                       // reserved2
            writer.Write((uint)filesOffset);        // coffFiles
            writer.Write(0u);                       // reserved3
            writer.Write((byte)3);                  // versionMinor
            writer.Write((byte)1);                  // versionMajor
            writer.Write((ushort)folderCount);      // cFolders
            writer.Write((ushort)inCabinet.Count);  // cFiles
            writer.Write((ushort)flags);            // flags
            writer.Write(setId);                    // setID
            writer.Write((ushort)index);            // iCabinet

            if (hasPrevious)
            {
                writer.Write(previousName);
                writer.Write((byte)0);
                writer.Write(disk);
            }

            if (hasNext)
            {
                writer.Write(nextName);
                writer.Write((byte)0);
                writer.Write(disk);
            }

            if (folderCount == 1)
            {
                writer.Write((uint)dataOffset);         // coffCabStart
                writer.Write((ushort)blockCount);       // cCFData
                writer.Write(compression.TypeCompress); // typeCompress
            }

            foreach (var entry in inCabinet)
            {
                long start = entry.Offset;
                long finish = start + entry.Size;
                var continuedFromPrevious = start < from;
                var continuedToNext = !isLast && finish > to;
                ushort folder = 0;
                if (continuedFromPrevious && continuedToNext)
                {
                    folder = 0xffff;
                }
                else if (continuedFromPrevious)
                {
                    folder = 0xfffd;
                }
                else if (continuedToNext)
                {
                    folder = 0xfffe;
                }

                writer.Write(entry.Size);
                writer.Write(entry.Offset);
                writer.Write(folder);
                writer.Write(entry.Date);
                writer.Write(entry.Time);
                writer.Write((ushort)(entry.Attributes | (entry.IsUtf ? UtfNameAttribute : 0)));
                writer.Write(entry.NameBytes);
                writer.Write((byte)0);
            }

            writer.Flush();

            var copy = new byte[81920];
            for (var i = firstBlock; i < endBlock; i++)
            {
                dataStream.Position = blockStarts[i];
                var remaining = blockLengths[i];
                while (remaining > 0)
                {
                    var read = dataStream.Read(copy, 0, Math.Min(copy.Length, remaining));
                    if (read <= 0)
                    {
                        throw new EndOfStreamException();
                    }

                    output.Write(copy, 0, read);
                    remaining -= read;
                }
            }

            output.Flush();
        }

        private static void ToDosDateTime(DateTime local, out ushort date, out ushort time)
        {
            if (local.Year < 1980 || local.Year > 2107)
            {
                date = 0x21; // 1980-01-01
                time = 0;
                return;
            }

            date = (ushort)(((local.Year - 1980) << 9) | (local.Month << 5) | local.Day);
            time = (ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2));
        }

        // Writes one CFDATA block.
        private static void WriteBlock(Stream output, byte[] data, int count, FolderCompression compression)
        {
            byte[] payload;
            if (compression.Lzx != null)
            {
                payload = compression.Lzx.EncodeFrame(data, count);
            }
            else if (compression.Mszip)
            {
                payload = CompressMszipBlock(data, count);
            }
            else
            {
                payload = new byte[count];
                Buffer.BlockCopy(data, 0, payload, 0, count);
            }

            var cbData = (ushort)payload.Length;
            var cbUncomp = (ushort)count;
            var checksum = Checksum(payload, payload.Length, 0);
            var sizes = new[] { (byte)(cbData & 0xff), (byte)((cbData >> 8) & 0xff), (byte)(cbUncomp & 0xff), (byte)((cbUncomp >> 8) & 0xff) };
            checksum = Checksum(sizes, 4, checksum);

            var header = new byte[8];
            header[0] = (byte)(checksum & 0xff);
            header[1] = (byte)((checksum >> 8) & 0xff);
            header[2] = (byte)((checksum >> 16) & 0xff);
            header[3] = (byte)((checksum >> 24) & 0xff);
            Buffer.BlockCopy(sizes, 0, header, 4, 4);

            output.Write(header, 0, header.Length);
            output.Write(payload, 0, payload.Length);
        }

        private static byte[] CompressMszipBlock(byte[] data, int count)
        {
            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                using (var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    deflate.Write(data, 0, count);
                }

                compressed = ms.ToArray();
            }

            // An incompressible block is stored instead (a single final stored deflate block).
            byte[] deflated;
            if (compressed.Length > count + 5)
            {
                deflated = new byte[count + 5];
                deflated[0] = 0x01;
                deflated[1] = (byte)(count & 0xff);
                deflated[2] = (byte)((count >> 8) & 0xff);
                deflated[3] = (byte)(~count & 0xff);
                deflated[4] = (byte)((~count >> 8) & 0xff);
                Buffer.BlockCopy(data, 0, deflated, 5, count);
            }
            else
            {
                deflated = compressed;
            }

            var result = new byte[deflated.Length + 2];
            result[0] = (byte)'C';
            result[1] = (byte)'K';
            Buffer.BlockCopy(deflated, 0, result, 2, deflated.Length);
            return result;
        }

        // The cabinet checksum: XOR of little-endian 32-bit words, with the trailing bytes folded in big-endian order.
        private static uint Checksum(byte[] data, int count, uint seed)
        {
            var checksum = seed;
            var words = count / 4;
            var i = 0;
            for (var w = 0; w < words; w++, i += 4)
            {
                checksum ^= unchecked((uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24)));
            }

            uint rest = 0;
            switch (count & 3)
            {
                case 3:
                    rest = (uint)((data[i] << 16) | (data[i + 1] << 8) | data[i + 2]);
                    break;
                case 2:
                    rest = (uint)((data[i] << 8) | data[i + 1]);
                    break;
                case 1:
                    rest = data[i];
                    break;
            }

            return checksum ^ rest;
        }

        // How the data blocks of the cabinet folder are compressed. The LZX encoder is stateful: it carries its window from block to block.
        private sealed class FolderCompression
        {
            public bool Mszip;
            public LzxEncoder Lzx;
            public ushort TypeCompress;

            public FolderCompression(CompressionLevel level)
            {
                if (level == CompressionLevel.None)
                {
                    return;
                }

                if (level == CompressionLevel.Min)
                {
                    this.Mszip = true;
                    this.TypeCompress = 1;
                    return;
                }

                var windowBits = level == CompressionLevel.Max ? 21 : 18;
                this.Lzx = new LzxEncoder(windowBits);
                this.TypeCompress = (ushort)(3 | (windowBits << 8));
            }
        }

        private sealed class FileEntry
        {
            public string Name;
            public byte[] NameBytes;
            public bool IsUtf;
            public uint Size;
            public uint Offset;
            public ushort Date;
            public ushort Time;
            public ushort Attributes;
        }
    }
}
