# AC Server Manager: project guide

This guide describes source version **0.4.1-preview**, its files and the steps for publishing it manually. All paths below are relative to the project root unless stated otherwise.

## Source, application and game server

There are three separate things:

| Location | Contents | Purpose |
| --- | --- | --- |
| This repository | C# source, XAML interface, translations, tests, scripts and documentation | Develop and build AC Server Manager. |
| `artifacts/desktop-0.4.1/` or an extracted portable package | `AssettoServerManager.exe`, application DLLs and dependencies | Run the manager. A portable package also includes .NET. |
| Your chosen server folder, for example `Desktop/AC Servers/<server>/` | `AssettoServer.exe`, `cfg/`, server content and plugins | Run an actual Assetto Corsa server. This is outside the repository. |

The manager executable retains the name `AssettoServerManager.exe` even though the product is called **AC Server Manager**. It is a different program from **AssettoServer.exe**. The game and Content Manager are also separate applications.

## Every project file

### Root and GitHub files

| File | Responsibility |
| --- | --- |
| `README.md` | Public introduction, features, download and build instructions. GitHub displays this on the repository home page. |
| `DESKTOP.md` | Guide to the main editor, catalog, process status, saving and recovery. |
| `WIZARD.md` | Five-step walkthrough for creating a new server. |
| `PROJECT.md` | This file: architecture, file map, builds and publishing. |
| `LICENSE` | The project's MIT license, copyright 2026 drkreis. Bundled runtimes have their own notices. |
| `ACServerManager.slnx` | XML solution listing the app, core and test projects. An IDE can open it as one workspace. It does not launch the application. |
| `Build-Desktop.ps1` | Builds the Windows app; `-Tests` also runs core tests. Uses `.tools/dotnet` if present, otherwise the SDK on PATH. |
| `Publish-Desktop.ps1` | Runs core tests, builds a portable Windows x64 package, copies guides/licenses and writes the ZIP and `SHA256SUMS.txt`. Does not publish to GitHub. |
| `Start-Manager.cmd` | Opens `artifacts/desktop-0.4.1/AssettoServerManager.exe`. It launches an existing build; it does not compile source or start a game server. |
| `.gitignore` | Excludes local SDKs, builds, caches, settings, server configurations, backups and game data from new Git additions. |
| `.github/workflows/build.yml` | GitHub Actions build on push, pull request or manual invocation. Runs portable packaging on Windows; currently does not upload artifacts or create releases. |
| `.github/releases/v0.3.0-preview.md` | Historical release notes for the first public release. Its 0.3 version and feature list are intentional. |
| `.github/releases/v0.4.1-preview.md` | Prepared release notes for the new preview. Copy them into the release description when publishing. Keeping this file in Git does not publish a release. |

### `src/ServerManager.App/`: Windows interface

This is the WPF application. **XAML** describes visible controls, layouts and styles. The matching **`.xaml.cs`** file contains C# behavior, such as handling a button click. Files sharing a `partial` class are compiled into the same class; QA files are not independent programs.

| File | Responsibility |
| --- | --- |
| `ServerManager.App.csproj` | App project settings: Windows target, WPF, .NET 10, executable name, versions and reference to Core. Current `Version` is `0.4.1`; `InformationalVersion` is `0.4.1-preview`. |
| `App.xaml` | Application-wide resources: palette, fonts, buttons, drop-downs, tooltips, scrollbars and table styles. |
| `App.xaml.cs` | Startup in `OnStartup`, UI exception handling and dispatch of optional QA command-line modes. Creates the main window in normal operation. |
| `MainWindow.xaml` | Main window layout: header, server sidebar, Overview, Cars and slots, Catalog, Validation, Log and action buttons. Contains the visible version badge. |
| `MainWindow.xaml.cs` | Main editor behavior: profiles and drafts, settings, catalog selection, language changes, slot editing, saving, launching/stopping servers and opening CM. Also contains smoke/window checks and UI helper types. |
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
| `ServerRuntime.cs` | Process status models and external AssettoServer process discovery. Matches profiles by executable folder and exposes status/PID to the interface. |

### `src/ServerManager.Core/`: application logic

Core has no WPF interface. The app and console tests both use it. It contains no external NuGet package references; framework libraries provide its file, ZIP and JSON support.

| File | Responsibility |
| --- | --- |
| `ServerManager.Core.csproj` | .NET 10 class library settings; embeds `Translations.json` into the compiled DLL. |
| `Configuration.cs` | Reads and edits INI/YAML fields, discovers/loads profiles, represents slots, builds configuration text and saves with backups, conflict checks and rollback. Preserves unknown fields instead of rewriting a whole configuration schema. |
| `Catalog.cs` | Reads installed car/skin/track metadata, previews and pit counts; groups layouts by track; finds the game; validates content and plans missing files to copy. Tolerates selected malformed mod JSON without editing game files. |
| `ServerCreation.cs` | Creates a fresh server from a folder or ZIP: validates inputs, installs program files, generates configurations, copies required content, handles staging/cancellation and refuses existing destinations. Generates an open practice session with `IS_OPEN=1`. |
| `Translations.json` | RU/EN UI strings and messages indexed by resource keys. Russian text here is intentional. This file is embedded, so translation edits require a rebuild. |
| `UiText.cs` | Loads embedded translations, selects the language and formats translated messages. |

### `tests/ServerManager.Core.Tests/`: core checks

| File | Responsibility |
| --- | --- |
| `ServerManager.Core.Tests.csproj` | Console test project targeting .NET 10 and referencing Core. It uses a small assertion runner, not a separate test framework. |
| `Program.cs` | Test entry point and assertions for configuration preservation, saving, backups, slot ordering, catalog parsing and translations; invokes creation tests. |
| `CreationTests.cs` | Tests fresh server creation, installation sources, generated settings/content, cancellation, cleanup, private-data separation and ZIP/destination validation. |

