namespace IsWiX.Compression
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Base class for an engine that can pack files into an archive and unpack them again.
    /// </summary>
    public abstract class CompressionEngine : IDisposable
    {
        private CompressionLevel compressionLevel;
        private bool dontUseTempFiles;

        protected CompressionEngine()
        {
            this.compressionLevel = CompressionLevel.Normal;
        }

        /// <summary>Reports progress of packing and unpacking.</summary>
        public event EventHandler<ArchiveProgressEventArgs> Progress;

        /// <summary>
        /// Gets or sets whether temporary files may be used while packing. Managed engines may ignore it.
        /// </summary>
        public bool UseTempFiles
        {
            get { return !this.dontUseTempFiles; }
            set { this.dontUseTempFiles = !value; }
        }

        /// <summary>Gets or sets the compression level used when packing.</summary>
        public CompressionLevel CompressionLevel
        {
            get { return this.compressionLevel; }
            set { this.compressionLevel = value; }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Pack(IPackStreamContext streamContext, IEnumerable<string> files)
        {
            if (files == null)
            {
                throw new ArgumentNullException("files");
            }

            this.Pack(streamContext, files, 0);
        }

        /// <summary>Packs files into one archive, or an archive chain when a maximum size is given.</summary>
        public abstract void Pack(IPackStreamContext streamContext, IEnumerable<string> files, long maxArchiveSize);

        /// <summary>Checks whether a stream begins with an archive header (no offset).</summary>
        public abstract bool IsArchive(Stream stream);

        /// <summary>Finds the start of an archive within a stream; -1 if there is none.</summary>
        public virtual long FindArchiveOffset(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            long sectionSize = 4096;
            var length = stream.Length;
            for (long offset = 0; offset < length; offset += sectionSize)
            {
                stream.Seek(offset, SeekOrigin.Begin);
                if (this.IsArchive(stream))
                {
                    return offset;
                }
            }

            return -1;
        }

        public IList<ArchiveFileInfo> GetFileInfo(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return this.GetFileInfo(new SingleStreamContext(stream), null);
        }

        public abstract IList<ArchiveFileInfo> GetFileInfo(IUnpackStreamContext streamContext, Predicate<string> fileFilter);

        public IList<string> GetFiles(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return this.GetFiles(new SingleStreamContext(stream), null);
        }

        public IList<string> GetFiles(IUnpackStreamContext streamContext, Predicate<string> fileFilter)
        {
            if (streamContext == null)
            {
                throw new ArgumentNullException("streamContext");
            }

            var files = new List<string>();
            foreach (var info in this.GetFileInfo(streamContext, fileFilter))
            {
                files.Add(info.Path);
            }

            return files;
        }

        /// <summary>Unpacks the first file in the archive stream and returns a stream positioned at its start.</summary>
        public Stream Unpack(Stream stream, string path)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (path == null)
            {
                throw new ArgumentNullException("path");
            }

            var context = new SingleFileContext(stream);
            this.Unpack(context, name => String.Equals(name, path, StringComparison.OrdinalIgnoreCase));
            if (context.Result == null)
            {
                throw new FileNotFoundException("The file was not found in the archive.", path);
            }

            context.Result.Position = 0;
            return context.Result;
        }

        public abstract void Unpack(IUnpackStreamContext streamContext, Predicate<string> fileFilter);

        /// <summary>Converts a DOS date and time, as stored in archives, to a DateTime.</summary>
        public static void DosDateAndTimeToDateTime(short dosDate, short dosTime, out DateTime dateTime)
        {
            var date = (ushort)dosDate;
            var time = (ushort)dosTime;
            if (date == 0 && time == 0)
            {
                dateTime = DateTime.MinValue;
                return;
            }

            var year = 1980 + ((date >> 9) & 0x7f);
            var month = Math.Max(1, Math.Min(12, (date >> 5) & 0xf));
            var day = Math.Max(1, Math.Min(DateTime.DaysInMonth(year, month), date & 0x1f));
            var hour = Math.Min(23, (time >> 11) & 0x1f);
            var minute = Math.Min(59, (time >> 5) & 0x3f);
            var second = Math.Min(59, (time & 0x1f) * 2);
            dateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        }

        /// <summary>Converts a DateTime to the DOS date and time stored in archives.</summary>
        public static void DateTimeToDosDateAndTime(DateTime dateTime, out short dosDate, out short dosTime)
        {
            if (dateTime.Year < 1980)
            {
                dosDate = 0;
                dosTime = 0;
                return;
            }

            dosDate = unchecked((short)(((dateTime.Year - 1980) << 9) | (dateTime.Month << 5) | dateTime.Day));
            dosTime = unchecked((short)((dateTime.Hour << 11) | (dateTime.Minute << 5) | (dateTime.Second / 2)));
        }

        protected void OnProgress(ArchiveProgressEventArgs e)
        {
            var handler = this.Progress;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>Context over one caller-owned archive stream; the stream is not closed.</summary>
        private sealed class SingleStreamContext : IUnpackStreamContext
        {
            private readonly Stream archive;

            public SingleStreamContext(Stream archive)
            {
                this.archive = archive;
            }

            public Stream OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine)
            {
                return archiveNumber == 0 ? this.archive : null;
            }

            public void CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream)
            {
            }

            public Stream OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime)
            {
                return null;
            }

            public void CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime)
            {
            }
        }

        private sealed class SingleFileContext : IUnpackStreamContext
        {
            private readonly Stream archive;

            public SingleFileContext(Stream archive)
            {
                this.archive = archive;
            }

            public Stream Result { get; private set; }

            public Stream OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine)
            {
                return archiveNumber == 0 ? this.archive : null;
            }

            public void CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream)
            {
            }

            public Stream OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime)
            {
                return new MemoryStream();
            }

            public void CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime)
            {
                this.Result = stream;
            }
        }
    }
}
