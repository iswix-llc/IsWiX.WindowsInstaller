namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

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

        /// <summary>
        /// Checks a row against the column definitions. Only the columns in <paramref name="only"/> are checked when
        /// it is given; the others may be null.
        /// </summary>
        public void Validate(object?[] row, ISet<int>? only = null)
        {
            for (int i = 0; i < this.Columns.Count; i++)
            {
                if (only != null && !only.Contains(i))
                {
                    continue;
                }
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
                        if (value is not (byte[] or LazyBinary))
                        {
                            throw Mismatch();
                        }
                        break;
                }
            }
        }

        public static MsiErrorException Failed() => new MsiErrorException((uint) MsiError.FUNCTION_FAILED);

        public static MsiErrorException Mismatch() => new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
    }
}
