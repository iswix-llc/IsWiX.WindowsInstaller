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
    /// Base class for Windows Installer exceptions.
    /// </summary>
    [Serializable]
    public class InstallerException : SystemException
    {
        private int errorCode;
        private object[] errorData;

        /// <summary>
        /// Creates a new InstallerException with a specified error message and a reference to the
        /// inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception. If the
        /// innerException parameter is not a null reference (Nothing in Visual Basic), the current exception
        /// is raised in a catch block that handles the inner exception.</param>
        public InstallerException(string msg, Exception innerException)
            : this(0, msg, innerException)
        {
        }

        /// <summary>
        /// Creates a new InstallerException with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public InstallerException(string msg)
            : this(0, msg)
        {
        }

        /// <summary>
        /// Creates a new InstallerException.
        /// </summary>
        public InstallerException()
            : this(0, null)
        {
        }

        internal InstallerException(int errorCode, string msg, Exception innerException)
            : base(msg, innerException)
        {
            this.errorCode = errorCode;
            this.SaveErrorRecord();
        }

        internal InstallerException(int errorCode, string msg)
            : this(errorCode, msg, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the InstallerException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected InstallerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            this.errorCode = info.GetInt32("msiErrorCode");
        }

        /// <summary>
        /// Gets the system error code that resulted in this exception, or 0 if not applicable.
        /// </summary>
        public int ErrorCode
        {
            get
            {
                return this.errorCode;
            }
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the exception.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            info.AddValue("msiErrorCode", this.errorCode);
            base.GetObjectData(info, context);
        }

        /// <summary>
        /// Gets extended information about the error, or null if no further information
        /// is available.
        /// </summary>
        /// <returns>A Record object. Field 1 of the Record contains the installer
        /// message code. Other fields contain data specific to the particular error.</returns>
        /// <remarks><p>
        /// If the record is passed to <see cref="Session.Message"/>, it is formatted
        /// by looking up the string in the current database. If there is no installation
        /// session, the formatted error message may be obtained by a query on the Error table using
        /// the error code, followed by a call to <see cref="Record.ToString()"/>.
        /// Alternatively, the standard MSI message can by retrieved by calling the
        /// <see cref="Installer.GetErrorMessage(Record,CultureInfo)"/> method.
        /// </p><p>
        /// The following methods and properties may report extended error data:
        /// <list type="bullet">
        /// <item><see cref="Database"/> (constructor)</item>
        /// <item><see cref="Database"/>.<see cref="Database.ApplyTransform(string,TransformErrors)"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.Commit"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.Execute(string,object[])"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ExecuteQuery(string,object[])"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ExecuteIntegerQuery(string,object[])"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ExecuteStringQuery(string,object[])"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.Export"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ExportAll"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.GenerateTransform"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.Import"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ImportAll"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.Merge(Database,string)"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.OpenView"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.SummaryInfo"/></item>
        /// <item><see cref="Database"/>.<see cref="Database.ViewTransform"/></item>
        /// <item><see cref="View"/>.<see cref="View.Assign"/></item>
        /// <item><see cref="View"/>.<see cref="View.Delete"/></item>
        /// <item><see cref="View"/>.<see cref="View.Execute(Record)"/></item>
        /// <item><see cref="View"/>.<see cref="View.Insert"/></item>
        /// <item><see cref="View"/>.<see cref="View.InsertTemporary"/></item>
        /// <item><see cref="View"/>.<see cref="View.Merge"/></item>
        /// <item><see cref="View"/>.<see cref="View.Modify"/></item>
        /// <item><see cref="View"/>.<see cref="View.Refresh"/></item>
        /// <item><see cref="View"/>.<see cref="View.Replace"/></item>
        /// <item><see cref="View"/>.<see cref="View.Seek"/></item>
        /// <item><see cref="View"/>.<see cref="View.Update"/></item>
        /// <item><see cref="View"/>.<see cref="View.Validate"/></item>
        /// <item><see cref="View"/>.<see cref="View.ValidateFields"/></item>
        /// <item><see cref="View"/>.<see cref="View.ValidateDelete"/></item>
        /// <item><see cref="View"/>.<see cref="View.ValidateNew"/></item>
        /// <item><see cref="SummaryInfo"/> (constructor)</item>
        /// <item><see cref="Record"/>.<see cref="Record.SetStream(int,string)"/></item>
        /// <item><see cref="Session"/>.<see cref="Session.SetInstallLevel"/></item>
        /// <item><see cref="Session"/>.<see cref="Session.GetSourcePath"/></item>
        /// <item><see cref="Session"/>.<see cref="Session.GetTargetPath"/></item>
        /// <item><see cref="Session"/>.<see cref="Session.SetTargetPath"/></item>
        /// <item><see cref="ComponentInfo"/>.<see cref="ComponentInfo.CurrentState"/></item>
        /// <item><see cref="FeatureInfo"/>.<see cref="FeatureInfo.CurrentState"/></item>
        /// <item><see cref="FeatureInfo"/>.<see cref="FeatureInfo.ValidStates"/></item>
        /// <item><see cref="FeatureInfo"/>.<see cref="FeatureInfo.GetCost"/></item>
        /// </list>
        /// </p><p>
        /// The Record object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msigetlasterrorrecord.asp">MsiGetLastErrorRecord</a>
        /// </p></remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public Record GetErrorRecord()
        {
            return this.errorData != null ? new Record(this.errorData) : null;
        }

        internal static Exception ExceptionFromReturnCode(uint errorCode)
        {
            return ExceptionFromReturnCode(errorCode, null);
        }

        /// <summary>
        /// The exception for an internal error. When the error was raised for an underlying failure, such as a
        /// file that could not be written, that failure's message is included so it is not lost.
        /// </summary>
        internal static Exception ExceptionFromReturnCode(MsiErrorException error, string? msg = null)
        {
            string? cause = error.InnerException?.Message;
            string? combined = msg == null ? cause : cause == null ? msg : msg + " " + cause;
            return ExceptionFromReturnCode(error.Code, combined!);
        }

        internal static Exception ExceptionFromReturnCode(uint errorCode, string msg)
        {
            msg = Combine(GetSystemMessage(errorCode), msg);
            switch (errorCode)
            {
                case (uint) MsiError.FILE_NOT_FOUND:
                case (uint) MsiError.PATH_NOT_FOUND: return new FileNotFoundException(msg);
                
                case (uint) MsiError.INVALID_PARAMETER:
                case (uint) MsiError.DIRECTORY:
                case (uint) MsiError.UNKNOWN_PROPERTY:
                case (uint) MsiError.UNKNOWN_PRODUCT:
                case (uint) MsiError.UNKNOWN_FEATURE:
                case (uint) MsiError.UNKNOWN_COMPONENT: return new ArgumentException(msg);
                
                case (uint) MsiError.BAD_QUERY_SYNTAX: return new BadQuerySyntaxException(msg);
                
                case (uint) MsiError.INVALID_HANDLE_STATE:
                case (uint) MsiError.INVALID_HANDLE:
                    InvalidHandleException ihex = new InvalidHandleException(msg);
                    ihex.errorCode = (int) errorCode;
                    return ihex;
                
                case (uint) MsiError.INSTALL_USEREXIT: return new InstallCanceledException(msg);
                
                case (uint) MsiError.CALL_NOT_IMPLEMENTED: return new NotImplementedException(msg);
                
                default: return new InstallerException((int) errorCode, msg);
            }
        }

        internal static string GetSystemMessage(uint errorCode)
        {
            return MsiApi.FormatMessage(errorCode)?.Trim();
        }

        internal void SaveErrorRecord()
        {
            // TODO: pass an affinity handle here?
            Record? errorRec = MsiApi.MsiGetLastErrorRecord();
            if (errorRec != null)
            {
                using (errorRec)
                {
                    this.errorData = new object[errorRec.FieldCount];
                    for (int i = 0; i < this.errorData.Length; i++)
                    {
                        this.errorData[i] = errorRec[i + 1];
                    }
                }
            }
            else
            {
                this.errorData = null;
            }
        }

        private static string Combine(string msg1, string msg2)
        {
            if (msg1 == null) return msg2;
            if (msg2 == null) return msg1;
            return msg1 + " " + msg2;
        }
    }
}
