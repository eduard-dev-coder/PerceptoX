# Installation

Use Windows x64 (Windows 10 build 19041 or newer, or Windows 11). Download only from [GitHub Releases](https://github.com/eduard-dev-coder/PerceptoX/releases). Clean-machine testing remains open. Current validation installers are unsigned; do not treat a warning as proof of authenticity or bypass your organization's security policy.

## Installer

1. Download `PerceptoX-<version>-Setup-win-x64.exe` and verify its SHA-256 against the release checksums.
2. Review the PerceptoX license and third-party terms, then consent to installation if you agree.
3. Choose the location and Desktop/Start shortcut options. Installation is per-user; no global ImageMagick or PATH change is required.
4. The offline codec payload is activated only after consent. Launch PerceptoX from a shortcut.

## ZIP package

1. Extract **all** files from `PerceptoX-win-x64.zip` into a local writable folder.
2. Run `PerceptoX.exe`. Its `app/` directory must remain alongside it.
3. For HEIC/HEIF/AVIF, open **Modules**, review terms, choose **Install / Update** and restart when requested.

The package is self-contained, not a single executable or a “leave no traces” mode. .NET, WinUI and SQLite runtimes are included. Logs/preferences use `%LocalAppData%\PerceptoX`; index/cache locations are configurable. Visual Studio, Python and Ollama are not required by end users.

## Verification, upgrades and uninstall

```powershell
Get-FileHash .\PerceptoX-1.0.0-Setup-win-x64.exe -Algorithm SHA256
```

Compare the full value with `SHA256SUMS.txt`. A checksum detects a changed file; it is not a digital signature.

Close the app before replacing package files. Keep index/reports/photos outside the installation and do not mix DLLs/codecs from different releases. Uninstall removes the installed application, not original image libraries. Cache/index cleanup is an explicit maintenance action.

Online module updates require an approved compatible source/manifest and consent; no automatic download-on-start is assumed. For format failures, report a non-sensitive reproducer, not private photographs.
