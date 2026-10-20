namespace IsWiX.WindowsInstaller.Format;

using System.Text;
using IsWiX.WindowsInstaller.Format.Internal;

/// <summary>A table to be written: its columns, and its rows in column order.</summary>
internal sealed class MsiWriteTable
{
    public MsiWriteTable(string name, IReadOnlyList<MsiWriteColumn> columns, IReadOnlyList<object?[]> rows)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public string Name { get; }
    public IReadOnlyList<MsiWriteColumn> Columns { get; }

    /// <summary>
    /// Each row holds one value per column: a string, an int, or null. Binary columns take null; their
    /// data is written as a plain stream named Table.key1.key2 (see <see cref="MsiWriter.Write"/>).
    /// </summary>
    public IReadOnlyList<object?[]> Rows { get; }
}
