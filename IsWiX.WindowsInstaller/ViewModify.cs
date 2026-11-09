namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
    using IsWiX.WindowsInstaller.Format.Internal;

    /// <summary>MsiViewModify on a single-table view: inserts, updates, deletes, assigns or replaces one row.</summary>
    internal static class ViewModify
    {
        public static void Apply(Database database, BoundSelect query, int mode, object?[] values, object?[]? source = null)
        {
            // A view with a WHERE clause or ORDER BY can change the rows it fetched (Update, Delete), but new rows
            // cannot be put through it.
            bool changesFetchedRow = mode == (int) ViewModifyMode.Update || mode == (int) ViewModifyMode.Delete;
            if (!query.IsSingleTable || (!query.IsSingleTableWithoutFilter && !changesFetchedRow))
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            database.RequireWritable();

            if (query.SingleTable.Name == "_Streams")
            {
                ApplyToStreams(database, query, mode, values);
                return;
            }
            if (query.SingleTable.Name == "_Storages")
            {
                ApplyToStorages(database, query, mode, values);
                return;
            }
            MutableTable table = database.GetUserTable(query.SingleTable.Name)
                ?? throw MutableTable.Failed();
            IReadOnlyList<SqlColumn> outputs = query.OutputColumns;
            if (values.Length < outputs.Count)
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

            // Update and Delete act on the row the record was fetched from, even when the view leaves out its key.
            bool useSource = source != null && source.Length == table.Columns.Count
                && (mode == (int) ViewModifyMode.Update || mode == (int) ViewModifyMode.Delete);
            int existing = table.FindByKey(table.KeyOf(useSource ? source! : supplied));
            switch (mode)
            {
                case (int) ViewModifyMode.Insert:
                    table.Insert(supplied, database.EnforceStringWidth);
                    break;

                case (int) ViewModifyMode.Update:
                    RequireExisting(existing);
                    table.Update(new[] { (existing, Merge(table.Rows[existing], supplied, named)) }, database.EnforceStringWidth);
                    break;

                case (int) ViewModifyMode.Delete:
                    RequireExisting(existing);
                    table.RemoveAt(existing);
                    break;

                case (int) ViewModifyMode.Assign:
                    if (existing >= 0)
                    {
                        table.Update(new[] { (existing, Merge(table.Rows[existing], supplied, named)) }, database.EnforceStringWidth);
                    }
                    else
                    {
                        table.Insert(supplied, database.EnforceStringWidth);
                    }
                    break;

                case (int) ViewModifyMode.Replace:
                    if (existing >= 0)
                    {
                        table.RemoveAt(existing);
                    }
                    table.Insert(supplied, database.EnforceStringWidth);
                    break;

                case (int) ViewModifyMode.Merge:
                    // A new row is inserted. A row with the same key must already hold the record's values.
                    if (existing < 0)
                    {
                        table.Insert(supplied, database.EnforceStringWidth);
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

        /// <summary>Modifies the plain streams of the database through a view on _Streams (Name, Data).</summary>
        private static void ApplyToStreams(Database database, BoundSelect query, int mode, object?[] values)
        {
            if (values.Length < query.OutputColumns.Count || query.OutputColumns.Count < 1 || query.OutputColumns[0].Name != "Name")
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            string name = values[0] as string ?? throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            bool exists = database.ReadPlainStream(name) != null;
            byte[]? data = values.Length > 1 ? LazyBinary.Resolve(values[1]) as byte[] : null;
            switch (mode)
            {
                case (int) ViewModifyMode.Insert:
                case (int) ViewModifyMode.InsertTemporary:
                    if (exists || data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    database.PutPlainStream(name, data);
                    break;

                case (int) ViewModifyMode.Update:
                    if (!exists || data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    database.PutPlainStream(name, data);
                    break;

                case (int) ViewModifyMode.Assign:
                case (int) ViewModifyMode.Replace:
                    if (data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    database.PutPlainStream(name, data);
                    break;

                default:
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
        }

        /// <summary>
        /// Modifies the storages of the database through a view on _Storages (Name, Data). The data is a compound
        /// file, such as a transform: its streams become the streams of the storage.
        /// </summary>
        private static void ApplyToStorages(Database database, BoundSelect query, int mode, object?[] values)
        {
            if (values.Length < query.OutputColumns.Count || query.OutputColumns.Count < 1 || query.OutputColumns[0].Name != "Name")
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            string name = values[0] as string ?? throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            if (name.Length == 0 || name.Length > 31)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            bool exists = database.HasStorage(name);
            byte[]? data = values.Length > 1 ? LazyBinary.Resolve(values[1]) as byte[] : null;
            switch (mode)
            {
                case (int) ViewModifyMode.Delete:
                    if (!exists)
                    {
                        throw MutableTable.Failed();
                    }
                    database.RemoveStorage(name);
                    return;

                case (int) ViewModifyMode.Insert:
                case (int) ViewModifyMode.InsertTemporary:
                    if (exists || data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    break;

                case (int) ViewModifyMode.Update:
                    if (!exists || data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    break;

                case (int) ViewModifyMode.Assign:
                case (int) ViewModifyMode.Replace:
                    if (data == null)
                    {
                        throw MutableTable.Failed();
                    }
                    break;

                default:
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }

            using (var input = new System.IO.MemoryStream(data!, false))
            {
                CompoundFile file;
                try
                {
                    file = new CompoundFile(input);
                }
                catch (MsiFormatException)
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_DATA);
                }
                var streams = new List<KeyValuePair<string, byte[]>>();
                foreach (CompoundFile.DirectoryEntry entry in file.Streams)
                {
                    streams.Add(new KeyValuePair<string, byte[]>(entry.RawName, file.ReadStream(entry)));
                }
                database.PutStorage(name, file.RootClsid, streams);
            }
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
            if (values.Length < query.OutputColumns.Count)
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
            table.Validate(supplied, named, database.EnforceStringWidth);
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
