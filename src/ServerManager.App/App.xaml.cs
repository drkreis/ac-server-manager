using System.IO;
using System.Windows;
namespace ServerManager.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--signal-stop") { Shutdown(ServerProcess.Signal(e.Args)); return; }
        base.OnStartup(e);
        AppDiagnostics.Initialize(e.Args);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppDiagnostics.Record(args.ExceptionObject as Exception ?? new Exception("Unhandled runtime error"), "Unhandled runtime exception");
        TaskScheduler.UnobservedTaskException += (_, args) => AppDiagnostics.Record(args.Exception, "Unobserved task exception");
        Localization.Current.Apply("en");
        var showingUnhandledError = false;
        DispatcherUnhandledException += (_, args) =>
        {
            var qaIndex = Array.FindIndex(e.Args, a => a is "--ui-qa" or "--smoke" or "--window-qa" or "--wizard-qa" or "--wizard-download-qa" or "--runtime-qa" or "--folder-qa" or "--recovery-qa");
            if (qaIndex >= 0)
            {
                var output = qaIndex + 1 < e.Args.Length ? e.Args[qaIndex + 1] : Path.Combine(AppContext.BaseDirectory, "smoke");
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-error.txt"), args.Exception.ToString());
                args.Handled = true; Shutdown(1); return;
            }
            AppDiagnostics.Record(args.Exception, "UI exception");
            args.Handled = true;
            if (showingUnhandledError) { Shutdown(1); return; }
            showingUnhandledError = true;
            try { AppDialog.Notify(MainWindow, args.Exception.Message, ServerManager.Core.UiText.T("Ошибка")); }
            finally { showingUnhandledError = false; }
        };
        var window = new MainWindow(e.Args);
        MainWindow = window;
        if (e.Args.Contains("--ui-qa") || e.Args.Contains("--smoke") || e.Args.Contains("--window-qa") || e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa") || e.Args.Contains("--runtime-qa") || (e.Args.Contains("--folder-qa") || e.Args.Contains("--recovery-qa")))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () =>
            {
                var output = e.Args.SkipWhile(a => a != "--ui-qa" && a != "--smoke" && a != "--window-qa" && a != "--wizard-qa" && a != "--wizard-download-qa" && a != "--runtime-qa" && a != "--folder-qa" && a != "--recovery-qa").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "smoke");
                try
                {
                    if(e.Args.Contains("--recovery-qa")) await window.RecoveryUiTest(output);
                    else if(e.Args.Contains("--folder-qa")) await window.FolderUiTest(output);
                    else if(e.Args.Contains("--runtime-qa")) await window.RuntimeTest(output);
                    else if(e.Args.Contains("--ui-qa")) await window.DetailsUiTest(output); else if(e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa")) await window.WizardSmokeTest(output);
                    else { await window.SmokeTest(output); if ((e.Args.Contains("--folder-qa") || e.Args.Contains("--recovery-qa")) || e.Args.Contains("--ui-qa") || e.Args.Contains("--window-qa")) await window.WindowGeometryTest(output); }
                    Shutdown(0);
                }
                catch (Exception ex) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-error.txt"), ex.ToString()); Shutdown(1); }
            });
            if ((e.Args.Contains("--folder-qa") || e.Args.Contains("--recovery-qa")) || e.Args.Contains("--ui-qa") || e.Args.Contains("--window-qa") || e.Args.Contains("--wizard-qa") || e.Args.Contains("--wizard-download-qa")) window.Show();
        }
        else window.Show();
    }
}
