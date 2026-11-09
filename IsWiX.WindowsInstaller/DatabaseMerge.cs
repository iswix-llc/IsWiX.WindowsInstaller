namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    public partial class Database
    {
        /// <summary>
        /// Merges the tables of another database into this one, as MsiDatabaseMerge does. A table the other database
        /// has and this one does not is added. A row whose primary key is already in this database is left alone when it
        /// is identical and counted as a conflict when it is not. When <paramref name="errorTable"/> is given and there are
        /// conflicts, a table of that name lists the number of conflicts of each table. Tables that both databases have must
        /// have the same columns, or nothing is merged.
        /// </summary>
        internal MsiApi.MergeOutcome MergeFrom(Database source, string? errorTable)
        {
            this.RequireWritable();

            // Check every table both databases have before anything changes.
            var plan = new List<(MutableTable Source, MutableTable? Target)>();
            foreach (string name in source.AllTableNames().ToList())
            {
                MutableTable? from = source.GetUserTable(name);
                if (from == null)
                {
                    continue;
                }

                MutableTable? to = this.GetUserTable(name);
                if (to != null && !SameSchema(from, to))
                {
                    return MsiApi.MergeOutcome.SchemaMismatch;
                }

                plan.Add((from, to));
            }

            var conflicts = new List<KeyValuePair<string, int>>();
            foreach ((MutableTable from, MutableTable? existing) in plan)
            {
                MutableTable to;
                if (existing == null)
                {
                    to = new MutableTable(from.Name, from.Columns, new List<object?[]>());
                    this.CreateTable(to);
                }
                else
                {
                    to = existing;
                }

                var rowsByKey = new Dictionary<object?[], object?[]>(RowComparer.Instance);
                foreach (object?[] row in to.Rows)
                {
                    rowsByKey[to.KeyOf(row)] = row;
                }

                int shared = Math.Min(from.Columns.Count, to.Columns.Count);
                int conflictCount = 0;
                foreach (object?[] row in from.Rows)
                {
                    object?[] copy = CopyRow(from, row, to.Columns.Count);

                    if (rowsByKey.TryGetValue(to.KeyOf(copy), out object?[]? current))
                    {
                        if (!RowComparer.Instance.Equals(Shared(current, shared), Shared(copy, shared)))
                        {
                            conflictCount++;
                        }

                        continue;
                    }

                    // A merge module's modularized identifier can be longer than its column, so the width is not checked.
                    to.Insert(copy, checkStringWidth: false);
                    rowsByKey[to.KeyOf(copy)] = copy;
                }

                if (conflictCount > 0)
                {
                    conflicts.Add(new KeyValuePair<string, int>(from.Name, conflictCount));
                }
            }

            if (conflicts.Count > 0 && errorTable != null && this.GetUserTable(errorTable) == null)
            {
                var columns = new List<SqlColumn>
                {
                    new SqlColumn(errorTable, "Table", MsiColumnType.String, 255, false, true, false),
                    new SqlColumn(errorTable, "NumRowMergeConflicts", MsiColumnType.Integer, 2, false, false, false),
                };
                var table = new MutableTable(errorTable, columns, new List<object?[]>());
                this.CreateTable(table);
                foreach (KeyValuePair<string, int> conflict in conflicts)
                {
                    table.Insert(new object?[] { conflict.Key, conflict.Value });
                }
            }

            this.Changed();
            return conflicts.Count > 0 ? MsiApi.MergeOutcome.Conflict : MsiApi.MergeOutcome.Merged;
        }

        /// <summary>
        /// msi.dll compares only the columns both tables have: they must match in name, type, key and nullability.
        /// Extra columns in the source are dropped; extra columns in this database must allow null.
        /// </summary>
        private static object?[] Shared(object?[] row, int count)
        {
            return row.Length == count ? row : row.Take(count).ToArray();
        }

        private static bool SameSchema(MutableTable source, MutableTable target)
        {
            int shared = Math.Min(source.Columns.Count, target.Columns.Count);
            for (int i = 0; i < shared; i++)
            {
                SqlColumn a = source.Columns[i];
                SqlColumn b = target.Columns[i];
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) || a.Type != b.Type || a.IsPrimaryKey != b.IsPrimaryKey || a.IsNullable != b.IsNullable)
                {
                    return false;
                }
            }

            for (int i = shared; i < target.Columns.Count; i++)
            {
                if (!target.Columns[i].IsNullable)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A copy of a row of another database. Binary cells are read now, so the copy does not need that database later.</summary>
        private static object?[] CopyRow(MutableTable table, object?[] row, int width)
        {
            var copy = new object?[width];
            for (int i = 0; i < Math.Min(row.Length, width); i++)
            {
                copy[i] = table.Columns[i].Type == MsiColumnType.Binary ? LazyBinary.Resolve(row[i]) : row[i];
            }

            return copy;
        }
    }
}
