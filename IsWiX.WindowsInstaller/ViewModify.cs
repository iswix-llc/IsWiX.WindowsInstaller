namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>MsiViewModify on a single-table view: inserts, updates, deletes, assigns or replaces one row.</summary>
    internal static class ViewModify
    {
        public static void Apply(Database database, BoundSelect query, int mode, object?[] values)
        {
            if (!query.IsSingleTableWithoutFilter)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            database.RequireWritable();

            MutableTable table = database.GetUserTable(query.SingleTable.Name)
                ?? throw MutableTable.Failed();
            IReadOnlyList<SqlColumn> outputs = query.OutputColumns;
            if (values.Length != outputs.Count)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
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

                case (int) ViewModifyMode.Merge:
                    // A new row is inserted. A row with the same key must already hold the record's values.
                    if (existing < 0)
                    {
                        table.Insert(supplied);
                    }
                    else
                    {
                        object?[] stored = table.Rows[existing];
                        foreach (int i in named)
                        {
                            if (!SameValue(stored[i], supplied[i]))
                            {
                                throw MutableTable.Failed();
                            }
                        }
                    }
                    break;

                default:
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }

            database.Changed();
        }

        /// <summary>The table, and the record's values placed at their table positions, for a single-table view.</summary>
        private static (MutableTable Table, object?[] Supplied, HashSet<int> Named, int[] Positions) Prepare(
            Database database, BoundSelect query, object?[] values)
        {
            if (!query.IsSingleTableWithoutFilter)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            MutableTable table = database.GetUserTable(query.SingleTable.Name)
                ?? throw MutableTable.Failed();
            if (values.Length != query.OutputColumns.Count)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            int[] positions = query.TableColumnIndexes;
            var supplied = new object?[table.Columns.Count];
            for (int j = 0; j < positions.Length; j++)
            {
                supplied[positions[j]] = values[j];
            }
            return (table, supplied, positions.ToHashSet(), positions);
        }

        /// <summary>Refresh and Seek: the view's values for the row with the record's key.</summary>
        public static object?[] Lookup(Database database, BoundSelect query, object?[] values)
        {
            (MutableTable table, object?[] supplied, _, int[] positions) = Prepare(database, query, values);
            int existing = table.FindByKey(table.KeyOf(supplied));
            RequireExisting(existing);
            object?[] stored = table.Rows[existing];
            var result = new object?[positions.Length];
            for (int j = 0; j < positions.Length; j++)
            {
                result[j] = LazyBinary.Resolve(stored[positions[j]]);
            }
            return result;
        }

        /// <summary>The Validate modes: checks the record against the table without changing it.</summary>
        public static void Check(Database database, BoundSelect query, int mode, object?[] values)
        {
            (MutableTable table, object?[] supplied, HashSet<int> named, _) = Prepare(database, query, values);
            table.Validate(supplied, named);
            int existing = table.FindByKey(table.KeyOf(supplied));
            switch (mode)
            {
                case (int) ViewModifyMode.ValidateNew:
                    if (existing >= 0)
                    {
                        throw MutableTable.Failed();
                    }
                    break;

                case (int) ViewModifyMode.ValidateDelete:
                    RequireExisting(existing);
                    break;
            }
        }

        private static bool SameValue(object? a, object? b)
        {
            a = LazyBinary.Resolve(a);
            b = LazyBinary.Resolve(b);
            if (a is byte[] left && b is byte[] right)
            {
                return left.AsSpan().SequenceEqual(right);
            }
            return Equals(a, b);
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
