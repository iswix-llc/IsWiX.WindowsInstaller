using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of Database.Merge.</summary>
public class MergeTests : IDisposable
{
    private const string CreateSample = "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` INT PRIMARY KEY `Key`)";

    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public MergeTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private string MakeDatabase(string name, params string[] statements)
    {
        string path = Path.Combine(folder, name);
        using var db = new Database(path, DatabaseOpenMode.Create);
        foreach (string statement in statements)
        {
            db.Execute(statement);
        }

        db.Commit();
        return path;
    }

    private string CopyOfFixture(string name)
    {
        string copy = Path.Combine(folder, name);
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
    public void MergeAddsTablesAndRows()
    {
        string target = MakeDatabase("target.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");
        string source = MakeDatabase("source.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('b', 2)",
            "CREATE TABLE `Other` (`Name` CHAR(32) NOT NULL, PRIMARY KEY `Name`)", "INSERT INTO `Other` (`Name`) VALUES ('x')");

        using (var db = new Database(target, DatabaseOpenMode.Transact))
        using (var other = new Database(source, DatabaseOpenMode.ReadOnly))
        {
            db.Merge(other);
            db.Commit();
        }

        using var merged = new Database(target);
        Assert.Equal(new[] { "a|1", "b|2" }, Rows(merged, "SELECT `Key`, `Value` FROM `Sample`").OrderBy(r => r).ToArray());
        Assert.Equal(new[] { "x" }, Rows(merged, "SELECT `Name` FROM `Other`"));
    }

    [Fact]
    public void IdenticalRowIsNotAConflict()
    {
        string target = MakeDatabase("target.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");
        string source = MakeDatabase("source.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");

        using var db = new Database(target, DatabaseOpenMode.Transact);
        using var other = new Database(source, DatabaseOpenMode.ReadOnly);
        db.Merge(other, "MergeErrors");

        Assert.Equal(new[] { "a|1" }, Rows(db, "SELECT `Key`, `Value` FROM `Sample`"));
        Assert.False(db.ContainsTable("MergeErrors"));
    }

    [Fact]
    public void ConflictingRowThrowsAndIsListedInTheErrorTable()
    {
        string target = MakeDatabase("target.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");
        string source = MakeDatabase("source.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)");

        using var db = new Database(target, DatabaseOpenMode.Transact);
        using var other = new Database(source, DatabaseOpenMode.ReadOnly);
        MergeException error = Assert.Throws<MergeException>(() => db.Merge(other, "MergeErrors"));

        Assert.Equal(new[] { "Sample" }, error.ConflictTables.ToArray());
        Assert.Equal(new[] { 1 }, error.ConflictCounts.ToArray());
        Assert.Equal(new[] { "a|1" }, Rows(db, "SELECT `Key`, `Value` FROM `Sample`"));
        Assert.Equal(new[] { "Sample|1" }, Rows(db, "SELECT `Table`, `NumRowMergeConflicts` FROM `MergeErrors`"));
    }

    [Fact]
    public void ConflictWithoutAnErrorTableStillThrows()
    {
        string target = MakeDatabase("target.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 1)");
        string source = MakeDatabase("source.msi", CreateSample, "INSERT INTO `Sample` (`Key`, `Value`) VALUES ('a', 2)");

        using var db = new Database(target, DatabaseOpenMode.Transact);
        using var other = new Database(source, DatabaseOpenMode.ReadOnly);

        Assert.Throws<MergeException>(() => db.Merge(other));
    }

    [Fact]
    public void DifferentSchemaThrowsAndMergesNothing()
    {
        string target = MakeDatabase("target.msi", CreateSample);
        string source = MakeDatabase("source.msi", "CREATE TABLE `Sample` (`Key` CHAR(72) NOT NULL, `Value` CHAR(32) PRIMARY KEY `Key`)",
            "CREATE TABLE `Other` (`Name` CHAR(32) NOT NULL, PRIMARY KEY `Name`)");

        using var db = new Database(target, DatabaseOpenMode.Transact);
        using var other = new Database(source, DatabaseOpenMode.ReadOnly);

        Assert.Throws<MergeException>(() => db.Merge(other));
        Assert.False(db.ContainsTable("Other"));
    }

    [Fact]
    public void MergingAnIdenticalDatabaseChangesNothing()
    {
        string target = CopyOfFixture("target.msi");
        string source = CopyOfFixture("source.msi");

        using var db = new Database(target, DatabaseOpenMode.Transact);
        using var other = new Database(source, DatabaseOpenMode.ReadOnly);
        int before = db.CountRows("Property");

        db.Merge(other, "MergeErrors");

        Assert.Equal(before, db.CountRows("Property"));
        Assert.False(db.ContainsTable("MergeErrors"));
    }

    [Fact]
    public void BinaryCellsAreCopied()
    {
        string target = CopyOfFixture("target.msi");
        string source = CopyOfFixture("source.msi");
        byte[] payload = { 4, 5, 6, 7 };
        using (var db = new Database(source, DatabaseOpenMode.Transact))
        using (View insert = db.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)"))
        using (var record = new Record("merged", null))
        {
            record.SetStream(2, new MemoryStream(payload));
            insert.Execute(record);
            db.Commit();
        }

        using (var db = new Database(target, DatabaseOpenMode.Transact))
        using (var other = new Database(source, DatabaseOpenMode.ReadOnly))
        {
            db.Merge(other);
            db.Commit();
        }

        using var reopened = new Database(target);
        using View select = reopened.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'merged'");
        select.Execute();
        using Record? row = select.Fetch();
        Assert.NotNull(row);
        var copied = new MemoryStream();
        row!.GetStream(1).CopyTo(copied);
        Assert.Equal(payload, copied.ToArray());
    }
}
