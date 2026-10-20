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
    internal sealed class ManagedDatabase : IDisposable
    {
        private readonly MsiDatabase reader;
        private readonly Dictionary<string, SqlTable> cache = new Dictionary<string, SqlTable>(StringComparer.Ordinal);

        private ManagedDatabase(MsiDatabase reader)
        {
            this.reader = reader;
        }

        public static ManagedDatabase Open(string path)
        {
            try
            {
                return new ManagedDatabase(MsiDatabase.Open(path));
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
            return this.reader.ContainsTable(name) || IsSystemTable(name);
        }

        /// <summary>Finds a table by name, including the system tables _Tables, _Columns and _Streams.</summary>
        public SqlTable? FindTable(string name)
        {
            if (this.cache.TryGetValue(name, out SqlTable? cached))
            {
                return cached;
            }

            SqlTable? table;
            switch (name)
            {
                case "_Tables":
                    table = this.BuildTablesTable();
                    break;
                case "_Columns":
                    table = this.BuildColumnsTable();
                    break;
                case "_Streams":
                    table = this.BuildStreamsTable();
                    break;
                default:
                    table = this.reader.TryGetTable(name, out MsiTable? msiTable) ? FromMsi(msiTable) : null;
                    break;
            }

            if (table != null)
            {
                this.cache[name] = table;
            }
            return table;
        }

        /// <summary>
        /// Writes a table to a file in Windows Installer text (IDT) format. The _ForceCodepage table holds
        /// only the database code page, and Database.CodePage reads it from the third line.
        /// </summary>
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

        private static SqlTable FromMsi(MsiTable table)
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

            return new SqlTable(table.Name, columns, rows);
        }

        private SqlTable BuildTablesTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Tables", "Name", MsiColumnType.String, 62, false, true, false),
            };
            var rows = new List<object?[]>();
            foreach (string name in this.reader.TableNames)
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
            foreach (string tableName in this.reader.TableNames)
            {
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

    /// <summary>A prepared SELECT statement bound to a database; executed with parameter values.</summary>
    internal sealed class ManagedView
    {
        public ManagedView(BoundSelect query)
        {
            this.Query = query;
        }

        public BoundSelect Query { get; }

        public SqlResult? Result { get; private set; }

        private int position;

        public void Execute(object?[] markers)
        {
            this.Result = this.Query.Execute(markers);
            this.position = 0;
        }

        /// <summary>Returns the next row as a record, or null when the rows are exhausted.</summary>
        public ManagedRecord? Fetch()
        {
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
    }
}
