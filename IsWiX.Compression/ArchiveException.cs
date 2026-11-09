namespace IsWiX.Compression
{
    using System;
    using System.IO;

    /// <summary>
    /// Base exception class for archive operations.
    /// </summary>
    public class ArchiveException : IOException
    {
        public ArchiveException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public ArchiveException(string message)
            : this(message, null)
        {
        }

        public ArchiveException()
            : this(null, null)
        {
        }
    }
}
