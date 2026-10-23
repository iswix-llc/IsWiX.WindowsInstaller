using System.Buffers.Binary;
using System.Text;

namespace IsWiX.WindowsInstaller.Format.Internal;

/// <summary>
/// The _StringPool and _StringData tables. Table cells hold 2- or 3-byte indexes
/// into this pool. Index 0 means null.
/// </summary>
internal sealed class StringPool
{
    static StringPool()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly string?[] _strings;

    /// <summary>The entries by id; index 0 is unused and an entry that is empty or unused in the file is null.</summary>
    public IReadOnlyList<string?> Entries => _strings;

    public int BytesPerRef { get; }
    public int CodePage { get; }

    private StringPool(string?[] strings, int bytesPerRef, int codePage)
    {
        _strings = strings;
        BytesPerRef = bytesPerRef;
        CodePage = codePage;
    }

    public static StringPool Empty { get; } = new(new string?[1], 2, 1252);

    private Dictionary<string, uint>? _ids;

    /// <summary>The index of the first entry with this text, or null if the pool has no such string.</summary>
    public uint? IdOf(string text)
    {
        if (_ids == null)
        {
            var ids = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (uint id = 1; id < _strings.Length; id++)
            {
                if (_strings[id] is string entry)
                    ids.TryAdd(entry, id);
            }
            _ids = ids;
        }
        return _ids.TryGetValue(text, out uint found) ? found : null;
    }

    public string? this[uint id]
    {
        get
        {
            if (id == 0) return null;
            if (id >= _strings.Length)
                throw new MsiFormatException($"String id {id} is outside the string pool.");
            return _strings[id];
        }
    }

    public static StringPool Load(byte[] pool, byte[] data)
    {
        if (pool.Length == 0)
            return Empty;
        if (pool.Length % 4 != 0)
            throw new MsiFormatException("_StringPool has an invalid size.");

        int wordCount = pool.Length / 2;
        ushort Word(int i) => BinaryPrimitives.ReadUInt16LittleEndian(pool.AsSpan(i * 2));

        int count = pool.Length / 4;
        bool longRefs = pool.Length > 4 && (Word(1) & 0x8000) != 0;
        int bytesPerRef = longRefs ? 3 : 2;
        int codePage = pool.Length > 4
            ? Word(0) | ((Word(1) & ~0x8000) << 16)
            : 0;

        var encoding = ResolveEncoding(codePage);
        var strings = new string?[count];
        int offset = 0;
        int i = 1;
        int n = 1;
        while (i < count)
        {
            ushort refs = Word(i * 2 + 1);
            if (Word(i * 2) == 0 && refs == 0)
            {
                i++;
                n++;
                continue;
            }

            int len;
            if (Word(i * 2) == 0)
            {
                // Strings of 64 KB or more: the length is split across the next entry.
                if (i * 2 + 3 >= wordCount)
                    throw new MsiFormatException("_StringPool is truncated.");
                len = (Word(i * 2 + 3) << 16) + Word(i * 2 + 2);
                i += 2;
            }
            else
            {
                len = Word(i * 2);
                i += 1;
            }

            if (offset + len > data.Length)
                throw new MsiFormatException("_StringData is shorter than the string pool describes.");
            if (n >= strings.Length)
                throw new MsiFormatException("String pool has more entries than its header allows.");

            strings[n] = encoding.GetString(data, offset, len);
            n++;
            offset += len;
        }

        return new StringPool(strings, bytesPerRef, codePage);
    }

    private static Encoding ResolveEncoding(int codePage)
    {
        // Code page 0 means "neutral"; MSI writers in practice use Windows-1252 for it.
        int cp = codePage == 0 ? 1252 : codePage;
        try
        {
            return cp switch
            {
                65001 => Encoding.UTF8,
                1200 => Encoding.Unicode,
                _ => Encoding.GetEncoding(cp),
            };
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }
}
