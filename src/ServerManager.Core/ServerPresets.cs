using System.Globalization;
using System.Text.Json;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public sealed record ServerPreset(string Id, string Name, string Track, string Layout, List<CreationCar> Cars, int StartMinutes = 1080,
    bool WeatherFx = false, int FuelRate = 0, int DamageRate = 0, int TyreWearRate = 0, int PracticeMinutes = 120, int? SlotLimit = null, bool InfinitePractice = true, bool EnableAi = true)
{
    public override string ToString() => Name;
    public string Summary => Track + (Layout.Length == 0 ? "" : " / " + Layout) + T(" · {0} слотов", Cars.Sum(c => c.Count));
}
public sealed record PresetLibrary(int Format, List<ServerPreset> Items);

public static class ServerPresets
{
    public static void Validate(ServerPreset p)
    {
        bool Id(string text, bool empty = false) => text != null && (empty && text.Length == 0 || text.Length is > 0 and <= 200 && text.Trim() == text && text is not ("." or "..")
            && !text.EndsWith('.') && text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && text.IndexOfAny(['/', '\\', ':']) < 0 && !text.Any(char.IsControl));
        if (p == null || !Guid.TryParseExact(p.Id, "N", out _) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || p.Name != p.Name.Trim() || p.Name.Any(char.IsControl)
            || !Id(p.Track) || !Id(p.Layout, true) || p.Cars == null || p.Cars.Count is < 1 or > 255
            || p.Cars.Any(c => c == null || !Id(c.Model) || !Id(c.Skin, true) || c.Role is not ("none" or "auto" or "fixed") || c.Count is < 1 or > 255)
            || p.Cars.Sum(c => (long)c.Count) > 255 || p.Cars.All(c => c.Role == "fixed") || p.StartMinutes is < 0 or > 1439
            || new[] { p.FuelRate, p.DamageRate, p.TyreWearRate }.Any(v => v is < 0 or > 500) || p.PracticeMinutes is < 1 or > 1440
            || p.SlotLimit is < 1 or > 255 || p.SlotLimit < p.Cars.Sum(c => c.Count)) throw new InvalidDataException(T("Некорректный пресет: проверьте название, контент, слоты и числовые параметры."));
    }
    private static bool Valid(PresetLibrary library)
    {
        if (library.Format != 1 || library.Items == null || library.Items.Count > 500 || library.Items.Select(p => p?.Id).Distinct().Count() != library.Items.Count) return false;
        try { foreach (var preset in library.Items) Validate(preset); return true; } catch (InvalidDataException) { return false; }
    }
    public static IReadOnlyList<ServerPreset> Load(string file)
    {
        foreach (var path in new[] { file, file + ".bak" })
            if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException(T("Библиотека пресетов слишком большая."));
        var value = SafeJsonStore.Load<PresetLibrary>(file, Valid);
        if (value == null && (File.Exists(file) || File.Exists(file + ".bak"))) throw new InvalidDataException(T("Библиотека пресетов повреждена или имеет неизвестный формат. Исходный файл сохранён."));
        return value?.Items.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() ?? [];
    }
    private static void Change(string file, Action<List<ServerPreset>> modify)
    {
        file = Path.GetFullPath(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var operation = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var items = Load(file).ToList(); modify(items);
        var library = new PresetLibrary(1, items);
        if (JsonSerializer.SerializeToUtf8Bytes(library, new JsonSerializerOptions { WriteIndented = true }).Length > 4 * 1024 * 1024) throw new IOException(T("Библиотека пресетов слишком большая."));
        SafeJsonStore.Save(file, library, Valid);
    }
    public static void Save(string file, ServerPreset preset)
    {
        Validate(preset);
        Change(file, items =>
        {
            if (items.Any(p => p.Id != preset.Id && p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))) throw new IOException(T("Пресет с таким названием уже существует."));
            items.RemoveAll(p => p.Id == preset.Id); items.Add(preset);
        });
    }
    public static void Delete(string file, string id) => Change(file, items => items.RemoveAll(p => p.Id == id));
    public static ServerPreset Capture(ServerProfile profile, string name)
    {
        int Number(string section, string key, int fallback)
        {
            var text = ConfigText.Get(profile.Ini, section, key, "__absent__");
            if (text == "__absent__") return fallback;
            return int.TryParse(text, out var n) ? n : throw new InvalidDataException(T("Некорректное значение {0}", key));
        }
        var minutes = 1080;
        if (double.TryParse(ConfigText.Get(profile.Ini, "SERVER", "SUN_ANGLE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var sun) && double.IsFinite(sun))
            minutes = (int)(((sun / 16 * 60 + 780) % 1440 + 1440) % 1440);
        var preset = new ServerPreset(Guid.NewGuid().ToString("N"), name.Trim(), profile.Track, profile.Layout,
            profile.Slots.Select(s => new CreationCar(s.Model, s.Skin, s.Ai, 1)).ToList(), minutes,
            ConfigText.GetYaml(profile.Extra, "EnableWeatherFx") == "true", Number("SERVER", "FUEL_RATE", 0), Number("SERVER", "DAMAGE_MULTIPLIER", 0),
            Number("SERVER", "TYRE_WEAR_RATE", 0), Math.Max(1, Number("PRACTICE", "TIME", 120)), Number("SERVER", "MAX_CLIENTS", profile.Slots.Count), ConfigText.Get(profile.Ini, "PRACTICE", "INFINITE", "0") == "1", ConfigText.GetYaml(profile.Extra, "EnableAi") == "true");
        Validate(preset); return preset;
    }
    public static void CheckContent(ServerPreset preset, ContentCatalog catalog)
    {
        Validate(preset); var missing = new List<string>();
        if (catalog.FindTrack(preset.Track, preset.Layout) == null) missing.Add(preset.Track + "/" + preset.Layout);
        foreach (var c in preset.Cars)
        {
            var car = catalog.FindCar(c.Model);
            if (car == null) missing.Add(c.Model);
            else if (c.Skin.Length > 0 && !(c.Skin == "generated" && c.Role != "none") && !car.Skins.Any(s => s.Id.Equals(c.Skin, StringComparison.OrdinalIgnoreCase))) missing.Add(c.Model + "/" + c.Skin);
        }
        if (missing.Count > 0) throw new InvalidDataException(T("Для пресета не найден контент:\n{0}\nУстановите его и обновите каталог. Черновик не изменён.", string.Join("\n", missing.Distinct())));
    }
}
