namespace IsWiX.Compression
{
    /// <summary>
    /// Specifies the compression level for packing an archive. The numeric values match WixToolset.Dtf.Compression.
    /// </summary>
    public enum CompressionLevel
    {
        /// <summary>Store the files without compression.</summary>
        None = 0,

        /// <summary>Use the fastest compression.</summary>
        Min = 1,

        /// <summary>Use a balance of speed and size.</summary>
        Normal = 6,

        /// <summary>Use the smallest output.</summary>
        Max = 10,
    }
}
