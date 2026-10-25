using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class MainWindow
{
    private string appliedServerRoot = "", appliedGameRoot = "";
    private bool browsingFolder;
    private NotifyCollectionChangedEventHandler? profileSlotsChanged;

    private static string NormalizeFolder(string text)
    {
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        return text.Length == 0 ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
    }
    private static bool SameFolder(string first, string second)
    {
        try { return NormalizeFolder(first).Equals(NormalizeFolder(second), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }
    private string AppliedFolder(TextBox field) => field == ServerRoot ? appliedServerRoot : appliedGameRoot;

    private async void FolderKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox field) return;
        if (e.Key == Key.Escape) { field.Text = AppliedFolder(field); e.Handled = true; return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await CommitFolderAsync(field);
    }
    private async void FolderLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (loading || browsingFolder || sender is not TextBox field) return;
        // A mouse click on Browse opens the chooser first. Keyboard Tab still commits the typed path.
        if (Mouse.LeftButton == MouseButtonState.Pressed &&
            (e.NewFocus == ServerBrowseButton || e.NewFocus == GameBrowseButton || e.NewFocus == EmptyBrowseButton)) return;
        await CommitFolderAsync(field);
    }

    private async Task<bool> CommitFolderAsync(TextBox field)
    {
        if (loading || saving || folderOperation || stoppingForClose || closing) return false;
        var previous = AppliedFolder(field);
        if (SameFolder(field.Text, previous)) { field.Text = previous; return true; }
        string target;
        try
        {
            target = NormalizeFolder(field.Text);
            if (target.Length > 0 && !Directory.Exists(target)) throw new DirectoryNotFoundException();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            var entered = field.Text;
            field.Text = previous;
            Status.Text = T("Папка не найдена или недоступна: {0}", entered);
            return false;
        }
        if (!Discard()) { field.Text = previous; return false; }
        folderOperation = true; SaveBusyOverlay.Visibility = Visibility.Visible;
        try
        {
            Status.Text = field == ServerRoot ? T("Обновляю список серверов…") : T("Читаю установленный контент…");
            if (field == ServerRoot)
            {
                var errors = new List<string>();
                var profiles = await Task.Run(() => target.Length == 0 ? new List<ServerProfile>() : ServerProfile.Discover(target, errors));
                ServerRoot.Text = target;
                ApplyProfiles(profiles, errors);
                Status.Text = profiles.Count > 0 ? T("Найдено {0}", profiles.Count) : T("В выбранной папке серверы не найдены.");
            }
            else
            {
                var refreshed = target.Length == 0 ? null : await Task.Run(() => ContentCatalog.Load(target));
                var selected = profile == null ? null : ServerProfile.Load(profile.Path);
                GameRoot.Text = target;
                AssignCatalog(refreshed);
                appliedGameRoot = target;
                if (selected != null)
                {
                    serverRows.FirstOrDefault(r => r.Profile.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase))?.ReplaceProfile(selected);
                    Load(selected);
                }
                else ClearProfile();
                Status.Text = catalog == null ? T("Укажите папку игры для каталога.")
                    : T("Каталог: {0} машин · {1} вариантов трасс · замечаний к метаданным: {2}", catalog.Cars.Count, catalog.Tracks.Count, catalog.Warnings.Count);
            }
            if (!IsTestMode) StoreSettings();
            return true;
        }
        catch (Exception ex)
        {
            field.Text = previous; Fail(ex); return false;
        }
        finally { folderOperation = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }

    private void AssignCatalog(ContentCatalog? value)
    {
        catalog = value; SkinListConverter.Catalog = value;
        CarList.ItemsSource = value?.Cars; TrackList.ItemsSource = value?.TrackGroups;
        CarSelected(this, null!); TrackSelected(this, null!);
    }

    private void UnsubscribeProfile()
    {
        if (profile == null) return;
        foreach (var slot in profile.Slots) slot.PropertyChanged -= SlotChanged;
        if (profileSlotsChanged != null) profile.Slots.CollectionChanged -= profileSlotsChanged;
        profileSlotsChanged = null;
    }

    private void ClearProfile(IReadOnlyList<string>? errors = null)
    {
        var wasLoading = loading; loading = true;
        try
        {
            UnsubscribeProfile();
            profile = null; lastSelectedPath = ""; dirty = false; draftBaseline = null; lastDiagnostics = null; lastCopies = 0;
            ServerList.SelectedItem = null; SlotGrid.ItemsSource = null; SkinListConverter.Imported = [];
            foreach (var field in new[] { NameInput, TrackInput, LayoutInput, MaxInput, TcpInput, UdpInput, HttpInput, FuelInput, DamageInput, TyresInput }) field.Clear();
            PasswordInput.Clear(); AiInput.IsChecked = false; WeatherInput.IsChecked = false; LobbyInput.IsChecked = false;
            ProfileTitle.SetResourceReference(TextBlock.TextProperty, "L012");
            SlotSummary.Text = ""; SlotsCount.Text = "";
            CurrentTrackName.Text = ""; TrackDetails.Text = ""; TrackPreview.Source = null;
            TrackLayoutPicker.ItemsSource = null; TrackLayoutPicker.Visibility = Visibility.Collapsed;
            TrackLayoutNote.Text = ""; TrackLayoutNote.Visibility = Visibility.Collapsed;
            Log.Clear(); displayedLog = null; displayedLogVersion = -1; Diagnostics.Clear();
            StartButton.IsEnabled = false; StopButton.IsEnabled = false; DeleteServerButton.IsEnabled = false;
            ProcessStatus.Text = ""; ProcessStatus.ToolTip = null;
            Editor.IsEnabled = false; Editor.Visibility = Visibility.Collapsed;
            EmptyServerState.Visibility = Visibility.Visible;
            if (errors?.Count > 0) EmptyServerDetails.Text = string.Join("\n", errors.Take(5));
            else EmptyServerDetails.SetResourceReference(TextBlock.TextProperty, "EmptyServersHint");
        }
        finally { loading = wasLoading; }
    }
}
