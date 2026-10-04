using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public record CreationCar(string Model, string Skin, string Role, int Count);
public sealed record ServerCreationRequest
{
    public required string ServersRoot { get; init; }
    public required string Folder { get; init; }
    public required string Name { get; init; }
    public required string Track { get; init; }
    public string Layout { get; init; } = "";
    public string AiSpline { get; init; } = "";
    public required IReadOnlyList<CreationCar> Cars { get; init; }
    public int TcpPort { get; init; } = 9600;
    public int UdpPort { get; init; } = 9600;
    public int HttpPort { get; init; } = 8081;
    public string Password { get; init; } = "";
    public string AdminPassword { get; init; } = "";
    public int StartMinutes { get; init; } = 18 * 60;
    public bool WeatherFx { get; init; }
    public bool PublishToLobby { get; init; }
    public bool Ai => Cars.Any(c => c.Role != "none");
}

public static class ServerCreation
{
    private static readonly string[] RequiredRuntime = ["AssettoServer.exe", "steam_api64.dll", "csp_xxhash3.dll"];
    public static string Destination(string root, string folder)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException(T("Укажите папку серверов."));
        if (string.IsNullOrWhiteSpace(folder) || folder.Length > 80 || folder.Trim() != folder || folder.EndsWith('.')
            || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.IndexOfAny(['/', '\\', ':']) >= 0
            || folder.Any(c => c < 32) || folder is "." or ".."
            || Regex.IsMatch(folder, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException(T("Введите допустимое имя новой папки без слешей и специальных символов."));
        var target = Path.GetFullPath(Path.Combine(root, folder));
        if (Directory.Exists(target) || File.Exists(target)) throw new InvalidOperationException(T("Папка уже существует. Выберите другое имя; существующие серверы не перезаписываются."));
        CheckNoLinks(Path.GetFullPath(root));
        return target;
    }
    public static void Validate(ServerCreationRequest request, ContentCatalog catalog)
    {
        var target = Destination(request.ServersRoot, request.Folder);
        if (Within(target, catalog.GamePath) || Within(catalog.GamePath, target)) throw new InvalidOperationException(T("Создавайте сервер вне папки Assetto Corsa."));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || request.Name.Any(c => c < 32)) throw new InvalidOperationException(T("Введите название сервера (до 120 символов)."));
        if (request.Password.Any(c => c < 32) || request.AdminPassword.Any(c => c < 32)) throw new InvalidOperationException(T("Пароль не должен содержать переносы строк."));
        if (request.AdminPassword.Length is > 0 and < 8) throw new InvalidOperationException(T("Пароль администратора должен содержать не менее 8 символов."));
        foreach (var port in new[] { request.TcpPort, request.UdpPort, request.HttpPort })
            if (port is < 1 or > 65535) throw new InvalidOperationException(T("Порты должны быть в диапазоне 1–65535."));
        if (request.TcpPort == request.HttpPort) throw new InvalidOperationException(T("TCP и HTTP должны использовать разные порты."));
        if (request.StartMinutes is < 0 or > 1439) throw new InvalidOperationException(T("Введите время в формате HH:mm."));
        var track = catalog.FindTrack(request.Track, request.Layout) ?? throw new InvalidOperationException(T("Выберите трассу и вариант из установленного контента."));
        if (!File.Exists(Path.Combine(track.Directory, track.Layout, "data", "surfaces.ini"))) throw new InvalidOperationException(T("У выбранного варианта нет data/surfaces.ini. Проверьте установку трассы."));
        var count = request.Cars.Sum(c => (long)c.Count);
        if (request.Cars.Count == 0 || count is < 1 or > 255 || request.Cars.Any(c => c.Count is < 1 or > 255)) throw new InvalidOperationException(T("Добавьте машины: всего от 1 до 255 слотов."));
        if (track.PitBoxes is > 0 && count > track.PitBoxes) throw new InvalidOperationException(T("У трассы {0} пит-боксов; слотов {1}. Уменьшите количество слотов.", track.PitBoxes, count));
        if (request.Cars.All(c => c.Role == "fixed")) throw new InvalidOperationException(T("Добавьте хотя бы один слот для игрока."));
        foreach (var choice in request.Cars)
        {
            if (choice.Role is not ("none" or "fixed" or "auto")) throw new InvalidOperationException(T("Неизвестная роль AI."));
            var car = catalog.FindCar(choice.Model) ?? throw new InvalidOperationException(T("В игре отсутствует машина: ") + choice.Model);
            if (!File.Exists(Path.Combine(car.Directory, "data.acd"))) throw new InvalidOperationException(T("Для {0} отсутствует data.acd. Упакуйте данные машины в Content Manager.", car.Name));
            if (choice.Skin != "" && !(choice.Skin == "generated" && choice.Role != "none") && !car.Skins.Any(s => s.Id.Equals(choice.Skin, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(T("Нет скина {0} у {1}.", choice.Skin, car.Name));
        }
        if (request.Ai && request.AiSpline.Length > 0 && (!File.Exists(request.AiSpline) || Path.GetExtension(request.AiSpline).ToLowerInvariant() is not (".ai" or ".aip")))
            throw new InvalidOperationException(T("Выберите существующий файл сплайна .ai или .aip."));
        if (request.Ai && request.AiSpline.Length == 0 && !new[] { Path.Combine(track.Directory, track.Layout, "ai"), Path.Combine(track.Directory, "ai") }
            .Any(dir => File.Exists(Path.Combine(dir, "fast_lane.ai")) || File.Exists(Path.Combine(dir, "fast_lane.aip"))))
            throw new InvalidOperationException(T("Для трафика нужен fast_lane.ai или fast_lane.aip выбранной трассы. Добавьте сплайн или уберите AI-слоты."));
    }
    public static ServerProfile Create(ServerCreationRequest request, ContentCatalog catalog, string runtimeSource, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        Validate(request, catalog);
        var target = Destination(request.ServersRoot, request.Folder);
        var root = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".acsm-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(staging, "plugins"));
            progress?.Report(T("Устанавливаю AssettoServer…"));
            InstallRuntime(runtimeSource, staging, cancellation);
            progress?.Report(T("Создаю конфиги и копирую данные контента…"));
            var texts = BuildConfiguration(request);
            Directory.CreateDirectory(Path.Combine(staging, "cfg"));
            foreach (var (file, text) in texts) File.WriteAllText(Path.Combine(staging, "cfg", file), text, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "welcome.txt"), "Created with AC Server Manager. Powered by AssettoServer.\n", new UTF8Encoding(false));
            var profile = ServerProfile.Load(staging);
            var plans = catalog.PrepareMissingContent(profile, texts);
            var track = catalog.FindTrack(request.Track, request.Layout)!;
            var modelsName = request.Layout.Length == 0 ? "models.ini" : "models_" + request.Layout + ".ini";
            var models = Path.Combine(track.Directory, modelsName);
            if (File.Exists(models)) plans.Add(new(models, Path.Combine("content", "tracks", request.Track, modelsName)));
            if (request.Ai && request.AiSpline.Length > 0)
            {
                var relative = Path.Combine("content", "tracks", request.Track, request.Layout, "ai", "fast_lane" + Path.GetExtension(request.AiSpline).ToLowerInvariant());
                // A custom spline must take precedence over either game spline format.
                var aiFolder = Path.GetDirectoryName(relative)!;
                plans.RemoveAll(p => Path.GetDirectoryName(p.RelativeDestination)?.Equals(aiFolder, StringComparison.OrdinalIgnoreCase) == true
                    && Path.GetFileNameWithoutExtension(p.RelativeDestination).Equals("fast_lane", StringComparison.OrdinalIgnoreCase));
                plans.Add(new(request.AiSpline, relative));
            }
            foreach (var plan in plans)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = SafeDestination(staging, plan.RelativeDestination);
                CheckNoLinks(plan.Source);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(plan.Source, path, false);
            }
            cancellation.ThrowIfCancellationRequested();
            // Publish only when all files are ready. Directory.Move cannot overwrite a server.
            CheckNoLinks(root);
            PublishDirectory(staging, target, cancellation);
            return ServerProfile.Load(target);
        }
        finally
        {
            // Only remove the private staging directory owned by this operation.
            if (Directory.Exists(staging) && Path.GetDirectoryName(staging) == root && Path.GetFileName(staging).StartsWith(".acsm-create-", StringComparison.Ordinal))
                Directory.Delete(staging, true);
        }
    }
    private static void PublishDirectory(string staging, string target, CancellationToken cancellation)
    {
        // Antivirus scans may briefly hold a newly extracted executable or its directory.
        // Retry only while our staging folder exists and the destination is still absent.
        for (var attempt=0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            CheckNoLinks(Path.GetDirectoryName(target)!);
            try { Directory.Move(staging, target); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 15
                && Directory.Exists(staging) && !Directory.Exists(target) && !File.Exists(target))
            {
                if (cancellation.WaitHandle.WaitOne(200)) cancellation.ThrowIfCancellationRequested();
            }
        }
    }
    public static Dictionary<string, string> BuildConfiguration(ServerCreationRequest request)
    {
        var admin = request.AdminPassword.Length == 0 ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) : request.AdminPassword;
        var ini = "[SERVER]\nCLIENT_SEND_INTERVAL_HZ=20\nLOOP_MODE=1\nFUEL_RATE=0\nDAMAGE_MULTIPLIER=0\nTYRE_WEAR_RATE=0\nALLOWED_TYRES_OUT=-1\nABS_ALLOWED=1\nTC_ALLOWED=1\nSTABILITY_ALLOWED=0\nAUTOCLUTCH_ALLOWED=1\nTYRE_BLANKETS_ALLOWED=0\nTIME_OF_DAY_MULT=0\nWELCOME_MESSAGE=welcome.txt\n\n[PRACTICE]\nNAME=Practice\nTIME=120\nIS_OPEN=1\nINFINITE=1\n\n[DYNAMIC_TRACK]\nSESSION_START=100\n\n[WEATHER_0]\nGRAPHICS=3_clear\nBASE_TEMPERATURE_AMBIENT=20\nBASE_TEMPERATURE_ROAD=6\nVARIATION_AMBIENT=0\nVARIATION_ROAD=0\nWIND_BASE_SPEED_MIN=0\nWIND_BASE_SPEED_MAX=0\nWIND_BASE_DIRECTION=0\nWIND_VARIATION_DIRECTION=0\n";
        var settings = new Dictionary<string, string> { ["NAME"]=request.Name, ["TRACK"]=request.Track, ["CONFIG_TRACK"]=request.Layout,
            ["TCP_PORT"]=request.TcpPort.ToString(), ["UDP_PORT"]=request.UdpPort.ToString(), ["HTTP_PORT"]=request.HttpPort.ToString(),
            ["MAX_CLIENTS"]=request.Cars.Sum(c => c.Count).ToString(), ["PASSWORD"]=request.Password, ["ADMIN_PASSWORD"]=admin,
            ["REGISTER_TO_LOBBY"]=request.PublishToLobby ? "1" : "0", ["SUN_ANGLE"]=(16.0 * (request.StartMinutes * 60 - 46800) / 3600).ToString(CultureInfo.InvariantCulture) };
        var extra = "# Created with AC Server Manager; AssettoServer fills unspecified defaults.\nEnableAi: false\nEnableWeatherFx: false\n";
        var profile = new ServerProfile { Path="", Ini=ini, Extra=extra, Entries="", OriginalBytes=[] };
        foreach (var car in request.Cars)
            for (var i = 0; i < car.Count; i++) profile.Slots.Add(new Slot { Model=car.Model, Skin=car.Skin, Ai=car.Role });
        var result = profile.Build(settings, request.Ai, request.WeatherFx);
        if (request.Ai) result["extra_cfg.yml"] += "AiParams:\n  MaxPlayerCount: " + profile.PlayerSlots + "\n";
        return result;
    }
    private static bool RuntimeFile(string relative)
    {
        var name = relative.Replace('\\', '/');
        if (name.Contains('/') && !name.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)) return false;
        return (name.Equals("AssettoServer.exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(Path.GetFileName(name), @"^(LICENSE|COPYING|NOTICE|THIRD.PARTY.NOTICES)(\..*)?$", RegexOptions.IgnoreCase));
    }
    private static void InstallRuntime(string source, string target, CancellationToken cancellation)
    {
        CheckNoLinks(source);
        if (Directory.Exists(source))
        {
            var files = Directory.EnumerateFiles(source).ToList();
            var plugins = Path.Combine(source, "plugins");
            if (Directory.Exists(plugins))
            {
                CheckNoLinks(plugins);
                files.AddRange(Directory.EnumerateFiles(plugins, "*", new EnumerationOptions { RecurseSubdirectories=true, AttributesToSkip=FileAttributes.ReparsePoint }));
            }
            foreach (var file in files.Where(f => RuntimeFile(Path.GetRelativePath(source, f))))
            {
                cancellation.ThrowIfCancellationRequested(); CheckNoLinks(file);
                var dest = SafeDestination(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, false);
            }
        }
        else
        {
            using var zip = ZipFile.OpenRead(source);
            var executables = zip.Entries.Where(e => Path.GetFileName(e.FullName.Replace('\\', '/')).Equals("AssettoServer.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (executables.Length != 1) throw new InvalidOperationException(T("Выберите Windows x64 ZIP с одним AssettoServer.exe."));
            var exe = executables[0].FullName.Replace('\\', '/'); var prefix = exe[..(exe.LastIndexOf('/') + 1)];
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                _ = SafeDestination(target, name);
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0) throw new InvalidOperationException(T("Ссылки в ZIP не поддерживаются."));
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = name[prefix.Length..];
                if (!RuntimeFile(relative)) continue;
                total += entry.Length;
                if (total > 1024L * 1024 * 1024) throw new InvalidOperationException(T("ZIP AssettoServer слишком большой."));
                cancellation.ThrowIfCancellationRequested();
                var dest = SafeDestination(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = entry.Open(); using var output = new FileStream(dest, FileMode.CreateNew);
                var buffer = new byte[81920]; int read;
                while ((read=input.Read(buffer)) != 0) { cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
            }
        }
        foreach (var file in RequiredRuntime) if (!File.Exists(Path.Combine(target, file))) throw new InvalidOperationException(T("В установке AssettoServer отсутствует {0}. Выберите полный Windows x64 ZIP или папку программы.", file));
    }
    private static bool Within(string path, string root) => path.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string SafeDestination(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p == "..")) throw new InvalidOperationException(T("Некорректный путь в пакете сервера."));
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(full, root) || full == root) throw new InvalidOperationException(T("Некорректный путь в пакете сервера."));
        return full;
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(T("Для создания сервера выберите обычные папки без символических ссылок."));
    }
}
