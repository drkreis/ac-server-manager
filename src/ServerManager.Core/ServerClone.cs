using System.Security.Cryptography;
using System.Text;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public record ServerPortSelection(int Tcp, int Udp, int Http)
{
    public static ServerPortSelection Suggest(string root)
    {
        var tcp = new HashSet<int>(); var udp = new HashSet<int>();
        if (Directory.Exists(root)) foreach (var p in ServerProfile.Discover(root))
        {
            foreach (var key in new[] { "TCP_PORT", "HTTP_PORT" }) if (int.TryParse(ConfigText.Get(p.Ini, "SERVER", key), out var port)) tcp.Add(port);
            if (int.TryParse(ConfigText.Get(p.Ini, "SERVER", "UDP_PORT"), out var u)) udp.Add(u);
        }
        bool Available(int port, bool datagram)
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, datagram ? System.Net.Sockets.SocketType.Dgram : System.Net.Sockets.SocketType.Stream, datagram ? System.Net.Sockets.ProtocolType.Udp : System.Net.Sockets.ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try { socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port)); return true; }
            catch (System.Net.Sockets.SocketException) { return false; }
        }
        var common = Enumerable.Range(9600, 4100).FirstOrDefault(p => !tcp.Contains(p) && !udp.Contains(p) && Available(p, false) && Available(p, true));
        var httpPort = Enumerable.Range(8081, 4100).FirstOrDefault(p => p != common && !tcp.Contains(p) && Available(p, false));
        if (common != 0 && httpPort != 0) return new(common, common, httpPort);
        throw new IOException(T("Не удалось подобрать свободные порты. Укажите их вручную."));
    }
}

public sealed record ServerCloneRequest(string Source, string ServersRoot, string Folder, string Name, int Tcp, int Udp, int Http, bool IncludeAdvanced = false);

