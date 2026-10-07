# Release readiness — 2026-10-07

## Prepared locally

Version 1.0.0 x64 folder, ZIP, root launcher and Inno setup exist, with notices, own sources and the exact-version ImageMagick/delegate source archive. Earlier recorded local checks: 295 tests passed; isolated installer consent/refusal/codec activation/uninstall passed; ZIP startup loaded app-local runtimes with restricted PATH.

The maintainer reported on 2026-10-08 that the application was tested successfully on clean Windows. This is user-reported acceptance, not an additional agent-run test. Documentation and Debug-only capture tooling are edited after that binary build. Public metadata must distinguish the source commit from the older installer build. Signing preparation and remaining provider enrollment are documented in [CODE_SIGNING.md](CODE_SIGNING.md).

## Before public binary distribution

1. Complete the existing GPL/Microsoft-runtime compatibility review for the combined package. Windows App SDK package terms impose redistribution requirements/restrictions; our GPL does not replace them. No license exception has silently been added to the author's GPL choice.
2. Reconcile Win2D 1.4.0 NuGet's obsolete declared license URL against precise package provenance. Official upstream MIT terms/source revision are collected, but precise package-source identity remains unresolved.
3. Confirm native-delegate source/build/replacement obligations for the exact shipped bundle. The matching-version 796 MB archive is present; a clean rebuild/relink verification is not complete.
4. Clean Windows application operation: confirmed by the maintainer on 2026-10-08. Detailed coverage of the wizard, codec variants and uninstall was not separately enumerated in that report.
5. Publish final binaries, exact corresponding sources, notices and checksums together. State signed/unsigned accurately; do not replace artifacts under old checksums.

Source and documentation can be public while binary gates remain open. No installer/ZIP is advertised as cleared while its manifest says redistribution is pending. Free distribution is not a license waiver. This records engineering evidence and review work, not legal advice.

## Publication capabilities

The GitHub connector confirms owner/repository and supports source commits. The owner created the repository. Releases/Pages administration require capabilities not exposed by that connector. Handoff scripts contain no credentials.
