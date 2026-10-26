using System.IO;
using System.Windows;
using System.Windows.Controls;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class MainWindow
{
    internal string PresetsFile
    {
        get
        {
            var i = Array.FindIndex(args, a => a.EndsWith("-qa") || a == "--smoke");
            var root = IsTestMode && i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : Path.GetDirectoryName(ManagerSettings.SharedFile)!;
            return Path.Combine(root, "presets.json");
        }
    }
    private void OpenPresets(object sender, RoutedEventArgs e)
    {
        if (saving || folderOperation || checkingDiagnostics) return;
        new PresetsWindow(PresetsFile) { Owner = this }.ShowDialog();
    }
    private void SavePreset(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ServerRow row } || saving || folderOperation || checkingDiagnostics) return;
        try
        {
            var saved = ServerProfile.Load(row.Profile.Path);
            var name = AppDialog.Prompt(this, T("Сохраняются трасса, слоты и основные параметры из файлов сервера. Пароли, порты, пути и плагины не включаются. Несохранённые изменения не используются."), T("Сохранить как пресет"), row.Folder[..Math.Min(row.Folder.Length, 80)], s => ServerPresets.Capture(saved, s));
            if (name == null) return; ServerPresets.Save(PresetsFile, ServerPresets.Capture(saved, name));
            Status.Text = T("Пресет сохранён: {0}. Выберите его в мастере создания сервера.", name.Trim());
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async void CloneServer(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ServerRow row } || saving || folderOperation || checkingDiagnostics || !Discard()) return;
        try
        {
            await EnsureFolderStopped(row.Profile.Path);
            var dialog = new CloneServerWindow(ServerProfile.Load(row.Profile.Path), appliedServerRoot, appliedGameRoot, CloneServerAsync) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.CreatedPath == null) return;
            dirty = false; lastSelectedPath = dialog.CreatedPath; ServerRoot.Text = dialog.ServersRoot;
            await InitializeAsync(); if (!IsTestMode) StoreSettings();
            Status.Text = T("Копия создана: {0}. Проверьте настройки перед запуском.", Path.GetFileName(dialog.CreatedPath));
        }
        catch (Exception ex) { Fail(ex); }
    }
    internal async Task<string> CloneServerAsync(ServerCloneRequest request, IProgress<string> progress, CancellationToken token)
    {
        if (saving || folderOperation || checkingDiagnostics) throw new InvalidOperationException(T("С сервером уже выполняется другая операция. Повторите позже."));
        folderOperation = true; SaveBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            await EnsureFolderStopped(request.Source);
            void Check()
            {
                var state = ServerProcessSnapshot.Read();
                if (state.HasUnreadablePaths || state.ByFolder.ContainsKey(Path.GetFullPath(request.Source))) throw new IOException(T("Остановите исходный сервер перед копированием."));
            }
            return (await Task.Run(() => ServerClone.Create(request, appliedGameRoot, progress, token, Check), token)).Path;
        }
        finally { folderOperation = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }
}
