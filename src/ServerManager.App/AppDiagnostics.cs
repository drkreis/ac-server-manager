using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ServerManager.Core;

namespace ServerManager.App;

internal static class AppDiagnostics
{
    private static ErrorJournal journal = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACServerManager", "logs"));
    internal static void Initialize(string[] args)
    {
        var index = Array.FindIndex(args, a => a is "--ui-qa" or "--smoke" or "--window-qa" or "--wizard-qa" or "--wizard-download-qa" or "--runtime-qa" or "--folder-qa" or "--recovery-qa" or "--templates-qa");
        if (index >= 0 && index + 1 < args.Length) journal = new(Path.Combine(Path.GetFullPath(args[index + 1]), "diagnostics"));
    }
    internal static void Record(Exception error, string operation) => journal.Record(error, operation);
    internal static string Report(string runtime, params (string Path, string Label)[] paths)
    {
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var report = new StringBuilder()
            .AppendLine("AC Server Manager diagnostic report")
            .AppendLine($"Version: {version}")
            .AppendLine($"Created: {DateTimeOffset.Now:O}")
            .AppendLine($"OS: {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}")
            .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}; language: {UiText.Language}")
            .AppendLine("No configuration files or game content are attached.")
            .AppendLine().AppendLine("Runtime:").AppendLine(runtime)
            .AppendLine().AppendLine("Manager errors:").AppendLine(journal.Read());
        return DiagnosticSanitizer.Redact(report.ToString(), paths);
    }
}
