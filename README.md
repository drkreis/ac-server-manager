# AC Server Manager

**v0.5.2 · Preview** is a Windows application for creating and configuring local [AssettoServer](https://assettoserver.org/) installations. Its main use case is free roam with friends and AI traffic. This is the source version; available download versions are listed in Releases and may differ from the current checkout.

The interface is available in Russian and English. The application is built with C#, WPF and .NET 10.

## Features

- A five-step server creation wizard: install AssettoServer, select a track and layout, add cars and slots, configure basic settings, and review before creating.
- Download the official stable Windows x64 AssettoServer release, or install from a local ZIP or program folder. Configurations are generated from scratch.
- Server discovery and import of `server_cfg.ini`, `entry_list.ini` and `extra_cfg.yml`.
- Track, layout, password, port, slot, AI and WeatherFX settings.
- A catalog of installed cars and tracks with previews; layouts are selected in the track details panel.
- Add, replace, duplicate, delete and reorder slots; choose skins and slot roles.
- Double-click a car name in Cars and slots to view its reference information, skin icons and preview, then apply a skin to that slot.
- Delete stopped local servers through the Windows Recycle Bin, with confirmation and folder checks.
- Checks for missing content and file differences between the game and the server.
- Server start, stop and logs; status indicators for all discovered servers, including those started outside the manager.
- Open Content Manager and connect to a local server.
- Save only changed files, with backups for real changes, stable editor/list controls and protection against overwriting changes made by another application. Unchanged or reverted drafts do not create backups.
- Rename stopped server folders from the context menu while preserving drafts, files and the separate in-game server name.

See the [user guide](DESKTOP.md) and [server creation guide](WIZARD.md) for details and limitations. The [project guide](PROJECT.md) explains every source file, startup, builds and manual GitHub publishing.

## Download the application

Choose an available `ACServerManager-<version>-win-x64.zip` from [Releases](https://github.com/drkreis/ac-server-manager/releases), extract the **entire archive** into a writable folder and run `AssettoServerManager.exe`. This is a portable Windows x64 build with .NET included; no separate SDK or Runtime installation is required. Keep the other files alongside the EXE. GitHub's automatically generated **Source code** archives contain the source, not the ready-to-run application.

On first launch, select your server folder; no default server location is assumed. The game folder is detected through Steam and its library manifests when possible, or you can select it manually. Paths and language are shared across versions in `%LOCALAPPDATA%\ACServerManager\settings.json`. Legacy settings beside the EXE and nearby older builds are imported automatically, including a missing Content Manager path. Servers remain in your chosen folder. Content Manager is required for the Connect action; you can set its path in the application. The game, Content Manager, AssettoServer and mods are not included in the archive.

## Build from source

Building on Windows requires the **.NET 10 SDK**, available through [Microsoft's official installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/windows). The SDK includes the Desktop Runtime. A regular build produced by `Build-Desktop.ps1` requires the .NET 10 Desktop Runtime x64 on the machine running it; the archive from Releases already includes the Runtime.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests
```

The output is placed in `artifacts/desktop-0.5.2/`. The build script derives the default folder from the project's version; you can override it with `-OutputDirectory`. Run `Start-Manager.cmd` from the project root or `AssettoServerManager.exe` from the output folder. Keep the DLLs and other build files alongside the EXE.

The script uses the local SDK in `.tools/dotnet` if available, otherwise `dotnet` from PATH. Visual Studio is not required.

To create a portable archive, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Publish-Desktop.ps1
```

The script runs the core tests, publishes the Windows x64 application with .NET included, adds the guides and licenses, and creates a ZIP archive and SHA-256 checksum file in `artifacts/release/`. Use `-OutputRoot` to choose another folder; existing package outputs are not overwritten. The first run needs access to NuGet to download the Runtime. Packaging does not upload files or create a GitHub release.

## Validation

`Build-Desktop.ps1 -Tests` builds the application and runs core tests using temporary data. These tests do not require the game or any server installations.

Additional UI checks use local game and server installations:

```powershell
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --smoke .\artifacts\qa --servers-root 'D:\AC Servers'
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --window-qa .\artifacts\window-qa --servers-root 'D:\AC Servers'
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --ui-qa .\artifacts\details-qa
```

Replace `D:\AC Servers` with your own fixture server location. Use `--game-root <path>` if Steam detection is unavailable. Smoke/window checks read the catalog and configurations, exercise drafts in memory and generate PNG screenshots. `--ui-qa` additionally checks fresh startup, actual caret/placeholder coordinates, the car details dialog, stable save controls, unchanged/reverted saves without new backups, failed-save draft preservation, inline validation notices, folder rename and context-menu process guards, and shared settings/Content Manager recovery. Writes are limited to disposable fixtures under its output folder. These checks do not write live configurations or start servers. They are not intended for a clean machine without game content.

Wizard integration checks are separate: `--wizard-qa <output>` installs from an existing program folder, while `--wizard-download-qa <output>` downloads the official AssettoServer ZIP. Each creates a **new test server under the output folder**, starts it, checks HTTP `/INFO`, TCP/UDP listeners and acceptance of an AC client handshake, and stops only that test process. Pass --servers-root <path> to provide the runtime source installations. They require the local game, server installations and the Imola track; the download check also needs internet access. Use a fresh output folder for each run. These checks do not modify existing servers. Acceptance of a handshake does not validate the game's subsequent content loading.

On pushes and pull requests, [GitHub Actions](https://github.com/drkreis/ac-server-manager/blob/master/.github/workflows/build.yml) runs the core tests and builds the portable Windows package. This workflow does not upload downloadable artifacts or publish a release. UI checks that require game content are performed locally.

## Project structure

- `src/ServerManager.App/` — WPF interface and process management.
- `src/ServerManager.Core/` — configurations, catalog and translations.
- `tests/ServerManager.Core.Tests/` — core tests.
- `Build-Desktop.ps1` — regular build; `Publish-Desktop.ps1` — portable archive; `Start-Manager.cmd` — launch a local build.
- `DESKTOP.md` — user guide; `WIZARD.md` — server creation walkthrough; `PROJECT.md` — complete file map and publishing instructions.

The SDK, builds, settings, logs, backups, server configurations and game content are excluded by `.gitignore`. The old prototype and intermediate outputs were moved to the local `.local-archive/` folder, which is also excluded from Git.

## Preview status

This version has been tested on a local Windows 11 x64 installation. The wizard creates free-roam practice servers and installs AssettoServer separately from the application. Presets, server cloning, a complete weather/AI/plugin editor, port forwarding and external connectivity checks are not available yet. A suitable AI spline is required for traffic; the presence of a spline alone does not confirm its suitability.

Saved configuration changes require a server restart to take effect. Closing the manager prompts you to terminate servers it started. The current Stop action forcibly terminates the process.

This is an independent project distributed under the [MIT license](LICENSE). The .NET components bundled with the release are distributed under their own terms; their license texts and notices are included in the archive's `licenses/` folder.