Run these tests with `Build-Desktop.ps1 -Tests`, `Publish-Desktop.ps1` or `dotnet run --project tests/ServerManager.Core.Tests -c Release`. `dotnet test` is not the test runner for this project. Core tests use temporary data and do not need the game or existing servers.

## Generated and local folders

| Folder/file | Responsibility | Commit it? |
| --- | --- | --- |
| `.git/` | Local Git history and repository configuration. Git communicates commits through `push`; you do not upload this directory manually. | No manual upload. |
| `.tools/` | Locally installed SDK, CLI home and NuGet cache used by scripts. Not application source. | No. |
| `.local-archive/` | Retained old PowerShell prototype and earlier development files. The current app does not load them. | No. |
| `artifacts/` | Builds, portable ZIPs, checksums, screenshots, logs and temporary QA servers. Old version folders remain as historical local outputs. | No; attach the selected ZIP/checksum to a Release instead. |
| `bin/` in a project | Default compiler output, including executable/library files. | No. |
| `obj/` in a project | Restore/build intermediates and generated WPF code. Recreated by the SDK. | No. |
| `tests/scratch-*/` | Temporary test fixtures if retained after an interrupted test. | No. |
| `settings.json` beside the manager EXE | Local server/game/CM paths and selected language. Different build folders can have different settings. Created when settings are saved. | No. |
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

Clicking **Start** in the manager launches the selected server's separate `AssettoServer.exe`, using that server folder as its working directory, and captures its output. Normal startup checks occupied ports and HTTP `/INFO` readiness; it does not run `HandshakeQA`. The process-status indicator is not an internet reachability check. Clicking **Connect** opens the local address in Content Manager. Creation does not automatically start the new server.

## Build and package

Open PowerShell in the project root. On another computer, install the **.NET 10 SDK for Windows**; a portable package needs no SDK on the user's machine.

```powershell
# Compile and run core checks.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests

# Open the regular build.
.\Start-Manager.cmd

# Create a portable package in a new output folder.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Publish-Desktop.ps1 -OutputRoot .\artifacts\manual-release-0.4.1
```

The default regular build folder is `artifacts/desktop-0.4.1/`; `-OutputDirectory` overrides it. `Start-Manager.cmd` still points to the default folder. Close the manager before rebuilding into a folder whose executable/DLLs it is using, or build into another folder.

The packaging example produces `ACServerManager-0.4.1-preview-win-x64.zip` and `SHA256SUMS.txt`. If that output already exists, choose another `-OutputRoot`. The script intentionally preserves old packages. It includes the four Markdown guides, MIT license and runtime notices, and refuses to include personal `settings.json`.

Optional smoke, window and wizard QA modes are described in [README.md](README.md). They depend on local game content; wizard QA creates and briefly starts its own test server. Ordinary builds and core tests do not start a server.

## Where to edit a feature

- **Colors/fonts/buttons:** start in `App.xaml`; window-specific layout lives in the relevant window's XAML.
- **Main editor behavior:** `MainWindow.xaml.cs`; process discovery/status lives in `ServerRuntime.cs`.
- **Wizard interface/navigation:** `CreateServerWindow.xaml` and `.xaml.cs`; installation/config generation lives in `ServerCreation.cs`.
- **Catalog/content handling:** `Catalog.cs`; configuration parsing/saving lives in `Configuration.cs`.
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

   Modified files show `M`; new files show `??`. Untracked wizard files must be added too. `git diff` alone does not show their contents.

2. Stage the source and project documents, then review the staged change:

   ```powershell
   git add .github .gitignore ACServerManager.slnx Build-Desktop.ps1 Publish-Desktop.ps1 Start-Manager.cmd README.md DESKTOP.md WIZARD.md PROJECT.md LICENSE src tests
   git diff --cached --stat
   git diff --cached
   ```

   This prepares the next commit. `.gitignore` excludes build outputs and local configuration. Confirm that the staged list contains the new wizard files and does not contain game/server data or personal settings.

3. Save a local commit, then upload it:

   ```powershell
   git commit -m "Add server creation wizard and fix practice joining (0.4.1)"
   git push origin master
   ```

   `commit` records a local revision; `push` sends it to GitHub. Complete Git's sign-in if requested. If Git reports that author identity is missing, configure your name/email before retrying the commit; a GitHub noreply email can keep your personal address private. If the push is rejected because the remote has newer commits, fetch and reconcile those changes before retrying. Do not force-push over them. See [GitHub's push guide](https://docs.github.com/en/get-started/using-git/pushing-commits-to-a-remote-repository).

4. Refresh the repository page, inspect the new commit and wait for the **Windows build and core tests** workflow in Actions. A successful workflow does not create a downloadable release; it also does not run local game integration checks.

## Publish the ready application separately

After pushing the source and verifying the intended commit:

1. Open the repository's [Releases](https://github.com/drkreis/ac-server-manager/releases) page and choose **Draft a new release**.
2. Create the tag **`v0.4.1-preview`**, targeting the pushed `master` revision. If this tag already exists, inspect it before reusing it; do not move an existing published version silently.
3. Use the title **AC Server Manager 0.4.1 Preview** and copy `.github/releases/v0.4.1-preview.md` into the description.
4. Attach the matching **`ACServerManager-0.4.1-preview-win-x64.zip`** and **`SHA256SUMS.txt`** from the same packaging output folder.
5. Select **This is a pre-release**, review the attachments, then choose **Publish release**, or **Save draft** to leave it unpublished.

These are separate operations: pushing source, building a package, and publishing a Release. The automatically generated **Source code** ZIP on GitHub does not replace the portable application attachment. See [GitHub's release guide](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository).
