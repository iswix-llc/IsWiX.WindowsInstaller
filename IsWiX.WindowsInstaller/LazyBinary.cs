namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>
    /// A binary cell of a table loaded from a file. Its bytes are read only when a query, a commit or an export
    /// needs them, so a table with large streams does not hold all of them in memory.
    /// </summary>
    internal sealed class LazyBinary
    {
        private readonly Func<byte[]?> read;

        public LazyBinary(long length, Func<byte[]?> read)
        {
            this.Length = length;
            this.read = read;
        }

        /// <summary>The size of the stream, known without reading it.</summary>
        public long Length { get; }

        public byte[]? Read() => this.read();

        /// <summary>The bytes of a lazy cell; any other value is returned as it is.</summary>
        public static object? Resolve(object? value) => value is LazyBinary lazy ? lazy.Read() : value;
    }
}
