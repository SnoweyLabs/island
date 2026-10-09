# Size of the shipped folder

Measured 2026-10-07 on the build of commit after `802215e`, by the commands below. One run, one computer.

| Build | Files | Size |
|---|---|---|
| `ship/app/` — self-contained: Microsoft's .NET runtime inside, a plain folder, not one file, not trimmed, not compressed, no debug files | 488 | 206,350,274 bytes (196.8 MB) |
| The same programs without the runtime inside (needs .NET 10 desktop runtime on the computer) | 20 | 27,057,833 bytes (25.8 MB) |

The difference, about 171 MB, is the runtime. It is the price of "a person needs nothing else": a missing runtime is not installed for the person (`Research/shipping.md`, section 7, [S43]). The package file (`.msix`) is compressed by its format, so what is downloaded is smaller than the folder; that number was not measured, because no package file was made here (see `ship/store/CE-FACI-TU-store.md`).

## The commands

Self-contained folder (the one that is shipped):

    dotnet publish src/Island.App -c Release -r win-x64 --self-contained true -o ship/app -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -p:Deterministic=true -p:ContinuousIntegrationBuild=true "-p:PathMap=<the project's folder>=/_"

Without the runtime, for the comparison only (not kept):

    dotnet publish src/Island.App -c Release -r win-x64 --self-contained false -o <a temporary folder> -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false

Sizes: the sum of the lengths of every file under the folder, counted by PowerShell (`Get-ChildItem -Recurse -File | Measure-Object Length -Sum`).

The switches are passed on the command line only: the project files and every ordinary build are as they were (the one exception is `Directory.Build.props`, which holds the version and the company, product and copyright names).
