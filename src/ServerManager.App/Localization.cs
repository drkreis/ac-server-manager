using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ServerManager.Core;

namespace ServerManager.App;

public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Current { get; } = new();
    public string Language => UiText.Language;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Apply(string? language)
    {
        UiText.Language = language == "en" ? "en" : "ru";
        foreach (var (id, translation) in UiText.Entries) Application.Current.Resources[id] = UiText.Language == "en" ? translation.En : translation.Ru;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }
}
public sealed class ServerSummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.FirstOrDefault() is ServerRow row ? row.Profile.Summary : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
