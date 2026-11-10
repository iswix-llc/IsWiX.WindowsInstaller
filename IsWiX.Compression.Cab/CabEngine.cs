namespace IsWiX.Compression.Cab
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using IsWiX.Compression;

    /// <summary>
    /// Engine that reads cabinet files: stored, MSZIP and LZX folders in a single cabinet file.
    /// </summary>
    public class CabEngine : CompressionEngine
    {
        private const int ArchiveNumber = 0;

        public CabEngine()
        {
        }

        /// <summary>
        /// Packs files into one cabinet, or into several when <paramref name="maxArchiveSize"/> is greater than zero and
        /// the cabinet would be larger. The files are named by their paths inside the cabinet; the context supplies their data.
        /// <see cref="CompressionEngine.CompressionLevel"/> selects the method: None stores, Min uses MSZIP, Normal uses LZX
        /// with an 18 bit window and Max uses LZX with a 21 bit window.
        /// </summary>
        public override void Pack(IPackStreamContext streamContext, IEnumerable<string> files, long maxArchiveSize)
        {
            if (streamContext == null)
            {
                throw new ArgumentNullException("streamContext");
            }

            if (files == null)
            {
                throw new ArgumentNullException("files");
            }

            CabWriter.Write(this, streamContext, files, maxArchiveSize);
        }

        /// <summary>Checks whether a stream, at its current position, begins with a cabinet header.</summary>
        public override bool IsArchive(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (stream.Length - stream.Position < 36)
            {
                return false;
            }

            var signature = new byte[4];
            var read = 0;
            while (read < signature.Length)
            {
                var count = stream.Read(signature, read, signature.Length - read);
                if (count <= 0)
                {
                    return false;
                }

                read += count;
            }

            return signature[0] == (byte)'M' && signature[1] == (byte)'S' && signature[2] == (byte)'C' && signature[3] == (byte)'F';
        }

        public override IList<ArchiveFileInfo> GetFileInfo(IUnpackStreamContext streamContext, Predicate<string> fileFilter)
        {
            if (streamContext == null)
            {
                throw new ArgumentNullException("streamContext");
            }

            var result = new List<ArchiveFileInfo>();
            var stream = OpenCabinet(streamContext);
            try
            {
                var cab = ReadHeader(stream);
                foreach (var entry in cab.Files)
                {
                    if (fileFilter != null && !fileFilter(entry.Name))
                    {
                        continue;
                    }

                    result.Add(new CabFileInfo(entry.Name, ArchiveNumber, entry.Folder, ToAttributes(entry.Attributes), ToDateTime(entry), entry.Size));
                }
            }
            finally
            {
                streamContext.CloseArchiveReadStream(ArchiveNumber, String.Empty, stream);
            }

            return result;
        }

        public override void Unpack(IUnpackStreamContext streamContext, Predicate<string> fileFilter)
        {
            if (streamContext == null)
            {
                throw new ArgumentNullException("streamContext");
            }

            var stream = OpenCabinet(streamContext);
            try
            {
                var cab = ReadHeader(stream);
                var folderIndex = -1;
                byte[] folderData = null;

                foreach (var entry in cab.Files)
                {
                    if (fileFilter != null && !fileFilter(entry.Name))
                    {
                        continue;
                    }

                    var lastWriteTime = ToDateTime(entry);
                    var output = streamContext.OpenFileWriteStream(entry.Name, entry.Size, lastWriteTime);
                    if (output == null)
                    {
                        continue;
                    }

                    if (folderIndex != entry.Folder)
                    {
                        folderData = ReadFolder(stream, cab, entry.Folder);
                        folderIndex = entry.Folder;
                    }

                    if ((long)entry.Offset + entry.Size > folderData.Length)
                    {
                        throw new CabException("The cabinet is corrupt: file data lies beyond the end of its folder.");
                    }

                    output.Write(folderData, checked((int)entry.Offset), checked((int)entry.Size));
                    streamContext.CloseFileWriteStream(entry.Name, output, ToAttributes(entry.Attributes), lastWriteTime);
                }
            }
            finally
            {
                streamContext.CloseArchiveReadStream(ArchiveNumber, String.Empty, stream);
            }
        }

        private static Stream OpenCabinet(IUnpackStreamContext streamContext)
        {
            var stream = streamContext.OpenArchiveReadStream(ArchiveNumber, String.Empty, null);
            if (stream == null)
            {
                throw new CabException("The cabinet could not be opened.");
            }

            return stream;
        }

        private static CabReader.CabinetData ReadHeader(Stream stream)
        {
            try
            {
                return CabReader.ReadHeader(stream);
            }
            catch (InvalidDataException e)
            {
                throw new CabException(e.Message, e);
            }
            catch (EndOfStreamException e)
            {
                throw new CabException("The cabinet is truncated.", e);
            }
            catch (NotSupportedException e)
            {
                throw new CabException(e.Message, e);
            }
        }

        private static byte[] ReadFolder(Stream stream, CabReader.CabinetData cab, int folderIndex)
        {
            try
            {
                return CabReader.ReadFolder(stream, cab, folderIndex);
            }
            catch (InvalidDataException e)
            {
                throw new CabException(e.Message, e);
            }
            catch (EndOfStreamException e)
            {
                throw new CabException("The cabinet is truncated.", e);
            }
            catch (NotSupportedException e)
            {
                throw new CabException(e.Message, e);
            }
        }

        private static DateTime ToDateTime(CabReader.FileEntry entry)
        {
            DateTime result;
            DosDateAndTimeToDateTime(unchecked((short)entry.Date), unchecked((short)entry.Time), out result);
            return result;
        }

        private static FileAttributes ToAttributes(ushort cabAttributes)
        {
            // The cabinet read-only, hidden, system and archive bits are the same values as FileAttributes.
            var attributes = (FileAttributes)(cabAttributes & 0x27);
            return attributes == 0 ? FileAttributes.Normal : attributes;
        }
    }
}
