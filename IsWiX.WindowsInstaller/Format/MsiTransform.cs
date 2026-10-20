using IsWiX.WindowsInstaller.Format.Internal;

namespace IsWiX.WindowsInstaller.Format;

/// <summary>
/// A transform (.mst) file. A transform has no _Tables or _Columns; its table streams are
/// kept raw, and its strings come from the transform's own string pool.
/// </summary>
internal sealed class MsiTransform : IDisposable
{
    private readonly FileStream _stream;
    private readonly StringPool _strings;
    private readonly List<KeyValuePair<string, byte[]>> _tables;

    private MsiTransform(FileStream stream, StringPool strings, List<KeyValuePair<string, byte[]>> tables)
    {
        _stream = stream;
        _strings = strings;
        _tables = tables;
    }

    /// <summary>Opens a transform file.</summary>
    public static MsiTransform Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var file = new CompoundFile(stream);
            byte[]? pool = null;
            byte[]? data = null;
            var tables = new List<KeyValuePair<string, byte[]>>();
            foreach (CompoundFile.DirectoryEntry entry in file.Streams)
            {
                // Plain streams such as the summary information are not table data.
                if (!StreamNameCodec.TryGetTableName(entry.RawName, out string name))
                {
                    continue;
                }
                byte[] bytes = file.ReadStream(entry);
                switch (name)
                {
                    case "_StringPool":
                        pool = bytes;
                        break;
                    case "_StringData":
                        data = bytes;
                        break;
                    default:
                        tables.Add(new KeyValuePair<string, byte[]>(name, bytes));
                        break;
                }
            }
            if (pool == null || data == null)
            {
                throw new MsiFormatException("The transform has no string pool.");
            }
            return new MsiTransform(stream, StringPool.Load(pool, data), tables);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The raw stream of each table the transform changes, keyed by table name.</summary>
    public IReadOnlyList<KeyValuePair<string, byte[]>> Tables => _tables;

    /// <summary>A string from the transform's pool. Id 0 is null.</summary>
    public string? GetString(uint id) => _strings[id];

    public void Dispose()
    {
        _stream.Dispose();
    }
}
