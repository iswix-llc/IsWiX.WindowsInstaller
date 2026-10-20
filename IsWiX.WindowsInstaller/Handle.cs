// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Base class for Windows Installer handle types (Database, View, Record, SummaryInfo).
    /// </summary>
    /// <remarks><p>
    /// Each instance refers to an entry in the engine's handle table by its integer id.
    /// Disposing an instance that owns its handle removes that entry and releases the object behind it.
    /// </p></remarks>
    public abstract class InstallerHandle : IDisposable
    {
        private readonly int id;
        private readonly bool ownsHandle;
        private readonly object sync = new object();
        private bool closed;

        /// <summary>
        /// Constructs a handle object from an engine handle id.
        /// </summary>
        /// <param name="id">Engine handle id.</param>
        /// <param name="ownsHandle">true to close the handle when this object is disposed</param>
        protected InstallerHandle(int id, bool ownsHandle)
        {
            if (id == 0)
            {
                throw new InvalidHandleException();
            }

            this.id = id;
            this.ownsHandle = ownsHandle;
        }

        /// <summary>
        /// Gets the native integer handle.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public IntPtr Handle
        {
            get
            {
                return this.Id;
            }
        }

        /// <summary>
        /// Gets the engine handle id. Throws if this handle has been closed.
        /// </summary>
        internal int Id
        {
            get
            {
                if (this.closed)
                {
                    throw new InvalidHandleException();
                }
                return this.id;
            }
        }

        /// <summary>
        /// Checks if the handle is closed. When closed, method calls on the handle object may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                return this.closed;
            }
        }

        /// <summary>
        /// Closes the handle.  After closing a handle, further method calls may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        /// <seealso cref="Close"/>
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

        /// <summary>
        /// Tests whether this handle object is equal to another handle object: same type and same engine id.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj != null && this.GetType() == obj.GetType() &&
                this.Id == ((InstallerHandle) obj).Id;
        }

        /// <summary>
        /// Gets a hash value for the handle object, derived from its engine id.
        /// </summary>
        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }

        /// <summary>
        /// Gets an object that can be used internally for safe synchronization.
        /// </summary>
        internal object Sync
        {
            get
            {
                return this.sync;
            }
        }

        /// <summary>
        /// Closes the handle. Subclasses override this to release their own resources first.
        /// </summary>
        /// <param name="disposing">If true, the handle is removed from the engine's handle table
        /// (when this object owns it). The finalizer is not used, because handles must be closed
        /// deliberately from the application's thread.</param>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        protected virtual void Dispose(bool disposing)
        {
            lock (this.sync)
            {
                if (this.closed)
                {
                    return;
                }
                this.closed = true;
            }

            if (disposing && this.ownsHandle)
            {
                NativeMethods.MsiCloseHandle(this.id);
            }
        }
    }
}
