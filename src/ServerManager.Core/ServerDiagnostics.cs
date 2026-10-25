using System.Text.RegularExpressions;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public enum DiagnosticSeverity { Error, Warning, Information }
public enum DiagnosticActionKind { Setting, Slots, Car, Track, Folder, Save, ContentManager, Reload }
public sealed record DiagnosticAction(DiagnosticActionKind Kind, string Target, string LabelRu, string? Boundary = null)
{ public string Label => T(LabelRu); }
public sealed record DiagnosticFinding(string Code, DiagnosticSeverity Severity, string TitleRu, string DetailRu, string RemedyRu, IReadOnlyList<DiagnosticAction> Actions, params object?[] Arguments)
{
    public string Title => T(TitleRu, Arguments);
    public string Detail => T(DetailRu, Arguments);
    public string Remedy => T(RemedyRu, Arguments);
    public string SeverityLabel => T(Severity switch { DiagnosticSeverity.Error => "Ошибка", DiagnosticSeverity.Warning => "Предупреждение", _ => "Информация" });
    public string Color => Severity switch { DiagnosticSeverity.Error => "#FF5D43", DiagnosticSeverity.Warning => "#E2B866", _ => "#9199A9" };
    public string CopyText => $"[{Code}] {SeverityLabel}: {Title}\n{Detail}\n{Remedy}";
}
public sealed record DiagnosticSlot(string Model, string Skin, string Role, int Number);
public sealed record DiagnosticInput(string ServerPath, IReadOnlyDictionary<string, string> Settings, IReadOnlyList<DiagnosticSlot> Slots, string Extra, IReadOnlyDictionary<string, byte[]>? Baseline = null, bool? AiEnabled = null);
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticFinding> Findings, DateTimeOffset CheckedAt)
{
    public int Errors => Findings.Count(f => f.Severity == DiagnosticSeverity.Error);
    public int Warnings => Findings.Count(f => f.Severity == DiagnosticSeverity.Warning);
}

public static class ServerDiagnostics
{
    public static DiagnosticInput FromProfile(ServerProfile profile) => new(profile.Path,
        new[] { "NAME", "TRACK", "CONFIG_TRACK", "MAX_CLIENTS", "TCP_PORT", "UDP_PORT", "HTTP_PORT", "FUEL_RATE", "DAMAGE_MULTIPLIER", "TYRE_WEAR_RATE", "PASSWORD" }
            .ToDictionary(k => k, k => ConfigText.Get(profile.Ini, "SERVER", k, k is "FUEL_RATE" or "DAMAGE_MULTIPLIER" or "TYRE_WEAR_RATE" ? "0" : "")),
        profile.Slots.Select(s => new DiagnosticSlot(s.Model, s.Skin, s.Ai, s.Number)).ToArray(), profile.Extra, profile.OriginalBytes.ToDictionary(p => p.Key, p => p.Value.ToArray()));

