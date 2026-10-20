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
