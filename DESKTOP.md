# AC Server Manager 0.5.2 Preview

A Windows application built with C#, WPF and .NET 10. PowerShell is only used for building and packaging.

The window uses a consistent dark theme with custom minimize, maximize and close buttons. The title area supports dragging and double-clicking to maximize; the window can be resized from its edges. Scrollbars use the orange accent color. The main font is Bahnschrift; paths and IDs use Consolas.

The title bar contains **AC / SERVER MANAGER** and the **RU / EN** language selector. Language changes apply immediately and are saved in `settings.json`. Switching languages preserves unsaved settings, selected cars, skins and slot roles. The interface, tooltips and validation messages are translated; content names and raw AssettoServer output retain the text supplied by the mods and server. List item borders keep the same thickness on hover, so the text does not shift.

Buttons and drop-down lists share corner shapes, borders, padding and hover styles. The Content Manager button and language selector use the same height and font; accent colors identify primary actions.

Confirmation dialogs, error messages and tooltips use the application's dark theme. In confirmation dialogs, Enter selects Cancel by default; Escape and the close button also cancel the action. Long messages can be scrolled. The **Content Manager** button next to the language selector opens CM independently of the selected server. If necessary, it asks you to select the CM executable and remembers its path. Discovery uses the saved path, running Content Manager processes, the registered `acmanager` protocol and common portable locations. CM starts from its own folder, independently of the manager installation.

Maximizing uses the native window frame with custom styling through WindowChrome and Windows transitions. The content compensates for the hidden resize frame, accounting for DPI and the current monitor's working area so that bottom controls stay above the taskbar. Animations depend on Windows visual effects settings. See the [WindowChrome documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome) for the underlying behavior.

## Getting started

