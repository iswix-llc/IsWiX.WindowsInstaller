// The summary information stream (MS-OLEPS property set) and the handle the MsiSummaryInfo functions use.
// The stream holds one section of typed properties. Property 1 is the code page that strings are encoded in.
namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using MsiReader;

    /// <summary>One summary property. Numbers, file times and strings are held in the fields for their type.</summary>
    internal sealed class SummaryValue
    {
        public SummaryValue(uint type, int number = 0, long fileTime = 0, string? text = null, byte[]? clipboard = null)
        {
            this.Type = type;
            this.Number = number;
            this.FileTime = fileTime;
            this.Text = text;
            this.Clipboard = clipboard;
        }

        public uint Type { get; }

        public int Number { get; }

        public long FileTime { get; }

        public string? Text { get; }

        /// <summary>The bytes of a VT_CF thumbnail, kept as they were read so that persisting does not drop them.</summary>
        public byte[]? Clipboard { get; }
    }

    internal static class SummaryStream
    {
        /// <summary>The stream name, which starts with the control character U+0005.</summary>
        public const string Name = "\u0005SummaryInformation";

        public const uint VtEmpty = 0;
        public const uint VtI2 = 2;
        public const uint VtI4 = 3;
        public const uint VtLpstr = 30;
        public const uint VtLpwstr = 31;
        public const uint VtFiletime = 64;
        public const uint VtClipboard = 71;

        public const uint CodePageProperty = 1;
        public const uint MaxProperty = 19;

        private const int HeaderSize = 48;
        private const int DefaultCodePage = 1252;

        // SummaryInformation FMTID {F29F85E0-4FF9-1068-AB91-08002B27B3D9} in its on-disk byte order.
        private static readonly byte[] Fmtid =
        {
            0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10, 0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9,
        };

        public static SortedDictionary<uint, SummaryValue> Read(byte[] data)
        {
            if (data.Length < HeaderSize + 8 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0xFFFE)
            {
                throw new MsiFormatException("Invalid summary information stream.");
            }

            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(44));
            if (offset < HeaderSize || offset > data.Length - 8)
            {
                throw new MsiFormatException("Invalid summary information section offset.");
            }

            int section = (int) offset;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(section + 4));
            if (count > (data.Length - section - 8) / 8)
            {
                throw new MsiFormatException("Invalid summary information property count.");
            }

            var raw = new SortedDictionary<uint, SummaryValue>();
            var texts = new Dictionary<uint, byte[]>();
            for (int i = 0; i < count; i++)
            {
                int entry = section + 8 + i * 8;
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry));
                uint at = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 4));
                if (at > data.Length - section - 4)
                {
                    throw new MsiFormatException("Invalid summary information property offset.");
                }
                int p = section + (int) at;
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                p += 4;
                switch (type)
                {
                    case VtI2:
                        raw[id] = new SummaryValue(type, number: (short) BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p)));
                        break;
                    case VtI4:
                        raw[id] = new SummaryValue(type, number: BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p)));
                        break;
                    case VtFiletime:
                        raw[id] = new SummaryValue(type, fileTime: BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p)));
                        break;
                    case VtLpstr:
                    {
                        uint cch = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                        if (cch > data.Length - p - 4)
                        {
                            throw new MsiFormatException("Invalid summary information string.");
                        }
                        texts[id] = data.AsSpan(p + 4, (int) Math.Max(0, cch - 1)).ToArray();
                        break;
                    }
                    case VtLpwstr:
                    {
                        uint cch = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                        if (cch > (data.Length - p - 4) / 2)
                        {
                            throw new MsiFormatException("Invalid summary information string.");
                        }
                        string text = Encoding.Unicode.GetString(data, p + 4, (int) Math.Max(0, cch - 1) * 2);
                        raw[id] = new SummaryValue(VtLpstr, text: text);
                        break;
                    }
                    case VtClipboard:
                    {
                        int size = (int) BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                        int total = 4 + size;
                        if (size < 4 || total > data.Length - p)
                        {
                            throw new MsiFormatException("Invalid summary information thumbnail.");
                        }
                        raw[id] = new SummaryValue(type, clipboard: data.AsSpan(p - 4, total + 4).ToArray());
                        break;
                    }
                    default:
                        // Other property types are not used by Windows Installer and are dropped.
                        break;
                }
            }

            // Strings are decoded with the code page the stream names, which is read first.
            Encoding encoding = ResolveEncoding(raw.TryGetValue(CodePageProperty, out SummaryValue? cp) ? cp.Number : DefaultCodePage);
            foreach (KeyValuePair<uint, byte[]> text in texts)
            {
                raw[text.Key] = new SummaryValue(VtLpstr, text: encoding.GetString(text.Value));
            }
            return raw;
        }

        public static byte[] Write(IReadOnlyDictionary<uint, SummaryValue> properties)
        {
            int codePage = properties.TryGetValue(CodePageProperty, out SummaryValue? cp) ? cp.Number : DefaultCodePage;
            Encoding encoding = ResolveEncoding(codePage);

            var ordered = properties.OrderBy(p => p.Key).ToList();
            var body = new MemoryStream();
            var pairs = new List<(uint Id, uint Offset)>();
            int headerSize = 8 + 8 * ordered.Count;
            foreach ((uint id, SummaryValue value) in ordered)
            {
                pairs.Add((id, (uint) (headerSize + body.Length)));
                WriteValue(body, value, encoding);
            }

            var output = new MemoryStream();
            var header = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0), 0xFFFE);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x00020005);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 1);
            Array.Copy(Fmtid, 0, header, 28, Fmtid.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), HeaderSize);
            output.Write(header, 0, header.Length);

            var section = new MemoryStream();
            WriteUInt32(section, (uint) (headerSize + body.Length));
            WriteUInt32(section, (uint) ordered.Count);
            foreach ((uint id, uint offset) in pairs)
            {
                WriteUInt32(section, id);
                WriteUInt32(section, offset);
            }
            body.Position = 0;
            body.CopyTo(section);

            section.Position = 0;
            section.CopyTo(output);
            return output.ToArray();
        }

        private static void WriteValue(MemoryStream stream, SummaryValue value, Encoding encoding)
        {
            switch (value.Type)
            {
                case VtI2:
                    WriteUInt32(stream, VtI2);
                    WriteUInt32(stream, (ushort) (short) value.Number);
                    break;
                case VtI4:
                    WriteUInt32(stream, VtI4);
                    WriteUInt32(stream, unchecked((uint) value.Number));
                    break;
                case VtFiletime:
                    WriteUInt32(stream, VtFiletime);
                    WriteUInt32(stream, unchecked((uint) value.FileTime));
                    WriteUInt32(stream, unchecked((uint) (value.FileTime >> 32)));
                    break;
                case VtLpstr:
                {
                    byte[] bytes = encoding.GetBytes(value.Text ?? string.Empty);
                    WriteUInt32(stream, VtLpstr);
                    WriteUInt32(stream, (uint) bytes.Length + 1);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.WriteByte(0);
                    Pad(stream);
                    break;
                }
                case VtClipboard:
                    if (value.Clipboard == null)
                    {
                        throw new ArgumentException("A thumbnail property has no data.");
                    }
                    stream.Write(value.Clipboard, 0, value.Clipboard.Length);
                    Pad(stream);
                    break;
                default:
                    throw new ArgumentException($"Unsupported summary property type {value.Type}.");
            }
        }

        private static void Pad(MemoryStream stream)
        {
            while (stream.Length % 4 != 0)
            {
                stream.WriteByte(0);
            }
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            stream.Write(bytes);
        }

        private static Encoding ResolveEncoding(int codePage)
        {
            int cp = codePage == 0 ? DefaultCodePage : codePage;
            if (cp == 65001)
            {
                return Encoding.UTF8;
            }
            if (cp == 1200)
            {
                return Encoding.Unicode;
            }
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(cp);
        }
    }

    /// <summary>An open summary information handle. Changes are written by Persist, which updates the database.</summary>
    internal sealed class EngineSummary : IDisposable
    {
        private readonly EngineDatabase? database;
        private readonly bool ownsDatabase;
        private readonly bool writable;
        private readonly SortedDictionary<uint, SummaryValue> properties;

        private EngineSummary(EngineDatabase database, bool ownsDatabase, bool writable)
        {
            this.database = database;
            this.ownsDatabase = ownsDatabase;
            this.writable = writable;
            byte[]? data = database.ReadPlainStream(SummaryStream.Name);
            this.properties = data == null ? new SortedDictionary<uint, SummaryValue>() : SummaryStream.Read(data);
        }

        private EngineSummary(byte[]? data)
        {
            this.database = null;
            this.ownsDatabase = false;
            this.writable = false;
            this.properties = data == null ? new SortedDictionary<uint, SummaryValue>() : SummaryStream.Read(data);
        }

        /// <summary>Opens the read-only summary of an MSI or transform file, without opening its tables.</summary>
        public static EngineSummary OpenFile(string path)
        {
            try
            {
                return new EngineSummary(MsiStreams.ReadPlain(path, SummaryStream.Name));
            }
            catch (MsiFormatException)
            {
                throw new MsiErrorException((uint) MsiError.INSTALL_PACKAGE_INVALID);
            }
        }

        /// <summary>Opens the summary of a database. An owned database is closed with the handle.</summary>
        public static EngineSummary Open(EngineDatabase database, bool ownsDatabase, bool writable)
        {
            try
            {
                return new EngineSummary(database, ownsDatabase, writable);
            }
            catch (MsiFormatException)
            {
                if (ownsDatabase)
                {
                    database.Dispose();
                }
                throw new MsiErrorException((uint) MsiError.INSTALL_PACKAGE_INVALID);
            }
        }

        public void Get(uint id, out uint type, out int number, out long fileTime, out string? text)
        {
            RequireProperty(id);
            if (!this.properties.TryGetValue(id, out SummaryValue? value))
            {
                type = SummaryStream.VtEmpty;
                number = 0;
                fileTime = 0;
                text = null;
                return;
            }
            type = value.Type;
            number = value.Number;
            fileTime = value.FileTime;
            text = value.Text;
        }

        public void Set(uint id, uint type, int number, long fileTime, string? text)
        {
            RequireProperty(id);
            if (!this.writable)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            if (type == SummaryStream.VtEmpty)
            {
                if (id == SummaryStream.CodePageProperty)
                {
                    throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
                }
                this.properties.Remove(id);
                return;
            }

            if (id == SummaryStream.CodePageProperty && type != SummaryStream.VtI2)
            {
                throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
            }
            switch (type)
            {
                case SummaryStream.VtI2:
                    if (number < short.MinValue || number > short.MaxValue)
                    {
                        throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
                    }
                    this.properties[id] = new SummaryValue(type, number: number);
                    break;
                case SummaryStream.VtI4:
                    this.properties[id] = new SummaryValue(type, number: number);
                    break;
                case SummaryStream.VtFiletime:
                    this.properties[id] = new SummaryValue(type, fileTime: fileTime);
                    break;
                case SummaryStream.VtLpstr:
                    this.properties[id] = new SummaryValue(type, text: text ?? string.Empty);
                    break;
                default:
                    throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
            }
        }

        /// <summary>Writes the properties into the database. A file-backed handle commits right away.</summary>
        public void Persist()
        {
            if (!this.writable)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            // Strings are written as Windows-1252 unless the stream names another code page, so say so explicitly.
            if (!this.properties.ContainsKey(SummaryStream.CodePageProperty))
            {
                this.properties[SummaryStream.CodePageProperty] = new SummaryValue(SummaryStream.VtI2, number: 1252);
            }
            this.database!.PutPlainStream(SummaryStream.Name, SummaryStream.Write(this.properties));
            if (this.ownsDatabase)
            {
                this.database!.Commit();
            }
        }

        public void Dispose()
        {
            if (this.ownsDatabase)
            {
                this.database!.Dispose();
            }
        }

        private static void RequireProperty(uint id)
        {
            if (id < 1 || id > SummaryStream.MaxProperty)
            {
                throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
            }
        }
    }
}
