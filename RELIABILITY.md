# AC Server Manager 0.8 reliability, recovery and reuse notes

0.8 is a local preview adding server copying and reusable presets to diagnostics and backup recovery, building on the 0.6 reliability work. Building this checkout does not publish anything to GitHub. These changes are a step toward 1.0, not a declaration of production stability.

## Changes

- Application preferences use a flushed temporary file and atomic replacement, with `settings.json.bak` retaining the last valid prior file. Truncated JSON and invalid settings schemas can recover from that backup. Identical preferences are not rewritten; failed writes leave the prior file intact.
- Managed servers run in separate hidden Windows consoles. Stop requests Ctrl+C through a short-lived helper which checks the process identity and refuses a shared console containing unrelated processes. The manager waits up to ten seconds for normal exit, then asks before force. External servers are not controlled. Closing is cancelled if the user declines to stop a remaining server.
- Startup confirms a bounded, valid `/INFO` JSON response for the selected track/layout. Process existence, invalid HTTP content and a different track are insufficient. Startup timeout leaves the process available for inspection with **Readiness unconfirmed**, rather than reporting it ready or killing it.
- Startup checks actual port binding availability instead of trusting a potentially stale listener table. Persistent conflicts are reported by protocol and port; an owned restart briefly waits for release without bypassing conflicts.
- Double starts are rejected, stopped process resources are released on restart, stop cancels startup polling, and early/unexpected exits retain the final output and a distinct failure status. A failed restart preserves the previous error log until a replacement process starts successfully.
- Output is read in bounded chunks off the UI thread, with lines capped at 16,384 characters and each server's visible history capped at 100,000 characters. UI text refresh is limited to five times per second. **Follow log** can be disabled.
- Manager errors are saved locally with rotation (one current and one previous file, approximately 256KB each). **Save report** exports version/environment information, process states, manager errors and the selected server's recent bounded output. Configurations and game files are excluded. Known password, token, key and identity fields, the user-profile path and selected local paths are redacted. Arbitrary plugin messages cannot be guaranteed anonymous: review before sharing. No automatic upload or telemetry is added.

- Structured diagnostics operate on an immutable draft snapshot off the UI thread and support cancellation. Errors block startup; warnings and information are separately filterable. External configuration changes are identified before launch. Findings link to settings/catalog/folders and can be exported with the current report.
- Backup history supports complete legacy copies and new hash manifests. A preview records current and stored hashes; restore rechecks the plan, acquires a per-server save/restore lock, captures a safety backup, stages replacement bytes and checks targets again before commit. Ordinary failures roll back applied files where safe. Content restoration is opt-in and restricted to listed supported data files; recorded absences become explicit removals. Unchanged restore creates no new copy.
- Restore is available only for stopped local servers. Fresh process checks reject managed/external processes and unreadable process identities. Paths are confined to the chosen server and backup; game targets, links, traversal and unsupported file types are rejected. Incomplete or invalid pre-recovery configurations are retained in the safety copy for manual recovery, but are not offered as a valid automatic undo.

Server configuration backups/rollback remain separate from application settings recovery. A multi-file server save is not a transaction that can guarantee recovery from power loss.

## Reproduce automated checks

On Windows with the .NET 10 SDK:

```powershell
.\Build-Desktop.ps1 -Tests
.\Test-Reliability.ps1
```

The second command builds `tests/ServerManager.ProcessFixture`, a disposable console program named `AssettoServer.exe` exclusively for process tests. It never loads game content or existing server configurations. Each test runs under a fresh output directory in `artifacts/`; logs/settings are isolated there. The fixture is never packaged as part of the application.

The process checks cover two simultaneous servers, isolated normal shutdown, final output drain, duplicate launch refusal, malformed/mismatched readiness responses, startup timeout, early failure, repeated restarts, cancellation during startup, 30,000 log lines plus a 300KB line, and Cancel/confirm paths for forced shutdown. They also verify UI dispatcher progress and report redaction. Since 0.6.1 the same script runs game-free WPF folder checks: Enter/focus-loss commits, populated/empty/unset server roots, draft-preserving Cancel, accepted discard without file writes, invalid/equivalent paths and game catalog replacement. A live-process check verifies that changing the visible root does not stop or lose ownership of running servers. TCP/UDP ports must be available; these checks exercise local networking only.

To test an existing regular or portable build, supply its directory and a fresh output path:

