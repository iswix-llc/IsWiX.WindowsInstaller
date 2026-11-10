#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Text;
using IsWiX.WindowsInstaller.Format;

// Captures how the real msi.dll hands out string ids, as ground truth for the managed MsiWriter. Windows only.
//
//   dotnet run --project StringPoolTruth -c Release -- <report file>
//       Runs a fixed set of small experiments through msi.dll (SQL and IDT import) and writes the string pool after each.
//
//   dotnet run --project StringPoolTruth -c Release -- <report file> --replay <file.msi>...
//       For each MSI, exports its tables with msi.dll, imports them one IDT per table into a new database (the way
//       WiX builds a database) in several table orders, and says which order, if any, gives the same string pool.
//
// The pool is read back with the managed reader (the library makes its internals visible to this project).

internal static class Program
{
    private const uint ReadOnly = 0;
    private const uint CreateDirect = 4;

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewClose(IntPtr view);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseImportW(IntPtr database, string folder, string file);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseExportW(IntPtr database, string table, string folder, string file);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiDatabaseCommit(IntPtr database);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern IntPtr MsiCreateRecord(uint parameters);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordSetStringW(IntPtr record, uint field, string value);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiRecordSetInteger(IntPtr record, uint field, int value);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewModify(IntPtr view, int mode, IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordSetStreamW(IntPtr record, uint field, string filePath);

    private sealed class NativeDatabase : IDisposable
    {
        private IntPtr _handle;

        public NativeDatabase(string path, uint mode)
        {
            Check(MsiOpenDatabaseW(path, (IntPtr) mode, out _handle), "open " + path);
        }

        public void Sql(string query)
        {
            Check(MsiDatabaseOpenViewW(_handle, query, out IntPtr view), query);
            try
            {
                Check(MsiViewExecute(view, IntPtr.Zero), query);
                MsiViewClose(view);
            }
            finally
            {
                MsiCloseHandle(view);
            }
        }

        // Adds a stream the way WiX does for a cabinet: a view over _Streams and an Assign of a record holding the data.
        public void AssignStream(string name, string dataFile)
        {
            const string query = "SELECT `Name`, `Data` FROM `_Streams`";
            Check(MsiDatabaseOpenViewW(_handle, query, out IntPtr view), query);
            IntPtr record = IntPtr.Zero;
            try
            {
                Check(MsiViewExecute(view, IntPtr.Zero), query);
                record = MsiCreateRecord(2);
                Check(MsiRecordSetStringW(record, 1, name), "stream name");
                Check(MsiRecordSetStreamW(record, 2, dataFile), "stream data");
                Check(MsiViewModify(view, 3, record), "assign stream");
                MsiViewClose(view);
            }
            finally
            {
                if (record != IntPtr.Zero)
                {
                    MsiCloseHandle(record);
                }

                MsiCloseHandle(view);
            }
        }

        // Fills the File table the way WiX does: a view over the table and an Assign of a record.
        public void AssignFileRow()
        {
            const string query = "SELECT * FROM `File`";
            Check(MsiDatabaseOpenViewW(_handle, query, out IntPtr view), query);
            IntPtr record = IntPtr.Zero;
            try
            {
                Check(MsiViewExecute(view, IntPtr.Zero), query);
                record = MsiCreateRecord(8);
                Check(MsiRecordSetStringW(record, 1, "HelloFile"), "field 1");
                Check(MsiRecordSetStringW(record, 2, "HelloComponent"), "field 2");
                Check(MsiRecordSetStringW(record, 3, "hello.txt"), "field 3");
                Check(MsiRecordSetInteger(record, 4, 5), "field 4");
                Check(MsiRecordSetInteger(record, 7, 512), "field 7");
                Check(MsiRecordSetInteger(record, 8, 1), "field 8");
                Check(MsiViewModify(view, 3, record), "assign File row");
                MsiViewClose(view);
            }
            finally
            {
                if (record != IntPtr.Zero)
                {
                    MsiCloseHandle(record);
                }

                MsiCloseHandle(view);
            }
        }

        public void Import(string folder, string file) => Check(MsiDatabaseImportW(_handle, folder, file), "import " + file);

        public void Export(string table, string folder, string file) => Check(MsiDatabaseExportW(_handle, table, folder, file), "export " + table);

        public void Commit() => Check(MsiDatabaseCommit(_handle), "commit");

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                MsiCloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private static void Check(uint code, string what)
        {
            if (code != 0)
            {
                throw new InvalidOperationException("msi.dll error " + code + " in " + what);
            }
        }
    }

    // An IDT file: names, types, then "table <tab> key columns", then the rows.
    private static void WriteIdt(string folder, string table, string[] columns, string[] types, string[] keys, params string[][] rows)
    {
        var text = new StringBuilder();
        text.Append(string.Join("\t", columns)).Append("\r\n");
        text.Append(string.Join("\t", types)).Append("\r\n");
        text.Append(table).Append('\t').Append(string.Join("\t", keys)).Append("\r\n");
        foreach (string[] row in rows)
        {
            text.Append(string.Join("\t", row)).Append("\r\n");
        }

        File.WriteAllText(Path.Combine(folder, table + ".idt"), text.ToString(), Encoding.Latin1);
    }

    private static List<string?> ReadPool(string path)
    {
        using MsiDatabase db = MsiDatabase.Open(path);
        return db.StringPoolEntries.Select(e => e).ToList();
    }

    private static string FormatPool(List<string?> pool)
    {
        var text = new StringBuilder();
        for (int id = 1; id < pool.Count; id++)
        {
            text.Append("  ").Append(id.ToString().PadLeft(3)).Append("  ").AppendLine(pool[id] == null ? "<blank>" : "'" + pool[id] + "'");
        }

        return text.ToString();
    }

    private static void Probe(StringBuilder report, string root, string name, string description, Action<NativeDatabase, string> body)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".msi");
        report.AppendLine("== " + name + ": " + description);
        try
        {
            using (var db = new NativeDatabase(path, CreateDirect))
            {
                body(db, folder);
                db.Commit();
            }

            report.Append(FormatPool(ReadPool(path)));
        }
        catch (Exception e)
        {
            report.AppendLine("  FAILED: " + e.Message);
        }

