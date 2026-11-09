# IsWiX.Compression.Cab

Reads cabinet (`.cab`) files on any platform, without `cabinet.dll`. It is the managed counterpart of `WixToolset.Dtf.Compression.Cab` and keeps its names: `CabInfo`, `CabFileInfo`, `CabEngine`, `CabException`.

```csharp
var cab = new CabInfo("example.cab");
foreach (CabFileInfo file in cab.GetFiles())
{
    Console.WriteLine($"{file.Name} {file.Length}");
    using var text = file.OpenText();
}
cab.Unpack(@"out");
```

Supported: stored, MSZIP and LZX folders in a single cabinet file. Not yet supported: cabinets that span several files, and packing (`CabEngine.Pack` throws `NotSupportedException`).
