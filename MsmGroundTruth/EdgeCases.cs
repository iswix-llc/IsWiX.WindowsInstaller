#pragma warning disable CA1416
using System.Text;
using IsWiX.WindowsInstaller;
using WixToolset.Core.Native.Msm;

// Edge-case scenarios for the merge module engine: small purpose-built modules and bases, merged with the real
// mergemod.dll and with the managed engine, then compared. Unlike the corpus run these reach the paths real
// modules rarely do: conflicting rows, dense sequence tables, exclusions, configuration callbacks, platform and
// language rules.
//   dotnet run --project MsmGroundTruth -- --edge <report file>
internal static partial class Program
{
    private const string G = "AAAAAAAA_BBBB_CCCC_DDDD_EEEEEEEEEEEE";
    private const string NullGuid = "{00000000-0000-0000-0000-000000000000}";

    private sealed class FixedConfig : IMsmConfigureModule
    {
        private readonly Dictionary<string, string> text;
        private readonly Dictionary<string, int> numbers;

        public FixedConfig(Dictionary<string, string>? text = null, Dictionary<string, int>? numbers = null)
        {
            this.text = text ?? new Dictionary<string, string>();
            this.numbers = numbers ?? new Dictionary<string, int>();
        }

        public int ProvideTextData(string name, out string configData)
        {
            if (this.text.TryGetValue(name, out string? value))
            {
                configData = value;
                return 0;
            }

            configData = string.Empty;
            return 1;
        }

        public int ProvideIntegerData(string name, out int configData)
        {
            if (this.numbers.TryGetValue(name, out int value))
            {
                configData = value;
                return 0;
            }

            configData = 0;
            return 1;
        }
    }

    private sealed class Scenario
    {
        public Scenario(string name, Action<Database> module)
        {
            this.Name = name;
            this.Module = module;
        }

        public string Name { get; }

        public Action<Database> Module { get; }

        public bool Standard { get; set; } = true;

        public Action<Database>? Base { get; set; }

        public string ModuleTemplate { get; set; } = "Intel;0";

        public string BaseTemplate { get; set; } = "Intel;1033";

        public string? Redirect { get; set; } = "INSTALLDIR";

        public int Language { get; set; }

        public bool Connect { get; set; }

        public string Feature { get; set; } = "F";

        public IMsmConfigureModule? Config { get; set; }

        public int Repeat { get; set; } = 1;

        public string? ModuleFile { get; set; }
    }

    private static void Ins(Database db, string table, params (string Column, object Value)[] values)
    {
        string columns = string.Join(", ", values.Select(v => "`" + v.Column + "`"));
        string markers = string.Join(", ", values.Select(_ => "?"));
        db.Execute("INSERT INTO `" + table + "` (" + columns + ") VALUES (" + markers + ")", new Record(values.Select(v => v.Value).ToArray()));
    }

    private static void Ddl(Database db, params string[] statements)
    {
        foreach (string statement in statements)
        {
            // A base scenario may name a table the standard base already has.
            if (statement.StartsWith("CREATE TABLE `", StringComparison.Ordinal))
            {
                string name = statement.Substring(14, statement.IndexOf('`', 14) - 14);
                if (db.Tables.Any(t => t.Name == name))
                {
                    continue;
                }
            }

            db.Execute(statement);
        }
    }

