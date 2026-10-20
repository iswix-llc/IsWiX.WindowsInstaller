// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace IsWiX.WindowsInstaller
{
    using System;
    using System.IO;

    internal class RecordStream : Stream
    {
        private Record record;
        private int field;
        private long position;

        internal RecordStream(Record record, int field)
            : base()
        {
            this.record = record;
            this.field = field;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override bool CanSeek { get { return false; } }

        public override long Length
        {
            get
            {
                return this.record.GetDataSize(this.field);
            }
        }

        public override long Position
        {
            get
            {
                return this.position;
            }

            set
            {
                throw new NotSupportedException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count > 0)
            {
                byte[] readBuffer = (offset == 0 ? buffer : new byte[count]);
                count = MsiApi.MsiRecordReadStream(this.record.Id, (uint) this.field, readBuffer, count);
                if (offset > 0)
                {
                    Array.Copy(readBuffer, 0, buffer, offset, count);
                }
                this.position += count;
            }
            return count;
        }

        public override void Write(byte[] array, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override string ToString()
        {
            return "[Binary data]";
        }
    }
}
