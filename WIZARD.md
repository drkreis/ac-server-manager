# Creating a server in AC Server Manager 0.4.1 Preview

Click **Create server** above the server list. The wizard follows the application's selected language and dark theme. You can go back to edit earlier choices; a server folder is created only after the final confirmation.

## 1. Name and installation

Enter the server name, choose the parent folder for servers and enter a new folder name. The destination must not exist, even if it is empty. Create the server outside the Assetto Corsa installation folder.

Select the game installation folder, containing `content/cars` and `content/tracks`. Installed content is read from this folder; the wizard does not download mods or change game files.

Choose how to install AssettoServer:

- **Download official stable release** fetches the Windows x64 ZIP from the official `compujuckel/AssettoServer` GitHub repository when you confirm creation. Internet access is required.
- **Local AssettoServer program folder** uses the executable, runtime libraries and plugin binaries from an existing installation. Its server configurations, passwords, administrator lists and game content are not copied.
- **Local AssettoServer ZIP** installs the Windows x64 program from a downloaded archive, including an archive with a single enclosing folder.

The new configurations are generated from your choices in all three cases. This is not server cloning. Runtime license and notice files are retained when present in the source.

If the GitHub API rate limit is reached, downloading falls back to GitHub's [direct link to the latest release asset](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases). The source remains the official AssettoServer repository. A local ZIP or program folder can be used when internet access is unavailable.

## 2. Track and layout

Search by track name or ID. Each track appears once; select its layout in the details panel. The preview and pit box count update with the layout.

For traffic, the track needs a suitable `fast_lane.ai` or `fast_lane.aip`. The wizard can use an installed spline or a file you select under **Custom AI spline**. A selected custom spline takes precedence over game spline files for that layout. The wizard checks whether a file exists, but cannot confirm that it was authored for the chosen layout or is suitable for traffic. Cars without AI do not require a spline.

## 3. Cars and slots

Search for an installed car, choose a skin and click **Add car**. Set the quantity and purpose in the selected cars table:

- **Player** reserves slots for players.
- **Traffic** uses fixed AI slots.
- **Player / AI** lets AI use a free player slot.

Add at least one player or mixed slot. The total must be between 1 and 255 and must not exceed the track's reported pit box count. Each quantity must be between 1 and 255. You can remove a selected car before continuing. Slots are generated in table order as `CAR_0`, `CAR_1`, and so on; after creation, individual skins, roles and order can be changed in **Cars and slots**.

Selected cars must have `data.acd` in the game installation. If a mod only contains an unpacked `data` folder, pack its data in Content Manager first. The wizard deliberately blocks this case instead of silently using stale server data.

## 4. Basic settings

The wizard creates an infinite practice session for free roam, with fuel use, damage and tyre wear disabled. Practice is explicitly open to joining players (`IS_OPEN=1`). Choose the fixed start time in `HH:mm` format and optionally set a join password.

An administrator password must contain at least eight characters. Leave it blank to generate a random password. It is stored in the new server's `cfg/server_cfg.ini`; keep this file private.

TCP, UDP and HTTP ports are suggested using known server configurations and current local listeners. You can change them. TCP and HTTP must differ; TCP and UDP can share a number. Occupied local ports are checked again before creating the server, but this cannot reserve ports against another process starting later.

WeatherFX and public lobby registration are optional and off by default. Enabling lobby registration does not configure your firewall, router or internet connectivity. Use the existing local **Connect** action for the initial check.

## 5. Review and create

Review the name, destination, track, slot counts, ports, time and installation source. Click **Create server** to download or copy AssettoServer, write the three configurations and prepare the required car and track data.

Creation is prepared in a temporary sibling folder and published as the requested new folder only after the files are ready. Existing server folders are never overwritten. On failure or cancellation, the wizard removes its own temporary creation folder. A newly created parent folder may remain empty. Cancel during a download or installation to request cancellation; allow it to finish cleanup before closing the window.

On success, the wizard closes and the new server is selected in the main editor. It is **not started automatically**. Click **Start**, wait for the server to become ready, then **Connect** to open it in Content Manager. Content Manager is needed for that connection action, but no CM server configuration is required to create the server.

## Scope of this preview

The wizard creates a basic local server. Presets, cloning, automated mod downloads, `data.acd` packing, router configuration and a full weather/plugin editor are deferred. Review advanced AssettoServer settings separately as needed. Changing server configuration after creation requires saving and restarting the server.

This guide covers source version **0.4.1-preview**. Available portable packages are listed in [GitHub Releases](https://github.com/drkreis/ac-server-manager/releases). Building or packaging locally does not publish a release.
