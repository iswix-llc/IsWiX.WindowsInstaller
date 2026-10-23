using System.Text;
using Xunit;
using IsWiX.WindowsInstaller.Format;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Round-trips databases written by <see cref="MsiWriter"/> back through <see cref="MsiDatabase"/>.</summary>
public class MsiWriterTests
{
    private static MsiWriteTable PropertyTable() => new(
        "Property",
        new[]
        {
            new MsiWriteColumn("Property", MsiColumnType.String, 72, isPrimaryKey: true),
            new MsiWriteColumn("Value", MsiColumnType.String, 0, isNullable: true, isLocalizable: true),
        },
        new List<object?[]>
        {
            new object?[] { "ProductName", "Contoso Widget" },
            new object?[] { "Manufacturer", "Zoë Fabrikam GmbH" },
            new object?[] { "Empty", null },
        });

    private static MsiWriteTable ComponentTable() => new(
        "Component",
        new[]
        {
            new MsiWriteColumn("Component", MsiColumnType.String, 72, isPrimaryKey: true),
            new MsiWriteColumn("Attributes", MsiColumnType.Integer, 2),
            new MsiWriteColumn("Size", MsiColumnType.Integer, 4, isNullable: true),
            new MsiWriteColumn("Data", MsiColumnType.Binary, 0, isNullable: true),
        },
        new List<object?[]>
        {
            new object?[] { "C1", 0, -5, null },
            new object?[] { "C2", -32767, null, null },
            new object?[] { "C3", 32767, 70000, null },
        });

    private static MsiDatabase Write(IReadOnlyList<MsiWriteTable> tables, IReadOnlyDictionary<string, byte[]>? streams = null)
    {
        var stream = new MemoryStream();
        MsiWriter.Write(stream, 1252, tables, streams);
        stream.Position = 0;
        return MsiDatabase.Open(stream, leaveOpen: false);
    }

    [Fact]
    public void RoundTripsStringsIntegersAndNulls()
    {
        using var db = Write(new[] { PropertyTable(), ComponentTable() });

        Assert.Equal(new[] { "Property", "Component" }, db.TableNames.Where(n => n is "Property" or "Component"));

        var property = db.GetTable("Property");
        Assert.Equal(3, property.Rows.Count);
        Assert.Equal("Contoso Widget", property.Rows[0].GetString("Value"));
        Assert.Equal("Zoë Fabrikam GmbH", property.Rows[1].GetString("Value"));
        Assert.Null(property.Rows[2].GetString("Value"));

        var component = db.GetTable("Component");
        Assert.Equal(-5, component.Rows[0]["Size"]);
        Assert.Null(component.Rows[1]["Size"]);
        Assert.Equal(-32767, component.Rows[1]["Attributes"]);
        Assert.Equal(32767, component.Rows[2]["Attributes"]);
        Assert.Equal(70000, component.Rows[2]["Size"]);
    }

    [Fact]
    public void RoundTripsBinaryAndPlainStreams()
    {
        var streams = new Dictionary<string, byte[]>
        {
            ["Component.C1"] = Encoding.ASCII.GetBytes("binary-one"),
            ["hello.txt"] = Encoding.ASCII.GetBytes("plain stream"),
        };
        using var db = Write(new[] { ComponentTable() }, streams);

        var component = db.GetTable("Component");
        Assert.Equal("binary-one", Encoding.ASCII.GetString(component.Rows[0].GetBinary("Data")!));
        Assert.Null(component.Rows[1].GetBinary("Data"));
        Assert.Equal("plain stream", Encoding.ASCII.GetString(db.ReadStream("hello.txt")!));
    }

    [Fact]
    public void LargeDatabaseNeedsDifatSectorsAndStillRoundTrips()
    {
        // Over 109 FAT sectors (about 7 MB) forces DIFAT chaining in the compound file.
        var big = new byte[7 * 1024 * 1024];
        new Random(7).NextBytes(big);

        var rows = new List<object?[]>();
        for (int i = 0; i < 1000; i++)
            rows.Add(new object?[] { "K" + i, i - 500, i * 1000, null });
        var table = new MsiWriteTable("Component", ComponentTable().Columns, rows);

        using var db = Write(new[] { table }, new Dictionary<string, byte[]> { ["big.bin"] = big, ["tiny.bin"] = new byte[] { 1, 2, 3 } });

        Assert.True(db.ReadStream("big.bin")!.AsSpan().SequenceEqual(big));
        Assert.True(db.ReadStream("tiny.bin")!.AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }));
        var component = db.GetTable("Component");
        Assert.Equal(1000, component.Rows.Count);
        Assert.Equal(-500, component.Rows[0]["Attributes"]);
        Assert.Equal("K999", component.Rows[999].GetString("Component"));
    }

    [Fact]
    public void TableWithoutPrimaryKeyIsRejected()
    {
        var table = new MsiWriteTable("Bad",
            new[] { new MsiWriteColumn("A", MsiColumnType.String, 10) },
            new List<object?[]>());
        Assert.Throws<ArgumentException>(() => Write(new[] { table }));
    }

    [Fact]
    public void GeneratedSystemTableNamesAreReserved()
    {
        var table = new MsiWriteTable("_Columns",
            new[] { new MsiWriteColumn("A", MsiColumnType.String, 10, isPrimaryKey: true) },
            new List<object?[]>());
        Assert.Throws<ArgumentException>(() => Write(new[] { table }));
    }

    [Fact]
    public void OtherUnderscoreTablesAreOrdinary()
    {
        // Real installers contain _Validation and similar tables.
        var table = new MsiWriteTable("_Validation",
            new[] { new MsiWriteColumn("Table", MsiColumnType.String, 32, isPrimaryKey: true) },
            new List<object?[]> { new object?[] { "Property" } });
        using var db = Write(new[] { table });
        Assert.Equal("Property", db.GetTable("_Validation").Rows[0].GetString("Table"));
    }

    [Fact]
    public void IntegerOutsideTheColumnRangeIsRejected()
    {
        var table = new MsiWriteTable("T",
            new[]
            {
                new MsiWriteColumn("K", MsiColumnType.String, 10, isPrimaryKey: true),
                new MsiWriteColumn("N", MsiColumnType.Integer, 2),
            },
            new List<object?[]> { new object?[] { "a", 40000 } });
        Assert.Throws<ArgumentException>(() => Write(new[] { table }));
    }

    [Fact]
    public void RowWithWrongColumnCountIsRejected()
    {
        var table = new MsiWriteTable("T",
            new[]
            {
                new MsiWriteColumn("K", MsiColumnType.String, 10, isPrimaryKey: true),
                new MsiWriteColumn("N", MsiColumnType.Integer, 2),
            },
            new List<object?[]> { new object?[] { "a" } });
        Assert.Throws<ArgumentException>(() => Write(new[] { table }));
    }

    [Fact]
    public void WritesStringsLongerThanTheirColumnSize()
    {
        // Packages in the wild can hold values longer than the declared size (a File.Language list, say).
        var table = new MsiWriteTable(
            "File",
            new[]
            {
                new MsiWriteColumn("File", MsiColumnType.String, 72, isPrimaryKey: true),
                new MsiWriteColumn("Language", MsiColumnType.String, 20, isNullable: true),
            },
            new List<object?[]> { new object?[] { "F1", "1033,1031,1036,1040,1041,1042,1049,2052" } });

        using var db = Write(new[] { table });

        Assert.Equal("1033,1031,1036,1040,1041,1042,1049,2052", db.GetTable("File").Rows[0].GetString("Language"));
    }
}
