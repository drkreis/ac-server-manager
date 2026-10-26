using System.ComponentModel;
using System.IO;
using System.Windows;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class CloneServerWindow : Window
{
    private readonly string source, game;
    private readonly Func<ServerCloneRequest, IProgress<string>, CancellationToken, Task<string>> copy;
    private CancellationTokenSource? cancellation;
    public string ServersRoot { get; }
    public string? CreatedPath { get; private set; }
    public CloneServerWindow(ServerProfile profile, string serversRoot, string gamePath, Func<ServerCloneRequest, IProgress<string>, CancellationToken, Task<string>> action)
    {
        source = profile.Path; game = gamePath; copy = action;
        ServersRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(serversRoot)).Equals(source, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(source)! : serversRoot;
        InitializeComponent(); Title = T("Копировать сервер"); SourceCaption.Text = T("Сохранённая версия: {0}", profile.Folder); DestinationRoot.Text = ServersRoot;
        var stem = profile.Folder[..Math.Min(profile.Folder.Length, 65)] + "-copy"; var name = stem; var n = 2;
        while (Directory.Exists(Path.Combine(ServersRoot, name)) || File.Exists(Path.Combine(ServersRoot, name))) name = stem + "-" + n++;
        FolderName.Text = name; ServerName.Text = ConfigText.Get(profile.Ini, "SERVER", "NAME", profile.Folder)[..Math.Min(ConfigText.Get(profile.Ini, "SERVER", "NAME", profile.Folder).Length, 110)] + T(" (копия)");
        SourceInitialized += (_, _) => NativeWindow.Attach(this);
        Loaded += async (_, _) =>
        {
            try { var p = await Task.Run(() => ServerPortSelection.Suggest(ServersRoot)); TcpPort.Text = p.Tcp.ToString(); UdpPort.Text = p.Udp.ToString(); HttpPort.Text = p.Http.ToString(); }
            catch (Exception ex) { CloneMessage.Text = ex.Message; }
        };
    }
    internal async Task CopyAsync()
    {
        if (cancellation != null) return;
        try
        {
            if (!int.TryParse(TcpPort.Text, out var tcp) || !int.TryParse(UdpPort.Text, out var udp) || !int.TryParse(HttpPort.Text, out var http)) throw new IOException(T("Порты должны быть в диапазоне 1–65535."));
            var request = new ServerCloneRequest(source, ServersRoot, FolderName.Text, ServerName.Text, tcp, udp, http, Advanced.IsChecked == true);
            _ = ServerClone.Validate(request, game);
            cancellation = new(); CloneFields.IsEnabled = false; CopyButton.IsEnabled = false; CloneProgress.Visibility = Visibility.Visible;
            CreatedPath = await copy(request, new Progress<string>(s => CloneMessage.Text = s), cancellation.Token);
            cancellation.Dispose(); cancellation = null; DialogResult = true;
        }
        catch (OperationCanceledException) { CloneMessage.Text = T("Копирование отменено. Новый сервер не создан."); }
        catch (Exception ex) { AppDiagnostics.Record(ex, "Clone server"); CloneMessage.Text = ex.Message; }
        finally { cancellation?.Dispose(); cancellation = null; CloneFields.IsEnabled = true; CopyButton.IsEnabled = true; CloneProgress.Visibility = Visibility.Collapsed; }
    }
    private async void CopyServer(object sender, RoutedEventArgs e) => await CopyAsync();
    private void CancelClone(object sender, RoutedEventArgs e) { if (cancellation != null) cancellation.Cancel(); else DialogResult = false; }
    private void CloneClosing(object? sender, CancelEventArgs e) { if (cancellation != null) { cancellation.Cancel(); e.Cancel = true; } }
}
