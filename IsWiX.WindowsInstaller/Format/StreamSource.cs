namespace IsWiX.WindowsInstaller.Format;

using System.Text;
using IsWiX.WindowsInstaller.Format.Internal;

/// <summary>A stream to write: its length, and a function that returns its bytes when the writer needs them.</summary>
internal readonly record struct StreamSource(long Length, Func<byte[]> Read)
{
    public static StreamSource Of(byte[] data) => new StreamSource(data.Length, () => data);
}
