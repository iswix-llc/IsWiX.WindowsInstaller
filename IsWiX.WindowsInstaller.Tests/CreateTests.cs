using MsiReader;
using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of databases made with the Create and CreateDirect modes.</summary>
public class CreateTests : IDisposable
{
    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public CreateTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private static void CreateAndFillTable(Database db)
    {
        db.Execute("CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Key`)");
        db.Execute("INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");
    }

    [Fact]
    public void CreateTransactWritesOnCommit()
    {
        string path = Path.Combine(folder, "new.msi");
        using (var db = new Database(path, DatabaseOpenMode.Create))
        {
            CreateAndFillTable(db);
            db.Commit();
        }

        using var reopened = new Database(path);
        Assert.Equal(1, reopened.CountRows("Sample"));
    }

    [Fact]
    public void CreateTransactWritesNothingWithoutCommit()
    {
        string path = Path.Combine(folder, "uncommitted.msi");
        using (var db = new Database(path, DatabaseOpenMode.Create))
        {
            CreateAndFillTable(db);
        }

        using var reopened = new Database(path);
        Assert.False(reopened.Tables.Contains("Sample"));
    }

    [Fact]
    public void CreateDirectWritesOnClose()
    {
        string path = Path.Combine(folder, "direct.msi");
        using (var db = new Database(path, DatabaseOpenMode.CreateDirect))
        {
            CreateAndFillTable(db);
        }

        using var reopened = new Database(path);
        Assert.Equal(1, reopened.CountRows("Sample"));
    }

    [Fact]
    public void CreateFailsWhenFileExistsAndLeavesItAlone()
    {
        string path = Path.Combine(folder, "exists.msi");
        File.WriteAllText(path, "not a database");

        Assert.ThrowsAny<InstallerException>(() => new Database(path, DatabaseOpenMode.Create));
        Assert.Equal("not a database", File.ReadAllText(path));
    }
}
