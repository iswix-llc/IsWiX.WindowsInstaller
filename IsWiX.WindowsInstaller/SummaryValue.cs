namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using IsWiX.WindowsInstaller.Format;

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
}
