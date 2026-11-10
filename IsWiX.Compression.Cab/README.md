# IsWiX.Compression.Cab

Reads and writes cabinet (`.cab`) files on any platform, without `cabinet.dll`. It is the managed counterpart of `WixToolset.Dtf.Compression.Cab` and keeps its names: `CabInfo`, `CabFileInfo`, `CabEngine`, `CabException`.

```csharp
var cab = new CabInfo("example.cab");
foreach (CabFileInfo file in cab.GetFiles())
{
    Console.WriteLine($"{file.Name} {file.Length}");
    using var text = file.OpenText();
}
cab.Unpack(@"out");
```

Supported: stored, MSZIP and LZX folders in a single cabinet file. Packing: `CabInfo.Pack(directory, includeSubdirectories, level)` or `CabEngine.Pack(context, files, maxArchiveSize)`. Levels: `None` stores, `Min` uses MSZIP, `Normal` uses LZX with an 18 bit window and `Max` uses LZX with a 21 bit window. A positive `maxArchiveSize` splits the output into continuation cabinets (`name.cab`, `namea.cab`, `nameb.cab`, ...).

```csharp
new CabInfo(@"out.cab").Pack(@"payload", true, CompressionLevel.Max);
```

Not yet supported: reading cabinets that span several files.
