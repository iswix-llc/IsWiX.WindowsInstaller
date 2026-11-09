#pragma warning disable CA1416
using System.Security.Cryptography;
using System.Text;
using IsWiX.WindowsInstaller;
using WixToolset.Core.Native.Msm;

// Captures what the real mergemod.dll (IMsmMerge2) does, as ground truth for a managed replacement. For every merge
// module it merges into a small base database (once without and once with a redirect directory), then writes the
// errors, the module's dependencies and configurable items, and every table of the result. Windows only.
//   dotnet run --project MsmGroundTruth -- <report file> <.msm file or folder of .msm files>...

internal static class Program
{
    private static void CreateBase(string path, int pageCount, bool sequences)
    {
        using (var db = new Database(path, DatabaseOpenMode.Create))
        {
            db.Execute("CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)");
            db.Execute("CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)");
            db.Execute("CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` INT, `Level` INT NOT NULL, `Directory_` CHAR(72), `Attributes` INT NOT NULL PRIMARY KEY `Feature`)");
            db.Execute("INSERT INTO `Directory` (`Directory`, `DefaultDir`) VALUES ('TARGETDIR', 'SourceDir')");
            db.Execute("INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES ('INSTALLDIR', 'TARGETDIR', 'Install')");
            db.Execute("INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductLanguage', '1033')");
            db.Execute("INSERT INTO `Feature` (`Feature`, `Title`, `Display`, `Level`, `Attributes`) VALUES ('F', 'F', 1, 1, 0)");
            db.Execute("INSERT INTO `Feature` (`Feature`, `Title`, `Display`, `Level`, `Attributes`) VALUES ('G', 'G', 2, 1, 0)");
            if (sequences)
            {
                foreach (string table in new[] { "InstallExecuteSequence", "AdminExecuteSequence", "AdvtExecuteSequence", "InstallUISequence", "AdminUISequence", "AdvtUISequence" })
                {
                    db.Execute("CREATE TABLE `" + table + "` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` INT PRIMARY KEY `Action`)");
                }

                foreach ((string action, int sequence) in new[] { ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("InstallValidate", 1400), ("InstallInitialize", 1500), ("ProcessComponents", 1600), ("UnpublishFeatures", 1800), ("RemoveFiles", 3500), ("InstallFiles", 4000), ("RegisterClassInfo", 4600), ("WriteRegistryValues", 5000), ("RegisterProduct", 6100), ("PublishFeatures", 6300), ("PublishProduct", 6400), ("InstallFinalize", 6600) })
                {
                    db.Execute("INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES ('" + action + "', " + sequence + ")");
                }

                foreach (string uiTable in new[] { "InstallUISequence", "AdminUISequence", "AdvtUISequence" })
                {
                    foreach ((string action, int sequence) in new[] { ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("ExecuteAction", 1300) })
                    {
                        db.Execute("INSERT INTO `" + uiTable + "` (`Action`, `Sequence`) VALUES ('" + action + "', " + sequence + ")");
                    }
                }

                foreach ((string action, int sequence) in new[] { ("CostInitialize", 800), ("CostFinalize", 1000), ("InstallValidate", 1400), ("InstallInitialize", 1500), ("InstallFiles", 4000), ("InstallFinalize", 6600) })
                {
                    db.Execute("INSERT INTO `AdminExecuteSequence` (`Action`, `Sequence`) VALUES ('" + action + "', " + sequence + ")");
                }

                foreach ((string action, int sequence) in new[] { ("CostInitialize", 800), ("CostFinalize", 1000), ("InstallValidate", 1400), ("InstallInitialize", 1500), ("PublishFeatures", 6300), ("PublishProduct", 6400), ("InstallFinalize", 6600) })
                {
                    db.Execute("INSERT INTO `AdvtExecuteSequence` (`Action`, `Sequence`) VALUES ('" + action + "', " + sequence + ")");
                }
            }

            db.Commit();
        }

        using (var summary = new SummaryInfo(path, true))
        {
            summary.Title = "Installation Database";
            summary.Template = "Intel;1033";
            summary.RevisionNumber = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
            summary.PageCount = pageCount;
            summary.WordCount = 0;
            summary.Persist();
        }
    }

