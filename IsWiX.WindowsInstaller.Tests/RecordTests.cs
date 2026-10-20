using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of Record values, including the stream read path.</summary>
public class RecordTests
{
    [Fact]
    public void SetAndGetValuesInMemory()
    {
        using var record = new Record(2);
        record.SetString(1, "text");
        record.SetInteger(2, 7);

        Assert.Equal(2, record.FieldCount);
        Assert.Equal("text", record.GetString(1));
        Assert.Equal(7, record.GetInteger(2));
    }

    [Fact]
    public void ParamsConstructorStoresValuesInOrder()
    {
        using var record = new Record("first", 2);

        Assert.Equal("first", record.GetString(1));
        Assert.Equal(2, record.GetInteger(2));
    }

    [Fact]
    public void StreamReadReturnsWholeContent()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'hello'");
        view.Execute();
        using Record? row = view.Fetch();
        using Stream stream = row!.GetStream(1);
        using var content = new MemoryStream();
        stream.CopyTo(content);

        Assert.Equal(File.ReadAllBytes(TestFixtures.Path("hello.bin")), content.ToArray());
    }

    [Fact]
    public void StreamReadIntoOffsetFillsRequestedSlice()
    {
        using var db = new Database(TestFixtures.Path("basic.msi"));
        using var view = db.OpenView("SELECT `Data` FROM `Binary` WHERE `Name` = 'hello'");
        view.Execute();
        using Record? row = view.Fetch();
        using Stream stream = row!.GetStream(1);
        byte[] expected = File.ReadAllBytes(TestFixtures.Path("hello.bin"));

        byte[] first = new byte[5];
        Assert.Equal(5, stream.Read(first, 0, 5));

        // The second read must continue where the first stopped and land at the requested offset.
        byte[] second = new byte[10];
        Assert.Equal(7, stream.Read(second, 3, 7));

        Assert.Equal(expected.Take(5), first);
        Assert.Equal(expected.Skip(5).Take(7), second.Skip(3).Take(7));
    }
}
