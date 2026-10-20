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
}
