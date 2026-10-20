using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using IsWiX.WindowsInstaller.Format.Internal;

namespace IsWiX.WindowsInstaller.Format;

/// <summary>
/// A read-only Windows Installer (.msi) database. Works on any platform; it does
/// not use msi.dll.
/// </summary>
internal sealed class MsiDatabase : IDisposable
{
    private readonly CompoundFile _file;
    private readonly StringPool _strings;
    private readonly Dictionary<string, CompoundFile.DirectoryEntry> _tableStreams;
    private readonly Dictionary<string, CompoundFile.DirectoryEntry> _plainStreams;
    private readonly Dictionary<string, List<MsiColumn>> _columns;
    private readonly Dictionary<string, MsiTable> _tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _tableNames;
    private readonly bool _ownsStream;
    private readonly Stream _stream;

    private MsiDatabase(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _file = new CompoundFile(stream);

        _tableStreams = new Dictionary<string, CompoundFile.DirectoryEntry>(StringComparer.Ordinal);
        _plainStreams = new Dictionary<string, CompoundFile.DirectoryEntry>(StringComparer.Ordinal);
        foreach (var entry in _file.Streams)
        {
            if (StreamNameCodec.TryGetTableName(entry.RawName, out var table))
                _tableStreams[table] = entry;
            else
                _plainStreams[StreamNameCodec.Decode(entry.RawName)] = entry;
        }

        if (!_tableStreams.ContainsKey("_Tables") || !_tableStreams.ContainsKey("_Columns"))
            throw new MsiFormatException("The file is missing _Tables or _Columns; it is not an MSI database.");

        _strings = LoadStringPool();
        _columns = LoadColumnDefinitions();
        _tableNames = LoadTableNames();
    }