    public static DiagnosticReport Analyze(DiagnosticInput input, ContentCatalog? catalog, bool probePorts = false, bool running = false, CancellationToken cancellation = default)
    {
        var findings = new List<DiagnosticFinding>();
        void Add(string code, DiagnosticSeverity severity, string title, string detail, string remedy, DiagnosticAction[] actions, params object?[] args)
            => findings.Add(new(code, severity, title, detail, remedy, actions, args));
        DiagnosticAction Setting(string key) => new(DiagnosticActionKind.Setting, key, "Открыть настройку");
        DiagnosticAction Slots() => new(DiagnosticActionKind.Slots, "", "Машины и слоты");
        DiagnosticAction Folder(string path, string boundary, string label) => new(DiagnosticActionKind.Folder, path, label, boundary);
        string Get(string key) => input.Settings.GetValueOrDefault(key, "");
        if (input.Baseline != null)
            foreach (var (name, bytes) in input.Baseline)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(input.ServerPath, "cfg", name);
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                    Add("config.external." + name, DiagnosticSeverity.Error, "Конфиг изменён вне менеджера: {0}", "Открытый редактор содержит другую версию файла.", "Перечитайте настройки перед сохранением или запуском. Несохранённые изменения потребуют подтверждения.", [new(DiagnosticActionKind.Reload, "", "Перечитать настройки")], name);
            }
        bool Id(string value, bool empty = false) => empty && value.Length == 0 || value is not ("." or "..") && Regex.IsMatch(value, @"^[\w .-]+$");
        foreach (var key in new[] { "NAME", "TRACK" })
            if (string.IsNullOrWhiteSpace(Get(key))) Add("setting.required." + key, DiagnosticSeverity.Error, "Не заполнено поле {0}", "Без этого значения нельзя подготовить конфигурацию сервера.", "Заполните поле и повторите проверку.", [Setting(key)], key);
        foreach (var key in input.Settings.Keys)
            if (Get(key).Contains('\n') || Get(key).Contains('\r')) Add("setting.newline." + key, DiagnosticSeverity.Error, "Недопустимый перенос строки: {0}", "Значение должно занимать одну строку.", "Удалите перенос строки.", [Setting(key)], key);
        if (Get("TRACK").Length > 0 && !Id(Get("TRACK")) || !Id(Get("CONFIG_TRACK"), true)) Add("track.invalid-id", DiagnosticSeverity.Error, "Некорректный ID трассы или варианта", "ID не должен содержать слеши или переходы между папками.", "Выберите трассу и вариант в каталоге.", [new(DiagnosticActionKind.Track, "", "Открыть каталог трасс")]);
        foreach (var key in new[] { "TCP_PORT", "UDP_PORT", "HTTP_PORT", "MAX_CLIENTS", "FUEL_RATE", "DAMAGE_MULTIPLIER", "TYRE_WEAR_RATE" })
        {
            if (key is "FUEL_RATE" or "DAMAGE_MULTIPLIER" or "TYRE_WEAR_RATE" && !input.Settings.ContainsKey(key)) continue;
            var port = key.EndsWith("_PORT"); var min = port || key == "MAX_CLIENTS" ? 1 : 0; var max = port ? 65535 : key == "MAX_CLIENTS" ? 255 : 500;
            if (!int.TryParse(Get(key), out var number) || number < min || number > max)
                Add("setting.range." + key, DiagnosticSeverity.Error, "Некорректное значение {0}", "Допустимый диапазон: {1}–{2}.", "Введите число в указанном диапазоне.", [Setting(key)], key, min, max);
        }
        if (int.TryParse(Get("TCP_PORT"), out var tcpValue) && int.TryParse(Get("HTTP_PORT"), out var httpValue) && tcpValue == httpValue) Add("ports.same", DiagnosticSeverity.Error, "TCP и HTTP используют один порт", "TCP и HTTP должны использовать разные порты.", "Измените один из портов.", [Setting("HTTP_PORT")]);
        if (input.Slots.Count is < 1 or > 255) Add("slots.count", DiagnosticSeverity.Error, "Некорректное число слотов", "Нужно от 1 до 255 слотов.", "Добавьте или удалите машины.", [Slots()]);
        if (int.TryParse(Get("MAX_CLIENTS"), out var limit) && limit < input.Slots.Count)
            Add("slots.limit", DiagnosticSeverity.Error, "Лимит меньше количества машин", "Слотов: {0}. Установленный лимит: {1}.", "Нажмите «По числу машин» в обзоре или увеличьте лимит.", [Setting("MAX_CLIENTS")], input.Slots.Count, limit);
        foreach (var slot in input.Slots)
        {
            if (!Id(slot.Model)) Add("car.invalid-id." + slot.Number, DiagnosticSeverity.Error, "Некорректная модель в CAR_{0}", "ID машины содержит недопустимые символы.", "Замените машину через каталог.", [Slots()], slot.Number);
            if (slot.Role is not ("none" or "fixed" or "auto")) Add("slot.role." + slot.Number, DiagnosticSeverity.Error, "Неизвестное назначение CAR_{0}", "Назначение слота не поддерживается менеджером.", "Выберите игрока, трафик или смешанный слот.", [Slots()], slot.Number);
        }
        if (!File.Exists(Path.Combine(input.ServerPath, "AssettoServer.exe"))) Add("runtime.missing", DiagnosticSeverity.Error, "Не найден AssettoServer.exe", "В папке выбранного сервера нет программы запуска.", "Установите AssettoServer или создайте сервер мастером.", [Folder(input.ServerPath, input.ServerPath, "Открыть папку сервера")]);
        cancellation.ThrowIfCancellationRequested();
        if (probePorts && !running && !findings.Any(f => f.Code.StartsWith("setting.range.") || f.Code == "ports.same"))
            foreach (var conflict in ServerPorts.FindConflicts(int.Parse(Get("TCP_PORT")), int.Parse(Get("HTTP_PORT")), int.Parse(Get("UDP_PORT"))))
                Add("port.unavailable." + conflict, DiagnosticSeverity.Error, "Порт недоступен: {0}", "Порт занят другим приложением или недоступен для привязки.", "Остановите использующее его приложение или выберите другой порт.", [Setting(conflict.StartsWith("UDP") ? "UDP_PORT" : conflict == "TCP " + int.Parse(Get("TCP_PORT")) ? "TCP_PORT" : "HTTP_PORT")], conflict);
        if (running && probePorts) Add("ports.running", DiagnosticSeverity.Information, "Сервер уже работает", "Занятость портов работающего сервера не считается конфликтом.", "Остановите сервер для отдельной проверки доступности портов.", []);
        if (findings.Any(f => f.Code is "track.invalid-id" or "setting.required.TRACK" || f.Code.StartsWith("car.invalid-id."))) return new(findings, DateTimeOffset.Now);
        if (catalog == null) Add("catalog.unavailable", DiagnosticSeverity.Information, "Каталог игры не загружен", "Локальные машины, скины и совпадение версий не проверены.", "Укажите папку Assetto Corsa для полной проверки.", [Setting("GAME_ROOT")]);
        var trackId = Get("TRACK"); var layout = Get("CONFIG_TRACK"); var track = catalog?.FindTrack(trackId, layout);
        var trackRoot = Path.Combine(input.ServerPath, "content", "tracks", trackId);
        if (track == null && !File.Exists(Path.Combine(trackRoot, layout, "data", "surfaces.ini")))
            Add("track.missing", DiagnosticSeverity.Warning, "Данные трассы не найдены", "Не найдены данные выбранной трассы в игре и на сервере.", "Проверьте ID, вариант и наличие контента. Специальные настройки CSP требуют отдельной проверки.", [new(DiagnosticActionKind.Track, trackId, "Открыть каталог трасс"), Setting("TRACK")]);
        if (track?.PitBoxes is > 0 && track.PitBoxes < input.Slots.Count)
            Add("track.pits", DiagnosticSeverity.Warning, "Слотов больше, чем пит-боксов", "У трассы {0} пит-боксов; слотов {1}.", "Проверьте допустимое число машин или уменьшите число слотов.", [Slots()], track.PitBoxes, input.Slots.Count);
        var alternatives = ConfigText.GetYaml(input.Extra, "EnableAlternativeCarChecksums") == "true";
        var ignoreChecksums = Regex.IsMatch(input.Extra, @"(?m)^IgnoreConfigurationErrors\s*:\s*(?:#[^\r\n]*)?\r?\n(?:[ \t]+[^\r\n]*\r?\n)*?[ \t]+MissingCarChecksums\s*:\s*true\b");
        foreach (var group in input.Slots.GroupBy(s => s.Model, StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested(); var model = group.Key; var car = catalog?.FindCar(model); var name = car?.Name ?? model;
            var serverCar = Path.Combine(input.ServerPath, "content", "cars", model); var serverData = Path.Combine(serverCar, "data.acd");
            var gameData = car == null ? null : Path.Combine(car.Directory, "data.acd");
            var actions = new List<DiagnosticAction> { Slots(), Folder(serverCar, input.ServerPath, "Папка машины на сервере") };
            if (car != null) actions.Add(new(DiagnosticActionKind.Car, model, "Машина в каталоге"));
            if (catalog != null && car == null) Add("car.missing-game." + model, DiagnosticSeverity.Warning, "Машина отсутствует в игре: {0}", "Для локального подключения нужна установленная версия этой машины.", "Установите мод в игру и обновите каталог.", [Folder(Path.Combine(catalog.GamePath, "content", "cars"), catalog.GamePath, "Открыть папку машин")], name);
            if (!File.Exists(serverData))
            {
                var alternate = alternatives && Directory.Exists(serverCar) && Directory.EnumerateFiles(serverCar, "data*.acd").Any();
                if (!alternate)
                {
                    var canPrepare = gameData != null && File.Exists(gameData);
                    Add("car.missing-data." + model, ignoreChecksums ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, "Нет data.acd на сервере: {0}",
                        canPrepare ? "Архив найден в игре и может быть подготовлен при сохранении." : "Для обычного запуска нужен архив data.acd. Распакованная папка data не заменяет серверный архив.",
                        canPrepare ? "Сохраните настройки для копирования недостающего файла." : "Установите нужную версию машины или упакуйте данные в Content Manager. Специальные исключения сервера проверяются отдельно.",
                        canPrepare ? [new(DiagnosticActionKind.Save, "", "Подготовить через сохранение"), .. actions] : actions.ToArray(), name);
                }
                else Add("car.alternative." + model, DiagnosticSeverity.Information, "Используются альтернативные данные: {0}", "На сервере включены альтернативные контрольные суммы машины.", "Убедитесь, что нужная версия клиента входит в разрешённые варианты.", actions.ToArray(), name);
            }
            if (car == null) continue;
            foreach (var slot in group)
                if (slot.Skin.Length > 0 && slot.Skin != "generated" && !car.Skins.Any(s => s.Id.Equals(slot.Skin, StringComparison.OrdinalIgnoreCase)))
                    Add("skin.missing." + slot.Number, DiagnosticSeverity.Warning, "Скин CAR_{0} не найден", "Скин {1} отсутствует у машины {2}.", "Выберите установленный скин или обновите мод.", [Slots(), new(DiagnosticActionKind.Car, model, "Машина в каталоге")], slot.Number, slot.Skin, name);
            if (!File.Exists(gameData!) && Directory.Exists(Path.Combine(car.Directory, "data")))
                Add("car.unpacked." + model, DiagnosticSeverity.Warning, "Данные машины распакованы: {0}", "В игре есть data без data.acd. Сервер проверяет архив, а не распакованную папку.", "Упакуйте данные в Content Manager и проверьте версии перед заменой файлов сервера.", [new(DiagnosticActionKind.ContentManager, "", "Открыть Content Manager"), Folder(car.Directory, catalog!.GamePath, "Папка машины в игре")], name);
            else if (File.Exists(gameData!) && File.Exists(serverData) && ServerBackups.Hash(gameData!, cancellation) != ServerBackups.Hash(serverData, cancellation))
                Add("car.checksum." + model, DiagnosticSeverity.Warning, "Разные данные машины: {0}", "data.acd в игре и на сервере различаются; подключение может завершиться ошибкой проверки файлов.", "Сравните версии мода. Перед ручной синхронизацией остановите сервер и сохраните бэкап.", [.. actions, Folder(car.Directory, catalog!.GamePath, "Папка машины в игре")], name);
        }
        if (track != null)
        {
            foreach (var relative in new[] { Path.Combine(layout, "data", "surfaces.ini"), layout.Length == 0 ? "models.ini" : $"models_{layout}.ini" })
            {
                cancellation.ThrowIfCancellationRequested(); var gameFile = Path.Combine(track.Directory, relative); var serverFile = Path.Combine(trackRoot, relative);
                if (File.Exists(gameFile) && File.Exists(serverFile) && ServerBackups.Hash(gameFile, cancellation) != ServerBackups.Hash(serverFile, cancellation))
                    Add("track.checksum." + relative, DiagnosticSeverity.Warning, "Разные файлы трассы: {0}", "Версии файла в игре и на сервере различаются.", "Проверьте версии трассы и настройки CSP. Менеджер не заменяет существующие файлы автоматически.", [Folder(Path.GetDirectoryName(serverFile)!, input.ServerPath, "Папка файла на сервере"), Folder(Path.GetDirectoryName(gameFile)!, catalog!.GamePath, "Папка файла в игре")], relative);
            }
        }
        if (input.AiEnabled ?? (ConfigText.GetYaml(input.Extra, "EnableAi") == "true"))
        {
            bool HasAi(string path)
            {
                var pending = new Stack<string>(); if (Directory.Exists(path)) pending.Push(path);
                while (pending.TryPop(out var current))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Directory.EnumerateFiles(current).Any(f => Path.GetExtension(f).ToLowerInvariant() is ".ai" or ".aip")) return true;
                    foreach (var child in Directory.EnumerateDirectories(current)) if (!File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
                }
                return false;
            }
            if (!HasAi(trackRoot) && (track == null || !HasAi(Path.Combine(track.Directory, layout, "ai")) && !HasAi(Path.Combine(track.Directory, "ai"))))
                Add("ai.missing", DiagnosticSeverity.Warning, "AI-сплайн не найден", "Трафик включён, но в проверенных папках нет файлов .ai или .aip.", "Выберите вариант с подходящим сплайном или настройте путь к нему. Наличие файла не гарантирует корректную работу трафика.", [Setting("TRACK"), Folder(trackRoot, input.ServerPath, "Папка трассы на сервере")]);
        }
        if (input.Slots.Count > 0 && input.Slots.All(s => s.Role == "fixed")) Add("slots.no-players", DiagnosticSeverity.Warning, "Нет слотов для игроков", "Все машины назначены трафиком.", "Измените назначение хотя бы одного слота.", [Slots()]);
        if (catalog?.Warnings.Count > 0) Add("catalog.metadata", DiagnosticSeverity.Information, "Замечания к описаниям контента", "Не удалось полностью прочитать описания: {0}. Это не проверка физики модов.", "При необходимости откройте каталог и проверьте проблемные описания.", [new(DiagnosticActionKind.Car, "", "Открыть каталог машин")], catalog.Warnings.Count);
        return new(findings.OrderBy(f => f.Severity).ToArray(), DateTimeOffset.Now);
    }
}
