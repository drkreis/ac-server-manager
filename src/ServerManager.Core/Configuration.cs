using static ServerManager.Core.UiText;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerManager.Core;

public static class ConfigText
{
    public static string Read(string path) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
    public static string Get(string text, string section, string key, string fallback = "")
    {
        var current = "";
        foreach (var line in text.Split('\n'))
        {
            var header = Regex.Match(line.TrimEnd('\r'), @"^\s*\[([^\]]+)\]\s*(?:[;#].*)?$");
            if (header.Success) { current = header.Groups[1].Value; continue; }
            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line, @"^\s*" + Regex.Escape(key) + @"\s*=(.*)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return fallback;
    }
    public static string Set(string text, string section, string key, string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) throw new InvalidOperationException(T("{0}: перенос строки недопустим.", key));
        if (Get(text, section, key, "\u0000") == value) return text;
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var start = -1; var end = lines.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            var header = Regex.Match(lines[i], @"^\s*\[([^\]]+)\]\s*(?:[;#].*)?$");
            if (!header.Success) continue;
            if (start >= 0) { end = i; break; }
            if (header.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase)) start = i;
        }
        if (start < 0) { lines.Add($"[{section}]"); lines.Add($"{key}={value}"); }
        else
        {
            var found = false;
            for (var i = start + 1; i < end; i++)
            {
                var match = Regex.Match(lines[i], @"^(\s*" + Regex.Escape(key) + @"\s*=).*$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                lines[i] = match.Groups[1].Value + value; found = true;
            }
            if (!found) lines.Insert(end, $"{key}={value}");
        }
        return string.Join(newline, lines);
    }
    public static string GetYaml(string text, string key, string fallback = "false")
    {
        var match = Regex.Match(text, @"(?m)^" + Regex.Escape(key) + @":[ \t]*([^\r\n#]*)");
        return match.Success ? match.Groups[1].Value.Trim() : fallback;
    }
    public static string SetYaml(string text, string key, bool value)
    {
        var val = value ? "true" : "false";
        var pattern = @"(?m)^(" + Regex.Escape(key) + @":)[^\r\n]*";
        var matches = Regex.Matches(text, pattern);
        if (matches.Count > 1) throw new InvalidOperationException(T("Повторяющийся YAML-параметр: {0}", key));
        if (matches.Count == 1)
        {
            if (GetYaml(text, key) == val) return text;
            return Regex.Replace(text, pattern, m => m.Groups[1].Value + " " + val + (m.Value.Contains('#') ? " " + m.Value[m.Value.IndexOf('#')..] : ""));
        }
        if (Regex.IsMatch(text, @"(?m)^---\s*$")) throw new InvalidOperationException(T("Добавление ключа в многодокументный YAML пока не поддерживается."));
        return text.TrimEnd('\r', '\n') + (text.Contains("\r\n") ? "\r\n" : "\n") + key + ": " + val + "\n";
    }
    public static List<(string Name, string Text)> Sections(string text)
    {
        var headers = Regex.Matches(text, @"(?m)^\s*\[([^\]\r\n]+)\][^\r\n]*(?:\r?\n|$)");
        var result = new List<(string, string)>();
        if (headers.Count == 0) return result;
        for (var i = 0; i < headers.Count; i++)
        {
            var h = headers[i]; var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            result.Add((h.Groups[1].Value, text[h.Index..end]));
        }
        return result;
    }
}

public sealed class Slot : INotifyPropertyChanged
{
    private string model = "", skin = "", ai = "none";
    private int number;
    public int Number { get => number; internal set { if (number == value) return; number = value; Changed(nameof(Number)); Changed(nameof(EntrySection)); } }
    public string EntrySection => "CAR_" + Number;
    public string OriginalSection { get; init; } = "CAR_0";
    public string Raw { get; init; } = "[CAR_0]\nMODEL=\nSKIN=\nAI=none\nGUID=\nBALLAST=0\nRESTRICTOR=0\n";
    public string Model { get => model; set { model = value; Changed(nameof(Model)); } }
    public string Skin { get => skin; set { skin = value; Changed(nameof(Skin)); } }
    public string Ai { get => ai; set { ai = value; Changed(nameof(Ai)); Changed(nameof(Role)); } }
    public string Role => Ai == "fixed" ? T("Трафик") : Ai == "auto" ? T("Игрок / AI") : T("Игрок");
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public Slot Duplicate() => new() { Model = Model, Skin = Skin, Ai = Ai, OriginalSection = OriginalSection,
        Raw = ConfigText.Set(ConfigText.Set(ConfigText.Set(Raw, OriginalSection, "GUID", ""), OriginalSection, "DRIVERNAME", ""), OriginalSection, "TEAM", "") };
    public string Serialize(int index)
    {
        var text = ConfigText.Set(ConfigText.Set(ConfigText.Set(Raw, OriginalSection, "MODEL", Model), OriginalSection, "SKIN", Skin), OriginalSection, "AI", Ai);
        return Regex.Replace(text, @"(?m)^(\s*)\[" + Regex.Escape(OriginalSection) + @"\]", "$1[CAR_" + index + "]", RegexOptions.IgnoreCase).TrimEnd('\r', '\n') + "\r\n\r\n";
    }
}

