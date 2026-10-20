// Changes to the in-memory tables: INSERT, UPDATE and DELETE statements, and MsiViewModify on a
// single-table view. Every change is checked against the column definitions and the primary key before
// it is applied, so a failed statement leaves the table as it was.
namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using MsiReader;

    /// <summary>A table whose rows can be changed. Rows hold one value per column: a string, an int, a byte[] or null.</summary>
    internal sealed class MutableTable
    {
        private readonly int[] keyIndexes;
        private HashSet<object?[]>? keys;

        public MutableTable(string name, IReadOnlyList<SqlColumn> columns, List<object?[]> rows)
        {
            this.Name = name;
            this.Columns = columns;
            this.Rows = rows;
            this.keyIndexes = Enumerable.Range(0, columns.Count).Where(i => columns[i].IsPrimaryKey).ToArray();
            this.Snapshot = new SqlTable(name, columns, rows);
        }

        public string Name { get; }

        public IReadOnlyList<SqlColumn> Columns { get; }

        public List<object?[]> Rows { get; }

        /// <summary>The table as queries see it. It shares the live row list, so later changes are visible to it.</summary>
        public SqlTable Snapshot { get; }

        public int ColumnIndex(string name)
        {
            for (int i = 0; i < this.Columns.Count; i++)
            {
                if (string.Equals(this.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        public object?[] KeyOf(object?[] row) => this.keyIndexes.Select(i => row[i]).ToArray();

        /// <summary>The row index holding the given primary key, or -1.</summary>
        public int FindByKey(object?[] key)
        {
            if (key.Any(v => v == null))
            {
                return -1;
            }
            for (int i = 0; i < this.Rows.Count; i++)
            {
                if (RowComparer.Instance.Equals(this.KeyOf(this.Rows[i]), key))
                {
                    return i;
                }
            }
            return -1;
        }

        public void Insert(object?[] row)
        {
            this.Validate(row);
            this.EnsureKeys();
            if (!this.keys!.Add(this.KeyOf(row)))
            {
                throw Failed();
            }
            this.Rows.Add(row);
        }

        /// <summary>Inserts a row at the given index. The row is checked like <see cref="Insert"/>.</summary>
        public void InsertAt(int index, object?[] row)
        {
            this.Validate(row);
            this.EnsureKeys();
            if (!this.keys!.Add(this.KeyOf(row)))
            {
                throw Failed();
            }
            this.Rows.Insert(index, row);
        }

        /// <summary>Replaces rows at the given indexes. All rows are checked before any of them is stored.</summary>
        public void Update(IReadOnlyList<(int Index, object?[] Row)> changes)
        {
            foreach ((_, object?[] row) in changes)
            {
                this.Validate(row);
            }

            var changed = new HashSet<int>(changes.Select(c => c.Index));
            var taken = new HashSet<object?[]>(RowComparer.Instance);
            for (int i = 0; i < this.Rows.Count; i++)
            {
                if (!changed.Contains(i))
                {
                    taken.Add(this.KeyOf(this.Rows[i]));
                }
            }
            foreach ((_, object?[] row) in changes)
            {
                if (!taken.Add(this.KeyOf(row)))
                {
                    throw Failed();
                }
            }

            foreach ((int index, object?[] row) in changes)
            {
                this.Rows[index] = row;
            }
            this.keys = null;
        }

        /// <summary>Removes the rows that match the predicate. Returns how many were removed.</summary>
        public int RemoveWhere(Predicate<object?[]> match)
        {
            int removed = this.Rows.RemoveAll(match);
            if (removed > 0)
            {
                this.keys = null;
            }
            return removed;
        }

        public void RemoveAt(int index)
        {
            this.Rows.RemoveAt(index);
            this.keys = null;
        }

        private void EnsureKeys()
        {
            this.keys ??= new HashSet<object?[]>(this.Rows.Select(this.KeyOf), RowComparer.Instance);
        }

        /// <summary>Checks a full row against the column definitions.</summary>
        private void Validate(object?[] row)
        {
            for (int i = 0; i < this.Columns.Count; i++)
            {
                SqlColumn column = this.Columns[i];
                object? value = row[i];
                if (value == null)
                {
                    if (!column.IsNullable)
                    {
                        throw Failed();
                    }
                    continue;
                }

                switch (column.Type)
                {
                    case MsiColumnType.Integer:
                        if (value is not int number)
                        {
                            throw Mismatch();
                        }
                        // 16-bit values are stored with a 0x8000 bias, so -32768 is not representable.
                        if ((column.Size == 2 && (number < -32767 || number > 32767)) || number == int.MinValue)
                        {
                            throw Mismatch();
                        }
                        break;
                    case MsiColumnType.String:
                        if (value is not string text)
                        {
                            throw Mismatch();
                        }
                        if (column.Size > 0 && text.Length > column.Size)
                        {
                            throw Mismatch();
                        }
                        break;
                    case MsiColumnType.Binary:
                        if (value is not byte[])
                        {
                            throw Mismatch();
                        }
                        break;
                }
            }
        }

        public static MsiErrorException Failed() => new MsiErrorException((uint) NativeMethods.Error.FUNCTION_FAILED);

        public static MsiErrorException Mismatch() => new MsiErrorException((uint) NativeMethods.Error.DATATYPE_MISMATCH);
    }

    /// <summary>An INSERT, UPDATE or DELETE statement, checked against the schema and ready to run.</summary>
    internal sealed class BoundWrite
    {
        private readonly ManagedDatabase database;
        private readonly SqlStatement statement;
        private readonly MutableTable table;
        private readonly int[] insertColumns;
        private readonly List<(int Column, SqlOperand Value)> assignments;
        private readonly Func<object?[], object?[], bool>? where;

        private BoundWrite(ManagedDatabase database, SqlStatement statement, MutableTable table, int[] insertColumns,
            List<(int Column, SqlOperand Value)> assignments, Func<object?[], object?[], bool>? where)
        {
            this.database = database;
            this.statement = statement;
            this.table = table;
            this.insertColumns = insertColumns;
            this.assignments = assignments;
            this.where = where;
        }

        public int MarkerCount => this.statement.MarkerCount;

        public static BoundWrite Bind(SqlStatement statement, ManagedDatabase database)
        {
            string tableName = statement switch
            {
                SqlInsert insert => insert.Table,
                SqlUpdate update => update.Table,
                SqlDelete delete => delete.Table,
                _ => throw SqlErrors.Syntax(),
            };

            MutableTable table = database.GetUserTable(tableName)
                ?? throw (database.ContainsTable(tableName) ? MutableTable.Failed() : SqlErrors.Syntax());

            switch (statement)
            {
                case SqlInsert insert:
                {
                    var columns = insert.Columns.Select(name => ResolveColumn(table, name)).ToArray();
                    if (columns.Distinct().Count() != columns.Length)
                    {
                        throw SqlErrors.Syntax();
                    }
                    return new BoundWrite(database, statement, table, columns, new List<(int, SqlOperand)>(), null);
                }
                case SqlUpdate update:
                {
                    var assigned = update.Assignments
                        .Select(a => (Column: ResolveColumn(table, a.Column), a.Value))
                        .ToList();
                    if (assigned.Select(a => a.Column).Distinct().Count() != assigned.Count)
                    {
                        throw SqlErrors.Syntax();
                    }
                    var where = update.Where == null ? null : BoundSelect.BindTableCondition(update.Where, table.Snapshot);
                    return new BoundWrite(database, statement, table, Array.Empty<int>(), assigned, where);
                }
                case SqlDelete delete:
                {
                    var where = delete.Where == null ? null : BoundSelect.BindTableCondition(delete.Where, table.Snapshot);
                    return new BoundWrite(database, statement, table, Array.Empty<int>(), new List<(int, SqlOperand)>(), where);
                }
                default:
                    throw SqlErrors.Syntax();
            }
        }

        private static int ResolveColumn(MutableTable table, string name)
        {
            int index = table.ColumnIndex(name);
            return index >= 0 ? index : throw SqlErrors.Syntax();
        }

        public void Execute(object?[] markers)
        {
            if (markers.Length < this.MarkerCount)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INVALID_PARAMETER);
            }
            this.database.RequireWritable();
            object?[] parameters = markers.Take(this.MarkerCount).ToArray();

            switch (this.statement)
            {
                case SqlInsert insert:
                {
                    var row = new object?[this.table.Columns.Count];
                    for (int k = 0; k < insert.Values.Count; k++)
                    {
                        row[this.insertColumns[k]] = Evaluate(insert.Values[k], parameters);
                    }
                    this.table.Insert(row);
                    break;
                }
                case SqlUpdate:
                {
                    var values = this.assignments.Select(a => (a.Column, Value: Evaluate(a.Value, parameters))).ToList();
                    var changes = new List<(int Index, object?[] Row)>();
                    for (int i = 0; i < this.table.Rows.Count; i++)
                    {
                        object?[] current = this.table.Rows[i];
                        if (this.where != null && !this.where(current, parameters))
                        {
                            continue;
                        }
                        var updated = (object?[]) current.Clone();
                        foreach ((int column, object? value) in values)
                        {
                            updated[column] = value;
                        }
                        changes.Add((i, updated));
                    }
                    this.table.Update(changes);
                    break;
                }
                case SqlDelete:
                {
                    Func<object?[], object?[], bool>? where = this.where;
                    this.table.RemoveWhere(row => where == null || where(row, parameters));
                    break;
                }
            }

            this.database.Changed();
        }

        /// <summary>Evaluates a value operand: a literal, or a parameter marker taken from the record.</summary>
        internal static object? Evaluate(SqlOperand operand, object?[] markers)
        {
            switch (operand)
            {
                case SqlConstantOperand constant:
                    return constant.Value;
                case SqlMarkerOperand marker:
                    if (marker.Index >= markers.Length)
                    {
                        throw new MsiErrorException((uint) NativeMethods.Error.INVALID_PARAMETER);
                    }
                    return markers[marker.Index];
                default:
                    throw SqlErrors.Syntax();
            }
        }
    }

    /// <summary>MsiViewModify on a single-table view: inserts, updates, deletes, assigns or replaces one row.</summary>
    internal static class ViewModify
    {
        public static void Apply(ManagedDatabase database, BoundSelect query, int mode, object?[] values)
        {
            if (!query.IsSingleTableWithoutFilter)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.FUNCTION_FAILED);
            }
            database.RequireWritable();

            MutableTable table = database.GetUserTable(query.SingleTable.Name)
                ?? throw MutableTable.Failed();
            IReadOnlyList<SqlColumn> outputs = query.OutputColumns;
            if (values.Length != outputs.Count)
            {
                throw new MsiErrorException((uint) NativeMethods.Error.INVALID_PARAMETER);
            }

            // The record's fields, placed at their table positions. Columns the view does not name stay null.
            int[] positions = query.TableColumnIndexes;
            var supplied = new object?[table.Columns.Count];
            for (int j = 0; j < positions.Length; j++)
            {
                supplied[positions[j]] = values[j];
            }
            var named = positions.ToHashSet();

            int existing = table.FindByKey(table.KeyOf(supplied));
            switch (mode)
            {
                case (int) ViewModifyMode.Insert:
                    table.Insert(supplied);
                    break;

                case (int) ViewModifyMode.Update:
                    RequireExisting(existing);
                    table.Update(new[] { (existing, Merge(table.Rows[existing], supplied, named)) });
                    break;

                case (int) ViewModifyMode.Delete:
                    RequireExisting(existing);
                    table.RemoveAt(existing);
                    break;

                case (int) ViewModifyMode.Assign:
                    if (existing >= 0)
                    {
                        table.Update(new[] { (existing, Merge(table.Rows[existing], supplied, named)) });
                    }
                    else
                    {
                        table.Insert(supplied);
                    }
                    break;

                case (int) ViewModifyMode.Replace:
                    if (existing >= 0)
                    {
                        table.RemoveAt(existing);
                    }
                    table.Insert(supplied);
                    break;

                default:
                    throw new MsiErrorException((uint) NativeMethods.Error.CALL_NOT_IMPLEMENTED);
            }

            database.Changed();
        }

        private static void RequireExisting(int index)
        {
            if (index < 0)
            {
                throw MutableTable.Failed();
            }
        }

        /// <summary>The stored row with the record's fields written over it.</summary>
        private static object?[] Merge(object?[] stored, object?[] supplied, HashSet<int> named)
        {
            var merged = (object?[]) stored.Clone();
            for (int i = 0; i < supplied.Length; i++)
            {
                if (named.Contains(i))
                {
                    merged[i] = supplied[i];
                }
            }
            return merged;
        }
    }
}
