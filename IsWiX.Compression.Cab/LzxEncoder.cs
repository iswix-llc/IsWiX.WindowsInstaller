namespace IsWiX.Compression.Cab
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Encoder for LZX data as used in cabinet files. Each cabinet data block (frame) of up to 32 KB is
    /// encoded as one verbatim LZX block, or as an uncompressed block when that would be clearly larger. The encoder keeps
    /// its window, repeated offsets and Huffman tree lengths between frames, exactly as <see cref="LzxDecoder"/> expects.
    /// </summary>
    /// <remarks>
    /// Matches are chosen by a cost-based parse: every position gets the matches found by a hash-chain search, and a
    /// dynamic program picks the cheapest sequence of literals and matches for the frame, using bit costs taken from
    /// the previous parse of the same frame. That is what gets the output close to the size of the native encoder.
    /// </remarks>
    internal sealed class LzxEncoder
    {
        private const int NumChars = 256;
        private const int MinMatch = 2;
        private const int MaxMatch = 257;
        private const int NumPrimaryLengths = 7;
        private const int NumSecondaryLengths = 249;
        private const int PretreeSymbols = 20;
        private const int FrameSize = 32768;
        private const int HashBits = 16;
        private const int BlockVerbatim = 1;
        private const int BlockUncompressed = 3;
        private const int ParsePasses = 2;

        // Intel E8 call translation, like the native encoder: relative call operands become absolute, which repeats better.
        private const int TranslationSize = 12000000;

        // A match this long is taken as it is; the search stops there.
        private const int NiceLength = 96;

        // Candidate matches are tried at every length up to this; longer ones only at their full length.
        private const int FullLengthRange = 32;

        // A verbatim block is kept unless it is larger than the uncompressed alternative by more than this many bytes,
        // so tiny frames stay verbatim the way the native encoder writes them.
        private const int UncompressedSlack = 64;

        private readonly int windowSize;
        private readonly int maxOffset;
        private readonly int mainElements;
        private readonly int maxChain;
        private readonly byte[] history;
        private readonly int[] head = new int[1 << HashBits];
        private readonly int[] prev;
        private readonly byte[] mainLengths;
        private readonly byte[] lengthLengths = new byte[NumSecondaryLengths];

        private int historyLength;
        private int baseAbsolute;
        private int r0 = 1;
        private int r1 = 1;
        private int r2 = 1;
        private bool firstFrame = true;
        private int frameNumber;
        private int intelCurrent;
        private bool intelStarted;

        public LzxEncoder(int windowBits)
        {
            if (windowBits < 15 || windowBits > 21)
            {
                throw new ArgumentOutOfRangeException(nameof(windowBits));
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

            this.windowSize = 1 << windowBits;
            this.maxOffset = this.windowSize - 3;
            this.mainElements = NumChars + (slots << 3);
            this.mainLengths = new byte[this.mainElements];
            this.history = new byte[this.windowSize + FrameSize + 512];
            this.prev = new int[this.windowSize];
            this.maxChain = windowBits >= 21 ? 128 : windowBits >= 18 ? 96 : 64;

            for (var i = 0; i < this.head.Length; i++)
            {
                this.head[i] = -1;
            }
        }

        /// <summary>Encodes one frame of at most 32768 bytes and returns the compressed cabinet data block.</summary>
        public byte[] EncodeFrame(byte[] data, int count)
        {
            if (count <= 0 || count > FrameSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            // The encoder works on the translated bytes; the decoder reverses the translation after decoding the frame.
            var work = new byte[count];
            Buffer.BlockCopy(data, 0, work, 0, count);
            if (this.frameNumber < 32768 && count > 10)
            {
                this.TranslateE8(work, count);
            }

            this.intelCurrent += count;
            this.frameNumber++;
            data = work;

            var start = this.Append(data, count);

            var savedR0 = this.r0;
            var savedR1 = this.r1;
            var savedR2 = this.r2;

            var tokenMain = new int[count];
            var tokenLength = new int[count];
            var tokenFooterValue = new int[count];
            var tokenFooterBits = new byte[count];
            var mainFrequencies = new int[this.mainElements];
            var lengthFrequencies = new int[NumSecondaryLengths];
            var tokens = this.Tokenize(start, count, tokenMain, tokenLength, tokenFooterValue, tokenFooterBits, mainFrequencies, lengthFrequencies);

            if (!this.intelStarted && mainFrequencies[0xe8] == 0)
            {
                // The decoder only reverses the translation once the E8 literal has a code (or after an uncompressed block).
                mainFrequencies[0xe8] = 1;
            }

            var newMainLengths = BuildLengths(mainFrequencies, this.mainElements, 16);
            var newLengthLengths = BuildLengths(lengthFrequencies, NumSecondaryLengths, 16);
            var mainCodes = BuildCodes(newMainLengths);
            var lengthCodes = BuildCodes(newLengthLengths);

            var writer = new BitWriter(count + 64);
            if (this.firstFrame)
            {
                writer.Write(1, 1); // Intel E8 translation, with its size.
                writer.Write(TranslationSize >> 16, 16);
                writer.Write(TranslationSize & 0xffff, 16);
            }

            writer.Write(BlockVerbatim, 3);
            writer.Write(count >> 8, 16);
            writer.Write(count & 0xff, 8);

            WriteLengths(writer, this.mainLengths, newMainLengths, 0, NumChars);
            WriteLengths(writer, this.mainLengths, newMainLengths, NumChars, this.mainElements);
            WriteLengths(writer, this.lengthLengths, newLengthLengths, 0, NumSecondaryLengths);

            for (var t = 0; t < tokens; t++)
            {
                var main = tokenMain[t];
                writer.Write(mainCodes[main], newMainLengths[main]);
                if (tokenLength[t] >= 0)
                {
                    var lengthSymbol = tokenLength[t];
                    writer.Write(lengthCodes[lengthSymbol], newLengthLengths[lengthSymbol]);
                }

                if (tokenFooterBits[t] > 0)
                {
                    writer.Write(tokenFooterValue[t], tokenFooterBits[t]);
                }
            }

            writer.Flush();

            var uncompressedSize = 4 + 12 + count + (count & 1);
            byte[] result;
            if (writer.Length <= uncompressedSize + UncompressedSlack)
            {
                result = writer.ToArray();
                Array.Copy(newMainLengths, this.mainLengths, this.mainElements);
                Array.Copy(newLengthLengths, this.lengthLengths, NumSecondaryLengths);
                this.intelStarted = true;
            }
            else
            {
                // Incompressible: store the frame and leave the trees and repeated offsets as they were.
                this.r0 = savedR0;
                this.r1 = savedR1;
                this.r2 = savedR2;
                result = this.EncodeUncompressed(data, count);
                this.intelStarted = true;
            }

            this.firstFrame = false;
            return result;
        }

        private static int[] BuildCodes(byte[] lengths)
        {
            var count = new int[18];
            foreach (var length in lengths)
            {
                count[length]++;
            }

            // The canonical assignment used by LzxDecoder: first code of a length is (first code of the previous length + its count) << 1.
            var firstCode = new int[18];
            var running = 0;
            for (var length = 1; length <= 16; length++)
            {
                firstCode[length] = running;
                running = (running + count[length]) << 1;
            }

            var codes = new int[lengths.Length];
            var assigned = new int[18];
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];
                if (length != 0)
                {
                    codes[symbol] = firstCode[length] + assigned[length]++;
                }
            }

            return codes;
        }

        // Builds length-limited Huffman code lengths. Trees must be complete (or empty) because strict LZX decoders reject incomplete ones.
        private static byte[] BuildLengths(int[] frequencies, int count, int maxLength)
        {
            var lengths = new byte[count];
            var working = new int[count];
            var used = 0;
            for (var i = 0; i < count; i++)
            {
                working[i] = frequencies[i];
                if (working[i] > 0)
                {
                    used++;
                }
            }

            if (used == 0)
            {
                return lengths;
            }

            if (used == 1)
            {
                for (var i = 0; i < count; i++)
                {
                    if (working[i] > 0)
                    {
                        lengths[i] = 1;
                        lengths[i == 0 ? 1 : 0] = 1;
                        break;
                    }
                }

                return lengths;
            }

            while (true)
            {
                var depth = HuffmanDepths(working, count);
                var longest = 0;
                for (var i = 0; i < count; i++)
                {
                    if (depth[i] > longest)
                    {
                        longest = depth[i];
                    }
                }

                if (longest <= maxLength)
                {
                    for (var i = 0; i < count; i++)
                    {
                        lengths[i] = (byte)depth[i];
                    }

                    return lengths;
                }

                for (var i = 0; i < count; i++)
                {
                    if (working[i] > 0)
                    {
                        working[i] = (working[i] + 1) >> 1;
                    }
                }
            }
        }

        private static int[] HuffmanDepths(int[] frequencies, int count)
        {
            var symbols = new List<int>();
            for (var i = 0; i < count; i++)
            {
                if (frequencies[i] > 0)
                {
                    symbols.Add(i);
                }
            }

            symbols.Sort((a, b) => frequencies[a] != frequencies[b] ? frequencies[a].CompareTo(frequencies[b]) : a.CompareTo(b));

            var leaves = symbols.Count;
            var nodes = (2 * leaves) - 1;
            var weight = new long[nodes];
            var parent = new int[nodes];
            for (var i = 0; i < leaves; i++)
            {
                weight[i] = frequencies[symbols[i]];
            }

            var leaf = 0;
            var internalNode = leaves;
            var next = leaves;
            for (var merge = 0; merge < leaves - 1; merge++)
            {
                var pair = new int[2];
                for (var k = 0; k < 2; k++)
                {
                    if (leaf < leaves && (internalNode >= next || weight[leaf] <= weight[internalNode]))
                    {
                        pair[k] = leaf++;
                    }
                    else
                    {
                        pair[k] = internalNode++;
                    }
                }

                weight[next] = weight[pair[0]] + weight[pair[1]];
                parent[pair[0]] = next;
                parent[pair[1]] = next;
                next++;
            }

            var nodeDepth = new int[nodes];
            for (var i = nodes - 2; i >= 0; i--)
            {
                nodeDepth[i] = nodeDepth[parent[i]] + 1;
            }

            var depths = new int[count];
            for (var i = 0; i < leaves; i++)
            {
                depths[symbols[i]] = nodeDepth[i];
            }

            return depths;
        }

        private static void WriteLengths(BitWriter writer, byte[] previous, byte[] current, int first, int last)
        {
            var tokenSymbol = new List<int>();
            var tokenExtraBits = new List<int>();
            var tokenExtraValue = new List<int>();
            var tokenDelta = new List<int>();

            var x = first;
            while (x < last)
            {
                var run = 1;
                while (x + run < last && current[x + run] == current[x])
                {
                    run++;
                }

                if (current[x] == 0 && run >= 20)
                {
                    run = Math.Min(run, 51);
                    tokenSymbol.Add(18);
                    tokenExtraBits.Add(5);
                    tokenExtraValue.Add(run - 20);
                    tokenDelta.Add(-1);
                }
                else if (current[x] == 0 && run >= 4)
                {
                    run = Math.Min(run, 19);
                    tokenSymbol.Add(17);
                    tokenExtraBits.Add(4);
                    tokenExtraValue.Add(run - 4);
                    tokenDelta.Add(-1);
                }
                else if (run >= 4)
                {
                    run = Math.Min(run, 5);
                    tokenSymbol.Add(19);
                    tokenExtraBits.Add(1);
                    tokenExtraValue.Add(run - 4);
                    tokenDelta.Add((previous[x] - current[x] + 17) % 17);
                }
                else
                {
                    run = 1;
                    tokenSymbol.Add((previous[x] - current[x] + 17) % 17);
                    tokenExtraBits.Add(0);
                    tokenExtraValue.Add(0);
                    tokenDelta.Add(-1);
                }

                x += run;
            }

            var frequencies = new int[PretreeSymbols];
            for (var t = 0; t < tokenSymbol.Count; t++)
            {
                frequencies[tokenSymbol[t]]++;
                if (tokenDelta[t] >= 0)
                {
                    frequencies[tokenDelta[t]]++;
                }
            }

            var lengths = BuildLengths(frequencies, PretreeSymbols, 15);
            var codes = BuildCodes(lengths);
            for (var i = 0; i < PretreeSymbols; i++)
            {
                writer.Write(lengths[i], 4);
            }

            for (var t = 0; t < tokenSymbol.Count; t++)
            {
                var symbol = tokenSymbol[t];
                writer.Write(codes[symbol], lengths[symbol]);
                if (tokenExtraBits[t] > 0)
                {
                    writer.Write(tokenExtraValue[t], tokenExtraBits[t]);
                }

                if (tokenDelta[t] >= 0)
                {
                    var delta = tokenDelta[t];
                    writer.Write(codes[delta], lengths[delta]);
                }
            }
        }

        private static int PositionSlot(int formatted, int slots)
        {
            var low = 0;
            var high = slots - 1;
            while (low < high)
            {
                var middle = (low + high + 1) >> 1;
                if (LzxDecoder.PositionBase[middle] <= formatted)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        private int Append(byte[] data, int count)
        {
            if (this.historyLength + count > this.history.Length)
            {
                var drop = this.historyLength - this.windowSize;
                Buffer.BlockCopy(this.history, drop, this.history, 0, this.windowSize);
                this.historyLength = this.windowSize;
                this.baseAbsolute += drop;
            }

            var start = this.historyLength;
            Buffer.BlockCopy(data, 0, this.history, start, count);
            this.historyLength += count;
            return start;
        }

        // Bit costs of the symbols, from the frequencies of the previous parse (or a flat guess for the first one).
        private static float[] SymbolCosts(int[] frequencies, int count, float flat)
        {
            var costs = new float[count];
            long total = 0;
            if (frequencies != null)
            {
                for (var i = 0; i < count; i++)
                {
                    total += frequencies[i];
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (frequencies == null || total == 0)
                {
                    costs[i] = flat;
                }
                else
                {
                    costs[i] = (float)(Math.Log(total + (0.5 * count), 2) - Math.Log(frequencies[i] + 0.5, 2));
                }
            }

            return costs;
        }

        private int Tokenize(int start, int count, int[] tokenMain, int[] tokenLength, int[] tokenFooterValue, byte[] tokenFooterBits, int[] mainFrequencies, int[] lengthFrequencies)
        {
            var slots = (this.mainElements - NumChars) >> 3;
            var end = start + count;

            // Every position gets its match candidates: offsets with strictly increasing lengths, plus the repeated offsets.
            var candidateStart = new int[count + 1];
            var candidateOffset = new List<int>(count);
            var candidateLength = new List<int>(count);
            for (var i = 0; i < count; i++)
            {
                candidateStart[i] = candidateOffset.Count;
                this.CollectCandidates(start + i, end, candidateOffset, candidateLength);
                this.Insert(start + i);
            }

            candidateStart[count] = candidateOffset.Count;

            var savedR0 = this.r0;
            var savedR1 = this.r1;
            var savedR2 = this.r2;

            var literalCost = SymbolCosts(null, NumChars, 7f);
            var mainCost = SymbolCosts(null, this.mainElements, 9f);
            var lengthCost = SymbolCosts(null, NumSecondaryLengths, 5f);

            var cost = new float[count + 1];
            var stepLength = new int[count + 1];
            var stepOffset = new int[count + 1];
            var tokens = 0;

            for (var pass = 0; pass < ParsePasses; pass++)
            {
                for (var i = 1; i <= count; i++)
                {
                    cost[i] = float.MaxValue;
                }

                cost[0] = 0;
                for (var i = 0; i < count; i++)
                {
                    var here = cost[i];
                    var literal = here + literalCost[this.history[start + i]];
                    if (literal < cost[i + 1])
                    {
                        cost[i + 1] = literal;
                        stepLength[i + 1] = 1;
                    }

                    for (var k = candidateStart[i]; k < candidateStart[i + 1]; k++)
                    {
                        var offset = candidateOffset[k];
                        var maxLength = candidateLength[k];

                        int slot;
                        var footerBits = 0;
                        if (offset == savedR0)
                        {
                            slot = 0;
                        }
                        else if (offset == savedR1)
                        {
                            slot = 1;
                        }
                        else if (offset == savedR2)
                        {
                            slot = 2;
                        }
                        else
                        {
                            slot = PositionSlot(offset + 2, slots);
                            footerBits = LzxDecoder.ExtraBits[slot];
                        }

                        for (var length = 3; length <= maxLength; length++)
                        {
                            if (length > FullLengthRange && length != maxLength)
                            {
                                continue;
                            }

                            var lengthCode = length - MinMatch;
                            var primary = Math.Min(lengthCode, NumPrimaryLengths);
                            var bits = here + mainCost[NumChars + (slot << 3) + primary] + footerBits;
                            if (lengthCode >= NumPrimaryLengths)
                            {
                                bits += lengthCost[lengthCode - NumPrimaryLengths];
                            }

                            if (bits < cost[i + length])
                            {
                                cost[i + length] = bits;
                                stepLength[i + length] = length;
                                stepOffset[i + length] = offset;
                            }
                        }
                    }
                }

                // Walk back from the end to get the chosen steps in order.
                var steps = new List<int>();
                for (var at = count; at > 0; at -= stepLength[at])
                {
                    steps.Add(at);
                }

                steps.Reverse();

                // Turn the steps into tokens, with the real repeated-offset slots.
                this.r0 = savedR0;
                this.r1 = savedR1;
                this.r2 = savedR2;
                Array.Clear(mainFrequencies, 0, mainFrequencies.Length);
                Array.Clear(lengthFrequencies, 0, lengthFrequencies.Length);
                tokens = 0;
                foreach (var finish in steps)
                {
                    var length = stepLength[finish];
                    var position = start + finish - length;
                    if (length == 1)
                    {
                        tokenMain[tokens] = this.history[position];
                        tokenLength[tokens] = -1;
                        tokenFooterBits[tokens] = 0;
                    }
                    else
                    {
                        var offset = stepOffset[finish];
                        int slot;
                        var footerBits = 0;
                        var footerValue = 0;
                        if (offset == this.r0)
                        {
                            slot = 0;
                        }
                        else if (offset == this.r1)
                        {
                            slot = 1;
                            this.r1 = this.r0;
                            this.r0 = offset;
                        }
                        else if (offset == this.r2)
                        {
                            slot = 2;
                            this.r2 = this.r0;
                            this.r0 = offset;
                        }
                        else
                        {
                            var formatted = offset + 2;
                            slot = PositionSlot(formatted, slots);
                            footerBits = LzxDecoder.ExtraBits[slot];
                            footerValue = formatted - LzxDecoder.PositionBase[slot];
                            this.r2 = this.r1;
                            this.r1 = this.r0;
                            this.r0 = offset;
                        }

                        var lengthCode = length - MinMatch;
                        var primary = Math.Min(lengthCode, NumPrimaryLengths);
                        tokenMain[tokens] = NumChars + (slot << 3) + primary;
                        tokenLength[tokens] = lengthCode >= NumPrimaryLengths ? lengthCode - NumPrimaryLengths : -1;
                        tokenFooterValue[tokens] = footerValue;
                        tokenFooterBits[tokens] = (byte)footerBits;
                    }

                    mainFrequencies[tokenMain[tokens]]++;
                    if (tokenLength[tokens] >= 0)
                    {
                        lengthFrequencies[tokenLength[tokens]]++;
                    }

                    tokens++;
                }

                if (pass == ParsePasses - 1)
                {
                    break;
                }

                // Costs for the next parse come from this one.
                var literalFrequencies = new int[NumChars];
                Array.Copy(mainFrequencies, literalFrequencies, NumChars);
                literalCost = SymbolCosts(literalFrequencies, NumChars, 7f);
                mainCost = SymbolCosts(mainFrequencies, this.mainElements, 12f);
                lengthCost = SymbolCosts(lengthFrequencies, NumSecondaryLengths, 8f);
            }

            return tokens;
        }

        // Replaces relative E8 call operands by absolute ones, exactly the inverse of what LzxDecoder does after decoding a frame.
        private void TranslateE8(byte[] buffer, int count)
        {
            var current = this.intelCurrent;
            var data = 0;
            var end = count - 10;
            while (data < end)
            {
                if (buffer[data++] != 0xe8)
                {
                    current++;
                    continue;
                }

                var relative = unchecked(buffer[data] | (buffer[data + 1] << 8) | (buffer[data + 2] << 16) | (buffer[data + 3] << 24));
                if (relative >= -current && relative < TranslationSize)
                {
                    var absolute = relative < TranslationSize - current ? relative + current : relative - TranslationSize;
                    buffer[data] = (byte)(absolute & 0xff);
                    buffer[data + 1] = (byte)((absolute >> 8) & 0xff);
                    buffer[data + 2] = (byte)((absolute >> 16) & 0xff);
                    buffer[data + 3] = (byte)((absolute >> 24) & 0xff);
                }

                data += 4;
                current += 5;
            }
        }

        private int HashAt(int index)
        {
            // Multiplicative hashing relies on wrapping arithmetic, which a project built with overflow checking would otherwise reject.
            unchecked
            {
                var value = (uint)((this.history[index] << 16) | (this.history[index + 1] << 8) | this.history[index + 2]);
                return (int)((value * 2654435761u) >> (32 - HashBits));
            }
        }

        private void Insert(int index)
        {
            if (index + 2 >= this.historyLength)
            {
                return;
            }

            var absolute = this.baseAbsolute + index;
            var hash = this.HashAt(index);
            this.prev[absolute & (this.windowSize - 1)] = this.head[hash];
            this.head[hash] = absolute;
        }

        // Adds the useful matches at a position: each offset that matches longer than every nearer one, and the repeated offsets.
        private void CollectCandidates(int index, int end, List<int> offsets, List<int> lengths)
        {
            var limit = Math.Min(MaxMatch, end - index);
            if (limit < 3)
            {
                return;
            }

            var available = index; // History index equals the number of bytes before this one in the buffer.
            var longest = 2;

            this.AddRepeated(this.r0, index, limit, available, offsets, lengths);
            if (this.r1 != this.r0)
            {
                this.AddRepeated(this.r1, index, limit, available, offsets, lengths);
            }

            if (this.r2 != this.r0 && this.r2 != this.r1)
            {
                this.AddRepeated(this.r2, index, limit, available, offsets, lengths);
            }

            if (index + 2 >= this.historyLength)
            {
                return;
            }

            var absolute = this.baseAbsolute + index;
            var candidate = this.head[this.HashAt(index)];
            var depth = this.maxChain;
            while (candidate >= this.baseAbsolute && absolute - candidate <= this.maxOffset && depth-- > 0)
            {
                var distance = absolute - candidate;
                if (distance <= available)
                {
                    var length = this.MatchLength(index - distance, index, limit);
                    if (length > longest)
                    {
                        longest = length;
                        offsets.Add(distance);
                        lengths.Add(length);
                        if (length >= limit || length >= NiceLength)
                        {
                            break;
                        }
                    }
                }

                candidate = this.prev[candidate & (this.windowSize - 1)];
            }
        }

        private void AddRepeated(int offset, int index, int limit, int available, List<int> offsets, List<int> lengths)
        {
            if (offset <= 0 || offset > this.maxOffset || offset > available)
            {
                return;
            }

            var length = this.MatchLength(index - offset, index, limit);
            if (length >= 3)
            {
                offsets.Add(offset);
                lengths.Add(length);
            }
        }

        private int MatchLength(int source, int index, int limit)
        {
            var length = 0;
            while (length < limit && this.history[source + length] == this.history[index + length])
            {
                length++;
            }

            return length;
        }

        private byte[] EncodeUncompressed(byte[] data, int count)
        {
            var writer = new BitWriter(count + 32);
            if (this.firstFrame)
            {
                writer.Write(1, 1);
                writer.Write(TranslationSize >> 16, 16);
                writer.Write(TranslationSize & 0xffff, 16);
            }

            writer.Write(BlockUncompressed, 3);
            writer.Write(count >> 8, 16);
            writer.Write(count & 0xff, 8);
            writer.Flush();
            writer.WriteInt32(this.r0);
            writer.WriteInt32(this.r1);
            writer.WriteInt32(this.r2);
            writer.WriteBytes(data, count);
            if ((count & 1) != 0)
            {
                writer.WriteBytes(new byte[1], 1);
            }

            return writer.ToArray();
        }

        private sealed class BitWriter
        {
            private readonly MemoryStream output;
            private ulong accumulator;
            private int bits;

            public BitWriter(int capacity)
            {
                this.output = new MemoryStream(capacity);
            }

            public int Length
            {
                get { return (int)this.output.Length; }
            }

            public void Write(int value, int count)
            {
                if (count == 0)
                {
                    return;
                }

                this.accumulator = (this.accumulator << count) | ((ulong)(uint)value & ((1UL << count) - 1));
                this.bits += count;
                while (this.bits >= 16)
                {
                    var word = (int)((this.accumulator >> (this.bits - 16)) & 0xffff);
                    this.output.WriteByte((byte)(word & 0xff));
                    this.output.WriteByte((byte)((word >> 8) & 0xff));
                    this.bits -= 16;
                }

                this.accumulator &= (1UL << this.bits) - 1;
            }

            public void Flush()
            {
                if (this.bits > 0)
                {
                    var word = (int)((this.accumulator << (16 - this.bits)) & 0xffff);
                    this.output.WriteByte((byte)(word & 0xff));
                    this.output.WriteByte((byte)((word >> 8) & 0xff));
                    this.bits = 0;
                    this.accumulator = 0;
                }
            }

            public void WriteInt32(int value)
            {
                this.output.WriteByte((byte)(value & 0xff));
                this.output.WriteByte((byte)((value >> 8) & 0xff));
                this.output.WriteByte((byte)((value >> 16) & 0xff));
                this.output.WriteByte((byte)((value >> 24) & 0xff));
            }

            public void WriteBytes(byte[] data, int count)
            {
                this.output.Write(data, 0, count);
            }

            public byte[] ToArray()
            {
                return this.output.ToArray();
            }
        }
    }
}
