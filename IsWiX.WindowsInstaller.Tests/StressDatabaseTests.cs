using System.Security.Cryptography;
using Xunit;
using IsWiX.WindowsInstaller.Format;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>
/// Shared fixture that opens the large generated database once. The file is made by
/// fixtures/generate.sh; if it is missing, these tests are skipped.
/// </summary>
public sealed class StressDatabase : IDisposable
{
    public const string Path = "generated/stress.msi";
    internal MsiDatabase? Db { get; }

    public StressDatabase()
    {
        var file = TestFixtures.Path(Path);
        if (File.Exists(file))
            Db = MsiDatabase.Open(file);
    }

    public void Dispose() => Db?.Dispose();
}

/// <summary>
/// Exercises paths the small fixture can't reach: 70,000 rows, 3-byte string
/// references, a string over 64 KB, a regular 9 MB stream that needs a DIFAT chain,
/// and a mini stream.
/// </summary>
public class StressDatabaseTests : IClassFixture<StressDatabase>
{
    private readonly StressDatabase _fixture;

    public StressDatabaseTests(StressDatabase fixture) => _fixture = fixture;

    private MsiDatabase _db
    {
        get
        {
            Skip.If(_fixture.Db is null, "Run fixtures/generate.sh to create generated/stress.msi.");
            return _fixture.Db!;
        }
    }

    [SkippableFact]
    public void ReadsAllRowsWithThreeByteStringReferences()
    {
        var t = _db.GetTable("Stress");
        Assert.Equal(70000, t.Rows.Count);
        Assert.Equal("k69999", t.Rows[69999].GetString("Id"));
        Assert.Equal("v69999", t.Rows[69999].GetString("Value"));
    }

    [SkippableFact]
    public void ReadsStringLongerThan64Kb()
    {
        Assert.Equal(70000, _db.GetTable("Stress").Rows[5].GetString("Value")!.Length);
    }

    [SkippableFact]
    public void ReadsNonAsciiString()
    {
        Assert.Equal("café ünï", _db.GetTable("Stress").Rows[6].GetString("Value"));
    }

    [SkippableFact]
    public void ReadsNegativeIntegers()
    {
        Assert.Equal(-42081, _db.GetTable("Stress").Rows[1].GetInt("Num"));
    }

    [SkippableFact]
    public void ReadsNineMegabyteStream()
    {
        var data = _db.GetTable("Blob").Rows[0].GetBinary("Data");
        var expected = File.ReadAllText(TestFixtures.Path("generated/expected_big_sha256.txt")).Trim();

        Assert.NotNull(data);
        Assert.Equal(9_000_000, data!.Length);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(data)));
    }

    [SkippableFact]
    public void ReadsSmallMiniStream()
    {
        var data = _db.GetTable("Blob").Rows[1].GetBinary("Data");
        Assert.Equal(new byte[] { 116, 105, 110, 121, 32, 112, 97, 121, 108, 111, 97, 100, 32, 0, 1, 2 }, data);
    }
}