        report.AppendLine();
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: StringPoolTruth <report file> [--replay <msi>...]");
            return 2;
        }

        string root = Path.Combine(Path.GetTempPath(), "StringPoolTruth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var report = new StringBuilder();
        report.AppendLine("StringPoolTruth " + DateTime.Now.ToString("s"));
        report.AppendLine();

        if (args.Length > 2 && args[1] == "--matrix")
        {
            Matrix(report, root, args[2]);
        }
        else if (args.Length > 2 && args[1] == "--synthetic")
        {
            Synthetic(report, root, args[2]);
        }
        else if (args.Length > 3 && args[1] == "--idts-file")
        {
            foreach (string variant in new[] { "assign", "sql", "assign+streams" })
            {
                ReplayIdtsWithFile(report, root, args[2], args[3], variant);
            }
        }
        else if (args.Length > 3 && args[1] == "--managed")
        {
            ManagedReplay(report, root, args[2], args[3]);
        }
        else if (args.Length > 3 && args[1] == "--idts")
        {
            ReplayIdts(report, root, args[2], args[3]);
        }
        else if (args.Length > 1 && args[1] == "--replay")
        {
            foreach (string msi in args.Skip(2))
            {
                Replay(report, root, msi);
            }
        }
        else
        {
            Probes(report, root);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        File.WriteAllText(args[0], report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine("Report written to " + args[0]);
        return 0;
    }

    private static void Probes(StringBuilder report, string root)
    {
        string[] threeCols = { "K", "A", "B" };
        string[] threeTypes = { "s32", "s32", "S32" };

        Probe(report, root, "p00-empty", "nothing but a commit", (db, dir) => { });

        Probe(report, root, "p01-create-table", "CREATE TABLE T(K,A,B), no rows",
            (db, dir) => db.Sql("CREATE TABLE `T` (`K` CHAR(32) NOT NULL, `A` CHAR(32), `B` CHAR(32) PRIMARY KEY `K`)"));

        Probe(report, root, "p02-sql-insert", "two rows by SQL INSERT in table column order",
            (db, dir) =>
            {
                db.Sql("CREATE TABLE `T` (`K` CHAR(32) NOT NULL, `A` CHAR(32), `B` CHAR(32) PRIMARY KEY `K`)");
                db.Sql("INSERT INTO `T` (`K`, `A`, `B`) VALUES ('k1', 'a1', 'b1')");
                db.Sql("INSERT INTO `T` (`K`, `A`, `B`) VALUES ('k2', 'a2', 'b2')");
            });

        Probe(report, root, "p03-sql-insert-reordered", "one row by SQL INSERT naming the columns B, A, K",
            (db, dir) =>
            {
                db.Sql("CREATE TABLE `T` (`K` CHAR(32) NOT NULL, `A` CHAR(32), `B` CHAR(32) PRIMARY KEY `K`)");
                db.Sql("INSERT INTO `T` (`B`, `A`, `K`) VALUES ('b1', 'a1', 'k1')");
            });

        Probe(report, root, "p04-idt-two-rows", "IDT import, rows k1 then k2",
            (db, dir) =>
            {
                WriteIdt(dir, "T", threeCols, threeTypes, new[] { "K" }, new[] { "k1", "a1", "b1" }, new[] { "k2", "a2", "b2" });
                db.Import(dir, "T.idt");
            });

        Probe(report, root, "p05-idt-unsorted-rows", "IDT import, rows in key order k2, k1, k3 (does import sort first?)",
            (db, dir) =>
            {
                WriteIdt(dir, "T", threeCols, threeTypes, new[] { "K" }, new[] { "k2", "a2", "b2" }, new[] { "k1", "a1", "b1" }, new[] { "k3", "a3", "b3" });
                db.Import(dir, "T.idt");
            });

        Probe(report, root, "p06-idt-two-tables", "IDT import of tables U then T that share the value 'shared'",
            (db, dir) =>
            {
                WriteIdt(dir, "U", new[] { "UK", "UA" }, new[] { "s32", "S32" }, new[] { "UK" }, new[] { "u1", "shared" }, new[] { "u2", "ua2" });
                WriteIdt(dir, "T", threeCols, threeTypes, new[] { "K" }, new[] { "k1", "shared", "b1" });
                db.Import(dir, "U.idt");
                db.Import(dir, "T.idt");
            });

        Probe(report, root, "p07-idt-commit-between", "same as p06 with a commit after each import",
            (db, dir) =>
            {
                WriteIdt(dir, "U", new[] { "UK", "UA" }, new[] { "s32", "S32" }, new[] { "UK" }, new[] { "u1", "shared" }, new[] { "u2", "ua2" });
                WriteIdt(dir, "T", threeCols, threeTypes, new[] { "K" }, new[] { "k1", "shared", "b1" });
                db.Import(dir, "U.idt");
                db.Commit();
                db.Import(dir, "T.idt");
                db.Commit();
            });

        Probe(report, root, "p08-idt-multikey", "table with a two-column primary key, rows given as (b,a) then (a,z) then (a,b)",
            (db, dir) =>
            {
                WriteIdt(dir, "M", new[] { "K1", "K2", "V" }, new[] { "s32", "s32", "S32" }, new[] { "K1", "K2" },
                    new[] { "kb", "ka", "v1" }, new[] { "ka", "kz", "v2" }, new[] { "ka", "kb", "v3" });
                db.Import(dir, "M.idt");
            });

        Probe(report, root, "p09-idt-file-like", "a File-shaped table: 8 columns, two rows",
            (db, dir) =>
            {
                WriteIdt(dir, "File",
                    new[] { "File", "Component_", "FileName", "FileSize", "Version", "Language", "Attributes", "Sequence" },
                    new[] { "s72", "s72", "l255", "i4", "S72", "S20", "I2", "i4" },
                    new[] { "File" },
                    new[] { "f1", "c1", "n1", "10", "v1", "l1", "0", "1" },
                    new[] { "f2", "c2", "n2", "20", "v2", "l2", "0", "2" });
                db.Import(dir, "File.idt");
            });

        Probe(report, root, "p10-create-order", "CREATE TABLE ZZ, AA, MM (SQL) then one row each",
            (db, dir) =>
            {
                foreach (string name in new[] { "ZZ", "AA", "MM" })
                {
                    db.Sql("CREATE TABLE `" + name + "` (`" + name + "K` CHAR(32) NOT NULL, `" + name + "V` CHAR(32) PRIMARY KEY `" + name + "K`)");
                }

                foreach (string name in new[] { "ZZ", "AA", "MM" })
                {
                    db.Sql("INSERT INTO `" + name + "` (`" + name + "K`, `" + name + "V`) VALUES ('" + name.ToLowerInvariant() + "1', 'val-" + name + "')");
                }
            });

        Probe(report, root, "p11-repeat-values", "IDT rows repeating a value in the same column and in another column",
            (db, dir) =>
            {
                WriteIdt(dir, "T", threeCols, threeTypes, new[] { "K" },
                    new[] { "k1", "x", "y" }, new[] { "k2", "y", "x" }, new[] { "k3", "x", "k1" });
                db.Import(dir, "T.idt");
            });

        Probe(report, root, "p12-property-like", "Property table, 3 rows, imported",
            (db, dir) =>
            {
                WriteIdt(dir, "Property", new[] { "Property", "Value" }, new[] { "s72", "l0" }, new[] { "Property" },
                    new[] { "ProductName", "Name1" }, new[] { "ALLUSERS", "1" }, new[] { "Manufacturer", "Maker" });
                db.Import(dir, "Property.idt");
            });
    }

    // Imports small subsets of the captured IDT files, each into a fresh database, to see which tables influence which
    // string ids. Each experiment prints the pool it produced.
    private static void Matrix(StringBuilder report, string root, string idtFolder)
    {
        string Find(string table)
        {
            string[] hits = Directory.GetFiles(idtFolder, "*_" + table + ".idt");
            if (hits.Length == 0)
            {
                throw new FileNotFoundException("no IDT for " + table);
            }

            return hits[0];
        }

        var experiments = new List<string[]>
        {
            new[] { "_SummaryInformation", "Component", "Directory" },
            new[] { "_SummaryInformation", "Component", "Directory", "Feature" },
            new[] { "_SummaryInformation", "LaunchCondition" },
            new[] { "_SummaryInformation", "Feature" },
            new[] { "_SummaryInformation", "Media" },
            new[] { "_SummaryInformation", "InstallExecuteSequence" },
            new[] { "_SummaryInformation", "!commit", "Upgrade" },
            new[] { "_SummaryInformation", "Upgrade", "!commit", "LaunchCondition" },
            new[] { "_SummaryInformation", "Upgrade", "LaunchCondition", "!commit", "Component" },
            new[] { "Upgrade", "LaunchCondition", "_SummaryInformation", "Component", "Directory" },
            new[] { "Component", "_SummaryInformation", "Upgrade", "Directory" },
        };

        int index = 0;
        foreach (string[] tables in experiments)
        {
            string path = Path.Combine(root, "matrix-" + index++ + ".msi");
            report.AppendLine("== import " + string.Join(" -> ", tables));
            try
            {
                using (var db = new NativeDatabase(path, CreateDirect))
                {
                    foreach (string table in tables)
                    {
                        if (table == "!commit")
                        {
                            db.Commit();
                            continue;
                        }

                        string file = Find(table);
                        db.Import(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileName(file));
                    }

                    db.Commit();
                }

                // Long text is shortened so the order is easy to read.
                List<string?> pool = ReadPool(path);
                var line = new StringBuilder();
                for (int id = 5; id < pool.Count && id < 140; id++)
                {
                    string text = pool[id] ?? "<blank>";
                    line.Append("  ").Append(id.ToString().PadLeft(3)).Append("  ").AppendLine(text.Length > 40 ? text.Substring(0, 40) + "..." : text);
                }

                report.Append(line);
            }
            catch (Exception e)
            {
                report.AppendLine("  FAILED: " + e.Message);
            }

            report.AppendLine();
        }
    }

    private sealed record SynthTable(string Table, int Cols, int Rows, bool Reverse = false, string Prefix = "");

    // Tiny made-up tables imported after the captured _SummaryInformation, to find the rule behind the scrambled
    // string order. Column names are <Table>C1..Cn, values <Prefix><Table>R<row>C<col>.
    private static void Synthetic(StringBuilder report, string root, string idtFolder)
    {
        string summary = Directory.GetFiles(idtFolder, "*__SummaryInformation.idt").First();
        string synth = Path.Combine(root, "synth");
        Directory.CreateDirectory(synth);

        const string Sum = "summary";
        SynthTable T(string table, int cols, int rows, bool reverse = false, string prefix = "") => new SynthTable(table, cols, rows, reverse, prefix);

        var experiments = new List<(string Title, object[] Steps)>
        {
            ("1 col, 1 row", new object[] { Sum, T("Alpha", 1, 1) }),
            ("2 cols, 1 row", new object[] { Sum, T("Alpha", 2, 1) }),
            ("4 cols, 1 row", new object[] { Sum, T("Alpha", 4, 1) }),
            ("8 cols, 1 row", new object[] { Sum, T("Alpha", 8, 1) }),
            ("2 cols, 4 rows", new object[] { Sum, T("Alpha", 2, 4) }),
            ("2 cols, 4 rows, rows reversed in the file", new object[] { Sum, T("Alpha", 2, 4, true) }),
            ("same values with another prefix", new object[] { Sum, T("Alpha", 2, 1, false, "zz") }),
            ("another table name", new object[] { Sum, T("Zeta", 2, 1) }),
            ("4 tables, 2 cols 1 row", new object[] { Sum, T("Alpha", 2, 1), T("Beta", 2, 1), T("Gamma", 2, 1), T("Delta", 2, 1) }),
            ("4 tables, Beta first", new object[] { Sum, T("Beta", 2, 1), T("Alpha", 2, 1), T("Gamma", 2, 1), T("Delta", 2, 1) }),
            ("4 tables, 1 col 1 row", new object[] { Sum, T("Alpha", 1, 1), T("Beta", 1, 1), T("Gamma", 1, 1), T("Delta", 1, 1) }),
            ("3 tables, 8 cols 1 row", new object[] { Sum, T("Alpha", 8, 1), T("Beta", 8, 1), T("Gamma", 8, 1) }),
            ("no summary: 2 tables", new object[] { T("Alpha", 2, 1), T("Beta", 2, 1) }),
        };

        int index = 0;
        foreach (var (title, steps) in experiments)
        {
            string path = Path.Combine(root, "synth-" + index++ + ".msi");
            report.AppendLine("== " + title);
            try
            {
                using (var db = new NativeDatabase(path, CreateDirect))
                {
                    foreach (object step in steps)
                    {
                        if (step is string)
                        {
                            db.Import(Path.GetDirectoryName(Path.GetFullPath(summary))!, Path.GetFileName(summary));
                            continue;
                        }

                        var t = (SynthTable)step;
                        var sb = new StringBuilder();
                        var names = Enumerable.Range(1, t.Cols).Select(c => t.Table + "C" + c).ToList();
                        sb.Append(string.Join("\t", names)).Append("\r\n");
                        sb.Append(string.Join("\t", Enumerable.Range(1, t.Cols).Select(c => (c == 1 ? "s72" : "S72")))).Append("\r\n");
                        sb.Append(t.Table).Append('\t').Append(names[0]).Append("\r\n");
                        var rowNumbers = Enumerable.Range(1, t.Rows).ToList();
                        if (t.Reverse)
                        {
                            rowNumbers.Reverse();
                        }

                        foreach (int r in rowNumbers)
                        {
                            sb.Append(string.Join("\t", Enumerable.Range(1, t.Cols).Select(c => t.Prefix + t.Table + "R" + r + "C" + c))).Append("\r\n");
                        }

                        string file = t.Table + ".idt";
                        File.WriteAllText(Path.Combine(synth, file), sb.ToString(), Encoding.ASCII);
                        db.Import(synth, file);
                    }

                    db.Commit();
                }

                List<string?> pool = ReadPool(path);
                for (int id = 5; id < pool.Count; id++)
                {
                    report.Append("  ").Append(id.ToString().PadLeft(3)).Append("  ").AppendLine(pool[id] ?? "<blank>");
                }
            }
            catch (Exception e)
            {
                report.AppendLine("  FAILED: " + e.Message);
            }

            report.AppendLine();
        }
    }

    // Replays the WiX IDT files through msi.dll and fills the File table with a record Assign (the way WiX does) or with SQL
    // INSERT, then compares the string pool with the Windows-built database.
    private static void ReplayIdtsWithFile(StringBuilder report, string root, string idtFolder, string originalMsi, string variant)
    {
        report.AppendLine("== msi.dll replay with the File row added by " + variant);
        List<string?> original = ReadPool(originalMsi);
        string path = Path.Combine(root, "idt-file-" + variant + ".msi");
        string[] files = Directory.GetFiles(idtFolder, "*.idt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        using (var db = new NativeDatabase(path, CreateDirect))
        {
            foreach (string file in files)
            {
                db.Import(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileName(file));
                if (variant.EndsWith("+streams", StringComparison.Ordinal) && file.EndsWith("_MsiFileHash.idt", StringComparison.OrdinalIgnoreCase))
                {
                    // WiX adds the cabinet through _Streams just before it imports _Validation.
                    string cab = Path.Combine(root, "cab1.cab");
                    File.WriteAllBytes(cab, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
                    db.AssignStream("cab1.cab", cab);
                }

                if (file.EndsWith("_File.idt", StringComparison.OrdinalIgnoreCase))
                {
                    if (variant.StartsWith("assign", StringComparison.Ordinal))
                    {
                        db.AssignFileRow();
                    }
                    else
                    {
                        db.Sql("INSERT INTO `File` (`File`, `Component_`, `FileName`, `FileSize`, `Version`, `Language`, `Attributes`, `Sequence`) VALUES ('HelloFile', 'HelloComponent', 'hello.txt', 5, NULL, NULL, 512, 1)");
                    }
                }
            }

            db.Commit();
        }

        List<string?> replayed = ReadPool(path);
        int differences = 0;
        int length = Math.Max(original.Count, replayed.Count);
        for (int id = 1; id < length; id++)
        {
            string? a = id < original.Count ? original[id] : null;
            string? b = id < replayed.Count ? replayed[id] : null;
            if (a != b)
            {
                differences++;
                if (differences <= 40)
                {
                    report.Append("  id ").Append(id).Append(": windows ").Append(a ?? "<blank>").Append("   msi.dll ").AppendLine(b ?? "<blank>");
                }
            }
        }

        report.AppendLine(differences == 0 ? "IDENTICAL pool" : differences + " ids differ");
        report.AppendLine();
    }

    // Builds a database with the managed library from the same IDT files WiX gave to msi.dll and compares its string pool
    // with the pool of the database Windows built.
    private static void ManagedReplay(StringBuilder report, string root, string idtFolder, string originalMsi)
    {
        report.AppendLine("== managed build from WiX IDT files in " + idtFolder);
        List<string?> original = ReadPool(originalMsi);

        // What a freshly created, committed managed database holds in its pool.
        string emptyPath = Path.Combine(root, "managed-empty.msi");
        using (var empty = new IsWiX.WindowsInstaller.Database(emptyPath, IsWiX.WindowsInstaller.DatabaseOpenMode.CreateDirect))
        {
            empty.Commit();
        }

        List<string?> emptyPool = ReadPool(emptyPath);
        report.AppendLine("fresh managed database pool (" + emptyPool.Count + " entries):");
        for (int id = 0; id < emptyPool.Count && id < 12; id++)
        {
            report.Append("  ").Append(id).Append("  ").AppendLine(emptyPool[id] == null ? "<null>" : "'" + emptyPool[id] + "'");
        }

        report.AppendLine();
        string path = Path.Combine(root, "managed-replay.msi");
        string[] files = Directory.GetFiles(idtFolder, "*.idt")
            .Where(f => !f.EndsWith("__ForceCodepage.idt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using (var db = new IsWiX.WindowsInstaller.Database(path, IsWiX.WindowsInstaller.DatabaseOpenMode.CreateDirect))
        {
            foreach (string file in files)
            {
                db.Import(Path.GetFullPath(file));
                if (file.EndsWith("_MsiFileHash.idt", StringComparison.OrdinalIgnoreCase))
                {
                    // WiX adds the cabinet through _Streams just before it imports _Validation.
                    string cab = Path.Combine(root, "cab1-managed.cab");
                    File.WriteAllBytes(cab, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
                    using (var view = db.OpenView("SELECT `Name`, `Data` FROM `_Streams`"))
                    {
                        view.Execute();
                        using var record = new IsWiX.WindowsInstaller.Record(2);
                        record[1] = "cab1.cab";
                        record.SetStream(2, cab);
                        view.Assign(record);
                    }
                }

                if (file.EndsWith("_File.idt", StringComparison.OrdinalIgnoreCase))
                {
                    // WiX fills the File table with records right after creating it from the (empty) IDT.
                    db.InsertRow("File", ("File", "HelloFile"), ("Component_", "HelloComponent"), ("FileName", "hello.txt"), ("FileSize", 5), ("Attributes", 512), ("Sequence", 1));
                }
            }

            db.Commit();
        }

        List<string?> managed = ReadPool(path);
        int differences = 0;
        int length = Math.Max(original.Count, managed.Count);
        for (int id = 1; id < length; id++)
        {
            string? a = id < original.Count ? original[id] : null;
            string? b = id < managed.Count ? managed[id] : null;
            if (a != b)
            {
                differences++;
                if (differences <= 40)
                {
                    report.Append("  id ").Append(id).Append(": windows ").Append(a ?? "<blank>").Append("   managed ").AppendLine(b ?? "<blank>");
                }
            }
        }

        report.AppendLine(differences == 0 ? "IDENTICAL pool" : differences + " ids differ");
        report.AppendLine("pool sizes: windows " + original.Count + ", managed " + managed.Count);
        report.AppendLine();
    }

    // Imports the IDT files WiX gave to msi.dll (kept with the WIX_KEEP_IDTS environment variable, numbered in import
    // order) into a new database and compares the string pool with the one in the database WiX built.
    private static void ReplayIdts(StringBuilder report, string root, string idtFolder, string originalMsi)
    {
        report.AppendLine("== replay of WiX IDT files from " + idtFolder);
        List<string?> original = ReadPool(originalMsi);
        string path = Path.Combine(root, "idt-replay.msi");
        string[] files = Directory.GetFiles(idtFolder, "*.idt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        report.AppendLine("import order: " + string.Join(", ", files.Select(Path.GetFileName)));
        using (var db = new NativeDatabase(path, CreateDirect))
        {
            foreach (string file in files)
            {
                db.Import(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileName(file));
            }

            db.Commit();
        }

        List<string?> replayed = ReadPool(path);
        int differences = 0;
        int length = Math.Max(original.Count, replayed.Count);
        for (int id = 1; id < length; id++)
        {
            string? a = id < original.Count ? original[id] : null;
            string? b = id < replayed.Count ? replayed[id] : null;
            if (a != b)
            {
                differences++;
                if (differences <= 60)
                {
                    report.Append("  id ").Append(id).Append(": original ").Append(a ?? "<blank>").Append("   replay ").AppendLine(b ?? "<blank>");
                }
            }
        }

        report.AppendLine(differences == 0 ? "IDENTICAL pool" : differences + " ids differ");
        report.AppendLine();
        report.AppendLine("replayed pool:");
        report.Append(FormatPool(replayed));
        report.AppendLine();
    }

    private static void Replay(StringBuilder report, string root, string msi)
    {
        report.AppendLine("== replay " + msi);
        List<string?> original;
        List<string> tables;
        using (MsiDatabase source = MsiDatabase.Open(msi))
        {
            original = source.StringPoolEntries.ToList();
            tables = source.TableNames.Where(t => !t.StartsWith('_') || t == "_Validation").ToList();
        }

        report.AppendLine("tables (as listed in _Tables): " + string.Join(", ", tables));

        string work = Path.Combine(root, "replay-" + Path.GetFileNameWithoutExtension(msi));
        string export = Path.Combine(work, "idt");
        Directory.CreateDirectory(export);
        var exported = new List<string>();
        using (var db = new NativeDatabase(msi, ReadOnly))
        {
            foreach (string table in tables)
            {
                try
                {
                    db.Export(table, export, table + ".idt");
                    exported.Add(table);
                }
                catch (Exception e)
                {
                    report.AppendLine("  could not export " + table + ": " + e.Message);
                }
            }
        }

        var orders = new List<(string Name, List<string> Tables)>
        {
            ("as listed", exported.ToList()),
            ("reversed", Enumerable.Reverse(exported).ToList()),
            ("alphabetical", exported.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList()),
        };

        int index = 0;
        foreach (var (name, order) in orders)
        {
            string path = Path.Combine(work, "replay-" + index++ + ".msi");
            try
            {
                using (var db = new NativeDatabase(path, CreateDirect))
                {
                    foreach (string table in order)
                    {
                        db.Import(export, table + ".idt");
                    }

                    db.Commit();
                }

                List<string?> replayed = ReadPool(path);
                int differences = 0;
                int length = Math.Max(original.Count, replayed.Count);
                var lines = new StringBuilder();
                for (int id = 1; id < length; id++)
                {
                    string? a = id < original.Count ? original[id] : null;
                    string? b = id < replayed.Count ? replayed[id] : null;
                    if (a != b)
                    {
                        differences++;
                        if (differences <= 25)
                        {
                            lines.Append("    id ").Append(id).Append(": original ").Append(a ?? "<blank>").Append("   replay ").AppendLine(b ?? "<blank>");
                        }
                    }
                }

                report.AppendLine("  order '" + name + "': " + (differences == 0 ? "IDENTICAL pool" : differences + " ids differ"));
                report.Append(lines);
            }
            catch (Exception e)
            {
                report.AppendLine("  order '" + name + "' FAILED: " + e.Message);
            }
        }

        report.AppendLine();
        report.AppendLine("original pool:");
        report.Append(FormatPool(original));
        report.AppendLine();
    }
}
