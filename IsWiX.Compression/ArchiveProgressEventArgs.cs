namespace IsWiX.Compression
{
    using System;

    /// <summary>
    /// Contains the data reported in an archive progress event.
    /// </summary>
    public class ArchiveProgressEventArgs : EventArgs
    {
        public ArchiveProgressEventArgs(
            ArchiveProgressType progressType,
            string currentFileName,
            int currentFileNumber,
            int totalFiles,
            long currentFileBytesProcessed,
            long currentFileTotalBytes,
            string currentArchiveName,
            int currentArchiveNumber,
            int totalArchives,
            long currentArchiveBytesProcessed,
            long currentArchiveTotalBytes,
            long fileBytesProcessed,
            long totalFileBytes)
        {
            this.ProgressType = progressType;
            this.CurrentFileName = currentFileName;
            this.CurrentFileNumber = currentFileNumber;
            this.TotalFiles = totalFiles;
            this.CurrentFileBytesProcessed = currentFileBytesProcessed;
            this.CurrentFileTotalBytes = currentFileTotalBytes;
            this.CurrentArchiveName = currentArchiveName;
            this.CurrentArchiveNumber = currentArchiveNumber;
            this.TotalArchives = totalArchives;
            this.CurrentArchiveBytesProcessed = currentArchiveBytesProcessed;
            this.CurrentArchiveTotalBytes = currentArchiveTotalBytes;
            this.FileBytesProcessed = fileBytesProcessed;
            this.TotalFileBytes = totalFileBytes;
        }

        public ArchiveProgressType ProgressType { get; private set; }

        public string CurrentFileName { get; private set; }

        public int CurrentFileNumber { get; private set; }

        public int TotalFiles { get; private set; }

        public long CurrentFileBytesProcessed { get; private set; }

        public long CurrentFileTotalBytes { get; private set; }

        public string CurrentArchiveName { get; private set; }

        public int CurrentArchiveNumber { get; private set; }

        public int TotalArchives { get; private set; }

        public long CurrentArchiveBytesProcessed { get; private set; }

        public long CurrentArchiveTotalBytes { get; private set; }

        public long FileBytesProcessed { get; private set; }

        public long TotalFileBytes { get; private set; }

        public override string ToString()
        {
            return this.ProgressType + " " + this.CurrentFileName + " " + this.CurrentFileBytesProcessed + "/" + this.CurrentFileTotalBytes;
        }
    }
}
