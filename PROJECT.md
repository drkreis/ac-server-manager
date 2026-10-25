# AC Server Manager: project guide

This guide describes source version **0.7.0-preview**, its files and the steps for publishing it manually. All paths below are relative to the project root unless stated otherwise.

## Source, application and game server

There are three separate things:

| Location | Contents | Purpose |
| --- | --- | --- |
| This repository | C# source, XAML interface, translations, tests, scripts and documentation | Develop and build AC Server Manager. |
| `artifacts/desktop-0.7.0/` or an extracted portable package | `AssettoServerManager.exe`, application DLLs and dependencies | Run the manager. A portable package also includes .NET. |
| Your chosen server folder, for example `Desktop/AC Servers/<server>/` | `AssettoServer.exe`, `cfg/`, server content and plugins | Run an actual Assetto Corsa server. This is outside the repository. |

The manager executable retains the name `AssettoServerManager.exe` even though the product is called **AC Server Manager**. It is a different program from **AssettoServer.exe**. The game and Content Manager are also separate applications.

## Every project file

### Root and GitHub files

| File | Responsibility |
| --- | --- |
| `README.md` | Public introduction, features, download and build instructions. GitHub displays this on the repository home page. |
| `CONTRIBUTING.md` | Contributor workflow, appropriate checks, code/documentation conventions and pull request guidance. |
| `CODE_OF_CONDUCT.md` | Community expectations, moderation and GitHub Report content / Report abuse guidance. |
| `SECURITY.md` | Supported-version policy and private vulnerability reporting instructions. |
| `ACCESSIBILITY.md` | Current interface support, known barriers and accessibility feedback route. |
| `.github/ISSUE_TEMPLATE/` | Public bug, feature and accessibility forms, plus links to private security reporting and help. |
| `.github/PULL_REQUEST_TEMPLATE.md` | Pull request description and validation checklist. |
| `DESKTOP.md` | Guide to the main editor, catalog, process status, saving and recovery. |
| `WIZARD.md` | Five-step walkthrough for creating a new server. |
| `PROJECT.md` | This file: architecture, file map, builds and publishing. |
| `RELIABILITY.md` | Current 0.7 reliability, diagnostics and recovery scope, isolated process test instructions and known limits. |
| `Test-Reliability.ps1` | Builds disposable test processes and runs the manager lifecycle tests without a game installation. |
| `LICENSE` | The project's MIT license, copyright 2026 drkreis. Bundled runtimes have their own notices. |
| `ACServerManager.slnx` | XML solution listing the app, core and test projects. An IDE can open it as one workspace. It does not launch the application. |
| `Build-Desktop.ps1` | Builds the Windows app; `-Tests` also runs core tests. Uses `.tools/dotnet` if present, otherwise the SDK on PATH. |
| `Publish-Desktop.ps1` | Runs core tests, builds a portable Windows x64 package, copies guides/licenses and writes the ZIP and `SHA256SUMS.txt`. Does not publish to GitHub. |
| `Start-Manager.cmd` | Opens `artifacts/desktop-0.7.0/AssettoServerManager.exe`. It launches an existing build; it does not compile source or start a game server. |
| `.gitignore` | Excludes local SDKs, builds, caches, settings, server configurations, backups and game data from new Git additions. |
| `.github/workflows/build.yml` | GitHub Actions build on push, pull request or manual invocation. Runs portable packaging on Windows; currently does not upload artifacts or create releases. |
| `.github/releases/v0.3.0-preview.md` | Historical release notes for the first public release. Its 0.3 version and feature list are intentional. |
| `.github/releases/v0.4.1-preview.md` | Historical notes for the wizard release and practice joining fix. |
| `.github/releases/v0.5.0-preview.md` and `v0.5.1-preview.md` | Historical notes for intermediate local previews. |
| `.github/releases/v0.5.2-preview.md` | Historical notes for the previous public release. |
| `.github/releases/v0.6.0-preview.md` | Historical notes for the reliability preview. |
| `.github/releases/v0.6.1-preview.md` | Historical notes for the folder-switching fix. |
| `.github/releases/v0.7.0-preview.md` | Prepared release notes for the new preview. Copy them into the release description when publishing. Keeping this file in Git does not publish a release. |

### `src/ServerManager.App/`: Windows interface

