namespace IsWiX.WindowsInstaller.Tests;

internal static class TestFixtures
{
    /// <summary>The path of a fixture file copied next to the test assembly.</summary>
    public static string Path(string name)
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    }
}
