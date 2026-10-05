using static ServerManager.Core.UiText;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ServerManager.Core;

namespace ServerManager.App;

public sealed class FileImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => Load(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    public static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 640; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException) { return null; }
    }
}
public sealed class SkinListConverter : IValueConverter
{
    public static ContentCatalog? Catalog { get; set; }
    public static Dictionary<string, List<string>> Imported { get; set; } = [];
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var model = value as string ?? "";
        var skins = Catalog?.FindCar(model)?.Skins.ToList() ?? [];
        foreach (var id in Imported.GetValueOrDefault(model) ?? []) if (!skins.Any(s => s.Id == id)) skins.Add(new(id, id.Length == 0 ? T("По умолчанию") : id, null));
        if (!skins.Any(s => s.Id == "generated")) skins.Add(new("generated", "generated (AI)", null));
        return skins;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class CarNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => SkinListConverter.Catalog?.FindCar(value as string ?? "")?.Name ?? value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public record RoleChoice(string Id, string RussianName) : INotifyPropertyChanged
{
    public string Name => T(RussianName);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshName() => PropertyChanged?.Invoke(this, new(nameof(Name)));
    public override string ToString() => Name;
}
public record UserSettings(string ServersRoot, string GameRoot, string ContentManager, string Language = "en");
public sealed class ManagedServer(Process process)
{
    public Process Process { get; } = process;
    public bool Starting { get; set; } = true;
    public StringBuilder Log { get; } = new();
}

public partial class MainWindow : Window
{
    private ServerProfile? profile;
    private ContentCatalog? catalog;
    private readonly Dictionary<string, ManagedServer> managed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ServerRow> serverRows = [];
    private readonly System.Windows.Threading.DispatcherTimer processTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ServerProcessSnapshot processSnapshot = new(new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase), false);
    private bool refreshingProcesses;
    private bool loading = true, dirty, closing, updatingLayoutPickers, saving, folderOperation;
    private string slotFilter = "all", cmPath = "", lastSelectedPath = "";
    private readonly string settingsFile;
    private readonly string[] args;
    private bool IsTestMode => args.Contains("--ui-qa") || args.Contains("--smoke") || args.Contains("--window-qa") || args.Contains("--wizard-qa") || args.Contains("--wizard-download-qa");
    private Dictionary<string, string>? lastDiagnostics;
    private Dictionary<string, string>? draftBaseline;
    private int lastCopies;
    public MainWindow(string[] args)
    {
        this.args = args;
        settingsFile = IsTestMode ? Path.Combine(AppContext.BaseDirectory, "settings.json") : ManagerSettings.SharedFile;
        RoleChoices = new[] { new RoleChoice("none", "Игрок"), new RoleChoice("fixed", "Трафик"), new RoleChoice("auto", "Игрок / AI") };
        InitializeComponent();
        ServerList.ItemsSource = serverRows;
        ServerRoot.Text = "";
        GameRoot.Text = ContentCatalog.FindGame() ?? "";
        try
        {
            if (ManagerSettings.Load(AppContext.BaseDirectory, settingsFile, IsTestMode) is { } settings)
            { ServerRoot.Text = settings.ServersRoot; GameRoot.Text = settings.GameRoot; cmPath = settings.ContentManager; Localization.Current.Apply(settings.Language); }
        }
        catch (Exception e) { Status.Text = T("Не удалось прочитать настройки приложения: ") + e.Message; }
        if (!Directory.Exists(ServerRoot.Text)) ServerRoot.Text = "";
        if (!Directory.Exists(GameRoot.Text)) GameRoot.Text = ContentCatalog.FindGame() ?? "";
        if (IsTestMode)
        {
            string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            ServerRoot.Text = Option("--servers-root") ?? ServerRoot.Text;
            GameRoot.Text = Option("--game-root") ?? GameRoot.Text;
        }
        Loaded += async (_, _) => { if (!IsTestMode) { await InitializeAsync(); StoreSettings(); } };
        Closing += WindowClosing;
        processTimer.Tick += async (_, _) => await RefreshProcessStatesAsync();
        SourceInitialized += (_, _) => NativeWindow.Attach(this);
        StateChanged += (_, _) =>
        {
            MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M3,0 H12 V9 M0,3 H9 V12 H0 Z" : "M0,0 H10 V10 H0 Z");
            Dispatcher.BeginInvoke(UpdateWindowInsets);
        };
        LocationChanged += (_, _) => { if (WindowState == WindowState.Maximized) Dispatcher.BeginInvoke(UpdateWindowInsets); };
        LanguagePicker.SelectedIndex = UiText.Language == "en" ? 1 : 0;
        UpdateColumnHeaders();
        loading = false;
    }
    private void UpdateWindowInsets()
    {
        var insets = WindowState == WindowState.Maximized ? NativeWindow.MaximizedInsets(this) : new Thickness();
        RootContent.Margin = new Thickness(24 + insets.Left, 24 + insets.Top, 24 + insets.Right, 24 + insets.Bottom);
    }
    private void LanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading || LanguagePicker.SelectedItem is not ComboBoxItem choice) return;
        Localization.Current.Apply(choice.Tag as string);
        UpdateColumnHeaders();
        foreach (var role in RoleChoices) role.RefreshName();
        foreach (var row in ServerList.Items.OfType<ServerRow>()) row.RefreshLabels();
        ServerCount.Text = T("Найдено {0}", ServerList.Items.Count);
        UpdateSlots(); UpdateTrack(); UpdateProcess();
        if (CarList.SelectedItem is CarInfo car) CarMeta.Text = car.Id + T("\n{0} скинов", car.Skins.Count);
        TrackSelected(this, e);
        RenderDiagnostics();
        Status.Text = dirty ? T("Есть несохранённые изменения") : profile != null ? T("Загружен ") + profile.Folder : T("Готов");
        if (!IsTestMode) StoreSettings();
    }
    private void UpdateColumnHeaders()
    {
        // DataGridColumn does not inherit the window's resource updates.
        SlotGrid.Columns[0].Header = T("Слот");
        SlotGrid.Columns[1].Header = T("Модель");
        SlotGrid.Columns[2].Header = T("Скин");
        SlotGrid.Columns[3].Header = T("Назначение");
    }
    public async Task InitializeAsync()
    {
        Editor.IsEnabled = false;
        Status.Text = T("Читаю установленный контент…");
        try
        {
            if (Directory.Exists(GameRoot.Text))
            {
                var path = GameRoot.Text;
                catalog = await Task.Run(() => ContentCatalog.Load(path));
                SkinListConverter.Catalog = catalog;
                CarList.ItemsSource = catalog.Cars; TrackList.ItemsSource = catalog.TrackGroups;
            }
            Scan();
            if (!IsTestMode) { await RefreshProcessStatesAsync(); processTimer.Start(); }
            Status.Text = string.IsNullOrWhiteSpace(ServerRoot.Text) ? T("Выберите папку серверов или создайте первый сервер.")
                : catalog == null ? T("Укажите папку игры для каталога.") : T("Каталог: {0} машин · {1} вариантов трасс · замечаний к метаданным: {2}", catalog.Cars.Count, catalog.Tracks.Count, catalog.Warnings.Count);
        }
        catch (Exception e) { Fail(e); }
    }
    private async void ReloadCatalog(object sender, RoutedEventArgs e)
    {
        try { await RefreshCatalogAsync(); } catch (Exception ex) { Fail(ex); }
    }
    private async Task RefreshCatalogAsync()
    {
        Status.Text = T("Читаю установленный контент…");
        var path = GameRoot.Text;
        var refreshed = await Task.Run(() => ContentCatalog.Load(path));
        var carId = (CarList.SelectedItem as CarInfo)?.Id;
        var skinId = (SkinPicker.SelectedItem as SkinInfo)?.Id;
        var track = CatalogLayoutPicker.SelectedItem as TrackInfo ?? (TrackList.SelectedItem as TrackGroup)?.DefaultLayout;
        var wasLoading = loading; loading = true;
        try
        {
            catalog = refreshed; SkinListConverter.Catalog = refreshed;
            CarSearchChanged(this, null!); TrackSearchChanged(this, null!);
            CarList.SelectedItem = carId == null ? null : catalog.FindCar(carId);
            if (skinId != null) SkinPicker.SelectedItem = (SkinPicker.ItemsSource as IEnumerable<SkinInfo>)?.FirstOrDefault(s => s.Id == skinId);
            TrackList.SelectedItem = track == null ? null : TrackList.Items.OfType<TrackGroup>().FirstOrDefault(g => g.Id.Equals(track.Id, StringComparison.OrdinalIgnoreCase));
            if (track != null && catalog.FindTrack(track.Id, track.Layout) is { } refreshedTrack) ShowCatalogTrack(refreshedTrack);
            SlotGrid.Items.Refresh(); UpdateTrack(); RenderDiagnostics();
        }
        finally { loading = wasLoading; }
        Status.Text = T("Каталог: {0} машин · {1} вариантов трасс · замечаний к метаданным: {2}", catalog.Cars.Count, catalog.Tracks.Count, catalog.Warnings.Count);
    }
    private void Scan()
    {
        var errors = new List<string>();
        var profiles = Directory.Exists(ServerRoot.Text) ? ServerProfile.Discover(ServerRoot.Text, errors) : [];
        loading = true;
        try
        {
            foreach (var obsolete in serverRows.Where(r => !profiles.Any(p => p.Path.Equals(r.Profile.Path, StringComparison.OrdinalIgnoreCase))).ToArray()) serverRows.Remove(obsolete);
            for (var index = 0; index < profiles.Count; index++)
            {
                var found = serverRows.FirstOrDefault(r => r.Profile.Path.Equals(profiles[index].Path, StringComparison.OrdinalIgnoreCase));
                if (found == null) serverRows.Insert(index, new ServerRow(profiles[index]));
                else { found.ReplaceProfile(profiles[index]); if (serverRows.IndexOf(found) != index) serverRows.Move(serverRows.IndexOf(found), index); }
            }
            ApplyProcessStates();
            ServerCount.Text = T("Найдено {0}", profiles.Count);
            var selected = profiles.Find(p => p.Path == lastSelectedPath) ?? profiles.FirstOrDefault();
            if (selected != null)
            {
                ServerList.SelectedItem = ServerList.Items.OfType<ServerRow>().First(r => r.Profile.Path == selected.Path);
                Load(selected);
            }
            else { profile = null; Editor.IsEnabled = false; DeleteServerButton.IsEnabled = false; }
            if (errors.Count > 0) Diagnostics.Text = string.Join("\n", errors);
        }
        finally { loading = false; }
    }
    private void Load(ServerProfile p)
    {
        loading = true;
        try
        {
            profile = p; lastSelectedPath = p.Path; Editor.IsEnabled = true;
            ProfileTitle.Text = p.Folder;
            var map = new Dictionary<TextBox, string> { [NameInput]="NAME", [TrackInput]="TRACK", [LayoutInput]="CONFIG_TRACK", [MaxInput]="MAX_CLIENTS", [TcpInput]="TCP_PORT", [UdpInput]="UDP_PORT", [HttpInput]="HTTP_PORT", [FuelInput]="FUEL_RATE", [DamageInput]="DAMAGE_MULTIPLIER", [TyresInput]="TYRE_WEAR_RATE" };
            foreach (var (box, key) in map) box.Text = ConfigText.Get(p.Ini, "SERVER", key);
            PasswordInput.Password = ConfigText.Get(p.Ini, "SERVER", "PASSWORD");
            AiInput.IsChecked = ConfigText.GetYaml(p.Extra, "EnableAi") == "true";
            WeatherInput.IsChecked = ConfigText.GetYaml(p.Extra, "EnableWeatherFx") == "true";
            LobbyInput.IsChecked = ConfigText.Get(p.Ini, "SERVER", "REGISTER_TO_LOBBY") == "1";
            SkinListConverter.Imported = p.Slots.GroupBy(s => s.Model).ToDictionary(g => g.Key, g => g.Select(s => s.Skin).Distinct().ToList());
            foreach (var slot in p.Slots) slot.PropertyChanged += SlotChanged;
            p.Slots.CollectionChanged += (_, _) => { if (!loading) MarkDirty(); UpdateSlots(); };
            SlotGrid.ItemsSource = p.Slots;
            FilterSlots(slotFilter);
            UpdateSlots(); UpdateTrack(); UpdateProcess();
            Log.Text = managed.TryGetValue(p.Path, out var server) ? server.Log.ToString() : "";
            lastDiagnostics = null; RenderDiagnostics();
            dirty = false; Status.Text = T("Загружен ") + p.Folder;
            try { draftBaseline = Build(); } catch (InvalidOperationException) { draftBaseline = null; }
        }
        finally { loading = false; }
    }
    private void SlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (loading) return;
        MarkDirty(); UpdateSlots();
        if (e.PropertyName == nameof(Slot.Ai)) Dispatcher.BeginInvoke(() => CollectionViewSource.GetDefaultView(SlotGrid.ItemsSource)?.Refresh());
    }
    private void MarkDirty() { if (!loading) { dirty = true; Status.Text = T("Есть несохранённые изменения"); } }
    private void FieldChanged(object sender, RoutedEventArgs e)
    {
        MarkDirty();
        if (!loading && (sender == TrackInput || sender == LayoutInput)) UpdateTrack();
    }
    private bool Discard() => !saving && !folderOperation && (!dirty || AppDialog.Confirm(this, T("Отбросить несохранённые изменения?"), T("Настройки"), "Отбросить"));
    private void Fail(Exception e) { Status.Text = e.Message; AppDialog.Notify(this, e.Message, T("Ошибка")); }
    private void UpdateSlots()
    {
        if (profile == null) return;
        SlotSummary.Text = profile.Summary + T(" · всего {0} слотов", profile.Slots.Count);
        SlotsCount.Text = T("{0} игроков / {1} AI", profile.PlayerSlots, profile.TrafficSlots);
    }
    private void UpdateTrack()
    {
        var track = catalog?.FindTrack(TrackInput.Text, LayoutInput.Text);
        CurrentTrackName.Text = track?.Name ?? TrackInput.Text;
        TrackDetails.Text = track == null ? LayoutInput.Text + T(" · нет в каталоге игры") : track.Layout + (track.PitBoxes != null ? T(" · {0} пит-боксов", track.PitBoxes) : "");
        TrackPreview.Source = FileImageConverter.Load(track?.Preview);
        var layouts = SetLayoutChoices(TrackLayoutPicker, TrackInput.Text, LayoutInput.Text);
        TrackLayoutNote.Text = layouts == 0 ? T("Варианты не найдены в каталоге игры") : layouts == 1 ? T("У этой трассы один установленный вариант") : "";
        TrackLayoutNote.Visibility = TrackLayoutNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private int SetLayoutChoices(ComboBox picker, string trackId, string layout)
    {
        var wasUpdating = updatingLayoutPickers; updatingLayoutPickers = true;
        try
        {
            var layouts = catalog?.Tracks.Where(t => t.Id.Equals(trackId, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Name).ToList() ?? [];
            picker.ItemsSource = layouts;
            picker.SelectedItem = layouts.FirstOrDefault(t => t.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase));
            picker.IsEnabled = layouts.Count > 1 || layouts.Count == 1 && picker.SelectedItem == null;
            picker.Visibility = layouts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            return layouts.Count;
        }
        finally { updatingLayoutPickers = wasUpdating; }
    }
    private void OverviewLayoutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading || updatingLayoutPickers || TrackLayoutPicker.SelectedItem is not TrackInfo track) return;
        if (!LayoutInput.Text.Equals(track.Layout, StringComparison.OrdinalIgnoreCase)) LayoutInput.Text = track.Layout;
    }
    private void ServerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ServerList.SelectedItem is not ServerRow row) return;
        var selected = row.Profile;
        if (!Discard()) { loading = true; ServerList.SelectedItem = ServerList.Items.OfType<ServerRow>().FirstOrDefault(r => r.Profile.Path == profile?.Path); loading = false; return; }
        try { Load(ServerProfile.Load(selected.Path)); } catch (Exception ex) { Fail(ex); }
    }
    private async void CreateServer(object sender, RoutedEventArgs e)
    {
        if (!Discard()) return;
        try
        {
            var runtime = ServerList.Items.OfType<ServerRow>().Select(r => r.Profile.Path).FirstOrDefault(p => File.Exists(Path.Combine(p, "AssettoServer.exe")));
            var wizard = new CreateServerWindow(ServerRoot.Text, GameRoot.Text, runtime, catalog) { Owner = this };
            if (wizard.ShowDialog() != true || wizard.CreatedPath == null) return;
            dirty = false; lastSelectedPath = wizard.CreatedPath;
            ServerRoot.Text = wizard.SelectedServersRoot; GameRoot.Text = wizard.SelectedGamePath;
            await InitializeAsync(); StoreSettings();
            Status.Text = T("Создан сервер {0}. Проверьте настройки и нажмите «Запустить».", Path.GetFileName(wizard.CreatedPath));
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void RefreshServers(object sender, RoutedEventArgs e) { if (Discard()) try { Scan(); } catch (Exception ex) { Fail(ex); } }
    private void ReloadProfile(object sender, RoutedEventArgs e) { if (profile != null && Discard()) try { Load(ServerProfile.Load(profile.Path)); } catch (Exception ex) { Fail(ex); } }
    private async void BrowseServers(object sender, RoutedEventArgs e)
    {
        if (!Discard()) return;
        var dialog = new OpenFolderDialog { Title = T("Папка серверов или один сервер") };
        if (dialog.ShowDialog(this) != true) return;
        ServerRoot.Text = dialog.FolderName; await InitializeAsync(); StoreSettings();
    }
    private async void BrowseGame(object sender, RoutedEventArgs e)
    {
        if (!Discard()) return;
        var dialog = new OpenFolderDialog { Title = T("Корневая папка Assetto Corsa") };
        if (dialog.ShowDialog(this) != true) return;
        GameRoot.Text = dialog.FolderName; await InitializeAsync(); StoreSettings();
    }
    private void StoreSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!); File.WriteAllText(settingsFile, JsonSerializer.Serialize(new UserSettings(ServerRoot.Text, GameRoot.Text, cmPath, UiText.Language), new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Status.Text = T("Настройки приложения не сохранены: ") + e.Message; }
    }
    private Dictionary<string, string> Build()
    {
        if (profile == null) throw new InvalidOperationException(T("Выберите сервер."));
        SlotGrid.CommitEdit(DataGridEditingUnit.Cell, true); SlotGrid.CommitEdit(DataGridEditingUnit.Row, true);
        return profile.Build(new() { ["NAME"]=NameInput.Text, ["TRACK"]=TrackInput.Text, ["CONFIG_TRACK"]=LayoutInput.Text, ["MAX_CLIENTS"]=MaxInput.Text,
            ["TCP_PORT"]=TcpInput.Text, ["UDP_PORT"]=UdpInput.Text, ["HTTP_PORT"]=HttpInput.Text, ["PASSWORD"]=PasswordInput.Password,
            ["FUEL_RATE"]=FuelInput.Text, ["DAMAGE_MULTIPLIER"]=DamageInput.Text, ["TYRE_WEAR_RATE"]=TyresInput.Text, ["REGISTER_TO_LOBBY"]=LobbyInput.IsChecked == true ? "1" : "0" }, AiInput.IsChecked == true, WeatherInput.IsChecked == true);
    }
    private List<string> Warnings(Dictionary<string, string> texts)
    {
        if (catalog == null) return [T("Каталог не загружен: наличие контента в игре не проверено.")];
        return catalog.Validate(profile!, texts);
    }
    private void ValidateProfile(object sender, RoutedEventArgs e)
    {
        try
        {
            lastDiagnostics = Build(); lastCopies = (catalog?.PrepareMissingContent(profile!, lastDiagnostics) ?? []).Count;
            RenderDiagnostics();
            Pages.SelectedIndex = 3; Status.Text = T("Проверка завершена");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void RenderDiagnostics()
    {
        if (lastDiagnostics == null)
        {
            Diagnostics.Text = T("Проверка охватывает конфиги и установленный контент. Доступность из интернета проверяется отдельно.");
            return;
        }
        var warnings = Warnings(lastDiagnostics);
        Diagnostics.Text = warnings.Count == 0 ? T("✓ Проблем по выполненным проверкам не найдено.") : string.Join("\n\n", warnings.Select(w => "• " + w));
        Diagnostics.AppendText(T("\n\nПри сохранении будет подготовлено {0} недостающих файлов.\nПроверка не подтверждает доступность из интернета и совместимость модов с клиентами друзей.", lastCopies));
        if (catalog?.Warnings.Count > 0) Diagnostics.AppendText(T("\n\nЗамечания к метаданным каталога:\n") + string.Join("\n", catalog.Warnings.Take(10)));
    }
    private async void SaveProfile(object sender, RoutedEventArgs e)
    {
        try { await SaveProfileAsync(); }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task SaveProfileAsync()
    {
        if (profile == null || saving) return;
        SlotGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SlotGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var p = profile;
        var draft = dirty ? Build() : null;
        var texts = draft == null || draftBaseline != null && draft.All(pair => draftBaseline.GetValueOrDefault(pair.Key) == pair.Value) ? p.SavedTexts : draft;
        var warnings = Warnings(texts); var copies = catalog?.PrepareMissingContent(p, texts) ?? [];
        saving = true;
        SaveBusyOverlay.Visibility = Visibility.Visible;
        Status.Text = T("Сохраняю конфиги и готовлю серверные данные…");
        try
        {
            var backup = await Task.Run(() => p.Save(texts, copies));
            if (backup != null) p.AcceptSaved(ServerProfile.Load(p.Path));
            dirty = false;
            draftBaseline = draft ?? draftBaseline;
            foreach (var row in ServerList.Items.OfType<ServerRow>().Where(r => r.Profile.Path == p.Path)) row.RefreshProfile();
            lastDiagnostics = texts; lastCopies = copies.Count; RenderDiagnostics();
            Status.Text = backup == null ? T("Изменений нет — новый бэкап не нужен.") : T("Сохранено · подготовлено {0} файлов · бэкап: {1} · для применения нужен перезапуск сервера", copies.Count, backup);
            if (warnings.Count > 0) Status.Text += T(" · замечаний: {0} — вкладка «Проверка»", warnings.Count);
        }
        finally { saving = false; SaveBusyOverlay.Visibility = Visibility.Collapsed; }
    }
    private void FitSlots(object sender, RoutedEventArgs e) { if (profile != null) MaxInput.Text = profile.Slots.Count.ToString(); }
    private void GoCars(object sender, RoutedEventArgs e) { Pages.SelectedIndex = 2; CatalogPages.SelectedIndex = 0; }
    private void GoTracks(object sender, RoutedEventArgs e) { Pages.SelectedIndex = 2; CatalogPages.SelectedIndex = 1; }
    private void FilterAll(object sender, RoutedEventArgs e) => FilterSlots("all");
    private void FilterPlayers(object sender, RoutedEventArgs e) => FilterSlots("players");
    private void FilterTraffic(object sender, RoutedEventArgs e) => FilterSlots("traffic");
    private void FilterSlots(string filter)
    {
        slotFilter = filter;
        if (SlotGrid.ItemsSource == null) return;
        SlotGrid.CommitEdit(DataGridEditingUnit.Cell, true); SlotGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CollectionViewSource.GetDefaultView(SlotGrid.ItemsSource).Filter = s => s is Slot slot && (filter == "all" || (filter == "traffic" ? slot.Ai == "fixed" : slot.Ai != "fixed"));
        UpdateMoveButtons();
    }
    public IReadOnlyList<RoleChoice> RoleChoices { get; private set; } = [];
    private void SlotSelected(object sender, SelectionChangedEventArgs e) => UpdateMoveButtons();
    private void UpdateMoveButtons()
    {
        if (MoveUpButton == null || MoveDownButton == null) return;
        var visible = SlotGrid.Items.Cast<Slot>().ToList();
        var index = SlotGrid.SelectedItem is Slot slot ? visible.IndexOf(slot) : -1;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < visible.Count - 1;
    }
    private void MoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int direction)
    {
        if (profile == null || SlotGrid.SelectedItem is not Slot selected) return;
        SlotGrid.CommitEdit(DataGridEditingUnit.Cell, true); SlotGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var visible = SlotGrid.Items.Cast<Slot>().ToList();
        var position = visible.IndexOf(selected); var next = position + direction;
        if (position < 0 || next < 0 || next >= visible.Count) return;
        profile.Slots.Move(profile.Slots.IndexOf(selected), profile.Slots.IndexOf(visible[next]));
        SlotGrid.SelectedItem = selected; SlotGrid.ScrollIntoView(selected);
        UpdateMoveButtons(); Status.Text = T("Порядок слотов изменён. Сохраните изменения.");
    }
    private void DuplicateSlot(object sender, RoutedEventArgs e)
    {
        if (profile == null || SlotGrid.SelectedItem is not Slot slot) return;
        if (profile.Slots.Count >= 255) { Fail(new InvalidOperationException(T("Достигнут предел 255 слотов."))); return; }
        var copy = slot.Duplicate(); copy.PropertyChanged += SlotChanged; profile.Slots.Insert(profile.Slots.IndexOf(slot) + 1, copy); FitSlots(sender, e);
    }
    private void DeleteSlot(object sender, RoutedEventArgs e)
    {
        if (profile == null || SlotGrid.SelectedItem is not Slot slot) return;
        if (profile.Slots.Count == 1) { Fail(new InvalidOperationException(T("Оставьте хотя бы один слот."))); return; }
        profile.Slots.Remove(slot); FitSlots(sender, e);
    }
    private void CarSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (catalog == null) return;
        var q = CarSearch.Text.Trim(); CarList.ItemsSource = catalog.Cars.Where(c => (c.Name + " " + c.Brand + " " + c.Id).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    private void TrackSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (catalog == null) return;
        var selectedId = (TrackList.SelectedItem as TrackGroup)?.Id;
        var selectedLayout = (CatalogLayoutPicker.SelectedItem as TrackInfo)?.Layout;
        var q = TrackSearch.Text.Trim(); TrackList.ItemsSource = catalog.TrackGroups.Where(t => (t.Name + " " + t.Id).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selectedId != null)
        {
            TrackList.SelectedItem = TrackList.Items.OfType<TrackGroup>().FirstOrDefault(g => g.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (TrackList.SelectedItem != null && catalog.FindTrack(selectedId, selectedLayout ?? "") is { } layout) ShowCatalogTrack(layout);
        }
    }
    private void CarSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CarList.SelectedItem is not CarInfo car) return;
        CarTitle.Text = car.Name; CarMeta.Text = car.Id + T("\n{0} скинов", car.Skins.Count);
        CarPreview.Source = FileImageConverter.Load(car.Preview); SkinPicker.ItemsSource = car.Skins; SkinPicker.SelectedIndex = 0;
    }
    private void SkinSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SkinPicker.SelectedItem is SkinInfo skin) CarPreview.Source = FileImageConverter.Load(skin.Preview);
    }
    private void AddSlot(object sender, RoutedEventArgs e)
    {
        if (profile == null || CarList.SelectedItem is not CarInfo car) return;
        if (profile.Slots.Count >= 255) { Fail(new InvalidOperationException(T("Достигнут предел 255 слотов."))); return; }
        var slot = new Slot { Model = car.Id, Skin = (SkinPicker.SelectedItem as SkinInfo)?.Id ?? car.DefaultSkin, Ai = (AddRole.SelectedItem as ComboBoxItem)?.Tag as string ?? "none" };
        slot.PropertyChanged += SlotChanged; profile.Slots.Add(slot);
        if (slot.Ai != "none") AiInput.IsChecked = true;
        MaxInput.Text = profile.Slots.Count.ToString(); FilterSlots("all"); SlotGrid.SelectedItem = slot;
        Status.Text = T("Добавлен слот: ") + car.Name + T(". Сохраните изменения.");
    }
    private void ReplaceSlot(object sender, RoutedEventArgs e)
    {
        if (SlotGrid.SelectedItem is not Slot slot || CarList.SelectedItem is not CarInfo car) { Fail(new InvalidOperationException(T("Выберите слот на вкладке машин, затем модель в каталоге."))); return; }
        slot.Model = car.Id; slot.Skin = (SkinPicker.SelectedItem as SkinInfo)?.Id ?? car.DefaultSkin;
        SlotGrid.Items.Refresh(); Status.Text = T("Модель слота заменена. Сохраните изменения.");
    }
    private void TrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TrackList.SelectedItem is not TrackGroup group)
        {
            CatalogTrackPreview.Source = null; CatalogTrackTitle.Text = T("Выберите трассу"); CatalogTrackDetails.Text = "";
            SetLayoutChoices(CatalogLayoutPicker, "", ""); UseTrackButton.IsEnabled = false;
            return;
        }
        var track = CatalogLayoutPicker.SelectedItem as TrackInfo;
        if (track == null || !track.Id.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
            track = group.Layouts.FirstOrDefault(t => t.Id.Equals(TrackInput.Text, StringComparison.OrdinalIgnoreCase) && t.Layout.Equals(LayoutInput.Text, StringComparison.OrdinalIgnoreCase)) ?? group.DefaultLayout;
        ShowCatalogTrack(track); UseTrackButton.IsEnabled = true;
    }
    private void ShowCatalogTrack(TrackInfo track)
    {
        SetLayoutChoices(CatalogLayoutPicker, track.Id, track.Layout);
        RenderCatalogTrack(track);
    }
    private void CatalogLayoutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (updatingLayoutPickers || CatalogLayoutPicker.SelectedItem is not TrackInfo track) return;
        RenderCatalogTrack(track);
    }
    private void RenderCatalogTrack(TrackInfo track)
    {
        CatalogTrackPreview.Source = FileImageConverter.Load(track.Preview); CatalogTrackTitle.Text = track.Name;
        CatalogTrackDetails.Text = track.Id + "\n" + track.Layout + (track.PitBoxes != null ? T("\n{0} пит-боксов", track.PitBoxes) : "");
    }
    private void UseTrack(object sender, RoutedEventArgs e)
    {
        if (TrackList.SelectedItem is not TrackGroup || CatalogLayoutPicker.SelectedItem is not TrackInfo track) return;
        TrackInput.Text = track.Id; LayoutInput.Text = track.Layout; UpdateTrack(); Pages.SelectedIndex = 0;
    }
    private void UpdateProcess()
    {
        ApplyProcessStates();
        if (profile == null) return;
        var ours = managed.TryGetValue(profile.Path, out var server) && !server.Process.HasExited;
        var row = ServerList.Items.OfType<ServerRow>().FirstOrDefault(r => r.Profile.Path.Equals(profile.Path, StringComparison.OrdinalIgnoreCase));
        StartButton.IsEnabled = !ours && row?.IsRunning != true; StopButton.IsEnabled = ours;
        DeleteServerButton.IsEnabled = !ours && row?.State == ServerRunState.Stopped;
        ProcessStatus.Text = row?.StatusLabel ?? T("Проверяю статус…");
        ProcessStatus.ToolTip = row?.StatusHint;
        ProcessStatus.Foreground = (Brush)new BrushConverter().ConvertFromString(row?.StatusColor ?? "#747D8D")!;
    }
    private async Task RefreshProcessStatesAsync()
    {
        if (refreshingProcesses || closing) return;
        refreshingProcesses = true;
        try { var snapshot = await Task.Run(ServerProcessSnapshot.Read); if (!closing) { processSnapshot = snapshot; UpdateProcess(); } }
        catch (Win32Exception) { if (!closing) { processSnapshot = new(new Dictionary<string, int[]>(), true); UpdateProcess(); } }
        finally { refreshingProcesses = false; }
    }
    private void ApplyProcessStates()
    {
        foreach (var row in ServerList.Items.OfType<ServerRow>())
        {
            if (managed.TryGetValue(row.Profile.Path, out var server) && !server.Process.HasExited)
                row.SetStatus(server.Starting ? ServerRunState.Starting : ServerRunState.Managed, [server.Process.Id]);
            else if (processSnapshot.ByFolder.TryGetValue(row.Profile.Path, out var ids)) row.SetStatus(ServerRunState.External, ids);
            else row.SetStatus(processSnapshot.HasUnreadablePaths ? ServerRunState.Unknown : ServerRunState.Stopped, []);
        }
    }
    private async void StartServer(object sender, RoutedEventArgs e)
    {
        try
        {
            if (profile == null) return;
            if (dirty) throw new InvalidOperationException(T("Сначала сохраните изменения или перечитайте конфигурацию."));
            var path = profile.Path; var current = ServerProfile.Load(path);
            var tcp = new[] { int.Parse(ConfigText.Get(current.Ini, "SERVER", "TCP_PORT")), int.Parse(ConfigText.Get(current.Ini, "SERVER", "HTTP_PORT")) };
            var udp = int.Parse(ConfigText.Get(current.Ini, "SERVER", "UDP_PORT"));
            var network = IPGlobalProperties.GetIPGlobalProperties();
            if (network.GetActiveTcpListeners().Any(l => tcp.Contains(l.Port)) || network.GetActiveUdpListeners().Any(l => l.Port == udp)) throw new InvalidOperationException(T("Порт сервера уже занят. Возможно, работает старый менеджер или другая установка."));
            var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(path, "AssettoServer.exe")) { WorkingDirectory = path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
            process.Start(); var item = new ManagedServer(process); managed[path] = item;
            Log.Clear(); UpdateProcess(); Status.Text = T("Процесс запущен; ожидаю ответа HTTP…");
            _ = Pump(path, item, process.StandardOutput); _ = Pump(path, item, process.StandardError); _ = WaitExit(path, item);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (var i = 0; i < 10 && !process.HasExited; i++)
            {
                await Task.Delay(1000);
                try { using var response = await client.GetAsync($"http://127.0.0.1:{tcp[1]}/INFO"); if (!response.IsSuccessStatusCode) continue; item.Starting = false; UpdateProcess(); if (profile?.Path == path) Status.Text = T("Сервер готов локально. Нажмите «Подключиться»."); return; }
                catch (HttpRequestException) { } catch (TaskCanceledException) { }
            }
            item.Starting = false; UpdateProcess();
            if (profile?.Path == path && !process.HasExited) Status.Text = T("Процесс работает, но HTTP пока не ответил. Проверьте журнал.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task Pump(string path, ManagedServer server, StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var safe = Regex.Replace(line, @"(?i)((?:password|token|secret|api[_-]?key)\s*[:=]\s*)\S+", T("$1[скрыто]"));
                await Dispatcher.InvokeAsync(() =>
                {
                    server.Log.AppendLine(safe);
                    if (server.Log.Length > 100000) server.Log.Remove(0, server.Log.Length - 70000);
                    if (!closing && profile?.Path == path) { Log.Text = server.Log.ToString(); Log.ScrollToEnd(); }
                });
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or TaskCanceledException) { }
    }
    private async Task WaitExit(string path, ManagedServer server)
    {
        await server.Process.WaitForExitAsync();
        if (closing) return;
        await RefreshProcessStatesAsync();
        await Dispatcher.InvokeAsync(() => { server.Log.AppendLine(T("Процесс завершён, код ") + server.Process.ExitCode); if (profile?.Path == path) { UpdateProcess(); Log.Text = server.Log.ToString(); Status.Text = T("Сервер остановлен"); } });
    }
    private void StopServer(object sender, RoutedEventArgs e)
    {
        if (profile == null || !managed.TryGetValue(profile.Path, out var server)) return;
        try { if (!server.Process.HasExited) server.Process.Kill(); } catch (Exception ex) { Fail(ex); }
    }
    private async void ConnectLocal(object sender, RoutedEventArgs e)
    {
        try
        {
            if (profile == null) return;
            var saved = ServerProfile.Load(profile.Path); var port = ConfigText.Get(saved.Ini, "SERVER", "HTTP_PORT");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var info = await client.GetStringAsync($"http://127.0.0.1:{port}/INFO");
            using var json = JsonDocument.Parse(info);
            var actualTrack = json.RootElement.GetProperty("track").GetString() ?? "";
            var expectedTrack = saved.Track + (saved.Layout.Length > 0 ? "-" + saved.Layout : "");
            if (actualTrack != expectedTrack) throw new InvalidOperationException(T("На этом порту отвечает другая трасса. Выберите соответствующий сервер."));
            if (LaunchContentManager($"acmanager://race/online/join?ip=127.0.0.1&httpPort={port}")) Status.Text = T("Локальный сервер открыт в Content Manager");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void OpenContentManager(object sender, RoutedEventArgs e)
    {
        try { if (LaunchContentManager()) Status.Text = T("Content Manager открыт"); }
        catch (Exception ex) { Fail(ex); }
    }
    private bool LaunchContentManager(string? connection = null)
    {
        cmPath = FindContentManager() ?? "";
        if (!File.Exists(cmPath))
        {
            var chooser = new OpenFileDialog { Title = T("Выберите Content Manager.exe"), Filter = "Content Manager|*.exe" };
            if (chooser.ShowDialog(this) != true) return false;
            cmPath = chooser.FileName;
        }
        var start = new ProcessStartInfo(cmPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(cmPath)! };
        if (connection != null) start.ArgumentList.Add(connection);
        Process.Start(start); StoreSettings();
        return true;
    }
    private string? FindContentManager()
    {
        var runningPaths = new List<string>();
        foreach (var running in Process.GetProcessesByName("Content Manager").Concat(Process.GetProcessesByName("ContentManager")))
        {
            using (running)
                try { if (running.MainModule?.FileName is { } path) runningPaths.Add(path); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException) { }
        }
        var commands = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.ClassesRoot })
        {
            try
            {
                using var key = hive.OpenSubKey(hive == Registry.CurrentUser ? @"Software\Classes\acmanager\shell\open\command" : @"acmanager\shell\open\command");
                if (key?.GetValue("") is string command) commands.Add(command);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return ContentManagerLocator.Find(cmPath, runningPaths, commands, [AppContext.BaseDirectory, GameRoot.Text,
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)]);
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (IsTestMode) { closing = true; processTimer.Stop(); return; }
        if (!Discard()) { e.Cancel = true; return; }
        var running = managed.Values.Where(s => !s.Process.HasExited).ToList();
        if (running.Count > 0 && !AppDialog.Confirm(this, T("Закрытие остановит серверы, запущенные этим менеджером. Закрыть?"), T("Работающие серверы"), "Закрыть")) { e.Cancel = true; return; }
        closing = true;
        processTimer.Stop();
        foreach (var server in running) server.Process.Kill();
        if (!IsTestMode) StoreSettings();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void ToggleMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    public async Task SmokeTest(string destination)
    {
        LanguagePicker.SelectedIndex = 0;
        await InitializeAsync();
        if (profile == null || catalog == null) throw new InvalidOperationException("Smoke test: не загружены профили или каталог.");
        foreach (var p in ServerList.Items.OfType<ServerRow>().Select(r => r.Profile))
        {
            Load(ServerProfile.Load(p.Path));
            var originalSkins = profile!.Slots.Select(s => s.Skin).ToArray();
            _ = Build();
            if (!originalSkins.SequenceEqual(profile.Slots.Select(s => s.Skin))) throw new InvalidOperationException("UI binding changed imported skins.");
        }
        loading = true; ServerList.SelectedIndex = 4; loading = false;
        var selected = (ServerList.SelectedItem as ServerRow)?.Profile ?? throw new InvalidOperationException("No selected profile.");
        Load(ServerProfile.Load(selected.Path));
        if ((string)RefreshCatalogButton.Content != T("Обновить каталог")) throw new InvalidOperationException("Catalog refresh caption was replaced by a formatted warning.");
        if (TrackList.Items.OfType<TrackGroup>().Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != TrackList.Items.Count
            || TrackList.Items.Count != catalog.TrackGroups.Count || TrackList.Items.Count >= catalog.Tracks.Count)
            throw new InvalidOperationException("Catalog must show each track once, independently of its layouts.");
        TrackSearch.Text = "highforce";
        if (TrackList.Items.Count != 1 || TrackList.Items[0] is not TrackGroup highForce || highForce.Layouts.Count < 2)
            throw new InvalidOperationException("Track search returned layouts instead of a single track.");
        TrackList.SelectedIndex = 0;
        if (CatalogLayoutPicker.Items.Count != highForce.Layouts.Count || CatalogTrackPreview.Source == null || !UseTrackButton.IsEnabled)
            throw new InvalidOperationException("Selected track did not populate its layout card.");
        TrackSearch.Text = "no-match-qa";
        if (CatalogLayoutPicker.SelectedItem != null || UseTrackButton.IsEnabled) throw new InvalidOperationException("Search retained a stale track card.");
        TrackSearch.Text = "";
        var selectedRow = ServerList.SelectedItem as ServerRow ?? throw new InvalidOperationException("No server status row.");
        var oldSnapshot = processSnapshot;
        processSnapshot = new(new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase) { [selected.Path.ToUpperInvariant()] = [12345] }, false);
        UpdateProcess();
        if (selectedRow.State != ServerRunState.External || StartButton.IsEnabled || StopButton.IsEnabled || DeleteServerButton.IsEnabled
            || ServerList.Items.OfType<ServerRow>().Count(r => r.IsRunning) != 1) throw new InvalidOperationException("External process was assigned to an incorrect profile or exposed management controls.");
        using (var fakeManagedProcess = Process.GetCurrentProcess())
        {
            var fakeManaged = new ManagedServer(fakeManagedProcess); managed[selected.Path] = fakeManaged;
            UpdateProcess();
            if (selectedRow.State != ServerRunState.Starting || !StopButton.IsEnabled || DeleteServerButton.IsEnabled) throw new InvalidOperationException("Managed starting status failed.");
            fakeManaged.Starting = false; UpdateProcess();
            if (selectedRow.State != ServerRunState.Managed || DeleteServerButton.IsEnabled) throw new InvalidOperationException("Managed running status failed.");
            managed.Remove(selected.Path);
        }
        processSnapshot = new(new Dictionary<string, int[]>(), true); UpdateProcess();
        if (selectedRow.State != ServerRunState.Unknown || DeleteServerButton.IsEnabled) throw new InvalidOperationException("Unreadable process paths were reported as stopped.");
        processSnapshot = new(new Dictionary<string, int[]>(), false); UpdateProcess();
        if (selectedRow.State != ServerRunState.Stopped || !StartButton.IsEnabled || !DeleteServerButton.IsEnabled) throw new InvalidOperationException("Process exit did not update the server status.");
        LanguagePicker.SelectedIndex = 1;
        if (selectedRow.StatusLabel != "Stopped") throw new InvalidOperationException("Runtime status did not switch language.");
        LanguagePicker.SelectedIndex = 0; processSnapshot = oldSnapshot; UpdateProcess();
        var originalLayout = catalog.FindTrack(TrackInput.Text, LayoutInput.Text);
        var alternativeLayout = catalog.Tracks.FirstOrDefault(t => t.Id == TrackInput.Text && t.Layout != LayoutInput.Text);
        if (originalLayout == null || alternativeLayout == null) throw new InvalidOperationException("Layout QA requires an installed track with multiple layouts.");
        TrackLayoutPicker.SelectedItem = alternativeLayout;
        var layoutDraft = Build();
        if (!dirty || ConfigText.Get(layoutDraft["server_cfg.ini"], "SERVER", "CONFIG_TRACK") != alternativeLayout.Layout || TrackInput.Text != originalLayout.Id || CurrentTrackName.Text != alternativeLayout.Name)
            throw new InvalidOperationException("Overview layout selection did not update the draft and preview.");
        LanguagePicker.SelectedIndex = 1;
        if (!layoutDraft.All(kv => Build()[kv.Key] == kv.Value) || (TrackLayoutPicker.SelectedItem as TrackInfo)?.Layout != alternativeLayout.Layout)
            throw new InvalidOperationException("Language switch lost the selected track layout.");
        LanguagePicker.SelectedIndex = 0;
        Load(ServerProfile.Load(selected.Path));
        TrackList.SelectedItem = TrackList.Items.OfType<TrackGroup>().First(g => g.Id == originalLayout.Id);
        CatalogLayoutPicker.SelectedItem = alternativeLayout;
        UseTrack(this, new RoutedEventArgs());
        if (LayoutInput.Text != alternativeLayout.Layout || (TrackLayoutPicker.SelectedItem as TrackInfo)?.Layout != alternativeLayout.Layout)
            throw new InvalidOperationException("Catalog applied the list layout instead of the chosen layout.");
        Load(ServerProfile.Load(selected.Path));
        var car = catalog.Cars.FirstOrDefault(c => c.Id == "drks_nissan_300zx_abflug") ?? catalog.Cars.First();
        CarList.SelectedItem = car; TrackList.SelectedItem = TrackList.Items.OfType<TrackGroup>().FirstOrDefault(t => t.Id == profile!.Track);
        var before = profile!.Slots.Count;
        AddSlot(this, new RoutedEventArgs());
        if (profile.Slots.Count != before + 1 || profile.Slots.Last().Model != car.Id) throw new InvalidOperationException("UI add slot failed.");
        DuplicateSlot(this, new RoutedEventArgs());
        if (profile.Slots.Count != before + 2) throw new InvalidOperationException("UI duplicate slot failed.");
        DeleteSlot(this, new RoutedEventArgs());
        if (profile.Slots.Count != before + 1) throw new InvalidOperationException("UI delete slot failed.");
        SlotGrid.SelectedItem = profile.Slots.First();
        ReplaceSlot(this, new RoutedEventArgs());
        if (profile.Slots.First().Model != car.Id || profile.Slots.First().Skin != (SkinPicker.SelectedItem as SkinInfo)?.Id) throw new InvalidOperationException("UI replace slot failed.");
        _ = Build();
        FilterSlots("traffic");
        if (SlotGrid.Items.Count != profile.TrafficSlots) throw new InvalidOperationException("UI AI filter failed.");
        FilterSlots("all");
        var moved = profile.Slots[0]; var nextSlot = profile.Slots[1];
        SlotGrid.SelectedItem = moved; MoveSelected(1);
        var movedText = Build()["entry_list.ini"];
        if (profile.Slots[1] != moved || moved.EntrySection != "CAR_1" || ConfigText.Get(movedText, "CAR_1", "MODEL") != moved.Model || !dirty || SlotGrid.SelectedItem != moved)
            throw new InvalidOperationException("Moving slot did not preserve selection, numbering or serialized order.");
        MoveSelected(-1);
        if (profile.Slots[0] != moved || profile.Slots[1] != nextSlot) throw new InvalidOperationException("Moving slot back failed.");
        Load(ServerProfile.Load(selected!.Path));
        NameInput.Text += " · draft";
        var draft = Build();
        var chosenSkin = SkinPicker.SelectedItem;
        var chosenSlot = SlotGrid.SelectedItem;
        var slotValues = profile!.Slots.Select(s => (s.Model, s.Skin, s.Ai)).ToArray();
        LanguagePicker.SelectedIndex = 1;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var englishDraft = Build();
        if (!dirty || !draft.All(kv => englishDraft[kv.Key] == kv.Value) || !slotValues.SequenceEqual(profile.Slots.Select(s => (s.Model, s.Skin, s.Ai))) || SkinPicker.SelectedItem != chosenSkin || SlotGrid.SelectedItem != chosenSlot)
            throw new InvalidOperationException("Language switch changed unsaved settings, skins, roles or selections.");
        if ((string)((TabItem)Pages.Items[0]).Header != "Overview" || (string)RoleColumn.Header != "Role" || RoleChoices[0].Name != "Player" || !SlotSummary.Text.Contains("players"))
            throw new InvalidOperationException($"Live English labels did not update: tab={((TabItem)Pages.Items[0]).Header}; column={RoleColumn.Header}; role={RoleChoices[0].Name}; summary={SlotSummary.Text}.");
        var legacySettings = JsonSerializer.Deserialize<UserSettings>("{\"ServersRoot\":\"servers\",\"GameRoot\":\"game\",\"ContentManager\":\"cm\"}");
        var savedSettings = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(new UserSettings("servers", "game", "cm", "en")));
        if (legacySettings?.Language != "en" || savedSettings?.Language != "en") throw new InvalidOperationException("Language preference serialization failed.");
        LanguagePicker.SelectedIndex = 0;
        if (!dirty || (string)((TabItem)Pages.Items[0]).Header != "Обзор" || !draft.All(kv => Build()[kv.Key] == kv.Value))
            throw new InvalidOperationException("Switch back to Russian changed the draft.");
        Load(ServerProfile.Load(selected.Path));
        Pages.SelectedIndex = 2;
        Width = 1360; Height = 920;
        var visual = (FrameworkElement)Content;
        visual.Measure(new Size(Width, Height)); visual.Arrange(new Rect(0, 0, Width, Height)); visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(destination);
        using (var stream = File.Create(Path.Combine(destination, "desktop-preview.png"))) encoder.Save(stream);
        var catalogScroll = VisualChildren<System.Windows.Controls.Primitives.ScrollBar>(CarList).FirstOrDefault(b => b.Orientation == Orientation.Vertical && b.Maximum > 0);
        if (catalogScroll == null || VisualChildren<System.Windows.Controls.Primitives.Thumb>(catalogScroll).FirstOrDefault()?.ActualHeight < 35)
            throw new InvalidOperationException("Large catalog has an undersized scrollbar thumb.");
        async Task Snapshot(string name, int page)
        {
            Pages.SelectedIndex = page;
            visual.Measure(new Size(Width, Height)); visual.Arrange(new Rect(0, 0, Width, Height)); visual.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            visual.Measure(new Size(Width, Height)); visual.Arrange(new Rect(0, 0, Width, Height)); visual.UpdateLayout();
            var image = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(destination, name)); png.Save(file);
        }
        await Snapshot("overview-preview.png", 0);
        if (TrackLayoutPicker.SelectedItem is TrackInfo visibleLayout && !VisualChildren<TextBlock>(TrackLayoutPicker).Any(t => t.Text == visibleLayout.Name))
            throw new InvalidOperationException("Layout picker must display the layout name.");
        await Snapshot("slots-preview.png", 1);
        LanguagePicker.SelectedIndex = 1;
        await Snapshot("english-overview-preview.png", 0); await Snapshot("english-slots-preview.png", 1); await Snapshot("english-catalog-preview.png", 2);
        var tooltip = new ToolTip { Content = CarSearch.ToolTip, Style = (Style)FindResource(typeof(ToolTip)) };
        tooltip.Measure(new Size(420, double.PositiveInfinity));
        tooltip.Arrange(new Rect(new Point(), tooltip.DesiredSize)); tooltip.UpdateLayout();
        var tipImage = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth), (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32); tipImage.Render(tooltip);
        var tipPng = new PngBitmapEncoder(); tipPng.Frames.Add(BitmapFrame.Create(tipImage));
        using (var file = File.Create(Path.Combine(destination, "tooltip-preview.png"))) tipPng.Save(file);
        LanguagePicker.SelectedIndex = 0;
        File.WriteAllText(Path.Combine(destination, "smoke-result.txt"), $"PASS: {ServerList.Items.Count} profiles; {catalog.Cars.Count} cars; {catalog.Tracks.Count} layouts; UI binding, add/duplicate/delete/replace, AI filter, live RU/EN switch, draft preservation and language preference serialization; no server files written.");
        var dialog = new AppDialog(T("Отбросить несохранённые изменения?"), T("Настройки"), "Отбросить", true);
        if (!dialog.CancelButton.IsDefault || dialog.AcceptButton.IsDefault) throw new InvalidOperationException("Destructive dialog must default to Cancel.");
        var dialogVisual = (FrameworkElement)dialog.Content;
        dialogVisual.Measure(new Size(540, double.PositiveInfinity));
        dialogVisual.Arrange(new Rect(0, 0, 540, dialogVisual.DesiredSize.Height)); dialogVisual.UpdateLayout();
        var dialogBitmap = new RenderTargetBitmap(540, (int)Math.Ceiling(dialogVisual.ActualHeight), 96, 96, PixelFormats.Pbgra32); dialogBitmap.Render(dialogVisual);
        var dialogPng = new PngBitmapEncoder(); dialogPng.Frames.Add(BitmapFrame.Create(dialogBitmap));
        using (var file = File.Create(Path.Combine(destination, "dialog-preview.png"))) dialogPng.Save(file);
        Pages.SelectedIndex = 1; SlotGrid.SelectedItem = profile.Slots.FirstOrDefault(s => s.Ai == "none") ?? profile.Slots.First();
        SlotGrid.ScrollIntoView(SlotGrid.SelectedItem);
        await Snapshot("selected-slots-preview.png", 1);
        CatalogPages.SelectedIndex = 1;
        TrackList.SelectedItem = TrackList.Items.OfType<TrackGroup>().First(g => g.Id == "highforce");
        await Snapshot("tracks-preview.png", 2);
        LanguagePicker.SelectedIndex = 1;
        await Snapshot("english-tracks-preview.png", 2);
        LanguagePicker.SelectedIndex = 0;
        CatalogPages.SelectedIndex = 0;
        var unchanged = Build(); await RefreshCatalogAsync();
        if (!unchanged.All(kv => Build()[kv.Key] == kv.Value)) throw new InvalidOperationException("Refreshing catalog changed server settings.");
        File.AppendAllText(Path.Combine(destination, "smoke-result.txt"), "\nPASS: move up/down, CAR_n numbering, themed dialog default and catalog refresh preserve config.");
        File.AppendAllText(Path.Combine(destination, "smoke-result.txt"), "\nPASS: unique translation IDs, catalog refresh caption, overview/catalog layout choices, layout draft across language switch and usable catalog scrollbar thumb.");
        File.AppendAllText(Path.Combine(destination, "smoke-result.txt"), "\nPASS: one result per track, search/card layout selection, cleared stale card, external/managed/starting/stopped/unknown statuses, exact folder matching and bilingual runtime labels.");
    }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
    public async Task WindowGeometryTest(string destination)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var work = NativeWindow.WorkArea(hwnd);
        SystemCommands.MaximizeWindow(this);
        await Task.Delay(450);
        NativeWindow.GetWindowRect(hwnd, out var maximized);
        var start = RootContent.PointToScreen(new Point());
        var end = RootContent.PointToScreen(new Point(RootContent.ActualWidth, RootContent.ActualHeight));
        if (WindowState != WindowState.Maximized || start.X < work.Left + 23 || start.Y < work.Top + 23 || end.X > work.Right - 23 || end.Y > work.Bottom - 23)
            throw new InvalidOperationException($"Maximized content exceeds work area: [{start},{end}] vs [{work.Left},{work.Top},{work.Right},{work.Bottom}].");
        Pages.SelectedIndex = 2; CatalogPages.SelectedIndex = 0;
        await Task.Delay(100);
        RootContent.UpdateLayout();
        var memoStart = CatalogMemo.PointToScreen(new Point());
        var listStart = CarList.PointToScreen(new Point());
        if (Math.Abs(memoStart.X - listStart.X) > 3 || CatalogMemo.ActualWidth < CarList.ActualWidth)
            throw new InvalidOperationException("Catalog memo is not aligned below the full catalog.");
        var maxWidth = Math.Ceiling(RootContent.ActualWidth);
        var maxHeight = Math.Ceiling(RootContent.ActualHeight);
        var capture = new DrawingVisual();
        using (var drawing = capture.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(RootContent), null, new Rect(0, 0, maxWidth, maxHeight));
        var maxImage = new RenderTargetBitmap((int)maxWidth, (int)maxHeight, 96, 96, PixelFormats.Pbgra32); maxImage.Render(capture);
        var maxPng = new PngBitmapEncoder(); maxPng.Frames.Add(BitmapFrame.Create(maxImage));
        using (var file = File.Create(Path.Combine(destination, "maximized-catalog-preview.png"))) maxPng.Save(file);
        SystemCommands.RestoreWindow(this); await Task.Delay(450);
        if (WindowState != WindowState.Normal) throw new InvalidOperationException("Native restore failed.");
        File.WriteAllText(Path.Combine(destination, "window-result.txt"), $"PASS: native maximize/restore; content [{start},{end}] is inside work area [{work.Left},{work.Top},{work.Right},{work.Bottom}]; invisible resize frame [{maximized.Left},{maximized.Top},{maximized.Right},{maximized.Bottom}].");
        foreach (var accept in new[] { false, true })
        {
            var modal = new AppDialog(T("Отбросить несохранённые изменения?"), T("Настройки"), "Отбросить", true) { Owner = this };
            _ = Dispatcher.BeginInvoke(() => (accept ? modal.AcceptButton : modal.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            if (modal.ShowDialog() != accept) throw new InvalidOperationException("Themed modal returned an incorrect decision.");
        }
        File.AppendAllText(Path.Combine(destination, "window-result.txt"), "\nPASS: themed modal cancel/accept routes return the correct decision.");
        File.AppendAllText(Path.Combine(destination, "window-result.txt"), "\nPASS: catalog memo spans the catalog and remains aligned with the list when maximized.");
    }
}