    private const string DirectoryDdl = "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)";
    private const string ComponentDdl = "CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` INT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)";
    private const string CustomActionDdl = "CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` INT NOT NULL, `Source` CHAR(72), `Target` CHAR(255) PRIMARY KEY `Action`)";
    private const string PropertyDdl = "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)";
    private const string SignatureDdl = "CREATE TABLE `ModuleSignature` (`ModuleID` CHAR(72) NOT NULL, `Language` INT NOT NULL, `Version` CHAR(72) NOT NULL PRIMARY KEY `ModuleID`, `Language`)";
    private const string ModuleComponentsDdl = "CREATE TABLE `ModuleComponents` (`Component` CHAR(72) NOT NULL, `ModuleID` CHAR(72) NOT NULL, `Language` INT NOT NULL PRIMARY KEY `Component`, `ModuleID`, `Language`)";
    private const string ExclusionDdl = "CREATE TABLE `ModuleExclusion` (`ModuleID` CHAR(72) NOT NULL, `ModuleLanguage` INT NOT NULL, `ExcludedID` CHAR(72) NOT NULL, `ExcludedLanguage` INT NOT NULL, `ExcludedMinVersion` CHAR(32), `ExcludedMaxVersion` CHAR(32) PRIMARY KEY `ModuleID`, `ModuleLanguage`, `ExcludedID`, `ExcludedLanguage`)";
    private const string DependencyDdl = "CREATE TABLE `ModuleDependency` (`ModuleID` CHAR(72) NOT NULL, `ModuleLanguage` INT NOT NULL, `RequiredID` CHAR(72) NOT NULL, `RequiredLanguage` INT NOT NULL, `RequiredVersion` CHAR(32) PRIMARY KEY `ModuleID`, `ModuleLanguage`, `RequiredID`, `RequiredLanguage`)";
    private const string ConfigurationDdl = "CREATE TABLE `ModuleConfiguration` (`Name` CHAR(72) NOT NULL, `Format` INT NOT NULL, `Type` CHAR(72), `ContextData` CHAR(255), `DefaultValue` CHAR(255), `Attributes` LONG, `DisplayName` CHAR(255) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `HelpLocation` CHAR(255), `HelpKeyword` CHAR(72) PRIMARY KEY `Name`)";
    private const string SubstitutionDdl = "CREATE TABLE `ModuleSubstitution` (`Table` CHAR(32) NOT NULL, `Row` CHAR(255) NOT NULL, `Column` CHAR(72) NOT NULL, `Value` CHAR(255) LOCALIZABLE PRIMARY KEY `Table`, `Row`, `Column`)";
    private const string IgnoreDdl = "CREATE TABLE `ModuleIgnoreTable` (`Table` CHAR(72) NOT NULL PRIMARY KEY `Table`)";
    private const string ModuleSequenceDdl = "CREATE TABLE `ModuleInstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Sequence` INT, `BaseAction` CHAR(72), `After` INT, `Condition` CHAR(255) PRIMARY KEY `Action`)";
    private const string FileDdl = "CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` INT, `Sequence` LONG NOT NULL PRIMARY KEY `File`)";
    private const string MediaDdl = "CREATE TABLE `Media` (`DiskId` INT NOT NULL, `LastSequence` INT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)";
    private const string AssemblyDdl = "CREATE TABLE `MsiAssembly` (`Component_` CHAR(72) NOT NULL, `Feature_` CHAR(38) NOT NULL, `File_Manifest` CHAR(72), `File_Application` CHAR(72), `Attributes` INT NOT NULL PRIMARY KEY `Component_`)";

    // The module's usual content: a directory, a component in it, and its signature.
    private static void StandardModule(Database db)
    {
        // mergemod only creates FeatureComponents and CustomAction in the database from the module's own empty
        // table definitions, which real modules carry; without them it fails to create the tables.
        Ddl(db, DirectoryDdl, ComponentDdl, SignatureDdl, ModuleComponentsDdl, CustomActionDdl,
            "CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)");
        Ins(db, "Directory", ("Directory", "TARGETDIR"), ("DefaultDir", "SourceDir"));
        Ins(db, "Directory", ("Directory", "MODDIR." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "Mod"));
        Ins(db, "Component", ("Component", "C1." + G), ("ComponentId", "{11111111-2222-3333-4444-555555555555}"), ("Directory_", "MODDIR." + G), ("Attributes", 0));
        Ins(db, "ModuleSignature", ("ModuleID", "M." + G), ("Language", 0), ("Version", "1.0.0.0"));
        Ins(db, "ModuleComponents", ("Component", "C1." + G), ("ModuleID", "M." + G), ("Language", 0));
    }

    private static void ModuleAction(Database db, string action, int? sequence = null, string? baseAction = null, bool after = false, string? condition = null)
    {
        var values = new List<(string, object)> { ("Action", action) };
        if (sequence.HasValue)
        {
            values.Add(("Sequence", sequence.Value));
        }

        if (baseAction != null)
        {
            values.Add(("BaseAction", baseAction));
            values.Add(("After", after ? 1 : 0));
        }

        if (condition != null)
        {
            values.Add(("Condition", condition));
        }

        Ins(db, "ModuleInstallExecuteSequence", values.ToArray());

        // Real modules define each custom action they sequence; an undefined name may count as orphaned.
        if (action.Contains('.') && db.Tables.Any(t => t.Name == "CustomAction"))
        {
            using (var check = db.OpenView("SELECT `Action` FROM `CustomAction` WHERE `Action` = ?"))
            {
                check.Execute(new Record(action));
                using (Record existing = check.Fetch())
                {
                    if (existing == null)
                    {
                        Ins(db, "CustomAction", ("Action", action), ("Type", 51), ("Source", "SEQPROP"), ("Target", "x"));
                    }
                }
            }
        }
    }

    private static void BaseAction(Database db, string action, int sequence)
    {
        Ins(db, "InstallExecuteSequence", ("Action", action), ("Sequence", sequence));
    }

