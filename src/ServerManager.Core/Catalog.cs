using static ServerManager.Core.UiText;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace ServerManager.Core;

public record SkinInfo(string Id, string Name, string? Preview)
{
    public override string ToString() => Name;
}
public record CarInfo(string Id, string Name, string Brand, string Directory, string? Preview, IReadOnlyList<SkinInfo> Skins)
{
    public string Label => Name + " · " + Id;
    public string DefaultSkin => Skins.FirstOrDefault()?.Id ?? "";
}
public record TrackInfo(string Id, string Layout, string Name, string Directory, string? Preview, string? Outline, int? PitBoxes)
{
    public string Label => Name + (Layout.Length > 0 ? " · " + Layout : "");
    public string Key => Id + "/" + Layout;
}
public record TrackGroup(string Id, string Name, IReadOnlyList<TrackInfo> Layouts)
{
    public TrackInfo DefaultLayout => Layouts.FirstOrDefault(t => t.Layout.Length == 0)
        ?? Layouts.FirstOrDefault(t => t.Layout.Equals("main_layout", StringComparison.OrdinalIgnoreCase)) ?? Layouts[0];
}
public sealed class ContentCatalog
{
    public required string GamePath { get; init; }
    public List<CarInfo> Cars { get; } = [];
    public List<TrackInfo> Tracks { get; } = [];
    public IReadOnlyList<TrackGroup> TrackGroups => Tracks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => new TrackGroup(g.Key, TrackName(g.ToArray()), g.ToArray()))
        .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    private static string TrackName(TrackInfo[] layouts)
    {
        if (layouts.FirstOrDefault(t => t.Layout.Length == 0) is { } root) return root.Name;
        if (layouts.Length == 1) return layouts[0].Name;
        if (layouts.All(t => t.Name.Equals(layouts[0].Name, StringComparison.OrdinalIgnoreCase))) return layouts[0].Name;
        var prefix = layouts[0].Name;
        foreach (var layout in layouts.Skip(1))
        {
            var i = 0;
            while (i < prefix.Length && i < layout.Name.Length && char.ToUpperInvariant(prefix[i]) == char.ToUpperInvariant(layout.Name[i])) i++;
            prefix = prefix[..i];
        }
        // Only use a common name ending at a word or separator boundary.
        if (prefix.Length > 0 && !char.IsWhiteSpace(prefix[^1]) && !"-–—|/·".Contains(prefix[^1]))
            prefix = prefix[..Math.Max(0, prefix.LastIndexOf(' '))];
        var name = prefix.Trim().TrimEnd('-', '–', '—', '|', '/', '·').Trim();
        return name.Length > 0 ? name : layouts[0].Id;
    }
    public List<string> Warnings { get; } = [];
    public CarInfo? FindCar(string id) => Cars.Find(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public TrackInfo? FindTrack(string id, string layout) => Tracks.Find(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && t.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase));
    private static string? ImageAt(string folder, params string[] names) => names.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);
    private static Dictionary<string, string> Metadata(string path, List<string> warnings)
    {
        if (!File.Exists(path)) return [];
        try
        {
            // Some AC mods store literal newlines in descriptions; escape controls in strings only.
            using var doc = JsonDocument.Parse(EscapeStringControls(ConfigText.Read(path)), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString());
        }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException) { warnings.Add(path + ": " + e.Message); return []; }
    }
    private static string EscapeStringControls(string text)
    {
        var result = new StringBuilder(text.Length); var quoted = false; var escaped = false;
        foreach (var c in text)
        {
            if (quoted && c < ' ') { result.Append("\\u").Append(((int)c).ToString("x4")); escaped = false; continue; }
            result.Append(c);
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true; continue; }
            if (c == '"') quoted = !quoted;
        }
        return result.ToString();
    }
    public static ContentCatalog Load(string gamePath)
    {
        if (!Directory.Exists(Path.Combine(gamePath, "content", "cars")) || !Directory.Exists(Path.Combine(gamePath, "content", "tracks")))
            throw new DirectoryNotFoundException(T("Выберите корневую папку Assetto Corsa с content/cars и content/tracks."));
        var catalog = new ContentCatalog { GamePath = Path.GetFullPath(gamePath) };
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(gamePath, "content", "cars")))
        {
            var id = Path.GetFileName(dir); var meta = Metadata(Path.Combine(dir, "ui", "ui_car.json"), catalog.Warnings);
            var skins = new List<SkinInfo>(); var skinsPath = Path.Combine(dir, "skins");
            if (Directory.Exists(skinsPath))
                foreach (var skinPath in Directory.EnumerateDirectories(skinsPath))
                {
                    var skinId = Path.GetFileName(skinPath); var skinMeta = Metadata(Path.Combine(skinPath, "ui_skin.json"), catalog.Warnings);
                    skins.Add(new(skinId, skinMeta.GetValueOrDefault("skinname", skinId), ImageAt(skinPath, "preview.jpg", "preview.png", "livery.png")));
                }
            catalog.Cars.Add(new(id, meta.GetValueOrDefault("name", id), meta.GetValueOrDefault("brand", ""), dir,
                skins.FirstOrDefault(s => s.Preview != null)?.Preview ?? ImageAt(Path.Combine(dir, "ui"), "preview.jpg", "preview.png", "badge.png"), skins.OrderBy(s => s.Name).ToArray()));
        }
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(gamePath, "content", "tracks")))
        {
            var id = Path.GetFileName(dir); var ui = Path.Combine(dir, "ui");
            if (File.Exists(Path.Combine(ui, "ui_track.json"))) AddTrack(dir, ui, "");
            if (Directory.Exists(ui))
                foreach (var layoutUi in Directory.EnumerateDirectories(ui))
                    if (File.Exists(Path.Combine(layoutUi, "ui_track.json"))) AddTrack(dir, layoutUi, Path.GetFileName(layoutUi));
            void AddTrack(string trackDir, string metadataDir, string layout)
            {
                var meta = Metadata(Path.Combine(metadataDir, "ui_track.json"), catalog.Warnings);
                int? pits = int.TryParse(meta.GetValueOrDefault("pitboxes"), out var count) ? count : null;
                catalog.Tracks.Add(new(id, layout, meta.GetValueOrDefault("name", id), trackDir, ImageAt(metadataDir, "preview.png", "preview.jpg"), ImageAt(metadataDir, "outline.png"), pits));
            }
        }
        catalog.Cars.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        catalog.Tracks.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Label, b.Label));
        return catalog;
    }
    public List<string> Validate(ServerProfile profile, Dictionary<string, string> texts)
    {
        var messages = new List<string>();
        var trackId = ConfigText.Get(texts["server_cfg.ini"], "SERVER", "TRACK"); var layout = ConfigText.Get(texts["server_cfg.ini"], "SERVER", "CONFIG_TRACK");
        var track = FindTrack(trackId, layout);
        var serverTrack = Path.Combine(profile.Path, "content", "tracks", trackId, layout);
        if (track == null && !File.Exists(Path.Combine(serverTrack, "data", "surfaces.ini"))) messages.Add(T("Не найдены данные выбранной трассы в игре и на сервере."));
        if (track?.PitBoxes is > 0 && track.PitBoxes < profile.Slots.Count) messages.Add(T("У трассы {0} пит-боксов; слотов {1}. Проверьте допустимое число машин.", track.PitBoxes, profile.Slots.Count));
        foreach (var slot in profile.Slots)
        {
            var car = FindCar(slot.Model);
            if (car == null) messages.Add(T("В игре отсутствует машина: ") + slot.Model);
            else if (!string.IsNullOrEmpty(slot.Skin) && slot.Skin != "generated" && !car.Skins.Any(s => s.Id.Equals(slot.Skin, StringComparison.OrdinalIgnoreCase))) messages.Add(T("Нет скина {0} у {1}.", slot.Skin, car.Name));
            var serverData = Path.Combine(profile.Path, "content", "cars", slot.Model, "data.acd");
            if (!File.Exists(serverData) && (car == null || !File.Exists(Path.Combine(car.Directory, "data.acd")))) messages.Add(T("Нет data.acd для сервера: ") + slot.Model + T(". Упаковка распакованных данных пока не поддерживается."));
        }
        foreach (var model in profile.Slots.Select(s => s.Model).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var car = FindCar(model);
            if (car == null) continue;
            var gameData = Path.Combine(car.Directory, "data.acd");
            var serverData = Path.Combine(profile.Path, "content", "cars", model, "data.acd");
            if (!File.Exists(gameData) && Directory.Exists(Path.Combine(car.Directory, "data")))
                messages.Add(T("У {0} в игре распакованная папка data без data.acd. Упакуйте данные в Content Manager и скопируйте новый data.acd на сервер; сервер проверяет архив data.acd, а не папку data.", model));
            else if (File.Exists(gameData) && File.Exists(serverData) && !SameFile(gameData, serverData))
                messages.Add(T("Не совпадает data.acd машины {0} в игре и на сервере. Синхронизируйте версии и перезапустите сервер; иначе подключение может завершиться ошибкой проверки файлов.", model));
        }
        if (track != null)
        {
            foreach (var relative in new[] { Path.Combine(layout, "data", "surfaces.ini"), layout.Length == 0 ? "models.ini" : $"models_{layout}.ini" })
            {
                var gameFile = Path.Combine(track.Directory, relative);
                var serverFile = Path.Combine(profile.Path, "content", "tracks", trackId, relative);
                if (File.Exists(gameFile) && File.Exists(serverFile) && !SameFile(gameFile, serverFile))
                    messages.Add(T("Не совпадает файл трассы {0} в игре и на сервере. Проверьте версии трассы и настройки CSP перед синхронизацией.", relative));
            }
        }
        if (ConfigText.GetYaml(texts["extra_cfg.yml"], "EnableAi") == "true")
        {
            var serverRoot = Path.Combine(profile.Path, "content", "tracks", trackId);
            bool HasAi(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(f => Path.GetExtension(f) is ".ai" or ".aip");
            var gameLayoutAi = track == null ? "" : Path.Combine(track.Directory, layout, "ai");
            var gameRootAi = track == null ? "" : Path.Combine(track.Directory, "ai");
            if (!HasAi(serverRoot) && !HasAi(gameLayoutAi) && !HasAi(gameRootAi)) messages.Add(T("Трафик включён, но файлы AI не найдены."));
        }
        if (profile.Slots.All(s => s.Ai == "fixed")) messages.Add(T("Нет машин для игроков: все слоты заняты трафиком."));
        return messages.Distinct().ToList();
    }
    private static bool SameFile(string first, string second)
    {
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return a.Length == b.Length && SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
    public List<CopyPlan> PrepareMissingContent(ServerProfile profile, Dictionary<string, string> texts)
    {
        var plans = new Dictionary<string, CopyPlan>(StringComparer.OrdinalIgnoreCase);
        void Add(string source, string relative)
        {
            if (File.Exists(source) && !File.Exists(Path.Combine(profile.Path, relative))) plans[relative] = new(source, relative);
        }
        foreach (var model in profile.Slots.Select(s => s.Model).Distinct())
        {
            var car = FindCar(model);
            if (car != null) Add(Path.Combine(car.Directory, "data.acd"), Path.Combine("content", "cars", model, "data.acd"));
        }
        var trackId = ConfigText.Get(texts["server_cfg.ini"], "SERVER", "TRACK"); var layout = ConfigText.Get(texts["server_cfg.ini"], "SERVER", "CONFIG_TRACK");
        var track = FindTrack(trackId, layout);
        if (track != null)
        {
            Add(Path.Combine(track.Directory, layout, "data", "surfaces.ini"), Path.Combine("content", "tracks", trackId, layout, "data", "surfaces.ini"));
            foreach (var aiDir in new[] { Path.Combine(track.Directory, "ai"), Path.Combine(track.Directory, layout, "ai") }.Distinct())
            {
                if (!Directory.Exists(aiDir)) continue;
                foreach (var file in Directory.EnumerateFiles(aiDir, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) is ".ai" or ".aip"))
                    Add(file, Path.Combine("content", "tracks", trackId, Path.GetRelativePath(track.Directory, file)));
            }
        }
        return plans.Values.ToList();
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string? FindGame()
    {
        var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") };
        using var steamKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steamKey?.GetValue("SteamPath") is string steam) roots.Insert(0, steam);
        foreach (var root in roots.ToArray())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        return roots.Distinct().Select(r => Path.Combine(r, "steamapps", "common", "assettocorsa")).FirstOrDefault(Directory.Exists);
    }
}
