using System.Text.Json;
using ServerManager.Core;

internal static class ReliabilityTests
{
    private sealed record Preferences(string? Folder, string Language);
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var file = Path.Combine(root, "settings.json");
        bool Valid(Preferences p) => p.Folder != null && p.Language is "en" or "ru";
        var first = new Preferences("D:\\Серверы\\日本語", "en");
        var second = first with { Language = "ru" };
        SafeJsonStore.Save(file, first, Valid);
        Check(SafeJsonStore.Load<Preferences>(file, Valid) == first, "Initial settings lost Unicode.");
        var timestamp = File.GetLastWriteTimeUtc(file);
        SafeJsonStore.Save(file, first, Valid);
        Check(!File.Exists(file + ".bak") && timestamp == File.GetLastWriteTimeUtc(file), "Identical preferences were rewritten.");
        SafeJsonStore.Save(file, second, Valid);
        Check(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file + ".bak")) == first, "Previous settings backup missing.");
        File.WriteAllText(file, "{\"Folder\":");
        Check(SafeJsonStore.Load<Preferences>(file, Valid) == first, "Truncated settings were not recovered.");
        SafeJsonStore.Save(file, second, Valid);
        Check(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file + ".bak")) == first, "Corrupted primary replaced valid backup.");
        File.WriteAllText(file, "{}");
        Check(SafeJsonStore.Load<Preferences>(file, Valid) == first, "Invalid schema hid valid backup.");
        SafeJsonStore.Save(file, second, Valid);
        Check(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file + ".bak")) == first, "Invalid-schema primary replaced backup.");
        using (var held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var rejected = false;
            try { SafeJsonStore.Save(file, first, Valid); } catch (IOException) { rejected = true; }
            Check(rejected, "Locked settings write was not rejected.");
        }
        Check(SafeJsonStore.Load<Preferences>(file, Valid) == second, "Failed write damaged preferences.");
        Parallel.For(0, 40, i => SafeJsonStore.Save(file, new Preferences("root " + i, "en"), Valid));
        Check(SafeJsonStore.Load<Preferences>(file, Valid)?.Folder?.StartsWith("root ") == true && Directory.GetFiles(root, "*.tmp").Length == 0, "Concurrent settings save left invalid data or temporary files.");

        var privateText = "PASSWORD=plain-secret\nADMIN_PASSWORD=\"secret with spaces\"\n{\"token\":\"json-secret\"}\nAuthorization: Bearer auth-secret\nGUID=driver-id\nhttps://x/?api_key=url-secret&x=1\nD:\\private\\server\n";
        var safe = DiagnosticSanitizer.Redact(privateText, (@"D:\private", "<servers>"));
        Check(new[] { "plain-secret", "secret with spaces", "json-secret", "auth-secret", "driver-id", "url-secret", @"D:\private" }.All(s => !safe.Contains(s)), "Diagnostic redaction exposed known private values.");
        Check(!DiagnosticSanitizer.Redact("Driver (76561198000000000 - 127.0.0.1) connected").Contains("76561198000000000"), "Standard connection output exposed a Steam identity.");
        Check(!DiagnosticSanitizer.Redact("{\"token\":\"before \\\"escaped\\\" after-secret\"}\nSteam ID: player-private\nAPI key=key-private\npassword=\"unfinished, comma-secret").Contains("secret")
            && !DiagnosticSanitizer.Redact("Steam ID: player-private\nAPI key=key-private").Contains("private"), "Escaped/truncated quoted secrets or spaced identity labels leaked.");
        var log = new BoundedLog(4000);
        Parallel.For(0, 10000, i => log.AppendLine("PASSWORD=entry-secret " + i));
        log.AppendLine("Last error retained");
        Check(log.Version == 10001 && log.ToString().Length <= 4000 && log.ToString().EndsWith("Last error retained" + Environment.NewLine) && !log.ToString().Contains("entry-secret"), "Burst log was unbounded, lost output or exposed secrets.");
        var journal = new ErrorJournal(Path.Combine(root, "logs"), 2000);
        for (var i = 0; i < 80; i++) Check(journal.Record(new IOException(privateText), "test"), "Error journal write failed.");
        Check(File.Exists(journal.FilePath + ".previous") && Directory.GetFiles(Path.GetDirectoryName(journal.FilePath)!).Length == 2
            && !journal.Read().Contains("plain-secret"), "Journal rotation or sanitization failed.");
        Check(new FileInfo(journal.FilePath).Length <= 2000 && new FileInfo(journal.FilePath + ".previous").Length <= 2000, "Error journal exceeded limit.");
        Check(ServerReadiness.Matches("{\"track\":\"imola\"}", "imola", "") && ServerReadiness.Matches("{\"track\":\"srp-main_layout\"}", "srp", "main_layout"), "Valid readiness rejected.");
        foreach (var json in new[] { "", "not json", "[]", "{}", "{\"track\":5}", "{\"track\":\"other\"}", new string('x', 70000) })
            Check(!ServerReadiness.Matches(json, "imola", ""), "Incorrect /INFO response marked ready.");
        Console.WriteLine("PASS: atomic settings, unchanged writes, corrupt/schema recovery, locked/concurrent writes, bounded burst logs, diagnostic redaction/rotation and readiness response validation.");
    }
}