    private static string Describe(IMsmStrings? value)
    {
        if (value == null)
        {
            return "<null>";
        }

        var items = new List<string>();
        for (int i = 1; i <= value.Count; i++)
        {
            items.Add(value[i]);
        }

        return string.Join(";", items);
    }

    private static string Merge(string module, string baseDatabase, string work, string? redirect, int language, bool connect, bool managed = false)
    {
        string target = Path.Combine(work, Guid.NewGuid().ToString("N") + ".msi");
        File.Copy(baseDatabase, target);
        string log = Path.Combine(work, Path.GetFileName(target) + ".log");
        var text = new StringBuilder();

        IMsmMerge2 merge = managed ? new ManagedMsmMerge() : MsmInterop.GetMsmMerge();
        bool commit = true;
        try
        {
            merge.OpenLog(log);
            merge.OpenDatabase(target);
            merge.OpenModule(module, (short) language);
            try
            {
                merge.MergeEx("F", redirect ?? string.Empty, null!);
                if (connect)
                {
                    merge.Connect("G");
                }

                text.AppendLine("merge: ok");
            }
            catch (Exception e)
            {
                commit = false;
                text.AppendLine("merge: " + e.GetType().Name + " 0x" + e.HResult.ToString("X8") + (e is System.Runtime.InteropServices.COMException ? string.Empty : " " + e.Message));
                if (managed) { Console.Error.WriteLine("  managed: " + Path.GetFileName(module) + ": " + e.Message); }
            }

            IMsmErrors errors = merge.Errors;
            text.AppendLine("errors: " + errors.Count);
            for (int i = 1; i <= errors.Count; i++)
            {
                IMsmError error = errors[i];
                text.AppendLine("  error " + i + ": type=" + (int) error.Type + " path=" + error.Path + " language=" + error.Language
                    + " dbTable=" + error.DatabaseTable + " dbKeys=" + Describe(error.DatabaseKeys)
                    + " modTable=" + error.ModuleTable + " modKeys=" + Describe(error.ModuleKeys));
            }

            try
            {
                text.AppendLine("moduleFiles: " + Describe(merge.ModuleFiles));
            }
            catch (Exception e)
            {
                text.AppendLine("moduleFiles: <" + e.GetType().Name + ">");
            }

            // Dependencies/ConfigurableItems are skipped: the native getters can fail fatally
            // (uncatchable CLR error) for some modules, and the managed engine returns null for both.

            merge.CloseModule();
        }
        finally
        {
            try { merge.CloseDatabase(commit); } catch { }
            try { merge.CloseLog(); } catch { }
        }

        text.Append(Dump(target));
        if (File.Exists(log))
        {
            text.AppendLine("  (merge log " + new FileInfo(log).Length + " bytes)");
            string logText = File.ReadAllText(log);
            if (logText.Length > 0 && (logText[0] == '\uFFFE' || logText[0] == '\uFEFF' || logText.Contains('\0')))
            {
                logText = File.ReadAllText(log, Encoding.Unicode);
            }

            foreach (string line in logText.Split('\n'))
            {
                text.AppendLine("  log: " + line.TrimEnd('\r'));
            }
        }

        return text.ToString();
    }

    private static string Dump(string path)
    {
        var text = new StringBuilder();
        using var db = new Database(path, DatabaseOpenMode.ReadOnly);
        var skip = new HashSet<string> { "_Tables", "_Columns", "_Streams", "_Storages", "_Validation" };
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

    private static string? PreloadMergeMod()
    {
        string? path = Environment.GetEnvironmentVariable("MERGEMOD_PATH");
        if (string.IsNullOrEmpty(path))
        {
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                path = Path.Combine(folder, "Common Files", "Microsoft Shared", "MSI Tools", "mergemod.dll");
                if (File.Exists(path))
                {
                    break;
                }
            }
        }

        if (!File.Exists(path))
        {
            return null;
        }

        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 0x3C;
            stream.Position = reader.ReadInt32() + 4;
            ushort machine = reader.ReadUInt16();
            string dll = machine == 0x014C ? "x86" : machine == 0x8664 ? "x64" : "0x" + machine.ToString("X");
            string process = Environment.Is64BitProcess ? "x64" : "x86";
            Console.WriteLine("mergemod.dll: " + path + " (" + dll + "), this process: " + process);
            if (dll != process)
            {
                Console.WriteLine("The bitness differs. Run it as " + dll + ": dotnet run --project MsmGroundTruth -r win-" + dll + " --self-contained false -- <args>, with the " + dll + " .NET 8 runtime installed.");
                return null;
            }
        }

