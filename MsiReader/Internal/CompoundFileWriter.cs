namespace MsiReader.Internal;

/// <summary>
/// Writes an OLE Compound File (MS-CFB, major version 3, 512-byte sectors) that holds the given
/// named streams. Streams shorter than 4096 bytes go in the mini stream, as the format requires.
/// </summary>
internal static class CompoundFileWriter
{
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    private const int FatEntriesPerSector = SectorSize / 4;
    private const int DifatEntriesPerSector = FatEntriesPerSector - 1;
    private const int HeaderDifatEntries = 109;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;
    private const uint NoStream = 0xFFFFFFFF;

    private sealed class Item
    {
        public Item(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public string Name { get; }
        public byte[] Data { get; }
        public bool IsMini => Data.Length > 0 && Data.Length < MiniStreamCutoff;
        public int RegularSectors => (Data.Length + SectorSize - 1) / SectorSize;
        public int MiniSectors => (Data.Length + MiniSectorSize - 1) / MiniSectorSize;

        /// <summary>First sector of the stream, in the regular or the mini sector space.</summary>
        public uint Start { get; set; } = EndOfChain;
        public int DirectoryIndex { get; set; }
        public int Depth { get; set; }
        public int LeftIndex { get; set; } = -1;
        public int RightIndex { get; set; } = -1;
    }

