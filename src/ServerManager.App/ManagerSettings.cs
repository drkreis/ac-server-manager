using System.IO;
using System.Text.Json;

namespace ServerManager.App;

internal static class ManagerSettings
{
    internal static string SharedFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACServerManager", "settings.json");
    internal static UserSettings? Load(string appDirectory, string sharedFile, bool isolated)
    {
        var candidates = new List<string>();
        if (!isolated) candidates.Add(sharedFile);
        candidates.Add(Path.Combine(appDirectory, "settings.json"));
        if (!isolated)
        {
            // Legacy portable builds stored settings beside the EXE. Only inspect neighboring manager builds.
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(appDirectory));
            foreach (var root in new[] { parent?.FullName, parent?.Parent?.FullName }.OfType<string>())
                try
                {
                    candidates.AddRange(Directory.EnumerateDirectories(root)
                        .Where(d => Path.GetFileName(d) == "desktop" || Path.GetFileName(d).StartsWith("desktop-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(d).StartsWith("ACServerManager-", StringComparison.OrdinalIgnoreCase))
                        .Select(d => Path.Combine(d, "settings.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var settings = new List<UserSettings>();
        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            try { if (File.Exists(file) && JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file)) is { } value) settings.Add(value); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var selected = settings.FirstOrDefault();
        if (selected == null) return null;
        if (!File.Exists(selected.ContentManager)) selected = selected with { ContentManager = settings.Select(s => s.ContentManager).FirstOrDefault(File.Exists) ?? "" };
        return selected;
    }
}