This is the WPF application. **XAML** describes visible controls, layouts and styles. The matching **`.xaml.cs`** file contains C# behavior, such as handling a button click. Files sharing a `partial` class are compiled into the same class; QA files are not independent programs.

| File | Responsibility |
| --- | --- |
| `ServerManager.App.csproj` | App project settings: Windows target, WPF, .NET 10, executable name, versions and reference to Core. Current `Version` is `0.7.0`; `InformationalVersion` is `0.7.0-preview`. |
| `App.xaml` | Application-wide resources: palette, fonts, buttons, drop-downs, tooltips, scrollbars and table styles. |
| `App.xaml.cs` | Startup in `OnStartup`, UI exception handling and dispatch of optional QA command-line modes. Creates the main window in normal operation. |
| `MainWindow.xaml` | Main window layout: header, server sidebar, Overview, Cars and slots, Catalog, Validation, Log and action buttons. Contains the visible version badge. |
| `MainWindow.xaml.cs` | Main editor behavior: profiles and drafts, settings, catalog selection, language changes, slot editing, saving, launching/stopping servers and opening CM. Also contains smoke/window checks and UI helper types. |
| `MainWindow.Diagnostics.cs` | Captures draft diagnostics, filters findings/actions, runs cancellable checks and coordinates stopped-server restore with editor reload. |
| `BackupsWindow.xaml` / `BackupsWindow.xaml.cs` | Backup history, optional content scope, per-file comparison, redacted previews, confirmation and restore progress. |
| `MainWindow.RecoveryQA.cs` | Game-free WPF diagnostics/restore tests with screenshots and disposable fixtures. |
| `MainWindow.ServerActions.cs` | Slot double-click handling and stopped-server folder actions: context menus, rename with draft preservation, process checks and Windows Recycle Bin deletion. |
| `ManagerSettings.cs` | Shared per-user settings and migration of older per-build settings, including recovery of the Content Manager path. |
| `MainWindow.DetailsQA.cs` | Optional UI checks for first launch, actual search caret geometry, car dialog behavior, saving and failure recovery, context-menu targets and process guards. Writes disposable fixtures under its output folder. |
| `CarDetailsWindow.xaml` | Single-car dialog layout, skin swatches, preview, reference fields and apply/cancel actions. |
| `CarDetailsWindow.xaml.cs` | Populates car information and switches previews; returns the chosen skin without editing game files or saving a server. |
| `MainWindow.WizardQA.cs` | Wizard integration test orchestration: creates a separate test server, starts it, checks HTTP/listeners/handshake and stops its own process. |
| `CreateServerWindow.xaml` | Layout of the five-step creation wizard. |
| `CreateServerWindow.xaml.cs` | Wizard navigation, input validation, choices, progress/cancellation and calling the installation/creation services. |
| `CreateServerWindow.QA.cs` | Exercises the actual wizard controls and rejection cases; captures screenshots. Used by the optional wizard QA modes. |
| `AssettoServerDownload.cs` | Downloads the official stable Windows x64 AssettoServer ZIP, with progress, cancellation and size/hash checks. The HTTP client identifies itself using the built application version. |
| `HandshakeQA.cs` | Test-only AC TCP handshake client. Checks whether a created server accepts an initial connection; it does not load the game or validate all later content checks. |
| `AppDialog.xaml` | Dark-themed dialog layout. |
| `AppDialog.xaml.cs` | Confirmation/notification behavior, result handling and safe Cancel defaults. |
| `Localization.cs` | Applies RU/EN resources to the WPF interface and updates translated bindings. |
| `NativeWindow.cs` | Windows frame integration: monitor working area, maximize bounds, DPI and window transitions. |
| `MainWindow.Folders.cs` | Committed path handling, Enter/focus/chooser updates, draft-safe folder changes, empty editor state and profile event cleanup. |
| `MainWindow.FoldersQA.cs` | Game-free WPF checks for empty/populated roots, routed path events, invalid paths, discard decisions and catalog replacement. |
| `MainWindow.Lifecycle.cs` | Managed process launch, readiness, bounded output readers, normal/forced stop, close coordination and diagnostic export. |
| `ServerProcess.cs` | Windows private hidden console creation and an isolated, identity-checked Ctrl+C helper. |
| `AppDiagnostics.cs` | Local rotated manager error journal and diagnostic report composition. |
| `MainWindow.RuntimeQA.cs` | Disposable process tests for readiness, output bursts, restarts and stop confirmation. |
| `ServerRuntime.cs` | Process status models and external AssettoServer process discovery. Matches profiles by executable folder and exposes status/PID to the interface. |

