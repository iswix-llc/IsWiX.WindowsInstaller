# IsWiX.Compression

Abstract base types for archive packing and unpacking, with no native code. It is the managed counterpart of `WixToolset.Dtf.Compression` and keeps its layering and names: `ArchiveInfo`, `ArchiveFileInfo`, `CompressionEngine`, `IUnpackStreamContext`, `IPackStreamContext`, `ArchiveException`, `CompressionLevel`, and the progress types.

Concrete formats live in their own packages, as upstream. `IsWiX.Compression.Cab` provides cabinets.

Differences from upstream: packing is not implemented yet (`ArchiveInfo.Pack*` throw `NotSupportedException`), and the serialization constructors are omitted.
