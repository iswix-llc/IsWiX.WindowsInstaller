namespace IsWiX.Compression
{
    using System;
    using System.IO;

    /// <summary>
    /// Provides the streams an unpacking operation reads archives from and writes files to.
    /// </summary>
    public interface IUnpackStreamContext
    {
        /// <summary>Opens the archive stream for reading. Returns null if the archive number does not exist.</summary>
        Stream OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine);

        void CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream);

        /// <summary>Opens a stream for writing an extracted file. Returning null skips the file.</summary>
        Stream OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime);

        void CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime);
    }
}
