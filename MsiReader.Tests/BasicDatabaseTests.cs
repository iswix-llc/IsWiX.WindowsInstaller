using Xunit;

namespace MsiReader.Tests;

/// <summary>Tests against the small, committed fixture basic.msi.</summary>
public class BasicDatabaseTests
{
    [Fact]
    public void ListsTablesFromTablesStream()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        Assert.Equal(new[] { "Property", "File", "Binary" }, db.TableNames);
    }

    [Fact]
    public void TableLookupIsCaseInsensitive()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        Assert.True(db.ContainsTable("file"));
        Assert.True(db.TryGetTable("PROPERTY", out _));
    }

    [Fact]
    public void MissingTableIsReported()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        Assert.False(db.TryGetTable("NoSuchTable", out _));
        Assert.Throws<KeyNotFoundException>(() => db.GetTable("NoSuchTable"));
    }

    [Fact]
    public void ReadsStringsIncludingNonAscii()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        var t = db.GetTable("Property");
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal("Contoso Widget", t.Rows[0].GetString("Value"));
        Assert.Equal("Zoë Fabrikam GmbH", t.Rows[1].GetString("Value"));
        Assert.Equal("café", t.Rows[2].GetString("Value"));
    }

    [Fact]
    public void ReadsIntegersNullsAndNegatives()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        var t = db.GetTable("File");
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(12, t.Rows[0].GetInt("FileSize"));
        Assert.Null(t.Rows[0].GetString("Version"));
        Assert.Equal(-7, t.Rows[1].GetInt("FileSize"));
        Assert.Equal("1.2.3.4", t.Rows[1].GetString("Version"));
        Assert.Equal(2, t.Rows[1].GetInt("Sequence"));
    }

    [Fact]
    public void ExposesColumnMetadata()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        var file = db.GetTable("File");

        var fileName = file.GetColumn("FileName");
        Assert.Equal(MsiColumnType.String, fileName.Type);
        Assert.True(fileName.IsLocalizable);
        Assert.False(fileName.IsPrimaryKey);

        Assert.True(file.GetColumn("File").IsPrimaryKey);
    }

    [Fact]
    public void BinaryColumnReadsStreamNamedTableAndKey()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        var row = db.GetTable("Binary").Rows[0];
        var expected = File.ReadAllBytes(TestFixtures.Path("expected/hello.bin"));

        var actual = row.GetBinary("Data");

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IndexerReturnsBinaryAsByteArray()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        Assert.IsType<byte[]>(db.GetTable("Binary").Rows[0]["Data"]);
    }

    [Fact]
    public void TypedAccessorRejectsWrongColumnType()
    {
        using var db = MsiDatabase.Open(TestFixtures.Path("basic.msi"));
        var row = db.GetTable("File").Rows[0];
        Assert.Throws<InvalidOperationException>(() => row.GetInt("FileName"));
        Assert.Throws<InvalidOperationException>(() => row.GetString("FileSize"));
    }
}
