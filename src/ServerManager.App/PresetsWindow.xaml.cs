using System.Windows;
using System.Windows.Controls;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class PresetsWindow : Window
{
    private readonly string file;
    public PresetsWindow(string presetsFile)
    {
        file = presetsFile; InitializeComponent(); Title = T("Пресеты"); SourceInitialized += (_, _) => NativeWindow.Attach(this); Reload();
    }
    internal void Reload()
    {
        try { PresetList.ItemsSource = ServerPresets.Load(file); LibraryMessage.Text = PresetList.Items.Count == 0 ? T("Пресетов пока нет. ПКМ по серверу → «Сохранить как пресет». ") : ""; }
        catch (Exception ex) { PresetList.ItemsSource = null; LibraryMessage.Text = ex.Message; }
    }
    private void SelectedPreset(object sender, SelectionChangedEventArgs e) => RenameButton.IsEnabled = RemoveButton.IsEnabled = PresetList.SelectedItem is ServerPreset;
    private void CloseLibrary(object sender, RoutedEventArgs e) => Close();
    private void ReloadLibrary(object sender, RoutedEventArgs e) => Reload();
    private void RenamePreset(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not ServerPreset preset) return;
        try
        {
            var name = AppDialog.Prompt(this, T("Название пресета"), T("Переименовать пресет"), preset.Name, s => ServerPresets.Validate(preset with { Name = s.Trim() }));
            if (name == null) return; ServerPresets.Save(file, preset with { Name = name.Trim() }); Reload();
        }
        catch (Exception ex) { LibraryMessage.Text = ex.Message; }
    }
    private void DeletePreset(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not ServerPreset preset || !AppDialog.Confirm(this, T("Удалить пресет «{0}»? Серверы не изменятся.", preset.Name), T("Удалить пресет"), "Удалить")) return;
        try { ServerPresets.Delete(file, preset.Id); Reload(); } catch (Exception ex) { LibraryMessage.Text = ex.Message; }
    }
}
