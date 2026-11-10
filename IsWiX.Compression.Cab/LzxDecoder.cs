#nullable disable

namespace IsWiX.Compression.Cab
{
    using System;
    using System.IO;

    /// <summary>
    /// Decoder for LZX data as used in cabinet files. Each cabinet data block (frame) holds up to
    /// 32 KB of output and is decoded in order, because the decoder state carries over between frames.
    /// </summary>
    internal sealed class LzxDecoder
    {
        private const int NumChars = 256;
        private const int MinMatch = 2;
        private const int NumPrimaryLengths = 7;
        private const int NumSecondaryLengths = 249;
        private const int PretreeSymbols = 20;
        private const int AlignedSymbols = 8;
        private const int BlockVerbatim = 1;
        private const int BlockAligned = 2;
        private const int BlockUncompressed = 3;

        internal static readonly int[] ExtraBits = BuildExtraBits();
        internal static readonly int[] PositionBase = BuildPositionBase();

        private readonly byte[] output;
        private readonly int mainElements;
        private readonly short[] mainLengths;
        private readonly short[] lengthLengths = new short[NumSecondaryLengths];

        private Huffman mainTree;
        private Huffman lengthTree;
        private Huffman alignedTree;

        private byte[] input;
        private int inputPosition;
        private ulong bitBuffer;
        private int bitsLeft;

        private int position;
        private int frameStart;
        private int frameNumber;
        private int r0 = 1;
        private int r1 = 1;
        private int r2 = 1;
        private bool headerRead;
        private int blockType;
        private int blockRemaining;
        private int blockLength;
        private bool intelStarted;
        private int intelFileSize;
        private int intelCurrentPosition;

        public LzxDecoder(int windowBits, int totalOutputSize)
        {
            if (windowBits < 15 || windowBits > 21)
            {
                throw new InvalidDataException("Unsupported LZX window size: " + windowBits);
            }

            int slots;
            if (windowBits == 20)
            {
                slots = 42;
            }
            else if (windowBits == 21)
            {
                slots = 50;
            }
            else
            {
                slots = windowBits << 1;
            }

            this.mainElements = NumChars + (slots << 3);
            this.mainLengths = new short[this.mainElements];
            this.output = new byte[totalOutputSize];
        }

        // The window the matches copy from. The E8 translation is reversed in a copy, because the compressor's window
        // holds the translated bytes.
        private byte[] translated;

        public byte[] Output
        {
            get { return this.translated ?? this.output; }
        }

        /// <summary>Decodes one cabinet data block that produces frameSize bytes of output.</summary>
        public void DecodeFrame(byte[] data, int frameSize)
        {
            this.input = data;
            this.inputPosition = 0;
            this.bitBuffer = 0;
            this.bitsLeft = 0;

            var target = this.frameStart + frameSize;
            if (target > this.output.Length)
            {
                throw new InvalidDataException("LZX data is larger than the cabinet declares.");
            }

            if (!this.headerRead)
            {
                var high = 0;
                var low = 0;
                if (this.ReadBits(1) != 0)
                {
                    high = this.ReadBits(16);
                    low = this.ReadBits(16);
                }

                this.intelFileSize = (high << 16) | low;
                this.headerRead = true;
            }

            while (this.position < target)
            {
                if (this.blockRemaining == 0)
                {
                    this.ReadBlockHeader();
                }

                var run = Math.Min(this.blockRemaining, target - this.position);
                if (this.blockType == BlockUncompressed)
                {
                    this.CopyUncompressed(run);
                }
                else
                {
                    this.DecodeSymbols(run);
                }
            }

            this.Postprocess(frameSize);
            this.frameStart = target;
            this.frameNumber++;
        }

        private static int[] BuildExtraBits()
        {
            var extra = new int[52];
            var j = 0;
            for (var i = 0; i <= 50; i += 2)
            {
                extra[i] = j;
                extra[i + 1] = j;
                if (i != 0 && j < 17)
                {
                    j++;
                }
            }

            return extra;
        }

        private static int[] BuildPositionBase()
        {
            var positions = new int[52];
            var j = 0;
            for (var i = 0; i <= 50; i++)
            {
                positions[i] = j;
                j += 1 << ExtraBits[i];
            }

            return positions;
        }

        private void EnsureBits(int count)
        {
            while (this.bitsLeft < count)
            {
                int word = 0;
                if (this.inputPosition + 1 < this.input.Length)
                {
                    word = this.input[this.inputPosition] | (this.input[this.inputPosition + 1] << 8);
                }
                else if (this.inputPosition >= this.input.Length + 2)
                {
                    throw new InvalidDataException("Unexpected end of LZX data.");
                }

                this.inputPosition += 2;
                this.bitBuffer |= (ulong)word << (48 - this.bitsLeft);
                this.bitsLeft += 16;
            }
        }

        private int PeekBits(int count)
        {
            return (int)(this.bitBuffer >> (64 - count));
        }

        private void RemoveBits(int count)
        {
            this.bitBuffer <<= count;
            this.bitsLeft -= count;
        }

