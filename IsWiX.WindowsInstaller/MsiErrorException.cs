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

        public uint Code { get; }
    }
}