### `src/ServerManager.Core/`: application logic

Core has no WPF interface. The app and console tests both use it. It contains no external NuGet package references; framework libraries provide its file, ZIP and JSON support.

| File | Responsibility |
| --- | --- |
| `ServerManager.Core.csproj` | .NET 10 class library settings; embeds `Translations.json` into the compiled DLL. |
| `Configuration.cs` | Reads and edits INI/YAML fields, discovers/loads profiles, represents slots, builds configuration text and saves with backups, conflict checks and rollback. Preserves unknown fields instead of rewriting a whole configuration schema. |
| `Catalog.cs` | Reads installed car/skin/track metadata, previews and pit counts; groups layouts by track; finds the game; validates content and plans missing files to copy. Tolerates selected malformed mod JSON without editing game files. |
| `ContentMetadata.cs` | Read-only UTF-8/Unicode/legacy Windows-1252 decoding for game metadata and conversion of description HTML to plain text. |
| `ContentManagerLocator.cs` | Finds a valid CM executable from saved/running paths, registered protocol commands and known portable folders. Parses commands without running a shell. |
| `ServerRename.cs` | Validates folder names and destinations, moves a server without rewriting files, supports case-only changes and rejects collisions. Process checks live in the app. |
| `ServerRemoval.cs` | Validates a selected removal target, excluding game paths, nested servers, links and network folders. Actual recycling is performed by the Windows app after confirmation. |
| `ServerCreation.cs` | Creates a fresh server from a folder or ZIP: validates inputs, installs program files, generates configurations, copies required content, handles staging/cancellation and refuses existing destinations. Generates an open practice session with `IS_OPEN=1`. |
| `Translations.json` | RU/EN UI strings and messages indexed by resource keys. Russian text here is intentional. This file is embedded, so translation edits require a rebuild. |
| `Reliability.cs` | Atomic JSON preference storage, bounded thread-safe logs, known-secret redaction and rotated error journals. |
| `ServerPorts.cs` | Probes actual IPv4 TCP/UDP port availability before launch, avoiding stale listener tables. |
| `ServerReadiness.cs` | Bounded `/INFO` JSON validation for the expected track and layout. |
| `ServerDiagnostics.cs` | Typed local findings and actions; required/numeric/slot/content/port checks, configuration baselines and read-only checksum comparison. |
| `ServerBackups.cs` | Legacy/new backup discovery, manifests, scoped paths, hashes, previews, operation locking, safety capture, staged restore and rollback. Running-process guards live in the app. |
| `UiText.cs` | Loads embedded translations, selects the language and formats translated messages. |

The separate `tests/ServerManager.ProcessFixture/` console project simulates HTTP readiness, failed startup, noisy output and Ctrl+C handling. It uses the executable name `AssettoServer.exe` only inside test outputs and is never included in the application package. It is not an Assetto Corsa server.

### `tests/ServerManager.Core.Tests/`: core checks

| File | Responsibility |
| --- | --- |
| `ServerManager.Core.Tests.csproj` | Console test project targeting .NET 10 and referencing Core. It uses a small assertion runner, not a separate test framework. |
| `Program.cs` | Test entry point and assertions for configuration preservation, saving, backups, slot ordering, catalog parsing and translations; invokes creation tests. |
| `BackupTests.cs` | Backup compatibility, exact recovery, safety undo, optional content, absent-file recovery, stale/tamper/path guards and rollback. |
| `DiagnosticsTests.cs` | Actionable findings, severity/overrides, external changes, port conflicts, cancellation, redaction and read-only checks. |
| `ReliabilityTests.cs` | Settings write/recovery/conflict tests, concurrent writes, log bounds/redaction/rotation and readiness validation. |
| `CreationTests.cs` | Tests fresh server creation, installation sources, generated settings/content, cancellation, cleanup, private-data separation and ZIP/destination validation. |
| `SaveAndFolderTests.cs` | No-op/content-only saves, backups and timestamps, stale baselines, real temporary-folder renames/collisions/case-only changes and CM discovery. |
| `ContentAndRemovalTests.cs` | Legacy/Unicode metadata, car fields, skin image discovery, Steam library discovery and server removal boundary checks. |

