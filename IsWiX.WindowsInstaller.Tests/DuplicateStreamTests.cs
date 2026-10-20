using Xunit;
using IsWiX.WindowsInstaller.Format;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>
/// The WinDirStat installer contains more than one stream named _StringPool. The reader
/// must pick the same copy msitools reads, or the tables can't be decoded.
/// </summary>
public class DuplicateStreamTests
{
    [SkippableFact]
    public void OpensFileWithDuplicateStringPoolStreams()
    {
        string path = TestFixtures.Path("WinDirStat-2.9.3-x64.msi");
        Skip.If(!File.Exists(path), "WinDirStat fixture is not present.");

        using var db = MsiDatabase.Open(path);
        Assert.Equal(37, db.TableNames.Count);

        var property = db.GetTable("Property");
        Assert.Equal(21, property.Rows.Count);
        Assert.Equal("WinDirStat 2.9.3 (x64)",
            property.Rows.Single(r => r.GetString("Property") == "ARPCOMMENTS").GetString("Value"));

        Assert.Equal(1, db.GetTable("File").Rows.Count);
    }
}
