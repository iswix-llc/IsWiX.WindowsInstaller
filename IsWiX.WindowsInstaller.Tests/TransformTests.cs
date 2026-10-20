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

    [Fact]
    public void ChangeTransformSetsProductNameLikeWindows()
    {
        string path = CopyOfBase();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.ApplyTransform(TestFixtures.Path("test-change.mst"));
            db.Commit();
        }

        using var check = new Database(path);
        Assert.Equal(ChangedRows, PropertyRows(check));
    }

    [Fact]
    public void AddTransformInsertsPropertyLikeWindows()
    {
        string path = CopyOfBase();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            db.ApplyTransform(TestFixtures.Path("test-add.mst"));
            db.Commit();
        }

        using var check = new Database(path);
        Assert.Equal(AddedRows, PropertyRows(check));
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
}
