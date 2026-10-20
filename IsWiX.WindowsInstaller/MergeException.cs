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
    /// A failure occurred when executing <see cref="Database.Merge(Database,string)"/>.  The exception may contain
    /// details about the merge conflict.
    /// </summary>
    [Serializable]
    public class MergeException : InstallerException
    {
        private IList<string> conflictTables;
        private IList<int> conflictCounts;

        /// <summary>
        /// Creates a new MergeException with a specified error message and a reference to the
        /// inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception. If the
        /// innerException parameter is not a null reference (Nothing in Visual Basic), the current exception
        /// is raised in a catch block that handles the inner exception.</param>
        public MergeException(string msg, Exception innerException)
            : base(msg, innerException)
        {
        }

        /// <summary>
        /// Creates a new MergeException with a specified error message.
        /// </summary>
        /// <param name="msg">The message that describes the error.</param>
        public MergeException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// Creates a new MergeException.
        /// </summary>
        public MergeException()
            : base()
        {
        }

        internal MergeException(Database db, string conflictsTableName)
            : base("Merge failed.")
        {
            if (conflictsTableName != null)
            {
                IList<string> conflictTableList = new List<string>();
                IList<int> conflictCountList = new List<int>();

                using (View view = db.OpenView("SELECT `Table`, `NumRowMergeConflicts` FROM `" + conflictsTableName + "`"))
                {
                    view.Execute();

                    foreach (Record rec in view) using (rec)
                    {
                        conflictTableList.Add(rec.GetString(1));
                        conflictCountList.Add((int) rec.GetInteger(2));
                    }
                }

                this.conflictTables = conflictTableList;
                this.conflictCounts = conflictCountList;
            }
        }

        /// <summary>
        /// Initializes a new instance of the MergeException class with serialized data.
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
        protected MergeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            this.conflictTables = (string[]) info.GetValue("mergeConflictTables", typeof(string[]));
            this.conflictCounts = (int[]) info.GetValue("mergeConflictCounts", typeof(int[]));
        }

        /// <summary>
        /// Gets the number of merge conflicts in each table, corresponding to the tables returned by
        /// <see cref="ConflictTables"/>.
        /// </summary>
        public IList<int> ConflictCounts
        {
            get
            {
                return new List<int>(this.conflictCounts);
            }
        }

        /// <summary>
        /// Gets the list of tables containing merge conflicts.
        /// </summary>
        public IList<string> ConflictTables
        {
            get
            {
                return new List<string>(this.conflictTables);
            }
        }

        /// <summary>
        /// Gets a message that describes the merge conflits.
        /// </summary>
        public override String Message
        {
            get
            {
                StringBuilder msg = new StringBuilder(base.Message);
                if (this.conflictTables != null)
                {
                    for (int i = 0; i < this.conflictTables.Count; i++)
                    {
                        msg.Append(i == 0 ? "  Conflicts: " : ", ");
                        msg.Append(this.conflictTables[i]);
                        msg.Append('(');
                        msg.Append(this.conflictCounts[i]);
                        msg.Append(')');
                    }
                }
                return msg.ToString();
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

            info.AddValue("mergeConflictTables", this.conflictTables);
            info.AddValue("mergeConflictCounts", this.conflictCounts);
            base.GetObjectData(info, context);
        }
    }
}