```powershell
.\Test-Reliability.ps1 -ApplicationDirectory .\artifacts\desktop-0.8.0 -OutputDirectory .\artifacts\my-reliability-check
```

Since 0.7 the script also runs `--recovery-qa` with fake content: actionable draft errors/warnings, severity filters, RU/EN resources, affected-field focus, stale markers, cancellation, omitted optional defaults, read-only checking, backup previews and privacy, confirmation Cancel, exact configuration restore/editor reload, safety undo and stale/incomplete backup refusal. Runtime checks verify that a running server cannot be restored. Core tests additionally inject a mid-commit sharing violation and verify rollback, legacy backups, optional content removal/undo, manifest tampering and path confinement.

GitHub Actions runs these process and game-free UI checks after its core tests and portable build. UI/catalog/wizard checks needing installed game content remain separate; see [README.md](README.md).

## Before 1.0

- Test fresh install and migration on more than one computer, including varied DPI, permissions, Steam libraries and paths containing spaces/Unicode.
- Run extended multi-server sessions with real clients, traffic and plugin output. Fixture readiness does not prove an Assetto Corsa connection or correct content checksums.
- Exercise close/stop/save interaction, interrupted operations and disk failures. Test unexpected process termination and very slow initialization with real server versions.
- Confirm normal shutdown with supported AssettoServer versions and plugins; a plugin which hangs may still require confirmed forced termination.
- Initial local readiness is not continuous health monitoring, an AC client handshake or internet reachability. A process left unconfirmed after the initial timeout is not automatically retried or restarted.
- Test diagnostics and restoration against more server/plugin configurations and manually managed historical backups. Preset import/export, automatic updates and remote server management remain deferred.

## Implementation references

The native console behavior follows Microsoft's [console creation](https://learn.microsoft.com/en-us/windows/console/creation-of-a-console), [AttachConsole](https://learn.microsoft.com/en-us/windows/console/attachconsole) and [GenerateConsoleCtrlEvent](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent) documentation. AssettoServer uses the .NET host lifecycle in its [Program.cs](https://github.com/compujuckel/AssettoServer/blob/master/AssettoServer/Program.cs) and publishes its shutdown hooks in [ACServer.cs](https://github.com/compujuckel/AssettoServer/blob/master/AssettoServer/Server/ACServer.cs). Compatibility is verified by tests; the fixture alone cannot establish it for every server/plugin version.


Diagnostic checksum behavior is checked against AssettoServer's [ChecksumManager.cs](https://github.com/compujuckel/AssettoServer/blob/master/AssettoServer/Server/ChecksumManager.cs). Alternative car archives and explicit missing-car-checksum exceptions are recognized; special CSP track handling remains a warning requiring review. Local readiness and connection checks use direct loopback HTTP without redirects or an HTTP proxy. Matching local bytes alone does not guarantee successful client loading.


## Reuse in 0.8

Copying operates on a stopped source and stages a new sibling/root destination before publishing it. It does not overwrite folders or start the result. The source snapshot is rechecked; cancellation, detected changes or ordinary write errors discard the owned staging directory. Source and active-port conflicts are checked. Core does not inspect Windows processes itself; the app supplies fresh checks before copying and publishing. Multi-file staging does not constitute protection against arbitrary external programs or power loss.

The default identity is separate: blank join password, new admin password, cleared GUID/driver/team fields and disabled lobby publication. Runtime/server content are copied without logs, backups and known cache/user-list locations. Advanced YAML/plugin/cfg files are an explicit opt-in and can retain sensitive values and external paths. Review diagnostics and custom plugin/path/alternative-checksum dependencies before starting a copy.

Presets serialize a strict basic-settings DTO. They do not serialize raw INI/YAML, private fields, network ports, custom spline paths or installation paths. The recoverable JSON store uses a per-library lock; validation rejects malformed schemas, excessive sizes, invalid IDs/numbers and duplicate names. Content validation completes before controls change, and a populated car draft requires a confirmation. Cancel and missing content preserve the prior draft. Preset capture and copy both read saved files.

`Test-Reliability.ps1` now includes `--templates-qa`: actual context-menu save/copy, rename/delete decisions, applying/replacing presets, missing-content behavior, wizard field/layout preservation, review values, selected stopped copies and bilingual dialogs. Runtime QA also refuses copying a running source. `TemplateTests.cs` covers byte preservation, identity clearing, optional plugin files, cancellation/source-change cleanup and library privacy/recovery.