Run these tests with `Build-Desktop.ps1 -Tests`, `Publish-Desktop.ps1` or `dotnet run --project tests/ServerManager.Core.Tests -c Release`. `dotnet test` is not the test runner for this project. Core tests use temporary data and do not need the game or existing servers.

## Generated and local folders

| Folder/file | Responsibility | Commit it? |
| --- | --- | --- |
| `.git/` | Local Git history and repository configuration. Git communicates commits through `push`; you do not upload this directory manually. | No manual upload. |
| `.tools/` | Locally installed SDK, CLI home and NuGet cache used by scripts. Not application source. | No. |
| `.local-archive/` | Retained old PowerShell prototype and earlier development files. The current app does not load them. | No. |
| `artifacts/` | Builds, portable ZIPs, checksums, screenshots, logs and temporary QA servers. Old version folders remain as historical local outputs. | No; attach the selected ZIP to a Release instead. |
| `bin/` in a project | Default compiler output, including executable/library files. | No. |
| `obj/` in a project | Restore/build intermediates and generated WPF code. Recreated by the SDK. | No. |
| `tests/scratch-*/` | Temporary test fixtures if retained after an interrupted test. | No. |
| `%LOCALAPPDATA%\ACServerManager\settings.json` | Shared per-user server/game/CM paths and language, reused across versions. Older per-build settings are read for migration. QA modes isolate settings and never write this file. | No. |
| `licenses/` in a portable package | License and third-party notices for the bundled .NET runtime. Generated during packaging. | Include in the release ZIP. |

An ordinary build also contains `AssettoServerManager.dll`, `ServerManager.Core.dll`, dependency/runtime JSON files and possibly PDB debugging symbols. The EXE is the Windows launch host for the compiled app. A portable package adds .NET runtime DLLs. Do not copy just the EXE out of either build.

Server directories live at the path chosen in the application. Their `cfg/server_cfg.ini` controls the session, ports and passwords; `cfg/entry_list.ini` defines `CAR_n` slots; `cfg/extra_cfg.yml` contains AssettoServer-specific settings. `content/` holds server-side content data, `plugins/` holds plugins, and `backups/` holds manager-created backups. These are operational files, not manager source.

## How startup works

```text
Start-Manager.cmd (or double-click the packaged EXE)
  -> AssettoServerManager.exe
  -> WPF-generated entry point -> App.OnStartup
  -> shared styles/resources -> MainWindow
  -> settings.json -> server discovery and content catalog
```

WPF generates the executable entry point from `App.xaml` during compilation. You do not run each `.cs` or `.xaml` file separately. The test project's `Program.cs` is the entry point for tests only.

Clicking **Start** in the manager launches the selected server's separate `AssettoServer.exe`, using that server folder as its working directory, and captures its output. Normal startup checks occupied ports and HTTP `/INFO` readiness; it does not run `HandshakeQA`. The process-status indicator is not an internet reachability check. Each managed process has a private hidden console. Normal stop uses an isolated helper to request Ctrl+C; forced termination requires confirmation if normal stop fails. Clicking **Connect** opens the local address in Content Manager. Creation does not automatically start the new server.

## Build and package

Open PowerShell in the project root. On another computer, install the **.NET 10 SDK for Windows**; a portable package needs no SDK on the user's machine.

```powershell
# Compile and run core checks.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests

# Open the regular build.
.\Start-Manager.cmd

# Create a portable package in a new output folder.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Publish-Desktop.ps1 -OutputRoot .\artifacts\manual-release-0.7.0
```

The default regular build folder is `artifacts/desktop-0.7.0/`; `-OutputDirectory` overrides it. `Start-Manager.cmd` still points to the default folder. Close the manager before rebuilding into a folder whose executable/DLLs it is using, or build into another folder.

The packaging example produces `ACServerManager-0.7.0-preview-win-x64.zip` and `SHA256SUMS.txt`. If that output already exists, choose another `-OutputRoot`. The script intentionally preserves old packages. It includes the five Markdown guides, MIT license and runtime notices, and refuses to include personal `settings.json`.

