using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    internal async Task RecoveryUiTest(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new InvalidOperationException("Recovery QA requires a fresh output folder.");
        Directory.CreateDirectory(output);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Recovery UI operation did not complete."); await Task.Delay(20); }
        }
        async Task Capture(FrameworkElement element, string name)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); element.UpdateLayout();
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var area = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                drawing.DrawRectangle((Brush)FindResource("Bg"), null, area);
                drawing.DrawRectangle(new VisualBrush(element), null, area);
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(output, name)); png.Save(file);
        }
        var server = Path.Combine(output, "servers", "recovery-fixture"); var game = Path.Combine(output, "game");
        Put(Path.Combine(server, "cfg/server_cfg.ini"), "[SERVER]\nNAME=Recovery fixture\nTRACK=fixture\nCONFIG_TRACK=\nMAX_CLIENTS=1\nTCP_PORT=29600\nUDP_PORT=29600\nHTTP_PORT=28081\nPASSWORD=qa-private-password\n");
        Put(Path.Combine(server, "cfg/entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=skin\nAI=none\nGUID=qa-private-guid\n");
        Put(Path.Combine(server, "cfg/extra_cfg.yml"), "EnableAi: false\n---\n!PluginConfiguration\nCustom: preserved\n");
        Put(Path.Combine(server, "AssettoServer.exe"), "fixture only; never executed");
        Put(Path.Combine(server, "content/cars/car/data.acd"), "old version");
        Put(Path.Combine(game, "content/cars/car/data.acd"), "game version");
        Put(Path.Combine(game, "content/cars/car/skins/skin/ui_skin.json"), "{}");
        Put(Path.Combine(game, "content/tracks/fixture/ui/ui_track.json"), "{\"name\":\"Fixture circuit\",\"pitboxes\":\"1\"}");
        ServerRoot.Text = Path.GetDirectoryName(server)!; GameRoot.Text = game; await InitializeAsync();
        var original = ServerBackups.ConfigurationFiles.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(server, "cfg", n)));
        var ini = Path.Combine(server, "cfg", "server_cfg.ini");
        HttpInput.Text = "70000";
        var report = await CheckDiagnosticsAsync(true);
        Check(report?.Errors == 1 && report.Warnings >= 1 && Pages.SelectedIndex == 3, "Raw invalid settings were not shown inline.");
        Check(!Directory.Exists(Path.Combine(server, "backups")) && original.All(p => File.ReadAllBytes(Path.Combine(server, "cfg", p.Key)).SequenceEqual(p.Value)), "Diagnostic check wrote server files.");
        var filter = new Button { Tag = "Error" }; FilterDiagnostics(filter, new RoutedEventArgs());
        Check(DiagnosticIssues.Items.Cast<DiagnosticFinding>().All(f => f.Severity == DiagnosticSeverity.Error), "Severity filter failed.");
        FilterDiagnostics(new Button { Tag = "all" }, new RoutedEventArgs());
        await Capture(RootContent, "diagnostics-en.png");
        LanguagePicker.SelectedIndex = 0;
        Check(DiagnosticSummary.Text.Contains("Ошибок:") && DiagnosticIssues.Items.Cast<DiagnosticFinding>().Any(f => f.Title.Contains("HTTP_PORT")), "Russian report did not refresh.");
        await Capture(RootContent, "diagnostics-ru.png"); LanguagePicker.SelectedIndex = 1;
        var action = report!.Findings.Single(f => f.Code == "setting.range.HTTP_PORT").Actions.Single();
        Activate();
        RunDiagnosticAction(new Button { Tag = action }, new RoutedEventArgs());
        await Wait(() => HttpInput.IsKeyboardFocused);
        Check(Pages.SelectedIndex == 0 && HttpInput.IsKeyboardFocused, "Open setting did not focus the affected field.");
        HttpInput.Text = "28081";
        Check(diagnosticsStale && DiagnosticStale.Visibility == Visibility.Visible, "Edited settings did not mark the report stale.");
        var cancel = CheckDiagnosticsAsync(true); diagnosticsCancellation?.Cancel(); await cancel;
        Check(!checkingDiagnostics && DiagnosticBusyOverlay.Visibility == Visibility.Collapsed && Status.Text.Contains("cancelled"), "Cancelling a check left the editor blocked.");
        // Preview and checks remain read-only; missing optional numeric settings retain server defaults.
        NameInput.Text = "Changed fixture"; await SaveProfileAsync();
        Check(!dirty && NameInput.Text == "Changed fixture", "Save rejected omitted optional defaults.");
        var backup = ServerBackups.List(server, game).Single(); var current = File.ReadAllBytes(ini);
        NameInput.Text = "Unsaved draft to retain on cancel";
        var window = new BackupsWindow(server, game, true, plan => RestoreBackupAsync(profile!, plan)) { Owner = this };
        window.Show(); await Wait(() => window.Plan != null);
        Check(window.IncludeContent.IsChecked == false && window.BackupFiles.Items.Count == 3 && window.Plan!.ChangedFiles > 0, "Backup dialog default scope or comparison is wrong.");
        Check(!window.CurrentFilePreview.Text.Contains("qa-private-password") && !window.StoredFilePreview.Text.Contains("qa-private-password"), "Backup dialog exposed a known secret.");
        Check(window.BackupFiles.Columns.All(c => c.Header is string s && s.Length > 0), "Backup table headers did not resolve translations.");
        await Capture((FrameworkElement)window.Content, "backups-en.png");
        async Task ConfirmRestore(bool accept)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d => d.IsVisible && d.IsLoaded) is not { } dialog) return;
                timer.Stop(); (accept ? dialog.AcceptButton : dialog.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start(); try { await window.RestoreSelectedAsync(); } finally { timer.Stop(); }
        }
        await ConfirmRestore(false);
        Check(dirty && NameInput.Text == "Unsaved draft to retain on cancel" && File.ReadAllBytes(ini).SequenceEqual(current) && ServerBackups.List(server, game).Count == 1, "Cancelled restore changed the draft or files.");
        await ConfirmRestore(true);
        Check(!dirty && NameInput.Text == "Recovery fixture" && original.All(p => File.ReadAllBytes(Path.Combine(server, "cfg", p.Key)).SequenceEqual(p.Value)), "Confirmed restore failed to reload exact configuration bytes.");
        Check(File.ReadAllText(Path.Combine(server, "content/cars/car/data.acd")) == "old version" && managed.Count == 0 && !folderOperation, "Restore changed content, launched a process or left the editor blocked.");
        await Wait(() => window.Plan != null);
        var safety = window.BackupList.Items.OfType<BackupRow>().Single(r => r.Backup.Reason == "BeforeRestore");
        window.BackupList.SelectedItem = safety; await Wait(() => window.Plan?.BackupPath == safety.Backup.Path);
        await ConfirmRestore(true);
        Check(NameInput.Text == "Changed fixture" && File.ReadAllBytes(ini).SequenceEqual(current), "Safety backup did not undo restore through the UI.");
        await Wait(() => window.Plan != null);
        // A comparison cannot silently overwrite an externally edited configuration.
        window.BackupList.SelectedItem = window.BackupList.Items.OfType<BackupRow>().Single(r => r.Backup.Path == backup.Path);
        await Wait(() => window.Plan?.BackupPath == backup.Path);
        File.AppendAllText(ini, "; external change\n"); var external = File.ReadAllBytes(ini);
        await ConfirmRestore(true);
        Check(window.Plan == null && !window.RestoreButton.IsEnabled && File.ReadAllBytes(ini).SequenceEqual(external), "Stale comparison was allowed to overwrite external changes.");
        File.WriteAllBytes(ini, external.Concat(new byte[] { 255 }).ToArray());
        var changedReport = await CheckDiagnosticsAsync(false, true);
        Check(changedReport?.Findings.Any(f => f.Code == "config.external.server_cfg.ini" && f.Severity == DiagnosticSeverity.Error) == true,
            "Startup diagnostics did not report an externally corrupted config inline.");
        File.WriteAllBytes(ini, current);
        var broken = Path.Combine(server, "backups", "broken"); Put(Path.Combine(broken, "server_cfg.ini"), "bad");
        await window.RefreshAsync();
        window.BackupList.SelectedItem = window.BackupList.Items.OfType<BackupRow>().Single(r => !r.Backup.Valid);
        Check(window.Plan == null && !window.RestoreButton.IsEnabled, "Incomplete backup was offered for restore.");
        window.Close();
        Localization.Current.Apply("ru");
        var translated = new BackupsWindow(server, game, false, plan => RestoreBackupAsync(profile!, plan)) { Owner = this };
        translated.Show(); await Wait(() => translated.Plan != null);
        await Capture((FrameworkElement)translated.Content, "backups-ru.png"); translated.Close();
        Localization.Current.Apply("en");
        // Verify the stopped-server gate against a freshly observed live fixture identity in runtime QA.
        Check(!File.Exists(Path.Combine(server, ".ac-server-manager.lock")), "Restore lock was not released.");
        File.WriteAllText(Path.Combine(output, "recovery-result.txt"), "PASS: actionable inline findings, RU/EN, filters, field focus, stale marker, cancellation, read-only checks, omitted defaults, backup list/diffs/privacy, default config-only scope, Cancel/draft preservation, exact restore and UI reload, safety undo, no auto-start, stale comparison refusal and incomplete-copy guard. All data is disposable; no game content or existing server was modified.");
    }
}
