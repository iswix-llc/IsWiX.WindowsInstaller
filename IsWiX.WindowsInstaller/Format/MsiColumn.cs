namespace IsWiX.WindowsInstaller.Format;

/// <summary>The kind of value a column holds.</summary>
internal enum MsiColumnType
{
    /// <summary>A 16- or 32-bit integer.</summary>
    Integer,
    /// <summary>A string, read from the string pool.</summary>
    String,
    /// <summary>A binary stream stored in its own compound-file stream.</summary>
    Binary,
}

/// <summary>Describes one column of an MSI table.</summary>
internal sealed class MsiColumn
{
    internal MsiColumn(string table, int number, string name, int rawType)
    {
        Table = table;
        Number = number;
        Name = name;
        RawType = rawType;

        bool isString = (rawType & 0x0800) != 0;
        if (isString && (rawType & ~0x1000) == (0x0800 | 0x0100))
        {
            Type = MsiColumnType.Binary;
            Size = 0;
        }
        else if (isString)
        {
            Type = MsiColumnType.String;
            Size = rawType & 0xFF;
        }
        else
        {
            Type = MsiColumnType.Integer;
            Size = (rawType & 0xFF) <= 2 ? 2 : 4;
        }

        IsNullable = (rawType & 0x1000) != 0;
        IsPrimaryKey = (rawType & 0x2000) != 0;
        IsLocalizable = (rawType & 0x0200) != 0;
    }

    /// <summary>Name of the table this column belongs to.</summary>
    public string Table { get; }

    /// <summary>One-based position of the column within its table.</summary>
    public int Number { get; }

    /// <summary>Column name.</summary>
    public string Name { get; }

    /// <summary>The kind of value this column holds.</summary>
    public MsiColumnType Type { get; }

    /// <summary>Maximum length for strings, or the width in bytes for integers.</summary>
    public int Size { get; }

    public bool IsNullable { get; }
    public bool IsPrimaryKey { get; }
    public bool IsLocalizable { get; }

    /// <summary>The raw type word from the _Columns table, after removing the storage bias.</summary>
    public int RawType { get; }

    /// <summary>Bytes used for this column in the stored table stream.</summary>
    internal int StorageWidth(int bytesPerStringRef) => Type switch
    {
        MsiColumnType.String => bytesPerStringRef,
        MsiColumnType.Binary => 2,
        _ => Size,
    };

    public override string ToString() => $"{Table}.{Name} ({Type})";
}
