using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class MainWindow
{
    private void BlockBusyKeyboard(object sender, KeyEventArgs e) { if (saving || folderOperation) e.Handled = true; }
    private void ServerMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (saving || folderOperation || e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ServerList, source) is not ListBoxItem item)
        { e.Handled = true; return; }
        PrepareServerMenu(item);
    }
    private void PrepareServerMenu(ListBoxItem item)
    {
        if (item.DataContext is not ServerRow row) return;
        var menu = item.ContextMenu;
        menu.Items.Clear();
        var rename = new MenuItem { Tag = row };
        rename.SetResourceReference(HeaderedItemsControl.HeaderProperty, "RenameFolder");
        rename.SetBinding(IsEnabledProperty, new Binding(nameof(ServerRow.CanDelete)) { Source = row });
        rename.Click += RenameServer;
        menu.Items.Add(rename);
        var remove = new MenuItem { Tag = row };
        remove.SetResourceReference(HeaderedItemsControl.HeaderProperty, "DeleteServer");
        remove.SetBinding(IsEnabledProperty, new Binding(nameof(ServerRow.CanDelete)) { Source = row });
        remove.Click += DeleteServer;
        menu.Items.Add(remove);
    }
    private async Task EnsureFolderStopped(string path)
    {
        var snapshot = await Task.Run(ServerProcessSnapshot.Read);
        if (snapshot.HasUnreadablePaths || snapshot.ByFolder.ContainsKey(path)
            || managed.TryGetValue(path, out var running) && !running.Process.HasExited)
            throw new InvalidOperationException(T("Остановите сервер перед изменением папки. Если статус недоступен, проверьте процессы вручную."));
    }
    private async void RenameServer(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ServerRow row } || saving || folderOperation || IsTestMode) return;
        try
        {
            await EnsureFolderStopped(row.Profile.Path);
            var name = AppDialog.Prompt(this, T("Имя папки меняет название в левом списке. Название сервера в игре остаётся прежним.\nКонфиги, контент и бэкапы сохраняются."), T("Переименовать папку сервера"), row.Folder,
                value => ServerRename.Validate(ServerRoot.Text, row.Profile.Path, value, GameRoot.Text));
            if (name != null) await RenameServerAsync(row, name);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task RenameServerAsync(ServerRow row, string name)
    {
        if (saving || folderOperation) return;
        var oldPath = row.Profile.Path;
        folderOperation = true; SaveBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            var target = ServerRename.Validate(ServerRoot.Text, oldPath, name, GameRoot.Text);
            if (target == oldPath) return;
            await EnsureFolderStopped(oldPath);
            target = ServerRename.Move(ServerRoot.Text, oldPath, name, GameRoot.Text);
            row.Profile.Relocate(target);
            if (managed.Remove(oldPath, out var old)) old.Process.Dispose();
            if (profile == row.Profile)
            {
                lastSelectedPath = target; ProfileTitle.Text = row.Folder;
                if (ServerRoot.Text.TrimEnd('\\').Equals(oldPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) ServerRoot.Text = target;
            }
            row.RefreshProfile(); UpdateProcess();
            if (!IsTestMode) StoreSettings();
            Status.Text = T("Папка переименована: {0}", name);
            if (dirty) Status.Text += T(" · есть несохранённые изменения");
        }
        finally { folderOperation = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }
    private void OpenSlotCar(object sender, MouseButtonEventArgs e)
    {
        // Skin/role drop-down clicks are editing gestures, not requests to open car details.
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridCell)
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        if (source is not DataGridCell cell || cell.Column != ModelColumn || cell.DataContext is not Slot slot) return;
        e.Handled = true;
        ShowSlotCar(slot);
    }
    private void ShowSlotCar(Slot slot)
    {
        var car = catalog?.FindCar(slot.Model);
        if (car == null) { AppDialog.Notify(this, T("Машина не найдена в установленном контенте: {0}", slot.Model), T("Информация о машине")); return; }
        var dialog = new CarDetailsWindow(car, slot.Skin) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedSkin is { } skin && profile?.Slots.Contains(slot) == true) slot.Skin = skin.Id;
    }
    private async void DeleteServer(object sender, RoutedEventArgs e)
    {
        var selected = sender is MenuItem { Tag: ServerRow row } ? row.Profile : profile;
        if (selected == null || IsTestMode || saving || folderOperation) return;
        try
        {
            var path = ServerRemoval.Validate(ServerRoot.Text, selected.Path, GameRoot.Text);
            async Task EnsureStopped()
            {
                var snapshot = await Task.Run(ServerProcessSnapshot.Read);
                if (snapshot.HasUnreadablePaths || snapshot.ByFolder.ContainsKey(path)
                    || managed.TryGetValue(path, out var running) && !running.Process.HasExited)
                    throw new InvalidOperationException(T("Остановите сервер перед удалением. Если статус недоступен, проверьте процессы вручную."));
            }
            await EnsureStopped();
            var message = T("Переместить сервер «{0}» в корзину Windows?\n\n{1}\n\nБудут удалены его конфиги, контент, плагины и бэкапы. Папка игры останется на месте.", selected.Folder, path);
            if (dirty && profile?.Path == path) message += T("\nНесохранённые изменения этого сервера будут отброшены.");
            if (!AppDialog.Confirm(this, message, T("Удаление сервера"), "В корзину")) return;
            await EnsureStopped();
            path = ServerRemoval.Validate(ServerRoot.Text, path, GameRoot.Text);
            // Recycle only: no fallback to permanent deletion if Windows cannot recycle the folder.
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            if (Directory.Exists(path)) throw new IOException(T("Папка сервера не была удалена."));
            if (managed.Remove(path, out var old)) old.Process.Dispose();
            RemoveServerRow(path);
            await RefreshProcessStatesAsync();
            Status.Text = T("Сервер перемещён в корзину: {0}", selected.Folder);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail(ex); }
    }
    private void RemoveServerRow(string path)
    {
        if (profile?.Path == path)
        {
            dirty = false; profile = null; lastSelectedPath = ""; SlotGrid.ItemsSource = null;
            Scan();
        }
        else
        {
            // Deleting another row through its context menu must preserve the open server's draft.
            loading = true;
            try
            {
                var removed = serverRows.FirstOrDefault(r => r.Profile.Path == path);
                if (removed != null) serverRows.Remove(removed);
                ServerCount.Text = T("Найдено {0}", serverRows.Count);
            }
            finally { loading = false; }
        }
    }
}
