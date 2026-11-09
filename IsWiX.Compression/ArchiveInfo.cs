namespace IsWiX.Compression
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Abstract object representing a compressed archive on disk; like a FileInfo object for an archive file.
    /// </summary>
    public abstract class ArchiveInfo : FileSystemInfo
    {
        protected ArchiveInfo(string path)
            : base()
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }

            this.OriginalPath = path;
            this.FullPath = System.IO.Path.GetFullPath(path);
        }

        /// <summary>Gets the directory that contains the archive.</summary>
        public DirectoryInfo Directory
        {
            get { return new FileInfo(this.FullName).Directory; }
        }

        /// <summary>Gets the full path of the directory that contains the archive.</summary>
        public string DirectoryName
        {
            get { return System.IO.Path.GetDirectoryName(this.FullName); }
        }

        /// <summary>Gets the size of the archive file.</summary>
        public long Length
        {
            get { return new FileInfo(this.FullName).Length; }
        }

        /// <summary>Gets the file name of the archive.</summary>
        public override string Name
        {
            get { return System.IO.Path.GetFileName(this.FullName); }
        }

        /// <summary>Gets whether the archive file exists.</summary>
        public override bool Exists
        {
            get { return File.Exists(this.FullName); }
        }

        public override string ToString()
        {
            return this.FullName;
        }

        /// <summary>Deletes the archive file.</summary>
        public override void Delete()
        {
            File.Delete(this.FullName);
        }

        public void CopyTo(string destFileName)
        {
            File.Copy(this.FullName, destFileName);
        }

        public void CopyTo(string destFileName, bool overwrite)
        {
            File.Copy(this.FullName, destFileName, overwrite);
        }

        public void MoveTo(string destFileName)
        {
            File.Move(this.FullName, destFileName);
            this.FullPath = System.IO.Path.GetFullPath(destFileName);
            this.OriginalPath = destFileName;
        }

        /// <summary>Checks whether the file is a valid archive of this type.</summary>
        public bool IsValid()
        {
            if (!this.Exists)
            {
                return false;
            }

            try
            {
                using (var engine = this.CreateCompressionEngine())
                using (var stream = new FileStream(this.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return engine.FindArchiveOffset(stream) >= 0;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>Gets information about all files in the archive.</summary>
        public IList<ArchiveFileInfo> GetFiles()
        {
            return this.InternalGetFiles(null);
        }

        /// <summary>Gets information about the files in the archive that match a search pattern ('*' and '?').</summary>
        public IList<ArchiveFileInfo> GetFiles(string searchPattern)
        {
            if (searchPattern == null)
            {
                throw new ArgumentNullException("searchPattern");
            }

            var pattern = new Regex(
                "^" + Regex.Escape(searchPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return this.InternalGetFiles(path => pattern.IsMatch(System.IO.Path.GetFileName(path)));
        }

        /// <summary>Extracts all files in the archive to a directory, keeping the internal paths.</summary>
        public void Unpack(string destDirectory)
        {
            this.Unpack(destDirectory, null);
        }

        /// <summary>Extracts all files in the archive to a directory, reporting progress.</summary>
        public void Unpack(string destDirectory, EventHandler<ArchiveProgressEventArgs> progressHandler)
        {
            if (destDirectory == null)
            {
                throw new ArgumentNullException("destDirectory");
            }

            using (var engine = this.CreateCompressionEngine())
            {
                if (progressHandler != null)
                {
                    engine.Progress += progressHandler;
                }

                engine.Unpack(new FileStreamContext(this.FullName, destDirectory, null), null);
            }
        }

        /// <summary>Extracts one file from the archive to a specific path.</summary>
        public void UnpackFile(string fileName, string destFileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException("fileName");
            }

            if (destFileName == null)
            {
                throw new ArgumentNullException("destFileName");
            }

            this.UnpackFiles(new[] { fileName }, null, new[] { destFileName });
        }

        /// <summary>
        /// Extracts the named files. Without destination file names the files go to the directory under their internal paths.
        /// </summary>
        public void UnpackFiles(IList<string> fileNames, string destDirectory, IList<string> destFileNames)
        {
            this.UnpackFiles(fileNames, destDirectory, destFileNames, null);
        }

        /// <summary>
        /// Extracts the named files, reporting progress. Without destination file names the files go to the directory
        /// under their internal paths.
        /// </summary>
        public void UnpackFiles(
            IList<string> fileNames,
            string destDirectory,
            IList<string> destFileNames,
            EventHandler<ArchiveProgressEventArgs> progressHandler)
        {
            if (fileNames == null)
            {
                throw new ArgumentNullException("fileNames");
            }

            if (destFileNames == null && destDirectory == null)
            {
                throw new ArgumentNullException("destDirectory");
            }

            if (destFileNames != null && destFileNames.Count != fileNames.Count)
            {
                throw new ArgumentOutOfRangeException("destFileNames");
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < fileNames.Count; i++)
            {
                map[fileNames[i]] = destFileNames != null ? destFileNames[i] : null;
            }

            var context = new FileStreamContext(this.FullName, destDirectory, map);

            using (var engine = this.CreateCompressionEngine())
            {
                if (progressHandler != null)
                {
                    engine.Progress += progressHandler;
                }

                engine.Unpack(context, path => map.ContainsKey(path));
            }

            foreach (var fileName in fileNames)
            {
                if (!context.Extracted.Contains(fileName))
                {
                    throw new FileNotFoundException("The file was not found in the archive.", fileName);
                }
            }
        }

        /// <summary>Opens a file in the archive for reading, as a stream over its uncompressed content.</summary>
        public Stream OpenRead(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException("fileName");
            }

            using (var engine = this.CreateCompressionEngine())
            using (var stream = new FileStream(this.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return engine.Unpack(stream, fileName);
            }
        }

        /// <summary>Opens a file in the archive for reading as text.</summary>
        public StreamReader OpenText(string fileName)
        {
            return new StreamReader(this.OpenRead(fileName));
        }

        /// <summary>Packing is not implemented yet.</summary>
        public void Pack(string sourceDirectory)
        {
            throw new NotSupportedException("Packing archives is not implemented.");
        }

        internal ArchiveFileInfo GetFile(string path)
        {
            var files = this.InternalGetFiles(p => String.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            return files.Count > 0 ? files[0] : null;
        }

        /// <summary>Creates the engine that reads and writes this type of archive.</summary>
        protected abstract CompressionEngine CreateCompressionEngine();

        private IList<ArchiveFileInfo> InternalGetFiles(Predicate<string> fileFilter)
        {
            using (var engine = this.CreateCompressionEngine())
            {
                var files = engine.GetFileInfo(new FileStreamContext(this.FullName, null, null), fileFilter);
                foreach (var file in files)
                {
                    file.Archive = this;
                }

                return files;
            }
        }

        /// <summary>
        /// Opens the archive file for the engine and writes extracted files to disk, either by internal path under a
        /// directory or to the destination names mapped for specific files.
        /// </summary>
        private sealed class FileStreamContext : IUnpackStreamContext
        {
            private readonly string archivePath;
            private readonly string directory;
            private readonly Dictionary<string, string> map;

            public FileStreamContext(string archivePath, string directory, Dictionary<string, string> map)
            {
                this.archivePath = archivePath;
                this.directory = directory;
                this.map = map;
                this.Extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            public HashSet<string> Extracted { get; private set; }

            public Stream OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine)
            {
                // Archive chains are not supported yet: only the first archive can be opened.
                if (archiveNumber != 0)
                {
                    return null;
                }

                return new FileStream(this.archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            public void CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream)
            {
                stream.Dispose();
            }

            public Stream OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime)
            {
                var target = this.GetTargetPath(path);
                if (target == null)
                {
                    return null;
                }

                var targetDirectory = System.IO.Path.GetDirectoryName(target);
                if (!String.IsNullOrEmpty(targetDirectory))
                {
                    System.IO.Directory.CreateDirectory(targetDirectory);
                }

                return new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            }

            public void CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime)
            {
                stream.Dispose();

                var target = this.GetTargetPath(path);
                this.Extracted.Add(path);

                try
                {
                    if (lastWriteTime != DateTime.MinValue)
                    {
                        File.SetLastWriteTime(target, lastWriteTime);
                    }

                    var settable = attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
                    if (settable != 0)
                    {
                        File.SetAttributes(target, settable);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            private string GetTargetPath(string path)
            {
                string destination = null;
                if (this.map != null && this.map.TryGetValue(path, out destination) && destination != null)
                {
                    return destination;
                }

                if (this.directory == null)
                {
                    return null;
                }

                var root = System.IO.Path.GetFullPath(this.directory);
                var relative = path.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
                var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
                var rootWithSeparator = root.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + System.IO.Path.DirectorySeparatorChar;

                // Never write outside the destination directory, whatever the archive says.
                if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArchiveException("The archive contains a file path outside the destination directory: " + path);
                }

                return target;
            }
        }
    }
}
