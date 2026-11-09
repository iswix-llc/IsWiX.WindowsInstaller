using System.Text;

namespace IsWiX.Compression.Cab.Tests;

public class CabInfoTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "iswix-cab-" + Guid.NewGuid().ToString("N"));

    public CabInfoTests()
    {
        Directory.CreateDirectory(this.temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.temp, true);
        }
        catch (IOException)
        {
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(this.temp, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ListsFilesInLzxCabinet()
    {
        var cab = new CabInfo(Fixture("lzx-window15.cab"));

        var file = Assert.Single(cab.GetFiles());

        Assert.Equal("test.txt", file.Name);
        Assert.Equal("test.txt", file.Path);
        Assert.Equal(17, file.Length);
        Assert.Equal(0, file.CabinetFolderNumber);
        Assert.Equal(cab.FullName, file.CabinetName);
        Assert.True(file.Exists);
    }

    [Fact]
    public void ReadsFileContentFromLzxCabinet()
    {
        var expected = File.ReadAllText(Fixture("test.txt"));
        var cab = new CabInfo(Fixture("lzx-window15.cab"));

        using (var text = cab.GetFiles().Single().OpenText())
        {
            Assert.Equal(expected, text.ReadToEnd());
        }

        using (var reader = cab.OpenText("test.txt"))
        {
            Assert.Equal(expected, reader.ReadToEnd());
        }
    }

    [Fact]
    public void ReadsLzxCabinet()
    {
        var cab = new CabInfo(Fixture("lzx-single.cab"));

        var file = Assert.Single(cab.GetFiles());

        Assert.Equal("filcV1yrx0x8wJWj4qMzcH21jwkPko", file.Name);
        Assert.Equal(17, file.Length);

        using (var stream = file.OpenRead())
        {
            Assert.Equal(17, stream.Length);
        }
    }

    [Fact]
    public void ReadsMszipCabinetWhoseSecondBlockUsesTheFirstBlockAsDictionary()
    {
        var expected = string.Concat(Enumerable.Repeat("hello world ", 100)) + string.Concat(Enumerable.Repeat("hello world ", 50)) + "tail";
        var cab = new CabInfo(Fixture("mszip-two-blocks.cab"));

        var file = Assert.Single(cab.GetFiles());

        Assert.Equal("m.txt", file.Name);
        Assert.Equal(expected.Length, file.Length);
        using (var text = file.OpenText())
        {
            Assert.Equal(expected, text.ReadToEnd());
        }
    }

    [Fact]
    public void UnpackExtractsAllFiles()
    {
        var cab = new CabInfo(Fixture("lzx-window15.cab"));
        var target = Path.Combine(this.temp, "out");

        cab.Unpack(target);

        Assert.Equal(File.ReadAllText(Fixture("test.txt")), File.ReadAllText(Path.Combine(target, "test.txt")));
    }

    [Fact]
    public void UnpackFileExtractsToTheGivenPath()
    {
        var cab = new CabInfo(Fixture("lzx-window15.cab"));
        var target = Path.Combine(this.temp, "renamed.bin");

        cab.UnpackFile("test.txt", target);

        Assert.Equal(File.ReadAllText(Fixture("test.txt")), File.ReadAllText(target));
    }

    [Fact]
    public void CopyToHonoursOverwrite()
    {
        var file = new CabInfo(Fixture("lzx-window15.cab")).GetFiles().Single();
        var target = Path.Combine(this.temp, "copy.txt");

        file.CopyTo(target);

        Assert.Throws<IOException>(() => file.CopyTo(target));
        file.CopyTo(target, true);
    }

    [Fact]
    public void UnpackFileThrowsForMissingFile()
    {
        var cab = new CabInfo(Fixture("lzx-window15.cab"));

        Assert.Throws<FileNotFoundException>(() => cab.UnpackFile("nope.txt", Path.Combine(this.temp, "x")));
    }

    [Fact]
    public void ListsAndExtractsSeveralFilesFromOneFolder()
    {
        var path = this.Write("two.cab", CabBuilder.Build(("a.txt", Encoding.ASCII.GetBytes("alpha")), ("b.txt", Encoding.ASCII.GetBytes("bravo!"))));
        var cab = new CabInfo(path);

        var files = cab.GetFiles();

        Assert.Equal(new[] { "a.txt", "b.txt" }, files.Select(f => f.Name).ToArray());
        Assert.Equal(new long[] { 5, 6 }, files.Select(f => f.Length).ToArray());
        Assert.Equal(new DateTime(2025, 1, 1, 12, 0, 0), files[0].LastWriteTime);
        using (var text = files[1].OpenText())
        {
            Assert.Equal("bravo!", text.ReadToEnd());
        }
    }

    [Fact]
    public void GetFilesWithPatternFiltersByFileName()
    {
        var path = this.Write("pattern.cab", CabBuilder.Build(("a.txt", new byte[1]), ("b.dat", new byte[2]), ("c.TXT", new byte[3])));

        var files = new CabInfo(path).GetFiles("*.txt");

        Assert.Equal(new[] { "a.txt", "c.TXT" }, files.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void IsValidRecognisesCabinets()
    {
        Assert.True(new CabInfo(Fixture("lzx-single.cab")).IsValid());
        Assert.False(new CabInfo(Fixture("test.txt")).IsValid());
        Assert.False(new CabInfo(Path.Combine(this.temp, "missing.cab")).IsValid());
    }

    [Fact]
    public void RejectsFilesThatEscapeTheDestinationDirectory()
    {
        var path = this.Write("evil.cab", CabBuilder.Build(("..\\evil.txt", Encoding.ASCII.GetBytes("x"))));
        var target = Path.Combine(this.temp, "out");

        Assert.Throws<ArchiveException>(() => new CabInfo(path).Unpack(target));
        Assert.False(File.Exists(Path.Combine(this.temp, "evil.txt")));
    }

    [Fact]
    public void ThrowsCabExceptionForNonCabinets()
    {
        var path = this.Write("junk.cab", Encoding.ASCII.GetBytes("this is not a cabinet file at all, not even close to one"));

        Assert.Throws<CabException>(() => new CabInfo(path).GetFiles());
    }

    [Fact]
    public void ThrowsCabExceptionForTruncatedCabinets()
    {
        var bytes = CabBuilder.Build(("a.txt", new byte[100]));
        var path = this.Write("short.cab", bytes.Take(bytes.Length - 50).ToArray());

        Assert.Throws<CabException>(() => new CabInfo(path).Unpack(Path.Combine(this.temp, "out")));
    }

    [Fact]
    public void PackIsNotImplemented()
    {
        Assert.Throws<NotSupportedException>(() => new CabInfo(Path.Combine(this.temp, "new.cab")).Pack(this.temp));
    }

    [Fact]
    public void DosDateAndTimeRoundTrips()
    {
        var original = new DateTime(2024, 6, 30, 13, 45, 22);

        CompressionEngine.DateTimeToDosDateAndTime(original, out var date, out var time);
        CompressionEngine.DosDateAndTimeToDateTime(date, time, out var result);

        Assert.Equal(original, result);
    }
}
