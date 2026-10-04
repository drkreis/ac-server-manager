using System.Windows;
using System.Windows.Input;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class AppDialog : Window
{
    public AppDialog(string message, string title, string accept, bool confirmation)
    {
        InitializeComponent();
        Title = title; DialogTitle.Text = title; DialogMessage.Text = message;
        AcceptButton.Content = T(accept);
        if (!confirmation) { CancelButton.Visibility = Visibility.Collapsed; CancelButton.IsDefault = false; AcceptButton.IsDefault = true; }
    }
    public static bool Confirm(Window owner, string message, string title, string accept)
        => new AppDialog(message, title, accept, true) { Owner = owner }.ShowDialog() == true;
    public static void Notify(Window? owner, string message, string title)
    {
        var dialog = new AppDialog(message, title, "Понятно", false);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }
    private void AcceptDialog(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelDialog(object sender, RoutedEventArgs e) => DialogResult = false;
    private void DialogKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }
}
