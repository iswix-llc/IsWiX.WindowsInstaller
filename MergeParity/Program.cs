using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using IsWiX.WindowsInstaller;

// Runs the same merges with msi.dll (MsiDatabaseMerge) and with the managed Database.Merge, then compares what they
// return and what the target database holds afterwards. Windows only. Writes a report and prints a summary.
//   dotnet run --project MergeParity -- <report file>

internal static class Program
{
    private const string Sample = "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Key`)";
    private const string NullableSample = "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT, `Note` CHAR(32) PRIMARY KEY `Key`)";

    private sealed record Scenario(string Name, string? ErrorTable, Action<Database> Target, Action<Database> Source);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string szDatabasePath, IntPtr szPersist, out IntPtr phDatabase);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseMergeW(IntPtr hDatabase, IntPtr hDatabaseMerge, string? szTableName);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiDatabaseCommit(IntPtr hDatabase);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr hAny);

    private static void Sql(Database db, params string[] statements)
    {
        foreach (string statement in statements)
        {
            db.Execute(statement);
        }
    }

    private static void InsertBinary(Database db, string name, byte[] data)
    {
        using View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)");
        using var record = new Record(name, null);
        record.SetStream(2, new MemoryStream(data));
        insert.Execute(record);
    }

    private static List<Scenario> Scenarios()
    {
        const string Binary = "CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT PRIMARY KEY `Name`)";
        const string Other = "CREATE TABLE `Other` (`Name` CHAR(32) NOT NULL PRIMARY KEY `Name`)";

        return new List<Scenario>
        {
            new("disjoint rows", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 2)")),
            new("identical rows", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)")),
            new("conflicting row", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)")),
            new("conflicting row, no error table", null, d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)")),
            new("conflict plus new row", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)", "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('c', 3)")),
            new("conflicts in two tables", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)", "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 1)", Other, "INSERT INTO `Other` (`Name`) VALUES ('x')"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)", "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 3)")),
            new("new table in source", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample, Other, "INSERT INTO `Other` (`Name`) VALUES ('x')")),
            new("table only in target", "Errors", d => Sql(d, Sample, Other, "INSERT INTO `Other` (`Name`) VALUES ('x')"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 2)")),
            new("schema: column type differs", "Errors", d => Sql(d, Sample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` CHAR(32) PRIMARY KEY `Key`)")),
            new("schema: string size differs", "Errors", d => Sql(d, Sample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(32) NOT NULL, `Value` INT PRIMARY KEY `Key`)")),
            new("schema: nullable differs", "Errors", d => Sql(d, Sample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT NOT NULL PRIMARY KEY `Key`)")),
            new("schema: localizable differs", "Errors", d => Sql(d, NullableSample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT, `Note` CHAR(32) LOCALIZABLE PRIMARY KEY `Key`)")),
            new("schema: extra column in source, with rows", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, NullableSample, "INSERT INTO `Sample` (`Key`, `Value`, `Note`) VALUES ('b', 2, 'n')")),
            new("schema: fewer columns in source, with rows", "Errors", d => Sql(d, NullableSample, "INSERT INTO `Sample` (`Key`, `Value`, `Note`) VALUES ('a', 1, 'n')"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 2)")),
            new("error table exists, no conflict", "Errors", d => Sql(d, Sample, "CREATE TABLE `Errors` (`Name` CHAR(32) NOT NULL PRIMARY KEY `Name`)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 2)")),
            new("schema: column name differs", "Errors", d => Sql(d, Sample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Count` INT PRIMARY KEY `Key`)")),
            new("schema: extra column", "Errors", d => Sql(d, Sample), d => Sql(d, NullableSample)),
            new("schema: key columns differ", "Errors", d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT NOT NULL PRIMARY KEY `Key`)"), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT NOT NULL PRIMARY KEY `Key`, `Value`)")),
            new("schema mismatch and a new table", "Errors", d => Sql(d, Sample), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` CHAR(32) PRIMARY KEY `Key`)", Other, "INSERT INTO `Other` (`Name`) VALUES ('x')")),
            new("error table name already in target", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)", "CREATE TABLE `Errors` (`Name` CHAR(32) NOT NULL PRIMARY KEY `Name`)"), d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)")),
            new("null versus value", "Errors", d => Sql(d, NullableSample, "INSERT INTO `Sample` (`Key`, `Value`, `Note`) VALUES ('a', 1, 'n')"), d => Sql(d, NullableSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)")),
            new("binary: new row", "Errors", d => { Sql(d, Binary); InsertBinary(d, "one", new byte[] { 1, 2, 3 }); }, d => { Sql(d, Binary); InsertBinary(d, "two", new byte[] { 4, 5 }); }),
            new("binary: same key, same data", "Errors", d => { Sql(d, Binary); InsertBinary(d, "one", new byte[] { 1, 2, 3 }); }, d => { Sql(d, Binary); InsertBinary(d, "one", new byte[] { 1, 2, 3 }); }),
            new("binary: same key, different data", "Errors", d => { Sql(d, Binary); InsertBinary(d, "one", new byte[] { 1, 2, 3 }); }, d => { Sql(d, Binary); InsertBinary(d, "one", new byte[] { 9, 9 }); }),
            new("empty source table", "Errors", d => Sql(d, Sample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)"), d => Sql(d, Sample)),
            new("long string in source row", "Errors", d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Key`)"), d => Sql(d, "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Key`)", "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('" + new string('k', 72) + "', 1)")),
        };
    }

    private static void Build(string path, Action<Database> fill)
    {
        using var db = new Database(path, DatabaseOpenMode.Create);
        fill(db);
        db.Commit();
    }

    private static string RunNative(string target, string source, string? errorTable)
    {
        uint code = MsiOpenDatabaseW(target, (IntPtr) 1, out IntPtr hTarget);
        if (code != 0)
        {
            return "open target failed " + code;
        }

        code = MsiOpenDatabaseW(source, IntPtr.Zero, out IntPtr hSource);
        if (code != 0)
        {
            MsiCloseHandle(hTarget);
            return "open source failed " + code;
        }

        uint merge = MsiDatabaseMergeW(hTarget, hSource, errorTable);
        uint commit = MsiDatabaseCommit(hTarget);
        MsiCloseHandle(hSource);
        MsiCloseHandle(hTarget);
        string label = merge == 0 ? "0" : merge == 1627 ? "conflict" : merge == 1629 ? "schema mismatch" : merge.ToString();
        return "merge=" + label + " commit=" + commit;
    }

    private static string RunManaged(string target, string source, string? errorTable)
    {
        try
        {
            using var db = new Database(target, DatabaseOpenMode.Transact);
            using var other = new Database(source, DatabaseOpenMode.ReadOnly);
            string result;
            try
            {
                db.Merge(other, errorTable);
                result = "merge=0";
            }
            catch (MergeException e)
            {
                result = "merge=" + (e.Message.StartsWith("Schema", StringComparison.Ordinal) ? "schema mismatch" : "conflict");
            }

            db.Commit();
            return result + " commit=0";
        }
        catch (Exception e)
        {
            return "exception " + e.GetType().Name + ": " + e.Message;
        }
    }

    private static string Dump(string path)
    {
        var text = new StringBuilder();
        using var db = new Database(path, DatabaseOpenMode.ReadOnly);
        var skip = new HashSet<string> { "_Tables", "_Columns", "_Streams", "_Storages" };
        foreach (TableInfo table in db.Tables.Where(t => !skip.Contains(t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            text.Append("  table ").Append(table.Name).Append(" (");
            text.Append(string.Join(", ", table.Columns.Select(c => c.Name + " " + c.ColumnDefinitionString))).Append(") keys ");
            text.AppendLine(string.Join(",", table.PrimaryKeys));

            var rows = new List<string>();
            using (View view = db.OpenView("SELECT * FROM `" + table.Name + "`"))
            {
                view.Execute();
                for (Record? row = view.Fetch(); row != null; row = view.Fetch())
                {
                    using (row)
                    {
                        var cells = new List<string>();
                        for (int i = 1; i <= table.Columns.Count; i++)
                        {
                            if (row.IsNull(i))
                            {
                                cells.Add("NULL");
                            }
                            else if (table.Columns[i - 1].Type == typeof(Stream))
                            {
                                using var buffer = new MemoryStream();
                                row.GetStream(i).CopyTo(buffer);
                                cells.Add("bin:" + buffer.Length + ":" + Convert.ToHexString(SHA1.HashData(buffer.ToArray())).Substring(0, 8));
                            }
                            else
                            {
                                cells.Add(row.GetString(i));
                            }
                        }

                        rows.Add(string.Join("|", cells));
                    }
                }
            }

            foreach (string row in rows.OrderBy(r => r, StringComparer.Ordinal))
            {
                text.Append("    ").AppendLine(row);
            }
        }

        return text.ToString();
    }

    private static int Main(string[] args)
    {
        string report = args.Length > 0 ? args[0] : "merge-parity-report.txt";
        string work = Path.Combine(Path.GetTempPath(), "merge-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        var text = new StringBuilder();
        int different = 0;
        int number = 0;

        foreach (Scenario scenario in Scenarios())
        {
            number++;
            string target = Path.Combine(work, number + "-target.msi");
            string source = Path.Combine(work, number + "-source.msi");
            string native = Path.Combine(work, number + "-native.msi");
            string managed = Path.Combine(work, number + "-managed.msi");

            string nativeResult, managedResult, nativeDump, managedDump;
            try
            {
                Build(target, scenario.Target);
                Build(source, scenario.Source);
                File.Copy(target, native);
                File.Copy(target, managed);

                nativeResult = RunNative(native, source, scenario.ErrorTable);
                managedResult = RunManaged(managed, source, scenario.ErrorTable);
                nativeDump = Dump(native);
                managedDump = Dump(managed);
            }
            catch (Exception e)
            {
                text.AppendLine("### " + number + " " + scenario.Name + ": harness failure " + e.GetType().Name + ": " + e.Message);
                different++;
                continue;
            }

            bool same = nativeResult == managedResult && nativeDump == managedDump;
            if (!same)
            {
                different++;
            }

            text.AppendLine("### " + number + " " + scenario.Name + " (error table " + (scenario.ErrorTable ?? "none") + "): " + (same ? "SAME" : "DIFFERENT"));
            text.AppendLine("msi.dll : " + nativeResult);
            text.AppendLine("managed : " + managedResult);
            if (!same)
            {
                text.AppendLine("msi.dll tables:").Append(nativeDump);
                text.AppendLine("managed tables:").Append(managedDump);
            }
            else
            {
                text.Append(nativeDump);
            }

            text.AppendLine();
        }

        File.WriteAllText(report, text.ToString());
        try
        {
            Directory.Delete(work, true);
        }
        catch (IOException)
        {
        }

        Console.WriteLine(number + " scenarios, " + different + " different. Report: " + Path.GetFullPath(report));
        return different == 0 ? 0 : 1;
    }
}
