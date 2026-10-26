using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    public async Task RuntimeTest(string output)
    {
        var fixtureIndex = Array.IndexOf(args, "--fixture");
        if (fixtureIndex < 0 || fixtureIndex + 1 >= args.Length) throw new InvalidOperationException("Provide --fixture <test build>/AssettoServer.exe.");
        var fixture = Path.GetFullPath(args[fixtureIndex + 1]);
        var root = Path.Combine(Path.GetFullPath(output), "servers");
        if (Directory.Exists(root)) throw new InvalidOperationException("Runtime QA requires a fresh output folder.");
        Directory.CreateDirectory(root); ServerRoot.Text = root; GameRoot.Text = "";
        var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var beats = 0; heartbeat.Tick += (_, _) => beats++; heartbeat.Start(); logTimer.Start();
        var completed = new StringBuilder();
        var usedPorts = new HashSet<int>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        int FreePort()
        {
            while (true)
            {
                using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
                var port = ((IPEndPoint)socket.LocalEndpoint).Port;
                if (usedPorts.Add(port) && !System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(p => p.Port == port)) return port;
            }
        }
        ServerProfile Create(string name)
        {
            var path = Path.Combine(root, name); var cfg = Path.Combine(path, "cfg"); Directory.CreateDirectory(cfg);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture)!)) File.Copy(file, Path.Combine(path, Path.GetFileName(file)));
            var ports = new HashSet<int>(); while (ports.Count < 3) ports.Add(FreePort()); var p = ports.ToArray();
            File.WriteAllText(Path.Combine(cfg, "server_cfg.ini"), $"[SERVER]\nNAME={name}\nTRACK=fixture\nCONFIG_TRACK=layout\nTCP_PORT={p[0]}\nUDP_PORT={p[1]}\nHTTP_PORT={p[2]}\nMAX_CLIENTS=1\nCARS=car\n");
            File.WriteAllText(Path.Combine(cfg, "extra_cfg.yml"), "EnableAi: false\nEnableWeatherFx: false\n");
            File.WriteAllText(Path.Combine(cfg, "entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=\nAI=none\n");
            return ServerProfile.Load(path);
        }
        async Task<ManagedServer> Start(ServerProfile p, string mode, int delay = 0, int timeout = 2000)
        {
            Scan(); Load(p);
            return await LaunchManagedAsync(p, [mode, ConfigText.Get(p.Ini, "SERVER", "HTTP_PORT"), delay.ToString()], TimeSpan.FromMilliseconds(timeout));
        }
        try
        {
            var first = Create("first"); var second = Create("second");
            using (var blocked = new TcpListener(IPAddress.Loopback, int.Parse(ConfigText.Get(first.Ini, "SERVER", "TCP_PORT"))))
            {
                blocked.Start(); var rejected = false;
                try { await Start(first, "ready"); } catch (InvalidOperationException ex) when (ex.Message.Contains("TCP ")) { rejected = true; }
                Check(rejected && !managed.ContainsKey(first.Path), "An occupied port was ignored or a process started before validation.");
            }
            var backupTexts = first.SavedTexts; backupTexts["server_cfg.ini"] = ConfigText.Set(first.Ini, "SERVER", "NAME", "Backup fixture");
            var backupPath = first.Save(backupTexts, [])!; first = ServerProfile.Load(first.Path);
            var restorePlan = ServerBackups.Preview(first.Path, backupPath);
            var one = await Start(first, "ready"); var two = await Start(second, "ready");
            await Task.WhenAll(one.Startup, two.Startup);
            Check(one.Ready && two.Ready, "Two independent servers did not become ready.");
            Load(first); UpdateProcess();
            var restoreRefused = false;
            try { await RestoreBackupAsync(first, restorePlan); } catch (InvalidOperationException) { restoreRefused = true; }
            Check(restoreRefused && !BackupsButton.IsEnabled && !one.Process.HasExited && ServerBackups.List(first.Path).Count == 1,
                "Restore did not refuse a running server before any write.");
            completed.AppendLine("PASS: backup restore checks fresh process state, refuses running servers without writes and disables the history action.");
            var cloneBlocked = false;
            try { await CloneServerAsync(new(first.Path,root,"running-copy","Copy",FreePort(),FreePort(),FreePort()), new Progress<string>(), CancellationToken.None); }
            catch (InvalidOperationException) { cloneBlocked = true; }
            Check(cloneBlocked && !folderOperation && !Directory.Exists(Path.Combine(root,"running-copy")) && !one.Process.HasExited, "Running source was copied or controls remained blocked.");
            completed.AppendLine("PASS: copying refuses a running source before any destination is created.");
            var emptyRoot = Path.Combine(output, "empty-root"); Directory.CreateDirectory(emptyRoot);
            ServerRoot.Text = emptyRoot;
            Check(await CommitFolderAsync(ServerRoot) && profile == null && !one.Process.HasExited && !two.Process.HasExited && managed.Count == 2,
                "Changing to an empty root stopped or lost ownership of running servers.");
            ServerRoot.Text = root;
            Check(await CommitFolderAsync(ServerRoot) && managed[first.Path] == one && managed[second.Path] == two,
                "Returning to the server root lost tracked processes.");
            var duplicateRejected = false;
            try { await LaunchManagedAsync(first); } catch (InvalidOperationException) { duplicateRejected = true; }
            Check(duplicateRejected && managed[first.Path] == one, "A duplicate start replaced the owned process.");
            Check(await StopManagedAsync(first.Path, one, false), "Graceful stop failed.");
            Check(File.Exists(Path.Combine(first.Path, "shutdown-marker.txt")) && one.Process.ExitCode == 0 && !two.Process.HasExited, "Stop did not deliver Ctrl+C to only the selected server.");
            Check(one.Log.ToString().Contains("FINAL SHUTDOWN LINE"), "Shutdown output was not drained.");
            completed.AppendLine("PASS: two hidden private consoles, isolated Ctrl+C, normal exit and final output drain.");

            foreach (var mode in new[] { "invalid", "mismatch", "early", "delay" })
            {
                var p = Create(mode); var child = await Start(p, mode, mode == "delay" ? 2000 : 0, 600);
                await child.Startup;
                if (mode == "early")
                {
                    await child.Completion; Check(child.Failed && child.Log.ToString().Contains("FINAL STARTUP ERROR"), "Early failure was not retained.");
                    var exe = Path.Combine(p.Path, "AssettoServer.exe"); File.Move(exe, exe + ".unavailable");
                    var rejected = false;
                    try { await LaunchManagedAsync(p); } catch (System.ComponentModel.Win32Exception) { rejected = true; }
                    finally { File.Move(exe + ".unavailable", exe); }
                    Check(rejected && managed[p.Path] == child && child.Log.ToString().Contains("FINAL STARTUP ERROR"), "Failed restart discarded the previous error log.");
                }
                else Check(!child.Ready && !child.Starting, "Unconfirmed startup was marked ready: " + mode);
                if (!child.Process.HasExited) Check(await StopManagedAsync(p.Path, child, false), "Test server did not stop: " + mode);
            }
            completed.AppendLine("PASS: invalid/mismatched HTTP, startup timeout and early failure remain distinct from readiness.");

            var burst = Create("burst"); var before = beats; var flooded = await Start(burst, "burst", timeout: 10000);
            await flooded.Startup;
            Check(flooded.Ready && beats - before >= 2 && flooded.Log.ToString().Length <= 100000 && !flooded.Log.ToString().Contains("burst-secret"), "Burst output blocked UI, leaked a secret or grew without bounds.");
            Check(await StopManagedAsync(burst.Path, flooded, false), "Burst process stop failed.");
            completed.AppendLine("PASS: 30,000 lines and a 300KB line without a newline; UI heartbeat, bounded log and redaction.");

            for (var cycle = 0; cycle < 5; cycle++)
            {
                ManagedServer child;
                try { child = await Start(first, "ready"); }
                catch
                {
                    var state = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                    var debug = string.Join("\n", state.GetActiveTcpConnections().Where(c => c.LocalEndPoint.Port == int.Parse(ConfigText.Get(first.Ini, "SERVER", "HTTP_PORT"))).Select(c => c.LocalEndPoint + " " + c.State));
                    File.WriteAllText(Path.Combine(output, "restart-debug.txt"), $"Cycle {cycle}\n{first.Ini}\n{debug}\n" + string.Join("\n", state.GetActiveTcpListeners().Select(p => p.ToString())));
                    throw;
                }
                await child.Startup;
                Check(child.Ready, "Restart failed.");
                Check(await StopManagedAsync(first.Path, child, false), "Restart cycle stop failed.");
            }
            var cancel = Create("cancel-startup"); var waiting = await Start(cancel, "delay", 2000, 10000);
            await Task.Delay(250);
            Check(await StopManagedAsync(cancel.Path, waiting, false), "Stop during startup failed."); await waiting.Startup;
            Check(!waiting.Ready && !waiting.Failed, "Cancelled startup was marked ready or failed.");
            completed.AppendLine("PASS: repeated restarts and cancellation during startup.");

            var ignore = Create("ignore"); var stubborn = await Start(ignore, "ignore"); await stubborn.Startup;
            var dialogResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialogTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            dialogTimer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d => d.IsVisible && d.IsLoaded) is not { } dialog) return;
                dialogTimer.Stop(); dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dialogResult.TrySetResult();
            };
            dialogTimer.Start();
            Check(!await StopManagedAsync(ignore.Path, stubborn, true), "Declining forced stop reported success.");
            await dialogResult.Task.WaitAsync(TimeSpan.FromSeconds(2)); dialogTimer.Stop();
            Check(!stubborn.Process.HasExited && !stubborn.Stopping && StopButton.IsEnabled, "Declining forced stop killed the process or left controls blocked.");
            var forceResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var forceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            forceTimer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d => d.IsVisible && d.IsLoaded) is not { } dialog) return;
                forceTimer.Stop(); dialog.AcceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); forceResult.TrySetResult();
            };
            forceTimer.Start();
            try { Check(await StopManagedAsync(ignore.Path, stubborn, true), "Confirmed forced stop failed."); await forceResult.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            finally { forceTimer.Stop(); }
            Check(!two.Process.HasExited, "Another server was affected by forced-stop handling.");
            completed.AppendLine("PASS: stubborn process asks before force; Cancel keeps it running and restores controls; confirmation stops only that process.");

            AppDiagnostics.Record(new IOException("PASSWORD=qa-private-secret"), "Runtime QA");
            var report = BuildReport();
            Check(!report.Contains("qa-private-secret") && !report.Contains(root) && report.Contains("Version: " + typeof(App).Assembly.GetName().Version!.ToString(3)), "Report leaked private fixture data or lost version.");
            File.WriteAllText(Path.Combine(output, "report-example.txt"), report);
            completed.AppendLine("PASS: report version, private-path replacement and secret redaction.");
            saving = true;
            try { Check(!await PrepareCloseAsync(false), "Close was allowed during save."); }
            finally { saving = false; }
            async Task<bool> CloseWithAnswer(bool accept)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                timer.Tick += (_, _) =>
                {
                    if (Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d => d.IsVisible && d.IsLoaded) is not { } dialog) return;
                    timer.Stop(); (accept ? dialog.AcceptButton : dialog.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
                try { return await PrepareCloseAsync(false); }
                finally { timer.Stop(); }
            }
            Check(!await CloseWithAnswer(false) && !two.Process.HasExited && !stoppingForClose && SaveBusyOverlay.Visibility == Visibility.Collapsed,
                "Cancelling close stopped a server or left the interface blocked.");
            Check(await CloseWithAnswer(true) && two.Process.HasExited && File.Exists(Path.Combine(second.Path, "shutdown-marker.txt")), "Confirmed close did not stop the remaining server normally.");
            completed.AppendLine("PASS: occupied-port gate, close blocked during save, cancelled close preserves running server, confirmed close stops it normally.");
            File.WriteAllText(Path.Combine(output, "runtime-result.txt"), completed.ToString());
        }
        finally
        {
            heartbeat.Stop(); logTimer.Stop();
            foreach (var child in managed.Values)
            {
                if (!child.Process.HasExited) child.Process.Kill();
                await Task.WhenAll(child.Completion, child.Startup); child.Dispose();
            }
            managed.Clear();
        }
    }
}
