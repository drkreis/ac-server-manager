using System.Text.Json;

namespace ServerManager.Core;

public static class ServerReadiness
{
    public static bool Matches(string json, string track, string layout)
    {
        if (json.Length > 65536) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("track", out var value) && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), track + (layout.Length > 0 ? "-" + layout : ""), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }
}
