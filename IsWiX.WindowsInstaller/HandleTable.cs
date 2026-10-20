namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using MsiReader;

    internal static class HandleTable
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<int, object> Items = new Dictionary<int, object>();
        private static int nextHandle = 1;

        public static int Add(object item)
        {
            lock (SyncRoot)
            {
                int handle = nextHandle++;
                Items[handle] = item;
                return handle;
            }
        }

        public static T Get<T>(int handle) where T : class
        {
            lock (SyncRoot)
            {
                if (Items.TryGetValue(handle, out object? item) && item is T typed)
                {
                    return typed;
                }
            }
            throw new MsiErrorException((uint) MsiError.INVALID_HANDLE);
        }

        public static void Close(int handle)
        {
            object? item;
            lock (SyncRoot)
            {
                if (!Items.TryGetValue(handle, out item))
                {
                    throw new MsiErrorException((uint) MsiError.INVALID_HANDLE);
                }
                Items.Remove(handle);
            }
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
