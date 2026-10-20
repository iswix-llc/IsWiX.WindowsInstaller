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
    internal static class MsiApi
    {

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


        internal static uint MsiOpenDatabase(string szDatabasePath, IntPtr uiOpenMode, out int hDatabase)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                // MSIDBOPEN_READONLY (0), MSIDBOPEN_TRANSACT (1) and MSIDBOPEN_DIRECT (2). Create modes are not supported yet.
                long mode = uiOpenMode.ToInt64();
                if (mode < 0 || mode > 2)
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                handle = HandleTable.Add(EngineDatabase.Open(szDatabasePath, (DatabaseMode) mode));
            });
            hDatabase = handle;
            return result;
        }

        internal static uint MsiOpenDatabase(string szDatabasePath, string szPersist, out int hDatabase)
        {
            hDatabase = 0;
            return (uint) MsiError.CALL_NOT_IMPLEMENTED;
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

        internal static uint MsiDatabaseOpenView(int hDatabase, string szQuery, out int hView)
        {
            int handle = 0;
            uint result = Guard(() =>
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
                handle = HandleTable.Add(view);
            });
            hView = handle;
            return result;
        }

        internal static uint MsiDatabaseMerge(int hDatabase, int hDatabaseMerge, string szTableName)
        {
            return (uint) MsiError.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiDatabaseCommit(int hDatabase)
        {
            return Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Commit());
        }

        internal static uint MsiDatabaseGetPrimaryKeys(int hDatabase, string szTableName, out int hRecord)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                SqlTable table = database.FindTable(szTableName)
                    ?? throw new MsiErrorException((uint) MsiError.INVALID_TABLE);
                object?[] keys = Array.ConvertAll(
                    System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(table.Columns, c => c.IsPrimaryKey)),
                    c => (object?) c.Name);
                handle = HandleTable.Add(new EngineRecord(keys));
            });
            hRecord = handle;
            return result;
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

        internal static uint MsiDatabaseExport(int hDatabase, string szTableName, string szFolderPath, string szFileName)
        {
            return Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Export(szTableName, System.IO.Path.Combine(szFolderPath, szFileName)));
        }

        internal static uint MsiDatabaseImport(int hDatabase, string szFolderPath, string szFileName)
        {
            return Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).Import(System.IO.Path.Combine(szFolderPath, szFileName)));
        }

        internal static uint MsiDatabaseGenerateTransform(int hDatabase, int hDatabaseReference, string szTransformFile, int iReserved1, int iReserved2)
        {
            return (uint) MsiError.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiCreateTransformSummaryInfo(int hDatabase, int hDatabaseReference, string szTransformFile, int iErrorConditions, int iValidation)
        {
            return (uint) MsiError.CALL_NOT_IMPLEMENTED;
        }

        internal static uint MsiDatabaseApplyTransform(int hDatabase, string szTransformFile, int iErrorConditions)
        {
            return Guard(() => HandleTable.Get<EngineDatabase>(hDatabase).ApplyTransform(szTransformFile));
        }

        internal static uint MsiViewExecute(int hView, int hRecord)
        {
            return Guard(() =>
            {
                EngineView view = HandleTable.Get<EngineView>(hView);
                object?[] markers = hRecord == 0
                    ? Array.Empty<object?>()
                    : HandleTable.Get<EngineRecord>(hRecord).Values();
                view.Execute(markers);
            });
        }

        internal static uint MsiViewFetch(int hView, out int hRecord)
        {
            int handle = 0;
            uint result = Guard(() =>
            {
                EngineRecord record = HandleTable.Get<EngineView>(hView).Fetch()
                    ?? throw new MsiErrorException((uint) MsiError.NO_MORE_ITEMS);
                handle = HandleTable.Add(record);
            });
            hRecord = handle;
            return result;
        }

        internal static uint MsiViewModify(int hView, int iModifyMode, int hRecord)
        {
            return Guard(() =>
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
            });
        }

        internal static int MsiViewGetError(int hView, StringBuilder szColumnNameBuffer, ref uint cchBuf)
        {
            try
            {
                HandleTable.Get<EngineView>(hView);
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
                handle = HandleTable.Add(new EngineRecord(values));
            });
            hRecord = handle;
            return result;
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

        internal static uint MsiFormatRecord(int hInstall, int hRecord, StringBuilder szResultBuf, ref uint cchResultBuf)
        {
            try
            {
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
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
            return Guard(() => HandleTable.Get<EngineRecord>(hRecord).Clear());
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

        internal static uint MsiRecordGetString(int hRecord, uint iField, StringBuilder szValueBuf, ref uint cchValueBuf)
        {
            try
            {
                string value = HandleTable.Get<EngineRecord>(hRecord).GetString(iField);
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
                HandleTable.Get<EngineRecord>(hRecord).Set(iField, iValue == int.MinValue ? null : (object) iValue);
            });
        }

        internal static uint MsiRecordSetString(int hRecord, uint iField, string szValue)
        {
            return Guard(() =>
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

        internal static uint MsiRecordReadStream(int hRecord, uint iField, byte[] szDataBuf, ref uint cbDataBuf)
        {
            try
            {
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
                object? value = record[iField];
                if (value == null)
                {
                    cbDataBuf = 0;
                    return 0;
                }
                if (value is not byte[] data)
                {
                    throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
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
                EngineRecord record = HandleTable.Get<EngineRecord>(hRecord);
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
                EngineSummary summary;
                if (hDatabase != 0)
                {
                    EngineDatabase database = HandleTable.Get<EngineDatabase>(hDatabase);
                    if (writable)
                    {
                        database.RequireWritable();
                    }
                    summary = EngineSummary.Open(database, ownsDatabase: false, writable);
                }
                else
                {
                    if (szDatabasePath == null)
                    {
                        throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
                    }
                    if (!writable)
                    {
                        // Read-only summaries come straight from the file, so transforms can be read too.
                        summary = EngineSummary.OpenFile(szDatabasePath);
                    }
                    else
                    {
                        EngineDatabase database = EngineDatabase.Open(szDatabasePath, DatabaseMode.Transact);
                        summary = EngineSummary.Open(database, ownsDatabase: true, writable);
                    }
                }
                handle = HandleTable.Add(summary);
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
            uint result = Guard(() => HandleTable.Get<EngineSummary>(hSummaryInfo)
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
            return Guard(() => HandleTable.Get<EngineSummary>(hSummaryInfo)
                .Set(uiProperty, uiDataType, iValue, fileTime, szValue));
        }

        internal static uint MsiSummaryInfoPersist(int hSummaryInfo)
        {
            return Guard(() => HandleTable.Get<EngineSummary>(hSummaryInfo).Persist());
        }

        internal static uint MsiCloseHandle(int hAny)
        {
            return Guard(() => HandleTable.Close(hAny));
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
                return (uint) MsiError.SUCCESS;
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
                return (uint) MsiError.MORE_DATA;
            }
            buffer.Clear();
            buffer.Append(value);
            cch = (uint) value.Length;
            return (uint) MsiError.SUCCESS;
        }
    }
}
