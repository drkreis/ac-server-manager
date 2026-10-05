using System.Windows;
using System.Windows.Input;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public partial class AppDialog : Window
{
    private Action<string>? validateInput;
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
    public static string? Prompt(Window owner, string message, string title, string initial, Action<string> validate)
    {
        var dialog = new AppDialog(message, title, "Переименовать", true) { Owner = owner, validateInput = validate };
        dialog.DialogInput.Text = initial; dialog.DialogInput.Visibility = Visibility.Visible;
        dialog.CancelButton.IsDefault = false; dialog.AcceptButton.IsDefault = true;
        dialog.Loaded += (_, _) => { dialog.DialogInput.Focus(); dialog.DialogInput.SelectAll(); };
        return dialog.ShowDialog() == true ? dialog.DialogInput.Text : null;
    }
    private void AcceptDialog(object sender, RoutedEventArgs e)
    {
        if (validateInput != null)
            try { validateInput(DialogInput.Text); }
            catch (Exception ex) { InputError.Text = ex.Message; InputError.Visibility = Visibility.Visible; return; }
        DialogResult = true;
    }
    private void CancelDialog(object sender, RoutedEventArgs e) => DialogResult = false;
    private void InputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (InputError != null) InputError.Visibility = Visibility.Collapsed; }
    private void DialogKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }
}
