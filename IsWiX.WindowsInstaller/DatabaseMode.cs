namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using MsiReader;

    /// <summary>An open database, read from a file with MsiReader.</summary>
    /// <summary>MSIDBOPEN_READONLY, MSIDBOPEN_TRANSACT and MSIDBOPEN_DIRECT.</summary>
    internal enum DatabaseMode
    {
        ReadOnly = 0,

        /// <summary>Changes are kept in memory until Commit is called.</summary>
        Transact = 1,

        /// <summary>Each change is written to the file as soon as it is made.</summary>
        Direct = 2,
    }
}
