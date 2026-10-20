using Xunit;

namespace IsWiX.WindowsInstaller.Tests;

/// <summary>Tests of summary information, read and written through the SummaryInfo wrapper.</summary>
public class SummaryTests : IDisposable
{
    private readonly string folder = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public SummaryTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
    }

    private string CopyOfFixture()
    {
        string copy = Path.Combine(folder, "basic.msi");
        File.Copy(TestFixtures.Path("basic.msi"), copy);
        return copy;
    }

    [Fact]
    public void ReadsFixtureProperties()
    {
        using var summary = new SummaryInfo(TestFixtures.Path("basic.msi"), false);

        Assert.Equal("Installation Database", summary.Title);
        Assert.Equal("Installer, MSI", summary.Keywords);
        Assert.Equal(";1033", summary.Template);
        Assert.Equal(0, (int) summary.CodePage); // neutral: strings decode as Windows-1252
    }

    [Fact]
    public void PersistChangesOneProperty()
    {
        string path = CopyOfFixture();
        using (var summary = new SummaryInfo(path, true))
        {
            summary.Title = "Changed title";
            summary.Persist();
        }

        using var reopened = new SummaryInfo(path, false);
        Assert.Equal("Changed title", reopened.Title);
        Assert.Equal("Installer, MSI", reopened.Keywords);
        Assert.Equal(";1033", reopened.Template);
        Assert.Equal(1252, (int) reopened.CodePage); // written explicitly on persist
    }

    [Fact]
    public void NonAsciiTextRoundTripsInCodePage1252()
    {
        string path = CopyOfFixture();
        using (var summary = new SummaryInfo(path, true))
        {
            summary.Comments = "Zoë café";
            summary.Persist();
        }

        using var reopened = new SummaryInfo(path, false);
        Assert.Equal("Zoë café", reopened.Comments);
    }

    [Fact]
    public void DateRoundTrips()
    {
        string path = CopyOfFixture();
        var when = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        using (var summary = new SummaryInfo(path, true))
        {
            summary.CreateTime = when;
            summary.Persist();
        }

        using var reopened = new SummaryInfo(path, false);
        Assert.Equal(when.ToFileTime(), reopened.CreateTime.ToFileTime());
    }

    [Fact]
    public void ReadOnlyHandleCannotBeChanged()
    {
        using var summary = new SummaryInfo(TestFixtures.Path("basic.msi"), false);

        Assert.ThrowsAny<InstallerException>(() => summary.Title = "Nope");
    }
}
