namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>An INSERT, UPDATE or DELETE statement, checked against the schema and ready to run.</summary>
    internal sealed class BoundWrite
    {
        private readonly Database database;
        private readonly SqlStatement statement;
        private readonly MutableTable table;
        private readonly int[] insertColumns;
        private readonly List<(int Column, SqlOperand Value)> assignments;
        private readonly Func<object?[], object?[], bool>? where;

        private BoundWrite(Database database, SqlStatement statement, MutableTable table, int[] insertColumns,
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

        public static BoundWrite Bind(SqlStatement statement, Database database)
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
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
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
                        throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
                    }
                    return markers[marker.Index];
                default:
                    throw SqlErrors.Syntax();
            }
        }
    }
}
