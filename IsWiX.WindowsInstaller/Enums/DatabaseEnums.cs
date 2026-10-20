namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Specifies the open mode for a <see cref="Database"/>.
    /// </summary>
    public enum DatabaseOpenMode : int
    {
        /// <summary>Open a database read-only, no persistent changes.</summary>
        ReadOnly = 0,

        /// <summary>Open a database read/write in transaction mode.</summary>
        Transact = 1,

        /// <summary>Open a database direct read/write without transaction.</summary>
        Direct = 2,

        /// <summary>Create a new database, transact mode read/write.</summary>
        Create = 3,

        /// <summary>Create a new database, direct mode read/write.</summary>
        CreateDirect = 4,
    }

    /// <summary>
    /// Attributes for <see cref="Transaction"/> methods.
    /// </summary>
    [Flags]
    public enum TransactionAttributes : int
    {
        /// <summary>No attributes.</summary>
        None                   = 0x00000000,

        /// <summary>Request that the Windows Installer not shutdown the embedded UI until the transaction is complete.</summary>
        ChainEmbeddedUI        = 0x00000001,

        /// <summary>Request that the Windows Installer transfer the embedded UI from the original installation.</summary>
        JoinExistingEmbeddedUI = 0x00000002,
    }
}
