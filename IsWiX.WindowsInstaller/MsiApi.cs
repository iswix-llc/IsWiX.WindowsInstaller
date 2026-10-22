namespace IsWiX.WindowsInstaller
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Runtime.InteropServices;
    using System.Diagnostics.CodeAnalysis;
    using IStream  = System.Runtime.InteropServices.ComTypes.IStream;
    using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
    using STATSTG  = System.Runtime.InteropServices.ComTypes.STATSTG;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>
    /// Managed implementation of the Windows Installer functions used by this library. Databases are
    /// read by the format layer, so this works on any platform without msi.dll. Operations that would change a
    /// database, and summary information, throw ERROR_CALL_NOT_IMPLEMENTED for now.
    /// </summary>
    /// <remarks><p>
    /// Each function returns its result directly. A Windows Installer error is thrown as the matching
    /// <see cref="InstallerException"/> subtype, so callers never inspect return codes. Results that
    /// callers branch on are returned as a value or a named outcome.
    /// </p></remarks>
    internal static class MsiApi
    {
        /// <summary>Outcome of a view modification for the cases callers handle themselves.</summary>
        internal enum ModifyOutcome
        {
            Ok,
            FunctionFailed,
            InvalidData,
        }

        /// <summary>Outcome of a database merge for the cases callers report differently.</summary>
        internal enum MergeOutcome
        {
            Merged,
            Conflict,
            SchemaMismatch,
        }

        internal enum SourceType : int
        {
            Unknown = 0,
            Network = 1,
            Url = 2,
            Media = 3,
        }

        [Flags]
        internal enum STGM : uint
        {
            DIRECT           = 0x00000000,
            TRANSACTED       = 0x00010000,
            SIMPLE           = 0x08000000,

            READ             = 0x00000000,
            WRITE            = 0x00000001,
            READWRITE        = 0x00000002,

            SHARE_DENY_NONE  = 0x00000040,
            SHARE_DENY_READ  = 0x00000030,
            SHARE_DENY_WRITE = 0x00000020,
            SHARE_EXCLUSIVE  = 0x00000010,

            PRIORITY         = 0x00040000,
            DELETEONRELEASE  = 0x04000000,
            NOSCRATCH        = 0x00100000,

            CREATE           = 0x00001000,
            CONVERT          = 0x00020000,
            FAILIFTHERE      = 0x00000000,

            NOSNAPSHOT       = 0x00200000,
            DIRECT_SWMR      = 0x00400000,
        }

        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        internal struct MsiPatchSequenceData
        {
            public string szPatchData;
            public int ePatchDataType;
            public int dwOrder;
            public uint dwStatus;
        }

        internal static int MsiGetDatabaseState(Database database)
        {
            database.ThrowIfClosed();
            // MSIDBSTATE_READ (0) for a read-only database, MSIDBSTATE_WRITE (1) for any other mode.
            return database.OpenMode == DatabaseOpenMode.ReadOnly ? 0 : 1;
        }

        internal static View MsiDatabaseOpenView(Database database, string szQuery)
        {
            return Guard(() =>
            {
                database.ThrowIfClosed();
                if (szQuery == null)
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
                }
                SqlStatement statement = new SqlParser(szQuery).ParseStatement();
                return statement switch
                {
                    SqlSelect select => new View(database, szQuery, BoundSelect.Bind(select, database)),
                    SqlCreateTable or SqlDropTable or SqlAlterTableAdd => new View(database, szQuery, BoundDdl.Bind(statement, database)),
                    _ => new View(database, szQuery, BoundWrite.Bind(statement, database)),
                };
            });
        }

        internal static MergeOutcome MsiDatabaseMerge(Database database, Database databaseMerge, string szTableName)
        {
            database.ThrowIfClosed();
            databaseMerge.ThrowIfClosed();
            // Merging is not implemented yet.
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiDatabaseCommit(Database database)
        {
            Guard(() =>
            {
                database.ThrowIfClosed();
                database.WriteChanges();
            });
        }

        internal static Record MsiDatabaseGetPrimaryKeys(Database database, string szTableName)
        {
            return Guard(() =>
            {
                database.ThrowIfClosed();
                SqlTable table = database.FindTable(szTableName)
                    ?? throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                object?[] keys = Array.ConvertAll(
                    System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(table.Columns, c => c.IsPrimaryKey)),
                    c => (object?) c.Name);
                return new Record(keys, (View) null);
            });
        }

        internal static uint MsiDatabaseIsTablePersistent(Database database, string szTableName)
        {
            try
            {
                database.ThrowIfClosed();
                return database.ContainsTable(szTableName) ? 1u : 2u; // MSICONDITION_TRUE or MSICONDITION_NONE
            }
            catch (MsiErrorException)
            {
                return 3u; // MSICONDITION_ERROR
            }
        }

        internal static void MsiDatabaseExport(Database database, string szTableName, string szFolderPath, string szFileName)
        {
            Guard(() =>
            {
                database.ThrowIfClosed();
                database.ExportTable(szTableName, Path.Combine(szFolderPath, szFileName));
            });
        }

        internal static void MsiDatabaseImport(Database database, string szFolderPath, string szFileName)
        {
            Guard(() =>
            {
                database.ThrowIfClosed();
                database.ImportTable(Path.Combine(szFolderPath, szFileName));
            });
        }

        internal static bool MsiDatabaseGenerateTransform(Database database, Database databaseReference, string szTransformFile, int iReserved1, int iReserved2)
        {
            // Generating transforms is not implemented yet.
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiCreateTransformSummaryInfo(Database database, Database databaseReference, string szTransformFile, int iErrorConditions, int iValidation)
        {
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiDatabaseApplyTransform(Database database, string szTransformFile, int iErrorConditions)
        {
            Guard(() =>
            {
                database.ThrowIfClosed();
                database.ApplyTransformFile(szTransformFile);
            });
        }

        internal static void MsiViewExecute(View view, Record? record)
        {
            Guard(() =>
            {
                view.ThrowIfClosed();
                if (record != null)
                {
                    record.ThrowIfClosed();
                }
                object?[] markers = record == null ? Array.Empty<object?>() : record.Values();
                view.RunStatement(markers);
            });
        }

        /// <summary>Fetches the next record, returning its handle, or null when the view has no more records.</summary>
        internal static Record? MsiViewFetch(View view)
        {
            return Guard(() =>
            {
                view.ThrowIfClosed();
                return view.NextRow();
            });
        }

        internal static ModifyOutcome MsiViewModify(View view, int iModifyMode, Record record)
        {
            try
            {
                ModifyRecord(view, iModifyMode, record);
                return ModifyOutcome.Ok;
            }
            catch (MsiErrorException e) when (e.Code == (uint) MsiError.FUNCTION_FAILED)
            {
                return ModifyOutcome.FunctionFailed;
            }
            catch (MsiErrorException e) when (e.Code == (uint) MsiError.INVALID_DATA)
            {
                return ModifyOutcome.InvalidData;
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e);
            }
        }

        private static void ModifyRecord(View view, int iModifyMode, Record record)
        {
            view.ThrowIfClosed();
            record.ThrowIfClosed();
            if (iModifyMode == (int) ViewModifyMode.Refresh || iModifyMode == (int) ViewModifyMode.Seek)
            {
                // These fill the record in place rather than change the table.
                object?[] found = view.LookupRow(record.Values());
                for (int j = 0; j < found.Length; j++)
                {
                    record.SetValue((uint) (j + 1), found[j]);
                }
                return;
            }
            view.ModifyRow(iModifyMode, record.Values());
        }

        /// <summary>
        /// Returns the next validation error for the last modification. The engine keeps no per-column error
        /// information yet, so the error is always 0 (none).
        /// </summary>
        internal static (int Error, string Column) MsiViewGetError(View view)
        {
            return (0, string.Empty);
        }

        internal static Record MsiViewGetColumnInfo(View view, uint eColumnInfo)
        {
            return Guard(() =>
            {
                view.ThrowIfClosed();
                IReadOnlyList<SqlColumn> columns = view.Query?.OutputColumns
                    ?? throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);

                // MSICOLINFO_NAMES is 0 and MSICOLINFO_TYPES is 1.
                var values = new object?[columns.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    SqlColumn column = columns[i];
                    values[i] = eColumnInfo == 0 ? column.Name : column.TypeCode;
                }
                return new Record(values, (View) null);
            });
        }

        internal static string MsiFormatRecord(Record record)
        {
            return Guard(() =>
            {
                record.ThrowIfClosed();
                string template = record.ReadString(0);
                return Regex.Replace(template, @"\[(\d+)\]", match =>
                {
                    int field = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return field >= 1 && field <= record.FieldCount ? record.ReadString((uint) field) : match.Value;
                });
            });
        }

        internal static void MsiRecordClearData(Record record)
        {
            Guard(() =>
            {
                record.ThrowIfClosed();
                record.ClearValues();
            });
        }

        internal static bool MsiRecordIsNull(Record record, uint iField)
        {
            try
            {
                record.ThrowIfClosed();
                return record.ValueAt(iField) == null;
            }
            catch (MsiErrorException)
            {
                return true;
            }
        }

        internal static int MsiRecordGetInteger(Record record, uint iField)
        {
            try
            {
                record.ThrowIfClosed();
                return record.ReadInteger(iField);
            }
            catch (MsiErrorException)
            {
                return int.MinValue; // MSI_NULL_INTEGER
            }
        }

        internal static string MsiRecordGetString(Record record, uint iField)
        {
            return Guard(() =>
            {
                record.ThrowIfClosed();
                return record.ReadString(iField);
            });
        }

        internal static void MsiRecordSetInteger(Record record, uint iField, int iValue)
        {
            Guard(() =>
            {
                record.ThrowIfClosed();
                // MSI_NULL_INTEGER clears the field.
                record.SetValue(iField, iValue == int.MinValue ? null : (object) iValue);
            });
        }

        internal static void MsiRecordSetString(Record record, uint iField, string szValue)
        {
            Guard(() =>
            {
                record.ThrowIfClosed();
                record.SetValue(iField, string.IsNullOrEmpty(szValue) ? null : szValue);
            });
        }

        internal static uint MsiRecordDataSize(Record record, uint iField)
        {
            try
            {
                record.ThrowIfClosed();
                object? value = record.ValueAt(iField);
                switch (value)
                {
                    case null:
                        return 0;
                    case string text:
                        return (uint) text.Length;
                    case byte[] data:
                        return (uint) data.Length;
                    default:
                        return 4;
                }
            }
            catch (MsiErrorException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> bytes of a binary field into <paramref name="buffer"/>, returning
        /// the number of bytes read. Each read continues where the previous one stopped, as the Windows Installer
        /// API does.
        /// </summary>
        internal static int MsiRecordReadStream(Record record, uint iField, byte[] buffer, int count)
        {
            return Guard(() =>
            {
                record.ThrowIfClosed();
                object? value = record.ValueAt(iField);
                if (value == null)
                {
                    return 0;
                }
                if (value is not byte[] data)
                {
                    throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
                }

                int offset = record.ReadOffset(iField);
                int read = Math.Min(count, Math.Max(0, data.Length - offset));
                if (buffer != null && read > 0)
                {
                    Array.Copy(data, offset, buffer, 0, read);
                }
                record.SetReadOffset(iField, offset + read);
                return read;
            });
        }

        internal static void MsiRecordSetStream(Record record, uint iField, string szFilePath)
        {
            Guard(() =>
            {
                record.ThrowIfClosed();
                byte[]? data = szFilePath == null ? null : File.ReadAllBytes(szFilePath);
                record.SetValue(iField, data);
            });
        }

        /// <summary>
        /// Opens summary information for a database handle or a package file, returning its handle. A
        /// non-zero update count asks for a handle that can be written.
        /// </summary>
        internal static SummaryInfo MsiGetSummaryInformation(Database database, string szDatabasePath, uint uiUpdateCount)
        {
            try
            {
                return OpenSummary(database, szDatabasePath, uiUpdateCount);
            }
            catch (MsiErrorException e) when (szDatabasePath != null && (e.Code == (uint) MsiError.FILE_NOT_FOUND || e.Code == (uint) MsiError.ACCESS_DENIED))
            {
                throw new FileNotFoundException(null, szDatabasePath);
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e);
            }
        }

        private static SummaryInfo OpenSummary(Database database, string szDatabasePath, uint uiUpdateCount)
        {
            bool writable = uiUpdateCount > 0;
            if (database != null)
            {
                database.ThrowIfClosed();
                if (writable)
                {
                    database.RequireWritable();
                }
                return SummaryInfo.Open(database, ownsDatabase: false, writable);
            }

            if (szDatabasePath == null)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            if (!writable)
            {
                // Read-only summaries come straight from the file, so transforms can be read too.
                return SummaryInfo.OpenFile(szDatabasePath);
            }

            Database opened = Database.OpenForSummary(szDatabasePath);
            return SummaryInfo.Open(opened, ownsDatabase: true, writable);
        }

        /// <summary>Gets a summary property. Text is set only for string properties.</summary>
        internal static (uint DataType, int Number, long FileTime, string Text) MsiSummaryInfoGetProperty(SummaryInfo summary, uint uiProperty)
        {
            return Guard(() =>
            {
                summary.ThrowIfClosed();
                summary.Get(uiProperty, out uint type, out int number, out long fileTime, out string? text);
                string value = type == SummaryStream.VtLpstr ? text ?? string.Empty : string.Empty;
                return (type, number, fileTime, value);
            });
        }

        internal static void MsiSummaryInfoSetProperty(SummaryInfo summary, uint uiProperty, uint uiDataType, int iValue, long ftValue, string szValue)
        {
            Guard(() =>
            {
                summary.ThrowIfClosed();
                summary.Set(uiProperty, uiDataType, iValue, ftValue, szValue);
            });
        }

        internal static void MsiSummaryInfoPersist(SummaryInfo summary)
        {
            Guard(() =>
            {
                summary.ThrowIfClosed();
                summary.WriteProperties();
            });
        }

        internal static Record? MsiGetLastErrorRecord()
        {
            // Errors are thrown, so there is never a separate error record.
            return null;
        }

        /// <summary>Returns the system text for an error code, or null when none is available.</summary>
        internal static string? FormatMessage(uint messageId)
        {
            // No system message table is available outside Windows.
            return null;
        }

        /// <summary>Runs an operation, throwing a Windows Installer error as its matching exception.</summary>
        private static void Guard(Action operation, string? message = null)
        {
            try
            {
                operation();
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e, message);
            }
        }

        /// <summary>Runs an operation that returns a value, throwing a Windows Installer error as its matching exception.</summary>
        private static T Guard<T>(Func<T> operation, string? message = null)
        {
            try
            {
                return operation();
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e, message);
            }
        }
    }
}
