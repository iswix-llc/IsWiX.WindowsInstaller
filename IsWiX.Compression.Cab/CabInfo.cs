namespace IsWiX.Compression.Cab
{
    using System.Collections.Generic;
    using IsWiX.Compression;

    /// <summary>
    /// Object representing a cabinet file on disk; like a FileInfo object for a cabinet.
    /// </summary>
    public class CabInfo : ArchiveInfo
    {
        public CabInfo(string path)
            : base(path)
        {
        }

        /// <summary>Gets information about all files in the cabinet.</summary>
        public new IList<CabFileInfo> GetFiles()
        {
            return CastFiles(base.GetFiles());
        }

        /// <summary>Gets information about the files in the cabinet that match a search pattern ('*' and '?').</summary>
        public new IList<CabFileInfo> GetFiles(string searchPattern)
        {
            return CastFiles(base.GetFiles(searchPattern));
        }

        protected override CompressionEngine CreateCompressionEngine()
        {
            return new CabEngine();
        }

        private static IList<CabFileInfo> CastFiles(IList<ArchiveFileInfo> files)
        {
            var result = new List<CabFileInfo>(files.Count);
            foreach (var file in files)
            {
                result.Add((CabFileInfo)file);
            }

            return result;
        }
    }
}
