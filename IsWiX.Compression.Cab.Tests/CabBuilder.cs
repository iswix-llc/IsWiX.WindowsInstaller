using System.Text;

namespace IsWiX.Compression.Cab.Tests;

/// <summary>
/// Builds a minimal stored (uncompressed) cabinet with one folder, so tests can cover specific headers.
/// </summary>
internal static class CabBuilder
{
    public static byte[] Build(params (string Name, byte[] Data)[] files)
    {
        var data = new MemoryStream();
        foreach (var file in files)
        {
            data.Write(file.Data, 0, file.Data.Length);
        }

        var all = data.ToArray();
        const int HeaderSize = 36;
        const int FolderSize = 8;
        var filesOffset = HeaderSize + FolderSize;

        var fileTable = new MemoryStream();
        var fileWriter = new BinaryWriter(fileTable);
        uint offset = 0;
        foreach (var file in files)
        {
            fileWriter.Write((uint)file.Data.Length);
            fileWriter.Write(offset);
            fileWriter.Write((ushort)0);       // folder
            fileWriter.Write((ushort)0x5a21);  // date: 2025-01-01
            fileWriter.Write((ushort)0x6000);  // time: 12:00:00
            fileWriter.Write((ushort)0x20);    // attributes: archive
            fileWriter.Write(Encoding.ASCII.GetBytes(file.Name));
            fileWriter.Write((byte)0);
            offset += (uint)file.Data.Length;
        }

        var dataOffset = filesOffset + (int)fileTable.Length;

        // One CFDATA block per 32 KB at most; tests stay far below that.
        var total = dataOffset + 8 + all.Length;

        var output = new MemoryStream();
        var writer = new BinaryWriter(output);
        writer.Write(Encoding.ASCII.GetBytes("MSCF"));
        writer.Write(0u);
        writer.Write((uint)total);
        writer.Write(0u);
        writer.Write((uint)filesOffset);
        writer.Write(0u);
        writer.Write((byte)3);
        writer.Write((byte)1);
        writer.Write((ushort)1);               // folders
        writer.Write((ushort)files.Length);
        writer.Write((ushort)0);               // flags
        writer.Write((ushort)1234);            // set id
        writer.Write((ushort)0);               // cabinet number

        writer.Write((uint)dataOffset);
        writer.Write((ushort)1);               // blocks
        writer.Write((ushort)0);               // stored

        writer.Write(fileTable.ToArray());

        writer.Write(0u);                      // checksum (not checked)
        writer.Write((ushort)all.Length);
        writer.Write((ushort)all.Length);
        writer.Write(all);
        return output.ToArray();
    }
}
