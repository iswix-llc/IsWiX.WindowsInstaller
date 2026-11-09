namespace IsWiX.Compression
{
    /// <summary>
    /// The type of progress event.
    /// </summary>
    public enum ArchiveProgressType : int
    {
        StartFile,
        PartialFile,
        FinishFile,
        StartArchive,
        PartialArchive,
        FinishArchive,
    }
}
