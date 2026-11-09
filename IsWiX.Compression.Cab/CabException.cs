namespace IsWiX.Compression.Cab
{
    using System;
    using IsWiX.Compression;

    /// <summary>
    /// Exception thrown when a cabinet cannot be read.
    /// </summary>
    public class CabException : ArchiveException
    {
        public CabException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public CabException(string message)
            : this(message, null)
        {
        }

        public CabException()
            : this(null, null)
        {
        }
    }
}
