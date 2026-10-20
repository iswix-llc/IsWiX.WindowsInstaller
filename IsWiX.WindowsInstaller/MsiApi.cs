// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

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

    [Guid("0000000b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IStorage
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        IStream CreateStream([MarshalAs(UnmanagedType.LPWStr)] string wcsName, uint grfMode, uint reserved1, uint reserved2);

        [return: MarshalAs(UnmanagedType.Interface)]
        IStream OpenStream([MarshalAs(UnmanagedType.LPWStr)] string wcsName, IntPtr reserved1, uint grfMode, uint reserved2);

        [return: MarshalAs(UnmanagedType.Interface)]
        IStorage CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string wcsName, uint grfMode, uint reserved1, uint reserved2);

        [return: MarshalAs(UnmanagedType.Interface)]
        IStorage OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string wcsName, IntPtr stgPriority, uint grfMode, IntPtr snbExclude, uint reserved);

        void CopyTo(uint ciidExclude, IntPtr rgiidExclude, IntPtr snbExclude, [MarshalAs(UnmanagedType.Interface)] IStorage stgDest);

        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string wcsName, [MarshalAs(UnmanagedType.Interface)] IStorage stgDest, [MarshalAs(UnmanagedType.LPWStr)] string wcsNewName, uint grfFlags);

        void Commit(uint grfCommitFlags);

        void Revert();

        IntPtr EnumElements(uint reserved1, IntPtr reserved2, uint reserved3);

        void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string wcsName);

        void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string wcsOldName, [MarshalAs(UnmanagedType.LPWStr)] string wcsNewName);

        void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string wcsName, ref FILETIME ctime, ref FILETIME atime, ref FILETIME mtime);

        void SetClass(ref Guid clsid);

        void SetStateBits(uint grfStateBits, uint grfMask);

        void Stat(ref STATSTG statstg, uint grfStatFlag);
    }

    /// <summary>
    /// Managed implementation of the Windows Installer functions used by this library. Databases are
    /// read with MsiReader, so this works on any platform without msi.dll. Operations that would change a
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

        internal static int MsiOpenDatabase(string szDatabasePath, IntPtr uiOpenMode)
        {
            long mode = uiOpenMode.ToInt64();
            return Guard(() =>
            {
                // MSIDBOPEN_READONLY (0), MSIDBOPEN_TRANSACT (1) and MSIDBOPEN_DIRECT (2). Create modes are not supported yet.
                if (mode < 0 || mode > 2)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                return HandleTable.Add(EngineDatabase.Open(szDatabasePath, (DatabaseMode) mode));
            }, String.Format(System.Globalization.CultureInfo.InvariantCulture, "Database=\"{0}\"", szDatabasePath));
        }

        internal static int MsiOpenDatabase(string szDatabasePath, string szPersist)
        {
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static int MsiGetDatabaseState(int hDatabase)
        {
            try
            {
                HandleTable.Get<EngineDatabase>(hDatabase);
                return 0; // MSIDBSTATE_READ
            }
            catch (MsiErrorException)
            {
                return -1; // MSIDBSTATE_ERROR
            }
        }

        internal static int MsiDatabaseOpenView(int hDatabase, string szQuery)
        {
            return Guard(() =>
            {
                EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                if (szQuery == null)
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
                }
                SqlStatement statement = new SqlParser(szQuery).ParseStatement();
                EngineView view = statement switch
                {
                    SqlSelect select => new EngineView(database, BoundSelect.Bind(select, database)),
                    SqlCreateTable or SqlDropTable or SqlAlterTableAdd => new EngineView(database, BoundDdl.Bind(statement, database)),
                    _ => new EngineView(database, BoundWrite.Bind(statement, database)),
                };
                return HandleTable.Add(view);
            });
        }

        internal static MergeOutcome MsiDatabaseMerge(int hDatabase, int hDatabaseMerge, string szTableName)
        {
            // Merging is not implemented yet.
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiDatabaseCommit(int hDatabase)
        {
            Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Commit());
        }

        internal static int MsiDatabaseGetPrimaryKeys(int hDatabase, string szTableName)
        {
            return Guard(() =>
            {
                EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                SqlTable table = database.FindTable(szTableName)
                    ?? throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                object?[] keys = Array.ConvertAll(
                    System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(table.Columns, c => c.IsPrimaryKey)),
                    c => (object?) c.Name);
                return HandleTable.Add(new EngineRecord(keys));
            });
        }

        internal static uint MsiDatabaseIsTablePersistent(int hDatabase, string szTableName)
        {
            try
            {
                EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                return database.ContainsTable(szTableName) ? 1u : 2u; // MSICONDITION_TRUE or MSICONDITION_NONE
            }
            catch (MsiErrorException)
            {
                return 3u; // MSICONDITION_ERROR
            }
        }

        internal static void MsiDatabaseExport(int hDatabase, string szTableName, string szFolderPath, string szFileName)
        {
            Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Export(szTableName, Path.Combine(szFolderPath, szFileName)));
        }

        internal static void MsiDatabaseImport(int hDatabase, string szFolderPath, string szFileName)
        {
            Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Import(Path.Combine(szFolderPath, szFileName)));
        }

        internal static bool MsiDatabaseGenerateTransform(int hDatabase, int hDatabaseReference, string szTransformFile, int iReserved1, int iReserved2)
        {
            // Generating transforms is not implemented yet.
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiCreateTransformSummaryInfo(int hDatabase, int hDatabaseReference, string szTransformFile, int iErrorConditions, int iValidation)
        {
            throw InstallerException.ExceptionFromReturnCode((uint) MsiError.CALL_NOT_IMPLEMENTED);
        }

        internal static void MsiDatabaseApplyTransform(int hDatabase, string szTransformFile, int iErrorConditions)
        {
            Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).ApplyTransform(szTransformFile));
        }

        internal static void MsiViewExecute(int hView, int hRecord)
        {
            Guard(() =>
            {
                EngineView view = HandleTable.Get<EngineView>(hView);
                object?[] markers = hRecord == 0
                    ? Array.Empty<object?>()
                    : HandleTable.Get<EngineRecord>(hRecord).Values();
                view.Execute(markers);
            });
        }

        /// <summary>Fetches the next record, returning its handle, or null when the view has no more records.</summary>
        internal static int? MsiViewFetch(int hView)
        {
            return Guard(() =>
            {
                EngineRecord? record = HandleTable.Get<EngineView>(hView).Fetch();
                return record == null ? (int?) null : HandleTable.Add(record);
            });
        }

        internal static ModifyOutcome MsiViewModify(int hView, int iModifyMode, int hRecord)
        {
            try
            {
                ModifyRecord(hView, iModifyMode, hRecord);
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
                throw InstallerException.ExceptionFromReturnCode(e.Code);
            }
        }

        private static void ModifyRecord(int hView, int iModifyMode, int hRecord)
        {
            EngineView view = HandleTable.Get<EngineView>(hView);
            EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
            if (iModifyMode == (int) ViewModifyMode.Refresh || iModifyMode == (int) ViewModifyMode.Seek)
            {
                // These fill the record in place rather than change the table.
                object?[] found = view.Lookup(record.Values());
                for (int j = 0; j < found.Length; j++)
                {
                    record.Set((uint) (j + 1), found[j]);
                }
                return;
            }
            view.Modify(iModifyMode, record.Values());
        }

        /// <summary>
        /// Returns the next validation error for the last modification. The engine keeps no per-column error
        /// information yet, so the error is always 0 (none).
        /// </summary>
        internal static (int Error, string Column) MsiViewGetError(int hView)
        {
            return (0, string.Empty);
        }

        internal static int MsiViewGetColumnInfo(int hView, uint eColumnInfo)
        {
            return Guard(() =>
            {
                EngineView view = HandleTable.Get<EngineView>(hView);
                IReadOnlyList<SqlColumn> columns = view.Query?.OutputColumns
                    ?? throw new MsiErrorException((uint) MsiError.FUNCTION_FAILED);

                // MSICOLINFO_NAMES is 0 and MSICOLINFO_TYPES is 1.
                var values = new object?[columns.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    SqlColumn column = columns[i];
                    values[i] = eColumnInfo == 0 ? column.Name : column.TypeCode;
                }
                return HandleTable.Add(new EngineRecord(values));
            });
        }

        internal static int MsiCreateRecord(uint cParams)
        {
            try
            {
                return HandleTable.Add(new EngineRecord((int) cParams));
            }
            catch (MsiErrorException)
            {
                return 0;
            }
        }

        internal static string MsiFormatRecord(int hRecord)
        {
            return Guard(() =>
            {
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
                string template = record.GetString(0);
                return Regex.Replace(template, @"\[(\d+)\]", match =>
                {
                    int field = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return field >= 1 && field <= record.FieldCount ? record.GetString((uint) field) : match.Value;
                });
            });
        }

        internal static void MsiRecordClearData(int hRecord)
        {
            Guard(() => HandleTable.Get<EngineRecord>(hRecord).Clear());
        }

        internal static uint MsiRecordGetFieldCount(int hRecord)
        {
            try
            {
                return (uint) HandleTable.Get<EngineRecord>(hRecord).FieldCount;
            }
            catch (MsiErrorException)
            {
                return 0;
            }
        }

        internal static bool MsiRecordIsNull(int hRecord, uint iField)
        {
            try
            {
                return HandleTable.Get<EngineRecord>(hRecord)[iField] == null;
            }
            catch (MsiErrorException)
            {
                return true;
            }
        }

        internal static int MsiRecordGetInteger(int hRecord, uint iField)
        {
            try
            {
                return HandleTable.Get<EngineRecord>(hRecord).GetInteger(iField);
            }
            catch (MsiErrorException)
            {
                return int.MinValue; // MSI_NULL_INTEGER
            }
        }

        internal static string MsiRecordGetString(int hRecord, uint iField)
        {
            return Guard(() => HandleTable.Get<EngineRecord>(hRecord).GetString(iField));
        }

        internal static void MsiRecordSetInteger(int hRecord, uint iField, int iValue)
        {
            Guard(() =>
            {
                // MSI_NULL_INTEGER clears the field.
                HandleTable.Get<EngineRecord>(hRecord).Set(iField, iValue == int.MinValue ? null : (object) iValue);
            });
        }

        internal static void MsiRecordSetString(int hRecord, uint iField, string szValue)
        {
            Guard(() =>
            {
                HandleTable.Get<EngineRecord>(hRecord).Set(iField, string.IsNullOrEmpty(szValue) ? null : szValue);
            });
        }

        internal static uint MsiRecordDataSize(int hRecord, uint iField)
        {
            try
            {
                object? value = HandleTable.Get<EngineRecord>(hRecord)[iField];
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
        internal static int MsiRecordReadStream(int hRecord, uint iField, byte[] buffer, int count)
        {
            return Guard(() =>
            {
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
                object? value = record[iField];
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

        internal static void MsiRecordSetStream(int hRecord, uint iField, string szFilePath)
        {
            Guard(() =>
            {
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
                byte[]? data = szFilePath == null ? null : File.ReadAllBytes(szFilePath);
                record.Set(iField, data);
            });
        }

        /// <summary>
        /// Opens summary information for a database handle or a package file, returning its handle. A
        /// non-zero update count asks for a handle that can be written.
        /// </summary>
        internal static int MsiGetSummaryInformation(int hDatabase, string szDatabasePath, uint uiUpdateCount)
        {
            try
            {
                return HandleTable.Add(OpenSummary(hDatabase, szDatabasePath, uiUpdateCount));
            }
            catch (MsiErrorException e) when (szDatabasePath != null && (e.Code == (uint) MsiError.FILE_NOT_FOUND || e.Code == (uint) MsiError.ACCESS_DENIED))
            {
                throw new FileNotFoundException(null, szDatabasePath);
            }
            catch (MsiErrorException e)
            {
                throw InstallerException.ExceptionFromReturnCode(e.Code);
            }
        }

        private static EngineSummary OpenSummary(int hDatabase, string szDatabasePath, uint uiUpdateCount)
        {
            bool writable = uiUpdateCount > 0;
            if (hDatabase != 0)
            {
                EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                if (writable)
                {
                    database.RequireWritable();
                }
                return EngineSummary.Open(database, ownsDatabase: false, writable);
            }

            if (szDatabasePath == null)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            if (!writable)
            {
                // Read-only summaries come straight from the file, so transforms can be read too.
                return EngineSummary.OpenFile(szDatabasePath);
            }

            EngineDatabase opened = EngineDatabase.Open(szDatabasePath, DatabaseMode.Transact);
            return EngineSummary.Open(opened, ownsDatabase: true, writable);
        }

        /// <summary>Gets a summary property. Text is set only for string properties.</summary>
        internal static (uint DataType, int Number, long FileTime, string Text) MsiSummaryInfoGetProperty(int hSummaryInfo, uint uiProperty)
        {
            return Guard(() =>
            {
                HandleTable.Get<EngineSummary>(hSummaryInfo).Get(uiProperty, out uint type, out int number, out long fileTime, out string? text);
                string value = type == SummaryStream.VtLpstr ? text ?? string.Empty : string.Empty;
                return (type, number, fileTime, value);
            });
        }

        internal static void MsiSummaryInfoSetProperty(int hSummaryInfo, uint uiProperty, uint uiDataType, int iValue, long ftValue, string szValue)
        {
            Guard(() => HandleTable.Get<EngineSummary>(hSummaryInfo).Set(uiProperty, uiDataType, iValue, ftValue, szValue));
        }

        internal static void MsiSummaryInfoPersist(int hSummaryInfo)
        {
            Guard(() => HandleTable.Get<EngineSummary>(hSummaryInfo).Persist());
        }

        /// <summary>Closes a handle. Closing a handle that is already closed is not an error.</summary>
        internal static void MsiCloseHandle(int hAny)
        {
            try
            {
                HandleTable.Close(hAny);
            }
            catch (MsiErrorException)
            {
            }
        }

        internal static int MsiGetLastErrorRecord()
        {
            // Errors are thrown, so there is never a separate error record.
            return 0;
        }

        /// <summary>Returns the system text for an error code, or null when none is available.</summary>
        internal static string? FormatMessage(uint messageId)
        {
            // No system message table is available outside Windows.
            return null;
        }

        internal static IStorage StgOpenStorage(string wcsName, uint grfMode)
        {
            throw new NotImplementedException();
        }

        internal static IStorage StgCreateDocfile(string wcsName, uint grfMode)
        {
            throw new NotImplementedException();
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
                throw InstallerException.ExceptionFromReturnCode(e.Code, message);
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
                throw InstallerException.ExceptionFromReturnCode(e.Code, message);
            }
        }
    }
}