    /// <summary>Opens an MSI file from disk. The file is read on demand, not loaded into memory.</summary>
    public static MsiDatabase Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new MsiDatabase(fs, ownsStream: true);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an MSI from a stream. The stream must be seekable; a non-seekable stream
    /// is copied into memory first.
    /// </summary>
    public static MsiDatabase Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return new MsiDatabase(copy, ownsStream: true);
        }
        return new MsiDatabase(stream, ownsStream: !leaveOpen);
    }

    /// <summary>Names of the tables in the database, in the order listed in _Tables.</summary>
    public IReadOnlyList<string> TableNames => _tableNames;

    /// <summary>Names of the non-table streams, such as binary data and the summary information.</summary>
    public IReadOnlyCollection<string> StreamNames => _plainStreams.Keys;

    /// <summary>The Windows code page that the database strings are stored in.</summary>
    public int CodePage => _strings.CodePage;

    /// <summary>The string pool index of a string in the file, or null if the file's pool does not have it.</summary>
    public uint? StringIdOf(string text) => _strings.IdOf(text);

    /// <summary>Returns true if the database contains a table with this name.</summary>
    public bool ContainsTable(string name) => _tableNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads a table, or returns false if it does not exist.</summary>
    public bool TryGetTable(string name, [NotNullWhen(true)] out MsiTable? table)
    {
        table = null;
        string? actual = _tableNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (actual is null) return false;

        if (!_tableCache.TryGetValue(actual, out table))
        {
            table = LoadTable(actual);
            _tableCache[actual] = table;
        }
        return true;
    }

    /// <summary>Loads a table, throwing if it does not exist.</summary>
    public MsiTable GetTable(string name) =>
        TryGetTable(name, out var t) ? t : throw new KeyNotFoundException($"No table named '{name}'.");

    /// <summary>The size of a non-table stream in bytes, or null if no such stream exists. Reads nothing.</summary>
    public long? StreamLength(string streamName) =>
        _plainStreams.TryGetValue(streamName, out var entry) ? (long) entry.Size : null;

    /// <summary>Reads a non-table stream by its decoded name, or returns null if it does not exist.</summary>
    public byte[]? ReadStream(string streamName)
    {
        return _plainStreams.TryGetValue(streamName, out var entry)
            ? _file.ReadStream(entry)
            : null;
    }

    public void Dispose()
    {
        // The caller's stream is closed only when this database opened it.
        if (_ownsStream) _stream.Dispose();
    }

    // ---- Loading -------------------------------------------------------------

    private StringPool LoadStringPool()
    {
        if (!_tableStreams.TryGetValue("_StringPool", out var poolEntry))
            return StringPool.Empty;
        _tableStreams.TryGetValue("_StringData", out var dataEntry);
        return StringPool.Load(
            _file.ReadStream(poolEntry),
            dataEntry is null ? Array.Empty<byte>() : _file.ReadStream(dataEntry));
    }

    private List<string> LoadTableNames()
    {
        var raw = ReadRaw("_Tables", new[]
        {
            new MsiColumn("_Tables", 1, "Name", 0x0800 | 0x0100 | 0x2000 | 64),
        });
        return raw.Rows.Select(r => (string?)r[0] ?? throw new MsiFormatException("_Tables has a null name."))
                       .ToList();
    }

    /// <summary>Reads the _Columns table to build the column definition for every table.</summary>
    private Dictionary<string, List<MsiColumn>> LoadColumnDefinitions()
    {
        // The _Columns table describes itself, so its layout is fixed here.
        var layout = new[]
        {
            new MsiColumn("_Columns", 1, "Table", 0x0800 | 0x0100 | 0x2000 | 64),
            new MsiColumn("_Columns", 2, "Number", 0x0100 | 0x2000 | 2),
            new MsiColumn("_Columns", 3, "Name", 0x0800 | 0x0100 | 64),
            new MsiColumn("_Columns", 4, "Type", 0x0100 | 2),
        };
        var raw = ReadRaw("_Columns", layout);

        var result = new Dictionary<string, List<MsiColumn>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in raw.Rows)
        {
            string table = (string?)row[0] ?? throw new MsiFormatException("_Columns has a null table name.");
            int number = (int?)row[1] ?? throw new MsiFormatException("_Columns has a null column number.");
            string name = (string?)row[2] ?? throw new MsiFormatException("_Columns has a null column name.");
            int rawType = (int?)row[3] ?? throw new MsiFormatException("_Columns has a null column type.");

            if (!result.TryGetValue(table, out var list))
                result[table] = list = new List<MsiColumn>();
            list.Add(new MsiColumn(table, number, name, rawType));
        }

        foreach (var (table, cols) in result)
        {
            cols.Sort((a, b) => a.Number.CompareTo(b.Number));
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i].Number != i + 1)
                    throw new MsiFormatException($"Table '{table}' has missing or duplicate column numbers.");
            }
        }
        return result;
    }

    private MsiTable LoadTable(string name)
    {
        if (!_columns.TryGetValue(name, out var cols) || cols.Count == 0)
            throw new MsiFormatException($"Table '{name}' has no column definitions in _Columns.");

        var raw = ReadRaw(name, cols);
        var rows = new List<MsiRow>(raw.Rows.Count);
        var table = new MsiTable(this, name, cols, rows);
        for (int i = 0; i < raw.Rows.Count; i++)
            rows.Add(new MsiRow(table, i, raw.Rows[i].Values));
        return table;
    }

    private sealed record RawTable(IReadOnlyList<RawRow> Rows);
    private sealed record RawRow(object?[] Values)
    {
        public object? this[int i] => Values[i];
    }

    /// <summary>
    /// Reads a table's stream. The stream is column-major: all of column 1 comes first,
    /// then all of column 2, and so on.
    /// </summary>
    private RawTable ReadRaw(string tableName, IReadOnlyList<MsiColumn> cols)
    {
        int bpr = _strings.BytesPerRef;
        int rowSize = cols.Sum(c => c.StorageWidth(bpr));
        if (!_tableStreams.TryGetValue(tableName, out var entry))
            return new RawTable(Array.Empty<RawRow>());

        var data = _file.ReadStream(entry);
        if (data.Length == 0)
            return new RawTable(Array.Empty<RawRow>());
        if (rowSize == 0 || data.Length % rowSize != 0)
            throw new MsiFormatException($"Table '{tableName}' has an invalid size for its columns.");

        int rowCount = data.Length / rowSize;
        var rows = new RawRow[rowCount];
        for (int i = 0; i < rowCount; i++)
            rows[i] = new RawRow(new object?[cols.Count]);

        int columnBase = 0;
        for (int j = 0; j < cols.Count; j++)
        {
            var col = cols[j];
            int width = col.StorageWidth(bpr);
            int start = columnBase * rowCount;
            for (int i = 0; i < rowCount; i++)
            {
                uint raw = ReadUInt(data, start + i * width, width);
                rows[i].Values[j] = DecodeCell(col, raw);
            }
            columnBase += width;
        }
        return new RawTable(rows);
    }

    private object? DecodeCell(MsiColumn col, uint raw)
    {
        switch (col.Type)
        {
            case MsiColumnType.String:
                return raw == 0 ? null : _strings[raw];
            case MsiColumnType.Integer:
                if (raw == 0) return null;
                // MSI stores integers with a bias: 16-bit values have 0x8000 added,
                // 32-bit values have the sign bit flipped. Zero means null.
                return col.Size == 2
                    ? (int)raw - 0x8000
                    : unchecked((int)(raw ^ 0x80000000u));
            case MsiColumnType.Binary:
                // The binary value is only a placeholder; the data is read through the key columns.
                return null;
            default:
                throw new InvalidOperationException();
        }
    }

    private static uint ReadUInt(byte[] data, int offset, int width) => width switch
    {
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
        3 => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16)),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)),
        _ => throw new MsiFormatException($"Unsupported column width {width}."),
    };
}
