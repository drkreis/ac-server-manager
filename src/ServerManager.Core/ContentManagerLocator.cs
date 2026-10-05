using System.Text.RegularExpressions;

namespace ServerManager.Core;

public static class ContentManagerLocator
{
    public static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var match = Regex.Match(expanded, "^(?:\"(?<exe>[^\"]+\\.exe)\"|(?<exe>.+?\\.exe))(?:\\s|$)", RegexOptions.IgnoreCase);
        return match.Success ? ExistingExecutable(match.Groups["exe"].Value) : null;
    }
    private static string? ExistingExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            return expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(expanded) ? expanded : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    public static string? Find(string? saved, IEnumerable<string> running, IEnumerable<string> commands, IEnumerable<string> folders)
    {
        if (ExistingExecutable(saved) is { } stored) return stored;
        foreach (var path in running) if (ExistingExecutable(path) is { } active) return active;
        foreach (var command in commands) if (ExecutableFromCommand(command) is { } registered) return registered;
        foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var relative in new[] { "Content Manager.exe", "ContentManager.exe", @"Content Manager\Content Manager.exe", @"ContentManager\ContentManager.exe" })
                if (ExistingExecutable(Path.Combine(folder, relative)) is { } installed) return installed;
        return null;
    }
}
