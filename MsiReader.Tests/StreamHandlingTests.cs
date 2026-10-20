using Xunit;

namespace MsiReader.Tests;

public class StreamHandlingTests
{
    [Fact]
    public void OpensFromNonSeekableStream()
    {
        using var raw = File.OpenRead(TestFixtures.Path("basic.msi"));
        using var forward = new ForwardOnlyStream(raw);
        using var db = MsiDatabase.Open(forward);
        Assert.Equal(3, db.GetTable("Property").Rows.Count);
    }

    [Fact]
    public void LeaveOpenKeepsCallerStreamOpen()
    {
        var ms = new MemoryStream(File.ReadAllBytes(TestFixtures.Path("basic.msi")));
        using (var db = MsiDatabase.Open(ms, leaveOpen: true))
        {
            _ = db.TableNames;
        }
        Assert.True(ms.CanRead);
    }

    [Fact]
    public void NonMsiDataThrowsFormatException()
    {
        Assert.Throws<MsiFormatException>(() => MsiDatabase.Open(new MemoryStream(new byte[600])));
    }

    [Fact]
    public void TruncatedFileThrowsFormatException()
    {
        var bytes = File.ReadAllBytes(TestFixtures.Path("basic.msi"));
        var truncated = new MemoryStream(bytes[..(bytes.Length / 2)]);

        Assert.Throws<MsiFormatException>(() =>
        {
            using var db = MsiDatabase.Open(truncated);
            _ = db.GetTable("File").Rows.Count;
        });
    }
}
