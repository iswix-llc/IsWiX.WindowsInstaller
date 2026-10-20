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
    /// A method was called on an invalid installer handle.  The handle may have been already closed.
    /// </summary>
    [Serializable]
    public class InvalidHandleException : InstallerException
    {
        /// <summary>
        /// Creates a new InvalidHandleException with a specified error message and a reference to the
        /// inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception. If the
        /// innerException parameter is not a null reference (Nothing in Visual Basic), the current exception
        /// is raised in a catch block that handles the inner exception.</param>
        public InvalidHandleException(string msg, Exception innerException)
            : base((int) MsiError.INVALID_HANDLE, msg, innerException)
        {
        }

        /// <summary>
        /// Creates a new InvalidHandleException with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public InvalidHandleException(string msg)
            : this(msg, null)
        {
        }

        /// <summary>
        /// Creates a new InvalidHandleException.
        /// </summary>
        public InvalidHandleException()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the InvalidHandleException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected InvalidHandleException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
