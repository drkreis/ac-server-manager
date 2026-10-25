using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public enum BackupFileState { Unchanged, Replace, Create, Remove }
public sealed record BackupInfo(string Path, string Id, DateTimeOffset Created, string Reason, bool Valid, string? Error);
public sealed record BackupEntry(string Destination, bool Existed, string? Hash);
public sealed record BackupManifest(int Format, DateTimeOffset Created, string Reason, List<BackupEntry> Files);
public sealed record BackupDifference(string RelativePath, BackupFileState State, long? CurrentSize, long? BackupSize, string CurrentPreview, string BackupPreview);
internal sealed record RestoreFile(BackupEntry Entry, string? CurrentHash, string? StoredHash, BackupDifference Difference);

public sealed class BackupRestorePlan
{
    internal BackupRestorePlan(string server, string backup, string fingerprint, List<RestoreFile> files)
    { ServerPath = server; BackupPath = backup; Fingerprint = fingerprint; Files = files; }
    public string ServerPath { get; }
    public string BackupPath { get; }
    internal string Fingerprint { get; }
    internal List<RestoreFile> Files { get; }
    public IReadOnlyList<BackupDifference> Differences => Files.Select(f => f.Difference).ToArray();
    public int ChangedFiles => Files.Count(f => f.Difference.State != BackupFileState.Unchanged);
}
public sealed record BackupRestoreResult(string? SafetyBackup, int ChangedFiles);