This guide covers version **0.5.2-preview**. Available Windows x64 packages are listed in [Releases](https://github.com/drkreis/ac-server-manager/releases); a source checkout may be newer than a published package. Extract the entire portable archive into a writable folder and run `AssettoServerManager.exe`; .NET is included. Keep the DLLs and other files alongside the EXE.

For a local build from source, double-click `Start-Manager.cmd` or `artifacts/desktop-0.5.2/AssettoServerManager.exe`. This regular build requires the .NET 10 Desktop Runtime x64.

Building requires the .NET 10 SDK. The script uses the local SDK in `.tools/dotnet` if available, otherwise `dotnet` from PATH. Visual Studio is not required; the SDK and cache are excluded from Git. See [README.md](README.md) for build and portable packaging commands.

## Working with a server

On a fresh launch, the interface is English and the server folder is empty. Select your own location; it is remembered in `settings.json`. An explicitly saved language remains selected. The game folder is discovered through Steam's installation path, additional libraries and Assetto Corsa app manifest; choose it manually if detection fails. Missing remembered paths are cleared or rediscovered. Server folders stay where you put them; importing ZIPs into a manager-owned library is deferred.

To create a new server, click **Create server** above the server list. The five-step wizard installs AssettoServer and generates the configurations without a Content Manager server preset. See [WIZARD.md](WIZARD.md) for the full walkthrough. The result is loaded into the editor; click **Start** when ready.

1. Select your server folder and the game installation folder. Steam libraries are detected automatically. Paths and language are saved in `%LOCALAPPDATA%\ACServerManager\settings.json`, shared across application versions. Older settings beside the EXE and neighboring manager builds are imported; a valid older Content Manager path can fill a missing path without replacing current folder/language preferences.
2. In **Catalog**, search for cars by name, brand or ID. The details panel shows a skin preview and lets you add a player, traffic or mixed player/AI slot.
3. In **Cars and slots**, select skins and roles, filter players and AI, and duplicate or delete slots. The `CAR_0`, `CAR_1` and subsequent numbers match the sections in the generated `entry_list.ini`. **↑ Move up / ↓ Move down** moves the selected car and updates the numbers automatically. When a filter is active, movement uses the neighboring visible slot in the full list. The new order takes effect after saving and restarting the server. To replace a car, select its slot, choose another car in the catalog and click **Replace selected slot**.
4. Each track appears once in the track catalog; search uses the track name and ID. Select a track, choose a layout in its details panel and click **Use this track**. You can also change the current track's layout directly in **Overview**. The preview and pit box count update immediately; server changes take effect after saving and restarting.
5. Check the settings and save. Missing `data.acd`, `surfaces.ini`, `.ai` and `.aip` files are copied from the game to the server. Existing files are not overwritten.
6. Start the server. The manager checks ports, displays logs and checks the HTTP `/INFO` response. This readiness check does not perform an AC client handshake or prove that the game can join; the separate wizard integration checks test handshake acceptance. **Connect** opens the local server directly in CM without using the acstuff.ru website.

After installing new mods, click **Refresh catalog** on the catalog tab. The server draft is preserved. Search fields include hints about what you can search for; the general guidance spans the width below the catalog. Scrollbar thumbs retain a minimum size even in a large catalog. The slot table displays row and column borders. Content IDs are compared without regard to case, matching Windows path behavior; mod descriptions containing literal line breaks are read without modifying the original JSON files.

**Validation** compares installed cars' `data.acd` files with the server copies and reports unpacked data without a `data.acd`, even if an old archive is already present on the server. It also compares the track's `surfaces.ini` and `models_<layout>.ini` files. Differences are reported as findings because CSP may require specially modified server files. Existing content is not replaced automatically; restart the server after synchronizing it.

If a server was started by an older manager, you can connect to it from the new version. To manage its process, stop it in the old manager and start it in the new one. Do not run servers with conflicting ports simultaneously. The manager only stops its own processes and offers to terminate them when closing.

The server list and selected profile header display **Stopped**, **Starting…**, **Running** or **Running externally**. Status updates every three seconds. For external launches, the AssettoServer process is matched to the executable's folder; matching port numbers do not determine which profile owns a process. The tooltip shows its PID. If the process path cannot be accessed, **Status unavailable** is displayed. Process status does not confirm internet accessibility. This manager cannot stop external processes.

### Car details and skins

In **Cars and slots**, double-click a car's **Model** cell. A separate dialog shows available reference fields, tags, description and a skin preview. Missing fields are omitted. The squares above the preview use the skin folder's `livery.png` or `livery.jpg`; the large image uses `preview.jpg` or `preview.png`. A missing image is shown as unavailable, not as another skin's preview. An AI `generated` skin is not silently replaced by opening the dialog.

Select a square and click **Use skin** to update only that slot's draft. Cancel leaves it unchanged. Save and restart the server to apply the change. This dialog is available only from **Cars and slots**; it does not edit game files or provide a 3D showroom.

### Delete a server

Select a stopped local server and click **Delete server** in the action bar, or right-click a server and choose **Delete server**. The context menu targets the clicked row without discarding edits to another server. The confirmation shows its folder and explains that configuration, content, plugins and backups move to the Windows Recycle Bin. Unsaved changes to that server are discarded only after successful removal. Running servers, unreadable process status, network folders, linked directories, game folders and directories containing another server are refused. Stop external servers in their original application first.

Restore an accidentally deleted folder from the Windows Recycle Bin, then refresh the server list. Folder selection and deletion do not move the game installation.

### Rename a server folder

Right-click a stopped local server and choose **Rename folder…**. The dialog validates the new name and refuses an existing destination. Configurations, content, plugins and backups move together; the separate in-game **Name** value is not changed. Unsaved edits and the selected row are preserved. Case-only name changes are supported. Running servers, unknown process status, linked/network folders and game directories are protected. If the selected servers root is the server itself, its remembered path updates to the renamed folder.

### Content metadata and search

Search fields use a compact 34-pixel height and orange #FF5D43 focus border. Orange accents share this resource throughout the interface; the version badge retains its own tint. The placeholder and input share the same font and text origin. Car, skin and track JSON supports UTF-8, Unicode BOM encodings and legacy Western Windows-1252 metadata used by older Kunos content. Decoding is read-only; server configuration handling remains strict UTF-8.

## Saving and recovery

Saving with no actual configuration or content changes does not write files or create a backup. Edits reverted to the loaded draft are also treated as unchanged. Missing server content can still require a content-only save. Only changed configuration files are replaced; the external-edit conflict check also runs for unchanged saves.

Before a real write, the manager copies the three configuration files to `<server>/backups/<date-time-id>/`. Content files being replaced are also backed up. Unknown settings, plugin YAML documents, GUIDs and other slot fields are preserved. Duplicated slots have their GUID, driver name and team cleared.

When slots are removed, `CARS` is rebuilt from the remaining cars and the sections are renumbered. Saving is blocked if the configurations have changed since they were loaded. All files are prepared before replacement; if an error occurs, files already written are restored. Recovery from a power failure during the operation is not covered.

A successful save is reported in the status bar without a confirmation dialog. Validation notices are available in the Validation tab and do not prevent saving; invalid configuration or a failed write still produces an error. After a successful write, the disk baseline is refreshed without recreating the server list, selection, editor or slot objects. A transparent input blocker prevents edits during a write without changing control colors. On failure, the unsaved draft remains available. Server switching and creation are blocked while saving. Saving does not restart the server. To restore a backup, stop the server and copy the backed-up configurations into `cfg`; restore content files as needed.

## Deferred features

Presets and server cloning; packing unpacked data into `data.acd`; updating existing server content; detailed weather, AI and plugin forms; port forwarding and external diagnostics. The presence of AI files does not confirm their suitability for traffic.

## Validation

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Desktop.ps1 -Tests
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --smoke .\artifacts\qa --servers-root 'D:\AC Servers'
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --window-qa .\artifacts\window-qa --servers-root 'D:\AC Servers'
.\artifacts\desktop-0.5.2\AssettoServerManager.exe --ui-qa .\artifacts\details-qa
```

Core tests use temporary servers and check messages in both languages, slot reordering with field preservation, and mod metadata parsing. The smoke test reads real profiles and the catalog, exercises UI actions in memory, checks RU / EN switching while preserving drafts, and verifies language settings compatibility. It renders PNG screenshots in both languages. `--window-qa` additionally shows a test window, checks actual maximize/restore behavior, content bounds and modal dialog button results. These checks do not write live configurations or start servers. The build uses the local SDK or an SDK from PATH.

Core tests also cover creation from a folder and ZIP, generated configuration and content, cancellation, failed installation cleanup, refusal to overwrite an existing server, ZIP path confinement and separation from private source data. Separate wizard integration checks exercise all five steps in Russian and English, start only a newly created test server and check its acceptance of an AC client handshake; see [README.md](README.md) for their behavior and requirements.
