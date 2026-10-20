using MsiReader.Internal;

namespace MsiReader;

/// <summary>Reads plain streams, such as the summary information, from an MSI or transform file without loading its tables.</summary>
public static class MsiStreams
{
    /// <summary>Returns the bytes of a plain stream, or null if the file has no stream with that name.</summary>
    public static byte[]? ReadPlain(string path, string streamName)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var file = new CompoundFile(stream);
        foreach (CompoundFile.DirectoryEntry entry in file.Streams)
        {
            if (StreamNameCodec.TryGetTableName(entry.RawName, out _))
            {
                continue;
            }
            if (StreamNameCodec.Decode(entry.RawName) == streamName)
            {
                return file.ReadStream(entry);
            }
        }
        return null;
    }
}
