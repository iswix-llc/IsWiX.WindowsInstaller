#nullable disable

namespace IsWiX.Compression.Cab
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Reads cabinet files without cabinet.dll: header parsing, and decoding of stored, MSZIP and LZX folders.
    /// </summary>
    internal static class CabReader
    {
        private const ushort UtfNameAttribute = 0x80;

        internal static CabinetData ReadHeader(Stream stream)
        {
            var reader = new BinaryReader(stream);
            stream.Position = 0;
            if (reader.ReadUInt32() != 0x4643534d)
            {
                throw new InvalidDataException("Not a cabinet file.");
            }

            reader.ReadUInt32(); // reserved1
            reader.ReadUInt32(); // cbCabinet
            reader.ReadUInt32(); // reserved2
            var filesOffset = reader.ReadUInt32();
            reader.ReadUInt32(); // reserved3
            reader.ReadByte();   // versionMinor
            reader.ReadByte();   // versionMajor
            int folderCount = reader.ReadUInt16();
            int fileCount = reader.ReadUInt16();
            int flags = reader.ReadUInt16();
            reader.ReadUInt16(); // setID
            reader.ReadUInt16(); // iCabinet

            if ((flags & 0x3) != 0)
            {
                throw new NotSupportedException("Cabinets that span several files are not supported by the managed cabinet reader.");
            }

            int reserveFolder = 0;
            int reserveData = 0;
            if ((flags & 0x4) != 0)
            {
                int reserveHeader = reader.ReadUInt16();
                reserveFolder = reader.ReadByte();
                reserveData = reader.ReadByte();
                reader.ReadBytes(reserveHeader);
            }

            var cab = new CabinetData { ReserveData = reserveData };
            for (var i = 0; i < folderCount; i++)
            {
                var folder = new FolderEntry
                {
                    DataOffset = reader.ReadUInt32(),
                    BlockCount = reader.ReadUInt16(),
                    Compression = reader.ReadUInt16(),
                };
                reader.ReadBytes(reserveFolder);
                cab.Folders.Add(folder);
            }

            stream.Position = filesOffset;
            for (var i = 0; i < fileCount; i++)
            {
                var entry = new FileEntry
                {
                    Size = reader.ReadUInt32(),
                    Offset = reader.ReadUInt32(),
                    Folder = reader.ReadUInt16(),
                    Date = reader.ReadUInt16(),
                    Time = reader.ReadUInt16(),
                    Attributes = reader.ReadUInt16(),
                };

                var name = new List<byte>();
                int b;
                while ((b = reader.ReadByte()) != 0)
                {
                    name.Add((byte)b);
                }

                entry.Name = ((entry.Attributes & UtfNameAttribute) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(28591)).GetString(name.ToArray());
                if (entry.Folder >= folderCount)
                {
                    throw new NotSupportedException("The cabinet file continues in another cabinet.");
                }

                cab.Files.Add(entry);
            }

            return cab;
        }

        internal static byte[] ReadFolder(Stream stream, CabinetData cab, int folderIndex)
        {
            var folder = cab.Folders[folderIndex];
            var type = folder.Compression & 0xf;
            if (type != 0 && type != 1 && type != 3)
            {
                throw new NotSupportedException("The managed cabinet reader supports uncompressed, MSZIP and LZX cabinets, not compression type " + type + ".");
            }

            stream.Position = folder.DataOffset;
            var reader = new BinaryReader(stream);

            var payloads = new List<byte[]>();
            var sizes = new List<int>();
            long total = 0;
            for (var i = 0; i < folder.BlockCount; i++)
            {
                reader.ReadUInt32(); // checksum
                int cbData = reader.ReadUInt16();
                int cbUncomp = reader.ReadUInt16();
                reader.ReadBytes(cab.ReserveData);
                var payload = reader.ReadBytes(cbData);
                if (payload.Length != cbData)
                {
                    throw new InvalidDataException("The cabinet is truncated.");
                }

                payloads.Add(payload);
                sizes.Add(cbUncomp);
                total += cbUncomp;
            }

            if (total > Int32.MaxValue)
            {
                throw new NotSupportedException("The cabinet folder is larger than 2 GB.");
            }

            if (type == 3)
            {
                var windowBits = (folder.Compression >> 8) & 0x1f;
                var lzx = new LzxDecoder(windowBits, (int)total);
                for (var i = 0; i < payloads.Count; i++)
                {
                    lzx.DecodeFrame(payloads[i], sizes[i]);
                }

                return lzx.Output;
            }

            var output = new MemoryStream();
            var history = new byte[0];
            for (var i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                byte[] block;
                if (type == 0)
                {
                    block = payload;
                }
                else
                {
                    if (payload.Length < 2 || payload[0] != 'C' || payload[1] != 'K')
                    {
                        throw new InvalidDataException("Invalid MSZIP block.");
                    }

                    block = Inflater.Inflate(payload, 2, history, sizes[i]);
                }

                output.Write(block, 0, block.Length);

                // Each MSZIP block may refer back into the previous 32 KB of output.
                history = block;
            }

            return output.ToArray();
        }

        internal sealed class FileEntry
        {
            public string Name;
            public byte[] NameBytes;
            public bool IsUtf;
            public uint Size;
            public uint Offset;
            public int Folder;
            public ushort Date;
            public ushort Time;
            public ushort Attributes;
        }

        internal sealed class FolderEntry
        {
            public uint DataOffset;
            public int BlockCount;
            public int Compression;
        }

        internal sealed class CabinetData
        {
            public readonly List<FolderEntry> Folders = new List<FolderEntry>();
            public readonly List<FileEntry> Files = new List<FileEntry>();
            public int ReserveData;
        }

        /// <summary>
        /// A small raw-deflate decoder that, unlike DeflateStream, can start with the previous block's output
        /// as its dictionary, which MSZIP requires.
        /// </summary>
        private sealed class Inflater
        {
            private static readonly ushort[] LengthBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
            private static readonly ushort[] LengthExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
            private static readonly ushort[] DistBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
            private static readonly ushort[] DistExtra = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
            private static readonly byte[] CodeLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

            private readonly byte[] input;
            private readonly byte[] window;
            private readonly int historyLength;
            private int position;
            private int bitBuffer;
            private int bitCount;
            private int outputLength;

            private Inflater(byte[] input, int start, byte[] history, int expected)
            {
                this.input = input;
                this.position = start;
                this.historyLength = history.Length;
                this.window = new byte[history.Length + expected];
                Buffer.BlockCopy(history, 0, this.window, 0, history.Length);
                this.outputLength = history.Length;
            }

            public static byte[] Inflate(byte[] input, int start, byte[] history, int expected)
            {
                var inflater = new Inflater(input, start, history, expected);
                inflater.Run();
                var count = inflater.outputLength - inflater.historyLength;
                var result = new byte[count];
                Buffer.BlockCopy(inflater.window, inflater.historyLength, result, 0, count);
                return result;
            }

            private void Run()
            {
                int last;
                do
                {
                    last = this.Bits(1);
                    var type = this.Bits(2);
                    switch (type)
                    {
                        case 0:
                            this.Stored();
                            break;
                        case 1:
                            this.Codes(FixedLengths(), FixedDistances());
                            break;
                        case 2:
                            this.Dynamic();
                            break;
                        default:
                            throw new InvalidDataException("Invalid deflate block type.");
                    }
                }
                while (last == 0);
            }

            private int Bits(int need)
            {
                var value = this.bitBuffer;
                while (this.bitCount < need)
                {
                    if (this.position >= this.input.Length)
                    {
                        throw new InvalidDataException("Unexpected end of MSZIP data.");
                    }

                    value |= this.input[this.position++] << this.bitCount;
                    this.bitCount += 8;
                }

                this.bitBuffer = value >> need;
                this.bitCount -= need;
                return value & ((1 << need) - 1);
            }

            private void Stored()
            {
                this.bitBuffer = 0;
                this.bitCount = 0;
                if (this.position + 4 > this.input.Length)
                {
                    throw new InvalidDataException("Unexpected end of MSZIP data.");
                }

                var len = this.input[this.position] | (this.input[this.position + 1] << 8);
                this.position += 4;
                if (this.position + len > this.input.Length || this.outputLength + len > this.window.Length)
                {
                    throw new InvalidDataException("Invalid stored block.");
                }

                Buffer.BlockCopy(this.input, this.position, this.window, this.outputLength, len);
                this.position += len;
                this.outputLength += len;
            }

            private void Dynamic()
            {
                var nlen = this.Bits(5) + 257;
                var ndist = this.Bits(5) + 1;
                var ncode = this.Bits(4) + 4;
                if (nlen > 286 || ndist > 30)
                {
                    throw new InvalidDataException("Bad dynamic block counts.");
                }

                var lengths = new short[19];
                for (var i = 0; i < ncode; i++)
                {
                    lengths[CodeLengthOrder[i]] = (short)this.Bits(3);
                }

                var lencode = new Huffman(lengths, 19);
                var index = 0;
                var all = new short[nlen + ndist];
                while (index < nlen + ndist)
                {
                    var symbol = this.Decode(lencode);
                    if (symbol < 16)
                    {
                        all[index++] = (short)symbol;
                    }
                    else
                    {
                        short previous = 0;
                        int repeat;
                        if (symbol == 16)
                        {
                            if (index == 0)
                            {
                                throw new InvalidDataException("Bad repeat.");
                            }

                            previous = all[index - 1];
                            repeat = 3 + this.Bits(2);
                        }
                        else if (symbol == 17)
                        {
                            repeat = 3 + this.Bits(3);
                        }
                        else
                        {
                            repeat = 11 + this.Bits(7);
                        }

                        if (index + repeat > nlen + ndist)
                        {
                            throw new InvalidDataException("Bad repeat length.");
                        }

                        while (repeat-- > 0)
                        {
                            all[index++] = previous;
                        }
                    }
                }

                var litLengths = new short[nlen];
                Array.Copy(all, 0, litLengths, 0, nlen);
                var distLengths = new short[ndist];
                Array.Copy(all, nlen, distLengths, 0, ndist);
                this.Codes(new Huffman(litLengths, nlen), new Huffman(distLengths, ndist));
            }

            private static Huffman FixedLengths()
            {
                var lengths = new short[288];
                for (var i = 0; i < 144; i++) { lengths[i] = 8; }
                for (var i = 144; i < 256; i++) { lengths[i] = 9; }
                for (var i = 256; i < 280; i++) { lengths[i] = 7; }
                for (var i = 280; i < 288; i++) { lengths[i] = 8; }
                return new Huffman(lengths, 288);
            }

            private static Huffman FixedDistances()
            {
                var lengths = new short[30];
                for (var i = 0; i < 30; i++) { lengths[i] = 5; }
                return new Huffman(lengths, 30);
            }

            private void Codes(Huffman lencode, Huffman distcode)
            {
                for (;;)
                {
                    var symbol = this.Decode(lencode);
                    if (symbol < 256)
                    {
                        if (this.outputLength >= this.window.Length)
                        {
                            throw new InvalidDataException("MSZIP block is larger than declared.");
                        }

                        this.window[this.outputLength++] = (byte)symbol;
                    }
                    else if (symbol == 256)
                    {
                        return;
                    }
                    else
                    {
                        symbol -= 257;
                        if (symbol >= 29)
                        {
                            throw new InvalidDataException("Bad length symbol.");
                        }

                        var length = LengthBase[symbol] + this.Bits(LengthExtra[symbol]);
                        var dsym = this.Decode(distcode);
                        if (dsym >= 30)
                        {
                            throw new InvalidDataException("Bad distance symbol.");
                        }

                        var distance = DistBase[dsym] + this.Bits(DistExtra[dsym]);
                        if (distance > this.outputLength || this.outputLength + length > this.window.Length)
                        {
                            throw new InvalidDataException("Bad distance in MSZIP block.");
                        }

                        while (length-- > 0)
                        {
                            this.window[this.outputLength] = this.window[this.outputLength - distance];
                            this.outputLength++;
                        }
                    }
                }
            }

            private int Decode(Huffman h)
            {
                var code = 0;
                var first = 0;
                var index = 0;
                for (var len = 1; len <= 15; len++)
                {
                    code |= this.Bits(1);
                    int count = h.Count[len];
                    if (code - count < first)
                    {
                        return h.Symbol[index + (code - first)];
                    }

                    index += count;
                    first += count;
                    first <<= 1;
                    code <<= 1;
                }

                throw new InvalidDataException("Invalid Huffman code.");
            }

            private sealed class Huffman
            {
                public readonly short[] Count = new short[16];
                public readonly short[] Symbol;

                public Huffman(short[] lengths, int n)
                {
                    this.Symbol = new short[n];
                    for (var s = 0; s < n; s++)
                    {
                        this.Count[lengths[s]]++;
                    }

                    var offs = new short[16];
                    for (var len = 1; len < 15; len++)
                    {
                        offs[len + 1] = (short)(offs[len] + this.Count[len]);
                    }

                    for (var s = 0; s < n; s++)
                    {
                        if (lengths[s] != 0)
                        {
                            this.Symbol[offs[lengths[s]]++] = (short)s;
                        }
                    }
                }
            }
        }
    }
}