public static class ServerBackups
{
    public static readonly string[] ConfigurationFiles = ["server_cfg.ini", "entry_list.ini", "extra_cfg.yml"];
    private const string ManifestFile = "manager-backup.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CheckedPath(string root, string relative)
    {
        root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Replace('\\', '/').Split('/').Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.')))
            throw new InvalidDataException(T("Некорректный путь в резервной копии."));
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("Некорректный путь в резервной копии."));
        CheckNoLinks(root); CheckNoLinks(path);
        return path;
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException(T("Восстановление через ссылки и junction-папки не поддерживается."));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static string Server(string path, string? gamePath = null)
    {
        path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (path == System.IO.Path.GetPathRoot(path) || !Directory.Exists(path) || !Directory.Exists(System.IO.Path.Combine(path, "cfg")))
            throw new InvalidDataException(T("Выберите папку сервера с каталогом cfg."));
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new InvalidDataException(T("Восстановление доступно только для локальных серверов."));
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            var game = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(gamePath));
            if (path.Equals(game, StringComparison.OrdinalIgnoreCase) || path.StartsWith(game + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || game.StartsWith(path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(T("Нельзя восстанавливать бэкап в папку игры."));
        }
        CheckNoLinks(path); _ = CheckedPath(path, "cfg");
        return path;
    }
    internal static IDisposable OperationLock(string server)
    {
        var path = System.IO.Path.Combine(server, ".ac-server-manager.lock"); CheckNoLinks(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException ex) { throw new IOException(T("С сервером уже выполняется другая операция. Повторите позже."), ex); }
    }
    private static string Destination(string relative)
    {
        relative = relative.Replace('\\', '/');
        if (ConfigurationFiles.Any(n => relative.Equals("cfg/" + n, StringComparison.OrdinalIgnoreCase))) return relative;
        var parts = relative.Split('/');
        if (parts.Length == 4 && parts[0] == "content" && parts[1] == "cars" && parts[3] == "data.acd") return relative;
        if (parts.Length >= 4 && parts[0] == "content" && parts[1] == "tracks")
        {
            var name = parts[^1];
            if (name is "surfaces.ini" && parts[^2] == "data" || name.EndsWith(".ai", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".aip", StringComparison.OrdinalIgnoreCase) || parts.Length == 4 && Regex.IsMatch(name, @"^models(?:_[\w .-]+)?\.ini$")) return relative;
        }
        throw new InvalidDataException(T("Резервная копия содержит неподдерживаемый файл: {0}", relative));
    }
    private static string Stored(string backup, string destination)
        => CheckedPath(backup, destination.StartsWith("cfg/", StringComparison.OrdinalIgnoreCase) ? destination[4..] : "content/" + destination);
    internal static string Hash(string path, CancellationToken cancellation = default)
    {
        using var stream = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; int count;
        while ((count = stream.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static string? CurrentHash(string path, CancellationToken cancellation) => File.Exists(path) ? Hash(path, cancellation) : null;
    private static string BackupPath(string server, string backup)
    {
        var root = CheckedPath(server, "backups");
        backup = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(backup));
        if (!string.Equals(System.IO.Path.GetDirectoryName(backup), root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(backup))
            throw new InvalidDataException(T("Выберите резервную копию этого сервера."));
        CheckNoLinks(backup); return backup;
    }
    private static BackupManifest Read(string backup)
    {
        var manifest = CheckedPath(backup, ManifestFile);
        BackupManifest value;
        if (File.Exists(manifest))
        {
            if (new FileInfo(manifest).Length > 1024 * 1024) throw new InvalidDataException(T("Повреждён манифест резервной копии."));
            value = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllBytes(manifest)) ?? throw new InvalidDataException(T("Повреждён манифест резервной копии."));
            if (value.Format != 1 || value.Files == null || value.Files.Count > 10000 || value.Reason is not ("Save" or "BeforeRestore"))
                throw new InvalidDataException(T("Формат резервной копии не поддерживается."));
        }
        else
        {
            var files = ConfigurationFiles.Select(n => new BackupEntry("cfg/" + n, true, null)).ToList();
            var wrapper = CheckedPath(backup, "content");
            if (Directory.Exists(wrapper))
                foreach (var file in SafeFiles(wrapper))
                {
                    if (files.Count >= 10000) throw new InvalidDataException(T("В резервной копии слишком много файлов."));
                    files.Add(new(Destination(System.IO.Path.GetRelativePath(wrapper, file).Replace('\\', '/')), true, null));
                }
            value = new(1, new DateTimeOffset(Directory.GetCreationTimeUtc(backup), TimeSpan.Zero), "Legacy", files);
        }
        if (value.Files.Count < 3 || value.Files.Any(f => f == null || f.Destination == null)
            || value.Files.Select(f => f.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Files.Count
            || ConfigurationFiles.Any(n => !value.Files.Any(f => f.Existed && f.Destination.Equals("cfg/" + n, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException(T("Резервная копия неполная: нужны все три конфига."));
        foreach (var entry in value.Files)
        {
            _ = Destination(entry.Destination); var file = Stored(backup, entry.Destination);
            if (entry.Existed && !File.Exists(file) || !entry.Existed && entry.Hash != null
                || value.Reason != "Legacy" && entry.Existed && (entry.Hash == null || !Regex.IsMatch(entry.Hash, "^[a-f0-9]{64}$")))
                throw new InvalidDataException(T("Файлы резервной копии отсутствуют или повреждены."));
        }
        foreach (var name in ConfigurationFiles)
        {
            var path = CheckedPath(backup, name);
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException(T("Слишком большой конфиг в резервной копии."));
            var text = ConfigText.Read(path);
            if (name == "server_cfg.ini" && !ConfigText.Sections(text).Any(s => s.Name.Equals("SERVER", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(T("В резервной копии нет секции SERVER."));
            if (name == "entry_list.ini")
            {
                var sections = ConfigText.Sections(text).Where(s => Regex.IsMatch(s.Name, @"^CAR_\d+$", RegexOptions.IgnoreCase)).ToArray();
                if (sections.Length == 0 || sections.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sections.Length)
                    throw new InvalidDataException(T("В резервной копии некорректный список слотов."));
            }
        }
        return value;
    }
    private static IEnumerable<string> SafeFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var path))
        {
            CheckNoLinks(path);
            foreach (var file in Directory.EnumerateFiles(path)) { CheckNoLinks(file); yield return file; }
            foreach (var directory in Directory.EnumerateDirectories(path)) { CheckNoLinks(directory); pending.Push(directory); }
        }
    }
    public static IReadOnlyList<BackupInfo> List(string server, string? gamePath = null)
    {
        server = Server(server, gamePath); var root = CheckedPath(server, "backups");
        if (!Directory.Exists(root)) return [];
        var result = new List<BackupInfo>();
        foreach (var path in Directory.EnumerateDirectories(root).Where(p => !System.IO.Path.GetFileName(p).StartsWith('.')).Take(2000))
        {
            var id = System.IO.Path.GetFileName(path);
            try
            {
                CheckNoLinks(path); var snapshot = Read(path);
                var date = snapshot.Created;
                if (snapshot.Reason == "Legacy" && id.Length >= 15 && DateTime.TryParseExact(id[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = new DateTimeOffset(parsed);
                result.Add(new(path, id, date, snapshot.Reason, true, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
            { result.Add(new(path, id, DateTimeOffset.MinValue, "", false, ex.Message)); }
        }
        return result.OrderByDescending(b => b.Created).ThenByDescending(b => b.Id).ToArray();
    }
    public static BackupRestorePlan Preview(string server, string backup, bool includeContent = false, string? gamePath = null, CancellationToken cancellation = default)
    {
        server = Server(server, gamePath); backup = BackupPath(server, backup); var snapshot = Read(backup);
        var manifestPath = CheckedPath(backup, ManifestFile);
        var fingerprint = File.Exists(manifestPath) ? Hash(manifestPath, cancellation) : "legacy";
        var files = new List<RestoreFile>();
        foreach (var entry in snapshot.Files.Where(f => includeContent || f.Destination.StartsWith("cfg/", StringComparison.OrdinalIgnoreCase)))
        {
            cancellation.ThrowIfCancellationRequested();
            var destination = CheckedPath(server, Destination(entry.Destination)); var stored = Stored(backup, entry.Destination);
            var storedHash = entry.Existed ? Hash(stored, cancellation) : null;
            if (entry.Hash != null && entry.Hash != storedHash) throw new InvalidDataException(T("Контрольная сумма бэкапа не совпадает: {0}", entry.Destination));
            var currentHash = CurrentHash(destination, cancellation);
            var state = currentHash == storedHash ? BackupFileState.Unchanged : !entry.Existed ? BackupFileState.Remove : currentHash == null ? BackupFileState.Create : BackupFileState.Replace;
            files.Add(new(entry, currentHash, storedHash, new(entry.Destination, state,
                File.Exists(destination) ? new FileInfo(destination).Length : null, entry.Existed ? new FileInfo(stored).Length : null,
                TextPreview(destination, currentHash), entry.Existed ? TextPreview(stored, storedHash) : T("Файл отсутствовал в этой копии."))));
        }
        return new(server, backup, fingerprint, files);
    }
    private static string TextPreview(string path, string? hash)
    {
        if (!File.Exists(path)) return T("Файл отсутствует.");
        if (!path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)) return T("Двоичный файл · {0} байт\nSHA-256: {1}", new FileInfo(path).Length, hash);
        using var reader = new StreamReader(path, Encoding.UTF8, true); var chars = new char[32000]; var count = reader.ReadBlock(chars, 0, chars.Length);
        return DiagnosticSanitizer.Redact(new string(chars, 0, count)) + (reader.Peek() >= 0 ? "\n[truncated]" : "");
    }
    internal static void WriteSaveManifest(string backup, IEnumerable<CopyPlan> content)
    {
        var files = ConfigurationFiles.Select(n => new BackupEntry("cfg/" + n, true, Hash(CheckedPath(backup, n)))).ToList();
        foreach (var plan in content)
        {
            var relative = Destination(plan.RelativeDestination.Replace('\\', '/')); var old = Stored(backup, relative);
            files.Add(new(relative, File.Exists(old), File.Exists(old) ? Hash(old) : null));
        }
        var manifest = new BackupManifest(1, DateTimeOffset.UtcNow, "Save", files);
        File.WriteAllBytes(CheckedPath(backup, ManifestFile), JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
    }
    private static string Capture(string server, IEnumerable<string> destinations)
    {
        var root = CheckedPath(server, "backups"); Directory.CreateDirectory(root);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var stage = CheckedPath(root, ".capture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var target = CheckedPath(root, id);
        try
        {
            var entries = new List<BackupEntry>();
            foreach (var relative in ConfigurationFiles.Select(n => "cfg/" + n).Concat(destinations).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = CheckedPath(server, Destination(relative)); var stored = Stored(stage, relative); var exists = File.Exists(source);
                if (exists) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(stored)!); File.Copy(source, stored); }
                entries.Add(new(relative, exists, exists ? Hash(stored) : null));
            }
            File.WriteAllBytes(CheckedPath(stage, ManifestFile), JsonSerializer.SerializeToUtf8Bytes(new BackupManifest(1, DateTimeOffset.UtcNow, "BeforeRestore", entries), JsonOptions));
            Directory.Move(stage, target); return target;
        }
        catch { if (Directory.Exists(stage)) RemoveCapture(stage); throw; }
    }
    private static void RemoveCapture(string stage)
    {
        // Only a private, randomly named capture directory created above is eligible; never follow links.
        if (!System.IO.Path.GetFileName(stage).StartsWith(".capture-", StringComparison.Ordinal)) throw new InvalidOperationException();
        foreach (var file in SafeFiles(stage).ToArray()) File.Delete(file);
        void Empty(string path) { CheckNoLinks(path); foreach (var child in Directory.EnumerateDirectories(path)) Empty(child); Directory.Delete(path); }
        Empty(stage);
    }
    public static BackupRestoreResult Restore(BackupRestorePlan plan, string? gamePath = null)
    {
        var server = Server(plan.ServerPath, gamePath); var backup = BackupPath(server, plan.BackupPath);
        using var operation = OperationLock(server);
        var current = Preview(server, backup, plan.Files.Any(f => !f.Entry.Destination.StartsWith("cfg/", StringComparison.OrdinalIgnoreCase)), gamePath);
        if (current.Fingerprint != plan.Fingerprint || current.Files.Count != plan.Files.Count
            || current.Files.Where((f, i) => f.Entry != plan.Files[i].Entry || f.CurrentHash != plan.Files[i].CurrentHash || f.StoredHash != plan.Files[i].StoredHash).Any())
            throw new InvalidOperationException(T("Файлы изменились после просмотра. Обновите сравнение перед восстановлением."));
        var changed = current.Files.Where(f => f.Difference.State != BackupFileState.Unchanged).ToArray();
        if (changed.Length == 0) return new(null, 0);
        var safety = Capture(server, changed.Select(f => f.Entry.Destination));
        foreach (var file in changed)
            if (CurrentHash(Stored(safety, file.Entry.Destination), default) != file.CurrentHash)
                throw new IOException(T("Файлы изменились после просмотра. Обновите сравнение перед восстановлением."));
        var id = Guid.NewGuid().ToString("N"); var staged = new List<(RestoreFile File, string Target, string? Temp)>(); var applied = new List<(RestoreFile File, string Target)>();
        try
        {
            foreach (var file in changed)
            {
                var target = CheckedPath(server, file.Entry.Destination); string? temp = null;
                if (file.Entry.Existed)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); temp = target + ".restore-" + id + ".tmp";
                    staged.Add((file, target, temp));
                    File.Copy(Stored(backup, file.Entry.Destination), temp);
                    if (Hash(temp) != file.StoredHash) throw new InvalidDataException(T("Файл бэкапа изменился во время подготовки."));
                }
                else staged.Add((file, target, null));
            }
            foreach (var (file, target, temp) in staged)
            {
                _ = CheckedPath(server, file.Entry.Destination);
                if (CurrentHash(target, default) != file.CurrentHash) throw new InvalidOperationException(T("Файлы изменились после просмотра. Обновите сравнение перед восстановлением."));
                if (temp == null) File.Delete(target);
                else if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                applied.Add((file, target));
            }
            return new(safety, applied.Count);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var (file, target) in applied.AsEnumerable().Reverse())
                try
                {
                    _ = CheckedPath(server, file.Entry.Destination);
                    if (CurrentHash(target, default) != file.StoredHash) throw new IOException(T("Файл изменился во время отката: {0}", file.Entry.Destination));
                    if (file.CurrentHash == null) File.Delete(target);
                    else
                    {
                        var rollback = target + ".rollback-" + id + ".tmp";
                        try
                        {
                            File.Copy(Stored(safety, file.Entry.Destination), rollback);
                            if (Hash(rollback) != file.CurrentHash) throw new IOException(T("Контрольная сумма бэкапа не совпадает: {0}", file.Entry.Destination));
                            if (File.Exists(target)) File.Replace(rollback, target, null); else File.Move(rollback, target);
                        }
                        finally { if (File.Exists(rollback)) File.Delete(rollback); }
                    }
                }
                catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 1) throw new AggregateException(T("Восстановление прервано. Проверьте файлы и страховочный бэкап: {0}", safety), errors);
            throw;
        }
        finally { foreach (var (_, _, temp) in staged) if (temp != null && File.Exists(temp)) File.Delete(temp); }
    }
}
