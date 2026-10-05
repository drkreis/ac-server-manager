using System.IO;
using System.Windows;
namespace ServerManager.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Localization.Current.Apply("en");
        DispatcherUnhandledException += (_, args) =>
        {
            var qaIndex = Array.FindIndex(e.Args, a => a is "--ui-qa" or "--smoke" or "--window-qa" or "--wizard-qa" or "--wizard-download-qa");
            if (qaIndex >= 0)
            {
                var output = qaIndex + 1 < e.Args.Length ? e.Args[qaIndex + 1] : Path.Combine(AppContext.BaseDirectory, "smoke");
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-error.txt"), args.Exception.ToString());
                args.Handled = true; Shutdown(1); return;
            }
            AppDialog.Notify(MainWindow, args.Exception.Message, ServerManager.Core.UiText.T("Ошибка")); args.Handled = true;
        };
        var window = new MainWindow(e.Args);
        MainWindow = window;
        if (e.Args.Contains("--ui-qa") || e.Args.Contains("--smoke") || e.Args.Contains("--window-qa") || e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () =>
            {
                var output = e.Args.SkipWhile(a => a != "--ui-qa" && a != "--smoke" && a != "--window-qa" && a != "--wizard-qa" && a != "--wizard-download-qa").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "smoke");
                try
                {
                    if(e.Args.Contains("--ui-qa")) await window.DetailsUiTest(output); else if(e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa")) await window.WizardSmokeTest(output);
                    else { await window.SmokeTest(output); if (e.Args.Contains("--ui-qa") || e.Args.Contains("--window-qa")) await window.WindowGeometryTest(output); }
                    Shutdown(0);
                }
                catch (Exception ex) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-error.txt"), ex.ToString()); Shutdown(1); }
            });
            if (e.Args.Contains("--ui-qa") || e.Args.Contains("--window-qa") || e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa")) window.Show();
        }
        else window.Show();
    }
}
