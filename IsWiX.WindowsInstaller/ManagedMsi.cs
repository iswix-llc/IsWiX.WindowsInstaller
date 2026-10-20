// Managed implementation of the Windows Installer handle model. Databases, views and records are
// kept in a process-wide table and referred to by the integer handles that NativeMethods returns.
namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using MsiReader;

    /// <summary>Carries a Windows Installer error code out of a managed operation.</summary>
    internal sealed class MsiErrorException : Exception
    {
        public MsiErrorException(uint code)
            : base("Windows Installer error " + code.ToString(CultureInfo.InvariantCulture))
        {
            Code = code;
        }

        public uint Code { get; }
    }

    internal static class ManagedHandles
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<int, object> Items = new Dictionary<int, object>();
        private static int nextHandle = 1;

        public static int Add(object item)
        {
            lock (SyncRoot)
            {
                int handle = nextHandle++;
                Items[handle] = item;
                return handle;
            }
        }

        public static T Get<T>(int handle) where T : class
        {
            lock (SyncRoot)
            {
                if (Items.TryGetValue(handle, out object? item) && item is T typed)
                {
                    return typed;
                }
            }
            throw new MsiErrorException((uint) NativeMethods.Error.INVALID_HANDLE);
        }

        public static void Close(int handle)
        {
            object? item;
            lock (SyncRoot)
            {
                if (!Items.TryGetValue(handle, out item))
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INVALID_HANDLE);
                }
                Items.Remove(handle);
            }
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>An open database, read from a file with MsiReader.</summary>
    /// <summary>MSIDBOPEN_READONLY, MSIDBOPEN_TRANSACT and MSIDBOPEN_DIRECT.</summary>
    internal enum DatabaseMode
    {
        ReadOnly = 0,

        /// <summary>Changes are kept in memory until Commit is called.</summary>
        Transact = 1,

        /// <summary>Each change is written to the file as soon as it is made.</summary>
        Direct = 2,
    }

    /// <summary>
    /// A database opened for reading, and for writing when it is not read-only. Tables are loaded into memory
    /// the first time they are used; Commit writes the whole database back through <see cref="MsiWriter"/>.
    /// </summary>
    internal sealed class ManagedDatabase : IDisposable
    {
        private readonly string path;
        private readonly Dictionary<string, MutableTable> tables = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> pendingStreams = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private MsiDatabase reader;
        private bool dirty;
        private readonly List<string> addedTables = new List<string>();

        private ManagedDatabase(MsiDatabase reader, string path, DatabaseMode mode)
        {
            this.reader = reader;
            this.path = path;
            this.Mode = mode;
        }

        public DatabaseMode Mode { get; }

        public static ManagedDatabase Open(string path)
        {
            return Open(path, DatabaseMode.ReadOnly);
        }

        public static ManagedDatabase Open(string path, DatabaseMode mode)
        {
            try
            {
                return new ManagedDatabase(MsiDatabase.Open(path), path, mode);
            }
            catch (FileNotFoundException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.FILE_NOT_FOUND);
            }
            catch (DirectoryNotFoundException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.PATH_NOT_FOUND);
            }
            catch (UnauthorizedAccessException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.ACCESS_DENIED);
            }
            catch (ArgumentException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INVALID_PARAMETER);
            }
            catch (IOException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.OPEN_FAILED);
            }
            catch (MsiFormatException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_PACKAGE_INVALID);
            }
        }

        public bool ContainsTable(string name)
        {
            return this.reader.ContainsTable(name)
                || this.addedTables.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))
                || IsSystemTable(name);
        }

        /// <summary>Finds a table by name, including the system tables _Tables, _Columns and _Streams.</summary>
        public SqlTable? FindTable(string name)
        {
            switch (name)
            {
                case "_Tables":
                    return this.BuildTablesTable();
                case "_Columns":
                    return this.BuildColumnsTable();
                case "_Streams":
                    return this.BuildStreamsTable();
                default:
                    return this.GetUserTable(name)?.Snapshot;
            }
        }

        /// <summary>The in-memory copy of a table in the database, or null if the database has no such table.</summary>
        public MutableTable? GetUserTable(string name)
        {
            if (this.tables.TryGetValue(name, out MutableTable? loaded))
            {
                return loaded;
            }
            if (!this.reader.TryGetTable(name, out MsiTable? msiTable))
            {
                return null;
            }
            MutableTable table = FromMsi(msiTable);
            this.tables[table.Name] = table;
            return table;
        }

        /// <summary>A plain stream's bytes, including any change not yet committed.</summary>
        public byte[]? ReadPlainStream(string name)
        {
            return this.pendingStreams.TryGetValue(name, out byte[]? pending) ? pending : this.reader.ReadStream(name);
        }

        /// <summary>Replaces a plain stream. The change is written on Commit, or straight away in Direct mode.</summary>
        public void PutPlainStream(string name, byte[] data)
        {
            this.RequireWritable();
            this.pendingStreams[name] = data;
            this.Changed();
        }

        public void RequireWritable()
        {
            if (this.Mode == DatabaseMode.ReadOnly)
            {
                throw MutableTable.Failed();
            }
        }

        /// <summary>Records a change. A Direct database writes it to the file straight away.</summary>
        public void Changed()
        {
            this.dirty = true;
            if (this.Mode == DatabaseMode.Direct)
            {
                this.Commit();
            }
        }

        /// <summary>
        /// Writes the tables and streams to a temporary file beside the database, then moves it over the original.
        /// Binary cells are written as streams named Table.key, as the format requires.
        /// </summary>
        public void Commit()
        {
            this.RequireWritable();
            if (!this.dirty)
            {
                return;
            }

            var writeTables = new List<MsiWriteTable>();
            var streams = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var binaryNames = new HashSet<string>(StringComparer.Ordinal);
            var binaryTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in this.AllTableNames())
            {
                MutableTable table = this.GetUserTable(name) ?? throw MutableTable.Failed();
                if (table.Columns.Any(c => c.Type == MsiColumnType.Binary))
                {
                    binaryTables.Add(table.Name);
                }
                var columns = table.Columns
                    .Select(c => new MsiWriteColumn(c.Name, c.Type, c.Size, c.IsNullable, c.IsPrimaryKey, c.IsLocalizable))
                    .ToList();

                var rows = new List<object?[]>(table.Rows.Count);
                foreach (object?[] row in table.Rows)
                {
                    var copy = (object?[]) row.Clone();
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        if (table.Columns[i].Type != MsiColumnType.Binary)
                        {
                            continue;
                        }
                        string? streamName = BinaryStreamName(table, row);
                        if (streamName != null)
                        {
                            binaryNames.Add(streamName);
                            if (row[i] is byte[] data)
                            {
                                streams[streamName] = data;
                            }
                        }
                        copy[i] = null;
                    }
                    rows.Add(copy);
                }
                writeTables.Add(new MsiWriteTable(table.Name, columns, rows));
            }

            // Plain streams are kept, except binary streams of tables with binary columns: those are owned by
            // the table rows, so a deleted row's stream must not come back.
            foreach (string name in this.reader.StreamNames)
            {
                int dot = name.IndexOf('.');
                if (binaryNames.Contains(name) || (dot > 0 && binaryTables.Contains(name.Substring(0, dot))))
                {
                    continue;
                }
                byte[]? data = this.ReadPlainStream(name);
                if (data != null)
                {
                    streams[name] = data;
                }
            }
            foreach (KeyValuePair<string, byte[]> pending in this.pendingStreams)
            {
                streams[pending.Key] = pending.Value;
            }

            int codePage = this.reader.CodePage;
            string directory = Path.GetDirectoryName(Path.GetFullPath(this.path)) ?? ".";
            string temp = Path.Combine(directory, "." + Path.GetFileName(this.path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool moved = false;
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    MsiWriter.Write(output, codePage, writeTables, streams);
                }
                this.reader.Dispose();
                File.Move(temp, this.path, overwrite: true);
                moved = true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                throw MutableTable.Failed();
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
                if (!moved)
                {
                    // The original file is untouched, so reopen it for further reads.
                    this.reader = MsiDatabase.Open(this.path);
                }
            }

            this.reader = MsiDatabase.Open(this.path);
            this.pendingStreams.Clear();
            this.dirty = false;
        }

        /// <summary>The stream holding a binary cell: "Table.key1.key2" from the primary-key values, or null if a key is null.</summary>
        private static string? BinaryStreamName(MutableTable table, object?[] row)
        {
            var parts = new List<string> { table.Name };
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (!table.Columns[i].IsPrimaryKey)
                {
                    continue;
                }
                object? key = row[i];
                if (key == null)
                {
                    return null;
                }
                parts.Add(key is int number ? number.ToString(CultureInfo.InvariantCulture) : (string) key);
            }
            return string.Join(".", parts);
        }

        /// <summary>
        /// Writes a table to a file in Windows Installer text (IDT) format. The _ForceCodepage table holds
        /// only the database code page, and Database.CodePage reads it from the third line.
        /// </summary>
        /// <summary>
        /// Applies a transform to the database. The transform is checked in full before any change is made,
        /// so a transform in an unsupported layout leaves the database as it was.
        /// </summary>
        public void ApplyTransform(string transformPath)
        {
            this.RequireWritable();
            MsiTransform transform;
            try
            {
                transform = MsiTransform.Open(transformPath);
            }
            catch (FileNotFoundException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.FILE_NOT_FOUND);
            }
            catch (MsiFormatException)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
            }

            using (transform)
            {
                Dictionary<string, MutableTable> newTables = PlanNewTables(transform);
                var changes = new List<Action>();
                foreach (MutableTable newTable in newTables.Values)
                {
                    MutableTable toAdd = newTable;
                    changes.Add(() => this.AddTable(toAdd));
                }

                foreach (KeyValuePair<string, byte[]> stream in transform.Tables)
                {
                    if (stream.Key == "_Tables" || stream.Key == "_Columns")
                    {
                        continue;
                    }
                    MutableTable table = this.GetUserTable(stream.Key)
                        ?? (newTables.TryGetValue(stream.Key, out MutableTable? defined) ? defined : null)
                        ?? throw new MsiErrorException((uint) NativeMethods.Error.INVALID_TABLE);
                    changes.AddRange(PlanRows(table, stream.Value, transform));
                }

                foreach (Action change in changes)
                {
                    change();
                }
                this.Changed();
            }
        }

        /// <summary>
        /// Reads the tables a transform defines, from its _Tables and _Columns streams. A table row is 2 words:
        /// the header 0x0101 and the table name. A column row is 5 words: the header 0x0401, the table name,
        /// the column number, the column name and the type word. Columns are kept in the order they appear.
        /// Other layouts are rejected.
        /// </summary>
        private Dictionary<string, MutableTable> PlanNewTables(MsiTransform transform)
        {
            var result = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
            byte[]? tablesStream = StreamNamed(transform, "_Tables");
            byte[]? columnsStream = StreamNamed(transform, "_Columns");
            if (tablesStream == null && columnsStream == null)
            {
                return result;
            }
            if (tablesStream == null || columnsStream == null || tablesStream.Length % 4 != 0 || columnsStream.Length % 10 != 0)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
            }

            var names = new List<string>();
            for (int position = 0; position < tablesStream.Length; position += 4)
            {
                if (ReadWord(tablesStream, position) != 0x0101)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
                }
                string name = transform.GetString((uint) ReadWord(tablesStream, position + 2))
                    ?? throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                if (this.ContainsTable(name) || names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                }
                names.Add(name);
            }

            var columns = names.ToDictionary(n => n, _ => new List<SqlColumn>(), StringComparer.OrdinalIgnoreCase);
            for (int position = 0; position < columnsStream.Length; position += 10)
            {
                if (ReadWord(columnsStream, position) != 0x0401)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
                }
                string tableName = transform.GetString((uint) ReadWord(columnsStream, position + 2))
                    ?? throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                string columnName = transform.GetString((uint) ReadWord(columnsStream, position + 6))
                    ?? throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                if (!columns.TryGetValue(tableName, out List<SqlColumn>? list))
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                }

                // Bit 0x8000 marks a column the transform adds. Only string columns are supported.
                int type = ReadWord(columnsStream, position + 8) & 0x7FFF;
                if ((type & 0x0800) == 0)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
                }
                list.Add(new SqlColumn(tableName, columnName, MsiColumnType.String, type & 0xFF,
                    (type & 0x1000) != 0, (type & 0x2000) != 0, (type & 0x0200) != 0));
            }

            foreach (string name in names)
            {
                List<SqlColumn> list = columns[name];
                if (list.Count == 0 || !list.Any(c => c.IsPrimaryKey))
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                }
                result[name] = new MutableTable(name, list, new List<object?[]>());
            }
            return result;
        }

        /// <summary>Adds a table defined by a transform. Runs only after the whole transform has been checked.</summary>
        private void AddTable(MutableTable table)
        {
            if (this.ContainsTable(table.Name))
            {
                throw MutableTable.Failed();
            }
            this.tables[table.Name] = table;
            this.addedTables.Add(table.Name);
        }

        /// <summary>The tables in the database: those read from the file, then those a transform added.</summary>
        private IEnumerable<string> AllTableNames()
        {
            return this.reader.TableNames.Concat(this.addedTables);
        }

        /// <summary>The column type word as the _Columns table stores it.</summary>
        private static int RawTypeOf(SqlColumn column)
        {
            int raw = 0x0100 | (column.Type == MsiColumnType.String ? 0x0C00 : 0) | (column.Size & 0xFF);
            if (column.IsLocalizable) raw |= 0x0200;
            if (column.IsNullable) raw |= 0x1000;
            if (column.IsPrimaryKey) raw |= 0x2000;
            return raw;
        }

        private static byte[]? StreamNamed(MsiTransform transform, string name)
        {
            foreach (KeyValuePair<string, byte[]> stream in transform.Tables)
            {
                if (stream.Key == name)
                {
                    return stream.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Plans the changes for one transform table stream. The stream is a sequence of rows of 2-byte words.
        /// A header of 0 deletes the row and is followed by its key. A header with bit 0x0200 inserts the row,
        /// and a header of 2 modifies the Value column of the row with that key. Insert and modify rows are
        /// followed by the key and the value. Only this layout is supported, because it is the one checked
        /// against Windows Installer output. Other layouts are rejected before the database changes.
        /// </summary>
        private static List<Action> PlanRows(MutableTable table, byte[] data, MsiTransform transform)
        {
            bool twoStringColumns = table.Columns.Count == 2
                && table.Columns[0].Type == MsiColumnType.String
                && table.Columns[1].Type == MsiColumnType.String;
            if (!twoStringColumns)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
            }

            var changes = new List<Action>();
            int position = 0;
            while (position < data.Length)
            {
                if (position + 2 > data.Length)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                }
                int header = ReadWord(data, position);

                if (header == 0)
                {
                    string? key = TransformString(data, position + 2, transform);
                    position += 4;
                    changes.Add(() =>
                    {
                        int index = table.FindByKey(new object?[] { key });
                        if (index < 0)
                        {
                            throw MutableTable.Failed();
                        }
                        table.RemoveAt(index);
                    });
                    continue;
                }

                if (position + 6 > data.Length)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.INSTALL_TRANSFORM_FAILURE);
                }
                string? rowKey = TransformString(data, position + 2, transform);
                string? value = TransformString(data, position + 4, transform);
                position += 6;

                if ((header & 0x0200) != 0)
                {
                    changes.Add(() => table.Insert(new object?[] { rowKey, value }));
                }
                else if (header == 2)
                {
                    changes.Add(() =>
                    {
                        int index = table.FindByKey(new object?[] { rowKey });
                        if (index < 0)
                        {
                            throw MutableTable.Failed();
                        }
                        object?[] row = (object?[]) table.Rows[index].Clone();
                        row[1] = value;
                        table.Update(new[] { (index, row) });
                    });
                }
                else
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
                }
            }
            return changes;
        }

        private static int ReadWord(byte[] data, int position)
        {
            return data[position] | (data[position + 1] << 8);
        }

        /// <summary>A key string from the transform's pool. A key cannot be null.</summary>
        private static string? TransformString(byte[] data, int position, MsiTransform transform)
        {
            return transform.GetString((uint) ReadWord(data, position));
        }

        public void Export(string table, string path)
        {
            string text;
            if (table == "_ForceCodepage")
            {
                text = "CodePage\r\ni2\r\n" + this.reader.CodePage.ToString(CultureInfo.InvariantCulture) + "\r\n";
            }
            else
            {
                SqlTable sqlTable = this.FindTable(table) ?? throw new MsiErrorException((uint) NativeMethods.Error.INVALID_TABLE);
                text = FormatIdt(sqlTable);
            }
            File.WriteAllText(path, text, System.Text.Encoding.UTF8);
        }

        private static string FormatIdt(SqlTable table)
        {
            var lines = new List<string>
            {
                string.Join("\t", table.Columns.Select(c => c.Name)),
                string.Join("\t", table.Columns.Select(c => c.TypeCode)),
                string.Join("\t", new[] { table.Name }.Concat(table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name))),
            };
            foreach (object?[] row in table.Rows)
            {
                lines.Add(string.Join("\t", row.Select(FormatIdtValue)));
            }
            return string.Join("\r\n", lines) + "\r\n";
        }

        private static string FormatIdtValue(object? value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case string text:
                    return text;
                default:
                    return string.Empty; // binary data is stored in separate files, not in the IDT text
            }
        }

        public void Dispose()
        {
            this.reader.Dispose();
        }

        private static bool IsSystemTable(string name)
        {
            return name == "_Tables" || name == "_Columns" || name == "_Streams";
        }

        private static MutableTable FromMsi(MsiTable table)
        {
            var columns = new List<SqlColumn>();
            foreach (MsiColumn column in table.Columns)
            {
                columns.Add(new SqlColumn(table.Name, column.Name, column.Type, column.Size,
                    column.IsNullable, column.IsPrimaryKey, column.IsLocalizable));
            }

            var rows = new List<object?[]>(table.Rows.Count);
            foreach (MsiRow row in table.Rows)
            {
                var values = new object?[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    values[i] = row[columns[i].Name];
                }
                rows.Add(values);
            }

            return new MutableTable(table.Name, columns, rows);
        }

        private SqlTable BuildTablesTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Tables", "Name", MsiColumnType.String, 62, false, true, false),
            };
            var rows = new List<object?[]>();
            foreach (string name in this.AllTableNames())
            {
                rows.Add(new object?[] { name });
            }
            return new SqlTable("_Tables", columns, rows);
        }

        private SqlTable BuildColumnsTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Columns", "Table", MsiColumnType.String, 62, false, true, false),
                new SqlColumn("_Columns", "Number", MsiColumnType.Integer, 2, false, true, false),
                new SqlColumn("_Columns", "Name", MsiColumnType.String, 64, false, true, false),
                new SqlColumn("_Columns", "Type", MsiColumnType.Integer, 2, false, false, false),
            };
            var rows = new List<object?[]>();
            foreach (string tableName in this.AllTableNames())
            {
                if (this.addedTables.Contains(tableName))
                {
                    MutableTable added = this.GetUserTable(tableName) ?? throw MutableTable.Failed();
                    for (int i = 0; i < added.Columns.Count; i++)
                    {
                        SqlColumn column = added.Columns[i];
                        rows.Add(new object?[] { added.Name, i + 1, column.Name, RawTypeOf(column) });
                    }
                    continue;
                }
                MsiTable table = this.reader.GetTable(tableName);
                foreach (MsiColumn column in table.Columns)
                {
                    rows.Add(new object?[] { table.Name, column.Number, column.Name, column.RawType });
                }
            }
            return new SqlTable("_Columns", columns, rows);
        }

        private SqlTable BuildStreamsTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Streams", "Name", MsiColumnType.String, 62, false, true, false),
                new SqlColumn("_Streams", "Data", MsiColumnType.Binary, 0, true, false, false),
            };
            var rows = new List<object?[]>();
            foreach (string name in this.reader.StreamNames)
            {
                rows.Add(new object?[] { name, this.reader.ReadStream(name) });
            }
            return new SqlTable("_Streams", columns, rows);
        }
    }

    /// <summary>A record: field 0 is the format template, fields 1..n hold values.</summary>
    internal sealed class ManagedRecord
    {
        private readonly object?[] fields;
        private readonly Dictionary<uint, int> readOffsets = new Dictionary<uint, int>();

        public ManagedRecord(int count)
        {
            this.fields = new object?[count + 1];
        }

        public ManagedRecord(IReadOnlyList<object?> values)
            : this(values.Count)
        {
            for (int i = 0; i < values.Count; i++)
            {
                this.fields[i + 1] = values[i];
            }
        }

        public int FieldCount => this.fields.Length - 1;

        public object? this[uint field]
        {
            get
            {
                this.CheckField(field);
                return this.fields[field];
            }
        }

        public void Set(uint field, object? value)
        {
            this.CheckField(field);
            this.fields[field] = value;
            this.readOffsets.Remove(field);
        }

        public void Clear()
        {
            for (int i = 0; i < this.fields.Length; i++)
            {
                this.fields[i] = null;
            }
            this.readOffsets.Clear();
        }

        public int ReadOffset(uint field)
        {
            return this.readOffsets.TryGetValue(field, out int offset) ? offset : 0;
        }

        public void SetReadOffset(uint field, int offset)
        {
            this.readOffsets[field] = offset;
        }

        /// <summary>Returns fields 1..n, the parameter values a query is executed with.</summary>
        public object?[] Values()
        {
            var values = new object?[this.FieldCount];
            Array.Copy(this.fields, 1, values, 0, this.FieldCount);
            return values;
        }

        public string GetString(uint field)
        {
            switch (this[field])
            {
                case null:
                    return string.Empty;
                case string text:
                    return text;
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                default:
                    throw new MsiErrorException((uint) NativeMethods.Error.DATATYPE_MISMATCH);
            }
        }

        public int GetInteger(uint field)
        {
            switch (this[field])
            {
                case int number:
                    return number;
                case string text when int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed):
                    return parsed;
                default:
                    return int.MinValue;
            }
        }

        private void CheckField(uint field)
        {
            if (field > this.FieldCount)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INVALID_PARAMETER);
            }
        }
    }

    /// <summary>
    /// A prepared statement. A SELECT returns rows from Fetch; an INSERT, UPDATE or DELETE changes the
    /// database when it is executed and returns no rows. Modify applies one record to a single-table view.
    /// </summary>
    internal sealed class ManagedView
    {
        private readonly ManagedDatabase database;
        private readonly BoundWrite? write;
        private bool executed;
        private int position;

        public ManagedView(ManagedDatabase database, BoundSelect query)
        {
            this.database = database;
            this.Query = query;
        }

        public ManagedView(ManagedDatabase database, BoundWrite write)
        {
            this.database = database;
            this.write = write;
        }

        /// <summary>The SELECT this view runs, or null for a write statement.</summary>
        public BoundSelect? Query { get; }

        public SqlResult? Result { get; private set; }

        public int MarkerCount => this.Query?.MarkerCount ?? this.write!.MarkerCount;

        public void Execute(object?[] markers)
        {
            if (this.write != null)
            {
                this.write.Execute(markers);
                this.executed = true;
                return;
            }
            this.Result = this.Query!.Execute(markers);
            this.position = 0;
        }

        /// <summary>Returns the next row as a record, or null when the rows are exhausted.</summary>
        public ManagedRecord? Fetch()
        {
            if (this.write != null)
            {
                if (!this.executed)
                {
                    throw new MsiErrorException((uint) NativeMethods.Error.FUNCTION_FAILED);
                }
                return null;
            }
            if (this.Result == null)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.FUNCTION_FAILED);
            }
            if (this.position >= this.Result.Rows.Count)
            {
                return null;
            }
            return new ManagedRecord(this.Result.Rows[this.position++]);
        }

        /// <summary>Applies one record to the view's table, as MsiViewModify does.</summary>
        public void Modify(int mode, object?[] values)
        {
            if (this.Query == null)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.FUNCTION_FAILED);
            }
            ViewModify.Apply(this.database, this.Query, mode, values);
        }
    }
}
