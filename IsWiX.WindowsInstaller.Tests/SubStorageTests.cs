using Xunit;
using IsWiX.WindowsInstaller.Format;
using IsWiX.WindowsInstaller.Format.Internal;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>
/// fixtures/substorage.cfb has a storage "Sub" holding streams A and B, and a root-level
/// stream "Top" that must not be copied.
/// </summary>
public class SubStorageTests
{
    [Fact]
    public void ExtractsOnlyTheStreamsInsideTheStorage()
    {
        string output = Path.GetTempFileName();
        try
        {
            Record.ExtractSubStorage(TestFixtures.Path("substorage.cfb"), "Sub", output);

            using var fs = File.OpenRead(output);
            var file = new CompoundFile(fs);
            var names = file.Streams.Select(s => s.RawName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "A", "B" }, names);

            var a = file.Streams.Single(s => s.RawName == "A");
            Assert.Equal(Enumerable.Repeat((byte)'a', 5000), file.ReadStream(a));
            var b = file.Streams.Single(s => s.RawName == "B");
            Assert.Equal(Enumerable.Repeat((byte)'b', 5000), file.ReadStream(b));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void MissingStorageIsReported()
    {
        string output = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<MsiFormatException>(() =>
                Record.ExtractSubStorage(TestFixtures.Path("substorage.cfb"), "Missing", output));
            Assert.True(ex.Message.Contains("Missing"));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
