using System.Text;
using IsWiX.Compression;

namespace IsWiX.Compression.Cab.Tests;

public class CabPackTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "iswix-cabpack-" + Guid.NewGuid().ToString("N"));

    public CabPackTests()
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

    private static byte[] Text(int length)
    {
        var words = new[] { "installer", "package", "component", "feature", "directory", "property", "custom", "action", "sequence", "table" };
        var random = new Random(7);
        var builder = new StringBuilder();
        while (builder.Length < length)
        {
            builder.Append(words[random.Next(words.Length)]).Append(random.Next(4) == 0 ? "\r\n" : " ");
        }

        return Encoding.ASCII.GetBytes(builder.ToString(0, length));
    }

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Mixed(int length)
    {
        var result = new byte[length];
        var text = Text(length);
        var noise = Random(length, 3);
        for (var i = 0; i < length; i++)
        {
            result[i] = (i / 5000) % 2 == 0 ? text[i] : noise[i];
        }

        return result;
    }

    private string Source(params (string Name, byte[] Data)[] files)
    {
        var folder = Path.Combine(this.temp, "src" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Data);
        }

        return folder;
    }

    private void RoundTrip(CompressionLevel level, params (string Name, byte[] Data)[] files)
    {
        var folder = this.Source(files);
        var cab = new CabInfo(Path.Combine(this.temp, "out" + Guid.NewGuid().ToString("N") + ".cab"));
        cab.Pack(folder, true, level);

        Assert.True(cab.Exists);
        var listed = cab.GetFiles();
        Assert.Equal(files.Length, listed.Count);
        foreach (var file in files)
        {
            using var input = cab.OpenRead(file.Name);
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            Assert.True(file.Data.AsSpan().SequenceEqual(copy.ToArray()), file.Name + " differs at level " + level);
        }
    }

    [Theory]
    [InlineData(CompressionLevel.None)]
    [InlineData(CompressionLevel.Min)]
    [InlineData(CompressionLevel.Normal)]
    [InlineData(CompressionLevel.Max)]
    public void PackedCabinetReadsBackAtEveryLevel(CompressionLevel level)
    {
        this.RoundTrip(level, ("a.txt", Text(100000)), ("b.bin", Random(70000, 1)), ("c.mix", Mixed(90000)), ("tiny.txt", Encoding.ASCII.GetBytes("hello")), ("empty.txt", Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(CompressionLevel.Min)]
    [InlineData(CompressionLevel.Normal)]
    [InlineData(CompressionLevel.Max)]
    public void FarRepeatsAcrossFramesRoundTrip(CompressionLevel level)
    {
        var chunk = Random(20000, 11);
        var data = new byte[0].Concat(chunk).Concat(Random(150000, 12)).Concat(chunk).Concat(chunk).ToArray();
        this.RoundTrip(level, ("repeat.bin", data));
    }

    private static byte[] CodeLike(int length)
    {
        // Calls (E8 plus a relative operand), operands that are out of range for translation, and repeating filler.
        var random = new Random(21);
        var data = new byte[length];
        var i = 0;
        while (i < length - 8)
        {
            var kind = random.Next(6);
            if (kind < 3)
            {
                data[i++] = 0xe8;
                var relative = random.Next(-200000, 400000);
                if (kind == 0)
                {
                    relative = random.Next(12000000, 12100000);
                }

                data[i++] = (byte)relative;
                data[i++] = (byte)(relative >> 8);
                data[i++] = (byte)(relative >> 16);
                data[i++] = (byte)(relative >> 24);
            }
            else
            {
                for (var k = 0; k < 4 && i < length; k++)
                {
                    data[i++] = (byte)(0x40 + random.Next(6));
                }
            }
        }

        return data;
    }

    [Theory]
    [InlineData(CompressionLevel.Normal)]
    [InlineData(CompressionLevel.Max)]
    public void CodeWithCallInstructionsRoundTrips(CompressionLevel level)
    {
        var code = CodeLike(400000);
        this.RoundTrip(level, ("code.exe", code), ("again.exe", code), ("small.dll", CodeLike(9)), ("text.txt", Text(40000)));
    }

    [Fact]
    public void SubdirectoriesKeepTheirRelativePaths()
    {
        this.RoundTrip(CompressionLevel.Normal, ("top.txt", Text(500)), ("sub\\inner.txt", Text(900)));
    }

    [Fact]
    public void LzxIsSmallerThanMszipOnText()
    {
        var folder = this.Source(("a.txt", Text(300000)));
        var lzx = new CabInfo(Path.Combine(this.temp, "lzx.cab"));
        var mszip = new CabInfo(Path.Combine(this.temp, "mszip.cab"));
        lzx.Pack(folder, false, CompressionLevel.Max);
        mszip.Pack(folder, false, CompressionLevel.Min);

        Assert.True(lzx.Length < mszip.Length, "lzx " + lzx.Length + " mszip " + mszip.Length);
    }

    [Fact]
    public void WindowsCabinetCodeCanExtractWhatWasPacked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Checked by reading the header back: signature, version and the LZX folder type value.
        var folder = this.Source(("a.txt", Text(50000)));
        var cab = new CabInfo(Path.Combine(this.temp, "header.cab"));
        cab.Pack(folder, false, CompressionLevel.Max);

        var bytes = File.ReadAllBytes(cab.FullName);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal(1, bytes[25]);
        Assert.Equal(3, bytes[24]);
        var typeCompress = BitConverter.ToUInt16(bytes, 36 + 6);
        Assert.Equal(3 | (21 << 8), typeCompress);
    }

    [Fact]
    public void MaxArchiveSizeSplitsIntoContinuationCabinets()
    {
        var folder = this.Source(("a.bin", Random(300000, 5)));
        var path = Path.Combine(this.temp, "split.cab");
        var context = new FolderContext(path, folder);
        using (var engine = new CabEngine { CompressionLevel = CompressionLevel.None })
        {
            engine.Pack(context, new[] { "a.bin" }, 100000);
        }

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(this.temp, "splita.cab")));
        Assert.True(File.Exists(Path.Combine(this.temp, "splitb.cab")));
        var first = File.ReadAllBytes(path);
        Assert.Equal(2, BitConverter.ToUInt16(first, 30) & 2);   // has a next cabinet
        var second = File.ReadAllBytes(Path.Combine(this.temp, "splita.cab"));
        Assert.Equal(1, BitConverter.ToUInt16(second, 30) & 1);  // has a previous cabinet
        Assert.Equal(1, BitConverter.ToUInt16(second, 34));      // cabinet number 1
    }

    [Fact]
    public void FileAttributesAndNamesArePreserved()
    {
        var folder = this.Source(("ünï.txt", Text(300)));
        var cab = new CabInfo(Path.Combine(this.temp, "utf.cab"));
        cab.Pack(folder, false, CompressionLevel.Normal);

        var file = Assert.Single(cab.GetFiles());
        Assert.Equal("ünï.txt", file.Name);
        Assert.Equal(300, file.Length);
    }

    private sealed class FolderContext : IPackStreamContext
    {
        private readonly string archive;
        private readonly string folder;

        public FolderContext(string archive, string folder)
        {
            this.archive = archive;
            this.folder = folder;
        }

        public string GetArchiveName(int archiveNumber)
        {
            var stem = Path.GetFileNameWithoutExtension(this.archive);
            return archiveNumber == 0 ? stem + ".cab" : stem + (char)('a' - 1 + archiveNumber) + ".cab";
        }

        public Stream OpenArchiveWriteStream(int archiveNumber, string archiveName, bool truncate, CompressionEngine compressionEngine)
            => new FileStream(Path.Combine(Path.GetDirectoryName(this.archive)!, archiveName), FileMode.Create, FileAccess.Write);

        public void CloseArchiveWriteStream(int archiveNumber, string archiveName, Stream stream) => stream.Dispose();

        public Stream OpenFileReadStream(string path, out FileAttributes attributes, out DateTime lastWriteTime)
        {
            var full = Path.Combine(this.folder, path);
            attributes = File.GetAttributes(full);
            lastWriteTime = File.GetLastWriteTime(full);
            return File.OpenRead(full);
        }

        public void CloseFileReadStream(string path, Stream stream) => stream.Dispose();

        public object GetOption(string optionName, object[] parameters) => null;
    }
}
