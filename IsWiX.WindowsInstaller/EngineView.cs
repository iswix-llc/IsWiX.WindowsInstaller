namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using MsiReader;

    /// <summary>
    /// A prepared statement. A SELECT returns rows from Fetch; an INSERT, UPDATE or DELETE changes the
    /// database when it is executed and returns no rows. Modify applies one record to a single-table view.
    /// </summary>
    internal sealed class EngineView
    {
        private readonly Database database;
        private readonly BoundWrite? write;
        private readonly BoundDdl? ddl;
        private bool executed;
        private int position;

        public EngineView(Database database, BoundSelect query)
        {
            this.database = database;
            this.Query = query;
        }

        public EngineView(Database database, BoundWrite write)
        {
            this.database = database;
            this.write = write;
        }

        public EngineView(Database database, BoundDdl ddl)
        {
            this.database = database;
            this.ddl = ddl;
        }

        /// <summary>The SELECT this view runs, or null for a write statement.</summary>
        public BoundSelect? Query { get; }

        public SqlResult? Result { get; private set; }

        public int MarkerCount => this.Query?.MarkerCount ?? this.write?.MarkerCount ?? this.ddl!.MarkerCount;

        public void Execute(object?[] markers)
        {
            if (this.write != null || this.ddl != null)
            {
                if (this.write != null)
                {
                    this.write.Execute(markers);
                }
                else
                {
                    this.ddl!.Execute(markers);
                }
                this.executed = true;
                return;
            }
            this.Result = this.Query!.Execute(markers);
            this.position = 0;
        }

        /// <summary>Returns the next row as a record, or null when the rows are exhausted.</summary>
        public EngineRecord? Fetch()
        {
            if (this.write != null || this.ddl != null)
            {
                if (!this.executed)
                {
                    throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
                }
                return null;
            }
            if (this.Result == null)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            if (this.position >= this.Result.Rows.Count)
            {
                return null;
            }
            return new EngineRecord(this.Result.Rows[this.position++]);
        }

        /// <summary>Applies one record to the view's table, as MsiViewModify does.</summary>
        public void Modify(int mode, object?[] values)
        {
            if (this.Query == null)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            if (mode >= (int) ViewModifyMode.Validate && mode <= (int) ViewModifyMode.ValidateDelete)
            {
                ViewModify.Check(this.database, this.Query, mode, values);
                return;
            }
            ViewModify.Apply(this.database, this.Query, mode, values);
        }

        /// <summary>The view's values for the row whose key the record names, as MsiViewModify's Refresh and Seek give them.</summary>
        public object?[] Lookup(object?[] values)
        {
            if (this.Query == null)
            {
                throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);
            }
            return ViewModify.Lookup(this.database, this.Query, values);
        }
    }
}
