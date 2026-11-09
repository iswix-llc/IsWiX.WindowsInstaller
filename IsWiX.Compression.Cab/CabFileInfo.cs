namespace IsWiX.Compression.Cab
{
    using System;
    using System.IO;
    using IsWiX.Compression;

    /// <summary>
    /// Object representing a compressed file within a cabinet; like a FileInfo object for a file in a cabinet.
    /// </summary>
    public class CabFileInfo : ArchiveFileInfo
    {
        private int cabFolder;

        /// <summary>Creates a CabFileInfo for a file within a cabinet, to be refreshed on first use.</summary>
        public CabFileInfo(CabInfo cabinetInfo, string filePath)
            : base(cabinetInfo, filePath)
        {
            if (cabinetInfo == null)
            {
                throw new ArgumentNullException("cabinetInfo");
            }
        }

        internal CabFileInfo(string filePath, int cabNumber, int cabFolder, FileAttributes attributes, DateTime lastWriteTime, long length)
            : base(filePath, cabNumber, attributes, lastWriteTime, length)
        {
            this.cabFolder = cabFolder;
        }

        /// <summary>Gets the cabinet that contains this file.</summary>
        public CabInfo Cabinet
        {
            get { return (CabInfo)this.Archive; }
        }

        /// <summary>Gets the full path of the cabinet that contains this file.</summary>
        public string CabinetName
        {
            get { return this.ArchiveName; }
        }

        /// <summary>Gets the number of the folder inside the cabinet that holds the file's data.</summary>
        public int CabinetFolderNumber
        {
            get
            {
                // Make sure the file has been refreshed from the cabinet.
                var ignored = this.Length;
                return this.cabFolder;
            }
        }

        protected override void Refresh(ArchiveFileInfo newFileInfo)
        {
            base.Refresh(newFileInfo);

            var newCabFileInfo = newFileInfo as CabFileInfo;
            if (newCabFileInfo != null)
            {
                this.cabFolder = newCabFileInfo.cabFolder;
            }
        }
    }
}