public static class ServerClone
{
    private static bool Within(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string Validate(ServerCloneRequest request, string? gamePath = null)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Source));
        var target = ServerCreation.Destination(request.ServersRoot, request.Folder);
        if (source.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(source)!).DriveType == DriveType.Network
            || target.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(target)!).DriveType == DriveType.Network)
            throw new IOException(T("Копирование доступно только для локальных папок."));
        if (source.Equals(Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase)) throw new IOException(T("Выберите отдельную папку сервера, а не корень диска."));
        _ = ServerBackups.CheckedPath(source, "cfg/server_cfg.ini");
        if (Within(target, source) || Within(source, target)) throw new IOException(T("Создавайте копию в отдельной папке рядом с исходным сервером."));
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
            if (Within(source, game) || Within(game, source) || Within(target, game) || Within(game, target)) throw new IOException(T("Создавайте сервер вне папки Assetto Corsa."));
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || request.Name.Any(char.IsControl)) throw new IOException(T("Введите название сервера (до 120 символов)."));
        if (new[] { request.Tcp, request.Udp, request.Http }.Any(p => p is < 1 or > 65535) || request.Tcp == request.Http) throw new IOException(T("TCP и HTTP должны быть разными; диапазон портов 1–65535."));
        var profile = ServerProfile.Load(source);
        if (profile.Slots.Count is < 1 or > 255) throw new IOException(T("Нужно от 1 до 255 слотов."));
        foreach (var key in new[] { "TCP_PORT", "HTTP_PORT" })
            if (int.TryParse(ConfigText.Get(profile.Ini, "SERVER", key), out var port) && (request.Tcp == port || request.Http == port)) throw new IOException(T("Для копии выберите порты, отличающиеся от исходного сервера."));
        if (int.TryParse(ConfigText.Get(profile.Ini, "SERVER", "UDP_PORT"), out var udp) && udp == request.Udp) throw new IOException(T("Для копии выберите порты, отличающиеся от исходного сервера."));
        foreach (var file in new[] { "AssettoServer.exe", "steam_api64.dll", "csp_xxhash3.dll" })
            if (!File.Exists(ServerBackups.CheckedPath(source, file))) throw new IOException(T("В установке AssettoServer отсутствует {0}. Выберите полный Windows x64 ZIP или папку программы.", file));
        return target;
    }
    private static List<string> Files(string source, bool advanced, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var files = Directory.EnumerateFiles(source).Select(p => Path.GetRelativePath(source, p)).Where(ServerCreation.RuntimeFile).ToList();
        files.AddRange(ServerBackups.ConfigurationFiles.Select(n => Path.Combine("cfg", n)));
        if (File.Exists(Path.Combine(source, "welcome.txt"))) files.Add("welcome.txt");
        foreach (var folder in advanced ? new[] { "content", "cfg", "plugins" } : new[] { "content" })
        {
            var start = ServerBackups.CheckedPath(source, folder); if (!Directory.Exists(start)) continue;
            var pending = new Stack<string>(); pending.Push(start);
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                _ = ServerBackups.CheckedPath(source, Path.GetRelativePath(source, directory));
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                        || new[] { "admins.txt", "whitelist.txt", "blacklist.txt" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    files.Add(Path.GetRelativePath(source, file));
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                    if (!new[] { "logs", "backups", "cache" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) pending.Push(child);
            }
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static ServerProfile Create(ServerCloneRequest request, string? gamePath = null, IProgress<string>? progress = null, CancellationToken cancellation = default, Action? ensureStopped = null)
    {
        cancellation.ThrowIfCancellationRequested(); var target = Validate(request, gamePath);
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Source));
        ensureStopped?.Invoke(); using var operation = ServerBackups.OperationLock(source);
        var profile = ServerProfile.Load(source); var files = Files(source, request.IncludeAdvanced, cancellation);
        var root = Path.GetDirectoryName(target)!; Directory.CreateDirectory(root);
        var stage = ServerBackups.CheckedPath(root, ".acsm-clone-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            // AssettoServer scans this directory even when no plugins are selected.
            Directory.CreateDirectory(ServerBackups.CheckedPath(stage, "plugins"));
            var hashes = new Dictionary<string, string>();
            foreach (var relative in files)
            {
                cancellation.ThrowIfCancellationRequested(); progress?.Report(T("Копирую файл: {0}", relative));
                var inputPath = ServerBackups.CheckedPath(source, relative); var outputPath = ServerBackups.CheckedPath(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var bytes = new byte[81920]; int count;
                while ((count = input.Read(bytes)) > 0) { cancellation.ThrowIfCancellationRequested(); output.Write(bytes, 0, count); hash.AppendData(bytes, 0, count); }
                hashes.Add(relative, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            }
            // Identity is fresh; arbitrary plugin configuration is an explicit local-only opt-in.
            var ini = profile.Ini;
            foreach (var (key, value) in new Dictionary<string, string> { ["NAME"]=request.Name, ["TCP_PORT"]=request.Tcp.ToString(), ["UDP_PORT"]=request.Udp.ToString(), ["HTTP_PORT"]=request.Http.ToString(), ["PASSWORD"]="", ["ADMIN_PASSWORD"]=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), ["REGISTER_TO_LOBBY"]="0", ["WELCOME_MESSAGE"]="welcome.txt" }) ini = ConfigText.Set(ini, "SERVER", key, value);
            var entries = profile.Entries;
            foreach (var slot in profile.Slots) foreach (var key in new[] { "GUID", "DRIVERNAME", "TEAM" }) entries = ConfigText.Set(entries, slot.OriginalSection, key, "");
            var extra = request.IncludeAdvanced ? profile.Extra : "# Copied with AC Server Manager. Advanced settings were not included.\nEnableAi: " + (ConfigText.GetYaml(profile.Extra, "EnableAi") == "true" ? "true" : "false") + "\nEnableWeatherFx: " + (ConfigText.GetYaml(profile.Extra, "EnableWeatherFx") == "true" ? "true" : "false") + "\nAiParams:\n  MaxPlayerCount: " + profile.PlayerSlots + "\n";
            File.WriteAllText(ServerBackups.CheckedPath(stage, "cfg/server_cfg.ini"), ini, new UTF8Encoding(false));
            File.WriteAllText(ServerBackups.CheckedPath(stage, "cfg/entry_list.ini"), entries, new UTF8Encoding(false));
            File.WriteAllText(ServerBackups.CheckedPath(stage, "cfg/extra_cfg.yml"), extra, new UTF8Encoding(false));
            if (!File.Exists(Path.Combine(stage, "welcome.txt"))) File.WriteAllText(Path.Combine(stage, "welcome.txt"), "Created with AC Server Manager.\n");
            if (!files.SequenceEqual(Files(source, request.IncludeAdvanced, cancellation), StringComparer.OrdinalIgnoreCase)
                || hashes.Any(pair => ServerBackups.Hash(ServerBackups.CheckedPath(source, pair.Key), cancellation) != pair.Value)
                || profile.OriginalBytes.Any(p => !File.ReadAllBytes(ServerBackups.CheckedPath(source, "cfg/" + p.Key)).SequenceEqual(p.Value)))
                throw new IOException(T("Исходный сервер изменился во время копирования. Повторите операцию."));
            cancellation.ThrowIfCancellationRequested(); ensureStopped?.Invoke();
            _ = Validate(request, gamePath);
            if (ServerPorts.FindConflicts(request.Tcp, request.Http, request.Udp).Length > 0) throw new IOException(T("Один из выбранных портов занят. Измените порты или остановите другой сервер."));
            _ = ServerProfile.Load(stage);
            ServerCreation.PublishDirectory(stage, target, cancellation);
            return ServerProfile.Load(target);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                _ = ServerBackups.CheckedPath(root, Path.GetFileName(stage));
                // The source tree was link-checked and all output bytes were created by this operation.
                foreach (var file in Directory.EnumerateFileSystemEntries(stage, "*", SearchOption.AllDirectories))
                    _ = ServerBackups.CheckedPath(stage, Path.GetRelativePath(stage, file));
                Directory.Delete(stage, true);
            }
        }
    }
}
