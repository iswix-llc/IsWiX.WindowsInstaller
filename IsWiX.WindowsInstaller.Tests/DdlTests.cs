using MsiReader;
using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of CREATE TABLE, DROP TABLE and ALTER TABLE ... ADD. Each test works on its own copy of basic.msi.</summary>
public class DdlTests : IDisposable
{
    private const string CreateScratch =
        "CREATE TABLE `Scratch` (`Key` CHAR(32) NOT NULL, `Value` LONGCHAR, PRIMARY KEY `Key`)";

    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public DdlTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private string CopyOfFixture()
    {
        string copy = Path.Combine(folder, "basic.msi");
        File.Copy(TestFixtures.Path("basic.msi"), copy);
        return copy;
    }

    private static List<string> Rows(Database db, string query)
    {
        using View view = db.OpenView(query);
        view.Execute();
        var rows = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            var cells = new List<string>();
            for (int i = 1; i <= row.FieldCount; i++)
            {
                cells.Add(row.IsNull(i) ? "NULL" : row.GetString(i));
            }
            rows.Add(string.Join("|", cells));
        }
        return rows;
    }

    [Fact]
    public void CreateTableThenInsertAndSelect()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using (View create = db.OpenView(CreateScratch))
        {
            create.Execute();
        }
        using (View insert = db.OpenView("INSERT INTO `Scratch` (`Key`, `Value`) VALUES (?, ?)"))
        {
            insert.Execute(new Record("a", "first"));
        }

        Assert.Equal(new[] { "a|first" }, Rows(db, "SELECT `Key`, `Value` FROM `Scratch`"));
    }

    [Fact]
    public void CreateTableWithExistingNameFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View create = db.OpenView("CREATE TABLE `Property` (`Name` CHAR(32) NOT NULL, PRIMARY KEY `Name`)");

        Assert.ThrowsAny<InstallerException>(() => create.Execute());
    }

    [Fact]
    public void CreateTableWithoutPrimaryKeyFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);

        Assert.ThrowsAny<InstallerException>(() => db.OpenView("CREATE TABLE `Scratch` (`Key` CHAR(32) NOT NULL)"));
    }

    [Fact]
    public void CreateTableWithPrimaryKeyNotFirstFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);

        Assert.ThrowsAny<InstallerException>(() => db.OpenView(
            "CREATE TABLE `Scratch` (`Value` LONGCHAR, `Key` CHAR(32) NOT NULL, PRIMARY KEY `Key`)"));
    }

    [Fact]
    public void DropTableRemovesTableFromQueries()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using (View create = db.OpenView(CreateScratch))
        {
            create.Execute();
        }
        using (View drop = db.OpenView("DROP TABLE `Scratch`"))
        {
            drop.Execute();
        }

        Assert.ThrowsAny<InstallerException>(() => db.OpenView("SELECT `Key` FROM `Scratch`"));
    }

    [Fact]
    public void DropMissingTableFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View drop = db.OpenView("DROP TABLE `NoSuchTable`");

        Assert.ThrowsAny<InstallerException>(() => drop.Execute());
    }

    [Fact]
    public void AddColumnToTableFromFileAppearsInSelectAndColumnsTable()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using (View alter = db.OpenView("ALTER TABLE `Property` ADD `Extra` CHAR(10)"))
        {
            alter.Execute();
        }

        var columns = Rows(db, "SELECT `Number`, `Name` FROM `_Columns` WHERE `Table` = 'Property'");
        Assert.Contains("3|Extra", columns);
    }

    [Fact]
    public void AddColumnToTableFromFileIsEmptyAndCommits()
    {
        string path = CopyOfFixture();
        using (var db = new Database(path, DatabaseOpenMode.Transact))
        {
            using (View alter = db.OpenView("ALTER TABLE `Property` ADD `Extra` CHAR(10)"))
            {
                alter.Execute();
            }
            db.Commit();
        }

        using var reopened = new Database(path, DatabaseOpenMode.ReadOnly);
        Assert.Contains("ProductName|Contoso Widget|NULL", Rows(reopened, "SELECT `Property`, `Value`, `Extra` FROM `Property`"));
    }

    [Fact]
    public void AddNotNullColumnToTableWithRowsFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View alter = db.OpenView("ALTER TABLE `Property` ADD `Extra` CHAR(10) NOT NULL");

        Assert.ThrowsAny<InstallerException>(() => alter.Execute());
    }

    [Fact]
    public void AddNotNullColumnToEmptyTableSucceeds()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using (View create = db.OpenView(CreateScratch))
        {
            create.Execute();
        }
        using (View alter = db.OpenView("ALTER TABLE `Scratch` ADD `Count` SHORT NOT NULL"))
        {
            alter.Execute();
        }
        using (View insert = db.OpenView("INSERT INTO `Scratch` (`Key`, `Count`) VALUES (?, ?)"))
        {
            insert.Execute(new Record("a", 7));
        }

        Assert.Equal(new[] { "a|NULL|7" }, Rows(db, "SELECT `Key`, `Value`, `Count` FROM `Scratch`"));
    }

    [Fact]
    public void AddExistingColumnFails()
    {
        using var db = new Database(CopyOfFixture(), DatabaseOpenMode.Transact);
        using View alter = db.OpenView("ALTER TABLE `Property` ADD `Value` CHAR(10)");

        Assert.ThrowsAny<InstallerException>(() => alter.Execute());
    }
}