    public static void Write(Stream output, IReadOnlyList<(string Name, byte[] Data)> streams)
    {
        var items = streams.Select(s => new Item(s.Name, s.Data)).ToList();
        items.Sort((a, b) => CompareNames(a.Name, b.Name));
        for (int i = 0; i < items.Count; i++)
        {
            items[i].DirectoryIndex = i + 1; // entry 0 is the root storage
            if (i > 0 && CompareNames(items[i - 1].Name, items[i].Name) == 0)
                throw new ArgumentException($"Duplicate stream name '{items[i].Name}'.");
        }
        int rootChild = items.Count > 0 ? BuildTree(items, 0, items.Count - 1, 0) : -1;
        int maxDepth = items.Count > 0 ? items.Max(i => i.Depth) : 0;

        // Sector counts.
        var mini = items.Where(i => i.IsMini).ToList();
        int miniSectorTotal = mini.Sum(i => i.MiniSectors);
        int containerSectors = (miniSectorTotal * MiniSectorSize + SectorSize - 1) / SectorSize;
        int miniFatSectors = (miniSectorTotal + FatEntriesPerSector - 1) / FatEntriesPerSector;
        int dirSectors = (items.Count + 1 + 3) / 4;
        int regularSectors = items.Where(i => !i.IsMini).Sum(i => i.RegularSectors);
        int fixedSectors = dirSectors + regularSectors + containerSectors + miniFatSectors;

        // FAT and DIFAT sizes depend on each other, so iterate to a fixed point.
        int fatSectors = 0, difatSectors = 0;
        while (true)
        {
            int f = (fixedSectors + difatSectors + fatSectors + FatEntriesPerSector - 1) / FatEntriesPerSector;
            int d = f > HeaderDifatEntries ? (f - HeaderDifatEntries + DifatEntriesPerSector - 1) / DifatEntriesPerSector : 0;
            if (f == fatSectors && d == difatSectors) break;
            fatSectors = f;
            difatSectors = d;
        }
        int totalSectors = fixedSectors + difatSectors + fatSectors;

        // Lay out the sectors: directory, regular streams, mini container, mini FAT, DIFAT, FAT.
        int next = dirSectors;
        foreach (var item in items.Where(i => !i.IsMini && i.Data.Length > 0))
        {
            item.Start = (uint) next;
            next += item.RegularSectors;
        }
        int containerStart = containerSectors > 0 ? next : -2; // -2 is ENDOFCHAIN; unused when there are no sectors
        next += containerSectors;
        int miniFatStart = miniFatSectors > 0 ? next : -2;
        next += miniFatSectors;
        int difatStart = difatSectors > 0 ? next : -2;
        next += difatSectors;
        int fatStart = next;

        uint miniCursor = 0;
        foreach (var item in mini)
        {
            item.Start = miniCursor;
            miniCursor += (uint) item.MiniSectors;
        }

        // FAT: chain every regular run, then mark the FAT and DIFAT sectors.
        var fat = new uint[fatSectors * FatEntriesPerSector];
        Array.Fill(fat, FreeSector);
        ChainRun(fat, 0, dirSectors);
        foreach (var item in items.Where(i => !i.IsMini && i.Data.Length > 0))
            ChainRun(fat, (int) item.Start, item.RegularSectors);
        if (containerSectors > 0) ChainRun(fat, containerStart, containerSectors);
        if (miniFatSectors > 0) ChainRun(fat, miniFatStart, miniFatSectors);
        for (int k = 0; k < difatSectors; k++) fat[difatStart + k] = DifatSector;
        for (int k = 0; k < fatSectors; k++) fat[fatStart + k] = FatSector;

        // Mini FAT: chain each mini stream.
        var miniFat = new uint[miniFatSectors * FatEntriesPerSector];
        Array.Fill(miniFat, FreeSector);
        foreach (var item in mini)
        {
            for (int k = 0; k < item.MiniSectors; k++)
            {
                int index = (int) item.Start + k;
                miniFat[index] = k == item.MiniSectors - 1 ? EndOfChain : (uint) (index + 1);
            }
        }

        // Body: everything after the 512-byte header.
        var body = new byte[totalSectors * SectorSize];
        var container = new byte[containerSectors * SectorSize];
        foreach (var item in mini)
            Array.Copy(item.Data, 0, container, (int) item.Start * MiniSectorSize, item.Data.Length);
        if (containerSectors > 0)
            Array.Copy(container, 0, body, containerStart * SectorSize, container.Length);
        foreach (var item in items.Where(i => !i.IsMini && i.Data.Length > 0))
            Array.Copy(item.Data, 0, body, (int) item.Start * SectorSize, item.Data.Length);

        WriteDirectory(body, items, rootChild, miniSectorTotal, containerStart, maxDepth);
        Words(miniFat, body, miniFatStart);
        Words(fat, body, fatStart);

        // DIFAT sectors list the FAT sectors beyond the 109 held in the header.
        for (int k = 0; k < difatSectors; k++)
        {
            var sector = new uint[FatEntriesPerSector];
            Array.Fill(sector, FreeSector);
            for (int j = 0; j < DifatEntriesPerSector; j++)
            {
                int fatIndex = HeaderDifatEntries + k * DifatEntriesPerSector + j;
                if (fatIndex < fatSectors) sector[j] = (uint) (fatStart + fatIndex);
            }
            sector[FatEntriesPerSector - 1] = k == difatSectors - 1 ? EndOfChain : (uint) (difatStart + k + 1);
            Words(sector, body, difatStart + k);
        }

        var header = new byte[SectorSize];
        var signature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        signature.CopyTo(header, 0);
        Put16(header, 24, 0x003E); // minor version
        Put16(header, 26, 0x0003); // major version
        Put16(header, 28, 0xFFFE); // byte order
        Put16(header, 30, 9);      // 512-byte sectors
        Put16(header, 32, 6);      // 64-byte mini sectors
        Put32(header, 44, (uint) fatSectors);
        Put32(header, 48, 0);      // first directory sector
        Put32(header, 56, MiniStreamCutoff);
        Put32(header, 60, miniFatSectors > 0 ? (uint) miniFatStart : EndOfChain);
        Put32(header, 64, (uint) miniFatSectors);
        Put32(header, 68, difatSectors > 0 ? (uint) difatStart : EndOfChain);
        Put32(header, 72, (uint) difatSectors);
        for (int k = 0; k < HeaderDifatEntries; k++)
            Put32(header, 76 + k * 4, k < fatSectors ? (uint) (fatStart + k) : FreeSector);

        output.Write(header, 0, header.Length);
        output.Write(body, 0, body.Length);
    }

