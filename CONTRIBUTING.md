# Contributing

Open an issue before a large change. Focused fixes, translation improvements and reproducible bug reports are welcome.

1. Fork and create a focused branch; follow [Development](docs/DEVELOPMENT.md).
2. Keep the engine independent of WinUI, with clear contracts and bounded resource use.
3. Add/run relevant regression tests. Build WinUI and check Light/Dark and EN/RO for UI changes. Do not claim unrun validation.
4. Exclude private photos/databases/logs, credentials, downloaded runtimes and build artifacts. Redact personal paths.
5. Explain behavior, validation and limitations in a pull request. AI-assisted code still needs understandable reasoning and verification.

Translations live in `languages/`; preserve keys/placeholders and run `eng/validate-languages.ps1`. Contributions must be compatible with GPL-3.0-only. Third-party assets/code need attribution and compatible terms.
