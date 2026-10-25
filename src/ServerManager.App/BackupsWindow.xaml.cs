using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public sealed record BackupRow(BackupInfo Backup)
{
    public string Id => Backup.Id;
    public string DateLabel => Backup.Created == DateTimeOffset.MinValue ? Backup.Id : Backup.Created.ToLocalTime().ToString("g");
    public string ReasonLabel => !Backup.Valid ? T("Неполная или повреждённая копия") : T(Backup.Reason switch { "BeforeRestore" => "Перед восстановлением", "Save" => "Перед сохранением", _ => "Старая резервная копия" });
    public string Color => Backup.Valid ? "#9199A9" : "#FF5D43";
}
public sealed record BackupFileRow(BackupDifference Difference)
{
    public string RelativePath => Difference.RelativePath;
    public string Sizes => $"{Difference.CurrentSize?.ToString() ?? "—"} → {Difference.BackupSize?.ToString() ?? "—"}";
    public string StateLabel => T(Difference.State switch { BackupFileState.Replace => "Заменить", BackupFileState.Create => "Восстановить отсутствующий", BackupFileState.Remove => "Удалить созданный позже", _ => "Без изменений" });
}

public partial class BackupsWindow : Window
{
    private readonly string server, game;
    private bool hasDraft;
    private readonly Func<BackupRestorePlan, Task<BackupRestoreResult>> restore;
    private CancellationTokenSource? previewCancellation;
    private int generation;
    private bool restoring;
    internal BackupRestorePlan? Plan { get; private set; }
    public BackupsWindow(string serverPath, string gamePath, bool dirty, Func<BackupRestorePlan, Task<BackupRestoreResult>> restoreAction)
    {
        server = serverPath; game = gamePath; hasDraft = dirty; restore = restoreAction;
        InitializeComponent(); Title = T("Резервные копии"); ServerCaption.Text = Path.GetFileName(server);
        SourceInitialized += (_, _) => NativeWindow.Attach(this);
        Loaded += async (_, _) => await RefreshAsync();
    }
    internal async Task RefreshAsync()
    {
        var selected = (BackupList.SelectedItem as BackupRow)?.Backup.Path;
        var current = ++generation; previewCancellation?.Cancel(); RestoreButton.IsEnabled = false; Plan = null;
        BackupStatus.Text = T("Читаю резервные копии…");
        try
        {
            var rows = await Task.Run(() => ServerBackups.List(server, game).Select(b => new BackupRow(b)).ToArray());
            if (current != generation) return;
            BackupList.ItemsSource = rows;
            BackupList.SelectedItem = rows.FirstOrDefault(r => r.Backup.Path == selected) ?? rows.FirstOrDefault();
            if (rows.Length == 0) { BackupSummary.Text = T("Резервных копий пока нет"); BackupStatus.Text = T("Копия создаётся перед сохранением реальных изменений."); ClearFiles(); }
            else await PreviewAsync();
        }
        catch (Exception ex) { if (current == generation) ShowError(ex); }
    }
    private void ClearFiles()
    { Plan = null; BackupFiles.ItemsSource = null; CurrentFilePreview.Clear(); StoredFilePreview.Clear(); RestoreButton.IsEnabled = false; }
    private async void BackupSelected(object sender, SelectionChangedEventArgs e) => await PreviewAsync();
    private async void ContentChanged(object sender, RoutedEventArgs e) => await PreviewAsync();
    internal async Task PreviewAsync()
    {
        previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = new();
        var token = previewCancellation.Token; var current = ++generation; ClearFiles();
        if (BackupList.SelectedItem is not BackupRow row) return;
        if (!row.Backup.Valid) { BackupSummary.Text = T("Эта копия недоступна для восстановления"); BackupStatus.Text = row.Backup.Error; return; }
        var content = IncludeContent.IsChecked == true;
        BackupSummary.Text = T("Сравниваю файлы…"); BackupStatus.Text = "";
        try
        {
            var plan = await Task.Run(() => ServerBackups.Preview(server, row.Backup.Path, content, game, token), token);
            if (current != generation || token.IsCancellationRequested) return;
            Plan = plan; BackupFiles.ItemsSource = plan.Differences.Select(d => new BackupFileRow(d)).ToArray();
            BackupFiles.SelectedIndex = 0; RestoreButton.IsEnabled = plan.ChangedFiles > 0;
            BackupSummary.Text = T("Изменится файлов: {0}", plan.ChangedFiles);
            BackupStatus.Text = content ? T("Включены только файлы контента, перечисленные в этой копии. Остальные файлы сохраняются.") : T("Восстанавливаются только три конфига. Контент остаётся на месте.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (current == generation) ShowError(ex); }
    }
    private void FileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (BackupFiles.SelectedItem is not BackupFileRow row) { CurrentFilePreview.Clear(); StoredFilePreview.Clear(); return; }
        CurrentFilePreview.Text = row.Difference.CurrentPreview; StoredFilePreview.Text = row.Difference.BackupPreview;
    }
    private async void ReloadBackups(object sender, RoutedEventArgs e) { if (!restoring) await RefreshAsync(); }
    private void CloseBackups(object sender, RoutedEventArgs e) { if (!restoring) Close(); }
    private void BackupKeyDown(object sender, KeyEventArgs e)
    {
        if (restoring) { e.Handled = true; return; }
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
    private void BackupClosing(object? sender, CancelEventArgs e)
    {
        if (restoring) { e.Cancel = true; return; }
        generation++; previewCancellation?.Cancel();
    }
    private void OpenBackupFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = (BackupList.SelectedItem as BackupRow)?.Backup.Path ?? Path.Combine(server, "backups");
            _ = ServerBackups.CheckedPath(server, Path.GetRelativePath(server, folder));
            if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void RestoreSelected(object sender, RoutedEventArgs e) => await RestoreSelectedAsync();
    internal async Task RestoreSelectedAsync()
    {
        var plan = Plan; if (plan == null || restoring || plan.ChangedFiles == 0) return;
        var details = string.Join("\n", plan.Differences.Where(f => f.State != BackupFileState.Unchanged).Select(f => new BackupFileRow(f).StateLabel + ": " + f.RelativePath));
        var message = T("Восстановить выбранную копию?\n\n{0}\n\nПеред изменением будет создан страховочный бэкап текущих файлов. Сервер не будет запущен автоматически.", details);
        if (hasDraft) message += T("\nНесохранённые изменения редактора будут отброшены после успешного восстановления.");
        if (!AppDialog.Confirm(this, message, T("Восстановление резервной копии"), "Восстановить")) return;
        restoring = true; BackupBusy.Visibility = Visibility.Visible; BusyMessage.Text = T("Восстанавливаю файлы и сохраняю текущую версию…");
        try
        {
            var result = await restore(plan);
            hasDraft = false;
            restoring = false; BackupBusy.Visibility = Visibility.Collapsed;
            await RefreshAsync();
            BackupStatus.Text = result.ChangedFiles == 0 ? T("Файлы уже совпадают с выбранной копией.") : T("Восстановлено файлов: {0}. Текущее состояние сохранено отдельной копией.", result.ChangedFiles);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { restoring = false; BackupBusy.Visibility = Visibility.Collapsed; }
    }
    private void ShowError(Exception ex)
    {
        AppDiagnostics.Record(ex, "Backups"); BackupStatus.Text = ex.Message;
        RestoreButton.IsEnabled = false; Plan = null;
    }
}
