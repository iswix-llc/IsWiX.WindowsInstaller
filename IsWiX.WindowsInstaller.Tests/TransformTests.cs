using IsWiX.WindowsInstaller;
using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>
/// Transform tests. Each expected table is what Windows Installer produced when it applied the same transform
/// to the same database. Each test works on its own copy of the base.
/// </summary>
public class TransformTests : IDisposable
{
    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public TransformTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private string CopyOfBase()
    {
        string copy = Path.Combine(folder, "test.msi");
        File.Copy(TestFixtures.Path("test-base.msi"), copy);
        return copy;
    }

    private static List<string> PropertyRows(Database db)
    {
        using View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`");
        view.Execute();
        var rows = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            rows.Add(row.GetString(1) + "\t" + (row.IsNull(2) ? "" : row.GetString(2)));
        }
        return rows;
    }

    private static List<string> ApplyAndReadBack(string transform, string path)
    {
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.ApplyTransform(TestFixtures.Path(transform));
            db.Commit();
        }

        using var check = new Database(path);
        return PropertyRows(check);
    }

    [Fact]
    public void ChangeTransformSetsProductNameLikeWindows()
    {
        Assert.Equal(ChangedRows, ApplyAndReadBack("test-change.mst", CopyOfBase()));
    }

    [Fact]
    public void AddTransformInsertsPropertyLikeWindows()
    {
        Assert.Equal(AddedRows, ApplyAndReadBack("test-add.mst", CopyOfBase()));
    }

    [Fact]
    public void DeleteTransformRemovesPropertyLikeWindows()
    {
        Assert.Equal(DeletedRows, ApplyAndReadBack("test-delete.mst", CopyOfBase()));
    }

    [Fact]
    public void MultiTransformChangesAndInsertsLikeWindows()
    {
        Assert.Equal(MultiRows, ApplyAndReadBack("test-multi.mst", CopyOfBase()));
    }

    [Fact]
    public void TwoTableTransformChangesBothTablesLikeWindows()
    {
        string path = CopyOfBase();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.ApplyTransform(TestFixtures.Path("test-twotables.mst"));
            db.Commit();
        }

        using var check = new Database(path);
        Assert.Equal(TwoTablePropertyRows, PropertyRows(check));
        Assert.Equal(TwoTableLaunchRows, LaunchConditionRows(check));
    }

    private static List<string> LaunchConditionRows(Database db)
    {
        using View view = db.OpenView("SELECT `Condition`, `Description` FROM `LaunchCondition`");
        view.Execute();
        var rows = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            rows.Add(row.GetString(1) + "\t" + row.GetString(2));
        }
        return rows;
    }

    [Fact]
    public void NewTableTransformAddsTableWithRowLikeWindows()
    {
        string path = CopyOfBase();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.ApplyTransform(TestFixtures.Path("test-newtable.mst"));
            db.Commit();
        }

        using var check = new Database(path);
        using View view = check.OpenView("SELECT `Key`, `Value` FROM `MyProperties`");
        view.Execute();
        var rows = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            rows.Add(row.GetString(1) + "\t" + row.GetString(2));
        }
        Assert.Equal(new List<string> { "Meaning\t42" }, rows);
    }

    [Fact]
    public void TransformOnReadOnlyDatabaseFails()
    {
        using var db = new Database(CopyOfBase());
        Assert.ThrowsAny<InstallerException>(() => db.ApplyTransform(TestFixtures.Path("test-change.mst")));
    }

    private static readonly string[] ChangedRows =
    {
        "ALLUSERS\t1",
        "ARPPRODUCTICON\tIcon.ico",
        "DISABLEADVTSHORTCUTS\t1",
        "Manufacturer\ttest",
        "ProductCode\t{3BE453B9-D0DF-4868-B2D0-4B2AB8B204DF}",
        "ProductLanguage\t1033",
        "ProductName\tchange",
        "ProductVersion\t0.0.1",
        "UpgradeCode\t{5BC56B19-5766-45AE-926A-8F0067411CBA}",
        "DefaultUIFont\tWixUI_Font_Normal",
        "ErrorDialog\tErrorDlg",
        "WixUIRMOption\tUseRM",
        "SecureCustomProperties\tBURNMSIMODIFY;BURNMSIREPAIR;BURNMSIUNINSTALL;WIX_DOWNGRADE_DETECTED;WIX_UPGRADE_DETECTED",
    };

    private static readonly string[] AddedRows =
    {
        "ALLUSERS\t1",
        "ARPPRODUCTICON\tIcon.ico",
        "DISABLEADVTSHORTCUTS\t1",
        "Manufacturer\ttest",
        "ProductCode\t{3BE453B9-D0DF-4868-B2D0-4B2AB8B204DF}",
        "ProductLanguage\t1033",
        "ProductName\ttest",
        "ProductVersion\t0.0.1",
        "UpgradeCode\t{5BC56B19-5766-45AE-926A-8F0067411CBA}",
        "DefaultUIFont\tWixUI_Font_Normal",
        "ErrorDialog\tErrorDlg",
        "WixUIRMOption\tUseRM",
        "SecureCustomProperties\tBURNMSIMODIFY;BURNMSIREPAIR;BURNMSIUNINSTALL;WIX_DOWNGRADE_DETECTED;WIX_UPGRADE_DETECTED",
        "Add\tSomething",
    };

    private static readonly string[] DeletedRows =
    {
        "ALLUSERS\t1",
        "ARPPRODUCTICON\tIcon.ico",
        "DISABLEADVTSHORTCUTS\t1",
        "ProductCode\t{3BE453B9-D0DF-4868-B2D0-4B2AB8B204DF}",
        "ProductLanguage\t1033",
        "ProductName\ttest",
        "ProductVersion\t0.0.1",
        "UpgradeCode\t{5BC56B19-5766-45AE-926A-8F0067411CBA}",
        "DefaultUIFont\tWixUI_Font_Normal",
        "ErrorDialog\tErrorDlg",
        "WixUIRMOption\tUseRM",
        "SecureCustomProperties\tBURNMSIMODIFY;BURNMSIREPAIR;BURNMSIUNINSTALL;WIX_DOWNGRADE_DETECTED;WIX_UPGRADE_DETECTED",
    };

    private static readonly string[] TwoTablePropertyRows =
    {
        "ALLUSERS\t1",
        "ARPPRODUCTICON\tIcon.ico",
        "DISABLEADVTSHORTCUTS\t1",
        "Manufacturer\ttest",
        "ProductCode\t{3BE453B9-D0DF-4868-B2D0-4B2AB8B204DF}",
        "ProductLanguage\t1033",
        "ProductName\ttest1",
        "ProductVersion\t0.0.1",
        "UpgradeCode\t{5BC56B19-5766-45AE-926A-8F0067411CBA}",
        "DefaultUIFont\tWixUI_Font_Normal",
        "ErrorDialog\tErrorDlg",
        "WixUIRMOption\tUseRM",
        "SecureCustomProperties\tBURNMSIMODIFY;BURNMSIREPAIR;BURNMSIUNINSTALL;WIX_DOWNGRADE_DETECTED;WIX_UPGRADE_DETECTED",
    };

    private static readonly string[] TwoTableLaunchRows =
    {
        "NOT WIX_DOWNGRADE_DETECTED\tA newer version of [ProductName] is already installed!",
    };

    private static readonly string[] MultiRows =
    {
        "ALLUSERS\t1",
        "ARPPRODUCTICON\tIcon.ico",
        "DISABLEADVTSHORTCUTS\t1",
        "Manufacturer\ttest1",
        "ProductCode\t{3BE453B9-D0DF-4868-B2D0-4B2AB8B204DF}",
        "ProductLanguage\t1033",
        "ProductName\ttest",
        "ProductVersion\t0.0.1",
        "UpgradeCode\t{5BC56B19-5766-45AE-926A-8F0067411CBA}",
        "DefaultUIFont\tWixUI_Font_Normal",
        "ErrorDialog\tErrorDlg",
        "WixUIRMOption\tUseRM",
        "SecureCustomProperties\tBURNMSIMODIFY;BURNMSIREPAIR;BURNMSIUNINSTALL;WIX_DOWNGRADE_DETECTED;WIX_UPGRADE_DETECTED",
        "New\tProperty",
    };
}
