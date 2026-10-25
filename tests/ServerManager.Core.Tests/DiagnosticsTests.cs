using System.Net;
using System.Net.Sockets;
using ServerManager.Core;

internal static class DiagnosticsTests
{
    public static void Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var server = Path.Combine(root, "server"); var game = Path.Combine(root, "game");
        Put(Path.Combine(server, "cfg/server_cfg.ini"), "[SERVER]\nNAME=Fixture\nTRACK=track\nCONFIG_TRACK=\nMAX_CLIENTS=1\nTCP_PORT=29600\nUDP_PORT=29600\nHTTP_PORT=28081\n");
        Put(Path.Combine(server, "cfg/entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=skin\nAI=none\n");
        Put(Path.Combine(server, "cfg/extra_cfg.yml"), "EnableAi: false\n");
        Put(Path.Combine(server, "AssettoServer.exe"), "fixture, never executed");
        Put(Path.Combine(server, "content/cars/car/data.acd"), "data");
        Put(Path.Combine(game, "content/cars/car/data.acd"), "data");
        Put(Path.Combine(game, "content/cars/car/skins/skin/ui_skin.json"), "{}");
        Put(Path.Combine(game, "content/tracks/track/ui/ui_track.json"), "{\"name\":\"Track\",\"pitboxes\":\"1\"}");
        var catalog = ContentCatalog.Load(game); var profile = ServerProfile.Load(server); var input = ServerDiagnostics.FromProfile(profile);
        var initial = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var good = ServerDiagnostics.Analyze(input, catalog);
        Check(good.Findings.Count == 0, "Healthy fixture produced false diagnostics.");
        var raw = input.Settings.ToDictionary(p => p.Key, p => p.Value); raw["HTTP_PORT"] = "70000"; raw["MAX_CLIENTS"] = "0";
        raw.Remove("FUEL_RATE"); raw.Remove("DAMAGE_MULTIPLIER"); raw.Remove("TYRE_WEAR_RATE");
        var invalid = ServerDiagnostics.Analyze(input with { Settings = raw }, catalog);
        Check(invalid.Errors == 3 && invalid.Findings.Single(f => f.Code == "setting.range.HTTP_PORT").Actions.Single().Target == "HTTP_PORT", "Invalid numeric fields did not have actionable diagnostics.");
        var samePorts = input.Settings.ToDictionary(p => p.Key, p => p.Key == "TCP_PORT" ? "09600" : p.Key == "HTTP_PORT" ? "9600" : p.Value);
        Check(ServerDiagnostics.Analyze(input with { Settings = samePorts }, catalog).Findings.Any(f => f.Code == "ports.same"), "Numerically equal TCP/HTTP ports escaped diagnostics.");
        raw["TRACK"] = "../../outside";
        Check(ServerDiagnostics.Analyze(input with { Settings = raw }, catalog).Findings.Any(f => f.Code == "track.invalid-id"), "Unsafe track ID was not rejected before content access.");
        Put(Path.Combine(game, "content/cars/car/data.acd"), "different");
        var mismatch = ServerDiagnostics.Analyze(input, catalog);
        Check(mismatch.Errors == 0 && mismatch.Findings.Any(f => f.Code == "car.checksum.car" && f.Severity == DiagnosticSeverity.Warning), "Checksum mismatch should explain a warning without overwriting files.");
        File.Delete(Path.Combine(server, "content/cars/car/data.acd"));
        var missing = ServerDiagnostics.Analyze(input, catalog).Findings.Single(f => f.Code == "car.missing-data.car");
        Check(missing.Severity == DiagnosticSeverity.Error && missing.Actions.Any(a => a.Kind == DiagnosticActionKind.Save), "Missing server car data should have a Save preparation action.");
        var ignored = ServerDiagnostics.Analyze(input with { Extra = "IgnoreConfigurationErrors:\n  MissingCarChecksums: true\n" }, catalog);
        Check(ignored.Findings.Single(f => f.Code == "car.missing-data.car").Severity == DiagnosticSeverity.Warning, "Explicit checksum override was treated as unconditional failure.");
        Put(Path.Combine(server, "content/cars/car/data-alt.acd"), "alternative");
        Check(ServerDiagnostics.Analyze(input with { Extra = "EnableAlternativeCarChecksums: true\n" }, catalog).Findings.Any(f => f.Code == "car.alternative.car"), "Alternative car archives were not recognized.");
        var special = input with { Settings = input.Settings.ToDictionary(p => p.Key, p => p.Key == "TRACK" ? "csp/special" : p.Value) };
        Check(ServerDiagnostics.Analyze(special, null).Errors > 0, "Unsafe paths were accepted.");
        var trackMissing = input with { Settings = input.Settings.ToDictionary(p => p.Key, p => p.Key == "TRACK" ? "unknown" : p.Value), Extra = "IgnoreConfigurationErrors:\n  MissingCarChecksums: true\n" };
        Check(ServerDiagnostics.Analyze(trackMissing, catalog).Findings.Single(f => f.Code == "track.missing").Severity == DiagnosticSeverity.Warning, "Missing track checksums should not claim a guaranteed startup failure.");
        using (var listener = new TcpListener(IPAddress.Any, 0))
        {
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
            var ports = input.Settings.ToDictionary(p => p.Key, p => p.Key == "HTTP_PORT" ? port : p.Value);
            var occupied = ServerDiagnostics.Analyze(input with { Settings = ports }, catalog, true);
            Check(occupied.Findings.Any(f => f.Code.StartsWith("port.unavailable.")), "Occupied port was not detected.");
            Check(!ServerDiagnostics.Analyze(input with { Settings = ports }, catalog, true, true).Findings.Any(f => f.Code.StartsWith("port.unavailable.")), "A running server's own port was treated as a conflict.");
        }
        File.AppendAllText(Path.Combine(server, "cfg/server_cfg.ini"), "; external\n");
        var external = ServerDiagnostics.Analyze(input, catalog).Findings.Single(f => f.Code == "config.external.server_cfg.ini");
        Check(external.Severity == DiagnosticSeverity.Error && external.Actions.Single().Kind == DiagnosticActionKind.Reload, "External changes were not identified before launch.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { ServerDiagnostics.Analyze(input, catalog, cancellation: cancellation.Token); throw new Exception("Cancellation was ignored."); } catch (OperationCanceledException) { }
        foreach (var (path, bytes) in initial) File.WriteAllBytes(path, bytes);
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        ServerDiagnostics.Analyze(ServerDiagnostics.FromProfile(ServerProfile.Load(server)), catalog);
        Check(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && before.Count == Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length, "Diagnostics wrote files.");
        var previous = UiText.Language;
        foreach (var language in new[] { "en", "ru" })
        {
            UiText.Language = language;
            foreach (var finding in invalid.Findings) Check(!finding.Title.Contains("{0}") && !finding.Detail.Contains("{1}") && finding.Actions.All(a => a.Label.Length > 0), "Finding formatting failed.");
        }
        UiText.Language = previous;
        var redacted = DiagnosticSanitizer.Redact("ApiToken: |\n  multiline-secret\n  more-secret\nEnabled: true\nPASSWORD=other-secret\n");
        Check(!redacted.Contains("multiline-secret") && !redacted.Contains("more-secret") && !redacted.Contains("other-secret") && redacted.Contains("Enabled: true"), "Multiline YAML credentials leaked into previews.");
        Console.WriteLine("PASS: structured diagnostics, actionable errors, warning severity, external changes, occupied ports, cancellation, privacy and read-only checks.");
    }
}
