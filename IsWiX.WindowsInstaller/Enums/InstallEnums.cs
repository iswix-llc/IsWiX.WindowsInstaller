namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Specifies a return status value for custom actions.
    /// </summary>
    public enum ActionResult : int
    {
        /// <summary>Action completed successfully.</summary>
        Success = 0,

        /// <summary>Skip remaining actions, not an error.</summary>
        SkipRemainingActions = 259,

        /// <summary>User terminated prematurely.</summary>
        UserExit = 1602,

        /// <summary>Unrecoverable error or unhandled exception occurred.</summary>
        Failure = 1603,

        /// <summary>Action not executed.</summary>
        NotExecuted = 1626,
    }

    /// <summary>
    /// Log modes available for <see cref="Installer.EnableLog(InstallLogModes,string)"/>
    /// and <see cref="Installer.SetExternalUI(ExternalUIHandler,InstallLogModes)"/>.
    /// </summary>
    [Flags]
    public enum InstallLogModes : int
    {
        /// <summary>Disable logging.</summary>
        None           = 0,

        /// <summary>Log out of memory or fatal exit information.</summary>
        FatalExit      = (1 << ((int) InstallMessage.FatalExit      >> 24)),

        /// <summary>Log error messages.</summary>
        Error          = (1 << ((int) InstallMessage.Error          >> 24)),

        /// <summary>Log warning messages.</summary>
        Warning        = (1 << ((int) InstallMessage.Warning        >> 24)),

        /// <summary>Log user requests.</summary>
        User           = (1 << ((int) InstallMessage.User           >> 24)),

        /// <summary>Log status messages that are not displayed.</summary>
        Info           = (1 << ((int) InstallMessage.Info           >> 24)),

        /// <summary>Log request to determine a valid source location.</summary>
        ResolveSource  = (1 << ((int) InstallMessage.ResolveSource  >> 24)),

        /// <summary>Log insufficient disk space error.</summary>
        OutOfDiskSpace = (1 << ((int) InstallMessage.OutOfDiskSpace >> 24)),

        /// <summary>Log the start of installation actions.</summary>
        ActionStart    = (1 << ((int) InstallMessage.ActionStart    >> 24)),

        /// <summary>Log the data record for installation actions.</summary>
        ActionData     = (1 << ((int) InstallMessage.ActionData     >> 24)),

        /// <summary>Log parameters for user-interface initialization.</summary>
        CommonData     = (1 << ((int) InstallMessage.CommonData     >> 24)),

        /// <summary>Log the property values at termination.</summary>
        PropertyDump   = (1 << ((int) InstallMessage.Progress       >> 24)), // log only

        /// <summary>
        /// Sends large amounts of information to log file not generally useful to users.
        /// May be used for support.
        /// </summary>
        Verbose        = (1 << ((int) InstallMessage.Initialize     >> 24)), // log only

        /// <summary>
        /// Log extra debugging information.
        /// </summary>
        ExtraDebug     = (1 << ((int) InstallMessage.Terminate      >> 24)), // log only

        /// <summary>
        /// Log only on error.
        /// </summary>
        LogOnlyOnError = (1 << ((int) InstallMessage.ShowDialog     >> 24)), // log only

        /// <summary>
        /// Log progress bar information. This message includes information on units so far and total number
        /// of units. See <see cref="Session.Message"/> for an explanation of the message format. This message
        /// is only sent to an external user interface and is not logged.
        /// </summary>
        Progress       = (1 << ((int) InstallMessage.Progress       >> 24)), // external handler only

        /// <summary>
        /// If this is not a quiet installation, then the basic UI has been initialized. If this is a full
        /// UI installation, the Full UI is not yet initialized. This message is only sent to an external
        /// user interface and is not logged.
        /// </summary>
        Initialize     = (1 << ((int) InstallMessage.Initialize     >> 24)), // external handler only

        /// <summary>
        /// If a full UI is being used, the full UI has ended. If this is not a quiet installation, the basic
        /// UI has not yet ended. This message is only sent to an external user interface and is not logged.
        /// </summary>
        Terminate      = (1 << ((int) InstallMessage.Terminate      >> 24)), // external handler only

        /// <summary>
        /// Sent prior to display of the Full UI dialog. This message is only sent to an external user
        /// interface and is not logged.
        /// </summary>
        ShowDialog     = (1 << ((int) InstallMessage.ShowDialog     >> 24)), // external handler only

        /// <summary>
        /// List of files in use that need to be replaced.
        /// </summary>
        FilesInUse     = (1 << ((int) InstallMessage.FilesInUse     >> 24)), // external handler only

        /// <summary>
        /// [MSI 4.0] List of apps that the user can request Restart Manager to shut down and restart.
        /// </summary>
        RMFilesInUse   = (1 << ((int) InstallMessage.RMFilesInUse   >> 24)), // external handler only
    }

    /// <summary>
    /// Type of message to be processed by <see cref="Session.Message"/>,
    /// <see cref="ExternalUIHandler"/>, or <see cref="ExternalUIRecordHandler"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1027:MarkEnumsWithFlags")]
    public enum InstallMessage : int
    {
        /// <summary>Premature termination, possibly fatal OOM.</summary>
        FatalExit      = 0x00000000,

        /// <summary>Formatted error message.</summary>
        Error          = 0x01000000,

        /// <summary>Formatted warning message.</summary>
        Warning        = 0x02000000,

        /// <summary>User request message.</summary>
        User           = 0x03000000,

        /// <summary>Informative message for log.</summary>
        Info           = 0x04000000,

        /// <summary>List of files in use that need to be replaced.</summary>
        FilesInUse     = 0x05000000,

        /// <summary>Request to determine a valid source location.</summary>
        ResolveSource  = 0x06000000,

        /// <summary>Insufficient disk space message.</summary>
        OutOfDiskSpace = 0x07000000,

        /// <summary>Start of action: action name &amp; description.</summary>
        ActionStart    = 0x08000000,

        /// <summary>Formatted data associated with individual action item.</summary>
        ActionData     = 0x09000000,

        /// <summary>Progress gauge info: units so far, total.</summary>
        Progress       = 0x0A000000,

        /// <summary>Product info for dialog: language Id, dialog caption.</summary>
        CommonData     = 0x0B000000,

        /// <summary>Sent prior to UI initialization, no string data.</summary>
        Initialize     = 0x0C000000,

        /// <summary>Sent after UI termination, no string data.</summary>
        Terminate      = 0x0D000000,

        /// <summary>Sent prior to display or authored dialog or wizard.</summary>
        ShowDialog     = 0x0E000000,

        /// <summary>[MSI 4.0] List of apps that the user can request Restart Manager to shut down and restart.</summary>
        RMFilesInUse   = 0x19000000,

        /// <summary>[MSI 4.5] Sent prior to install of a product.</summary>
        InstallStart   = 0x1A000000,

        /// <summary>[MSI 4.5] Sent after install of a product.</summary>
        InstallEnd     = 0x1B000000,
    }

    /// <summary>
    /// Specifies the install mode for <see cref="Installer.ProvideComponent"/> or <see cref="Installer.ProvideQualifiedComponent"/>.
    /// </summary>
    public enum InstallMode : int
    {
        /// <summary>Provide the component only if the feature's installation state is <see cref="InstallState.Local"/>.</summary>
        NoSourceResolution = -3,

        /// <summary>Only check that the component is registered, without verifying that the key file of the component exists.</summary>
        NoDetection        = -2,

        /// <summary>Provide the component only if the feature exists.</summary>
        Existing           = -1,

        /// <summary>Provide the component and perform any installation necessary to provide the component.</summary>
        Default            = 0,
    }

    /// <summary>
    /// Specifies the run mode for <see cref="Session.GetMode"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1027:MarkEnumsWithFlags")]
    public enum InstallRunMode : int
    {
        /// <summary>The administrative mode is installing, or the product is installing.</summary>
        Admin           = 0,

        /// <summary>The advertisements are installing or the product is installing or updating.</summary>
        Advertise       = 1,

        /// <summary>An existing installation is being modified or there is a new installation.</summary>
        Maintenance     = 2,

        /// <summary>Rollback is enabled.</summary>
        RollbackEnabled = 3,

        /// <summary>The log file is active. It was enabled prior to the installation session.</summary>
        LogEnabled      = 4,

        /// <summary>Execute operations are spooling or they are in the determination phase.</summary>
        Operations      = 5,

        /// <summary>A reboot is necessary after a successful installation (settable).</summary>
        RebootAtEnd     = 6,

        /// <summary>A reboot is necessary to continue the installation (settable).</summary>
        RebootNow       = 7,

        /// <summary>Files from cabinets and Media table files are installing.</summary>
        Cabinet         = 8,

        /// <summary>The source LongFileNames is suppressed through the PID_MSISOURCE summary property.</summary>
        SourceShortNames = 9,

        /// <summary>The target LongFileNames is suppressed through the SHORTFILENAMES property.</summary>
        TargetShortNames = 10,

        // <summary>Reserved for future use.</summary>
        //Reserved11      = 11,

        /// <summary>The operating system is Windows 95, Windows 98, or Windows ME.</summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "x")]
        Windows9x       = 12,

        /// <summary>The operating system supports demand installation.</summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Zaw")]
        ZawEnabled      = 13,

        // <summary>Reserved for future use.</summary>
        //Reserved14      = 14,

        // <summary>Reserved for future use.</summary>
        //Reserved15      = 15,

        /// <summary>A custom action called from install script execution.</summary>
        Scheduled       = 16,

        /// <summary>A custom action called from rollback execution script.</summary>
        Rollback        = 17,

        /// <summary>A custom action called from commit execution script.</summary>
        Commit          = 18,
    }

    /// <summary>
    /// Installed state of a Component or Feature.
    /// </summary>
    public enum InstallState : int
    {
        /// <summary>The component is disabled.</summary>
        NotUsed        = -7,

        /// <summary>The installation configuration data is corrupt.</summary>
        BadConfig      = -6,

        /// <summary>The installation is suspended or in progress.</summary>
        Incomplete     = -5,

        /// <summary>Component is set to run from source, but source is unavailable.</summary>
        SourceAbsent   = -4,

        /// <summary>The buffer overflow is returned.</summary>
        MoreData       = -3,

        /// <summary>An invalid parameter was passed to the function.</summary>
        InvalidArgument = -2,

        /// <summary>An unrecognized product or feature name was passed to the function.</summary>
        Unknown        = -1,

        /// <summary>The component is broken.</summary>
        Broken         = 0,

        /// <summary>The feature is advertised.</summary>
        Advertised     = 1,

        /// <summary>The component is being removed. In action state and not settable.</summary>
        Removed        = 1,

        /// <summary>The component is not installed, or action state is absent but clients remain.</summary>
        Absent         = 2,

        /// <summary>The component is installed on the local drive.</summary>
        Local          = 3,

        /// <summary>The component will run from the source, CD, or network.</summary>
        Source         = 4,

        /// <summary>The component will be installed in the default location: local or source.</summary>
        Default        = 5,
    }

    /// <summary>
    /// Specifies the type of installation for <see cref="Installer.ApplyPatch(string,string,InstallType,string)"/>.
    /// </summary>
    public enum InstallType : int
    {
        /// <summary>Searches system for products to patch.</summary>
        Default = 0,

        /// <summary>Indicates a administrative installation.</summary>
        NetworkImage = 1,

        /// <summary>Indicates a particular instance.</summary>
        SingleInstance = 2,
    }

    /// <summary>
    /// Level of the installation user interface, specified with
    /// <see cref="Installer.SetInternalUI(InstallUIOptions)"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    [Flags]
    public enum InstallUIOptions : int
    {
        /// <summary>Does not change UI level.</summary>
        NoChange     = 0,

        /// <summary>Uses Default UI level.</summary>
        Default      = 1,

        /// <summary>Silent installation.</summary>
        Silent       = 2,

        /// <summary>Simple progress and error handling.</summary>
        Basic        = 3,

        /// <summary>Authored UI, wizard dialogs suppressed.</summary>
        Reduced      = 4,

        /// <summary>Authored UI with wizards, progress, and errors.</summary>
        Full         = 5,

        /// <summary>
        /// When combined with the <see cref="Basic"/> value, the installer does not display
        /// the cancel button in the progress dialog.
        /// </summary>
        HideCancel   = 0x20,

        /// <summary>
        /// When combined with the <see cref="Basic"/> value, the installer displays progress
        /// dialog boxes but does not display any modal dialog boxes or error dialog boxes.
        /// </summary>
        ProgressOnly = 0x40,

        /// <summary>
        /// When combined with another value, the installer displays a modal dialog
        /// box at the end of a successful installation or if there has been an error.
        /// No dialog box is displayed if the user cancels.
        /// </summary>
        EndDialog    = 0x80,

        /// <summary>
        /// Forces display of the source resolution dialog even if the UI is otherwise silent.
        /// </summary>
        SourceResolutionOnly = 0x100,

        /// <summary>
        /// [MSI 5.0] Forces display of the UAC dialog even if the UI is otherwise silent.
        /// </summary>
        UacOnly = 0x200,
    }

    /// <summary>
    /// Specifies the reinstall mode for <see cref="Installer.ReinstallFeature"/> or <see cref="Installer.ReinstallProduct"/>.
    /// </summary>
    [Flags]
    public enum ReinstallModes : int
    {
        /// <summary>Reinstall only if file is missing.</summary>
        FileMissing      = 0x00000002,

        /// <summary>Reinstall if file is missing, or older version.</summary>
        FileOlderVersion = 0x00000004,

        /// <summary>Reinstall if file is missing, or equal or older version.</summary>
        FileEqualVersion = 0x00000008,

        /// <summary>Reinstall if file is missing, or not exact version.</summary>
        FileExact        = 0x00000010,

        /// <summary>Checksum executables, reinstall if missing or corrupt.</summary>
        FileVerify       = 0x00000020,

        /// <summary>Reinstall all files, regardless of version.</summary>
        FileReplace      = 0x00000040,

        /// <summary>Insure required machine reg entries.</summary>
        MachineData      = 0x00000080,

        /// <summary>Insure required user reg entries.</summary>
        UserData         = 0x00000100,

        /// <summary>Validate shortcuts items.</summary>
        Shortcut         = 0x00000200,

        /// <summary>Use re-cache source install package.</summary>
        Package          = 0x00000400,
    }

    /// <summary>
    /// Specifies the installation context for <see cref="ProductInstallation"/>s,
    /// <see cref="PatchInstallation"/>es, and
    /// <see cref="Installer.DetermineApplicablePatches(string,string[],InapplicablePatchHandler,string,UserContexts)"/>
    /// </summary>
    [Flags]
    public enum UserContexts : int
    {
        /// <summary>Not installed.</summary>
        None           = 0,

        /// <summary>User managed install context.</summary>
        UserManaged    = 1,

        /// <summary>User non-managed context.</summary>
        UserUnmanaged  = 2,

        /// <summary>Per-machine context.</summary>
        Machine        = 4,

        /// <summary>All contexts, or all valid values.</summary>
        All            = (UserManaged | UserUnmanaged | Machine),

        /// <summary>All user-managed contexts.</summary>
        AllUserManaged = 8,
    }
}
