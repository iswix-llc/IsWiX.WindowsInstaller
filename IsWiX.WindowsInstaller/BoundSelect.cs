namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
    /// <summary>A SELECT bound to a database's tables, ready to execute with parameter values.</summary>
    internal sealed class BoundSelect
    {
        private readonly List<SqlTable> tables;
        private readonly int[]? projection;
        private readonly Func<object?[], object?[], bool>? filter;
        private readonly int[] orderKeys;
        private readonly bool distinct;
        private readonly List<SqlColumn> outputColumns;

        private BoundSelect(List<SqlTable> tables, int[]? projection, Func<object?[], object?[], bool>? filter,
            int[] orderKeys, bool distinct, List<SqlColumn> outputColumns, int markerCount)
        {
            this.tables = tables;
            this.projection = projection;
            this.filter = filter;
            this.orderKeys = orderKeys;
            this.distinct = distinct;
            this.outputColumns = outputColumns;
            this.MarkerCount = markerCount;
        }

        public int MarkerCount { get; }

        /// <summary>True for a query over one table with no WHERE, ORDER BY or DISTINCT. Such views can be modified.</summary>
        public bool IsSingleTableWithoutFilter =>
            this.tables.Count == 1 && this.filter == null && this.orderKeys.Length == 0 && !this.distinct;

        /// <summary>The one table of a single-table query.</summary>
        public SqlTable SingleTable => this.tables[0];

        /// <summary>For each output column, the index of its column in the table (single-table queries only).</summary>
        public int[] TableColumnIndexes => this.projection
            ?? Enumerable.Range(0, this.tables[0].Columns.Count).ToArray();

        /// <summary>Binds a WHERE condition over the columns of one table. The result takes a row and the markers.</summary>
        public static Func<object?[], object?[], bool> BindTableCondition(SqlCondition condition, SqlTable table)
        {
            int Resolve(SqlColumnRef reference)
            {
                int found = -1;
                int count = 0;
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    if (table.Columns[i].Name != reference.Name || (reference.Table != null && reference.Table != table.Name))
                    {
                        continue;
                    }
                    found = i;
                    count++;
                }
                if (count != 1)
                {
                    throw SqlErrors.Syntax();
                }
                return found;
            }
            return BindCondition(condition, Resolve);
        }

        /// <summary>The columns the query returns, known from the statement before it is executed.</summary>
        public IReadOnlyList<SqlColumn> OutputColumns => this.outputColumns;

        public static BoundSelect Bind(SqlSelect select, Database database)
        {
            var tables = new List<SqlTable>();
            foreach (string name in select.Tables)
            {
                if (tables.Any(t => t.Name == name))
                {
                    throw SqlErrors.Syntax();
                }
                tables.Add(database.FindTable(name) ?? throw SqlErrors.Syntax());
            }

            // Every column of every FROM table, in join order. Offsets index into a joined row.
            var combined = new List<(string Table, SqlColumn Column)>();
            foreach (SqlTable table in tables)
            {
                foreach (SqlColumn column in table.Columns)
                {
                    combined.Add((table.Name, column));
                }
            }

            int Resolve(SqlColumnRef reference)
            {
                int found = -1;
                int count = 0;
                for (int i = 0; i < combined.Count; i++)
                {
                    (string table, SqlColumn column) = combined[i];
                    if (column.Name != reference.Name || (reference.Table != null && table != reference.Table))
                    {
                        continue;
                    }
                    found = i;
                    count++;
                }
                if (count != 1)
                {
                    throw SqlErrors.Syntax();
                }
                return found;
            }

            int[]? projection = null;
            List<SqlColumn> outputColumns;
            if (select.Columns == null)
            {
                outputColumns = combined.Select(c => c.Column).ToList();
            }
            else
            {
                projection = select.Columns.Select(Resolve).ToArray();
                outputColumns = projection.Select(offset => combined[offset].Column).ToList();
            }

            Func<object?[], object?[], bool>? filter = select.Where == null
                ? null
                : BindCondition(select.Where, Resolve);

            int[] orderKeys = select.OrderBy.Select(Resolve).ToArray();

            return new BoundSelect(tables, projection, filter, orderKeys, select.Distinct, outputColumns, select.MarkerCount);
        }

        public SqlResult Execute(object?[] markers)
        {
            if (markers.Length < this.MarkerCount)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            object?[] parameters = markers.Take(this.MarkerCount).ToArray();

            IEnumerable<object?[]> rows = CrossJoin(this.tables);
            if (this.filter != null)
            {
                Func<object?[], object?[], bool> test = this.filter;
                rows = rows.Where(row => test(row, parameters));
            }

            List<object?[]> matched = rows.ToList();
            if (this.orderKeys.Length > 0)
            {
                int[] keys = this.orderKeys;
                matched = matched
                    .OrderBy(row => row, Comparer<object?[]>.Create((a, b) => CompareKeys(a, b, keys)))
                    .ToList();
            }

            IEnumerable<object?[]> output = matched;
            if (this.projection != null)
            {
                int[] columns = this.projection;
                output = matched.Select(row => columns.Select(offset => LazyBinary.Resolve(row[offset])).ToArray());
            }
            else
            {
                output = matched.Select(row => row.Select(LazyBinary.Resolve).ToArray());
            }
            if (this.distinct)
            {
                output = output.Distinct(RowComparer.Instance);
            }

            return new SqlResult(this.outputColumns, output.ToList());
        }

        private static IEnumerable<object?[]> CrossJoin(IReadOnlyList<SqlTable> tables)
        {
            IEnumerable<object?[]> joined = new[] { Array.Empty<object?>() };
            foreach (SqlTable table in tables)
            {
                SqlTable current = table;
                joined = joined.SelectMany(prefix => current.Rows.Select(row => prefix.Concat(row).ToArray()));
            }
            return joined;
        }

        private static int CompareKeys(object?[] a, object?[] b, int[] keys)
        {
            foreach (int key in keys)
            {
                int result = SqlValues.Order(a[key], b[key]);
                if (result != 0)
                {
                    return result;
                }
            }
            return 0;
        }

        private static Func<object?[], object?[], bool> BindCondition(SqlCondition condition, Func<SqlColumnRef, int> resolve)
        {
            switch (condition)
            {
                case SqlAndCondition andCondition:
                {
                    Func<object?[], object?[], bool> left = BindCondition(andCondition.Left, resolve);
                    Func<object?[], object?[], bool> right = BindCondition(andCondition.Right, resolve);
                    return (row, parameters) => left(row, parameters) && right(row, parameters);
                }
                case SqlOrCondition orCondition:
                {
                    Func<object?[], object?[], bool> left = BindCondition(orCondition.Left, resolve);
                    Func<object?[], object?[], bool> right = BindCondition(orCondition.Right, resolve);
                    return (row, parameters) => left(row, parameters) || right(row, parameters);
                }
                case SqlNullCondition nullCondition:
                {
                    int offset = resolve(nullCondition.Column);
                    bool negated = nullCondition.Negated;
                    return (row, parameters) => (row[offset] == null) != negated;
                }
                case SqlCompareCondition compare:
                {
                    Func<object?[], object?[], object?> left = BindOperand(compare.Left, resolve);
                    Func<object?[], object?[], object?> right = BindOperand(compare.Right, resolve);
                    SqlOperator op = compare.Operator;
                    return (row, parameters) => SqlValues.Compare(op, left(row, parameters), right(row, parameters));
                }
                default:
                    throw SqlErrors.Syntax();
            }
        }

        private static Func<object?[], object?[], object?> BindOperand(SqlOperand operand, Func<SqlColumnRef, int> resolve)
        {
            switch (operand)
            {
                case SqlColumnOperand column:
                {
                    int offset = resolve(column.Reference);
                    return (row, parameters) => row[offset];
                }
                case SqlConstantOperand constant:
                {
                    object? value = constant.Value;
                    return (row, parameters) => value;
                }
                case SqlMarkerOperand marker:
                {
                    int index = marker.Index;
                    return (row, parameters) => parameters[index];
                }
                default:
                    throw SqlErrors.Syntax();
            }
        }
    }

}
