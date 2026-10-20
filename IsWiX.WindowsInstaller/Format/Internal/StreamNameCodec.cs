namespace IsWiX.WindowsInstaller.Format.Internal;

/// <summary>
/// MSI stores table and binary streams under obfuscated names. Characters in the
/// ranges U+3800-U+47FF and U+4800-U+483F pack two or one 6-bit symbols from the
/// alphabet [0-9A-Za-z._]. A stream for a table is prefixed with U+4840.
/// </summary>
internal static class StreamNameCodec
{
    /// <summary>Marker character that prefixes the name of a table stream.</summary>
    public const char TablePrefix = '䡀';

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._";

    public static string Decode(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length * 2);
        foreach (char ch in raw)
        {
            int c = ch;
            if (c >= 0x3800 && c < 0x4800)
            {
                int v = c - 0x3800;
                sb.Append(Alphabet[v & 0x3F]);
                sb.Append(Alphabet[v >> 6]);
            }
            else if (c >= 0x4800 && c < 0x4840)
            {
                sb.Append(Alphabet[c - 0x4800]);
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>Obfuscates a decoded stream name. The inverse of <see cref="Decode"/>.</summary>
    public static string Encode(string decoded)
    {
        var sb = new System.Text.StringBuilder(decoded.Length);
        int i = 0;
        while (i < decoded.Length)
        {
            int a = Alphabet.IndexOf(decoded[i]);
            if (a >= 0 && i + 1 < decoded.Length && Alphabet.IndexOf(decoded[i + 1]) >= 0)
            {
                int b = Alphabet.IndexOf(decoded[i + 1]);
                sb.Append((char)(0x3800 + (a | (b << 6))));
                i += 2;
            }
            else if (a >= 0)
            {
                sb.Append((char)(0x4800 + a));
                i++;
            }
            else
            {
                sb.Append(decoded[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Returns true and the table name when the raw name is a table stream.</summary>
    public static bool TryGetTableName(string raw, out string tableName)
    {
        if (raw.Length > 0 && raw[0] == TablePrefix)
        {
            tableName = Decode(raw.Substring(1));
            return true;
        }
        tableName = string.Empty;
        return false;
    }
}
