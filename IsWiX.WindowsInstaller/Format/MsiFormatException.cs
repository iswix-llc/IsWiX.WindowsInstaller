namespace IsWiX.WindowsInstaller.Format;

/// <summary>Thrown when a file is not a valid or supported Windows Installer database.</summary>
internal sealed class MsiFormatException : Exception
{
    public MsiFormatException(string message) : base(message) { }
    public MsiFormatException(string message, Exception inner) : base(message, inner) { }
}
