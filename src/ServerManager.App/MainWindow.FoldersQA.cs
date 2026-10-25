using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    internal async Task FolderUiTest(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new InvalidOperationException("Folder QA requires a fresh output folder.");
        Directory.CreateDirectory(output);
        var a = Path.Combine(output, "servers-a"); var b = Path.Combine(output, "servers-b"); var empty = Path.Combine(output, "empty");
        Directory.CreateDirectory(empty);
        string Create(string root, string name)
        {
            var cfg = Path.Combine(root, name, "cfg"); Directory.CreateDirectory(cfg);
            File.WriteAllText(Path.Combine(cfg, "server_cfg.ini"), $"[SERVER]\nNAME={name}\nTRACK=fixture\nMAX_CLIENTS=1\nTCP_PORT=9600\nUDP_PORT=9600\nHTTP_PORT=8081\n");
            File.WriteAllText(Path.Combine(cfg, "entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=skin\nAI=none\n");
            File.WriteAllText(Path.Combine(cfg, "extra_cfg.yml"), "EnableAi: false\n");
            return Path.GetDirectoryName(cfg)!;
        }
        var first = Create(a, "first"); var second = Create(b, "second");
        var game = Path.Combine(output, "game");
        var carUi = Path.Combine(game, "content", "cars", "car", "ui"); var trackUi = Path.Combine(game, "content", "tracks", "fixture", "ui");
        Directory.CreateDirectory(carUi); Directory.CreateDirectory(trackUi);
        File.WriteAllText(Path.Combine(carUi, "ui_car.json"), "{\"name\":\"Fixture car\"}");
        File.WriteAllText(Path.Combine(trackUi, "ui_track.json"), "{\"name\":\"Fixture track\",\"pitboxes\":\"1\"}");
        var preview = new PngBitmapEncoder(); preview.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)));
        using (var file = File.Create(Path.Combine(trackUi, "preview.png"))) preview.Save(file);
        ServerRoot.Text = a; GameRoot.Text = game; await InitializeAsync();
        var savedIni = File.ReadAllBytes(Path.Combine(first, "cfg", "server_cfg.ini"));
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Folder event did not complete."); await Task.Delay(15); }
        }
        async Task Enter(TextBox field)
        {
            field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Wait(() => !folderOperation);
        }
        async Task Capture(string name)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); RootContent.UpdateLayout();
            var target = new RenderTargetBitmap((int)Math.Ceiling(RootContent.ActualWidth), (int)Math.Ceiling(RootContent.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(RootContent), null, new Rect(0, 0, RootContent.ActualWidth, RootContent.ActualHeight));
            target.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
        }
        Check(profile?.Path == first && SlotGrid.Items.Count == 1, "Initial profile not loaded.");
        var originalCatalog = catalog; var oldProfile = profile;
        Log.Text = "old server log"; diagnosticText = "old diagnostics";
        ServerRoot.Text = empty; await Enter(ServerRoot);
        void EmptyState()
        {
            Check(profile == null && ServerList.Items.Count == 0 && ServerList.SelectedItem == null && lastSelectedPath == "" && !dirty,
                "Empty root retained a selected profile or draft.");
            Check(Editor.Visibility == Visibility.Collapsed && !Editor.IsEnabled && EmptyServerState.Visibility == Visibility.Visible && NewServerButton.IsEnabled && ReportButton.IsEnabled,
                "Empty state is unusable or old editor remains visible.");
            Check(SlotGrid.ItemsSource == null && NameInput.Text == "" && TrackInput.Text == "" && LayoutInput.Text == "" && PasswordInput.Password == ""
                && SlotSummary.Text == "" && SlotsCount.Text == "" && TrackPreview.Source == null && CurrentTrackName.Text == "" && TrackDetails.Text == ""
                && Log.Text == "" && diagnosticText == "" && draftBaseline == null && lastReport == null && !StartButton.IsEnabled && !StopButton.IsEnabled && !DeleteServerButton.IsEnabled,
                "Old fields, content, log or actions survived clear.");
        }
        EmptyState(); Check(catalog == originalCatalog, "Server-root change unnecessarily reloaded game content.");
        oldProfile!.Slots[0].Skin = "detached"; oldProfile.Slots.Add(new Slot { Model = "detached" });
        EmptyState();
        await Capture("empty-en.png");
        LanguagePicker.SelectedIndex = 0; EmptyState(); await Capture("empty-ru.png"); LanguagePicker.SelectedIndex = 1;

        ServerRoot.Text = b;
        ServerRoot.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, ServerRoot, GameRoot) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        await Wait(() => !folderOperation);
        Check(profile?.Path == second && ServerList.Items.Count == 1 && Editor.Visibility == Visibility.Visible && EmptyServerState.Visibility == Visibility.Collapsed && NameInput.Text == "second", "Focus loss did not commit the new root.");
        var selected = profile;
        ServerRoot.Text = Path.Combine(output, "missing"); await Enter(ServerRoot);
        Check(ServerRoot.Text == b && appliedServerRoot == b && profile == selected, "Invalid path retained a mismatched editor or lost the profile.");

        NameInput.Text = "Unsaved folder QA draft"; var draft = profile;
        ServerRoot.Text = b.ToUpperInvariant() + "\\"; await Enter(ServerRoot);
        Check(profile == draft && dirty && NameInput.Text == "Unsaved folder QA draft", "Equivalent path discarded a draft.");
        async Task<bool> ChangeWithAnswer(bool accept)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d => d.IsVisible && d.IsLoaded) is not { } dialog) return;
                timer.Stop(); (accept ? dialog.AcceptButton : dialog.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { return await CommitFolderAsync(ServerRoot); }
            finally { timer.Stop(); }
        }
        ServerRoot.Text = empty;
        Check(!await ChangeWithAnswer(false) && ServerRoot.Text == b && profile == draft && dirty && NameInput.Text == "Unsaved folder QA draft", "Cancel did not preserve root, profile and draft.");
        ServerRoot.Text = empty; Check(await ChangeWithAnswer(true), "Confirmed folder change failed."); EmptyState();
        Check(File.ReadAllBytes(Path.Combine(first, "cfg", "server_cfg.ini")).SequenceEqual(savedIni)
            && !File.ReadAllText(Path.Combine(second, "cfg", "server_cfg.ini")).Contains("Unsaved"), "Folder change saved or modified a fixture configuration.");

        ServerRoot.Text = a; await Enter(ServerRoot); Check(profile?.Path == first && !dirty, "Returning to a populated root did not load it.");
        GameRoot.Text = ""; await Enter(GameRoot);
        Check(catalog == null && CarList.Items.Count == 0 && TrackList.Items.Count == 0 && CarPreview.Source == null && CatalogTrackPreview.Source == null, "Clearing game root retained the old catalog.");
        GameRoot.Text = game; await Enter(GameRoot); Check(catalog?.Cars.Count == 1 && catalog.Tracks.Count == 1 && profile?.Path == first, "Game root did not reload automatically.");
        ServerRoot.Text = ""; await Enter(ServerRoot); EmptyState();
        File.WriteAllText(Path.Combine(output, "folder-result.txt"), "PASS: populated/empty/unset roots, Enter and focus-loss commits, complete editor clearing, old event detachment, RU/EN empty states, catalog reuse, invalid/equivalent paths, discard Cancel/accept, unchanged config bytes and game-root catalog reload. All writes confined to fixtures; no game or server process started.");
    }
}
