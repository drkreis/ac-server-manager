using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class MainWindow
{
    private readonly HashSet<string> pendingStarts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer logTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ManagedServer? displayedLog;
    private long displayedLogVersion = -1;
    private bool closeAfterStopping, stoppingForClose;

    private async Task<ManagedServer> LaunchManagedAsync(ServerProfile current, string[]? arguments = null, TimeSpan? timeout = null)
    {
        var path = current.Path;
        if (managed.TryGetValue(path, out var existing) && !existing.Process.HasExited || !pendingStarts.Add(path))
            throw new InvalidOperationException(T("Сервер уже запускается или работает."));
        try
        {
            UpdateProcess();
            var snapshot = await Task.Run(ServerProcessSnapshot.Read);
            if (snapshot.HasUnreadablePaths || snapshot.ByFolder.ContainsKey(path)) throw new InvalidOperationException(T("Сервер уже работает или его статус недоступен."));
            int Port(string key)
            {
                if (!int.TryParse(ConfigText.Get(current.Ini, "SERVER", key), out var number) || number is < 1 or > 65535)
                    throw new InvalidOperationException(T("Некорректный порт: {0}", key));
                return number;
            }
            var tcp = Port("TCP_PORT"); var http = Port("HTTP_PORT"); var udp = Port("UDP_PORT");
            if (tcp == http) throw new InvalidOperationException(T("TCP и HTTP должны использовать разные порты."));
            string[] Conflicts() => ServerPorts.FindConflicts(tcp, http, udp);
            var conflicts = Conflicts();
            // An owned process can exit before Windows' listener table reflects the released socket.
            // Recheck briefly on restart; never bypass a conflict that persists or kill another process.
            if (existing != null && existing.Process.HasExited)
                for (var attempt = 0; conflicts.Length > 0 && attempt < 10; attempt++)
                { await Task.Delay(150); conflicts = Conflicts(); }
            if (conflicts.Length > 0) throw new InvalidOperationException(T("Порты заняты или недоступны: {0}. Остановите использующий их сервер или измените настройки.", string.Join(", ", conflicts)));
            if (closing || stoppingForClose) throw new OperationCanceledException();
            if (existing != null) await Task.WhenAll(existing.Completion, existing.Startup);
            var child = ServerProcess.Start(Path.Combine(path, "AssettoServer.exe"), arguments ?? []);
            var server = new ManagedServer(child.Process) { Output = child.Output, Error = child.Error };
            managed[path] = server;
            existing?.Dispose();
            displayedLog = null;
            server.Readers = Task.WhenAll(Pump(path, server, child.Output), Pump(path, server, child.Error));
            server.Completion = WaitExit(path, server);
            server.Startup = CheckReadinessAsync(current, server, http, timeout ?? TimeSpan.FromSeconds(30));
            UpdateProcess();
            if (profile?.Path == path) Status.Text = T("Процесс запущен; ожидаю ответа HTTP…");
            return server;
        }
        finally { pendingStarts.Remove(path); UpdateProcess(); }
    }

    private async Task CheckReadinessAsync(ServerProfile current, ManagedServer server, int port, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(server.StartupCancellation.Token);
        deadline.CancelAfter(timeout);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            while (!server.Process.HasExited)
            {
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/INFO", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        await response.Content.LoadIntoBufferAsync(65536, deadline.Token);
                        if (ServerReadiness.Matches(await response.Content.ReadAsStringAsync(deadline.Token), current.Track, current.Layout)
                            && !server.Process.HasExited && !server.Stopping)
                        {
                            server.Ready = true; server.Starting = false; UpdateProcess();
                            if (profile?.Path == current.Path) Status.Text = T("Сервер готов локально. Нажмите «Подключиться».");
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
                await Task.Delay(350, deadline.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppDiagnostics.Record(ex, "Server readiness"); }
        finally
        {
            server.Starting = false;
            if (!server.Process.HasExited && !server.Stopping && !server.Ready)
            {
                server.Log.AppendLine(T("Процесс работает, но готовность не подтверждена. Проверьте журнал и настройки."));
                if (profile?.Path == current.Path) Status.Text = T("Процесс работает, но готовность не подтверждена. Проверьте журнал и настройки.");
            }
            if (!closing) UpdateProcess();
        }
    }

    private async Task Pump(string path, ManagedServer server, StreamReader reader)
    {
        // Read bounded fragments, including when a faulty server produces a very long line without a newline.
        var chunk = new char[4096]; var line = new StringBuilder(); var truncated = false;
        void Flush()
        {
            server.Log.AppendLine(line.ToString().TrimEnd('\r') + (truncated ? " [line truncated]" : ""));
            line.Clear(); truncated = false;
        }
        try
        {
            int count;
            while ((count = await reader.ReadAsync(chunk.AsMemory()).ConfigureAwait(false)) > 0)
                for (var i = 0; i < count; i++)
                {
                    if (chunk[i] == '\n') Flush();
                    else if (line.Length < 16384) line.Append(chunk[i]);
                    else truncated = true;
                }
            if (line.Length > 0 || truncated) Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        { AppDiagnostics.Record(ex, "Reading server output"); }
    }

    private void RefreshLog()
    {
        if (profile == null || !managed.TryGetValue(profile.Path, out var server)) return;
        var version = server.Log.Version;
        if (displayedLog == server && displayedLogVersion == version) return;
        displayedLog = server; displayedLogVersion = version;
        var offset = Log.VerticalOffset; var selection = Log.SelectionStart; var length = Log.SelectionLength;
        Log.Text = server.Log.ToString();
        if (FollowLog.IsChecked == true) Log.ScrollToEnd();
        else
        {
            selection = Math.Min(selection, Log.Text.Length);
            Log.Select(selection, Math.Min(length, Log.Text.Length - selection));
            Log.ScrollToVerticalOffset(offset);
        }
    }

    private async Task WaitExit(string path, ManagedServer server)
    {
        try
        {
            await server.Process.WaitForExitAsync();
            server.StartupCancellation.Cancel();
            // Drain both streams before showing the final state, retaining the last error line.
            await server.Readers;
            server.Failed = !server.StopRequested;
            server.Log.AppendLine(T("Процесс завершён, код ") + server.Process.ExitCode);
            if (server.Failed) AppDiagnostics.Record(new InvalidOperationException($"Server exited unexpectedly (code {server.Process.ExitCode})."), "Server process");
            if (closing) return;
            await RefreshProcessStatesAsync();
            UpdateProcess(); RefreshLog();
            if (profile?.Path == path) Status.Text = server.Failed ? T("Сервер неожиданно завершился. Проверьте журнал.") : T("Сервер остановлен");
        }
        catch (Exception ex) { AppDiagnostics.Record(ex, "Observing server exit"); }
    }

    private async Task<bool> StopManagedAsync(string path, ManagedServer server, bool offerForce)
    {
        if (server.Process.HasExited) { await server.Completion; return true; }
        if (server.Stopping) return false;
        server.Stopping = true; server.Starting = false; server.Ready = false; server.StopRequested = true;
        server.StartupCancellation.Cancel(); UpdateProcess();
        try
        {
            var signalled = await ServerProcess.RequestStop(server.Process);
            if (signalled)
                try { await server.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { }
            if (!server.Process.HasExited)
            {
                if (!offerForce || !AppDialog.Confirm(this, T("Сервер не завершился штатно. Принудительно остановить его?"), T("Остановка сервера"), "Остановить принудительно"))
                    return false;
                server.Process.Kill();
                await server.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                server.Log.AppendLine(T("Сервер остановлен принудительно."));
            }
            await server.Completion;
            return true;
        }
        finally { server.Stopping = false; if (!server.Process.HasExited) server.StopRequested = false; if (!closing) UpdateProcess(); }
    }

    private async void StopServer(object sender, RoutedEventArgs e)
    {
        if (profile == null || !managed.TryGetValue(profile.Path, out var server)) return;
        try { await StopManagedAsync(profile.Path, server, true); }
        catch (Exception ex) { Fail(ex); }
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closeAfterStopping || IsTestMode) { closing = true; processTimer.Stop(); logTimer.Stop(); return; }
        e.Cancel = true;
        try
        {
            if (await PrepareCloseAsync(true)) _ = Dispatcher.BeginInvoke(() => { closeAfterStopping = true; Close(); });
        }
        catch (Exception ex) { Fail(ex); }
    }

    private async Task<bool> PrepareCloseAsync(bool persistSettings)
    {
        if (saving || folderOperation || stoppingForClose || pendingStarts.Count > 0) return false;
        if (!Discard()) return false;
        var running = managed.Where(pair => !pair.Value.Process.HasExited).ToArray();
        if (running.Length > 0 && !AppDialog.Confirm(this, T("Закрытие остановит серверы, запущенные этим менеджером. Закрыть?"), T("Работающие серверы"), "Закрыть")) return false;
        stoppingForClose = true; SaveBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            var allStopped = true;
            foreach (var pair in running) if (!await StopManagedAsync(pair.Key, pair.Value, true)) allStopped = false;
            if (!allStopped) return false;
            await Task.WhenAll(managed.Values.SelectMany(s => new[] { s.Completion, s.Startup }));
            if (persistSettings) StoreSettings();
            return true;
        }
        finally { stoppingForClose = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }

    private string BuildReport()
    {
        var runtime = string.Join(Environment.NewLine, serverRows.Select((row, i) => $"Server {i + 1}: {row.State}"));
        if (profile != null && managed.TryGetValue(profile.Path, out var server)) runtime += "\n\nSelected server output (bounded):\n" + server.Log;
        return AppDiagnostics.Report(runtime, (GameRoot.Text, "<game>"), (ServerRoot.Text, "<servers>"), (cmPath, "<ContentManager>"));
    }
    private void ExportReport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = T("Сохранить отчёт"), Filter = "Text (*.txt)|*.txt", FileName = "ACServerManager-report.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, BuildReport(), new UTF8Encoding(false)); Status.Text = T("Отчёт сохранён."); }
        catch (Exception ex) { Fail(ex); }
    }
}
