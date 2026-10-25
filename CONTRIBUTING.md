# Contributing to AC Server Manager

Bug reports, documentation fixes, translations, accessibility feedback and focused code contributions are welcome. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Choose the right channel

- Use the [bug report form](https://github.com/drkreis/ac-server-manager/issues/new?template=bug_report.yml) for reproducible application problems.
- Use the [feature request form](https://github.com/drkreis/ac-server-manager/issues/new?template=feature_request.yml) to describe a need or propose a change. Open an issue before starting a substantial redesign or new feature so its scope can be discussed.
- Use the [accessibility report form](https://github.com/drkreis/ac-server-manager/issues/new?template=accessibility.yml) for barriers involving keyboard use, readability or assistive technology.
- Report vulnerabilities privately using [SECURITY.md](SECURITY.md), rather than a public issue.

Search existing issues first and include the application version, relevant environment, reproduction steps and expected versus actual behavior. For connection problems, distinguish the manager's own behavior from AssettoServer, Content Manager, game content and network configuration.

Do not upload live server installations, game/mod files or unedited configurations. Remove passwords, tokens, GUIDs/Steam IDs, IP addresses and personal paths from logs and screenshots. **Save report** redacts recognized values, but you should review the result before sharing. Use sample values and disposable server folders for reproduction.

## Build and check changes

Development requires Windows and the .NET 10 SDK. Visual Studio is optional. Fork the repository, create a branch for your change and work from the repository root.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests
```

This builds the application and runs the core tests with temporary data. The test project is a console runner; `dotnet test` is not its entry point.

For process lifecycle, folder switching, diagnostics or recovery changes, also run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Reliability.ps1
```

These checks create disposable fixtures and briefly run local test processes without an installed game. Their output is placed under `artifacts/`. See [RELIABILITY.md](RELIABILITY.md) for their limits. Checks that need actual game content are described in [README.md](README.md); use disposable servers and do not modify your live installations for a test.

Documentation-only changes need working links and valid Markdown. Issue forms must be valid YAML with unique field IDs. Interface changes should be checked in both English and Russian, including window resizing and empty states. Add meaningful regression coverage when changing behavior that can lose data, overwrite files or affect process ownership.

## Code and content guidelines

- Keep changes focused and follow the style of nearby C# and XAML.
- Keep configuration and content logic in `ServerManager.Core`; keep Windows interface and process coordination in `ServerManager.App`.
- Preserve unknown INI/YAML fields and plugin documents. Keep backup, stale-file, path and process checks in place when changing file operations.
- Keep long-running work off the UI thread and preserve drafts when an operation fails or is cancelled.
- Add UI messages to `Translations.json` in both languages. Translation IDs and Russian lookup strings must be unique.
- Write public documentation in English. Content names and raw server output retain their original language.
- Do not commit local settings, SDKs, build outputs, logs, server data, game/mod files or personal notes. `.gitignore` is a safeguard, not a substitute for reviewing the diff.

See [PROJECT.md](PROJECT.md) for the file map and [DESKTOP.md](DESKTOP.md) for user-facing behavior.

## Submit a pull request

Target `master` and use the pull request template. Explain the problem, the resulting behavior and how you checked it. Include screenshots for visible UI changes and note any tests you could not run. Avoid unrelated formatting or generated files. Version bumps and releases are coordinated by the maintainer.

Review feedback may ask for a smaller scope, additional checks or a different implementation. Contributions must be work you have the right to share under the project's [MIT license](LICENSE); retain required attribution for any permitted third-party material.
