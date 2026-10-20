// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace IsWiX.WindowsInstaller
{
    using System;

    /// <summary>
    /// Holds the engine handle id behind a Database, View, Record or SummaryInfo object. Closing it removes
    /// the engine's entry when the owning object is the one that opened it.
    /// </summary>
    internal sealed class EngineHandle
    {
        private readonly int id;
        private readonly bool ownsHandle;
        private readonly object sync = new object();
        private bool closed;

        internal EngineHandle(int id, bool ownsHandle)
        {
            if (id == 0)
            {
                throw new InvalidHandleException();
            }

            this.id = id;
            this.ownsHandle = ownsHandle;
        }

        /// <summary>Gets the engine handle id. Throws if the handle has been closed.</summary>
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

        internal bool IsClosed
        {
            get
            {
                return this.closed;
            }
        }

        /// <summary>Closes the handle, removing the engine entry if this object owns it. Closing twice has no effect.</summary>
        internal void Close()
        {
            lock (this.sync)
            {
                if (this.closed)
                {
                    return;
                }
                this.closed = true;
            }

            if (this.ownsHandle)
            {
                MsiApi.MsiCloseHandle(this.id);
            }
        }
    }
}
