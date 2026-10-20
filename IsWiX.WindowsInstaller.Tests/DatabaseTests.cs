using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of Database against the small committed fixture basic.msi.</summary>
public class DatabaseTests
{
    [Fact]
    public void OpensReadOnlyAndListsTables()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.True(db.IsReadOnly);
        Assert.Equal(new[] { "Property", "File", "Binary" }, db.Tables.Select(t => t.Name));
    }

    [Fact]
    public void PrimaryKeysComeFromTheTableDefinition()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.Equal(new[] { "Property" }, db.Tables["Property"].PrimaryKeys);
    }

    [Fact]
    public void CodePageIsReadThroughExport()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.Equal(0, db.CodePage);
    }

    [Fact]
    public void ExportWritesIdtHeadersAndRows()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".idt");
        try
        {
            db.Export("Property", path);

            string[] lines = File.ReadAllLines(path);
            Assert.Equal("Property\tValue", lines[0]);
            Assert.Equal("Property\tProperty", lines[2]);
            Assert.Equal(6, lines.Length); // three header lines and three rows
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileIsReported()
    {
        Assert.Throws<FileNotFoundException>(() => new Database(TestFixtures.Path("no-such-file.msi")));
    }

    [Fact]
    public void CommitIsNotSupportedYet()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));

        Assert.Throws<NotImplementedException>(() => db.Commit());
    }
}