    private static List<Scenario> Scenarios()
    {
        var list = new List<Scenario>();

        // Conflicting and identical rows.
        list.Add(new Scenario("conflict: Property differs", db =>
        {
            Ddl(db, PropertyDdl);
            Ins(db, "Property", ("Property", "Shared"), ("Value", "module"));
        })
        { Base = db => { Ddl(db, PropertyDdl); Ins(db, "Property", ("Property", "Shared"), ("Value", "base")); } });
        list.Add(new Scenario("conflict: identical Property row", db =>
        {
            Ddl(db, PropertyDdl);
            Ins(db, "Property", ("Property", "Shared"), ("Value", "same"));
        })
        { Base = db => { Ddl(db, PropertyDdl); Ins(db, "Property", ("Property", "Shared"), ("Value", "same")); } });
        list.Add(new Scenario("conflict: Directory row differs (after redirect)", db => { })
        { Base = db => Ins(db, "Directory", ("Directory", "MODDIR." + G), ("Directory_Parent", "INSTALLDIR"), ("DefaultDir", "Other")) });
        list.Add(new Scenario("conflict: two tables conflict", db =>
        {
            Ddl(db, PropertyDdl, CustomActionDdl);
            Ins(db, "Property", ("Property", "Shared"), ("Value", "module"));
            Ins(db, "CustomAction", ("Action", "CA1"), ("Type", 51), ("Source", "P"), ("Target", "module"));
        })
        {
            Base = db =>
            {
                Ddl(db, PropertyDdl, CustomActionDdl);
                Ins(db, "Property", ("Property", "Shared"), ("Value", "base"));
                Ins(db, "CustomAction", ("Action", "CA1"), ("Type", 51), ("Source", "P"), ("Target", "base"));
            },
        });
        list.Add(new Scenario("conflict: merged twice", db => { }) { Repeat = 2 });
        list.Add(new Scenario("schema: column type differs", db =>
        {
            Ddl(db, "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Property`)");
            Ins(db, "Property", ("Property", "X"), ("Value", 1));
        })
        { Base = db => { Ddl(db, PropertyDdl); Ins(db, "Property", ("Property", "Y"), ("Value", "y")); } });

        // Sequences.
        list.Add(new Scenario("sequence: dense base, after", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "A." + G, baseAction: "CostInitialize", after: true);
        })
        { Base = db => { BaseAction(db, "D1", 801); BaseAction(db, "D2", 802); BaseAction(db, "D3", 803); } });
        list.Add(new Scenario("sequence: dense base, before", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "B." + G, baseAction: "D3", after: false);
        })
        { Base = db => { BaseAction(db, "D1", 801); BaseAction(db, "D2", 802); BaseAction(db, "D3", 803); } });
        list.Add(new Scenario("sequence: dense base, several after the same action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "A1." + G, baseAction: "CostInitialize", after: true);
            ModuleAction(db, "A2." + G, baseAction: "CostInitialize", after: true);
            ModuleAction(db, "A3." + G, baseAction: "CostInitialize", after: true);
        })
        { Base = db => { BaseAction(db, "D1", 801); BaseAction(db, "D2", 802); BaseAction(db, "D3", 803); } });
        list.Add(new Scenario("sequence: explicit number used by another action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 800);
        }));
        list.Add(new Scenario("sequence: explicit number, condition", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1234, condition: "NOT Installed");
        }));
        list.Add(new Scenario("sequence: base action does not exist", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "N." + G, baseAction: "NoSuchAction", after: true);
        }));
        list.Add(new Scenario("sequence: chained module actions", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "C3." + G, baseAction: "C2." + G, after: true);
            ModuleAction(db, "C2." + G, baseAction: "C1." + G, after: true);
            ModuleAction(db, "C1." + G, baseAction: "CostFinalize", after: true);
        }));
        list.Add(new Scenario("sequence: cycle between module actions", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "Y1." + G, baseAction: "Y2." + G, after: true);
            ModuleAction(db, "Y2." + G, baseAction: "Y1." + G, after: true);
        }));
        list.Add(new Scenario("sequence: before the first action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "F1." + G, baseAction: "CostInitialize", after: false);
        }));
        list.Add(new Scenario("sequence: after the last action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "L1." + G, baseAction: "InstallFinalize", after: true);
        }));
        list.Add(new Scenario("sequence: action already in the base", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "CostFinalize", sequence: 1111);
        }));
        list.Add(new Scenario("sequence: standard folder directories, several", db =>
        {
            Ins(db, "Directory", ("Directory", "ProgramFilesFolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "SystemFolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "AppDataFolder_x." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "NotAStandardFolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "windowsfolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "TempFolder"), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
            Ins(db, "Directory", ("Directory", "ProgramFiles64Folder_64." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));

        // Features.
        list.Add(new Scenario("feature: placeholder, no feature given", db =>
        {
            Ddl(db, AssemblyDdl);
            Ins(db, "MsiAssembly", ("Component_", "C1." + G), ("Feature_", NullGuid), ("Attributes", 0));
        })
        { Feature = string.Empty });
        list.Add(new Scenario("feature: placeholder, feature given", db =>
        {
            Ddl(db, AssemblyDdl);
            Ins(db, "MsiAssembly", ("Component_", "C1." + G), ("Feature_", NullGuid), ("Attributes", 0));
        }));
        list.Add(new Scenario("feature: placeholder in a Property value", db =>
        {
            Ddl(db, PropertyDdl);
            Ins(db, "Property", ("Property", "Owner"), ("Value", NullGuid));
        }));
        list.Add(new Scenario("feature: feature is not in the database", db => { }) { Feature = "NoSuchFeature", Connect = true });
        list.Add(new Scenario("feature: no feature, components not connected", db => { }) { Feature = string.Empty });
        list.Add(new Scenario("feature: connect to a second feature", db => { }) { Connect = true });

        // Exclusions and dependencies.
        list.Add(new Scenario("exclusion: module excludes one in the database", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 0));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 0), ("Version", "1.5.0.0")); } });
        list.Add(new Scenario("exclusion: version inside the range", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 0), ("ExcludedMinVersion", "1.0.0.0"), ("ExcludedMaxVersion", "2.0.0.0"));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 0), ("Version", "1.5.0.0")); } });
        list.Add(new Scenario("exclusion: version above the range", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 0), ("ExcludedMinVersion", "1.0.0.0"), ("ExcludedMaxVersion", "2.0.0.0"));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 0), ("Version", "3.0.0.0")); } });
        list.Add(new Scenario("exclusion: version below the range", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 0), ("ExcludedMinVersion", "1.0.0.0"), ("ExcludedMaxVersion", "2.0.0.0"));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 0), ("Version", "0.5.0.0")); } });
        list.Add(new Scenario("exclusion: only a minimum", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 0), ("ExcludedMinVersion", "2.0.0.0"));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 0), ("Version", "3.0.0.0")); } });
        list.Add(new Scenario("exclusion: other language excluded", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", 1031));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 1033), ("Version", "1.0.0.0")); } });
        list.Add(new Scenario("exclusion: every language but one", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "Other.1"), ("ExcludedLanguage", -1033));
        })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "Other.1"), ("Language", 1033), ("Version", "1.0.0.0")); } });
        list.Add(new Scenario("exclusion: the database excludes the module", db => { })
        {
            Base = db =>
            {
                Ddl(db, ExclusionDdl);
                Ins(db, "ModuleExclusion", ("ModuleID", "Resident.1"), ("ModuleLanguage", 0), ("ExcludedID", "M." + G), ("ExcludedLanguage", 0));
            },
        });
        list.Add(new Scenario("exclusion: the module excludes itself", db =>
        {
            Ddl(db, ExclusionDdl);
            Ins(db, "ModuleExclusion", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("ExcludedID", "M." + G), ("ExcludedLanguage", 0));
        }));
        list.Add(new Scenario("dependency: unmet dependency", db =>
        {
            Ddl(db, DependencyDdl);
            Ins(db, "ModuleDependency", ("ModuleID", "M." + G), ("ModuleLanguage", 0), ("RequiredID", "Missing.1"), ("RequiredLanguage", 0));
        }));
        list.Add(new Scenario("signature: module already merged", db => { })
        { Base = db => { Ddl(db, SignatureDdl); Ins(db, "ModuleSignature", ("ModuleID", "M." + G), ("Language", 0), ("Version", "0.9.0.0")); } });

        // Configuration and substitution.
        list.Add(new Scenario("substitution: callback supplies text", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "[=NAME1]"));
            Ins(db, "ModuleConfiguration", ("Name", "NAME1"), ("Format", 0), ("DefaultValue", "default"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=NAME1]"));
        })
        { Config = new FixedConfig(new Dictionary<string, string> { { "NAME1", "from callback" } }) });
        list.Add(new Scenario("substitution: callback declines, default used", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "[=NAME1]"));
            Ins(db, "ModuleConfiguration", ("Name", "NAME1"), ("Format", 0), ("DefaultValue", "default"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=NAME1]"));
        })
        { Config = new FixedConfig() });
        list.Add(new Scenario("substitution: no callback, default used", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "[=NAME1]"));
            Ins(db, "ModuleConfiguration", ("Name", "NAME1"), ("Format", 0), ("DefaultValue", "default"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=NAME1]"));
        }));
        list.Add(new Scenario("substitution: no default and nothing supplied", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "[=NAME1]"));
            Ins(db, "ModuleConfiguration", ("Name", "NAME1"), ("Format", 0));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=NAME1]"));
        })
        { Config = new FixedConfig() });
        list.Add(new Scenario("substitution: several tokens in one value", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "x"));
            Ins(db, "ModuleConfiguration", ("Name", "N1"), ("Format", 0), ("DefaultValue", "d1"));
            Ins(db, "ModuleConfiguration", ("Name", "N2"), ("Format", 0), ("DefaultValue", "d2"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "a[=N1]b[=N2]c"));
        })
        { Config = new FixedConfig(new Dictionary<string, string> { { "N1", "one" } }) });
        list.Add(new Scenario("substitution: integer item", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "x"));
            Ins(db, "ModuleConfiguration", ("Name", "COUNT"), ("Format", 2), ("DefaultValue", "3"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=COUNT]"));
        })
        { Config = new FixedConfig(null, new Dictionary<string, int> { { "COUNT", 7 } }) });
        list.Add(new Scenario("substitution: substituted column is an integer", db =>
        {
            Ddl(db, CustomActionDdl, ConfigurationDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "x"));
            Ins(db, "ModuleConfiguration", ("Name", "KIND"), ("Format", 2), ("DefaultValue", "1"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Type"), ("Value", "[=KIND]"));
        })
        { Config = new FixedConfig(null, new Dictionary<string, int> { { "KIND", 35 } }) });
        list.Add(new Scenario("substitution: item is not configured", db =>
        {
            Ddl(db, CustomActionDdl, SubstitutionDdl);
            Ins(db, "CustomAction", ("Action", "CA1." + G), ("Type", 51), ("Source", "PROP"), ("Target", "x"));
            Ins(db, "ModuleSubstitution", ("Table", "CustomAction"), ("Row", "CA1." + G), ("Column", "Target"), ("Value", "[=MISSING]"));
        })
        { Config = new FixedConfig() });
        list.Add(new Scenario("substitution: row with a composite key", db =>
        {
            Ddl(db, "CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` INT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)", ConfigurationDdl, SubstitutionDdl);
            Ins(db, "Registry", ("Registry", "R1." + G), ("Root", 2), ("Key", "Software\\Test"), ("Name", "n"), ("Value", "x"), ("Component_", "C1." + G));
            Ins(db, "ModuleConfiguration", ("Name", "V"), ("Format", 0), ("DefaultValue", "dv"));
            Ins(db, "ModuleSubstitution", ("Table", "Registry"), ("Row", "R1." + G), ("Column", "Value"), ("Value", "[=V]"));
        }));

        // Platform and language.
        list.Add(new Scenario("platform: x64 module, x64 database", db => { }) { ModuleTemplate = "x64;0", BaseTemplate = "x64;1033" });
        list.Add(new Scenario("platform: Intel module, x64 database", db => { }) { ModuleTemplate = "Intel;0", BaseTemplate = "x64;1033" });
        list.Add(new Scenario("platform: x64 module, Intel database", db => { }) { ModuleTemplate = "x64;0", BaseTemplate = "Intel;1033" });
        list.Add(new Scenario("platform: x64 module, database with no platform", db => { }) { ModuleTemplate = "x64;0", BaseTemplate = ";1033" });
        list.Add(new Scenario("platform: module with no platform, x64 database", db => { }) { ModuleTemplate = ";0", BaseTemplate = "x64;1033" });
        list.Add(new Scenario("platform: Intel64 module, x64 database", db => { }) { ModuleTemplate = "Intel64;0", BaseTemplate = "x64;1033" });
        list.Add(new Scenario("platform: Intel64 module, Intel database", db => { }) { ModuleTemplate = "Intel64;0", BaseTemplate = "Intel;1033" });
        list.Add(new Scenario("platform: Arm64 module, Arm64 database", db => { }) { ModuleTemplate = "Arm64;0", BaseTemplate = "Arm64;1033" });
        list.Add(new Scenario("platform: two-platform module, x64 database", db => { }) { ModuleTemplate = "Intel,x64;0", BaseTemplate = "x64;1033" });
        list.Add(new Scenario("language: module 1033, requested 1033", db => { }) { ModuleTemplate = "Intel;1033", Language = 1033 });
        list.Add(new Scenario("language: module 1033, requested 1031", db => { }) { ModuleTemplate = "Intel;1033", Language = 1031 });
        list.Add(new Scenario("language: module 1033, requested neutral", db => { }) { ModuleTemplate = "Intel;1033", Language = 0 });
        list.Add(new Scenario("language: neutral module, requested 1031", db => { }) { ModuleTemplate = "Intel;0", Language = 1031 });
        list.Add(new Scenario("language: module lists 1033 and 1031, requested 1031", db => { }) { ModuleTemplate = "Intel;1033,1031", Language = 1031 });
        list.Add(new Scenario("language: module lists 1033 and 1031, requested 1033", db => { }) { ModuleTemplate = "Intel;1033,1031", Language = 1033 });
        list.Add(new Scenario("language: module lists 1033 and 1031, requested 1036", db => { }) { ModuleTemplate = "Intel;1033,1031", Language = 1036 });
        list.Add(new Scenario("language: module language 1031, database language 1033", db => { }) { ModuleTemplate = "Intel;1031", Language = 1031 });

        // Other tables.
        list.Add(new Scenario("files: sequence numbers when the database has files", db =>
        {
            Ddl(db, FileDdl);
            Ins(db, "File", ("File", "F1." + G), ("Component_", "C1." + G), ("FileName", "f1.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 1));
            Ins(db, "File", ("File", "F2." + G), ("Component_", "C1." + G), ("FileName", "f2.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 2));
        })
        {
            Base = db =>
            {
                Ddl(db, FileDdl, MediaDdl);
                Ins(db, "File", ("File", "Old1"), ("Component_", "OldC"), ("FileName", "o1.txt"), ("FileSize", 5), ("Attributes", 0), ("Sequence", 1));
                Ins(db, "File", ("File", "Old2"), ("Component_", "OldC"), ("FileName", "o2.txt"), ("FileSize", 5), ("Attributes", 0), ("Sequence", 2));
                Ins(db, "File", ("File", "Old3"), ("Component_", "OldC"), ("FileName", "o3.txt"), ("FileSize", 5), ("Attributes", 0), ("Sequence", 3));
                Ins(db, "Media", ("DiskId", 1), ("LastSequence", 3), ("Cabinet", "#cab1.cab"));
            },
        });
        list.Add(new Scenario("files: module with files and no cabinet", db =>
        {
            Ddl(db, FileDdl);
            Ins(db, "File", ("File", "F1." + G), ("Component_", "C1." + G), ("FileName", "f1.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 1));
        }));
        list.Add(new Scenario("directory: component in TARGETDIR, redirected", db =>
        {
            Ins(db, "Component", ("Component", "C2." + G), ("ComponentId", "{22222222-2222-3333-4444-555555555555}"), ("Directory_", "TARGETDIR"), ("Attributes", 0));
        }));
        list.Add(new Scenario("directory: redirect directory does not exist", db => { }) { Redirect = "NOSUCHDIR" });
        list.Add(new Scenario("directory: nested directories under the module root", db =>
        {
            Ins(db, "Directory", ("Directory", "SUB." + G), ("Directory_Parent", "MODDIR." + G), ("DefaultDir", "Sub"));
        }));
        list.Add(new Scenario("directory: database has no Directory table", db => { }) { Base = db => db.Execute("DROP TABLE `Directory`") });
        list.Add(new Scenario("ignore: ModuleIgnoreTable names a table", db =>
        {
            Ddl(db, IgnoreDdl, PropertyDdl);
            Ins(db, "ModuleIgnoreTable", ("Table", "Property"));
            Ins(db, "Property", ("Property", "Ignored"), ("Value", "x"));
        }));
        list.Add(new Scenario("ignore: ModuleIgnoreTable names a table that is absent", db =>
        {
            Ddl(db, IgnoreDdl);
            Ins(db, "ModuleIgnoreTable", ("Table", "NoSuchTable"));
        }));
        list.Add(new Scenario("module: no ModuleSignature or ModuleComponents", db => { }) { Standard = false });
        list.Add(new Scenario("module: extra table the database lacks", db =>
        {
            Ddl(db, "CREATE TABLE `Custom` (`K` CHAR(72) NOT NULL, `V` CHAR(72) PRIMARY KEY `K`)");
            Ins(db, "Custom", ("K", "k"), ("V", "v"));
        }));
        list.Add(new Scenario("module: standard sequence table inside the module", db =>
        {
            Ddl(db, "CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` INT NOT NULL PRIMARY KEY `Action`)");
            Ins(db, "InstallExecuteSequence", ("Action", "Mine." + G), ("Sequence", 4321));
        }));

        // Follow-ups that isolate behaviour seen in the first runs.
        list.Add(new Scenario("connect: component with a file key path", db =>
        {
            Ddl(db, FileDdl);
            Ins(db, "File", ("File", "F1." + G), ("Component_", "C1." + G), ("FileName", "f1.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 1));
            db.Execute("UPDATE `Component` SET `KeyPath` = 'F1." + G + "' WHERE `Component` = 'C1." + G + "'");
        }));
        list.Add(new Scenario("connect: Connect only, no feature on merge", db => { }) { Feature = string.Empty, Connect = true });
        list.Add(new Scenario("sequence: single action after CostFinalize", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "S1." + G, baseAction: "CostFinalize", after: true);
        }));
        list.Add(new Scenario("sequence: chain of two, first after CostFinalize", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "T1." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "T2." + G, baseAction: "T1." + G, after: true);
        }));
        list.Add(new Scenario("sequence: two actions after different base actions", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "U1." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "U2." + G, baseAction: "InstallValidate", after: true);
        }));
        list.Add(new Scenario("sequence: explicit number 1500 (next action 1600)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1500);
        }));
        list.Add(new Scenario("sequence: explicit number 1400 (next action 1500)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1400);
        }));
        list.Add(new Scenario("sequence: explicit number 801 in a dense base", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 801);
        })
        { Base = db => { BaseAction(db, "D1", 801); BaseAction(db, "D2", 802); BaseAction(db, "D3", 803); } });
        list.Add(new Scenario("sequence: explicit number 6600 (the last action)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 6600);
        }));
        list.Add(new Scenario("sequence: explicit number 1234 (free)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1234);
        }));
        list.Add(new Scenario("directory: ProgramFilesFolder only", db =>
        {
            Ins(db, "Directory", ("Directory", "ProgramFilesFolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));
        list.Add(new Scenario("directory: lower case windowsfolder only", db =>
        {
            Ins(db, "Directory", ("Directory", "windowsfolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));
        list.Add(new Scenario("directory: TempFolder (no suffix) only", db =>
        {
            Ins(db, "Directory", ("Directory", "TempFolder"), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));
        list.Add(new Scenario("directory: AppDataFolder_x only", db =>
        {
            Ins(db, "Directory", ("Directory", "AppDataFolder_x." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));
        list.Add(new Scenario("directory: ordinary name only", db =>
        {
            Ins(db, "Directory", ("Directory", "NotAStandardFolder." + G), ("Directory_Parent", "TARGETDIR"), ("DefaultDir", "."));
        }));

        list.Add(new Scenario("sequence: action defined with a numeric order, base exists", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "V1." + G, baseAction: "InstallInitialize", after: true);
            ModuleAction(db, "V2." + G, baseAction: "RemoveFiles", after: false);
        }));
        list.Add(new Scenario("files: media last sequence larger than the files", db =>
        {
            Ddl(db, FileDdl);
            Ins(db, "File", ("File", "F1." + G), ("Component_", "C1." + G), ("FileName", "f1.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 1));
        })
        {
            Base = db =>
            {
                Ddl(db, FileDdl, MediaDdl);
                Ins(db, "File", ("File", "Old1"), ("Component_", "OldC"), ("FileName", "o1.txt"), ("FileSize", 5), ("Attributes", 0), ("Sequence", 3));
                Ins(db, "Media", ("DiskId", 1), ("LastSequence", 10), ("Cabinet", "#cab1.cab"));
            },
        });
        list.Add(new Scenario("files: database has a File table with no rows", db =>
        {
            Ddl(db, FileDdl);
            Ins(db, "File", ("File", "F1." + G), ("Component_", "C1." + G), ("FileName", "f1.txt"), ("FileSize", 10), ("Attributes", 0), ("Sequence", 1));
        })
        { Base = db => Ddl(db, FileDdl) });

        // Real modules list the base actions they refer to in their own sequence table (CostInitialize|800|...).
        Action<Database, string, int> anchor = (db, action, number) => ModuleAction(db, action, sequence: number);
        Action<Database> dense = db => { BaseAction(db, "D1", 801); BaseAction(db, "D2", 802); BaseAction(db, "D3", 803); };
        list.Add(new Scenario("anchor: single action after an anchor", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostFinalize", 1000);
            ModuleAction(db, "S1." + G, baseAction: "CostFinalize", after: true);
        }));
        list.Add(new Scenario("anchor: single action before an anchor", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "InstallValidate", 1400);
            ModuleAction(db, "S1." + G, baseAction: "InstallValidate", after: false);
        }));
        list.Add(new Scenario("anchor: anchor number differs from the database", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostFinalize", 1050);
            ModuleAction(db, "S1." + G, baseAction: "CostFinalize", after: true);
        }));
        list.Add(new Scenario("anchor: anchor is not in the database", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "Anchor", 2000);
            ModuleAction(db, "S1." + G, baseAction: "Anchor", after: true);
        }));
        list.Add(new Scenario("anchor: chain of two", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostFinalize", 1000);
            ModuleAction(db, "T1." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "T2." + G, baseAction: "T1." + G, after: true);
        }));
        list.Add(new Scenario("anchor: three after the same anchor", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostFinalize", 1000);
            ModuleAction(db, "A1." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "A2." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "A3." + G, baseAction: "CostFinalize", after: true);
        }));
        list.Add(new Scenario("anchor: three before the same anchor", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "InstallValidate", 1400);
            ModuleAction(db, "B1." + G, baseAction: "InstallValidate", after: false);
            ModuleAction(db, "B2." + G, baseAction: "InstallValidate", after: false);
            ModuleAction(db, "B3." + G, baseAction: "InstallValidate", after: false);
        }));
        list.Add(new Scenario("anchor: dense base, after", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostInitialize", 800);
            ModuleAction(db, "A." + G, baseAction: "CostInitialize", after: true);
        })
        { Base = dense });
        list.Add(new Scenario("anchor: dense base, before", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "D3", 803);
            ModuleAction(db, "B." + G, baseAction: "D3", after: false);
        })
        { Base = dense });
        list.Add(new Scenario("anchor: dense base, several after", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostInitialize", 800);
            ModuleAction(db, "A1." + G, baseAction: "CostInitialize", after: true);
            ModuleAction(db, "A2." + G, baseAction: "CostInitialize", after: true);
        })
        { Base = dense });
        list.Add(new Scenario("anchor: before the first action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostInitialize", 800);
            ModuleAction(db, "F1." + G, baseAction: "CostInitialize", after: false);
        }));
        list.Add(new Scenario("anchor: after the last action", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "InstallFinalize", 6600);
            ModuleAction(db, "L1." + G, baseAction: "InstallFinalize", after: true);
        }));
        list.Add(new Scenario("anchor: two actions after different anchors", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "CostFinalize", 1000);
            anchor(db, "InstallValidate", 1400);
            ModuleAction(db, "U1." + G, baseAction: "CostFinalize", after: true);
            ModuleAction(db, "U2." + G, baseAction: "InstallValidate", after: true);
        }));
        list.Add(new Scenario("anchor: before an anchor with a previous action close by", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            anchor(db, "FileCost", 900);
            ModuleAction(db, "P1." + G, baseAction: "FileCost", after: false);
        }));
        list.Add(new Scenario("anchor: explicit number 1000 (the number of a database action)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1000);
        }));
        list.Add(new Scenario("anchor: explicit number 1050 (free)", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 1050);
        }));
        list.Add(new Scenario("anchor: explicit number 4500 collides in a sparse base", db =>
        {
            Ddl(db, ModuleSequenceDdl);
            ModuleAction(db, "X." + G, sequence: 4600);
        }));

        return list;
    }

    private static string BuildDatabase(string path, string template, Action<Database> build, bool createBase)
    {
        if (createBase)
        {
            CreateBase(path, 500, true, build);

            if (template != "Intel;1033")
            {
                using (var summary = new SummaryInfo(path, true))
                {
                    summary.Template = template;
                    summary.Persist();
                }
            }

            return path;
        }

        using (var db = new Database(path, DatabaseOpenMode.Create))
        {
            build(db);
            db.Commit();
        }

        using (var summary = new SummaryInfo(path, true))
        {
            summary.Title = "Merge Module";
            summary.Template = template;
            summary.RevisionNumber = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
            summary.PageCount = 500;
            summary.WordCount = 0;
            summary.Persist();
        }

        return path;
    }

    private static int Edge(string reportPath, string? realModule)
    {
        string work = Path.Combine(Path.GetTempPath(), "msm-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        var report = new StringBuilder();
        int different = 0;
        int number = 0;
        List<Scenario> scenarios = Scenarios();
        if (realModule != null)
        {
            // A real module through the same harness: tells whether table-creation failures come from the harness or from the synthetic modules.
            scenarios.Insert(0, new Scenario("real module " + Path.GetFileName(realModule), db => { }) { ModuleFile = realModule, ModuleTemplate = "Intel;0", Standard = false, Connect = true });
        }

        foreach (Scenario scenario in scenarios)
        {
            number++;
            string id = number.ToString("D2");
            string modulePath = Path.Combine(work, "module" + id + ".msm");
            string basePath = Path.Combine(work, "base" + id + ".msi");
            try
            {
                if (scenario.ModuleFile != null)
                {
                    File.Copy(scenario.ModuleFile, modulePath, true);
                    File.SetAttributes(modulePath, FileAttributes.Normal);
                }
                else
                {
                    BuildDatabase(modulePath, scenario.ModuleTemplate, db =>
                    {
                        if (scenario.Standard)
                        {
                            StandardModule(db);
                        }

                        scenario.Module(db);
                    }, createBase: false);
                }

                BuildDatabase(basePath, scenario.BaseTemplate, db => scenario.Base?.Invoke(db), createBase: true);

                string nativeRaw = Merge(modulePath, basePath, work, scenario.Redirect, scenario.Language, scenario.Connect, false, scenario.Feature, scenario.Config, scenario.Repeat);
                string native = Section(nativeRaw, "native");
                string managed = Section(Merge(modulePath, basePath, work, scenario.Redirect, scenario.Language, scenario.Connect, true, scenario.Feature, scenario.Config, scenario.Repeat), "managed");

                bool same = native == managed;
                if (!same)
                {
                    different++;
                }

                report.AppendLine("### " + id + " " + scenario.Name + ": " + (same ? "SAME" : "DIFFERENT"));

                // What mergemod did, in short, so the report documents its behaviour even when the engines agree.
                foreach (string line in nativeRaw.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("  log:", StringComparison.Ordinal)).Take(30))
                {
                    report.AppendLine("  mergemod log: " + line.Trim());
                }

                foreach (string line in native.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("merge:", StringComparison.Ordinal) || l.StartsWith("errors:", StringComparison.Ordinal) || l.StartsWith("  error ", StringComparison.Ordinal)))
                {
                    report.AppendLine("  mergemod    : " + line);
                }

                if (!same)
                {
                    HashSet<string> nativeLines = Lines(native, l => l.Length > 0);
                    HashSet<string> managedLines = Lines(managed, l => l.Length > 0);
                    foreach (string line in nativeLines.Where(l => !managedLines.Contains(l)).Take(80))
                    {
                        report.AppendLine("  native only : " + (line.Length > 600 ? line.Substring(0, 600) + "..." : line));
                    }

                    foreach (string line in managedLines.Where(l => !nativeLines.Contains(l)).Take(80))
                    {
                        report.AppendLine("  managed only: " + (line.Length > 600 ? line.Substring(0, 600) + "..." : line));
                    }
                }
            }
            catch (Exception e)
            {
                different++;
                report.AppendLine("### " + id + " " + scenario.Name + ": harness failure " + e.GetType().Name + ": " + e.Message);
                report.AppendLine(e.StackTrace);
            }
        }

        report.Insert(0, scenarios.Count + " scenarios, " + different + " different." + Environment.NewLine);
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(scenarios.Count + " scenarios, " + different + " different. Report: " + Path.GetFullPath(reportPath));
        return 0;
    }
}
