using System.Text.Json;

namespace ServerManager.Core;

public record Translation(string Ru, string En);
public static class UiText
{
    public static string Language { get; set; } = "ru";
    public static IReadOnlyDictionary<string, Translation> Entries { get; } = Read();
    private static readonly Dictionary<string, Translation> ByRussian = Entries.Values.ToDictionary(v => v.Ru);
    private static Dictionary<string, Translation> Read()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("ServerManager.Core.Translations.json") ?? throw new InvalidOperationException("Translation resources are missing.");
        using var json = JsonDocument.Parse(stream);
        var entries = new Dictionary<string, Translation>();
        foreach (var item in json.RootElement.EnumerateObject())
            if (!entries.TryAdd(item.Name, item.Value.Deserialize<Translation>()!))
                throw new InvalidOperationException($"Duplicate translation ID: {item.Name}");
        return entries;
    }
    public static string T(string russian, params object?[] args)
    {
        var text = Language == "en" && ByRussian.TryGetValue(russian, out var translation) ? translation.En : russian;
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
    }
}
