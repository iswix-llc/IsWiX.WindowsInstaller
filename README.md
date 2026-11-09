# IsWiX.WindowsInstaller

A managed library for reading and writing Windows Installer (`.msi`, `.msm` and `.msp`) databases. It is a replacement for `wixtoolset.windowsinstaller` that does not depend on `msi.dll`, and it runs on any platform .NET supports. The package targets `net8.0` and `netstandard2.0`; the `netstandard2.0` build lets the WiX toolset (`WixToolset.Core.Native`) reference it directly.

The package is published as `IsWiX.WindowsInstaller` and is licensed under the MIT License (see `LICENSE.txt`, which is included in the package).

## Solution layout

| Project | Purpose |
| --- | --- |
| `IsWiX.WindowsInstaller` | The library. This is what ships in the NuGet package. |
| `IsWiX.WindowsInstaller.Tests` | xUnit tests for the library, with fixture databases. |
| `IsWiX.Compression` | Abstract archive types (`ArchiveInfo`, `ArchiveFileInfo`, `CompressionEngine`, stream contexts), mirroring `WixToolset.Dtf.Compression`. Packaged as `IsWiX.Compression`. |
| `IsWiX.Compression.Cab` | Reads cabinet files without `cabinet.dll` (`CabInfo`, `CabFileInfo`, `CabEngine`), mirroring `WixToolset.Dtf.Compression.Cab`. Stored, MSZIP and LZX; no packing and no multi-file cabinets yet. Packaged as `IsWiX.Compression.Cab`. |
| `IsWiX.Compression.Cab.Tests` | xUnit tests for the cabinet reader, with fixture cabinets. |
| `TestApp` | A scratch console app. It is not part of the package. |

## How the library is organised

- **Public API** (`Database`, `View`, `Record`, `SummaryInfo`, `TableInfo`, `ColumnInfo`, and their collections). These mirror the `wixtoolset.windowsinstaller` surface. Consumers only need these types.
- **`MsiApi`**: Win32-shaped functions that the public classes call. Each function takes objects rather than handles, and returns its result directly. Windows Installer errors are thrown as `InstallerException`.
- **`Format/`**: the binary MSI format. It reads and writes OLE compound files, the string pool, MSI tables, and the summary information stream. These types are internal. The test project can see them through `InternalsVisibleTo`.
- **SQL engine**: `SqlLexer`, `SqlParser`, `SqlStatements`, `SqlConditions`, and the bound query classes (`BoundSelect`, `BoundWrite`, `BoundDdl`, `ViewModify`). They evaluate the Windows Installer SQL dialect over in-memory tables.
- **`Enums/`**: the public enumerations, in the `IsWiX.WindowsInstaller` namespace.

## Opening a database

`DatabaseOpenMode` selects how a database is opened:

| Mode | Behaviour |
| --- | --- |
| `ReadOnly` | Reads only. |
| `Transact` | Keeps changes in memory until `Commit()`. |
| `Direct` | Writes each change to the file immediately. |
| `Create` | Creates a new, empty database. Like Windows Installer, it replaces a file that is already a database, and fails with `FILE_EXISTS` if the file is anything else. |
| `CreateDirect` | Creates a new database and writes each change immediately, with the same rules for an existing file. |
| `PatchFile` | A flag combined with another mode to open or create a patch package (`.msp`). A file with an `.msp` extension is treated as a patch. |

## Example

```csharp
using IsWiX.WindowsInstaller;

using (Database database = new Database(@"C:\path\app.msi", DatabaseOpenMode.ReadOnly))
using (View view = database.OpenView("SELECT * FROM Property"))
{
    view.Execute();

    Record record;
    while ((record = view.Fetch()) != null)
    {
        Console.WriteLine($"{record.GetString("Property")} : {record.GetString("Value")}");
    }
}
```

## What it can do

- **Queries.** `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`, `DROP TABLE` and `ALTER TABLE ... ADD`, plus view modification (`View.Modify`) and `Commit`.
- **IDT import.** `Database.Import` and `ImportAll` read installer text archives. Integer columns of width 0 or 1 are stored as short integers, and strings longer than their column width are accepted, as Windows Installer does.
- **Transforms.** `GenerateTransform`, `CreateTransformSummaryInfo`, `ApplyTransform` (including the error-suppression flags) and `ViewTransform`.
- **Patches.** Patch databases open and are written with `DatabaseOpenMode.PatchFile`.
- **Streams and substorages.** Binary streams, `_Streams`, `_Storages`, and `ExtractSubStorage` all work without COM, so they run on every platform.

## Not implemented yet

These throw `CALL_NOT_IMPLEMENTED`:

- Merging databases (`Database.Merge`).
- The constructor that takes an output path (`new Database(filePath, outputPath)`), and temporary databases opened with an empty path (that throws `ArgumentNullException`).
- Temporary columns and tables (`TEMPORARY`, `ALTER TABLE ... HOLD` and `FREE`).

## Known issues

- String pool ids are assigned in a different order than `msi.dll` uses. Databases are valid, but a byte-for-byte comparison with an `msi.dll` database will differ.

## Building and testing

Build and test with the .NET 8 SDK:

```
dotnet build IsWiX.WindowsInstaller.slnx
dotnet test IsWiX.WindowsInstaller.Tests\IsWiX.WindowsInstaller.Tests.csproj
```

The test fixtures in `IsWiX.WindowsInstaller.Tests/fixtures` are committed, except for the large stress database. The stress tests skip when it is missing. To generate it, run `fixtures/generate.sh` on Linux, or in WSL, after installing the tools it needs:

```
sudo apt install msitools python3
cd IsWiX.WindowsInstaller.Tests/fixtures
bash generate.sh
```

The script writes `generated/stress.msi` and `generated/expected_big_sha256.txt`. The `generated/` folder is ignored by git.

## Packaging

```
dotnet pack IsWiX.WindowsInstaller\IsWiX.WindowsInstaller.csproj -c Release
dotnet nuget push "IsWiX.WindowsInstaller\bin\Release\IsWiX.WindowsInstaller.<version>.nupkg" --api-key <your key> --source https://api.nuget.org/v3/index.json
```
