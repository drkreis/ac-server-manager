using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class CarDetailsWindow : Window
{
    public SkinInfo? SelectedSkin => SkinSwatches.SelectedItem as SkinInfo;
    public CarDetailsWindow(CarInfo car, string currentSkin)
    {
        InitializeComponent(); Title = T("Информация о машине"); CarName.Text = car.Name; CarId.Text = car.Id;
        SkinSwatches.ItemsSource = car.Skins;
        SkinSwatches.SelectedItem = car.Skins.FirstOrDefault(s => s.Id.Equals(currentSkin, StringComparison.OrdinalIgnoreCase));
        // An unavailable/generated skin must not silently become a real skin when opening the dialog.
        if (SelectedSkin == null) { SkinName.Text = currentSkin.Length == 0 ? T("Скин не выбран") : T("Текущий скин: {0} (нет превью)", currentSkin); UpdatePreview(null); }
        UseSkinButton.IsEnabled = SelectedSkin != null;
        Fact("Марка", car.Brand); Fact("Страна", car.Details.Country); Fact("Год", car.Details.Year); Fact("Класс", car.Details.Class); Fact("Автор", car.Details.Author);
        foreach (var (id, label) in new[] { ("bhp", "Мощность"), ("torque", "Крутящий момент"), ("weight", "Масса"), ("topspeed", "Макс. скорость"), ("acceleration", "Разгон"), ("pwratio", "Масса / мощность") })
            Fact(label, car.Details.Specs.GetValueOrDefault(id, ""));
        Tags.Text = string.Join(" · ", car.Details.Tags); Description.Text = car.Details.Description;
    }
    private void Fact(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var panel = new StackPanel { MinWidth = 155, Margin = new Thickness(0, 0, 20, 14) };
        panel.Children.Add(new TextBlock { Text = T(label), Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 4, 0, 0) });
        Facts.Children.Add(panel);
    }
    private void SkinChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedSkin is not { } skin) return;
        SkinName.Text = skin.Name; SkinId.Text = skin.Id; UseSkinButton.IsEnabled = true; UpdatePreview(skin.Preview);
    }
    private void UpdatePreview(string? path)
    {
        Preview.Source = FileImageConverter.Load(path);
        NoPreview.Visibility = Preview.Source == null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplySkin(object sender, RoutedEventArgs e) { if (SelectedSkin != null) DialogResult = true; }
    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }
}
