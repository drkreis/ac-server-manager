using System.IO;
using System.Windows;
using System.Windows.Controls;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class CreateServerWindow
{
    private string? presetsFile;
    private void ReloadPresetChoices()
    {
        PresetBlock.Visibility = presetsFile == null ? Visibility.Collapsed : Visibility.Visible;
        if (presetsFile == null) return;
        try
        {
            var selected = (PresetChoice.SelectedItem as ServerPreset)?.Id;
            PresetChoice.ItemsSource = ServerPresets.Load(presetsFile);
            PresetChoice.SelectedItem = PresetChoice.Items.OfType<ServerPreset>().FirstOrDefault(p => p.Id == selected);
        }
        catch (Exception ex) { PresetChoice.ItemsSource = null; Message.Text = ex.Message; }
    }
    private void RefreshPresets(object sender, RoutedEventArgs e) => ReloadPresetChoices();
    private async void ApplyPreset(object sender, RoutedEventArgs e) => await ApplyPresetAsync();
    internal async Task ApplyPresetAsync()
    {
        if (busy) return;
        if (PresetChoice.SelectedItem is not ServerPreset preset) { Message.Text = T("Выберите пресет из списка."); return; }
        SetBusy(true);
        try
        {
            var game = GamePath.Text;
            var candidate = catalog != null && catalog.GamePath.Equals(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase) ? catalog : await Task.Run(() => ContentCatalog.Load(game));
            ServerPresets.CheckContent(preset, candidate);
            if (chosen.Count > 0 && !AppDialog.Confirm(this, T("Заменить трассу, машины и основные параметры черновика пресетом «{0}»? Имя, папка, порты и пароли останутся прежними.", preset.Name), T("Применить пресет"), "Применить")) return;
            var rows = preset.Cars.Select(c => new WizardCar(candidate.FindCar(c.Model)!, c.Skin) { Quantity = c.Count.ToString(), Role = c.Role }).ToArray();
            catalog = candidate; Tracks.ItemsSource = catalog.TrackGroups; Cars.ItemsSource = catalog.Cars;
            Tracks.SelectedItem = Tracks.Items.OfType<TrackGroup>().Single(g => g.Id.Equals(preset.Track, StringComparison.OrdinalIgnoreCase));
            Layouts.SelectedItem = Layouts.Items.OfType<TrackInfo>().Single(t => t.Layout.Equals(preset.Layout, StringComparison.OrdinalIgnoreCase));
            chosen.Clear(); foreach (var row in rows) { row.PropertyChanged += (_, _) => UpdateCount(); chosen.Add(row); }
            EnableTraffic.IsChecked = preset.EnableAi;
            StartTime.Text = $"{preset.StartMinutes / 60:00}:{preset.StartMinutes % 60:00}"; WeatherFx.IsChecked = preset.WeatherFx;
            FuelRate.Text = preset.FuelRate.ToString(); DamageRate.Text = preset.DamageRate.ToString(); TyreWearRate.Text = preset.TyreWearRate.ToString();
            InfinitePractice.IsChecked = preset.InfinitePractice; PracticeMinutes.Text = preset.PracticeMinutes.ToString(); SlotLimit.Text = preset.SlotLimit?.ToString() ?? "";
            SplinePath.Clear(); reviewedRequest = null; UpdateCount();
            Message.Text = T("Пресет «{0}» применён. Проверьте шаги мастера; свой AI-сплайн при необходимости выберите заново.", preset.Name);
        }
        catch (Exception ex) { Message.Text = ex.Message; }
        finally { SetBusy(false); }
    }
}
