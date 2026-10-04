using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public sealed class WizardCar(CarInfo car, string skin) : INotifyPropertyChanged
{
    public string Model => car.Id;
    public string Name => car.Name;
    public string Skin { get; } = skin;
    private string quantity = "1", role = "none";
    public string Quantity { get => quantity; set { quantity = value; PropertyChanged?.Invoke(this, new(nameof(Quantity))); } }
    public string Role { get => role; set { role = value; PropertyChanged?.Invoke(this, new(nameof(Role))); } }
    public IReadOnlyList<RoleChoice> Roles { get; } = [new("none", "Игрок"), new("fixed", "Трафик"), new("auto", "Игрок / AI")];
    public event PropertyChangedEventHandler? PropertyChanged;
    public CreationCar Build()
    {
        if (!int.TryParse(Quantity, out var count) || count is < 1 or > 255) throw new InvalidOperationException(T("Количество каждой машины должно быть от 1 до 255."));
        return new(Model, Skin, Role, count);
    }
}

public partial class CreateServerWindow : Window
{
    private readonly ObservableCollection<WizardCar> chosen = [];
    private ContentCatalog? catalog;
    private int step;
    private bool ready, busy;
    private string suggestedRoot = "";
    private CancellationTokenSource? cancellation;
    private ServerCreationRequest? reviewedRequest;
    private Exception? creationError;
    public string? CreatedPath { get; private set; }
    public string SelectedGamePath => GamePath.Text;
    public string SelectedServersRoot => ServersPath.Text;
    public CreateServerWindow(string serversRoot, string gamePath, string? runtimeFolder, ContentCatalog? initialCatalog)
    {
        InitializeComponent();
        catalog = initialCatalog;
        ServersPath.Text = File.Exists(Path.Combine(serversRoot, "cfg", "server_cfg.ini")) ? Path.GetDirectoryName(serversRoot)! : serversRoot;
        GamePath.Text = gamePath; SourcePath.Text = runtimeFolder ?? "";
        ChosenCars.ItemsSource = chosen;
        Localization.Current.PropertyChanged += LanguageChanged;
        Closed += (_, _) => Localization.Current.PropertyChanged -= LanguageChanged;
        chosen.CollectionChanged += (_, _) => UpdateCount();
        SourceInitialized += (_, _) => NativeWindow.Attach(this);
        StateChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            var insets = WindowState == WindowState.Maximized ? NativeWindow.MaximizedInsets(this) : new Thickness();
            WizardRoot.Margin = new(24 + insets.Left, 24 + insets.Top, 24 + insets.Right, 24 + insets.Bottom);
        });
        Closing += (_, e) => { if (busy) { cancellation?.Cancel(); e.Cancel = true; } };
        ready = true; SourceMode.SelectedIndex = 0;
        SuggestPorts(); RefreshLanguage();
        var candidate = "my-ac-server"; var i = 2;
        while (Directory.Exists(Path.Combine(ServersPath.Text, candidate))) candidate = "my-ac-server-" + i++;
        FolderName.Text = candidate;
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshLanguage();
    private void RefreshLanguage()
    {
        ModelColumn.Header = T("Модель"); QuantityColumn.Header = T("Количество"); RoleColumn.Header = T("Назначение"); SkinColumn.Header = T("Скин");
        foreach (var row in chosen) foreach (var role in row.Roles) role.RefreshName();
        ShowStep(); UpdateCount();
        if (reviewedRequest != null) RenderReview(reviewedRequest);
        LayoutSelected(this, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }
    private void RenderReview(ServerCreationRequest request)
    {
        ReviewText.Text=T("Название: {0}\nПапка: {1}\nТрасса: {2}\nСлоты: {3} игроков · {4} AI\nПорты: TCP {5} · UDP {6} · HTTP {7}\nВремя: {8}\nAssettoServer: {9}",
            request.Name, ServerCreation.Destination(request.ServersRoot, request.Folder), catalog!.FindTrack(request.Track, request.Layout)!.Label,
            request.Cars.Where(c => c.Role != "fixed").Sum(c => c.Count), request.Cars.Where(c => c.Role == "fixed").Sum(c => c.Count),
            request.TcpPort, request.UdpPort, request.HttpPort, StartTime.Text,
            SourceMode.SelectedIndex == 0 ? T("Официальный стабильный релиз") : SourcePath.Text);
    }
    private void SuggestPorts()
    {
        suggestedRoot = ServersPath.Text;
        var tcp = new HashSet<int>(); var udp = new HashSet<int>();
        if (Directory.Exists(ServersPath.Text))
            foreach (var profile in ServerProfile.Discover(ServersPath.Text))
            {
                if (int.TryParse(ConfigText.Get(profile.Ini, "SERVER", "TCP_PORT"), out var t)) tcp.Add(t);
                if (int.TryParse(ConfigText.Get(profile.Ini, "SERVER", "HTTP_PORT"), out var h)) tcp.Add(h);
                if (int.TryParse(ConfigText.Get(profile.Ini, "SERVER", "UDP_PORT"), out var u)) udp.Add(u);
            }
        var network = IPGlobalProperties.GetIPGlobalProperties();
        tcp.UnionWith(network.GetActiveTcpListeners().Select(p => p.Port)); udp.UnionWith(network.GetActiveUdpListeners().Select(p => p.Port));
        var port = 9600; while (port < 65535 && (tcp.Contains(port) || udp.Contains(port))) port++;
        var http = 8081; while (http < 65535 && (tcp.Contains(http) || http == port)) http++;
        TcpPort.Text = UdpPort.Text = port.ToString(); HttpPort.Text = http.ToString();
    }
    private void CheckPorts(ServerCreationRequest request)
    {
        var network = IPGlobalProperties.GetIPGlobalProperties();
        if (network.GetActiveTcpListeners().Any(p => p.Port == request.TcpPort || p.Port == request.HttpPort)
            || network.GetActiveUdpListeners().Any(p => p.Port == request.UdpPort))
            throw new InvalidOperationException(T("Один из выбранных портов занят. Измените порты или остановите другой сервер."));
    }
    private void SourceModeChanged(object sender, SelectionChangedEventArgs e)
    { if (ready) SourcePathPanel.Visibility = SourceMode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; }
    private void BrowseRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title=T("Папка для новых серверов") };
        if (dialog.ShowDialog(this) == true) { ServersPath.Text=dialog.FolderName; SuggestPorts(); }
    }
    private void BrowseGame(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title=T("Корневая папка Assetto Corsa") };
        if (dialog.ShowDialog(this) == true) GamePath.Text=dialog.FolderName;
    }
    private void BrowseSource(object sender, RoutedEventArgs e)
    {
        if (SourceMode.SelectedIndex == 1)
        {
            var dialog = new OpenFolderDialog { Title=T("Папка программы AssettoServer") };
            if (dialog.ShowDialog(this) == true) SourcePath.Text=dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog { Filter="AssettoServer ZIP|*.zip", Title=T("Windows x64 ZIP AssettoServer") };
            if (dialog.ShowDialog(this) == true) SourcePath.Text=dialog.FileName;
        }
    }
    private void BrowseSpline(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter="AI spline|*.ai;*.aip", Title=T("Сплайн AI выбранной трассы") };
        if (dialog.ShowDialog(this) == true) SplinePath.Text=dialog.FileName;
    }
    private void ShowStep()
    {
        FrameworkElement[] panels = [BasicsStep, TrackStep, CarsStep, SettingsStep, ReviewStep];
        for (var i = 0; i < panels.Length; i++) panels[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        string[] captions = ["Новый сервер и установка", "Выбор трассы и варианта", "Машины для игроков и трафика", "Основные параметры", "Проверка перед созданием"];
        StepCaption.Text = T(captions[step]); StepCounter.Text = (step + 1) + " / 5";
        BackButton.IsEnabled = step > 0 && !busy;
        NextButton.Content = T(step == 4 ? "Создать сервер" : "Далее →");
        Message.Text = "";
    }
    private void Previous(object sender, RoutedEventArgs e) { if (!busy && step > 0) { step--; ShowStep(); } }
    private async void Next(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            Message.Text = "";
            if (step == 0)
            {
                _ = ServerCreation.Destination(ServersPath.Text, FolderName.Text);
                if (suggestedRoot != ServersPath.Text) SuggestPorts();
                if (string.IsNullOrWhiteSpace(ServerName.Text)) throw new InvalidOperationException(T("Введите название сервера (до 120 символов)."));
                if (SourceMode.SelectedIndex == 1 && !File.Exists(Path.Combine(SourcePath.Text, "AssettoServer.exe"))) throw new InvalidOperationException(T("Выберите папку с AssettoServer.exe."));
                if (SourceMode.SelectedIndex == 2 && !File.Exists(SourcePath.Text)) throw new InvalidOperationException(T("Выберите скачанный ZIP AssettoServer."));
                if (catalog == null || !Path.GetFullPath(GamePath.Text).Equals(catalog.GamePath, StringComparison.OrdinalIgnoreCase))
                {
                    SetBusy(true); Message.Text=T("Читаю установленный контент…");
                    try { var game = GamePath.Text; catalog = await Task.Run(() => ContentCatalog.Load(game)); }
                    finally { SetBusy(false); }
                }
                Tracks.ItemsSource=catalog.TrackGroups; Cars.ItemsSource=catalog.Cars;
                if (Tracks.SelectedIndex < 0) Tracks.SelectedIndex=0;
            }
            else if (step == 1 && Layouts.SelectedItem is not TrackInfo) throw new InvalidOperationException(T("Выберите трассу и вариант из установленного контента."));
            else if (step == 2) ValidateContentDraft();
            else if (step == 3)
            {
                var request = BuildRequest(); ServerCreation.Validate(request, catalog!); CheckPorts(request);
                reviewedRequest=request; RenderReview(request);
            }
            else if (step == 4) { await CreateAsync(); return; }
            step++; ShowStep();
        }
        catch (Exception ex) { Message.Text=ex.Message; }
    }
    private ServerCreationRequest BuildRequest()
    {
        ChosenCars.CommitEdit(DataGridEditingUnit.Cell, true); ChosenCars.CommitEdit(DataGridEditingUnit.Row, true);
        var track = Layouts.SelectedItem as TrackInfo ?? throw new InvalidOperationException(T("Выберите трассу и вариант из установленного контента."));
        if (!int.TryParse(TcpPort.Text, out var tcp) || !int.TryParse(UdpPort.Text, out var udp) || !int.TryParse(HttpPort.Text, out var http)) throw new InvalidOperationException(T("Порты должны быть в диапазоне 1–65535."));
        if (!TimeOnly.TryParseExact(StartTime.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new InvalidOperationException(T("Введите время в формате HH:mm."));
        return new() { ServersRoot=ServersPath.Text, Folder=FolderName.Text, Name=ServerName.Text.Trim(), Track=track.Id, Layout=track.Layout,
            AiSpline=SplinePath.Text, Cars=chosen.Select(c => c.Build()).ToArray(), TcpPort=tcp, UdpPort=udp, HttpPort=http, StartMinutes=time.Hour*60+time.Minute,
            Password=JoinPassword.Password, AdminPassword=AdminPassword.Password, WeatherFx=WeatherFx.IsChecked == true, PublishToLobby=Lobby.IsChecked == true };
    }
    private void ValidateContentDraft()
    {
        var track = (TrackInfo)Layouts.SelectedItem;
        var request = new ServerCreationRequest { ServersRoot=ServersPath.Text, Folder=FolderName.Text, Name=ServerName.Text.Trim(), Track=track.Id, Layout=track.Layout, AiSpline=SplinePath.Text, Cars=chosen.Select(c => c.Build()).ToArray() };
        ServerCreation.Validate(request, catalog!);
    }
    private async Task CreateAsync()
    {
        creationError=null;
        var request=BuildRequest(); ServerCreation.Validate(request, catalog!); CheckPorts(request);
        cancellation=new(); var token=cancellation.Token;
        var mode=SourceMode.SelectedIndex; var source=SourcePath.Text;
        var downloadDir=Path.Combine(Path.GetTempPath(), "ACServerManager-download-" + Guid.NewGuid().ToString("N"));
        SetBusy(true);
        try
        {
            var progress=new Progress<string>(text => Message.Text=text);
            if (mode == 0)
            {
                Directory.CreateDirectory(downloadDir); source=Path.Combine(downloadDir, "assetto-server-win-x64.zip");
                await AssettoServerDownload.Download(source, progress, token);
            }
            var result = await Task.Run(() => ServerCreation.Create(request, catalog!, source, progress, token), token);
            CreatedPath=result.Path; SetBusy(false); DialogResult=true;
        }
        catch (OperationCanceledException) { Message.Text=T("Создание отменено. Новый сервер не создан."); }
        catch (HttpRequestException ex) { Message.Text=T("Не удалось скачать AssettoServer. Проверьте интернет или выберите локальный ZIP.") + "\n" + ex.Message; }
        catch (Exception ex) { creationError=ex; Message.Text=ex.Message; }
        finally
        {
            if (Directory.Exists(downloadDir) && Path.GetFileName(downloadDir).StartsWith("ACServerManager-download-", StringComparison.Ordinal) && Path.GetDirectoryName(downloadDir) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())) Directory.Delete(downloadDir, true);
            SetBusy(false); cancellation.Dispose(); cancellation=null;
        }
    }
    private void SetBusy(bool value)
    { busy=value; StepContainer.IsEnabled=!value; NextButton.IsEnabled=!value; BackButton.IsEnabled=!value && step>0; Progress.Visibility=value ? Visibility.Visible : Visibility.Collapsed; }
    private void CancelWizard(object sender, RoutedEventArgs e) { if (busy) cancellation?.Cancel(); else DialogResult=false; }
    private void TrackSearchChanged(object sender, TextChangedEventArgs e)
    { if (ready && catalog != null) Tracks.ItemsSource=catalog.TrackGroups.Where(g => (g.Name + " " + g.Id).Contains(TrackSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
    private void TrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        var group=Tracks.SelectedItem as TrackGroup; Layouts.ItemsSource=group?.Layouts; Layouts.SelectedItem=group?.DefaultLayout;
        TrackName.Text=group?.Name ?? T("Выберите трассу");
    }
    private void LayoutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        var track=Layouts.SelectedItem as TrackInfo; TrackPreview.Source=FileImageConverter.Load(track?.Preview);
        TrackDetails.Text=track == null ? "" : track.Layout + (track.PitBoxes.HasValue ? T(" · {0} пит-боксов", track.PitBoxes) : "");
        UpdateCount();
    }
    private void CarSearchChanged(object sender, TextChangedEventArgs e)
    { if (ready && catalog != null) Cars.ItemsSource=catalog.Cars.Where(c => (c.Name + " " + c.Brand + " " + c.Id).Contains(CarSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
    private void CarSelected(object sender, SelectionChangedEventArgs e)
    { if (!ready) return; CarSkin.ItemsSource=(Cars.SelectedItem as CarInfo)?.Skins; CarSkin.SelectedIndex=0; }
    private void AddCar(object sender, RoutedEventArgs e)
    {
        if (Cars.SelectedItem is not CarInfo car) { Message.Text=T("Выберите машину в каталоге."); return; }
        var row=new WizardCar(car, (CarSkin.SelectedItem as SkinInfo)?.Id ?? "");
        row.PropertyChanged += (_, _) => UpdateCount(); chosen.Add(row); ChosenCars.SelectedItem=row; Message.Text="";
    }
    private void RemoveCar(object sender, RoutedEventArgs e) { if (ChosenCars.SelectedItem is WizardCar row) chosen.Remove(row); }
    private void CarsEdited(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(UpdateCount);
    private void UpdateCount()
    {
        if (!ready) return;
        var players=0; var traffic=0;
        foreach (var row in chosen) if (int.TryParse(row.Quantity, out var count)) { if (row.Role == "fixed") traffic+=Math.Clamp(count, 0, 255); else players+=Math.Clamp(count, 0, 255); }
        SlotCount.Text=T("{0} игроков · {1} AI", players, traffic);
    }
}
