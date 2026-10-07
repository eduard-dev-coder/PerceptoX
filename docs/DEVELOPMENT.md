# Development and packaging

## Prerequisites

- Windows x64, .NET SDK **10.0.301** (`global.json`).
- Compatible Visual Studio/Build Tools with WinUI, Windows SDK and x64 C++ tools for the native launcher.
- PowerShell 7 for development scripts. Distributed validation scripts support Windows PowerShell 5.1 where documented.
- NuGet access for first restore; versions are pinned in `Directory.Packages.props`, cache in `.packages/`.

These are developer prerequisites, not end-user requirements.

## Build and test

```powershell
dotnet restore PerceptoX.sln --configfile NuGet.Config
dotnet build src/PerceptoX.WinUI/PerceptoX.WinUI.csproj -p:Platform=x64 --no-restore
dotnet test tests/PerceptoX.Core.Tests/PerceptoX.Core.Tests.csproj
dotnet test tests/PerceptoX.Application.Tests/PerceptoX.Application.Tests.csproj
dotnet test tests/PerceptoX.Infrastructure.Tests/PerceptoX.Infrastructure.Tests.csproj
dotnet test tests/PerceptoX.Presentation.Tests/PerceptoX.Presentation.Tests.csproj
```

For the isolated workspace wrapper use an explicit array; otherwise PowerShell may interpret MSBuild's `-p:` as its own parameter:

```powershell
.\eng\dotnet-sandbox.ps1 -DotNetArguments @('build', 'src/PerceptoX.WinUI/PerceptoX.WinUI.csproj', '-p:Platform=x64', '--no-restore')
```

Run `src/PerceptoX.WinUI/bin/x64/Debug/net10.0-windows10.0.19041.0/PerceptoX.WinUI.exe`. Debug capture/external-codec flags are absent from Release.

## Architecture

| Directory | Responsibility |
| --- | --- |
| `src/PerceptoX.Core` | Fingerprints, metric search and domain models. |
| `src/PerceptoX.Application` | Indexing/matching/grouping use cases. |
| `src/PerceptoX.Infrastructure` | Decoding, SQLite, files, thumbnails, exports, modules. |
| `src/PerceptoX.Presentation` | Framework-independent view models/localization. |
| `src/PerceptoX.WinUI` | Views/dialogs, theme and Windows composition. |
| `src/PerceptoX.Cli` | Engine command-line companion. |
| `tests`, `benchmarks`, `tools` | Automated tests, measurements and datasets. |
| `eng` | Build, packaging, validation and publication. |
| `website` | Static GitHub Pages source and real application captures. |

## Packaging and publication

Follow [STANDALONE](STANDALONE.md): prepare a pinned x64 ImageMagick bundle, collect its corresponding sources, publish a new self-contained delivery, then compile Inno. The tools do not install ImageMagick globally or change PATH. Native launcher tools are discovered automatically or overridden with `-VisualStudioRoot`/`-WindowsSdkRoot`.

`eng/package-sources.ps1` exports source without runtime binaries/caches. `eng/prepare-github-publication.ps1` builds a reviewed public snapshot and checksum inventory. Never commit `artifacts/`, `vendor/` or downloaded tools.

`eng/capture-documentation.ps1` downloads an attributed NASA demo, then drives the actual production engine through the real English WinUI shell in Debug. Captures are XAML renders, not window-chrome desktop captures or synthetic-result mockups. [Capture provenance](../website/assets/SCREENSHOTS.md).

Source CI/Pages are separate from binary release. Follow [release readiness](RELEASE_READINESS.md), resolve redistribution checks and verify on clean Windows before publishing binaries. A pre-release label does not waive licenses.
