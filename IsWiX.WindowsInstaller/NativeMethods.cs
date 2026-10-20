// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Text;
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
    /// database, and summary information, return ERROR_CALL_NOT_IMPLEMENTED for now.
    /// </summary>
    internal static class NativeMethods
    {
        internal enum Error : uint
        {
            SUCCESS = 0,
            FILE_NOT_FOUND = 2,
            PATH_NOT_FOUND = 3,
            ACCESS_DENIED = 5,
            INVALID_HANDLE = 6,
            INVALID_DATA = 13,
            INVALID_PARAMETER = 87,
            OPEN_FAILED = 110,
            DISK_FULL = 112,
            CALL_NOT_IMPLEMENTED = 120,
            BAD_PATHNAME = 161,
            NO_DATA = 232,
            MORE_DATA = 234,
            NO_MORE_ITEMS = 259,
            DIRECTORY = 267,
            INSTALL_USEREXIT = 1602,
            INSTALL_FAILURE = 1603,
            FILE_INVALID = 1006,
            UNKNOWN_PRODUCT = 1605,
            UNKNOWN_FEATURE = 1606,
            UNKNOWN_COMPONENT = 1607,
            UNKNOWN_PROPERTY = 1608,
            INVALID_HANDLE_STATE = 1609,
            INSTALL_SOURCE_ABSENT = 1612,
            BAD_QUERY_SYNTAX = 1615,
            INSTALL_PACKAGE_INVALID = 1620,
            FUNCTION_FAILED = 1627,
            INVALID_TABLE = 1628,
            DATATYPE_MISMATCH = 1629,
            CREATE_FAILED = 1631,
            SUCCESS_REBOOT_INITIATED = 1641,
            SUCCESS_REBOOT_REQUIRED = 3010,
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

        internal class MsiHandle : SafeHandle
        {
            [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
            public MsiHandle(IntPtr handle, bool ownsHandle)
                : base(handle, ownsHandle)
            {
            }

            public override bool IsInvalid
            {
                get
                {
                    return this.handle == IntPtr.Zero;
                }
            }

            public static implicit operator IntPtr(MsiHandle msiHandle)
            {
                return msiHandle.handle;
            }

            protected override bool ReleaseHandle()
            {
                    return true;
            }
        }

        internal static uint MsiOpenDatabase(string szDatabasePath, IntPtr uiOpenMode, out int hDatabase)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                // MSIDBOPEN_READONLY (0), MSIDBOPEN_TRANSACT (1) and MSIDBOPEN_DIRECT (2). Create modes are not supported yet.
                long mode = uiOpenMode.ToInt64();
                if (mode < 0 || mode > 2)
                {
                    throw new MsiErrorException((uint) Error.CALL_NOT_IMPLEMENTED);
                }
                handle = ManagedHandles.Add(ManagedDatabase.Open(szDatabasePath, (DatabaseMode) mode));
            });
            hDatabase = handle;
            return result;
        }

        internal static uint MsiOpenDatabase(string szDatabasePath, string szPersist, out int hDatabase)
        {
            hDatabase = 0;
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static int MsiGetDatabaseState(int hDatabase)
        {
            try
            {
                ManagedHandles.Get<ManagedDatabase>(hDatabase);
                return 0; // MSIDBSTATE_READ
            }
            catch (MsiErrorException)
            {
                return -1; // MSIDBSTATE_ERROR
            }
        }

        internal static uint MsiDatabaseOpenView(int hDatabase, string szQuery, out int hView)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                ManagedDatabase database = ManagedHandles.Get<ManagedDatabase>(hDatabase);
                if (szQuery == null)
                {
                    throw new MsiErrorException((uint) Error.INVALID_PARAMETER);
                }
                SqlStatement statement = new SqlParser(szQuery).ParseStatement();
                ManagedView view = statement is SqlSelect select
                    ? new ManagedView(database, BoundSelect.Bind(select, database))
                    : new ManagedView(database, BoundWrite.Bind(statement, database));
                handle = ManagedHandles.Add(view);
            });
            hView = handle;
            return result;
        }

        internal static uint MsiDatabaseMerge(int hDatabase, int hDatabaseMerge, string szTableName)
        {
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiDatabaseCommit(int hDatabase)
        {
            return Guard(() => ManagedHandles.Get<ManagedDatabase>(hDatabase).Commit());
        }

        internal static uint MsiDatabaseGetPrimaryKeys(int hDatabase, string szTableName, out int hRecord)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                ManagedDatabase database = ManagedHandles.Get<ManagedDatabase>(hDatabase);
                SqlTable table = database.FindTable(szTableName)
                    ?? throw new MsiErrorException((uint) Error.INVALID_TABLE);
                object?[] keys = Array.ConvertAll(
                    System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(table.Columns, c => c.IsPrimaryKey)),
                    c => (object?) c.Name);
                handle = ManagedHandles.Add(new ManagedRecord(keys));
            });
            hRecord = handle;
            return result;
        }

        internal static uint MsiDatabaseIsTablePersistent(int hDatabase, string szTableName)
        {
            try
            {
                ManagedDatabase database = ManagedHandles.Get<ManagedDatabase>(hDatabase);
                return database.ContainsTable(szTableName) ? 1u : 2u; // MSICONDITION_TRUE or MSICONDITION_NONE
            }
            catch (MsiErrorException)
            {
                return 3u; // MSICONDITION_ERROR
            }
        }

        internal static uint MsiDatabaseExport(int hDatabase, string szTableName, string szFolderPath, string szFileName)
        {
            return Guard(() => ManagedHandles.Get<ManagedDatabase>(hDatabase).Export(szTableName, System.IO.Path.Combine(szFolderPath, szFileName)));
        }

        internal static uint MsiDatabaseImport(int hDatabase, string szFolderPath, string szFileName)
        {
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiDatabaseGenerateTransform(int hDatabase, int hDatabaseReference, string szTransformFile, int iReserved1, int iReserved2)
        {
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiCreateTransformSummaryInfo(int hDatabase, int hDatabaseReference, string szTransformFile, int iErrorConditions, int iValidation)
        {
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiDatabaseApplyTransform(int hDatabase, string szTransformFile, int iErrorConditions)
        {
            return (uint) Error.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiViewExecute(int hView, int hRecord)
        {
            return Guard(() =>
            {
                ManagedView view = ManagedHandles.Get<ManagedView>(hView);
                object?[] markers = hRecord == 0
                    ? Array.Empty<object?>()
                    : ManagedHandles.Get<ManagedRecord>(hRecord).Values();
                view.Execute(markers);
            });
        }

        internal static uint MsiViewFetch(int hView, out int hRecord)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                ManagedRecord record = ManagedHandles.Get<ManagedView>(hView).Fetch()
                    ?? throw new MsiErrorException((uint) Error.NO_MORE_ITEMS);
                handle = ManagedHandles.Add(record);
            });
            hRecord = handle;
            return result;
        }

        internal static uint MsiViewModify(int hView, int iModifyMode, int hRecord)
        {
            return Guard(() =>
            {
                ManagedView view = ManagedHandles.Get<ManagedView>(hView);
                object?[] values = ManagedHandles.Get<ManagedRecord>(hRecord).Values();
                view.Modify(iModifyMode, values);
            });
        }

        internal static int MsiViewGetError(int hView, StringBuilder szColumnNameBuffer, ref uint cchBuf)
        {
            try
            {
                ManagedHandles.Get<ManagedView>(hView);
            }
            catch (MsiErrorException)
            {
                return 0;
            }
            return (int) WriteString(string.Empty, szColumnNameBuffer, ref cchBuf);
        }

        internal static uint MsiViewGetColumnInfo(int hView, uint eColumnInfo, out int hRecord)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                ManagedView view = ManagedHandles.Get<ManagedView>(hView);
                IReadOnlyList<SqlColumn> columns = view.Query?.OutputColumns
                    ?? throw new MsiErrorException((uint) Error.FUNCTION_FAILED);

                // MSICOLINFO_NAMES is 0 and MSICOLINFO_TYPES is 1.
                var values = new object?[columns.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    SqlColumn column = columns[i];
                    values[i] = eColumnInfo == 0 ? column.Name : column.TypeCode;
                }
                handle = ManagedHandles.Add(new ManagedRecord(values));
            });
            hRecord = handle;
            return result;
        }

        internal static int MsiCreateRecord(uint cParams)
        {
            try
            {
                return ManagedHandles.Add(new ManagedRecord((int) cParams));
            }
            catch (MsiErrorException)
            {
                return 0;
            }
        }

        internal static uint MsiFormatRecord(int hInstall, int hRecord, StringBuilder szResultBuf, ref uint cchResultBuf)
        {
            try
            {
                ManagedRecord record = ManagedHandles.Get<ManagedRecord>(hRecord);
                string template = record.GetString(0);
                string text = Regex.Replace(template, @"\[(\d+)\]", match =>
                {
                    int field = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return field >= 1 && field <= record.FieldCount ? record.GetString((uint) field) : match.Value;
                });
                return WriteString(text, szResultBuf, ref cchResultBuf);
            }
            catch (MsiErrorException e)
            {
                return e.Code;
            }
        }

        internal static uint MsiRecordClearData(int hRecord)
        {
            return Guard(() => ManagedHandles.Get<ManagedRecord>(hRecord).Clear());
        }

        internal static uint MsiRecordGetFieldCount(int hRecord)
        {
            try
            {
                return (uint) ManagedHandles.Get<ManagedRecord>(hRecord).FieldCount;
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
                return ManagedHandles.Get<ManagedRecord>(hRecord)[iField] == null;
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
                return ManagedHandles.Get<ManagedRecord>(hRecord).GetInteger(iField);
            }
            catch (MsiErrorException)
            {
                return int.MinValue; // MSI_NULL_INTEGER
            }
        }

        internal static uint MsiRecordGetString(int hRecord, uint iField, StringBuilder szValueBuf, ref uint cchValueBuf)
        {
            try
            {
                string value = ManagedHandles.Get<ManagedRecord>(hRecord).GetString(iField);
                return WriteString(value, szValueBuf, ref cchValueBuf);
            }
            catch (MsiErrorException e)
            {
                return e.Code;
            }
        }

        internal static uint MsiRecordSetInteger(int hRecord, uint iField, int iValue)
        {
            return Guard(() =>
            {
                // MSI_NULL_INTEGER clears the field.
                ManagedHandles.Get<ManagedRecord>(hRecord).Set(iField, iValue == int.MinValue ? null : (object) iValue);
            });
        }

        internal static uint MsiRecordSetString(int hRecord, uint iField, string szValue)
        {
            return Guard(() =>
            {
                ManagedHandles.Get<ManagedRecord>(hRecord).Set(iField, string.IsNullOrEmpty(szValue) ? null : szValue);
            });
        }

        internal static uint MsiRecordDataSize(int hRecord, uint iField)
        {
            try
            {
                object? value = ManagedHandles.Get<ManagedRecord>(hRecord)[iField];
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

        internal static uint MsiRecordReadStream(int hRecord, uint iField, byte[] szDataBuf, ref uint cbDataBuf)
        {
            try
            {
                ManagedRecord record = ManagedHandles.Get<ManagedRecord>(hRecord);
                object? value = record[iField];
                if (value == null)
                {
                    cbDataBuf = 0;
                    return 0;
                }
                if (value is not byte[] data)
                {
                    throw new MsiErrorException((uint) Error.DATATYPE_MISMATCH);
                }

                // Each read continues where the previous one stopped, as the Windows Installer API does.
                int offset = record.ReadOffset(iField);
                int count = (int) Math.Min(cbDataBuf, (uint) Math.Max(0, data.Length - offset));
                if (szDataBuf != null && count > 0)
                {
                    Array.Copy(data, offset, szDataBuf, 0, count);
                }
                record.SetReadOffset(iField, offset + count);
                cbDataBuf = (uint) count;
                return 0;
            }
            catch (MsiErrorException e)
            {
                return e.Code;
            }
        }

        internal static uint MsiRecordSetStream(int hRecord, uint iField, string szFilePath)
        {
            return Guard(() =>
            {
                ManagedRecord record = ManagedHandles.Get<ManagedRecord>(hRecord);
                byte[]? data = szFilePath == null ? null : System.IO.File.ReadAllBytes(szFilePath);
                record.Set(iField, data);
            });
        }

        internal static uint MsiGetSummaryInformation(int hDatabase, string szDatabasePath, uint uiUpdateCount, out int hSummaryInfo)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                // A non-zero update count asks for a handle that can be written.
                bool writable = uiUpdateCount > 0;
                ManagedSummary summary;
                if (hDatabase != 0)
                {
                    ManagedDatabase database = ManagedHandles.Get<ManagedDatabase>(hDatabase);
                    if (writable)
                    {
                        database.RequireWritable();
                    }
                    summary = ManagedSummary.Open(database, ownsDatabase: false, writable);
                }
                else
                {
                    if (szDatabasePath == null)
                    {
                        throw new MsiErrorException((uint) Error.INVALID_PARAMETER);
                    }
                    ManagedDatabase database = ManagedDatabase.Open(szDatabasePath,
                        writable ? DatabaseMode.Transact : DatabaseMode.ReadOnly);
                    summary = ManagedSummary.Open(database, ownsDatabase: true, writable);
                }
                handle = ManagedHandles.Add(summary);
            });
            hSummaryInfo = handle;
            return result;
        }

        internal static uint MsiSummaryInfoGetProperty(int hSummaryInfo, uint uiProperty, out uint uiDataType, out int iValue, ref long ftValue, StringBuilder szValueBuf, ref uint cchValueBuf)
        {
            uint type = 0;
            int number = 0;
            long fileTime = 0;
            string? text = null;
            uint result = Guard(() => ManagedHandles.Get<ManagedSummary>(hSummaryInfo)
                .Get(uiProperty, out type, out number, out fileTime, out text));

            uiDataType = type;
            iValue = number;
            ftValue = fileTime;
            if (result == 0 && type == SummaryStream.VtLpstr)
            {
                result = WriteString(text ?? string.Empty, szValueBuf, ref cchValueBuf);
            }
            return result;
        }

        internal static uint MsiSummaryInfoSetProperty(int hSummaryInfo, uint uiProperty, uint uiDataType, int iValue, ref long ftValue, string szValue)
        {
            long fileTime = ftValue;
            return Guard(() => ManagedHandles.Get<ManagedSummary>(hSummaryInfo)
                .Set(uiProperty, uiDataType, iValue, fileTime, szValue));
        }

        internal static uint MsiSummaryInfoPersist(int hSummaryInfo)
        {
            return Guard(() => ManagedHandles.Get<ManagedSummary>(hSummaryInfo).Persist());
        }

        internal static uint MsiCloseHandle(int hAny)
        {
            return Guard(() => ManagedHandles.Close(hAny));
        }

        internal static int MsiGetLastErrorRecord()
        {
            // Errors are reported through return codes, so there is never a separate error record.
            return 0;
        }

        internal static uint FormatMessage(uint dwFlags, IntPtr lpSource, uint dwMessageId, uint dwLanguageId, StringBuilder lpBuffer, uint nSize, IntPtr Arguments)
        {
            // No system message table is available outside Windows; zero tells callers there is no text.
            return 0;
        }

        internal static int StgOpenStorage(string wcsName, IntPtr stgPriority, uint grfMode, IntPtr snbExclude, uint reserved, out IStorage stgOpen)
        {
            stgOpen = null!;
            return E_NOTIMPL;
        }

        internal static int StgCreateDocfile(string wcsName, uint grfMode, uint reserved, out IStorage stgOpen)
        {
            stgOpen = null!;
            return E_NOTIMPL;
        }

        private const int E_NOTIMPL = unchecked((int) 0x80004001);

        /// <summary>Runs an operation and converts a Windows Installer error into its return code.</summary>
        private static uint Guard(Action operation)
        {
            try
            {
                operation();
                return (uint) Error.SUCCESS;
            }
            catch (MsiErrorException e)
            {
                return e.Code;
            }
        }

        /// <summary>
        /// Copies text into a caller-supplied buffer using the Windows Installer size protocol: when the
        /// buffer is too small, the required length is returned in cch and ERROR_MORE_DATA is returned.
        /// </summary>
        private static uint WriteString(string value, StringBuilder buffer, ref uint cch)
        {
            if (value.Length + 1 > buffer.Capacity)
            {
                cch = (uint) value.Length;
                return (uint) Error.MORE_DATA;
            }
            buffer.Clear();
            buffer.Append(value);
            cch = (uint) value.Length;
            return (uint) Error.SUCCESS;
        }
    }
}
