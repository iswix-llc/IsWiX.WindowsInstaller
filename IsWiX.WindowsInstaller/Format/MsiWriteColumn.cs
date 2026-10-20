namespace IsWiX.WindowsInstaller.Format;

using System.Text;
using IsWiX.WindowsInstaller.Format.Internal;

/// <summary>Describes one column of a table to be written.</summary>
internal sealed class MsiWriteColumn
{
    /// <summary>
    /// Creates a column. For integer columns, <paramref name="size"/> must be 2 or 4. For string columns it is
    /// the maximum length, with 0 meaning unlimited. Binary columns ignore the size.
    /// </summary>
    public MsiWriteColumn(string name, MsiColumnType type, int size, bool isNullable = false, bool isPrimaryKey = false, bool isLocalizable = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Size = type == MsiColumnType.Integer ? (size == 2 ? 2 : 4) : size;
        IsNullable = isNullable;
        IsPrimaryKey = isPrimaryKey;
        IsLocalizable = isLocalizable && type == MsiColumnType.String;
    }

    public string Name { get; }
    public MsiColumnType Type { get; }
    public int Size { get; }
    public bool IsNullable { get; }
    public bool IsPrimaryKey { get; }
    public bool IsLocalizable { get; }
}
