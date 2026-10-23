using System.Text;
using IsWiX.WindowsInstaller.Format.Internal;

namespace IsWiX.WindowsInstaller.Format;

/// <summary>Writes an MSI database from tables and streams. Output can be read back by <see cref="MsiDatabase"/>.</summary>
internal static class MsiWriter
{
    private const int MaxStringLength = 65535;

    // Tables the writer generates itself. Other underscore names, such as _Validation, are ordinary tables.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_Tables", "_Columns", "_StringPool", "_StringData",
    };

    /// <summary>
    /// Writes an MSI database.
    /// </summary>
    /// <param name="output">Stream to write the compound file to.</param>
    /// <param name="codePage">Code page the strings are encoded in. 0 means neutral (Windows-1252 is used).</param>
    /// <param name="tables">The tables. The names _Tables, _Columns, _StringPool and _StringData are reserved.</param>
    /// <param name="streams">Plain streams by decoded name, such as binary data and the summary information stream.</param>
    public static void Write(Stream output, int codePage, IReadOnlyList<MsiWriteTable> tables, IReadOnlyDictionary<string, byte[]>? streams = null)
    {
        var sources = streams?.ToDictionary(p => p.Key, p => StreamSource.Of(p.Value), StringComparer.Ordinal);
        WriteSources(output, codePage, tables, sources);
    }

    /// <summary>
    /// Writes an MSI like <see cref="Write"/>, but each plain stream is given by its length and a reader, so a large
    /// stream is read only while it is being written.
    /// </summary>
    public static void WriteSources(Stream output, int codePage, IReadOnlyList<MsiWriteTable> tables, IReadOnlyDictionary<string, StreamSource>? streams = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(tables);

        foreach (var table in tables)
        {
            if (table.Name.Length == 0 || table.Name.Length > 31)
                throw new ArgumentException($"Invalid table name '{table.Name}'.");
            if (ReservedNames.Contains(table.Name))
                throw new ArgumentException($"'{table.Name}' is written by the writer itself.");
            if (table.Columns.Count == 0)
                throw new ArgumentException($"Table '{table.Name}' has no columns.");
            if (!table.Columns.Any(c => c.IsPrimaryKey))
                throw new ArgumentException($"Table '{table.Name}' has no primary key column.");
        }

        var pool = new StringPoolBuilder();

        // Cells first: string cells get pool ids, so the pool's reference width is known before serializing.
        var prepared = tables.Select(t => PrepareTable(t, pool)).ToList();
        var systemTables = PrepareSystemTables(tables, pool);

        var pooled = pool.Build(codePage);
        int width = pooled.BytesPerRef;

        var streamList = new List<(string Name, long Length, Func<byte[]> Read)>
        {
            Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_StringPool"), pooled.Pool),
            Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_StringData"), pooled.Data),
            Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_Tables"), SerializeColumns(systemTables.TablesCells, width)),
            Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_Columns"), SerializeColumns(systemTables.ColumnsCells, width)),
        };
        foreach (var table in prepared)
        {
            streamList.Add(Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode(table.Name), SerializeColumns(table.Cells, width)));
        }
        if (streams != null)
        {
            foreach (var (name, source) in streams)
            {
                if (name.Length > 0 && name[0] == StreamNameCodec.TablePrefix)
                    throw new ArgumentException($"Stream '{name}' uses the reserved table prefix.");
                // Names starting with U+0005 are OLE property sets, such as the summary information, and are stored as written.
                streamList.Add((name[0] == '\u0005' ? name : StreamNameCodec.Encode(name), source.Length, source.Read));
            }
        }

        CompoundFileWriter.Write(output, streamList);
    }

    private static (string Name, long Length, Func<byte[]> Read) Bytes(string name, byte[] data) => (name, data.Length, () => data);

    private sealed class PreparedTable
    {
        public PreparedTable(string name, Cells cells)
        {
            Name = name;
            Cells = cells;
        }

        public string Name { get; }
        public Cells Cells { get; }
    }

    /// <summary>Cells in column-major order: each column's values, one per row. Strings are pool ids.</summary>
    private sealed class Cells
    {
        public Cells(IReadOnlyList<MsiColumnType> types, IReadOnlyList<int> sizes, IReadOnlyList<uint[]> columns, int rowCount)
        {
            Types = types;
            Sizes = sizes;
            Columns = columns;
            RowCount = rowCount;
        }

        public IReadOnlyList<MsiColumnType> Types { get; }
        public IReadOnlyList<int> Sizes { get; }
        public IReadOnlyList<uint[]> Columns { get; }
        public int RowCount { get; }
    }

    private sealed record SystemTables(Cells TablesCells, Cells ColumnsCells);

    private static PreparedTable PrepareTable(MsiWriteTable table, StringPoolBuilder pool)
    {
        return new PreparedTable(table.Name, Prepare(table.Columns, table.Rows, table.Name, pool));
    }

    private static Cells Prepare(IReadOnlyList<MsiWriteColumn> columns, IReadOnlyList<object?[]> rows, string table, StringPoolBuilder pool)
    {
        var values = new List<uint[]>(columns.Count);
        for (int j = 0; j < columns.Count; j++)
        {
            var column = columns[j];
            var cells = new uint[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length != columns.Count)
                    throw new ArgumentException($"Row {i} of '{table}' has {rows[i].Length} values for {columns.Count} columns.");
                cells[i] = CellValue(column, rows[i][j], table, i, pool);
            }
            values.Add(cells);
        }
        return new Cells(columns.Select(c => c.Type).ToList(), columns.Select(c => c.Size).ToList(), values, rows.Count);
    }

    /// <summary>The _Tables and _Columns system tables, describing the user tables.</summary>
    private static SystemTables PrepareSystemTables(IReadOnlyList<MsiWriteTable> tables, StringPoolBuilder pool)
    {
        var tableNames = tables.Select(t => new object?[] { t.Name }).ToList();
        var tablesColumns = new[] { new MsiWriteColumn("Name", MsiColumnType.String, 64, isPrimaryKey: true) };
        var tablesCells = Prepare(tablesColumns, tableNames, "_Tables", pool);

        var definitions = new List<object?[]>();
        foreach (var table in tables)
        {
            for (int n = 0; n < table.Columns.Count; n++)
            {
                var column = table.Columns[n];
                definitions.Add(new object?[] { table.Name, n + 1, column.Name, TypeWord(column) });
            }
        }
        var columnsColumns = new[]
        {
            new MsiWriteColumn("Table", MsiColumnType.String, 64, isPrimaryKey: true),
            new MsiWriteColumn("Number", MsiColumnType.Integer, 2, isPrimaryKey: true),
            new MsiWriteColumn("Name", MsiColumnType.String, 64),
            new MsiWriteColumn("Type", MsiColumnType.Integer, 2),
        };
        var columnsCells = Prepare(columnsColumns, definitions, "_Columns", pool);
        return new SystemTables(tablesCells, columnsCells);
    }

    /// <summary>The raw type word stored in _Columns: string or integer or binary, with the size and flags.</summary>
    private static int TypeWord(MsiWriteColumn column)
    {
        // 0x0100 marks the column as valid. Strings also set 0x0800; the 0x0100 bit is set for every column kind.
        int flags = 0x0100 | (column.IsNullable ? 0x1000 : 0) | (column.IsPrimaryKey ? 0x2000 : 0) | (column.IsLocalizable ? 0x0200 : 0);
        switch (column.Type)
        {
            case MsiColumnType.String:
                return 0x0800 | 0x0400 | flags | (column.Size & 0xFF);
            case MsiColumnType.Binary:
                return 0x0900 | 0x0100 | (column.IsNullable ? 0x1000 : 0);
            default:
                return 0x0100 | flags | column.Size;
        }
    }

    private static uint CellValue(MsiWriteColumn column, object? value, string table, int row, StringPoolBuilder pool)
    {
        switch (column.Type)
        {
            case MsiColumnType.String:
            {
                if (value == null) return 0;
                if (value is not string text)
                    throw new ArgumentException($"'{table}' row {row}, column '{column.Name}' needs a string.");
                // The declared size is not enforced here. It is checked when a value is inserted or updated, and
                // real packages can hold values longer than their column's size, which Windows Installer reads
                // without complaint. Refusing to write them would stop every change to such a package.
                return pool.Add(text);
            }
            case MsiColumnType.Integer:
            {
                if (value == null) return 0;
                if (value is not int number)
                    throw new ArgumentException($"'{table}' row {row}, column '{column.Name}' needs an integer.");
                if (column.Size == 2)
                {
                    // 16-bit values are stored with 0x8000 added; 0 is reserved for null.
                    if (number < -32767 || number > 32767)
                        throw new ArgumentException($"'{table}' row {row}, column '{column.Name}' is outside the 16-bit range.");
                    return (uint) (number + 0x8000);
                }
                // 32-bit values have the sign bit flipped; int.MinValue would collide with null.
                if (number == int.MinValue)
                    throw new ArgumentException($"'{table}' row {row}, column '{column.Name}' cannot store the null value.");
                return unchecked((uint) number) ^ 0x80000000u;
            }
            default:
                // Binary cells hold no value; the data is in its own stream.
                return 0;
        }
    }

    /// <summary>Serializes a table's cells as column-major little-endian values.</summary>
    private static byte[] SerializeColumns(Cells cells, int stringWidth)
    {
        int rowSize = 0;
        var widths = new int[cells.Types.Count];
        for (int j = 0; j < cells.Types.Count; j++)
        {
            widths[j] = cells.Types[j] switch
            {
                MsiColumnType.String => stringWidth,
                MsiColumnType.Binary => 2,
                _ => cells.Sizes[j],
            };
            rowSize += widths[j];
        }
        var data = new byte[rowSize * cells.RowCount];
        int at = 0;
        for (int j = 0; j < cells.Types.Count; j++)
        {
            uint[] column = cells.Columns[j];
            for (int i = 0; i < cells.RowCount; i++)
            {
                uint value = column[i];
                for (int b = 0; b < widths[j]; b++)
                {
                    data[at++] = (byte) (value >> (8 * b));
                }
            }
        }
        return data;
    }

    private sealed record PooledStrings(byte[] Pool, byte[] Data, int BytesPerRef);

    /// <summary>Builds the _StringPool and _StringData streams from the strings added during preparation.</summary>
    private sealed class StringPoolBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<string> _strings = new();
        private readonly List<int> _refs = new();

        public uint Add(string text)
        {
            if (text.Length == 0)
                return 0; // empty strings are stored as null, which MSI reads back as empty
            if (_ids.TryGetValue(text, out uint id))
            {
                _refs[(int) id - 1]++;
                return id;
            }
            _strings.Add(text);
            _refs.Add(1);
            id = (uint) _strings.Count;
            _ids[text] = id;
            return id;
        }

        public PooledStrings Build(int codePage)
        {
            var encoding = ResolveEncoding(codePage);
            bool longRefs = _strings.Count > 0xFFFF;
            int bytesPerRef = longRefs ? 3 : 2;

            var data = new MemoryStream();
            var pool = new MemoryStream();
            // Header: code page in the low 16 bits of word 0, the high bits in word 1, and the long-reference flag.
            WriteWord(pool, codePage & 0xFFFF);
            WriteWord(pool, ((codePage >> 16) & 0x7FFF) | (longRefs ? 0x8000 : 0));
            for (int i = 0; i < _strings.Count; i++)
            {
                byte[] bytes = encoding.GetBytes(_strings[i]);
                if (bytes.Length > MaxStringLength)
                    throw new ArgumentException("A string is too long for the string pool.");
                WriteWord(pool, bytes.Length);
                WriteWord(pool, Math.Min(_refs[i], 0xFFFF));
                data.Write(bytes, 0, bytes.Length);
            }
            return new PooledStrings(pool.ToArray(), data.ToArray(), bytesPerRef);
        }

        private static void WriteWord(MemoryStream stream, int value)
        {
            stream.WriteByte((byte) value);
            stream.WriteByte((byte) (value >> 8));
        }

        private static Encoding ResolveEncoding(int codePage)
        {
            int cp = codePage == 0 ? 1252 : codePage;
            return cp switch
            {
                65001 => Encoding.UTF8,
                1200 => Encoding.Unicode,
                _ => CodePageEncodings.Get(cp),
            };
        }
    }

    private static class CodePageEncodings
    {
        static CodePageEncodings()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static Encoding Get(int codePage) => Encoding.GetEncoding(codePage);
    }
}
