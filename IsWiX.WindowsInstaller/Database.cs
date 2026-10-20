namespace IsWiX.WindowsInstaller
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text;
    using MsiReader;

    /// <summary>
    /// Accesses a Windows Installer database.
    /// </summary>
    /// <remarks><p>
    /// The <see cref="Commit"/> method must be called before the Database is closed to write out all
    /// persistent changes. If the Commit method is not called, the installer performs an implicit
    /// rollback upon object destruction.
    /// </p><p>
    /// The client can use the following procedure for data access:<list type="number">
    /// <item><description>Obtain a Database object using one of the Database constructors.</description></item> 
    /// <item><description>Initiate a query using a SQL string by calling the <see cref="OpenView"/>
    ///		method of the Database.</description></item> 
    /// <item><description>Set query parameters in a <see cref="Record"/> and execute the database
    ///		query by calling the <see cref="View.Execute(Record)"/> method of the <see cref="View"/>. This
    ///		produces a result that can be fetched or updated.</description></item> 
    /// <item><description>Call the <see cref="View.Fetch"/> method of the View repeatedly to return
    ///		Records.</description></item> 
    /// <item><description>Update database rows of a Record object obtained by the Fetch method using
    ///		one of the <see cref="View.Modify"/> methods of the View.</description></item> 
    /// <item><description>Release the query and any unfetched records by calling the <see cref="Close"/>
    ///		method of the View.</description></item> 
    /// <item><description>Persist any database updates by calling the Commit method of the Database.
    ///		</description></item> 
    /// </list>
    /// </p></remarks>
    public partial class Database : IDisposable
    {

        private readonly object summaryLock = new object();
        private bool closed;

        /// <summary>
        /// Checks if the database is closed. When closed, method calls on the database may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                return this.closed;
            }
        }

        /// <summary>
        /// Throws <see cref="InvalidHandleException"/> if the database has been closed.
        /// </summary>
        internal void ThrowIfClosed()
        {
            if (this.closed)
            {
                throw new InvalidHandleException();
            }
        }

        /// <summary>
        /// Closes the database. After closing it, further method calls may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Closes the handle. This method is merely an alias for the <see cref="Dispose()"/> method.
        /// </summary>
        public void Close()
        {
            this.Dispose();
        }

        private string filePath;
        private DatabaseOpenMode openMode;
        private SummaryInfo summaryInfo;
        private TableCollection tables;
        private IList<string> deleteOnClose;

        /// <summary>
        /// Opens an existing database in read-only mode.
        /// </summary>
        /// <param name="filePath">Path to the database file.</param>
        /// <exception cref="InstallerException">the database could not be created/opened</exception>
        /// <remarks><p>
        /// Because this constructor initiates database access, it cannot be used with a
        /// running installation.
        /// </p><p>
        /// The Database object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiopendatabase.asp">MsiOpenDatabase</a>
        /// </p></remarks>
        public Database(string filePath)
            : this(filePath, DatabaseOpenMode.ReadOnly)
        {
        }

        /// <summary>
        /// Opens an existing database with another database as output.
        /// </summary>
        /// <param name="filePath">Path to the database to be read.</param>
        /// <param name="outputPath">Open mode for the database</param>
        /// <returns>Database object representing the created or opened database</returns>
        /// <exception cref="InstallerException">the database could not be created/opened</exception>
        /// <remarks><p>
        /// When a database is opened as the output of another database, the summary information stream
        /// of the output database is actually a read-only mirror of the original database and thus cannot
        /// be changed. Additionally, it is not persisted with the database. To create or modify the
        /// summary information for the output database it must be closed and re-opened.
        /// </p><p>
        /// The Database object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// The database is opened in <see cref="DatabaseOpenMode.CreateDirect" /> mode, and will be
        /// automatically commited when it is closed.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiopendatabase.asp">MsiOpenDatabase</a>
        /// </p></remarks>
        public Database(string filePath, string outputPath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException("filePath");
            }
            if (String.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentNullException("outputPath");
            }
            // Output databases (the create modes) are not supported yet.
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        /// <summary>
        /// Opens an existing database or creates a new one.
        /// </summary>
        /// <param name="filePath">Path to the database file. If an empty string
        /// is supplied, a temporary database is created that is not persisted.</param>
        /// <param name="mode">Open mode for the database</param>
        /// <exception cref="InstallerException">the database could not be created/opened</exception>
        /// <remarks><p>
        /// Because this constructor initiates database access, it cannot be used with a
        /// running installation.
        /// </p><p>
        /// The database object should be <see cref="Close"/>d after use.
        /// The finalizer will close the handle if it is still open, however due to the nondeterministic
        /// nature of finalization it is best that the handle be closed manually as soon as it is no
        /// longer needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// A database opened in <see cref="DatabaseOpenMode.CreateDirect" /> or
        /// <see cref="DatabaseOpenMode.Direct" /> mode will be automatically commited when it is
        /// closed. However a database opened in <see cref="DatabaseOpenMode.Create" /> or
        /// <see cref="DatabaseOpenMode.Transact" /> mode must have the <see cref="Commit" /> method
        /// called before it is closed, otherwise no changes will be persisted.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiopendatabase.asp">MsiOpenDatabase</a>
        /// </p></remarks>
        public Database(string filePath, DatabaseOpenMode mode)
            : this(Database.OpenReader(filePath, mode), filePath, mode)
        {
        }

        /// <summary>
        /// Creates a database object over an open engine database.
        /// </summary>
        private Database(MsiDatabase reader, string filePath, DatabaseOpenMode openMode)
        {
            this.reader = reader;
            this.filePath = filePath;
            this.openMode = openMode;
            this.Mode = (DatabaseMode) openMode;
        }

        /// <summary>
        /// Gets the file path the Database was originally opened from, or null if not known.
        /// </summary>
        public String FilePath
        {
            get
            {
                return this.filePath;
            }
        }

        /// <summary>
        /// Gets the open mode for the database.
        /// </summary>
        public DatabaseOpenMode OpenMode
        {
            get
            {
                return this.openMode;
            }
        }

        /// <summary>
        /// Gets a boolean value indicating whether this database was opened in read-only mode.
        /// </summary>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msigetdatabasestate.asp">MsiGetDatabaseState</a>
        /// </p></remarks>
        public bool IsReadOnly
        {
            get
            {
                int state = MsiApi.MsiGetDatabaseState(this);
                return state != 1;
            }
        }

        /// <summary>
        /// Gets the collection of tables in the Database.
        /// </summary>
        public TableCollection Tables
        {
            get
            {
                if (this.tables == null)
                {
                    this.tables = new TableCollection(this);
                }
                return this.tables;
            }
        }

        /// <summary>
        /// Gets or sets the code page of the Database.
        /// </summary>
        /// <exception cref="IOException">error exporting/importing the codepage data</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// Getting or setting the code page is a slow operation because it involves an export or import
        /// of the codepage data to/from a temporary file.
        /// </p></remarks>
        public int CodePage
        {
            get
            {
                string tempFile = Path.GetTempFileName();
                StreamReader reader = null;
                try
                {
                    this.Export("_ForceCodepage", tempFile);
                    reader = File.OpenText(tempFile);
                    reader.ReadLine();  // Skip column name record.
                    reader.ReadLine();  // Skip column defn record.
                    string codePageLine = reader.ReadLine();
                    return Int32.Parse(codePageLine.Split('\t')[0], CultureInfo.InvariantCulture.NumberFormat);
                }
                finally
                {
                    if (reader != null) reader.Close();
                    File.Delete(tempFile);
                }
            }

            set
            {
                string tempFile = Path.GetTempFileName();
                StreamWriter writer = null;
                try
                {
                    writer = File.AppendText(tempFile);
                    writer.WriteLine("");
                    writer.WriteLine("");
                    writer.WriteLine("{0}\t_ForceCodepage", value);
                    writer.Close();
                    writer = null;
                    this.Import(tempFile);
                }
                finally
                {
                    if (writer != null) writer.Close();
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Gets the SummaryInfo object for this database that can be used to examine and modify properties
        /// to the summary information stream.
        /// </summary>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// The object returned from this property does not need to be explicitly persisted or closed.
        /// Any modifications will be automatically saved when the database is committed.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msigetsummaryinformation.asp">MsiGetSummaryInformation</a>
        /// </p></remarks>
        public SummaryInfo SummaryInfo
        {
            get
            {
                if (this.summaryInfo == null || this.summaryInfo.IsClosed)
                {
                    lock (this.summaryLock)
                    {
                        if (this.summaryInfo == null || this.summaryInfo.IsClosed)
                        {
                            int maxProperties = this.IsReadOnly ? 0 : SummaryInfo.MAX_PROPERTIES;
                            int summaryInfoHandle = MsiApi.MsiGetSummaryInformation(this, null, (uint) maxProperties);
                            this.summaryInfo = new SummaryInfo(summaryInfoHandle, true);
                        }
                    }
                }
                return this.summaryInfo;
            }
        }

        /// <summary>
        /// Schedules a file or directory for deletion after the database handle is closed.
        /// </summary>
        /// <param name="path">File or directory path to be deleted. All files and subdirectories
        /// under a directory are deleted.</param>
        /// <remarks><p>
        /// Once an item is scheduled, it cannot be unscheduled.
        /// </p><p>
        /// The items cannot be deleted if the Database object is auto-disposed by the
        /// garbage collector; the handle must be explicitly closed.
        /// </p><p>
        /// Files which are read-only or otherwise locked cannot be deleted,
        /// but they will not cause an exception to be thrown.
        /// </p></remarks>
        public void DeleteOnClose(string path)
        {
            if (this.deleteOnClose == null)
            {
                this.deleteOnClose = new List<string>();
            }
            this.deleteOnClose.Add(path);
        }

        /// <summary>
        /// Merges another database with this database.
        /// </summary>
        /// <param name="otherDatabase">The database to be merged into this database</param>
        /// <param name="errorTable">Optional name of table to contain the names of the tables containing
        /// merge conflicts, the number of conflicting rows within the table, and a reference to the table
        /// with the merge conflict.</param>
        /// <exception cref="MergeException">merge failed due to a schema difference or data conflict</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// Merge does not copy over embedded cabinet files or embedded transforms from the
        /// reference database into the target database. Embedded data streams that are listed in the
        /// Binary table or Icon table are copied from the reference database to the target database.
        /// Storage embedded in the reference database are not copied to the target database.
        /// </p><p>
        /// The Merge method merges the data of two databases. These databases must have the same
        /// codepage. The merge fails if any tables or rows in the databases conflict. A conflict exists
        /// if the data in any row in the first database differs from the data in the corresponding row
        /// of the second database. Corresponding rows are in the same table of both databases and have
        /// the same primary key in both databases. The tables of non-conflicting databases must have
        /// the same number of primary keys, same number of columns, same column types, same column names,
        /// and the same data in rows with identical primary keys. Temporary columns however don't matter
        /// in the column count and corresponding tables can have a different number of temporary columns
        /// without creating conflict as long as the persistent columns match.
        /// </p><p>
        /// If the number, type, or name of columns in corresponding tables are different, the
        /// schema of the two databases are incompatible and the installer will stop processing tables
        /// and the merge fails. The installer checks that the two databases have the same schema before
        /// checking for row merge conflicts. If the schemas are incompatible, the databases have be
        /// modified.
        /// </p><p>
        /// If the data in particular rows differ, this is a row merge conflict, the merge fails
        /// and creates a new table with the specified name. The first column of this table is the name
        /// of the table having the conflict. The second column gives the number of rows in the table
        /// having the conflict.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabasemerge.asp">MsiDatabaseMerge</a>
        /// </p></remarks>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Merge(Database otherDatabase, string errorTable)
        {
            if (otherDatabase == null)
            {
                throw new ArgumentNullException("otherDatabase");
            }

            switch (MsiApi.MsiDatabaseMerge(this, otherDatabase, errorTable))
            {
                case MsiApi.MergeOutcome.Conflict:
                    throw new MergeException(this, errorTable);
                case MsiApi.MergeOutcome.SchemaMismatch:
                    throw new MergeException("Schema difference between the two databases.");
            }
        }

        /// <summary>
        /// Merges another database with this database.
        /// </summary>
        /// <param name="otherDatabase">The database to be merged into this database</param>
        /// <exception cref="MergeException">merge failed due to a schema difference or data conflict</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// MsiDatabaseMerge does not copy over embedded cabinet files or embedded transforms from
        /// the reference database into the target database. Embedded data streams that are listed in
        /// the Binary table or Icon table are copied from the reference database to the target database.
        /// Storage embedded in the reference database are not copied to the target database.
        /// </p><p>
        /// The Merge method merges the data of two databases. These databases must have the same
        /// codepage. The merge fails if any tables or rows in the databases conflict. A conflict exists
        /// if the data in any row in the first database differs from the data in the corresponding row
        /// of the second database. Corresponding rows are in the same table of both databases and have
        /// the same primary key in both databases. The tables of non-conflicting databases must have
        /// the same number of primary keys, same number of columns, same column types, same column names,
        /// and the same data in rows with identical primary keys. Temporary columns however don't matter
        /// in the column count and corresponding tables can have a different number of temporary columns
        /// without creating conflict as long as the persistent columns match.
        /// </p><p>
        /// If the number, type, or name of columns in corresponding tables are different, the
        /// schema of the two databases are incompatible and the installer will stop processing tables
        /// and the merge fails. The installer checks that the two databases have the same schema before
        /// checking for row merge conflicts. If the schemas are incompatible, the databases have be
        /// modified.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabasemerge.asp">MsiDatabaseMerge</a>
        /// </p></remarks>
        public void Merge(Database otherDatabase) { this.Merge(otherDatabase, null); }

        /// <summary>
        /// Checks whether a table exists and is persistent in the database.
        /// </summary>
        /// <param name="table">The table to the checked</param>
        /// <returns>true if the table exists and is persistent in the database; false otherwise</returns>
        /// <exception cref="ArgumentException">the table is unknown</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// To check whether a table exists regardless of persistence,
        /// use <see cref="TableCollection.Contains"/>.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabaseistablepersistent.asp">MsiDatabaseIsTablePersistent</a>
        /// </p></remarks>
        public bool IsTablePersistent(string table)
        {
            if (String.IsNullOrEmpty(table))
            {
                throw new ArgumentNullException("table");
            }
            uint ret = MsiApi.MsiDatabaseIsTablePersistent(this, table);
            if (ret == 3)  // MSICONDITION_ERROR
            {
                throw new InstallerException();
            }
            return ret == 1;
        }

        /// <summary>
        /// Checks whether a table contains a persistent column with a given name.
        /// </summary>
        /// <param name="table">The table to the checked</param>
        /// <param name="column">The name of the column to be checked</param>
        /// <returns>true if the column exists in the table; false if the column is temporary or does not exist.</returns>
        /// <exception cref="InstallerException">the View could not be executed</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// To check whether a column exists regardless of persistence,
        /// use <see cref="ColumnCollection.Contains"/>.
        /// </p></remarks>
        public bool IsColumnPersistent(string table, string column)
        {
            if (String.IsNullOrEmpty(table))
            {
                throw new ArgumentNullException("table");
            }
            if (String.IsNullOrEmpty(column))
            {
                throw new ArgumentNullException("column");
            }
            using (View view = this.OpenView(
                "SELECT `Number` FROM `_Columns` WHERE `Table` = '{0}' AND `Name` = '{1}'", table, column))
            {
                view.Execute();
                using (Record rec = view.Fetch())
                {
                    return (rec != null);
                }
            }
        }

        /// <summary>
        /// Gets the count of all rows in the table.
        /// </summary>
        /// <param name="table">Name of the table whose rows are to be counted</param>
        /// <returns>The count of all rows in the table</returns>
        /// <exception cref="InstallerException">the View could not be executed</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        public int CountRows(string table)
        {
            return this.CountRows(table, null);
        }

        /// <summary>
        /// Gets the count of all rows in the table that satisfy a given condition.
        /// </summary>
        /// <param name="table">Name of the table whose rows are to be counted</param>
        /// <param name="where">Conditional expression, such as could be placed on the end of a SQL WHERE clause</param>
        /// <returns>The count of all rows in the table satisfying the condition</returns>
        /// <exception cref="BadQuerySyntaxException">the SQL WHERE syntax is invalid</exception>
        /// <exception cref="InstallerException">the View could not be executed</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        public int CountRows(string table, string where)
        {
            if (String.IsNullOrEmpty(table))
            {
                throw new ArgumentNullException("table");
            }

            // to support temporary tables like _Streams, run the query even if the table isn't persistent
            TableInfo tableInfo = this.Tables[table];
            string primaryKeys = tableInfo == null ? "*" : String.Concat("`", tableInfo.PrimaryKeys[0], "`");
            int count;

            try
            {
                using (View view = this.OpenView(
                    "SELECT {0} FROM `{1}`{2}",
                    primaryKeys,
                    table,
                    (where != null && where.Length != 0 ? " WHERE " + where : "")))
                {
                    view.Execute();
                    for (count = 0; ; count++)
                    {
                        // Avoid creating unnecessary Record objects by not calling View.Fetch().
                        if (MsiApi.MsiViewFetch(view) == null)
                        {
                            break;
                        }
                    }
                }
            }
            catch (BadQuerySyntaxException)
            {
                // table was missing
                count = 0;
            }

            return count;
        }

        /// <summary>
        /// Finalizes the persistent form of the database. All persistent data is written
        /// to the writeable database, and no temporary columns or rows are written.
        /// </summary>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// For a database open in <see cref="DatabaseOpenMode.ReadOnly"/> mode, this method has no effect.
        /// </p><p>
        /// For a database open in <see cref="DatabaseOpenMode.CreateDirect" /> or <see cref="DatabaseOpenMode.Direct" />
        /// mode, it is not necessary to call this method because the database will be automatically committed
        /// when it is closed. However this method may be called at any time to persist the current state of tables
        /// loaded into memory.
        /// </p><p>
        /// For a database open in <see cref="DatabaseOpenMode.Create" /> or <see cref="DatabaseOpenMode.Transact" />
        /// mode, no changes will be persisted until this method is called. If the database object is closed without
        /// calling this method, the database file remains unmodified.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabasecommit.asp">MsiDatabaseCommit</a>
        /// </p></remarks>
        public void Commit()
        {
            if (this.summaryInfo != null && !this.summaryInfo.IsClosed)
            {
                this.summaryInfo.Persist();
                this.summaryInfo.Close();
                this.summaryInfo = null;
            }
            MsiApi.MsiDatabaseCommit(this);
        }

        /// <summary>
        /// Copies the structure and data from a specified table to a text archive file.
        /// </summary>
        /// <param name="table">Name of the table to be exported</param>
        /// <param name="exportFilePath">Path to the file to be created</param>
        /// <exception cref="FileNotFoundException">the file path is invalid</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabaseexport.asp">MsiDatabaseExport</a>
        /// </p></remarks>
        public void Export(string table, string exportFilePath)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            FileInfo file = new FileInfo(exportFilePath);
            MsiApi.MsiDatabaseExport(this, table, file.DirectoryName, file.Name);
        }

        /// <summary>
        /// Imports a database table from a text archive file, dropping any existing table.
        /// </summary>
        /// <param name="importFilePath">Path to the file to be imported. 
        /// The table name is specified within the file.</param>
        /// <exception cref="FileNotFoundException">the file path is invalid</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabaseimport.asp">MsiDatabaseImport</a>
        /// </p></remarks>
        public void Import(string importFilePath)
        {
            if (String.IsNullOrEmpty(importFilePath))
            {
                throw new ArgumentNullException("importFilePath");
            }

            FileInfo file = new FileInfo(importFilePath);
            MsiApi.MsiDatabaseImport(this, file.DirectoryName, file.Name);
        }

        /// <summary>
        /// Exports all database tables, streams, and summary information to archive files.
        /// </summary>
        /// <param name="directoryPath">Path to the directory where archive files will be created</param>
        /// <exception cref="FileNotFoundException">the directory path is invalid</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// The directory will be created if it does not already exist.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabaseexport.asp">MsiDatabaseExport</a>
        /// </p></remarks>
        public void ExportAll(string directoryPath)
        {
            if (String.IsNullOrEmpty(directoryPath))
            {
                throw new ArgumentNullException("directoryPath");
            }

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            this.Export("_SummaryInformation", Path.Combine(directoryPath, "_SummaryInformation.idt"));

            using (View view = this.OpenView("SELECT `Name` FROM `_Tables`"))
            {
                view.Execute();

                foreach (Record rec in view) using (rec)
                {
                    string table = (string) rec[1];

                    this.Export(table, Path.Combine(directoryPath, table + ".idt"));
                }
            }

            if (!Directory.Exists(Path.Combine(directoryPath, "_Streams")))
            {
                Directory.CreateDirectory(Path.Combine(directoryPath, "_Streams"));
            }

            using (View view = this.OpenView("SELECT `Name`, `Data` FROM `_Streams`"))
            {
                view.Execute();

                foreach (Record rec in view) using (rec)
                {
                    string stream = (string) rec[1];
                    if (stream.EndsWith("SummaryInformation", StringComparison.Ordinal)) continue;

                    int i = stream.IndexOf('.');
                    if (i >= 0) 
                    {
                        if (File.Exists(Path.Combine(
                            directoryPath,
                            Path.Combine(stream.Substring(0, i), stream.Substring(i + 1) + ".ibd"))))
                        {
                            continue;
                        }
                    }
                    rec.GetStream(2, Path.Combine(directoryPath, Path.Combine("_Streams", stream)));
                }
            }
        }

        /// <summary>
        /// Imports all database tables, streams, and summary information from archive files.
        /// </summary>
        /// <param name="directoryPath">Path to the directory from which archive files will be imported</param>
        /// <exception cref="FileNotFoundException">the directory path is invalid</exception>
        /// <exception cref="InvalidHandleException">the Database handle is invalid</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msidatabaseimport.asp">MsiDatabaseImport</a>
        /// </p></remarks>
        public void ImportAll(string directoryPath)
        {
            if (String.IsNullOrEmpty(directoryPath))
            {
                throw new ArgumentNullException("directoryPath");
            }

            if (File.Exists(Path.Combine(directoryPath, "_SummaryInformation.idt")))
            {
                this.Import(Path.Combine(directoryPath, "_SummaryInformation.idt"));
            }

            string[] idtFiles = Directory.GetFiles(directoryPath, "*.idt");
            foreach (string file in idtFiles)
            {
                if (Path.GetFileName(file) != "_SummaryInformation.idt")
                {
                    this.Import(file);
                }
            }

            if (Directory.Exists(Path.Combine(directoryPath, "_Streams")))
            {
                View view = this.OpenView("SELECT `Name`, `Data` FROM `_Streams`");
                Record rec = null;
                try
                {
                    view.Execute();
                    string[] streamFiles = Directory.GetFiles(Path.Combine(directoryPath, "_Streams"));
                    foreach (string file in streamFiles)
                    {
                        rec = this.CreateRecord(2);
                        rec[1] = Path.GetFileName(file);
                        rec.SetStream(2, file);
                        view.Insert(rec);
                        rec.Close();
                        rec = null;
                    }
                }
                finally
                {
                    if (rec != null) rec.Close();
                    view.Close();
                }
            }
        }

        /// <summary>
        /// Creates a new record object with the requested number of fields.
        /// </summary>
        /// <param name="fieldCount">Required number of fields, which may be 0.
        /// The maximum number of fields in a record is limited to 65535.</param>
        /// <returns>A new record object that can be used with the database.</returns>
        /// <remarks><p>
        /// This method is equivalent to directly calling the <see cref="Record" />
        /// constructor in all cases outside of a custom action context. When in a
        /// custom action session, this method allows creation of a record that can
        /// work with a database other than the session database.
        /// </p><p>
        /// The Record object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msicreaterecord.asp">MsiCreateRecord</a>
        /// </p></remarks>
        public Record CreateRecord(int fieldCount)
        {
            return new Record(fieldCount);
        }

        /// <summary>
        /// Returns the file path of this database, or the handle value if a file path was not specified.
        /// </summary>
        public override string ToString()
        {
            return this.FilePath ?? base.ToString();
        }

        /// <summary>
        /// Closes the database handle.  After closing a handle, further method calls may throw <see cref="InvalidHandleException"/>.
        /// </summary>
        /// <param name="disposing">If true, the method has been called directly or
        /// indirectly by a user's code, so managed and unmanaged resources will be
        /// disposed. If false, only unmanaged resources will be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsClosed &&
                (this.OpenMode == DatabaseOpenMode.CreateDirect ||
                 this.OpenMode == DatabaseOpenMode.Direct))
            {
                // Always commit a direct-opened database before closing.
                // This avoids unexpected corruption of the database.
                this.Commit();
            }

            if (disposing)
            {
                this.closed = true;
                this.ReleaseReader();

                if (this.summaryInfo != null)
                {
                    this.summaryInfo.Close();
                    this.summaryInfo = null;
                }

                if (this.deleteOnClose != null)
                {
                    foreach (string path in this.deleteOnClose)
                    {
                        try
                        {
                            if (Directory.Exists(path))
                            {
                                Directory.Delete(path, true);
                            }
                            else
                            {
                                if (File.Exists(path)) File.Delete(path);
                            }
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                    this.deleteOnClose = null;
                }
            }
        }

        /// <summary>
        /// Opens a database file for reading, and for writing in the transactional and direct modes.
        /// Engine errors become <see cref="InstallerException"/>.
        /// </summary>
        private static MsiDatabase OpenReader(string filePath, DatabaseOpenMode mode)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException("filePath");
            }

            if (Path.GetExtension(filePath).Equals(".msp", StringComparison.Ordinal))
            {
                // Patch databases are not supported yet.
                mode = (DatabaseOpenMode) ((int) mode | 32);
            }
            try
            {
                return OpenFile(filePath, (DatabaseMode) mode);
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e.Code,
                    String.Format(CultureInfo.InvariantCulture, "Database=\"{0}\"", filePath));
            }
        }

        /// <summary>
        /// Opens a database for the summary information of a file, in transaction mode.
        /// </summary>
        internal static Database OpenForSummary(string filePath)
        {
            return new Database(OpenFile(filePath, DatabaseMode.Transact), filePath, DatabaseOpenMode.Transact);
        }

        /// <summary>
        /// Returns the value of the specified property.
        /// </summary>
        /// <param name="property">Name of the property to retrieve.</param>
        public string ExecutePropertyQuery(string property)
        {
            IList<string> values = this.ExecuteStringQuery("SELECT `Value` FROM `Property` WHERE `Property` = '{0}'", property);
            return (values.Count > 0 ? values[0] : null);
        }

        private readonly Dictionary<string, MutableTable> loadedTables = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> pendingStreams = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private MsiDatabase reader;
        private bool dirty;
        private readonly List<string> addedTables = new List<string>();
        private readonly List<string> removedTables = new List<string>();
        private readonly List<string> alteredTables = new List<string>();
        private int? codePageOverride;
        private readonly Dictionary<string, long> newKeyRanks = new Dictionary<string, long>(StringComparer.Ordinal);
        private const long NewStringBase = 1L << 32;

        internal DatabaseMode Mode { get; }

        /// <summary>Opens the file for reading. Failures are reported as engine error codes.</summary>
        private static MsiDatabase OpenFile(string path, DatabaseMode mode)
        {
            // MSIDBOPEN_READONLY (0), MSIDBOPEN_TRANSACT (1) and MSIDBOPEN_DIRECT (2). Create modes are not supported yet.
            if ((int) mode < 0 || (int) mode > 2)
            {
                throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
            try
            {
                return MsiDatabase.Open(path);
            }
            catch (FileNotFoundException)
            {
                throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
            }
            catch (DirectoryNotFoundException)
            {
                throw new MsiErrorException((uint) MsiError.PATH_NOT_FOUND);
            }
            catch (UnauthorizedAccessException)
            {
                throw new MsiErrorException((uint) MsiError.ACCESS_DENIED);
            }
            catch (ArgumentException)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            catch (IOException)
            {
                throw new MsiErrorException((uint) MsiError.OPEN_FAILED);
            }
            catch (MsiFormatException)
            {
                throw new MsiErrorException((uint) MsiError.INSTALL_PACKAGE_INVALID);
            }
        }

        internal bool ContainsTable(string name)
        {
            return (this.reader.ContainsTable(name) && !this.IsRemoved(name))
                || this.addedTables.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))
                || IsSystemTable(name);
        }

        /// <summary>Finds a table by name, including the system tables _Tables, _Columns and _Streams.</summary>
        internal SqlTable? FindTable(string name)
        {
            switch (name)
            {
                case "_Tables":
                    return this.BuildTablesTable();
                case "_Columns":
                    return this.BuildColumnsTable();
                case "_Streams":
                    return this.BuildStreamsTable();
                default:
                    return this.GetUserTable(name)?.Snapshot;
            }
        }

        /// <summary>The in-memory copy of a table in the database, or null if the database has no such table.</summary>
        internal MutableTable? GetUserTable(string name)
        {
            if (this.IsRemoved(name))
            {
                return null;
            }
            if (this.loadedTables.TryGetValue(name, out MutableTable? loaded))
            {
                return loaded;
            }
            if (!this.reader.TryGetTable(name, out MsiTable? msiTable))
            {
                return null;
            }
            MutableTable table = FromMsi(msiTable);
            this.loadedTables[table.Name] = table;
            return table;
        }

        /// <summary>A plain stream's bytes, including any change not yet committed.</summary>
        internal byte[]? ReadPlainStream(string name)
        {
            return this.pendingStreams.TryGetValue(name, out byte[]? pending) ? pending : this.reader.ReadStream(name);
        }

        /// <summary>Replaces a plain stream. The change is written on Commit, or straight away in Direct mode.</summary>
        internal void PutPlainStream(string name, byte[] data)
        {
            this.RequireWritable();
            this.pendingStreams[name] = data;
            this.Changed();
        }

        internal void RequireWritable()
        {
            if (this.Mode == DatabaseMode.ReadOnly)
            {
                throw MutableTable.Failed();
            }
        }

        /// <summary>Records a change. A Direct database writes it to the file straight away.</summary>
        internal void Changed()
        {
            this.dirty = true;
            if (this.Mode == DatabaseMode.Direct)
            {
                this.WriteChanges();
            }
        }

        /// <summary>
        /// Writes the tables and streams to a temporary file beside the database, then moves it over the original.
        /// Binary cells are written as streams named Table.key, as the format requires.
        /// </summary>
        internal void WriteChanges()
        {
            this.RequireWritable();
            if (!this.dirty)
            {
                return;
            }

            var writeTables = new List<MsiWriteTable>();
            var streams = new Dictionary<string, StreamSource>(StringComparer.Ordinal);
            var binaryNames = new HashSet<string>(StringComparer.Ordinal);
            var binaryTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in this.AllTableNames())
            {
                MutableTable table = this.GetUserTable(name) ?? throw MutableTable.Failed();
                if (table.Columns.Any(c => c.Type == MsiColumnType.Binary))
                {
                    binaryTables.Add(table.Name);
                }
                var columns = table.Columns
                    .Select(c => new MsiWriteColumn(c.Name, c.Type, c.Size, c.IsNullable, c.IsPrimaryKey, c.IsLocalizable))
                    .ToList();

                var rows = new List<object?[]>(table.Rows.Count);
                foreach (object?[] row in table.Rows)
                {
                    var copy = (object?[]) row.Clone();
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        if (table.Columns[i].Type != MsiColumnType.Binary)
                        {
                            continue;
                        }
                        string? streamName = BinaryStreamName(table, row);
                        if (streamName != null)
                        {
                            binaryNames.Add(streamName);
                            if (row[i] is LazyBinary lazy)
                            {
                                streams[streamName] = new StreamSource(lazy.Length, () => lazy.Read() ?? Array.Empty<byte>());
                            }
                            else if (row[i] is byte[] data)
                            {
                                streams[streamName] = StreamSource.Of(data);
                            }
                        }
                        copy[i] = null;
                    }
                    rows.Add(copy);
                }
                writeTables.Add(new MsiWriteTable(table.Name, columns, rows));
            }

            // Plain streams are kept, except binary streams of tables with binary columns: those are owned by
            // the table rows, so a deleted row's stream must not come back.
            foreach (string name in this.reader.StreamNames)
            {
                int dot = name.IndexOf('.');
                if (binaryNames.Contains(name) || (dot > 0 && binaryTables.Contains(name.Substring(0, dot))))
                {
                    continue;
                }
                long? length = this.reader.StreamLength(name);
                if (length is long size)
                {
                    streams[name] = new StreamSource(size, () => this.ReadPlainStream(name) ?? Array.Empty<byte>());
                }
            }
            foreach (KeyValuePair<string, byte[]> pending in this.pendingStreams)
            {
                streams[pending.Key] = StreamSource.Of(pending.Value);
            }

            int codePage = this.ActiveCodePage;
            string directory = Path.GetDirectoryName(Path.GetFullPath(this.filePath)) ?? ".";
            string temp = Path.Combine(directory, "." + Path.GetFileName(this.filePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool moved = false;
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    MsiWriter.WriteSources(output, codePage, writeTables, streams);
                }
                this.reader.Dispose();
                File.Move(temp, this.filePath, overwrite: true);
                moved = true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                throw MutableTable.Failed();
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
                if (!moved)
                {
                    // The original file is untouched, so reopen it for further reads.
                    this.reader = MsiDatabase.Open(this.filePath);
                }
            }

            this.reader = MsiDatabase.Open(this.filePath);
            this.pendingStreams.Clear();
            // The file now holds every change, and the cached rows match it. Their binary cells must read the
            // stream names the new file uses, which differ from the old names when a primary key changed.
            foreach (MutableTable table in this.loadedTables.Values)
            {
                RebindBinary(table);
            }
            this.addedTables.Clear();
            this.removedTables.Clear();
            this.alteredTables.Clear();
            this.dirty = false;
        }

        /// <summary>Points each lazy binary cell of a table at its stream in the current file.</summary>
        private void RebindBinary(MutableTable table)
        {
            var binaryColumns = Enumerable.Range(0, table.Columns.Count).Where(i => table.Columns[i].Type == MsiColumnType.Binary).ToArray();
            if (binaryColumns.Length == 0)
            {
                return;
            }
            foreach (object?[] row in table.Rows)
            {
                foreach (int i in binaryColumns)
                {
                    if (row[i] is not LazyBinary lazy)
                    {
                        continue;
                    }
                    string? name = BinaryStreamName(table, row);
                    row[i] = name == null ? null : new LazyBinary(lazy.Length, () => this.ReadPlainStream(name));
                }
            }
        }

        /// <summary>The stream holding a binary cell: "Table.key1.key2" from the primary-key values, or null if a key is null.</summary>
        private static string? BinaryStreamName(MutableTable table, object?[] row)
        {
            var parts = new List<string> { table.Name };
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (!table.Columns[i].IsPrimaryKey)
                {
                    continue;
                }
                object? key = row[i];
                if (key == null)
                {
                    return null;
                }
                parts.Add(key is int number ? number.ToString(CultureInfo.InvariantCulture) : (string) key);
            }
            return string.Join(".", parts);
        }

        /// <summary>
        /// Applies a transform to the database. The transform is checked in full before any change is made,
        /// so a transform in an unsupported layout leaves the database as it was.
        /// </summary>
        internal void ApplyTransformFile(string transformPath)
        {
            this.RequireWritable();
            MsiTransform transform;
            try
            {
                transform = MsiTransform.Open(transformPath);
            }
            catch (FileNotFoundException)
            {
                throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
            }
            catch (MsiFormatException)
            {
                throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
            }

            using (transform)
            {
                Dictionary<string, MutableTable> newTables = PlanNewTables(transform);
                List<string> deletedTables = PlanDeletedTables(transform);
                var changes = new List<Action>();
                foreach (MutableTable newTable in newTables.Values)
                {
                    MutableTable toAdd = newTable;
                    changes.Add(() => this.AddTable(toAdd));
                }

                foreach (KeyValuePair<string, byte[]> stream in transform.Tables)
                {
                    if (stream.Key == "_Tables" || stream.Key == "_Columns")
                    {
                        continue;
                    }
                    if (deletedTables.Contains(stream.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                    }
                    MutableTable table = this.GetUserTable(stream.Key)
                        ?? (newTables.TryGetValue(stream.Key, out MutableTable? defined) ? defined : null)
                        ?? throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                    changes.AddRange(PlanRows(table, stream.Value, transform, this.reader, this.newKeyRanks));
                }

                foreach (string name in deletedTables)
                {
                    string deleted = name;
                    changes.Add(() => this.RemoveTable(deleted));
                }

                foreach (Action change in changes)
                {
                    change();
                }
                this.Changed();
            }
        }

        /// <summary>
        /// Reads the tables a transform deletes: rows of its _Tables stream with header 0 and the table name as key.
        /// The table must be in the database, and each table can be deleted once.
        /// </summary>
        private List<string> PlanDeletedTables(MsiTransform transform)
        {
            var deleted = new List<string>();
            byte[]? tablesStream = StreamNamed(transform, "_Tables");
            if (tablesStream == null)
            {
                return deleted;
            }
            for (int position = 0; position < tablesStream.Length; position += 4)
            {
                if (ReadWord(tablesStream, position) != 0)
                {
                    continue;
                }
                string name = transform.GetString((uint) ReadWord(tablesStream, position + 2))
                    ?? throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                if (!this.reader.ContainsTable(name) || this.IsRemoved(name))
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                }
                if (deleted.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }
                deleted.Add(name);
            }
            return deleted;
        }

        /// <summary>
        /// Reads the tables a transform defines, from its _Tables and _Columns streams. A table row is 2 words:
        /// the header 0x0101 and the table name. A column row is 5 words: the header 0x0401, the table name,
        /// the column number, the column name and the type word. Columns are kept in the order they appear.
        /// Other layouts are rejected.
        /// </summary>
        private Dictionary<string, MutableTable> PlanNewTables(MsiTransform transform)
        {
            var result = new Dictionary<string, MutableTable>(StringComparer.OrdinalIgnoreCase);
            byte[]? tablesStream = StreamNamed(transform, "_Tables");
            byte[]? columnsStream = StreamNamed(transform, "_Columns");
            if (tablesStream == null && columnsStream == null)
            {
                return result;
            }
            if (tablesStream == null || (columnsStream != null && columnsStream.Length % 10 != 0) || tablesStream.Length % 4 != 0)
            {
                throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
            byte[] columnsData = columnsStream ?? new byte[0];

            var names = new List<string>();
            for (int position = 0; position < tablesStream.Length; position += 4)
            {
                int header = ReadWord(tablesStream, position);
                if (header == 0)
                {
                    // A deleted table. PlanDeletedTables reads it.
                    continue;
                }
                if (header != 0x0101)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                string name = transform.GetString((uint) ReadWord(tablesStream, position + 2))
                    ?? throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                if (this.ContainsTable(name) || names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }
                names.Add(name);
            }

            var columns = names.ToDictionary(n => n, _ => new List<SqlColumn>(), StringComparer.OrdinalIgnoreCase);
            for (int position = 0; position < columnsData.Length; position += 10)
            {
                if (ReadWord(columnsData, position) != 0x0401)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                string tableName = transform.GetString((uint) ReadWord(columnsData, position + 2))
                    ?? throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                string columnName = transform.GetString((uint) ReadWord(columnsData, position + 6))
                    ?? throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                if (!columns.TryGetValue(tableName, out List<SqlColumn>? list))
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }

                // Bit 0x8000 marks a column the transform adds. Only string columns are supported.
                int type = ReadWord(columnsData, position + 8) & 0x7FFF;
                if ((type & 0x0800) == 0)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                list.Add(new SqlColumn(tableName, columnName, MsiColumnType.String, type & 0xFF,
                    (type & 0x1000) != 0, (type & 0x2000) != 0, (type & 0x0200) != 0));
            }

            foreach (string name in names)
            {
                List<SqlColumn> list = columns[name];
                if (list.Count == 0 || !list.Any(c => c.IsPrimaryKey))
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }
                result[name] = new MutableTable(name, list, new List<object?[]>());
            }
            return result;
        }

        /// <summary>Adds a table defined by a transform. Runs only after the whole transform has been checked.</summary>
        private void AddTable(MutableTable table)
        {
            if (this.ContainsTable(table.Name))
            {
                throw MutableTable.Failed();
            }
            this.loadedTables[table.Name] = table;
            this.addedTables.Add(table.Name);
        }

        /// <summary>
        /// Inserts a row of a two-string-column table where Windows Installer keeps it: rows stay in key-pool order.
        /// A key string the file's pool already has sorts by that pool index. A new key string sorts after all of
        /// them, in the order the transform's pool gives. Runs only after the whole transform has been checked.
        /// </summary>
        private static void InsertRanked(MutableTable table, object?[] row, int transformKeyId, MsiDatabase reader, Dictionary<string, long> newKeyRanks)
        {
            if (row[0] is not string key)
            {
                throw MutableTable.Failed();
            }
            long rank = KeyRankForInsert(key, transformKeyId, reader, newKeyRanks);
            int index = table.Rows.Count;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                if (KeyRank(table.Rows[i][0] as string, reader, newKeyRanks) > rank)
                {
                    index = i;
                    break;
                }
            }
            table.InsertAt(index, row);
        }

        private static long KeyRankForInsert(string key, int transformKeyId, MsiDatabase reader, Dictionary<string, long> newKeyRanks)
        {
            uint? id = reader.StringIdOf(key);
            if (id.HasValue)
            {
                return id.Value;
            }
            if (!newKeyRanks.TryGetValue(key, out long rank))
            {
                rank = NewStringBase + transformKeyId;
                newKeyRanks[key] = rank;
            }
            return rank;
        }

        private static long KeyRank(string? key, MsiDatabase reader, Dictionary<string, long> newKeyRanks)
        {
            if (key == null)
            {
                return long.MaxValue;
            }
            uint? id = reader.StringIdOf(key);
            if (id.HasValue)
            {
                return id.Value;
            }
            return newKeyRanks.TryGetValue(key, out long rank) ? rank : long.MaxValue;
        }

        /// <summary>CREATE TABLE: adds a table whose name the database does not have.</summary>
        internal void CreateTable(MutableTable table)
        {
            if (this.ContainsTable(table.Name))
            {
                throw MutableTable.Failed();
            }
            this.PutTable(table);
        }

        /// <summary>DROP TABLE: removes a table the database has. System tables cannot be dropped.</summary>
        internal void DropTable(string name)
        {
            if (IsSystemTable(name) || !this.ContainsTable(name))
            {
                throw MutableTable.Failed();
            }
            this.alteredTables.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (this.addedTables.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                this.loadedTables.Remove(name);
                return;
            }
            this.RemoveTable(name);
        }

        /// <summary>ALTER TABLE ... ADD: a new column, empty in every existing row. A NOT NULL column needs an empty table.</summary>
        internal void AddColumn(string name, SqlColumn column)
        {
            if (IsSystemTable(name))
            {
                throw MutableTable.Failed();
            }
            MutableTable table = this.GetUserTable(name) ?? throw MutableTable.Failed();
            if (table.ColumnIndex(column.Name) >= 0 || (!column.IsNullable && table.Rows.Count > 0))
            {
                throw MutableTable.Failed();
            }
            var columns = table.Columns.Append(column).ToList();
            var rows = table.Rows.Select(r => r.Append((object?) null).ToArray()).ToList();
            this.PutTable(new MutableTable(name, columns, rows));
            if (!this.alteredTables.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            {
                this.alteredTables.Add(name);
            }
        }

        /// <summary>Removes a table defined in the file. Runs only after the whole transform has been checked.</summary>
        private void RemoveTable(string name)
        {
            this.loadedTables.Remove(name);
            this.removedTables.Add(name);
        }

        private bool IsRemoved(string name)
        {
            return this.removedTables.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The tables in the database: those read from the file, then those a transform added.</summary>
        private IEnumerable<string> AllTableNames()
        {
            return this.reader.TableNames.Where(n => !this.IsRemoved(n)).Concat(this.addedTables);
        }

        /// <summary>The column type word as the _Columns table stores it.</summary>
        private static int RawTypeOf(SqlColumn column)
        {
            int raw = 0x0100 | (column.Type == MsiColumnType.String ? 0x0C00 : 0) | (column.Size & 0xFF);
            if (column.IsLocalizable) raw |= 0x0200;
            if (column.IsNullable) raw |= 0x1000;
            if (column.IsPrimaryKey) raw |= 0x2000;
            return raw;
        }

        private static byte[]? StreamNamed(MsiTransform transform, string name)
        {
            foreach (KeyValuePair<string, byte[]> stream in transform.Tables)
            {
                if (stream.Key == name)
                {
                    return stream.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Plans the changes for one transform table stream. The stream is a sequence of rows of 2-byte words.
        /// A header of 0 deletes the row and is followed by its key. A header with bit 0x0200 inserts a row of a
        /// two-string-column table: the key and the value follow. Any other header modifies the row with that key:
        /// bit i of the header marks column i, and one value word follows for each marked column, in column order.
        /// Only string and 16-bit integer columns of tables with a single-column key are modified. Other layouts are
        /// rejected before the database changes.
        /// </summary>
        private static List<Action> PlanRows(MutableTable table, byte[] data, MsiTransform transform, MsiDatabase reader, Dictionary<string, long> newKeyRanks)
        {
            var changes = new List<Action>();
            int position = 0;
            while (position < data.Length)
            {
                if (position + 4 > data.Length)
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }
                int header = ReadWord(data, position);
                string? key = TransformString(data, position + 2, transform);

                if (header == 0)
                {
                    position += 4;
                    changes.Add(() => table.RemoveAt(RequireRow(table, key)));
                    continue;
                }

                if ((header & 0x0200) != 0)
                {
                    RequireTwoStringColumns(table);
                    if (position + 6 > data.Length)
                    {
                        throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                    }
                    string? value = TransformString(data, position + 4, transform);
                    int keyId = ReadWord(data, position + 2);
                    position += 6;
                    changes.Add(() => InsertRanked(table, new object?[] { key, value }, keyId, reader, newKeyRanks));
                    continue;
                }

                if ((header & 0x0001) != 0 || (header & 0xFE00) != 0 || !HasSingleKey(table))
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                var columns = new List<int>();
                for (int bit = 1; bit < 16; bit++)
                {
                    if ((header & (1 << bit)) != 0)
                    {
                        columns.Add(bit);
                    }
                }
                foreach (int column in columns)
                {
                    if (column >= table.Columns.Count || !IsModifiable(table.Columns[column]))
                    {
                        throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                    }
                }
                int end = position + 4 + 2 * columns.Count;
                if (columns.Count == 0 || end > data.Length)
                {
                    throw new MsiErrorException((uint) MsiError.INSTALL_TRANSFORM_FAILURE);
                }
                var values = new object?[columns.Count];
                for (int n = 0; n < columns.Count; n++)
                {
                    values[n] = table.Columns[columns[n]].Type == MsiColumnType.String
                        ? TransformString(data, position + 4 + 2 * n, transform)
                        : TransformInteger(ReadWord(data, position + 4 + 2 * n));
                }
                position = end;
                changes.Add(() =>
                {
                    int index = RequireRow(table, key);
                    object?[] row = (object?[]) table.Rows[index].Clone();
                    for (int n = 0; n < columns.Count; n++)
                    {
                        row[columns[n]] = values[n];
                    }
                    table.Update(new[] { (index, row) });
                });
            }
            return changes;
        }

        /// <summary>A column a transform can change in place: a string column, or a 16-bit integer column.</summary>
        private static bool IsModifiable(SqlColumn column)
        {
            return column.Type == MsiColumnType.String || (column.Type == MsiColumnType.Integer && column.Size is 0 or 2);
        }

        /// <summary>Decodes a 16-bit integer cell of a transform. The value has a 0x8000 bias, and 0 is null.</summary>
        private static object? TransformInteger(int word)
        {
            return word == 0 ? null : (object) (word - 0x8000);
        }

        /// <summary>The index of the row with the given key. A key that is not in the table fails the transform.</summary>
        private static int RequireRow(MutableTable table, string? key)
        {
            int index = table.FindByKey(new object?[] { key });
            if (index < 0)
            {
                throw MutableTable.Failed();
            }
            return index;
        }

        private static bool HasSingleKey(MutableTable table)
        {
            return table.Columns.Count > 0 && table.Columns[0].IsPrimaryKey && table.Columns.Count(c => c.IsPrimaryKey) == 1;
        }

        private static void RequireTwoStringColumns(MutableTable table)
        {
            bool ok = table.Columns.Count == 2
                && table.Columns[0].Type == MsiColumnType.String
                && table.Columns[1].Type == MsiColumnType.String;
            if (!ok)
            {
                throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
        }

        private static int ReadWord(byte[] data, int position)
        {
            return data[position] | (data[position + 1] << 8);
        }

        /// <summary>A key string from the transform's pool. A key cannot be null.</summary>
        private static string? TransformString(byte[] data, int position, MsiTransform transform)
        {
            return transform.GetString((uint) ReadWord(data, position));
        }

        /// <summary>
        /// Writes a table to a file in Windows Installer text (IDT) format. The _ForceCodepage table holds
        /// only the database code page, and Database.CodePage reads it from the third line.
        /// </summary>
        internal void ExportTable(string table, string path)
        {
            string text;
            if (table == "_ForceCodepage")
            {
                text = "CodePage\r\ni2\r\n" + this.ActiveCodePage.ToString(CultureInfo.InvariantCulture) + "\r\n";
            }
            else
            {
                SqlTable sqlTable = this.FindTable(table) ?? throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                text = FormatIdt(sqlTable, folder);
            }
            File.WriteAllText(path, text, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Reads a table from an IDT file and replaces the table of that name, or adds it. A binary cell names a file
        /// in the same folder, and its bytes become the value. Rows are checked as they are inserted.
        /// </summary>
        internal void ImportTable(string path)
        {
            this.RequireWritable();
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
            }
            catch (UnauthorizedAccessException)
            {
                throw new MsiErrorException((uint) MsiError.ACCESS_DENIED);
            }

            if (lines.Length < 3)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }
            if (lines[0].Length == 0 && lines[1].Length == 0)
            {
                // The code page form: two empty header lines, then "<code page>\t_ForceCodepage".
                string[] record = lines[2].Split('\t');
                if (record.Length == 2 && record[1] == "_ForceCodepage"
                    && int.TryParse(record[0], NumberStyles.None, CultureInfo.InvariantCulture, out int codePage))
                {
                    this.SetCodePage(codePage);
                    return;
                }
            }
            string[] names = lines[0].Split('\t');
            string[] types = lines[1].Split('\t');
            string[] keyLine = lines[2].Split('\t');
            if (names.Length != types.Length || keyLine.Length < 2)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }
            string tableName = keyLine[0];
            if (tableName.StartsWith('_') || tableName.Length == 0)
            {
                throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }

            var columns = new List<SqlColumn>();
            for (int i = 0; i < names.Length; i++)
            {
                bool isKey = keyLine.Skip(1).Contains(names[i], StringComparer.Ordinal);
                columns.Add(ParseIdtColumn(tableName, names[i], types[i], isKey));
            }
            if (!columns.Any(c => c.IsPrimaryKey))
            {
                throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }

            var table = new MutableTable(tableName, columns, new List<object?[]>());
            for (int line = 3; line < lines.Length; line++)
            {
                if (lines[line].Length == 0)
                {
                    continue;
                }
                string[] cells = lines[line].Split('\t');
                if (cells.Length != columns.Count)
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                }
                var row = new object?[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    row[i] = ParseIdtCell(columns[i], cells[i], folder);
                }
                table.Insert(row);
            }

            this.PutTable(table);
            this.Changed();
        }

        /// <summary>The database code page. A value set here replaces the file's when the database is committed.</summary>
        internal int ActiveCodePage => this.codePageOverride ?? this.reader.CodePage;

        /// <summary>Sets the database code page. Strings are written in it on Commit.</summary>
        internal void SetCodePage(int codePage)
        {
            this.RequireWritable();
            if (codePage < 0 || codePage > 0xFFFF)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            if (codePage != 0)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    Encoding.GetEncoding(codePage);
                }
                catch (ArgumentException)
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
                }
            }
            this.codePageOverride = codePage;
            this.Changed();
        }

        private static SqlColumn ParseIdtColumn(string tableName, string name, string type, bool isKey)
        {
            if (type.Length < 2)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }
            bool nullable = char.IsUpper(type[0]);
            char letter = char.ToLowerInvariant(type[0]);
            if (!int.TryParse(type.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size < 0)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }
            switch (letter)
            {
                case 'i':
                    if (size != 2 && size != 4)
                    {
                        throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                    }
                    return new SqlColumn(tableName, name, MsiColumnType.Integer, size, nullable, isKey, false);
                case 's':
                    return new SqlColumn(tableName, name, MsiColumnType.String, size, nullable, isKey, false);
                case 'l':
                    return new SqlColumn(tableName, name, MsiColumnType.String, size, nullable, isKey, true);
                case 'v':
                    return new SqlColumn(tableName, name, MsiColumnType.Binary, 0, nullable, isKey, false);
                default:
                    throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
            }
        }

        private static object? ParseIdtCell(SqlColumn column, string text, string folder)
        {
            switch (column.Type)
            {
                case MsiColumnType.String:
                    return text.Length == 0 ? null : text;
                case MsiColumnType.Integer:
                    if (text.Length == 0)
                    {
                        return null;
                    }
                    if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
                    {
                        throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                    }
                    return number;
                case MsiColumnType.Binary:
                    if (text.Length == 0)
                    {
                        return null;
                    }
                    // The file must sit beside the IDT: a name with a folder part is not a binary cell.
                    if (Path.GetFileName(text) != text)
                    {
                        throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                    }
                    try
                    {
                        return File.ReadAllBytes(Path.Combine(folder, text));
                    }
                    catch (FileNotFoundException)
                    {
                        throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw new MsiErrorException((uint) MsiError.FILE_NOT_FOUND);
                    }
                default:
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
        }

        /// <summary>Puts a table in place of the one with its name, or adds it if the database has none.</summary>
        private void PutTable(MutableTable table)
        {
            bool inFile = this.reader.ContainsTable(table.Name);
            this.removedTables.RemoveAll(t => string.Equals(t, table.Name, StringComparison.OrdinalIgnoreCase));
            if (!inFile && !this.addedTables.Any(t => string.Equals(t, table.Name, StringComparison.OrdinalIgnoreCase)))
            {
                this.addedTables.Add(table.Name);
            }
            this.loadedTables[table.Name] = table;
        }

        private static string FormatIdt(SqlTable table, string folder)
        {
            var lines = new List<string>
            {
                string.Join("\t", table.Columns.Select(c => c.Name)),
                string.Join("\t", table.Columns.Select(c => c.TypeCode)),
                string.Join("\t", new[] { table.Name }.Concat(table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name))),
            };
            foreach (object?[] row in table.Rows)
            {
                var cells = new List<string>(row.Length);
                for (int i = 0; i < row.Length; i++)
                {
                    cells.Add(table.Columns[i].Type == MsiColumnType.Binary
                        ? WriteBinaryCell(table, row, i, folder)
                        : FormatIdtValue(row[i]));
                }
                lines.Add(string.Join("\t", cells));
            }
            return string.Join("\r\n", lines) + "\r\n";
        }

        /// <summary>
        /// Writes a binary cell's bytes to a file beside the IDT, named "Table.key1.key2" as the stream is, and returns
        /// that name for the cell. A null binary value is an empty cell.
        /// </summary>
        private static string WriteBinaryCell(SqlTable table, object?[] row, int column, string folder)
        {
            if (LazyBinary.Resolve(row[column]) is not byte[] data)
            {
                return string.Empty;
            }
            var parts = new List<string> { table.Name };
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (table.Columns[i].IsPrimaryKey)
                {
                    parts.Add(FormatIdtValue(row[i]));
                }
            }
            string name = string.Join(".", parts);
            File.WriteAllBytes(Path.Combine(folder, name), data);
            return name;
        }

        private static string FormatIdtValue(object? value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case string text:
                    return text;
                default:
                    return string.Empty; // binary data is stored in separate files, not in the IDT text
            }
        }

        internal void ReleaseReader()
        {
            this.reader.Dispose();
        }

        private static bool IsSystemTable(string name)
        {
            return name == "_Tables" || name == "_Columns" || name == "_Streams";
        }

        private MutableTable FromMsi(MsiTable table)
        {
            var columns = new List<SqlColumn>();
            foreach (MsiColumn column in table.Columns)
            {
                columns.Add(new SqlColumn(table.Name, column.Name, column.Type, column.Size,
                    column.IsNullable, column.IsPrimaryKey, column.IsLocalizable));
            }

            var rows = new List<object?[]>(table.Rows.Count);
            foreach (MsiRow row in table.Rows)
            {
                var values = new object?[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    if (columns[i].Type == MsiColumnType.Binary)
                    {
                        // The bytes stay in the file until something needs them.
                        string? streamName = row.BinaryStreamName(columns[i].Name);
                        long? length = streamName == null ? null : this.reader.StreamLength(streamName);
                        values[i] = length is long size
                            ? new LazyBinary(size, () => this.ReadPlainStream(streamName!))
                            : null;
                    }
                    else
                    {
                        values[i] = row[columns[i].Name];
                    }
                }
                rows.Add(values);
            }

            return new MutableTable(table.Name, columns, rows);
        }

        private SqlTable BuildTablesTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Tables", "Name", MsiColumnType.String, 62, false, true, false),
            };
            var rows = new List<object?[]>();
            foreach (string name in this.AllTableNames())
            {
                rows.Add(new object?[] { name });
            }
            return new SqlTable("_Tables", columns, rows);
        }

        private SqlTable BuildColumnsTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Columns", "Table", MsiColumnType.String, 62, false, true, false),
                new SqlColumn("_Columns", "Number", MsiColumnType.Integer, 2, false, true, false),
                new SqlColumn("_Columns", "Name", MsiColumnType.String, 64, false, true, false),
                new SqlColumn("_Columns", "Type", MsiColumnType.Integer, 2, false, false, false),
            };
            var rows = new List<object?[]>();
            foreach (string tableName in this.AllTableNames())
            {
                if (this.addedTables.Contains(tableName) || this.alteredTables.Contains(tableName))
                {
                    MutableTable added = this.GetUserTable(tableName) ?? throw MutableTable.Failed();
                    for (int i = 0; i < added.Columns.Count; i++)
                    {
                        SqlColumn column = added.Columns[i];
                        rows.Add(new object?[] { added.Name, i + 1, column.Name, RawTypeOf(column) });
                    }
                    continue;
                }
                MsiTable table = this.reader.GetTable(tableName);
                foreach (MsiColumn column in table.Columns)
                {
                    rows.Add(new object?[] { table.Name, column.Number, column.Name, column.RawType });
                }
            }
            return new SqlTable("_Columns", columns, rows);
        }

        private SqlTable BuildStreamsTable()
        {
            var columns = new List<SqlColumn>
            {
                new SqlColumn("_Streams", "Name", MsiColumnType.String, 62, false, true, false),
                new SqlColumn("_Streams", "Data", MsiColumnType.Binary, 0, true, false, false),
            };
            var rows = new List<object?[]>();
            foreach (string name in this.reader.StreamNames)
            {
                rows.Add(new object?[] { name, new LazyBinary(this.reader.StreamLength(name) ?? 0, () => this.ReadPlainStream(name)) });
            }
            return new SqlTable("_Streams", columns, rows);
        }
    }
}
