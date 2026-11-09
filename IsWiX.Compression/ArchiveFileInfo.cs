namespace IsWiX.Compression
{
    using System;
    using System.IO;

    /// <summary>
    /// Abstract object representing a compressed file within an archive; like a FileInfo object for a file in an archive.
    /// </summary>
    public abstract class ArchiveFileInfo : FileSystemInfo
    {
        private ArchiveInfo archiveInfo;
        private string name;
        private string path;
        private bool initialized;
        private bool exists;
        private int archiveNumber;
        private FileAttributes attributes;
        private DateTime lastWriteTime;
        private long length;

        /// <summary>Creates a new ArchiveFileInfo for a file within an archive, to be refreshed on first use.</summary>
        protected ArchiveFileInfo(ArchiveInfo archiveInfo, string filePath)
            : base()
        {
            if (filePath == null)
            {
                throw new ArgumentNullException("filePath");
            }

            this.Archive = archiveInfo;

            this.name = System.IO.Path.GetFileName(filePath);
            this.path = filePath;
        }

        /// <summary>Creates a new ArchiveFileInfo with all of its information already known.</summary>
        protected ArchiveFileInfo(string filePath, int archiveNumber, FileAttributes attributes, DateTime lastWriteTime, long length)
            : this(null, filePath)
        {
            this.exists = true;
            this.archiveNumber = archiveNumber;
            this.attributes = attributes;
            this.lastWriteTime = lastWriteTime;
            this.length = length;
            this.initialized = true;
        }

        /// <summary>Gets the name of the file, without its path inside the archive.</summary>
        public override string Name
        {
            get { return this.name; }
        }

        /// <summary>Gets the internal path of the file in the archive.</summary>
        public string Path
        {
            get { return this.path; }
        }

        /// <summary>Gets the full path to the file: the archive path plus the internal path.</summary>
        public override string FullName
        {
            get
            {
                var fullName = this.path;
                if (this.Archive != null)
                {
                    fullName = System.IO.Path.Combine(this.ArchiveName, fullName);
                }

                return fullName;
            }
        }

        /// <summary>Gets the archive that contains this file.</summary>
        public ArchiveInfo Archive
        {
            get { return this.archiveInfo; }
            internal set { this.archiveInfo = value; }
        }

        /// <summary>Gets the full path of the archive that contains this file.</summary>
        public string ArchiveName
        {
            get { return this.Archive != null ? this.Archive.FullName : null; }
        }

        /// <summary>Gets the number of the archive in a chain that contains the start of this file.</summary>
        public int ArchiveNumber
        {
            get
            {
                this.EnsureInitialized();
                return this.archiveNumber;
            }
        }

        /// <summary>Gets whether the file exists in the archive.</summary>
        public override bool Exists
        {
            get
            {
                this.EnsureInitialized();
                return this.exists;
            }
        }

        /// <summary>Gets the uncompressed size of the file.</summary>
        public long Length
        {
            get
            {
                this.EnsureInitialized();
                return this.length;
            }
        }

        /// <summary>Gets the attributes of the file.</summary>
        public new FileAttributes Attributes
        {
            get
            {
                this.EnsureInitialized();
                return this.attributes;
            }
        }

        /// <summary>Gets the last write time of the file.</summary>
        public new DateTime LastWriteTime
        {
            get
            {
                this.EnsureInitialized();
                return this.lastWriteTime;
            }
        }

        public override string ToString()
        {
            return this.FullName;
        }

        /// <summary>Files cannot be deleted from an archive.</summary>
        public override void Delete()
        {
            throw new NotSupportedException();
        }

        /// <summary>Refreshes the information about the file from the archive.</summary>
        public new void Refresh()
        {
            var newFile = this.Archive != null ? this.Archive.GetFile(this.Path) : null;
            if (newFile == null)
            {
                this.exists = false;
                this.initialized = true;
                return;
            }

            this.Refresh(newFile);
        }

        /// <summary>Extracts the file to a path on disk; the destination must not exist.</summary>
        public void CopyTo(string destFileName)
        {
            this.CopyTo(destFileName, false);
        }

        /// <summary>Extracts the file to a path on disk.</summary>
        public void CopyTo(string destFileName, bool overwrite)
        {
            if (destFileName == null)
            {
                throw new ArgumentNullException("destFileName");
            }

            if (!overwrite && File.Exists(destFileName))
            {
                throw new IOException("The destination file already exists: " + destFileName);
            }

            if (this.Archive == null)
            {
                throw new InvalidOperationException("The file is not associated with an archive.");
            }

            this.Archive.UnpackFile(this.Path, destFileName);
        }

        /// <summary>Opens the file for reading, as a stream over its uncompressed content.</summary>
        public Stream OpenRead()
        {
            if (this.Archive == null)
            {
                throw new InvalidOperationException("The file is not associated with an archive.");
            }

            return this.Archive.OpenRead(this.Path);
        }

        /// <summary>Opens the file for reading as text.</summary>
        public StreamReader OpenText()
        {
            if (this.Archive == null)
            {
                throw new InvalidOperationException("The file is not associated with an archive.");
            }

            return this.Archive.OpenText(this.Path);
        }

        /// <summary>Copies the details of a refreshed file into this one. Derived classes extend it for their own details.</summary>
        protected virtual void Refresh(ArchiveFileInfo newFileInfo)
        {
            this.name = newFileInfo.name;
            this.path = newFileInfo.path;
            this.exists = newFileInfo.exists;
            this.archiveNumber = newFileInfo.archiveNumber;
            this.attributes = newFileInfo.attributes;
            this.lastWriteTime = newFileInfo.lastWriteTime;
            this.length = newFileInfo.length;
            this.initialized = true;
        }

        private void EnsureInitialized()
        {
            if (!this.initialized)
            {
                this.Refresh();
            }
        }
    }
}