        System.Runtime.InteropServices.NativeLibrary.Load(path);
        return path;
    }

    private static HashSet<string> Lines(string text, Func<string, bool> keep)
    {
        return new HashSet<string>(text.Split('\n').Select(l => l.TrimEnd('\r')).Where(keep), StringComparer.Ordinal);
    }

    private static string Section(string text, string prefix)
    {
        // The comparable part of a merge result: the outcome, the errors and the tables, but not the log, the temp paths or the COM-only properties.
        var kept = new List<string>();
        foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith("merge:", StringComparison.Ordinal) || line.StartsWith("errors:", StringComparison.Ordinal) || line.StartsWith("  error ", StringComparison.Ordinal)
                || line.StartsWith("  table ", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal) && !line.StartsWith("  log:", StringComparison.Ordinal))
            {
                kept.Add(line);
            }
        }

        // Prefix every row with its table so a difference says where it is.
        var result = new StringBuilder();
        string table = "";
        foreach (string line in kept)
        {
            if (line.StartsWith("  table ", StringComparison.Ordinal))
            {
                table = line.Substring(8, line.IndexOf(' ', 8) - 8);
                result.AppendLine(line);
            }
            else if (line.StartsWith("    ", StringComparison.Ordinal))
            {
                result.AppendLine(table + " | " + line.Trim());
            }
            else
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    // The language to open the module with: the first language of its Template summary property, or 0.
    private static int ModuleLanguage(string module)
    {
        try
        {
            using (var summary = new SummaryInfo(module, false))
            {
                string template = summary.Template ?? string.Empty;
                int semicolon = template.IndexOf(';');
                if (semicolon >= 0)
                {
                    string first = template.Substring(semicolon + 1).Split(',')[0].Trim();
                    if (int.TryParse(first, out int language))
                    {
                        return language;
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return 0;
    }

    private static List<string> ModuleSequences(string module)
    {
        var lines = new List<string>();
        try
        {
            using (var db = new Database(module, DatabaseOpenMode.ReadOnly))
            {
                foreach (string name in new[] { "AdminExecuteSequence", "AdminUISequence", "AdvtExecuteSequence", "AdvtUISequence", "InstallExecuteSequence", "InstallUISequence" })
                {
                    string table = "Module" + name;
                    if (!db.Tables.Contains(table))
                    {
                        continue;
                    }

                    using (View view = db.OpenView("SELECT * FROM `" + table + "`"))
                    {
                        view.Execute();
                        for (Record row = view.Fetch(); row != null; row = view.Fetch())
                        {
                            using (row)
                            {
                                var cells = new List<string>();
                                for (int i = 1; i <= row.FieldCount; i++)
                                {
                                    cells.Add(row.IsNull(i) ? "NULL" : row.GetString(i));
                                }

                                lines.Add(table + " | " + string.Join("|", cells));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            lines.Add("<" + e.GetType().Name + ": " + e.Message + ">");
        }

        return lines;
    }

    private static int Compare(string reportPath, List<string> modules)
    {
        string work = Path.Combine(Path.GetTempPath(), "msm-compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string newerBase = Path.Combine(work, "base500.msi");
        CreateBase(newerBase, 500, true);

        var report = new StringBuilder();
        int different = 0;
        int runs = 0;
        var scenarios = new[]
        {
            (Name: "no redirect, language 0", Redirect: (string?) null, Connect: false, Language: 0),
            (Name: "redirect INSTALLDIR, connect G", Redirect: (string?) "INSTALLDIR", Connect: true, Language: 0),
        };

        foreach (string module in modules.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            int moduleLanguage = ModuleLanguage(module);
            foreach (var scenario in scenarios)
            {
                runs++;
                string native;
                string managed;
                try
                {
                    native = Section(Merge(module, newerBase, work, scenario.Redirect, moduleLanguage, scenario.Connect), "native");
                    managed = Section(Merge(module, newerBase, work, scenario.Redirect, moduleLanguage, scenario.Connect, managed: true), "managed");
                }
                catch (Exception e)
                {
                    report.AppendLine("### " + Path.GetFileName(module) + " / " + scenario.Name + ": harness failure " + e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace);
                    different++;
                    continue;
                }

                if (native == managed)
                {
                    report.AppendLine("### " + Path.GetFileName(module) + " / " + scenario.Name + ": SAME");
                    continue;
                }

                different++;
                report.AppendLine("### " + Path.GetFileName(module) + " / " + scenario.Name + ": DIFFERENT (language " + moduleLanguage + ")");
                foreach (string line in ModuleSequences(module))
                {
                    report.AppendLine("  module seq  : " + line);
                }

                HashSet<string> nativeLines = Lines(native, l => l.Length > 0);
                HashSet<string> managedLines = Lines(managed, l => l.Length > 0);
                foreach (string line in nativeLines.Where(l => !managedLines.Contains(l)).Take(60))
                {
                    report.AppendLine("  native only : " + (line.Length > 1500 ? line.Substring(0, 1500) + "..." : line));
                }

                foreach (string line in managedLines.Where(l => !nativeLines.Contains(l)).Take(60))
                {
                    report.AppendLine("  managed only: " + (line.Length > 1500 ? line.Substring(0, 1500) + "..." : line));
                }
            }
        }

        report.Insert(0, runs + " merges, " + different + " different." + Environment.NewLine);
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(runs + " merges, " + different + " different. Report: " + Path.GetFullPath(reportPath));
        return 0;
    }

    private static int Main(string[] args)
    {
        if (PreloadMergeMod() == null)
        {
            return 3;
        }

        bool compareMode = args.Length > 0 && args[0] == "--compare";
        if (compareMode)
        {
            args = args.Skip(1).ToArray();
        }

        if (args.Length < 2)
        {
            Console.WriteLine("usage: MsmGroundTruth [--compare] <report file> <.msm file or folder>...");
            return 2;
        }

        var modules = new List<string>();
        foreach (string arg in args.Skip(1))
        {
            modules.AddRange(Directory.Exists(arg) ? Directory.GetFiles(arg, "*.msm", SearchOption.AllDirectories) : new[] { arg });
        }

        if (compareMode)
        {
            return Compare(args[0], modules);
        }

        string work = Path.Combine(Path.GetTempPath(), "msm-truth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string baseDatabase = Path.Combine(work, "base.msi");
        CreateBase(baseDatabase, 200, false);
        string newerBase = Path.Combine(work, "base500.msi");
        CreateBase(newerBase, 500, true);

        var report = new StringBuilder();
        foreach (string module in modules.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            report.AppendLine("##### " + module);
            try
            {
                using (var db = new Database(module, DatabaseOpenMode.ReadOnly))
                {
                    report.AppendLine("module tables:");
                    report.Append(Dump(module));
                }

                using (var summary = new SummaryInfo(module, false))
                {
                    report.AppendLine("module summary: template=" + summary.Template + " pageCount=" + summary.PageCount + " revision=" + summary.RevisionNumber);
                }

                report.AppendLine("=== merge, no redirect, language 0");
                report.Append(Merge(module, baseDatabase, work, null, 0, false));
                report.AppendLine("=== merge, redirect INSTALLDIR, language 0, connect G");
                report.Append(Merge(module, baseDatabase, work, "INSTALLDIR", 0, true));
                report.AppendLine("=== merge, no redirect, language 1031 (unsupported unless the module is neutral)");
                report.Append(Merge(module, baseDatabase, work, null, 1031, false));
                report.AppendLine("=== merge into schema 5.0 base with standard sequences, no redirect, language 0");
                report.Append(Merge(module, newerBase, work, null, 0, false));
                report.AppendLine("=== merge into schema 5.0 base with standard sequences, redirect INSTALLDIR, connect G");
                report.Append(Merge(module, newerBase, work, "INSTALLDIR", 0, true));
            }
            catch (Exception e)
            {
                report.AppendLine("harness failure " + e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace);
            }

            report.AppendLine();
        }

        File.WriteAllText(args[0], report.ToString());
        Console.WriteLine(modules.Count + " modules. Report: " + Path.GetFullPath(args[0]));
        return 0;
    }
}
