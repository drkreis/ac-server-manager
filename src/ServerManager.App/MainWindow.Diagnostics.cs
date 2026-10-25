using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class MainWindow
{
    private DiagnosticReport? lastReport;
    private bool diagnosticsStale, checkingDiagnostics;
    private CancellationTokenSource? diagnosticsCancellation;
    private string diagnosticFilter = "all", diagnosticText = "";

    private DiagnosticInput CaptureDiagnosticInput()
    {
        var current = profile ?? throw new InvalidOperationException(T("Выберите сервер."));
        var settings = new Dictionary<string, string>
        {
            ["NAME"] = NameInput.Text, ["TRACK"] = TrackInput.Text, ["CONFIG_TRACK"] = LayoutInput.Text,
            ["MAX_CLIENTS"] = MaxInput.Text, ["TCP_PORT"] = TcpInput.Text, ["UDP_PORT"] = UdpInput.Text, ["HTTP_PORT"] = HttpInput.Text,
            ["FUEL_RATE"] = FuelInput.Text, ["DAMAGE_MULTIPLIER"] = DamageInput.Text, ["TYRE_WEAR_RATE"] = TyresInput.Text,
            ["PASSWORD"] = PasswordInput.Password
        };
        foreach (var key in new[] { "FUEL_RATE", "DAMAGE_MULTIPLIER", "TYRE_WEAR_RATE" })
            if (settings[key].Length == 0 && ConfigText.Get(current.Ini, "SERVER", key, "__absent__") == "__absent__") settings.Remove(key);
        return new(current.Path, settings, current.Slots.Select(s => new DiagnosticSlot(s.Model, s.Skin, s.Ai, s.Number)).ToArray(), current.Extra,
            current.OriginalBytes.ToDictionary(p => p.Key, p => p.Value.ToArray()), AiInput.IsChecked == true);
    }
    private void InvalidateDiagnostics()
    {
        if (lastReport == null) return;
        diagnosticsStale = true; RenderDiagnostics();
    }
    private void SetDiagnostics(DiagnosticReport report)
    {
        lastReport = report; diagnosticsStale = false; RenderDiagnostics();
    }
    private void RenderDiagnostics()
    {
        DiagnosticAllClear.Visibility = Visibility.Collapsed;
        foreach (var button in DiagnosticFilters.Children.OfType<Button>())
            if ((string?)button.Tag == diagnosticFilter) button.SetResourceReference(Control.BorderBrushProperty, "Accent");
            else button.ClearValue(Control.BorderBrushProperty);
        if (profile == null)
        {
            diagnosticText = ""; DiagnosticIssues.ItemsSource = null; DiagnosticSummary.Text = ""; DiagnosticHint.Visibility = Visibility.Visible; DiagnosticStale.Visibility = Visibility.Collapsed;
            return;
        }
        if (lastReport == null)
        {
            diagnosticText = ""; DiagnosticIssues.ItemsSource = null; DiagnosticSummary.Text = T("Проверка ещё не выполнена"); DiagnosticHint.Visibility = Visibility.Visible; DiagnosticStale.Visibility = Visibility.Collapsed;
            return;
        }
        DiagnosticHint.Visibility = Visibility.Collapsed;
        DiagnosticStale.Visibility = diagnosticsStale ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticSummary.Text = T("Ошибок: {0} · предупреждений: {1} · проверено: {2}", lastReport.Errors, lastReport.Warnings, lastReport.CheckedAt.LocalDateTime.ToString("t"));
        var visible = lastReport.Findings.Where(f => diagnosticFilter == "all" || f.Severity.ToString() == diagnosticFilter).ToArray();
        DiagnosticIssues.ItemsSource = visible;
        DiagnosticAllClear.Visibility = lastReport.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        diagnosticText = string.Join("\n\n", lastReport.Findings.Select(f => f.CopyText));
    }
    private void FilterDiagnostics(object sender, RoutedEventArgs e)
    {
        diagnosticFilter = (sender as Button)?.Tag as string ?? "all"; RenderDiagnostics();
    }
    private async void ValidateProfile(object sender, RoutedEventArgs e)
    {
        try { await CheckDiagnosticsAsync(true); } catch (Exception ex) { Fail(ex); }
    }
    internal async Task<DiagnosticReport?> CheckDiagnosticsAsync(bool selectTab, bool savedConfiguration = false)
    {
        if (profile == null || checkingDiagnostics || saving || folderOperation) return null;
        SlotGrid.CommitEdit(DataGridEditingUnit.Cell, true); SlotGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = profile; var selectedCatalog = catalog;
        var input = savedConfiguration ? ServerDiagnostics.FromProfile(selected) : CaptureDiagnosticInput();
        checkingDiagnostics = true; diagnosticsCancellation = new();
        var token = diagnosticsCancellation.Token;
        DiagnosticBusyOverlay.Visibility = Visibility.Visible; CancelDiagnosticButton.Focus();
        if (selectTab) Pages.SelectedIndex = 3;
        try
        {
            var report = await Task.Run(() =>
            {
                var snapshot = ServerProcessSnapshot.Read();
                var running = snapshot.HasUnreadablePaths || snapshot.ByFolder.ContainsKey(input.ServerPath);
                return ServerDiagnostics.Analyze(input, selectedCatalog, true, running, token);
            }, token);
            if (profile != selected) return null;
            SetDiagnostics(report); Status.Text = T("Проверка завершена"); return report;
        }
        catch (OperationCanceledException) { Status.Text = T("Проверка отменена. Файлы не изменены."); return null; }
        catch (Exception ex)
        {
            AppDiagnostics.Record(ex, "Validation");
            var failed = new DiagnosticReport([new("validation.unavailable", DiagnosticSeverity.Error, "Проверка недоступна", "{0}", "Запустите проверку ещё раз.", [], DiagnosticSanitizer.Redact(ex.Message))], DateTimeOffset.Now);
            SetDiagnostics(failed); Status.Text = T("Проверка недоступна"); if (selectTab) Pages.SelectedIndex = 3; return failed;
        }
        finally { checkingDiagnostics = false; DiagnosticBusyOverlay.Visibility = Visibility.Collapsed; diagnosticsCancellation.Dispose(); diagnosticsCancellation = null; }
    }
    private void CancelDiagnosticCheck(object sender, RoutedEventArgs e) => diagnosticsCancellation?.Cancel();
    private void CopyDiagnostic(object sender, RoutedEventArgs e)
    {
        try { if ((sender as Button)?.Tag is DiagnosticFinding finding) { Clipboard.SetText(DiagnosticSanitizer.Redact(finding.CopyText)); Status.Text = T("Подробности скопированы."); } }
        catch (Exception ex) { Fail(ex); }
    }
    private async void RunDiagnosticAction(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DiagnosticAction action || profile == null) return;
        try
        {
            switch (action.Kind)
            {
                case DiagnosticActionKind.Setting:
                    Pages.SelectedIndex = 0;
                    var field = action.Target switch
                    {
                        "NAME" => NameInput, "TRACK" => TrackInput, "CONFIG_TRACK" => LayoutInput, "MAX_CLIENTS" => MaxInput,
                        "TCP_PORT" => TcpInput, "UDP_PORT" => UdpInput, "HTTP_PORT" => HttpInput, "FUEL_RATE" => FuelInput,
                        "DAMAGE_MULTIPLIER" => DamageInput, "TYRE_WEAR_RATE" => TyresInput, "GAME_ROOT" => GameRoot, _ => NameInput
                    };
                    await Dispatcher.InvokeAsync(() => { field.BringIntoView(); field.Focus(); field.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Loaded);
                    break;
                case DiagnosticActionKind.Slots: Pages.SelectedIndex = 1; break;
                case DiagnosticActionKind.Car:
                    Pages.SelectedIndex = 2; CatalogPages.SelectedIndex = 0; CarSearch.Text = action.Target;
                    CarList.SelectedItem = CarList.Items.OfType<CarInfo>().FirstOrDefault(c => c.Id.Equals(action.Target, StringComparison.OrdinalIgnoreCase)); break;
                case DiagnosticActionKind.Track:
                    Pages.SelectedIndex = 2; CatalogPages.SelectedIndex = 1; TrackSearch.Text = action.Target;
                    TrackList.SelectedItem = TrackList.Items.OfType<TrackGroup>().FirstOrDefault(t => t.Id.Equals(action.Target, StringComparison.OrdinalIgnoreCase)); break;
                case DiagnosticActionKind.Folder:
                    var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(action.Boundary!));
                    var folder = Path.GetFullPath(action.Target);
                    if (!folder.Equals(boundary, StringComparison.OrdinalIgnoreCase) && !folder.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(T("Некорректный путь папки."));
                    while (!Directory.Exists(folder) && !folder.Equals(boundary, StringComparison.OrdinalIgnoreCase)) folder = Path.GetDirectoryName(folder)!;
                    if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                    break;
                case DiagnosticActionKind.Reload: ReloadProfile(sender, e); break;
                case DiagnosticActionKind.Save: await SaveProfileAsync(); break;
                case DiagnosticActionKind.ContentManager: LaunchContentManager(null); break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    internal async Task<BackupRestoreResult> RestoreBackupAsync(ServerProfile selected, BackupRestorePlan plan)
    {
        if (profile != selected || plan.ServerPath != selected.Path || saving || folderOperation || checkingDiagnostics)
            throw new InvalidOperationException(T("Выбранный сервер изменился. Откройте копии заново."));
        await EnsureFolderStopped(selected.Path);
        if (profile != selected || saving || folderOperation || checkingDiagnostics)
            throw new InvalidOperationException(T("Выбранный сервер изменился. Откройте копии заново."));
        folderOperation = true; SaveBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            await EnsureFolderStopped(selected.Path);
            var result = await Task.Run(() => ServerBackups.Restore(plan, appliedGameRoot));
            var refreshed = ServerProfile.Load(selected.Path);
            serverRows.FirstOrDefault(r => r.Profile.Path == selected.Path)?.ReplaceProfile(refreshed);
            Load(refreshed);
            Status.Text = T("Восстановлено файлов: {0}. Сервер не запущен.", result.ChangedFiles);
            return result;
        }
        finally { folderOperation = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }
    private async void OpenBackups(object sender, RoutedEventArgs e)
    {
        if (profile == null || saving || folderOperation || checkingDiagnostics) return;
        var selected = profile;
        try
        {
            await EnsureFolderStopped(selected.Path);
            var window = new BackupsWindow(selected.Path, appliedGameRoot, dirty, async plan =>
            {
                var result = await RestoreBackupAsync(selected, plan); selected = profile!; return result;
            }) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex) { Fail(ex); }
    }
}