        private int ReadBits(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            this.EnsureBits(count);
            var value = this.PeekBits(count);
            this.RemoveBits(count);
            return value;
        }

        private int DecodeSymbol(Huffman tree)
        {
            var code = 0;
            var first = 0;
            var index = 0;
            for (var length = 1; length <= 16; length++)
            {
                code |= this.ReadBits(1);
                int count = tree.Count[length];
                if (code - count < first)
                {
                    return tree.Symbol[index + (code - first)];
                }

                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }

            throw new InvalidDataException("Invalid LZX Huffman code.");
        }

        private void ReadLengths(short[] lengths, int first, int last)
        {
            var pretreeLengths = new short[PretreeSymbols];
            for (var i = 0; i < PretreeSymbols; i++)
            {
                pretreeLengths[i] = (short)this.ReadBits(4);
            }

            var pretree = new Huffman(pretreeLengths, PretreeSymbols);

            var x = first;
            while (x < last)
            {
                var z = this.DecodeSymbol(pretree);
                if (z == 17)
                {
                    var count = this.ReadBits(4) + 4;
                    if (x + count > last)
                    {
                        throw new InvalidDataException("Bad LZX length run.");
                    }

                    while (count-- > 0)
                    {
                        lengths[x++] = 0;
                    }
                }
                else if (z == 18)
                {
                    var count = this.ReadBits(5) + 20;
                    if (x + count > last)
                    {
                        throw new InvalidDataException("Bad LZX length run.");
                    }

                    while (count-- > 0)
                    {
                        lengths[x++] = 0;
                    }
                }
                else if (z == 19)
                {
                    var count = this.ReadBits(1) + 4;
                    z = this.DecodeSymbol(pretree);
                    z = lengths[x] - z;
                    if (z < 0)
                    {
                        z += 17;
                    }

                    if (x + count > last)
                    {
                        throw new InvalidDataException("Bad LZX length run.");
                    }

                    while (count-- > 0)
                    {
                        lengths[x++] = (short)z;
                    }
                }
                else
                {
                    z = lengths[x] - z;
                    if (z < 0)
                    {
                        z += 17;
                    }

                    lengths[x++] = (short)z;
                }
            }
        }

        private void ReadBlockHeader()
        {
            // An uncompressed block leaves the input aligned; padding to a word boundary was consumed with it.
            this.blockType = this.ReadBits(3);
            var high = this.ReadBits(16);
            var low = this.ReadBits(8);
            this.blockLength = (high << 8) | low;
            this.blockRemaining = this.blockLength;

            switch (this.blockType)
            {
                case BlockAligned:
                    {
                        var alignedLengths = new short[AlignedSymbols];
                        for (var i = 0; i < AlignedSymbols; i++)
                        {
                            alignedLengths[i] = (short)this.ReadBits(3);
                        }

                        this.alignedTree = new Huffman(alignedLengths, AlignedSymbols);
                        this.ReadTrees();
                        break;
                    }

                case BlockVerbatim:
                    this.ReadTrees();
                    break;

                case BlockUncompressed:
                    {
                        this.intelStarted = true;
                        this.EnsureBits(16);
                        if (this.bitsLeft > 16)
                        {
                            this.inputPosition -= 2;
                        }

                        this.bitBuffer = 0;
                        this.bitsLeft = 0;

                        if (this.inputPosition + 12 > this.input.Length)
                        {
                            throw new InvalidDataException("Unexpected end of LZX data.");
                        }

                        this.r0 = this.ReadInt32();
                        this.r1 = this.ReadInt32();
                        this.r2 = this.ReadInt32();
                        break;
                    }

                default:
                    throw new InvalidDataException("Invalid LZX block type " + this.blockType + ".");
            }
        }

        private int ReadInt32()
        {
            var value = unchecked(this.input[this.inputPosition] | (this.input[this.inputPosition + 1] << 8) | (this.input[this.inputPosition + 2] << 16) | (this.input[this.inputPosition + 3] << 24));
            this.inputPosition += 4;
            return value;
        }

        private void ReadTrees()
        {
            this.ReadLengths(this.mainLengths, 0, NumChars);
            this.ReadLengths(this.mainLengths, NumChars, this.mainElements);
            this.mainTree = new Huffman(this.mainLengths, this.mainElements);
            if (this.mainLengths[0xe8] != 0)
            {
                this.intelStarted = true;
            }

            this.ReadLengths(this.lengthLengths, 0, NumSecondaryLengths);
            this.lengthTree = new Huffman(this.lengthLengths, NumSecondaryLengths);
        }

        private void CopyUncompressed(int run)
        {
            var available = this.input.Length - this.inputPosition;
            var count = Math.Min(run, available);
            if (count <= 0)
            {
                throw new InvalidDataException("Unexpected end of LZX data.");
            }

            Buffer.BlockCopy(this.input, this.inputPosition, this.output, this.position, count);
            this.inputPosition += count;
            this.position += count;
            this.blockRemaining -= count;

            // An uncompressed block with an odd length is followed by a pad byte.
            if (this.blockRemaining == 0 && (this.blockLength & 1) != 0)
            {
                this.inputPosition++;
            }
        }

