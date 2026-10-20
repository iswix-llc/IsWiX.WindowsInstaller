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
    /// A bad SQL query string was passed to <see cref="Database.OpenView"/> or <see cref="Database.Execute(string,object[])"/>.
    /// </summary>
    [Serializable]
    public class BadQuerySyntaxException : InstallerException
    {
        /// <summary>
        /// Creates a new BadQuerySyntaxException with a specified error message and a reference to the
        /// inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception. If the
        /// innerException parameter is not a null reference (Nothing in Visual Basic), the current exception
        /// is raised in a catch block that handles the inner exception.</param>
        public BadQuerySyntaxException(string msg, Exception innerException)
            : base((int) MsiError.BAD_QUERY_SYNTAX, msg, innerException)
        {
        }

        /// <summary>
        /// Creates a new BadQuerySyntaxException with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public BadQuerySyntaxException(string msg)
            : this(msg, null)
        {
        }

        /// <summary>
        /// Creates a new BadQuerySyntaxException.
        /// </summary>
        public BadQuerySyntaxException()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the BadQuerySyntaxException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected BadQuerySyntaxException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
