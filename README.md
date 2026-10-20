# IsWiX.WindowsInstaller

A managed library for reading and writing Windows Installer (`.msi`) databases. It is a replacement for `wixtoolset.windowsinstaller` that does not depend on `msi.dll`. Reading and writing databases runs on any platform .NET 8 supports. Extracting binary substorages needs Windows (see [Known issues](#known-issues)).

The package is published as `IsWiX.WindowsInstaller` and is licensed under the MIT License.

## Solution layout

| Project | Purpose |
| --- | --- |
| `IsWiX.WindowsInstaller` | The library. This is what ships in the NuGet package. |
| `IsWiX.WindowsInstaller.Tests` | xUnit tests for the library, with fixture databases. |
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
| `Create` | Creates a new, empty database. Fails if the file already exists. |
| `CreateDirect` | Creates a new database and writes each change immediately. Fails if the file already exists. |

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

## Not implemented yet

These throw `CALL_NOT_IMPLEMENTED`:

- Merging databases (`Database.Merge`).
- Generating transforms and their summary information.
- Some SQL forms. The parser accepts the statement types listed above, but not every form of each.
- The constructor that takes an output path, and temporary databases opened with an empty path.

## Known issues

- `IsReadOnly` always returns `true`, because the database state is not read yet.
- `ExtractSubStorage` uses Windows COM storage APIs. Calling it on other platforms throws `PlatformNotSupportedException`.

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
dotnet nuget push IsWiX.WindowsInstaller\bin\Release\IsWiX.WindowsInstaller.0.1.0.nupkg --api-key <your key> --source https://api.nuget.org/v3/index.json
```
