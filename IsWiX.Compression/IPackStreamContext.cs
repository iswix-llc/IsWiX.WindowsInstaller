namespace IsWiX.Compression
{
    using System;
    using System.IO;

    /// <summary>
    /// Provides the streams a packing operation reads files from and writes archives to.
    /// </summary>
    public interface IPackStreamContext
    {
        string GetArchiveName(int archiveNumber);

        Stream OpenArchiveWriteStream(int archiveNumber, string archiveName, bool truncate, CompressionEngine compressionEngine);

        void CloseArchiveWriteStream(int archiveNumber, string archiveName, Stream stream);

        Stream OpenFileReadStream(string path, out FileAttributes attributes, out DateTime lastWriteTime);

        void CloseFileReadStream(string path, Stream stream);

        object GetOption(string optionName, object[] parameters);
    }
}
