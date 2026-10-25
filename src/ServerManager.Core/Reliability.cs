using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServerManager.Core;

/// <summary>Settings are prepared and flushed before an atomic replacement; a valid prior version is retained.</summary>
public static class SafeJsonStore
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static T? Load<T>(string path, Func<T, bool>? validate = null) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
            try
            {
                if (File.Exists(candidate) && JsonSerializer.Deserialize<T>(File.ReadAllBytes(candidate)) is { } value
                    && (validate == null || validate(value))) return value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    public static void Save<T>(string path, T value, Func<T, bool>? validate = null) where T : class
    {
        path = Path.GetFullPath(path);
        if (validate != null && !validate(value)) throw new InvalidDataException("Invalid settings.");
        lock (Gates.GetOrAdd(path, _ => new object())) SaveLocked(path, value, validate);
    }
    private static void SaveLocked<T>(string path, T value, Func<T, bool>? validate) where T : class
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path))
            {
                // A broken primary must never replace the last usable backup.
                var valid = false;
                try { valid = JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) is { } previous && (validate == null || validate(previous)); }
                catch (JsonException) { }
                File.Replace(temp, path, valid ? path + ".bak" : null);
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static partial class DiagnosticSanitizer
{
    [GeneratedRegex("""(?im)(["']?[\w.-]*(?:password|token|secret|api[ _-]?key|authorization|guid|steam[ _-]?id)[\w.-]*["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'(?:''|\\.|[^'\\])*'|[^\r\n]+)""", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Secrets();
    [GeneratedRegex("(?i)(?:Bearer|Basic)\\s+[a-z0-9+/=._-]+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Authentication();
    [GeneratedRegex(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdentities();

    public static string Redact(string text, params (string Path, string Label)[] privatePaths)
    {
        try
        {
            text = Secrets().Replace(text, "$1[redacted]");
            text = Authentication().Replace(text, "[redacted authentication]");
            text = SteamIdentities().Replace(text, "[redacted identity]");
        }
        catch (RegexMatchTimeoutException) { return "[redaction unavailable]"; }
        foreach (var (path, label) in privatePaths.OrderByDescending(p => p.Path?.Length ?? 0))
            if (!string.IsNullOrWhiteSpace(path)) text = text.Replace(path, label, StringComparison.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home)) text = text.Replace(home, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}

/// <summary>A bounded, thread-safe log shared by output readers and the UI refresh timer.</summary>
public sealed class BoundedLog(int capacity = 100000)
{
    private readonly int limit = capacity >= 128 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Queue<string> lines = new();
    private int length;
    private readonly object gate = new();
    public long Version { get { lock (gate) return version; } }
    private long version;
    public void AppendLine(string text)
    {
        text = DiagnosticSanitizer.Redact(text);
        lock (gate)
        {
            var maximumLine = limit - Environment.NewLine.Length;
            if (text.Length > maximumLine) text = text[^maximumLine..];
            text += Environment.NewLine;
            lines.Enqueue(text); length += text.Length;
            while (length > limit) length -= lines.Dequeue().Length;
            version++;
        }
    }
    public override string ToString() { lock (gate) return string.Concat(lines); }
}

public sealed class ErrorJournal(string directory, int maxBytes = 256000)
{
    private readonly object gate = new();
    public string FilePath => Path.Combine(directory, "manager-errors.log");
    public bool Record(Exception exception, string operation)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                var entry = DiagnosticSanitizer.Redact($"{DateTimeOffset.Now:O} | {operation}\n{exception}\n\n");
                if (entry.Length > maxBytes / 4) entry = entry[..(maxBytes / 4)] + "\n[truncated]\n";
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + Encoding.UTF8.GetByteCount(entry) > maxBytes)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, entry, new UTF8Encoding(false));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public string Read()
    {
        lock (gate)
        {
            var text = new StringBuilder();
            foreach (var path in new[] { FilePath + ".previous", FilePath })
                try { if (File.Exists(path)) text.Append(File.ReadAllText(path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return DiagnosticSanitizer.Redact(text.ToString());
        }
    }
}
