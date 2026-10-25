# AC Server Manager 0.6 reliability notes

0.6 is a local preview focused on reliability. Building this checkout does not publish anything to GitHub. These changes are a step toward 1.0, not a declaration of production stability.

## Changes

- Application preferences use a flushed temporary file and atomic replacement, with `settings.json.bak` retaining the last valid prior file. Truncated JSON and invalid settings schemas can recover from that backup. Identical preferences are not rewritten; failed writes leave the prior file intact.
- Managed servers run in separate hidden Windows consoles. Stop requests Ctrl+C through a short-lived helper which checks the process identity and refuses a shared console containing unrelated processes. The manager waits up to ten seconds for normal exit, then asks before force. External servers are not controlled. Closing is cancelled if the user declines to stop a remaining server.
- Startup confirms a bounded, valid `/INFO` JSON response for the selected track/layout. Process existence, invalid HTTP content and a different track are insufficient. Startup timeout leaves the process available for inspection with **Readiness unconfirmed**, rather than reporting it ready or killing it.
- Startup checks actual port binding availability instead of trusting a potentially stale listener table. Persistent conflicts are reported by protocol and port; an owned restart briefly waits for release without bypassing conflicts.
- Double starts are rejected, stopped process resources are released on restart, stop cancels startup polling, and early/unexpected exits retain the final output and a distinct failure status. A failed restart preserves the previous error log until a replacement process starts successfully.
- Output is read in bounded chunks off the UI thread, with lines capped at 16,384 characters and each server's visible history capped at 100,000 characters. UI text refresh is limited to five times per second. **Follow log** can be disabled.
- Manager errors are saved locally with rotation (one current and one previous file, approximately 256KB each). **Save report** exports version/environment information, process states, manager errors and the selected server's recent bounded output. Configurations and game files are excluded. Known password, token, key and identity fields, the user-profile path and selected local paths are redacted. Arbitrary plugin messages cannot be guaranteed anonymous: review before sharing. No automatic upload or telemetry is added.

Server configuration backups/rollback remain separate from application settings recovery. A multi-file server save is not a transaction that can guarantee recovery from power loss.

## Reproduce automated checks

On Windows with the .NET 10 SDK:

```powershell
.\Build-Desktop.ps1 -Tests
.\Test-Reliability.ps1
```

The second command builds `tests/ServerManager.ProcessFixture`, a disposable console program named `AssettoServer.exe` exclusively for process tests. It never loads game content or existing server configurations. Each test runs under a fresh output directory in `artifacts/`; logs/settings are isolated there. The fixture is never packaged as part of the application.

The process checks cover two simultaneous servers, isolated normal shutdown, final output drain, duplicate launch refusal, malformed/mismatched readiness responses, startup timeout, early failure, repeated restarts, cancellation during startup, 30,000 log lines plus a 300KB line, and Cancel/confirm paths for forced shutdown. They also verify UI dispatcher progress and report redaction. TCP/UDP ports must be available; these checks exercise local networking only.

To test an existing regular or portable build, supply its directory and a fresh output path:

```powershell
.\Test-Reliability.ps1 -ApplicationDirectory .\artifacts\desktop-0.6.0 -OutputDirectory .\artifacts\my-reliability-check
```

GitHub Actions runs these process checks after its core tests and portable build. UI/catalog/wizard checks needing installed game content remain separate; see [README.md](README.md).

## Before 1.0

- Test fresh install and migration on more than one computer, including varied DPI, permissions, Steam libraries and paths containing spaces/Unicode.
- Run extended multi-server sessions with real clients, traffic and plugin output. Fixture readiness does not prove an Assetto Corsa connection or correct content checksums.
- Exercise close/stop/save interaction, interrupted operations and disk failures. Test unexpected process termination and very slow initialization with real server versions.
- Confirm normal shutdown with supported AssettoServer versions and plugins; a plugin which hangs may still require confirmed forced termination.
- Initial local readiness is not continuous health monitoring, an AC client handshake or internet reachability. A process left unconfirmed after the initial timeout is not automatically retried or restarted.
- Plan structured diagnostics and a backup restore interface separately. Presets, cloning, automatic updates and remote server management are outside 0.6.

## Implementation references

The native console behavior follows Microsoft's [console creation](https://learn.microsoft.com/en-us/windows/console/creation-of-a-console), [AttachConsole](https://learn.microsoft.com/en-us/windows/console/attachconsole) and [GenerateConsoleCtrlEvent](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent) documentation. AssettoServer uses the .NET host lifecycle in its [Program.cs](https://github.com/compujuckel/AssettoServer/blob/master/AssettoServer/Program.cs) and publishes its shutdown hooks in [ACServer.cs](https://github.com/compujuckel/AssettoServer/blob/master/AssettoServer/Server/ACServer.cs). Compatibility is verified by tests; the fixture alone cannot establish it for every server/plugin version.
