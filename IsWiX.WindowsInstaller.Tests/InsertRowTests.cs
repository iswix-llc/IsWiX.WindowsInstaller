using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of Database.InsertRow, TableExists and CreateTableIfMissing.</summary>
public class InsertRowTests : IDisposable
{
    private const string CreateSample = "CREATE TABLE `Sample` (`Key` CHAR(8) NOT NULL, `Value` INT, `Note` CHAR(0) PRIMARY KEY `Key`)";

    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public InsertRowTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private Database NewDatabase()
    {
        var db = new Database(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".msi"), DatabaseOpenMode.Create);
        db.Execute(CreateSample);
        return db;
    }

    private static List<string> Rows(Database db)
    {
        using View view = db.OpenView("SELECT `Key`, `Value`, `Note` FROM `Sample`");
        view.Execute();
        var rows = new List<string>();
        for (Record? row = view.Fetch(); row != null; row = view.Fetch())
        {
            rows.Add(string.Join("|", Enumerable.Range(1, 3).Select(i => row.IsNull(i) ? "NULL" : row.GetString(i))));
        }

        return rows;
    }

    [Fact]
    public void InsertsARowAndLeavesOmittedColumnsNull()
    {
        using Database db = NewDatabase();

        db.InsertRow("Sample", ("Key", "a"), ("Value", 5));

        Assert.Equal(new[] { "a|5|NULL" }, Rows(db));
    }

    [Fact]
    public void ABraceInAValueIsNotAFormatItem()
    {
        using Database db = NewDatabase();

        db.InsertRow("Sample", new Dictionary<string, object> { { "Key", "a" }, { "Note", "{00000000-0000-0000-0000-000000000000}" } });

        Assert.Equal(new[] { "a|NULL|{00000000-0000-0000-0000-000000000000}" }, Rows(db));
    }

    [Fact]
    public void RejectsAValueLongerThanTheColumn()
    {
        using Database db = NewDatabase();

        Assert.ThrowsAny<InstallerException>(() => db.InsertRow("Sample", ("Key", "123456789")));
    }

    [Fact]
    public void RejectsADuplicatePrimaryKey()
    {
        using Database db = NewDatabase();
        db.InsertRow("Sample", ("Key", "a"));

        Assert.ThrowsAny<InstallerException>(() => db.InsertRow("Sample", ("Key", "a")));
    }

    [Fact]
    public void ReportsWhetherATableExists()
    {
        using Database db = NewDatabase();

        Assert.True(db.TableExists("Sample"));
        Assert.False(db.TableExists("Other"));
    }

    [Fact]
    public void CreatesATableOnlyOnce()
    {
        using Database db = NewDatabase();
        const string other = "CREATE TABLE `Other` (`Key` CHAR(8) NOT NULL PRIMARY KEY `Key`)";

        Assert.False(db.CreateTableIfMissing(CreateSample));
        Assert.True(db.CreateTableIfMissing(other));
        Assert.False(db.CreateTableIfMissing(other));
        Assert.True(db.TableExists("Other"));
    }

    [Fact]
    public void CreateTableIfMissingNeedsACreateTableStatement()
    {
        using Database db = NewDatabase();

        Assert.Throws<ArgumentException>(() => db.CreateTableIfMissing("DROP TABLE `Sample`"));
    }

    [Fact]
    public void WidthCheckingCanBeTurnedOff()
    {
        using Database db = NewDatabase();
        db.EnforceStringWidth = false;

        db.InsertRow("Sample", ("Key", "123456789"));
        db.Execute("INSERT INTO `Sample` (`Key`) VALUES ('abcdefghijk')");
        db.Execute("UPDATE `Sample` SET `Key` = 'xyzxyzxyzxyz' WHERE `Key` = '123456789'");

        Assert.Equal(new[] { "abcdefghijk|NULL|NULL", "xyzxyzxyzxyz|NULL|NULL" }, Rows(db).OrderBy(r => r, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void WidthCheckingIsOnByDefaultForSqlWrites()
    {
        using Database db = NewDatabase();
        db.InsertRow("Sample", ("Key", "a"));

        Assert.True(db.EnforceStringWidth);
        Assert.ThrowsAny<InstallerException>(() => db.Execute("INSERT INTO `Sample` (`Key`) VALUES ('abcdefghijk')"));
        Assert.ThrowsAny<InstallerException>(() => db.Execute("UPDATE `Sample` SET `Key` = 'abcdefghijk' WHERE `Key` = 'a'"));
    }
}