    private static int BuildTree(List<Item> items, int lo, int hi, int depth)
    {
        if (lo > hi) return -1;
        int mid = (lo + hi) / 2;
        Item node = items[mid];
        node.Depth = depth;
        node.LeftIndex = BuildTree(items, lo, mid - 1, depth + 1);
        node.RightIndex = BuildTree(items, mid + 1, hi, depth + 1);
        return node.DirectoryIndex;
    }

    private static void WriteDirectory(byte[] body, List<Item> items, int rootChild, int miniSectorTotal, int containerStart, int maxDepth)
    {
        int entries = items.Count + 1;
        var dir = new byte[((entries + 3) / 4) * 4 * 128];
        for (int e = 0; e < dir.Length / 128; e++)
        {
            int at = e * 128;
            Put32(dir, at + 68, NoStream); // left sibling
            Put32(dir, at + 72, NoStream); // right sibling
            Put32(dir, at + 76, NoStream); // child
        }

        // Root storage.
        WriteEntry(dir, 0, "Root Entry", 5, 1, NoStream, NoStream, rootChild < 0 ? NoStream : (uint) rootChild,
            miniSectorTotal > 0 ? (uint) containerStart : EndOfChain, (ulong) miniSectorTotal * MiniSectorSize);
        // Windows Installer identifies a database by this class id on the root storage.
        byte[] installerClsid = { 0x84, 0x10, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0, 0, 0, 0, 0, 0, 0x46 };
        Array.Copy(installerClsid, 0, dir, 80, installerClsid.Length);

        foreach (var item in items)
        {
            // Red nodes are the deepest level; this keeps every path to a leaf at the same black height.
            int color = maxDepth > 0 && item.Depth == maxDepth ? 0 : 1;
            WriteEntry(dir, item.DirectoryIndex, item.Name, 2, color,
                item.LeftIndex < 0 ? NoStream : (uint) item.LeftIndex,
                item.RightIndex < 0 ? NoStream : (uint) item.RightIndex,
                NoStream, item.Data.Length == 0 ? EndOfChain : item.Start, (ulong) item.Data.Length);
        }

        Array.Copy(dir, 0, body, 0, dir.Length);
    }

    private static void WriteEntry(byte[] dir, int index, string name, byte type, int color,
        uint left, uint right, uint child, uint start, ulong size)
    {
        int at = index * 128;
        for (int k = 0; k < name.Length; k++)
            Put16(dir, at + k * 2, name[k]);
        Put16(dir, at + 64, (ushort) ((name.Length + 1) * 2));
        dir[at + 66] = type;
        dir[at + 67] = (byte) color;
        Put32(dir, at + 68, left);
        Put32(dir, at + 72, right);
        Put32(dir, at + 76, child);
        Put32(dir, at + 116, start);
        Put32(dir, at + 120, (uint) size);
        Put32(dir, at + 124, (uint) (size >> 32));
    }

    private static void ChainRun(uint[] fat, int start, int count)
    {
        for (int k = 0; k < count; k++)
            fat[start + k] = k == count - 1 ? EndOfChain : (uint) (start + k + 1);
    }

    private static void Words(uint[] values, byte[] body, int sector)
    {
        int at = sector * SectorSize;
        for (int k = 0; k < values.Length; k++)
            Put32(body, at + k * 4, values[k]);
    }

    /// <summary>Directory order: shorter names first, then case-insensitive, as MS-CFB specifies.</summary>
    private static int CompareNames(string a, string b)
    {
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            char x = char.ToUpperInvariant(a[i]);
            char y = char.ToUpperInvariant(b[i]);
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static void Put16(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte) value;
        buffer[at + 1] = (byte) (value >> 8);
    }

    private static void Put32(byte[] buffer, int at, uint value)
    {
        buffer[at] = (byte) value;
        buffer[at + 1] = (byte) (value >> 8);
        buffer[at + 2] = (byte) (value >> 16);
        buffer[at + 3] = (byte) (value >> 24);
    }
}
