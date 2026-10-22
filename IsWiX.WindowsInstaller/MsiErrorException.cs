namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>Carries a Windows Installer error code out of a managed operation.</summary>
    internal sealed class MsiErrorException : Exception
    {
        public MsiErrorException(uint code)
            : base("Windows Installer error " + code.ToString(CultureInfo.InvariantCulture))
        {
            Code = code;
        }

        /// <summary>An error code with the exception that caused it, so its message is not lost.</summary>
        public MsiErrorException(uint code, Exception innerException)
            : base("Windows Installer error " + code.ToString(CultureInfo.InvariantCulture), innerException)
        {
            Code = code;
        }

        public uint Code { get; }
    }
}