Optional smoke, window and wizard QA modes are described in [README.md](README.md). They depend on local game content; wizard QA creates and briefly starts its own test server. Ordinary builds and core tests do not start a server.

## Where to edit a feature

- **Colors/fonts/buttons:** start in `App.xaml`; window-specific layout lives in the relevant window's XAML.
- **Main editor behavior:** `MainWindow.xaml.cs`; process discovery/status lives in `ServerRuntime.cs`.
- **Wizard interface/navigation:** `CreateServerWindow.xaml` and `.xaml.cs`; installation/config generation lives in `ServerCreation.cs`.
- **Catalog/content handling:** `Catalog.cs`; configuration parsing/saving lives in `Configuration.cs`.
- **Diagnostics/recovery:** `ServerDiagnostics.cs` and `MainWindow.Diagnostics.cs`; backups use `ServerBackups.cs` and `BackupsWindow.xaml` / `.xaml.cs`.
- **Translation:** `Translations.json`, then the localization helpers for new binding behavior.

After an edit, rebuild. Editing source does not change an EXE already compiled or running.

For a future version bump, update `Version` and `InformationalVersion` in the app project, the badge in `MainWindow.xaml`, the path in `Start-Manager.cmd`, and active documentation/examples. Build/package paths and the download User-Agent derive their version from the project/assembly. Retain historical release notes and old output folders with their original versions.

## Publish the source yourself

For this existing checkout, the remote is `origin` pointing to `https://github.com/drkreis/ac-server-manager.git`, and the current branch is `master`. No new repository, `git init`, remote setup or branch rename is needed. Check these values before using the commands in a different checkout.

1. Open PowerShell in the project folder and inspect the changes:

   ```powershell
   git status --short
   git branch --show-current
   git remote -v
   git diff --check
   git diff --stat
   ```

   Modified files show `M`; new files show `??`. New diagnostics, recovery and QA files must be added too. `git diff` alone does not show their contents.

2. Stage the source and project documents, then review the staged change:

   ```powershell
   git add .github .gitignore ACServerManager.slnx Build-Desktop.ps1 Publish-Desktop.ps1 Start-Manager.cmd README.md DESKTOP.md WIZARD.md PROJECT.md RELIABILITY.md Test-Reliability.ps1 LICENSE src tests
   git diff --cached --stat
   git diff --cached
   ```

   This prepares the next commit. `.gitignore` excludes build outputs and local configuration. Confirm that the staged list contains the new diagnostics/recovery files and does not contain game/server data or personal settings.

3. Save a local commit, then upload it:

   ```powershell
   git commit -m "Add diagnostics and backup recovery (0.7.0)"
   git push origin master
   ```

   `commit` records a local revision; `push` sends it to GitHub. Complete Git's sign-in if requested. If Git reports that author identity is missing, configure your name/email before retrying the commit; a GitHub noreply email can keep your personal address private. If the push is rejected because the remote has newer commits, fetch and reconcile those changes before retrying. Do not force-push over them. See [GitHub's push guide](https://docs.github.com/en/get-started/using-git/pushing-commits-to-a-remote-repository).

4. Refresh the repository page, inspect the new commit and wait for the **Windows build and core tests** workflow in Actions. A successful workflow does not create a downloadable release; it also does not run local game integration checks.

## Publish the ready application separately

After pushing the source and verifying the intended commit:

1. Open the repository's [Releases](https://github.com/drkreis/ac-server-manager/releases) page and choose **Draft a new release**.
2. Create the tag **`v0.7.0-preview`**, targeting the pushed `master` revision. If this tag already exists, inspect it before reusing it; do not move an existing published version silently.
3. Use the title **AC Server Manager 0.7.0 Preview** and copy `.github/releases/v0.7.0-preview.md` into the description.
4. Attach the matching **`ACServerManager-0.7.0-preview-win-x64.zip`**. The packaging script also generates `SHA256SUMS.txt` for local verification; a separate checksum attachment is not required for this release.
5. Select **This is a pre-release**, review the attachments, then choose **Publish release**, or **Save draft** to leave it unpublished.

These are separate operations: pushing source, building a package, and publishing a Release. The automatically generated **Source code** ZIP on GitHub does not replace the portable application attachment. See [GitHub's release guide](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository).
