namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using IsWiX.WindowsInstaller.Format;
    using IsWiX.WindowsInstaller.Format.Internal;

    /// <summary>
    /// Writes transform (.mst) files. A transform is a compound file with its own string pool and one stream per
    /// changed table. Each stream is a list of rows made of 16-bit words: a header word, then the cells.
    /// A header of 0 deletes the row (its key cells follow); a header with bit 0 inserts a row (all cells follow,
    /// and the high byte is the column count); any other header updates the row with that key, bit i marking
    /// column i, and the changed cells follow the key. Strings are pool ids, 16-bit integers are biased by 0x8000
    /// and 32-bit integers by 0x80000000; 0 is null. Tables are added and dropped through rows in the transform's
    /// _Tables stream, and columns through rows in its _Columns stream.
    /// </summary>
    internal static class TransformGenerator
    {
        private static readonly byte[] TransformClsid = { 0x82, 0x10, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0, 0, 0, 0, 0, 0, 0x46 };

        private const int AddedColumnFlag = 0x8000;

        /// <summary>
        /// Writes the transform that turns <paramref name="reference"/> into <paramref name="updated"/>.
        /// Returns false, and writes no file, when the two databases do not differ.
        /// </summary>
        public static bool Generate(Database updated, Database reference, string path)
        {
            var pool = new Pool();
            var tablesRows = new MemoryStream();
            var columnsRows = new MemoryStream();
            var tableStreams = new List<KeyValuePair<string, byte[]>>();

            var updatedTables = new List<MutableTable>();
            foreach (string name in updated.TransformTableNames())
            {
                updatedTables.Add(updated.GetUserTable(name) ?? throw NotSupported());
            }
            var referenceTables = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in reference.TransformTableNames())
            {
                referenceTables[name] = reference.GetUserTable(name) ?? throw NotSupported();
            }
            var updatedNames = new HashSet<string>(updatedTables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            // The strings get their ids in the order they are written: _Tables, then _Columns, then the table streams.
            foreach (MutableTable table in updatedTables)
            {
                if (!referenceTables.ContainsKey(table.Name))
                {
                    Word(tablesRows, 0x0101);
                    Word(tablesRows, pool.Id(table.Name));
                }
            }
            foreach (MutableTable table in referenceTables.Values)
            {
                if (!updatedNames.Contains(table.Name))
                {
                    Word(tablesRows, 0);
                    Word(tablesRows, pool.Id(table.Name));
                }
            }

            foreach (MutableTable table in updatedTables)
            {
                if (!referenceTables.TryGetValue(table.Name, out MutableTable? before))
                {
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        WriteColumnRow(columnsRows, pool, table.Name, 0, table.Columns[i]);
                    }
                    continue;
                }

                if (before.Columns.Count > table.Columns.Count)
                {
                    throw NotSupported();
                }
                for (int i = 0; i < before.Columns.Count; i++)
                {
                    if (!SameColumn(before.Columns[i], table.Columns[i]))
                    {
                        throw NotSupported();
                    }
                }
                for (int i = before.Columns.Count; i < table.Columns.Count; i++)
                {
                    if (table.Columns[i].IsPrimaryKey)
                    {
                        throw NotSupported();
                    }
                    WriteColumnRow(columnsRows, pool, table.Name, 0x8000 + i + 1, table.Columns[i]);
                }
            }

            foreach (MutableTable table in updatedTables)
            {
                referenceTables.TryGetValue(table.Name, out MutableTable? before);
                byte[] rows = BuildRows(table, before, pool);
                if (rows.Length > 0)
                {
                    tableStreams.Add(new KeyValuePair<string, byte[]>(table.Name, rows));
                }
            }

            if (tablesRows.Length == 0 && columnsRows.Length == 0 && tableStreams.Count == 0)
            {
                return false;
            }

            PooledStrings pooled = pool.Build(updated.ActiveCodePage);
            var streams = new List<(string Name, long Length, Func<byte[]> Read)>
            {
                Entry("_StringPool", pooled.Pool),
                Entry("_StringData", pooled.Data),
            };
            if (tablesRows.Length > 0)
            {
                streams.Add(Entry("_Tables", tablesRows.ToArray()));
            }
            if (columnsRows.Length > 0)
            {
                streams.Add(Entry("_Columns", columnsRows.ToArray()));
            }
            foreach (KeyValuePair<string, byte[]> stream in tableStreams)
            {
                streams.Add(Entry(stream.Key, stream.Value));
            }

            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                CompoundFileWriter.Write(output, streams, TransformClsid);
            }
            return true;
        }

        /// <summary>Adds the summary information a transform carries: where it applies and what it expects.</summary>
        public static void WriteSummary(Database updated, Database reference, string path, int errors, int validations)
        {
            if (!File.Exists(path))
            {
                throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
            }
            SortedDictionary<uint, SummaryValue> after = ReadSummary(updated);
            SortedDictionary<uint, SummaryValue> before = ReadSummary(reference);

            var properties = new SortedDictionary<uint, SummaryValue>();
            properties[1] = after.TryGetValue(1, out SummaryValue? codePage) ? codePage : new SummaryValue(SummaryStream.VtI2, number: 1252);
            foreach (uint id in new uint[] { 2, 3, 4, 5, 6 })
            {
                if (after.TryGetValue(id, out SummaryValue? text) && text.Type == SummaryStream.VtLpstr)
                {
                    properties[id] = text;
                }
            }
            properties[7] = Text(before, 7);
            properties[8] = Text(after, 7);
            properties[9] = new SummaryValue(SummaryStream.VtLpstr, text:
                Property(reference, "ProductCode") + Property(reference, "ProductVersion") + ";" +
                Property(updated, "ProductCode") + Property(updated, "ProductVersion") + ";" +
                Property(updated, "UpgradeCode"));
            if (after.TryGetValue(12, out SummaryValue? created))
            {
                properties[12] = created;
            }
            if (after.TryGetValue(14, out SummaryValue? pages))
            {
                properties[14] = pages;
            }
            properties[16] = new SummaryValue(SummaryStream.VtI4, number: unchecked((int) (((uint) validations << 16) | ((uint) errors & 0xFFFF))));
            if (after.TryGetValue(18, out SummaryValue? application))
            {
                properties[18] = application;
            }
            properties[19] = after.TryGetValue(19, out SummaryValue? security) ? security : new SummaryValue(SummaryStream.VtI4, number: 2);

            var streams = new List<(string Name, long Length, Func<byte[]> Read)>();
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var file = new CompoundFile(input);
                foreach (CompoundFile.DirectoryEntry entry in file.Streams)
                {
                    if (entry.RawName == SummaryStream.Name)
                    {
                        continue;
                    }
                    byte[] bytes = file.ReadStream(entry);
                    streams.Add((entry.RawName, bytes.Length, () => bytes));
                }
            }
            byte[] summary = SummaryStream.Write(properties);
            streams.Add((SummaryStream.Name, summary.Length, () => summary));

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    CompoundFileWriter.Write(output, streams, TransformClsid);
                }
                File.Delete(path);
                File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        private static SummaryValue Text(SortedDictionary<uint, SummaryValue> values, uint id)
        {
            return new SummaryValue(SummaryStream.VtLpstr, text: values.TryGetValue(id, out SummaryValue? value) ? value.Text ?? string.Empty : string.Empty);
        }

        private static SortedDictionary<uint, SummaryValue> ReadSummary(Database database)
        {
            byte[]? data = database.ReadPlainStream(SummaryStream.Name);
            return data == null ? new SortedDictionary<uint, SummaryValue>() : SummaryStream.Read(data);
        }

        private static string Property(Database database, string name)
        {
            MutableTable? table = database.GetUserTable("Property");
            if (table == null)
            {
                return string.Empty;
            }
            foreach (object?[] row in table.Rows)
            {
                if (row.Length > 1 && row[0] is string key && key == name)
                {
                    return row[1] as string ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private static void WriteColumnRow(MemoryStream stream, Pool pool, string table, int number, SqlColumn column)
        {
            Word(stream, 0x0401);
            Word(stream, pool.Id(table));
            Word(stream, number);
            Word(stream, pool.Id(column.Name));
            Word(stream, TypeWord(column) | AddedColumnFlag);
        }

        /// <summary>The raw type word stored in _Columns.</summary>
        private static int TypeWord(SqlColumn column)
        {
            int flags = 0x0100 | (column.IsNullable ? 0x1000 : 0) | (column.IsPrimaryKey ? 0x2000 : 0) | (column.IsLocalizable ? 0x0200 : 0);
            switch (column.Type)
            {
                case MsiColumnType.String:
                    return 0x0800 | 0x0400 | flags | (column.Size & 0xFF);
                case MsiColumnType.Binary:
                    return 0x0900 | 0x0100 | (column.IsNullable ? 0x1000 : 0);
                default:
                    int size = column.Size == 2 ? 2 : 4;
                    return flags | size | (size == 2 ? 0x0400 : 0);
            }
        }

        private static bool SameColumn(SqlColumn a, SqlColumn b)
        {
            return string.Equals(a.Name, b.Name, StringComparison.Ordinal) && a.Type == b.Type && a.Size == b.Size
                && a.IsNullable == b.IsNullable && a.IsPrimaryKey == b.IsPrimaryKey && a.IsLocalizable == b.IsLocalizable;
        }

        /// <summary>The rows of one table's stream: updates and inserts in the new table's order, then deletes.</summary>
        private static byte[] BuildRows(MutableTable table, MutableTable? before, Pool pool)
        {
            var stream = new MemoryStream();
            var beforeIndex = new Dictionary<object?[], int>(RowComparer.Instance);
            var afterIndex = new Dictionary<object?[], int>(RowComparer.Instance);
            if (before != null)
            {
                for (int i = 0; i < before.Rows.Count; i++)
                {
                    beforeIndex[before.KeyOf(before.Rows[i])] = i;
                }
            }
            for (int i = 0; i < table.Rows.Count; i++)
            {
                afterIndex[table.KeyOf(table.Rows[i])] = i;
            }
            var keyColumns = Enumerable.Range(0, table.Columns.Count).Where(i => table.Columns[i].IsPrimaryKey).ToArray();

            foreach (object?[] row in table.Rows)
            {
                if (before != null && beforeIndex.TryGetValue(table.KeyOf(row), out int index))
                {
                    object?[] old = before.Rows[index];
                    int mask = 0;
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        if (table.Columns[i].IsPrimaryKey)
                        {
                            continue;
                        }
                        object? was = i < old.Length && i < before.Columns.Count ? old[i] : null;
                        if (SameValue(was, row[i]))
                        {
                            continue;
                        }
                        if (i > 15 || table.Columns[i].Type == MsiColumnType.Binary)
                        {
                            throw NotSupported();
                        }
                        mask |= 1 << i;
                    }
                    if (mask == 0)
                    {
                        continue;
                    }
                    Word(stream, mask);
                    foreach (int k in keyColumns)
                    {
                        WriteCell(stream, pool, table.Columns[k], row[k]);
                    }
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            WriteCell(stream, pool, table.Columns[i], row[i]);
                        }
                    }
                }
                else
                {
                    if (table.Columns.Count > 255)
                    {
                        throw NotSupported();
                    }
                    Word(stream, 1 | (table.Columns.Count << 8));
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        WriteCell(stream, pool, table.Columns[i], row[i]);
                    }
                }
            }

            if (before != null)
            {
                foreach (object?[] old in before.Rows)
                {
                    if (afterIndex.ContainsKey(before.KeyOf(old)))
                    {
                        continue;
                    }
                    Word(stream, 0);
                    foreach (int k in keyColumns)
                    {
                        WriteCell(stream, pool, before.Columns[k], old[k]);
                    }
                }
            }
            return stream.ToArray();
        }

        private static void WriteCell(MemoryStream stream, Pool pool, SqlColumn column, object? value)
        {
            value = LazyBinary.Resolve(value);
            switch (column.Type)
            {
                case MsiColumnType.String:
                    Word(stream, value is string text && text.Length > 0 ? pool.Id(text) : 0);
                    break;
                case MsiColumnType.Integer:
                    if (column.Size == 2 || column.Size == 0)
                    {
                        Word(stream, value == null ? 0 : unchecked((int) ((uint) Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) + 0x8000u)) & 0xFFFF);
                    }
                    else
                    {
                        uint biased = value == null ? 0u : unchecked((uint) Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) + 0x80000000u);
                        Word(stream, (int) (biased & 0xFFFF));
                        Word(stream, (int) (biased >> 16));
                    }
                    break;
                default:
                    // The stream behind a binary cell travels in its own transform stream, which is not written yet.
                    if (value != null)
                    {
                        throw NotSupported();
                    }
                    Word(stream, 0);
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
            if (a is string sa && sa.Length == 0)
            {
                a = null;
            }
            if (b is string sb && sb.Length == 0)
            {
                b = null;
            }
            return Equals(a, b);
        }

        private static void Word(MemoryStream stream, int value)
        {
            stream.WriteByte((byte) value);
            stream.WriteByte((byte) (value >> 8));
        }

        private static (string Name, long Length, Func<byte[]> Read) Entry(string table, byte[] data)
        {
            return (StreamNameCodec.TablePrefix + StreamNameCodec.Encode(table), data.Length, () => data);
        }

        private static MsiErrorException NotSupported()
        {
            return new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        private sealed record PooledStrings(byte[] Pool, byte[] Data);

        /// <summary>The transform's own string pool. Ids start at 1 and follow first use.</summary>
        private sealed class Pool
        {
            private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<string> strings = new List<string>();
            private readonly List<int> references = new List<int>();

            public int Id(string text)
            {
                if (this.ids.TryGetValue(text, out int id))
                {
                    this.references[id - 1]++;
                    return id;
                }
                this.strings.Add(text);
                this.references.Add(1);
                id = this.strings.Count;
                this.ids[text] = id;
                if (id > 0xFFFF)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                return id;
            }

            public PooledStrings Build(int codePage)
            {
                int cp = codePage == 0 ? 1252 : codePage;
                Encoding encoding;
                if (cp == 65001)
                {
                    encoding = Encoding.UTF8;
                }
                else if (cp == 1200)
                {
                    encoding = Encoding.Unicode;
                }
                else
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    encoding = Encoding.GetEncoding(cp);
                }

                var pool = new MemoryStream();
                var data = new MemoryStream();
                Word(pool, codePage & 0xFFFF);
                Word(pool, (codePage >> 16) & 0x7FFF);
                for (int i = 0; i < this.strings.Count; i++)
                {
                    byte[] bytes = encoding.GetBytes(this.strings[i]);
                    if (bytes.Length > 0xFFFF)
                    {
                        throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                    }
                    Word(pool, bytes.Length);
                    Word(pool, Math.Min(this.references[i], 0xFFFF));
                    data.Write(bytes, 0, bytes.Length);
                }
                return new PooledStrings(pool.ToArray(), data.ToArray());
            }
        }
    }
}
