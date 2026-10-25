using System.Text;
using System.Text.Json;
using ServerManager.Core;

internal static class BackupTests
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
        string Fixture(string name)
        {
            var path = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(path, "cfg"));
            File.WriteAllText(Path.Combine(path, "cfg", "server_cfg.ini"), "[SERVER]\r\nNAME=Original\r\nTRACK=track\r\nPASSWORD=private-test-password\r\nMAX_CLIENTS=1\r\n", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(path, "cfg", "entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=old\nAI=none\nGUID=private-test-guid\n");
            File.WriteAllText(Path.Combine(path, "cfg", "extra_cfg.yml"), "EnableAi: false\n---\n!PluginConfiguration\nUnknown: retained\n");
            return path;
        }
        Dictionary<string, byte[]> Bytes(string server) => ServerBackups.ConfigurationFiles.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(server, "cfg", n)));
        void Equal(string server, Dictionary<string, byte[]> expected, string message) => Check(expected.All(p => File.ReadAllBytes(Path.Combine(server, "cfg", p.Key)).SequenceEqual(p.Value)), message);
        var server = Fixture("server"); var original = Bytes(server); var profile = ServerProfile.Load(server);
        var source = Path.Combine(root, "new-data.acd"); File.WriteAllText(source, "new data");
        var texts = profile.SavedTexts; texts["server_cfg.ini"] = texts["server_cfg.ini"].Replace("Original", "Changed");
        var backup = profile.Save(texts, [new(source, "content/cars/car/data.acd")])!;
        Check(ServerBackups.List(server).Single().Valid && File.Exists(Path.Combine(backup, "manager-backup.json")), "New save backup was not recognized.");
        var current = Bytes(server); var plan = ServerBackups.Preview(server, backup);
        Check(plan.ChangedFiles == 1 && plan.Differences.Count == 3, "Configuration preview is incorrect.");
        Check(plan.Differences.All(d => !d.CurrentPreview.Contains("private-test-password") && !d.BackupPreview.Contains("private-test-guid")), "Diff preview exposed private fields.");
        Equal(server, current, "Preview modified configurations.");
        var restored = ServerBackups.Restore(plan);
        Equal(server, original, "Restore did not preserve exact bytes/BOM/plugin fields.");
        Check(restored.ChangedFiles == 1 && restored.SafetyBackup != null && File.Exists(Path.Combine(server, "content/cars/car/data.acd")), "Default restore changed content or omitted a safety copy.");
        ServerBackups.Restore(ServerBackups.Preview(server, restored.SafetyBackup!));
        Equal(server, current, "Safety backup did not undo restore.");
        var contentPlan = ServerBackups.Preview(server, backup, true);
        Check(contentPlan.Differences.Any(d => d.RelativePath == "content/cars/car/data.acd" && d.State == BackupFileState.Remove), "Absent content was not shown as removal.");
        var withContent = ServerBackups.Restore(contentPlan);
        Check(!File.Exists(Path.Combine(server, "content/cars/car/data.acd")), "Content removal was not applied.");
        ServerBackups.Restore(ServerBackups.Preview(server, withContent.SafetyBackup!, true));
        Check(File.ReadAllText(Path.Combine(server, "content/cars/car/data.acd")) == "new data", "Undo did not restore removed content.");
        var beforeCount = ServerBackups.List(server).Count;
        var noop = ServerBackups.Restore(ServerBackups.Preview(server, withContent.SafetyBackup!, true));
        Check(noop.ChangedFiles == 0 && noop.SafetyBackup == null && ServerBackups.List(server).Count == beforeCount, "No-op restore wrote another backup.");

        var stale = ServerBackups.Preview(server, backup); File.AppendAllText(Path.Combine(server, "cfg", "server_cfg.ini"), "; external\n");
        var external = Bytes(server); Reject(() => ServerBackups.Restore(stale), "Stale current files were overwritten."); Equal(server, external, "Stale restore modified files.");
        plan = ServerBackups.Preview(server, backup);
        var backupIni = Path.Combine(backup, "server_cfg.ini"); var beforeTamper = File.ReadAllBytes(backupIni); File.AppendAllText(backupIni, "; tampered\n");
        Reject(() => ServerBackups.Restore(plan), "Tampered backup was accepted."); Equal(server, external, "Tampered backup wrote files."); File.WriteAllBytes(backupIni, beforeTamper);

        var legacy = Path.Combine(server, "backups", "20260101-120000-legacy"); Directory.CreateDirectory(legacy);
        foreach (var file in original) File.WriteAllBytes(Path.Combine(legacy, file.Key), file.Value);
        var oldContent = Path.Combine(legacy, "content/content/cars/car/data.acd"); Directory.CreateDirectory(Path.GetDirectoryName(oldContent)!); File.WriteAllText(oldContent, "legacy data");
        Check(ServerBackups.List(server).Single(b => b.Path == legacy).Valid && ServerBackups.Preview(server, legacy, true).Differences.Count == 4, "Historical flat backup layout was not read.");
        var bad = Path.Combine(server, "backups", "incomplete"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad, "server_cfg.ini"), "broken");
        Check(!ServerBackups.List(server).Single(b => b.Path == bad).Valid, "Incomplete backup was offered for restore.");
        Reject(() => ServerBackups.Preview(server, Path.Combine(root, "outside")), "An outside backup was accepted.");
        Reject(() => ServerBackups.Preview(server, backup, false, server), "Game folder restore was accepted.");
        var manifestPath = Path.Combine(backup, "manager-backup.json"); var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestBytes)!;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Files = manifest.Files.Concat([new BackupEntry("../outside.txt", false, null)]).ToList() }));
        Reject(() => ServerBackups.Preview(server, backup, true), "Manifest traversal was accepted."); File.WriteAllBytes(manifestPath, manifestBytes);

        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject(() => ServerBackups.Preview(server, backup, cancellation: cancelled.Token), "Cancelled preview continued.");
        using (var held = new FileStream(Path.Combine(server, ".ac-server-manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            Reject(() => ServerBackups.Restore(ServerBackups.Preview(server, backup)), "Concurrent operation lock was bypassed.");
        plan = ServerBackups.Preview(server, backup);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Created = manifest.Created.AddSeconds(1) }));
        Reject(() => ServerBackups.Restore(plan), "Changed manifest was accepted after preview."); File.WriteAllBytes(manifestPath, manifestBytes);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Files = manifest.Files.Concat([new BackupEntry("AssettoServer.exe", false, null)]).ToList() }));
        Reject(() => ServerBackups.Preview(server, backup), "Unsupported executable restoration was accepted."); File.WriteAllBytes(manifestPath, manifestBytes);

        var missing = Fixture("missing-config"); var missingProfile = ServerProfile.Load(missing); var missingOriginal = Bytes(missing);
        var missingTexts = missingProfile.SavedTexts; missingTexts["server_cfg.ini"] += "; changed\n";
        var missingBackup = missingProfile.Save(missingTexts)!;
        File.Delete(Path.Combine(missing, "cfg", "entry_list.ini"));
        var missingPlan = ServerBackups.Preview(missing, missingBackup);
        Check(missingPlan.Differences.Any(f => f.RelativePath == "cfg/entry_list.ini" && f.State == BackupFileState.Create), "Missing configuration was not shown as creation.");
        var missingResult = ServerBackups.Restore(missingPlan); Equal(missing, missingOriginal, "Missing configuration was not recovered.");
        Check(!ServerBackups.List(missing).Single(b => b.Path == missingResult.SafetyBackup).Valid, "Incomplete pre-recovery state was offered as a healthy automatic undo.");

        // A sharing violation on the last configuration forces rollback after earlier replacements.
        var rollback = Fixture("rollback"); var rollbackOriginal = Bytes(rollback); var rollbackProfile = ServerProfile.Load(rollback);
        var rollbackTexts = rollbackProfile.SavedTexts;
        foreach (var name in ServerBackups.ConfigurationFiles) rollbackTexts[name] += "\n; changed\n";
        var rollbackBackup = rollbackProfile.Save(rollbackTexts)!; var rollbackCurrent = Bytes(rollback); var rollbackPlan = ServerBackups.Preview(rollback, rollbackBackup);
        using (var held = new FileStream(Path.Combine(rollback, "cfg", "extra_cfg.yml"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => ServerBackups.Restore(rollbackPlan), "Locked target did not reject restore.");
        Equal(rollback, rollbackCurrent, "Failed restore left partially restored configurations.");
        Check(!Directory.EnumerateFiles(Path.Combine(rollback, "cfg")).Any(p => p.EndsWith(".tmp")), "Restore left staged files.");
        ServerBackups.Restore(ServerBackups.Preview(rollback, rollbackBackup)); Equal(rollback, rollbackOriginal, "Restore after a failed operation did not recover.");
        Console.WriteLine("PASS: backup listing, legacy/manifest compatibility, redacted previews, exact restore/BOM/plugin preservation, safety undo, optional content/absence recovery, no-op, stale/tamper/traversal/game guards and mid-commit rollback.");
    }
}