        private void DecodeSymbols(int run)
        {
            var end = this.position + run;
            while (this.position < end)
            {
                var mainElement = this.DecodeSymbol(this.mainTree);
                if (mainElement < NumChars)
                {
                    this.output[this.position++] = (byte)mainElement;
                    continue;
                }

                mainElement -= NumChars;
                var matchLength = mainElement & NumPrimaryLengths;
                if (matchLength == NumPrimaryLengths)
                {
                    matchLength += this.DecodeSymbol(this.lengthTree);
                }

                matchLength += MinMatch;

                var matchOffset = mainElement >> 3;
                if (matchOffset > 2)
                {
                    if (this.blockType == BlockAligned)
                    {
                        var extra = ExtraBits[matchOffset];
                        matchOffset = PositionBase[matchOffset] - 2;
                        if (extra > 3)
                        {
                            matchOffset += this.ReadBits(extra - 3) << 3;
                            matchOffset += this.DecodeSymbol(this.alignedTree);
                        }
                        else if (extra == 3)
                        {
                            matchOffset += this.DecodeSymbol(this.alignedTree);
                        }
                        else if (extra > 0)
                        {
                            matchOffset += this.ReadBits(extra);
                        }
                        else
                        {
                            matchOffset = 1;
                        }
                    }
                    else if (matchOffset != 3)
                    {
                        var extra = ExtraBits[matchOffset];
                        matchOffset = PositionBase[matchOffset] - 2 + this.ReadBits(extra);
                    }
                    else
                    {
                        matchOffset = 1;
                    }

                    this.r2 = this.r1;
                    this.r1 = this.r0;
                    this.r0 = matchOffset;
                }
                else if (matchOffset == 0)
                {
                    matchOffset = this.r0;
                }
                else if (matchOffset == 1)
                {
                    matchOffset = this.r1;
                    this.r1 = this.r0;
                    this.r0 = matchOffset;
                }
                else
                {
                    matchOffset = this.r2;
                    this.r2 = this.r0;
                    this.r0 = matchOffset;
                }

                if (matchOffset <= 0 || matchOffset > this.position)
                {
                    throw new InvalidDataException("Bad LZX match offset.");
                }

                if (this.position + matchLength > end)
                {
                    throw new InvalidDataException("An LZX match extends beyond its block or frame.");
                }

                var source = this.position - matchOffset;
                for (var i = 0; i < matchLength; i++)
                {
                    this.output[this.position++] = this.output[source++];
                }
            }

            this.blockRemaining -= run;
        }

        // Reverses the E8 call translation that the compressor applied to x86 code.
        private void Postprocess(int frameSize)
        {
            if (!this.intelStarted || this.intelFileSize == 0 || this.frameNumber >= 32768 || frameSize <= 10)
            {
                if (this.translated != null)
                {
                    Buffer.BlockCopy(this.output, this.frameStart, this.translated, this.frameStart, frameSize);
                }

                this.intelCurrentPosition += frameSize;
                return;
            }

            if (this.translated == null)
            {
                this.translated = new byte[this.output.Length];
                Buffer.BlockCopy(this.output, 0, this.translated, 0, this.frameStart);
            }

            Buffer.BlockCopy(this.output, this.frameStart, this.translated, this.frameStart, frameSize);

            var data = this.frameStart;
            var dataEnd = this.frameStart + frameSize - 10;
            var current = this.intelCurrentPosition;
            var buffer = this.translated;

            while (data < dataEnd)
            {
                if (buffer[data++] != 0xe8)
                {
                    current++;
                    continue;
                }

                var absolute = unchecked(buffer[data] | (buffer[data + 1] << 8) | (buffer[data + 2] << 16) | (buffer[data + 3] << 24));
                if (absolute >= -current && absolute < this.intelFileSize)
                {
                    var relative = absolute >= 0 ? absolute - current : absolute + this.intelFileSize;
                    buffer[data] = (byte)(relative & 0xff);
                    buffer[data + 1] = (byte)((relative >> 8) & 0xff);
                    buffer[data + 2] = (byte)((relative >> 16) & 0xff);
                    buffer[data + 3] = (byte)((relative >> 24) & 0xff);
                }

                data += 4;
                current += 5;
            }

            this.intelCurrentPosition += frameSize;
        }

        private sealed class Huffman
        {
            public readonly short[] Count = new short[17];
            public readonly short[] Symbol;

            public Huffman(short[] lengths, int n)
            {
                this.Symbol = new short[n];
                for (var s = 0; s < n; s++)
                {
                    this.Count[lengths[s]]++;
                }

                var offsets = new short[18];
                for (var length = 1; length <= 16; length++)
                {
                    offsets[length + 1] = (short)(offsets[length] + this.Count[length]);
                }

                for (var s = 0; s < n; s++)
                {
                    if (lengths[s] != 0)
                    {
                        this.Symbol[offsets[lengths[s]]++] = (short)s;
                    }
                }
            }
        }
    }
}
