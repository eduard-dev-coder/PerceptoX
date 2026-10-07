# PerceptoX code signing

## Current status — 2026-10-08

The maintainer reports successful testing on a clean Windows installation. The existing Windows x64 launcher, application executable and Inno Setup installer are unsigned. No signing certificate has been issued or enabled for this project.

## Free trusted signing candidate

[SignPath Foundation](https://signpath.org/) offers free code signing for accepted open-source projects. Acceptance is discretionary and is not guaranteed for a new project. [Apply here](https://signpath.org/apply), using the repository owner account. Enable multi-factor authentication on GitHub and SignPath.

Suggested application details:

- Project: PerceptoX
- Repository: https://github.com/eduard-dev-coder/PerceptoX
- Website: https://eduard-dev-coder.github.io/PerceptoX/
- Maintainer / author / reviewer / signing approver: Eduard Prepelita (`eduard-dev-coder`)
- License of own source: GPL-3.0
- Purpose: Windows desktop application to identify original photos from references and group identical or near-identical photos; image processing is local.
- Artifacts requested: own Windows x64 launcher, WinUI application executable, and Inno Setup installer.
- Dependencies: self-contained .NET / Windows App SDK, ImageSharp 3.1.12 used under its Apache-2.0 grant for open-source software, SQLite and bundled ImageMagick codecs. Disclose the complete dependency inventory for provider assessment; do not assume acceptance of the combined package.

The Foundation requires an already released application, verifiable reputation, source-verifiable builds and manual signing approval. A transparently unsigned first release may therefore precede enrollment. Do not advertise sponsorship or trusted signing before approval.

## Integration after acceptance

1. Obtain the organization/project identifiers, approved artifact configuration and signing policy from SignPath. Install their GitHub integration as documented in [GitHub trusted builds](https://docs.signpath.io/trusted-build-systems/github).
2. Store the signing API token only in a protected GitHub environment secret, not source, issue text or chat. Require release approval and restrict the signing workflow to the maintainer's repository and reviewed release refs, never untrusted pull requests.
3. Build from the exact release commit in GitHub Actions. Sign only PerceptoX-owned binaries: the launcher and application executable (and own DLLs if agreed with the provider). Preserve upstream signatures; do not sign all bundled DLLs using the project's identity.
4. Recompute the package manifest after signing. Create the portable ZIP from the signed package and build Inno Setup using that same package.
5. Sign the resulting installer last. If signing the uninstaller is required, configure Inno Setup's signing hook during compilation, not just a post-build installer signature.
6. Verify Authenticode, expected signer and timestamp on every intended signed artifact. Generate ZIP/installer SHA-256 checksums only after all signing completes. Publish exact corresponding sources and notices alongside immutable release assets.

No production signing workflow is enabled until the provider configuration exists. The old unsigned delivery must not be modified in place or published with stale checksums.

## Important limits

A self-signed certificate is not trusted on other PCs and is not a public-distribution solution. Do not install a private root certificate on users' systems. A trusted signature identifies a publisher and protects file integrity, but does not guarantee the immediate disappearance of every SmartScreen reputation warning.

[Microsoft's signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) also describe free signing for MSIX packages submitted to the Microsoft Store; that does not sign the existing portable ZIP or Inno Setup installer.

## Local verification

Run `eng/verify-code-signatures.ps1 -PackageDirectory <folder> -InstallerPath <setup.exe>` to inspect signatures. Add `-RequireValid` to fail when any checked artifact is not trusted. This checks signatures only; it does not issue certificates or claim SmartScreen reputation.
