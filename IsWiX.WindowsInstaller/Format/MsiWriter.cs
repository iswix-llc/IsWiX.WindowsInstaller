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
    public static void WriteSources(Stream output, int codePage, IReadOnlyList<MsiWriteTable> tables, IReadOnlyDictionary<string, StreamSource>? streams = null, IReadOnlyList<string?>? stringPoolSeed = null, IReadOnlyList<CompoundFileWriter.Storage>? storages = null, byte[]? rootClsid = null, bool windowsLayout = true, IReadOnlyList<PoolEvent>? poolEvents = null, StringPoolCarrier? carrier = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (tables is null) throw new ArgumentNullException(nameof(tables));

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

        // A database that is written after every change (Direct) hands the same pool from one write to the next, so ids,
        // free ids and the summary step survive; a pool that does not match the file's is started again from the file.
        StringPoolBuilder pool;
        bool freshPool;
        int eventsDone;
        if (carrier?.Builder is StringPoolBuilder carried && stringPoolSeed != null && carried.IdCount + 1 == stringPoolSeed.Count)
        {
            pool = carried;
            pool.ResetReferences();
            freshPool = carrier.Fresh;
            eventsDone = carrier.EventsDone;
        }
        else
        {
            pool = new StringPoolBuilder(stringPoolSeed);
            freshPool = stringPoolSeed == null || StringPoolBuilder.IsEmptyPool(stringPoolSeed);
            eventsDone = 0;
        }

        // Cells first: string cells get pool ids, so the pool's reference width is known before serializing.
        // Importing _SummaryInformation interns its values and releases them again; the ids they free are reused by the
        // strings added next, newest first. Only a new database starts from an empty pool, so only then is this modelled.
        // Windows Installer also takes strings for a stream put through _Streams and gives them back. The events are
        // applied in the order they happened, each after the table that was imported last when it happened.
        int eventCount = freshPool && poolEvents != null ? poolEvents.Count : 0;
        var prepared = new List<PreparedTable>();
        while (eventsDone < eventCount && (poolEvents![eventsDone].AfterTable == null || !tables.Any(t => t.Name == poolEvents[eventsDone].AfterTable)))
        {
            poolEvents[eventsDone++].Apply(pool);
        }
        foreach (MsiWriteTable table in tables)
        {
            prepared.Add(PrepareTable(table, pool));
            while (eventsDone < eventCount && poolEvents![eventsDone].AfterTable == table.Name)
            {
                poolEvents[eventsDone++].Apply(pool);
            }
        }
        var systemTables = PrepareSystemTables(tables, pool);

        var pooled = pool.Build(codePage);
        if (carrier != null)
        {
            carrier.Builder = pool;
            carrier.Fresh = freshPool;
            carrier.EventsDone = eventsDone;
        }
        int width = pooled.BytesPerRef;

        // Windows Installer creates the streams in this order, and the compound file lists them in the order they were created:
        // the table streams in table order with the summary information after the first one, then other streams such as
        // cabinets, then _Validation, then the system tables.
        var tableStreams = new List<(string Name, long Length, Func<byte[]> Read)>();
        (string Name, long Length, Func<byte[]> Read)? validation = null;
        foreach (var table in prepared)
        {
            // Windows Installer leaves out the stream of a table with no rows.
            if (table.Cells.RowCount == 0) continue;
            var entry = Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode(table.Name), SerializeColumns(table.Cells, width));
            if (table.Name == "_Validation") validation = entry;
            else tableStreams.Add(entry);
        }
        (string Name, long Length, Func<byte[]> Read)? summary = null;
        var otherStreams = new List<(string Name, long Length, Func<byte[]> Read)>();
        if (streams != null)
        {
            foreach (var (name, source) in streams)
            {
                if (name.Length > 0 && name[0] == StreamNameCodec.TablePrefix)
                    throw new ArgumentException($"Stream '{name}' uses the reserved table prefix.");
                // Names starting with U+0005 are OLE property sets, such as the summary information, and are stored as written.
                var entry = (name[0] == '\u0005' ? name : StreamNameCodec.Encode(name), source.Length, source.Read);
                if (name == SummaryStream.Name) summary = entry;
                else otherStreams.Add(entry);
            }
        }

        var streamList = new List<(string Name, long Length, Func<byte[]> Read)>();
        for (int i = 0; i < tableStreams.Count; i++)
        {
            streamList.Add(tableStreams[i]);
            if (i == 0 && summary != null) streamList.Add(summary.Value);
        }
        if (tableStreams.Count == 0 && summary != null) streamList.Add(summary.Value);
        streamList.AddRange(otherStreams);
        if (validation != null) streamList.Add(validation.Value);
        streamList.Add(Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_Tables"), SerializeColumns(systemTables.TablesCells, width)));
        streamList.Add(Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_Columns"), SerializeColumns(systemTables.ColumnsCells, width)));
        streamList.Add(Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_StringPool"), pooled.Pool));
        streamList.Add(Bytes(StreamNameCodec.TablePrefix + StreamNameCodec.Encode("_StringData"), pooled.Data));

        CompoundFileWriter.Write(output, streamList, rootClsid, storages, windowsLayout);
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

    /// <summary>
    /// Orders the rows by their primary key cells (string pool ids and encoded integers). Windows Installer looks rows
    /// up by binary search, so a table stream with its rows in any other order is rejected by msi.dll.
    /// </summary>
    private static Cells SortByPrimaryKey(Cells cells, IReadOnlyList<MsiWriteColumn> columns)
    {
        var keyColumns = new List<int>();
        for (int j = 0; j < columns.Count; j++)
        {
            if (columns[j].IsPrimaryKey) keyColumns.Add(j);
        }
        if (cells.RowCount < 2 || keyColumns.Count == 0) return cells;

        var order = Enumerable.Range(0, cells.RowCount).ToArray();
        Array.Sort(order, (a, b) =>
        {
            foreach (int j in keyColumns)
            {
                int c = cells.Columns[j][a].CompareTo(cells.Columns[j][b]);
                if (c != 0) return c;
            }
            return a.CompareTo(b);
        });

        var sorted = new List<uint[]>(cells.Columns.Count);
        foreach (uint[] column in cells.Columns)
        {
            var copy = new uint[column.Length];
            for (int i = 0; i < order.Length; i++) copy[i] = column[order[i]];
            sorted.Add(copy);
        }
        return new Cells(cells.Types, cells.Sizes, sorted, cells.RowCount);
    }

    private static PreparedTable PrepareTable(MsiWriteTable table, StringPoolBuilder pool)
    {
        // The table name and then the column names get their ids first, as when Windows Installer creates the table.
        pool.Reserve(table.Name);
        foreach (MsiWriteColumn column in table.Columns)
            pool.Reserve(column.Name);
        Cells cells = Prepare(table.Columns, table.Rows, table.Name, pool, table.InternReversed);
        return new PreparedTable(table.Name, SortByPrimaryKey(cells, table.Columns));
    }

    private static Cells Prepare(IReadOnlyList<MsiWriteColumn> columns, IReadOnlyList<object?[]> rows, string table, StringPoolBuilder pool, bool reversed = false)
    {
        // Strings get their ids row by row, left to right, which is the order Windows Installer reads an IDT file in.
        var values = new List<uint[]>(columns.Count);
        for (int j = 0; j < columns.Count; j++)
            values.Add(new uint[rows.Count]);
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length != columns.Count)
                throw new ArgumentException($"Row {i} of '{table}' has {rows[i].Length} values for {columns.Count} columns.");
            for (int n = 0; n < columns.Count; n++)
            {
                int j = reversed ? columns.Count - 1 - n : n;
                values[j][i] = CellValue(columns[j], rows[i][j], table, i, pool);
            }
        }
        return new Cells(columns.Select(c => c.Type).ToList(), columns.Select(c => c.Size).ToList(), values, rows.Count);
    }

    /// <summary>The _Tables and _Columns system tables, describing the user tables.</summary>
    private static SystemTables PrepareSystemTables(IReadOnlyList<MsiWriteTable> tables, StringPoolBuilder pool)
    {
        var tableNames = tables.Select(t => new object?[] { t.Name }).ToList();
        var tablesColumns = new[] { new MsiWriteColumn("Name", MsiColumnType.String, 64, isPrimaryKey: true) };
        var tablesCells = SortByPrimaryKey(Prepare(tablesColumns, tableNames, "_Tables", pool), tablesColumns);

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
        var columnsCells = SortByPrimaryKey(Prepare(columnsColumns, definitions, "_Columns", pool), columnsColumns);
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
                // Windows Installer marks 16-bit integer columns with 0x0400 (0x0502, 0x1502, 0x2502). Without it
                // the installer rejects the database as unopenable. 32-bit columns do not carry the bit.
                return flags | column.Size | (column.Size == 2 ? 0x0400 : 0);
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
                // A binary cell holds only a marker: 1 when its data is present in its own stream, 0 when null.
                // Windows Installer writes 1 for every binary cell; a 0 makes it treat the stream as missing.
                return value == null ? 0u : 1u;
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

    internal sealed record PooledStrings(byte[] Pool, byte[] Data, int BytesPerRef);

    /// <summary>Builds the _StringPool and _StringData streams from the strings added during preparation.</summary>
    internal sealed class StringPoolBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<string> _strings = new();
        private readonly List<int> _refs = new();

        /// <summary>
        /// Starts the pool. Windows Installer keeps a string's id for the life of a database, and rejects a database
        /// whose ids it does not recognize (error 2219), so a database that was loaded keeps its pool: pass its
        /// entries as the seed and only strings it did not have are appended. Without a seed the pool starts with
        /// the four entries every Windows-made database begins with: "Name", "Table", an empty entry and "Type".
        /// </summary>
        public StringPoolBuilder(IReadOnlyList<string?>? seed)
        {
            if (seed == null || IsEmptyPool(seed))
            {
                // A database with no tables yet has only the four standard entries (and the writer blanks them in the
                // empty file it starts with), so a new pool begins with the four entries every Windows-made database has.
                seed = new string?[] { null, "Name", "Table", null, "Type" };
            }
            for (int id = 1; id < seed.Count; id++)
            {
                string? entry = seed[id];
                _strings.Add(entry ?? "");
                _refs.Add(0);
                if (!string.IsNullOrEmpty(entry))
                {
                    _ids.TryAdd(entry, (uint) _strings.Count);
                }
            }
        }

        /// <summary>
        /// Gives a string its id without counting a reference to it. Windows Installer interns a table's name and its column
        /// names when the table is created, before any row is added; the references to them come later from _Tables and _Columns.
        /// </summary>
        /// <summary>True for the pool of a database with no tables: nothing in it has text (the writer blanks unused entries).</summary>
        public int IdCount => _strings.Count;

        private int _temporaries;

        private void Free(uint id)
        {
            _ids.Remove(_strings[(int) id - 1]);
            _strings[(int) id - 1] = "";
            _refs[(int) id - 1] = 0;
            _free.Push(id);
        }

        /// <summary>
        /// Models a stream put through a view on _Streams. The first one takes four strings (the view's query parts and the
        /// stream name) and gives them back, the third, the second, the fourth and then the first; a later one only the
        /// stream name. The ids freed are reused by the strings added next. Seen with one cabinet stream.
        /// </summary>
        public void StreamPut(bool firstView)
        {
            int count = firstView ? 4 : 1;
            var taken = new uint[count];
            for (int i = 0; i < count; i++)
                taken[i] = Allocate("\u0001temporary" + _temporaries++, 0);
            if (firstView)
            {
                Free(taken[2]);
                Free(taken[1]);
                Free(taken[3]);
                Free(taken[0]);
            }
            else
            {
                Free(taken[0]);
            }
        }

        /// <summary>Forgets the references counted so far, keeping ids, text and free ids; the tables are counted again.</summary>
        public void ResetReferences()
        {
            for (int i = 0; i < _refs.Count; i++)
                _refs[i] = 0;
        }

        public static bool IsEmptyPool(IReadOnlyList<string?> seed)
        {
            for (int i = 0; i < seed.Count; i++)
            {
                if (!string.IsNullOrEmpty(seed[i]))
                    return false;
            }
            return true;
        }

        public void Reserve(string text)
        {
            if (text.Length == 0 || _ids.ContainsKey(text))
                return;
            Allocate(text, 0);
        }

        public uint Add(string text)
        {
            if (text.Length == 0)
                return 0; // empty strings are stored as null, which MSI reads back as empty
            if (_ids.TryGetValue(text, out uint id))
            {
                _refs[(int) id - 1]++;
                return id;
            }
            return Allocate(text, 1);
        }

        // Ids released earlier are handed out again, the most recently released first; a pool with none appends.
        private readonly Stack<uint> _free = new();

        private uint Allocate(string text, int refs)
        {
            uint id;
            if (_free.Count > 0)
            {
                id = _free.Pop();
                _strings[(int) id - 1] = text;
                _refs[(int) id - 1] = refs;
            }
            else
            {
                _strings.Add(text);
                _refs.Add(refs);
                id = (uint) _strings.Count;
            }
            _ids[text] = id;
            return id;
        }

        /// <summary>
        /// Models importing a _SummaryInformation IDT into Windows Installer: the column names and then the values, in file
        /// order, get ids; then the values are released in property id order (a string goes once its last user is released),
        /// then the column names. Strings that were already in the pool stay. The ids left free are reused newest first.
        /// </summary>
        public void ImportTemporary(IReadOnlyList<(int Id, string Value)> rows)
        {
            var created = new HashSet<uint>();
            var uses = new Dictionary<uint, int>();
            uint Take(string text)
            {
                if (text.Length == 0)
                    return 0;
                if (!_ids.TryGetValue(text, out uint id))
                {
                    id = Allocate(text, 0);
                    created.Add(id);
                }
                uses[id] = (uses.TryGetValue(id, out int n) ? n : 0) + 1;
                return id;
            }
            void Release(uint id)
            {
                if (id == 0 || --uses[id] > 0 || !created.Contains(id))
                    return;
                Free(id);
            }

            uint propertyId = Take("PropertyId");
            uint value = Take("Value");
            var taken = new List<(int Id, uint Text)>(rows.Count);
            foreach (var row in rows)
                taken.Add((row.Id, Take(row.Value)));
            foreach (var row in taken.OrderBy(t => t.Id))
                Release(row.Text);
            Release(propertyId);
            Release(value);
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
                // msi.dll blanks the text of an entry nothing references any more but keeps its slot, so ids stay stable.
                byte[] bytes = _refs[i] == 0 ? Array.Empty<byte>() : encoding.GetBytes(_strings[i]);
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

/// <summary>
/// Something that took strings from Windows Installer's pool and gave them back while a database was built: the import
/// of a _SummaryInformation IDT, or a stream put through _Streams. AfterTable is the table imported last when it happened
/// (null for before any), so the pool can be allocated the way Windows Installer does.
/// </summary>
internal sealed class PoolEvent
{
    private readonly IReadOnlyList<(int Id, string Value)>? _summaryRows;
    private readonly bool _firstStreamView;

    private PoolEvent(string? afterTable, IReadOnlyList<(int Id, string Value)>? summaryRows, bool firstStreamView)
    {
        AfterTable = afterTable;
        _summaryRows = summaryRows;
        _firstStreamView = firstStreamView;
    }

    public string? AfterTable { get; }

    public bool IsStream => _summaryRows == null;

    public static PoolEvent ForSummary(string? afterTable, IReadOnlyList<(int Id, string Value)> rows) => new PoolEvent(afterTable, rows, false);

    public static PoolEvent ForStream(string? afterTable, bool firstView) => new PoolEvent(afterTable, null, firstView);

    internal void Apply(MsiWriter.StringPoolBuilder pool)
    {
        if (_summaryRows != null)
            pool.ImportTemporary(_summaryRows);
        else
            pool.StreamPut(_firstStreamView);
    }
}

/// <summary>The string pool a Direct database keeps between its writes (see <see cref="MsiWriter.WriteSources"/>).</summary>
internal sealed class StringPoolCarrier
{
    internal object? Builder;
    internal bool Fresh;
    internal int EventsDone;
}
