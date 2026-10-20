namespace IsWiX.WindowsInstaller
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// User Canceled the installation.
    /// </summary>
    [Serializable]
    public class InstallCanceledException : InstallerException
    {
        /// <summary>
        /// Creates a new InstallCanceledException with a specified error message and a reference to the
        /// inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception. If the
        /// innerException parameter is not a null reference (Nothing in Visual Basic), the current exception
        /// is raised in a catch block that handles the inner exception.</param>
        public InstallCanceledException(string msg, Exception innerException)
            : base((int) MsiError.INSTALL_USEREXIT, msg, innerException)
        {
        }

        /// <summary>
        /// Creates a new InstallCanceledException with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public InstallCanceledException(string msg)
            : this(msg, null)
        {
        }

        /// <summary>
        /// Creates a new InstallCanceledException.
        /// </summary>
        public InstallCanceledException()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the InstallCanceledException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected InstallCanceledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
