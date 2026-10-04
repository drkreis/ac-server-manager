# AC Server Manager

**v0.3 · Preview** is a Windows application for configuring existing local [AssettoServer](https://assettoserver.org/) installations. Its main use case is free roam with friends and AI traffic.

The interface is available in Russian and English. The application is built with C#, WPF and .NET 10.

## Features

- Server discovery and import of `server_cfg.ini`, `entry_list.ini` and `extra_cfg.yml`.
- Track, layout, password, port, slot, AI and WeatherFX settings.
- A catalog of installed cars and tracks with previews; layouts are selected in the track details panel.
- Add, replace, duplicate, delete and reorder slots; choose skins and slot roles.
- Checks for missing content and file differences between the game and the server.
- Server start, stop and logs; status indicators for all discovered servers, including those started outside the manager.
- Open Content Manager and connect to a local server.
- Backups before saving, preservation of unknown configuration fields and protection against overwriting changes made by another application.

See the [user guide](DESKTOP.md) for details and limitations.

## Download the application

Download `ACServerManager-0.3.0-preview-win-x64.zip` from [Releases](https://github.com/drkreis/ac-server-manager/releases), extract the **entire archive** into a writable folder and run `AssettoServerManager.exe`. This is a portable Windows x64 build with .NET included; no separate SDK or Runtime installation is required. Keep the other files alongside the EXE.

On first launch, select your server folder and the Assetto Corsa installation folder. Content Manager is required for the Connect action; you can set its path in the application. The game, Content Manager, AssettoServer and mods are not included in the archive.

## Build from source

Building on Windows requires the **.NET 10 SDK**, available through [Microsoft's official installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/windows). The SDK includes the Desktop Runtime. A regular build produced by `Build-Desktop.ps1` requires the .NET 10 Desktop Runtime x64 on the machine running it; the archive from Releases already includes the Runtime.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests
```

The output is placed in `artifacts/desktop/`. Run `Start-Manager.cmd` from the project root or `AssettoServerManager.exe` from the output folder. Keep the DLLs and other build files alongside the EXE.

The script uses the local SDK in `.tools/dotnet` if available, otherwise `dotnet` from PATH. Visual Studio is not required.

To create a portable archive, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Publish-Desktop.ps1
```

The script runs the core tests, publishes the Windows x64 application with .NET included, adds the guide and licenses, and creates a ZIP archive and SHA-256 checksum file in `artifacts/release/`. The first run needs access to NuGet to download the Runtime.

## Validation

`Build-Desktop.ps1 -Tests` builds the application and runs core tests using temporary data. These tests do not require the game or any server installations.

Additional UI checks use local game and server installations:

```powershell
.\artifacts\desktop\AssettoServerManager.exe --smoke .\artifacts\qa
.\artifacts\desktop\AssettoServerManager.exe --window-qa .\artifacts\window-qa
```

These checks read the catalog and configurations, exercise drafts in memory and generate PNG screenshots. They do not write live configurations or start servers. They are not intended for a clean machine without game content.

On pushes and pull requests, [GitHub Actions](.github/workflows/build.yml) runs the core tests and builds the portable Windows package. UI checks that require game content are performed locally.

## Project structure

- `src/ServerManager.App/` — WPF interface and process management.
- `src/ServerManager.Core/` — configurations, catalog and translations.
- `tests/ServerManager.Core.Tests/` — core tests.
- `Build-Desktop.ps1` — regular build; `Publish-Desktop.ps1` — portable archive; `Start-Manager.cmd` — launch a local build.
- `DESKTOP.md` — user guide.

The SDK, builds, settings, logs, backups, server configurations and game content are excluded by `.gitignore`. The old prototype and intermediate outputs were moved to the local `.local-archive/` folder, which is also excluded from Git.

## Preview status

This version has been tested on a local Windows 11 x64 installation. It is an early version for existing servers: server creation, AssettoServer installation, a complete weather/AI/plugin editor, port forwarding and external connectivity checks are not available yet.

Saved configuration changes require a server restart to take effect. Closing the manager prompts you to terminate servers it started. The current Stop action forcibly terminates the process.

This is an independent project distributed under the [MIT license](LICENSE). The .NET components bundled with the release are distributed under their own terms; their license texts and notices are included in the archive's `licenses/` folder.