public sealed class ServerProfile
{
    private string path = "", ini = "", extra = "", entries = "";
    public ServerProfile() => Slots.CollectionChanged += (_, _) => { for (var i = 0; i < Slots.Count; i++) Slots[i].Number = i; };
    public required string Path { get => path; init => path = value; }
    public string Folder => System.IO.Path.GetFileName(Path);
    public string Name => ConfigText.Get(Ini, "SERVER", "NAME", Folder);
    public required string Ini { get => ini; init => ini = value; }
    public required string Extra { get => extra; init => extra = value; }
    public required string Entries { get => entries; init => entries = value; }
    public required Dictionary<string, byte[]> OriginalBytes { get; init; }
    public ObservableCollection<Slot> Slots { get; } = [];
    public string Track => ConfigText.Get(Ini, "SERVER", "TRACK");
    public string Layout => ConfigText.Get(Ini, "SERVER", "CONFIG_TRACK");
    public int PlayerSlots => Slots.Count(s => s.Ai != "fixed");
    public int TrafficSlots => Slots.Count(s => s.Ai == "fixed");
    public string Summary => T("{0} игроков · {1} AI", PlayerSlots, TrafficSlots);
    public static ServerProfile Load(string path)
    {
        var files = new[] { "server_cfg.ini", "extra_cfg.yml", "entry_list.ini" };
        var bytes = files.ToDictionary(n => n, n => File.ReadAllBytes(System.IO.Path.Combine(path, "cfg", n)));
        string Text(string key) => new UTF8Encoding(false, true).GetString(bytes[key]).TrimStart('\uFEFF');
        var profile = new ServerProfile { Path = System.IO.Path.GetFullPath(path), Ini = Text(files[0]), Extra = Text(files[1]), Entries = Text(files[2]), OriginalBytes = bytes };
        foreach (var (name, text) in ConfigText.Sections(profile.Entries).Where(s => Regex.IsMatch(s.Name, @"^CAR_\d+$", RegexOptions.IgnoreCase)))
            profile.Slots.Add(new Slot { OriginalSection = name, Raw = text, Model = ConfigText.Get(text, name, "MODEL"), Skin = ConfigText.Get(text, name, "SKIN"), Ai = ConfigText.Get(text, name, "AI", "none") });
        if (profile.Slots.Select(s => s.OriginalSection).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.Slots.Count) throw new InvalidOperationException(T("Повторяющиеся секции CAR_n."));
        return profile;
    }
    public static List<ServerProfile> Discover(string root, ICollection<string>? errors = null)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(T("Папка серверов не найдена."));
        var result = new List<ServerProfile>();
        foreach (var path in new[] { root }.Concat(Directory.EnumerateDirectories(root)))
        {
            if (!File.Exists(System.IO.Path.Combine(path, "cfg", "server_cfg.ini"))) continue;
            try { result.Add(Load(path)); } catch (Exception e) { errors?.Add($"{System.IO.Path.GetFileName(path)}: {e.Message}"); }
        }
        return result;
    }
    public Dictionary<string, string> Build(Dictionary<string, string> settings, bool ai, bool weather)
    {
        if (Slots.Count is < 1 or > 255) throw new InvalidOperationException(T("Нужно от 1 до 255 слотов."));
        var ini = Ini;
        foreach (var (key, value) in settings)
        {
            if (key is "NAME" or "TRACK" && string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(T("{0} не может быть пустым.", key));
            if (key.EndsWith("_PORT") || key is "MAX_CLIENTS" or "FUEL_RATE" or "DAMAGE_MULTIPLIER" or "TYRE_WEAR_RATE")
            {
                var min = key.EndsWith("_PORT") || key == "MAX_CLIENTS" ? 1 : 0;
                var max = key.EndsWith("_PORT") ? 65535 : key == "MAX_CLIENTS" ? 255 : 500;
                if (!int.TryParse(value, out var number) || number < min || number > max) throw new InvalidOperationException(T("{0}: допустимо {1}–{2}.", key, min, max));
            }
            ini = ConfigText.Set(ini, "SERVER", key, value);
        }
        if (int.Parse(ConfigText.Get(ini, "SERVER", "MAX_CLIENTS", "0")) < Slots.Count) throw new InvalidOperationException(T("Лимит слотов меньше количества машин. Нажмите «По числу машин»."));
        foreach (var slot in Slots)
        {
            if (!Regex.IsMatch(slot.Model, @"^[\w .-]+$") || slot.Model is "." or "..") throw new InvalidOperationException(T("Некорректный ID модели: ") + slot.Model);
            if (slot.Ai is not ("none" or "fixed" or "auto")) throw new InvalidOperationException(T("Неизвестная роль AI."));
        }
        // CARS is derived from the current slot list, including additions and removals.
        ini = ConfigText.Set(ini, "SERVER", "CARS", string.Join(';', Slots.Select(s => s.Model).Distinct()));
        var oldSections = ConfigText.Sections(Entries);
        var firstHeader = Regex.Match(Entries, @"(?m)^\s*\[");
        var prefix = firstHeader.Success ? Entries[..firstHeader.Index] : Entries;
        var serialized = string.Concat(Slots.Select((s, i) => s.Serialize(i)));
        var output = new StringBuilder(prefix); var inserted = false;
        foreach (var section in oldSections)
        {
            if (Regex.IsMatch(section.Name, @"^CAR_\d+$", RegexOptions.IgnoreCase))
            { if (!inserted) { output.Append(serialized); inserted = true; } }
            else output.Append(section.Text);
        }
        if (!inserted) output.Append(serialized);
        return new() { ["server_cfg.ini"] = ini, ["entry_list.ini"] = output.ToString(),
            ["extra_cfg.yml"] = ConfigText.SetYaml(ConfigText.SetYaml(Extra, "EnableAi", ai), "EnableWeatherFx", weather) };
    }
    public Dictionary<string, string> SavedTexts => new() { ["server_cfg.ini"] = Ini, ["extra_cfg.yml"] = Extra, ["entry_list.ini"] = Entries };
    public void Relocate(string destination) => path = System.IO.Path.GetFullPath(destination);
    // Refresh the disk baseline without replacing slot objects bound to the editor.
    public void AcceptSaved(ServerProfile saved)
    {
        path = saved.Path; ini = saved.Ini; extra = saved.Extra; entries = saved.Entries;
        OriginalBytes.Clear();
        foreach (var pair in saved.OriginalBytes) OriginalBytes.Add(pair.Key, pair.Value);
    }
    public string? Save(Dictionary<string, string> texts, IReadOnlyList<CopyPlan>? content = null)
    {
        foreach (var (file, original) in OriginalBytes)
            if (!File.ReadAllBytes(System.IO.Path.Combine(Path, "cfg", file)).SequenceEqual(original)) throw new InvalidOperationException(T("Файлы изменились после открытия. Перечитайте сервер."));
        byte[] Encode(string file, string text)
        {
            var encoding = new UTF8Encoding(OriginalBytes[file].Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
            return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        }
        string Destination(CopyPlan plan)
        {
            var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, plan.RelativeDestination));
            if (!destination.StartsWith(Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(T("Некорректный путь серверного контента."));
            return destination;
        }
        bool ContentChanged(CopyPlan plan)
        {
            var destination = Destination(plan);
            if (!File.Exists(destination)) return true;
            using var source = File.OpenRead(plan.Source); using var target = File.OpenRead(destination);
            return source.Length != target.Length || !System.Security.Cryptography.SHA256.HashData(source).SequenceEqual(System.Security.Cryptography.SHA256.HashData(target));
        }
        var changedTexts = texts.Where(pair => !Encode(pair.Key, pair.Value).SequenceEqual(OriginalBytes[pair.Key])).ToDictionary();
        var changedContent = (content ?? []).Where(ContentChanged).ToList();
        if (changedTexts.Count == 0 && changedContent.Count == 0) return null;
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var backup = System.IO.Path.Combine(Path, "backups", id); Directory.CreateDirectory(backup);
        foreach (var file in OriginalBytes.Keys) File.Copy(System.IO.Path.Combine(Path, "cfg", file), System.IO.Path.Combine(backup, file));
        var writes = new List<(string Destination, string? Backup)>();
        var staged = new List<(string Temp, string Destination, string? Backup)>();
        try
        {
            foreach (var plan in changedContent)
            {
                var dest = Destination(plan);
                string? old = null;
                if (File.Exists(dest)) { old = System.IO.Path.Combine(backup, "content", plan.RelativeDestination); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(old)!); File.Copy(dest, old); }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                var temp = dest + "." + id + ".tmp"; staged.Add((temp, dest, old)); File.Copy(plan.Source, temp);
            }
            foreach (var (file, text) in changedTexts)
            {
                var dest = System.IO.Path.Combine(Path, "cfg", file); var temp = dest + "." + id + ".tmp";
                staged.Add((temp, dest, System.IO.Path.Combine(backup, file)));
                File.WriteAllBytes(temp, Encode(file, text));
            }
            foreach (var entry in staged)
            {
                if (File.Exists(entry.Destination)) File.Replace(entry.Temp, entry.Destination, null);
                else File.Move(entry.Temp, entry.Destination);
                writes.Add((entry.Destination, entry.Backup));
            }
            return backup;
        }
        catch
        {
            foreach (var (dest, old) in writes.AsEnumerable().Reverse())
            { if (old != null) File.Copy(old, dest, true); else File.Delete(dest); }
            throw;
        }
        finally { foreach (var item in staged) if (File.Exists(item.Temp)) File.Delete(item.Temp); }
    }
}

public record CopyPlan(string Source, string RelativeDestination);
