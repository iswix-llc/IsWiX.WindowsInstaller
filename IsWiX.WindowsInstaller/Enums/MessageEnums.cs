namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Specifies a return status value for message handlers.  These values are returned by
    /// <see cref="Session.Message"/>, <see cref="ExternalUIHandler"/>, and <see cref="IEmbeddedUI.ProcessMessage"/>.
    /// </summary>
    public enum MessageResult : int
    {
        /// <summary>An error was found in the message handler.</summary>
        Error  = -1,

        /// <summary>No action was taken.</summary>
        None   = 0,

        /// <summary>IDOK</summary>
        [SuppressMessage("Microsoft.Naming", "CA1706:ShortAcronymsShouldBeUppercase")]
        OK     = 1,

        /// <summary>IDCANCEL</summary>
        Cancel = 2,

        /// <summary>IDABORT</summary>
        Abort  = 3,

        /// <summary>IDRETRY</summary>
        Retry  = 4,

        /// <summary>IDIGNORE</summary>
        Ignore = 5,

        /// <summary>IDYES</summary>
        Yes    = 6,

        /// <summary>IDNO</summary>
        No     = 7,
    }

    /// <summary>
    /// Specifies constants defining which buttons to display for a message. This can be cast to
    /// the MessageBoxButtons enum in System.Windows.Forms and System.Windows.
    /// </summary>
    public enum MessageButtons
    {
        /// <summary>
        /// The message contains an OK button.
        /// </summary>
        OK = 0,

        /// <summary>
        /// The message contains OK and Cancel buttons.
        /// </summary>
        OKCancel = 1,

        /// <summary>
        /// The message contains Abort, Retry, and Ignore buttons.
        /// </summary>
        AbortRetryIgnore = 2,

        /// <summary>
        /// The message contains Yes, No, and Cancel buttons.
        /// </summary>
        YesNoCancel = 3,

        /// <summary>
        /// The message contains Yes and No buttons.
        /// </summary>
        YesNo = 4,

        /// <summary>
        /// The message contains Retry and Cancel buttons.
        /// </summary>
        RetryCancel = 5,
    }

    /// <summary>
    /// Specifies constants defining which information to display. This can be cast to
    /// the MessageBoxIcon enum in System.Windows.Forms and System.Windows.
    /// </summary>
    public enum MessageIcon
    {
        /// <summary>
        /// The message contain no symbols.
        /// </summary>
        None = 0,

        /// <summary>
        /// The message contains a symbol consisting of white X in a circle with a red background.
        /// </summary>
        Error = 16,

        /// <summary>
        /// The message contains a symbol consisting of a white X in a circle with a red background.
        /// </summary>
        Hand = 16,

        /// <summary>
        /// The message contains a symbol consisting of white X in a circle with a red background.
        /// </summary>
        Stop = 16,

        /// <summary>
        /// The message contains a symbol consisting of a question mark in a circle.
        /// </summary>
        Question = 32,

        /// <summary>
        /// The message contains a symbol consisting of an exclamation point in a triangle with a yellow background.
        /// </summary>
        Exclamation = 48,

        /// <summary>
        /// The message contains a symbol consisting of an exclamation point in a triangle with a yellow background.
        /// </summary>
        Warning = 48,

        /// <summary>
        /// The message contains a symbol consisting of a lowercase letter i in a circle.
        /// </summary>
        Information = 64,

        /// <summary>
        /// The message contains a symbol consisting of a lowercase letter i in a circle.
        /// </summary>
        Asterisk = 64,
    }

    /// <summary>
    /// Specifies constants defining the default button on a message. This can be cast to
    /// the MessageBoxDefaultButton enum in System.Windows.Forms and System.Windows.
    /// </summary>
    public enum MessageDefaultButton
    {
        /// <summary>
        /// The first button on the message is the default button.
        /// </summary>
        Button1 = 0,

        /// <summary>
        /// The second button on the message is the default button.
        /// </summary>
        Button2 = 256,

        /// <summary>
        /// The third button on the message is the default button.
        /// </summary>
        Button3 = 512,
    }

    /// <summary>
    /// Additional styles for use with message boxes.
    /// </summary>
    [Flags]
    internal enum MessageBoxStyles
    {
        /// <summary>
        /// The message box is created with the WS_EX_TOPMOST window style.
        /// </summary>
        TopMost = 0x00040000,

        /// <summary>
        /// The caller is a service notifying the user of an event.
        /// The function displays a message box on the current active desktop, even if there is no user logged on to the computer.
        /// </summary>
        ServiceNotification = 0x00200000,
    }
}
