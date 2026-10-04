using ServerManager.Core;
using System.Text;

var root = Path.Combine(Path.GetTempPath(), "AssettoManager-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool result, string message) { if (!result) throw new Exception(message); }
void MustThrow(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(true)); }
try
{
    CreationTests.Run(Path.Combine(root, "creation"));
    var server = Path.Combine(root, "server"); var cfg = Path.Combine(server, "cfg");
    Put(Path.Combine(cfg, "server_cfg.ini"), "; retained\r\n[SERVER]\r\nNAME=Fixture\r\nTRACK=track\r\nCONFIG_TRACK=layout\r\nMAX_CLIENTS=10\r\nCARS=old;unused\r\nPASSWORD=private\r\nCUSTOM_SERVER=yes\r\n[WEATHER_0]\r\nGRAPHICS=3_clear\r\n");
    Put(Path.Combine(cfg, "extra_cfg.yml"), "# retained\r\nEnableAi: false # comment\r\nEnableWeatherFx: true\r\nAiParams:\r\n  UnknownNested: yes\r\n---\r\n!PluginConfiguration\r\nOption: retained\r\n");
    Put(Path.Combine(cfg, "entry_list.ini"), "; slot comment\r\n[CAR_0]\r\nMODEL=old\r\nSKIN=blue\r\nAI=none\r\nGUID=private-guid\r\nCUSTOM_SLOT=yes\r\n[CAR_5]\r\nMODEL=traffic\r\nSKIN=generated\r\nAI=fixed\r\nBALLAST=13\r\n[OTHER]\r\nRETAIN=1\r\n");
    var groupingCatalog = new ContentCatalog { GamePath = root };
    groupingCatalog.Tracks.AddRange(new[] {
        new TrackInfo("highforce", "hotel", "High Force - Hotel", root, null, null, 10),
        new TrackInfo("HIGHFORCE", "stanhope", "High Force - Stanhope", root, null, null, 17),
        new TrackInfo("srp", "main_layout", "Shutoko - Main Layout", root, null, null, 170),
        new TrackInfo("srp", "pa", "Shutoko - Parking", root, null, null, 20),
        new TrackInfo("single", "", "Single Track", root, null, null, 12)
    });
    Check(groupingCatalog.TrackGroups.Count == 3 && groupingCatalog.TrackGroups.Single(g => g.Id == "highforce").Name == "High Force", "Tracks not grouped by case-insensitive ID/base name");
    Check(groupingCatalog.TrackGroups.Single(g => g.Id == "srp").DefaultLayout.Layout == "main_layout", "Main layout was not preferred");
    Check(groupingCatalog.TrackGroups.Single(g => g.Id == "single").DefaultLayout.Layout == "", "Default track without named layouts was lost");
    groupingCatalog.Tracks.AddRange(new[] { new TrackInfo("same", "a", "Same Track Name", root, null, null, 10), new TrackInfo("same", "b", "Same Track Name", root, null, null, 20) });
    Check(groupingCatalog.TrackGroups.Single(g => g.Id == "same").Name == "Same Track Name", "Identical layout names were truncated");
    var profile = ServerProfile.Load(server);
    Check(profile.Slots.Count == 2 && profile.PlayerSlots == 1 && profile.TrafficSlots == 1, "Import counts");
    profile.Slots.Move(0, 1);
    var reordered = profile.Build(new(), false, true)["entry_list.ini"];
    Check(profile.Slots[0].EntrySection == "CAR_0" && profile.Slots[1].EntrySection == "CAR_1", "Slot numbers after move");
    Check(ConfigText.Get(reordered, "CAR_0", "MODEL") == "traffic" && ConfigText.Get(reordered, "CAR_1", "GUID") == "private-guid", "Move did not retain car identity/private fields");
    profile.Slots.Move(1, 0);
    var dup = profile.Slots[0].Duplicate(); profile.Slots.Insert(1, dup); dup.Model = "new";
    Check(ConfigText.Get(dup.Raw, dup.OriginalSection, "GUID") == "", "Duplicated GUID retained");
    profile.Slots.RemoveAt(2);
    var texts = profile.Build(new() { ["NAME"]="Сервер друзей", ["HTTP_PORT"]="8082" }, false, true);
    Check(ConfigText.Get(texts["server_cfg.ini"], "SERVER", "CARS") == "old;new", "Deleted/unassigned CARS retained");
    Check(texts["server_cfg.ini"].Contains("CUSTOM_SERVER=yes"), "Unknown server field lost");
    Check(texts["server_cfg.ini"].Contains("PASSWORD=private"), "Password lost");
    Check(texts["extra_cfg.yml"] == profile.Extra, "Untouched YAML altered");
    Check(texts["entry_list.ini"].Contains("GUID=private-guid") && texts["entry_list.ini"].Contains("CUSTOM_SLOT=yes"), "Unknown slot fields lost");
    Check(ConfigText.Get(texts["entry_list.ini"], "OTHER", "RETAIN") == "1", "Other section lost");
    Check(ConfigText.Sections(texts["entry_list.ini"]).Select(s => s.Name).SequenceEqual(new[] { "CAR_0", "CAR_1", "OTHER" }), "Slot renumbering failed");
    Check(ConfigText.SetYaml(profile.Extra, "EnableAi", true).Contains("EnableAi: true # comment"), "YAML comment lost");
    MustThrow(() => profile.Build(new() { ["TCP_PORT"]="65536" }, false, true), "Invalid port accepted");
    MustThrow(() => profile.Build(new() { ["NAME"]="bad\nname" }, false, true), "Newline accepted");
    MustThrow(() => profile.Build(new() { ["MAX_CLIENTS"]="1" }, false, true), "Insufficient slot cap accepted");
    MustThrow(() => ConfigText.SetYaml("EnableAi: true\nEnableAi: false", "EnableAi", true), "Duplicate YAML accepted");
    var source = Path.Combine(root, "source.acd"); File.WriteAllText(source, "car data");
    var backup = profile.Save(texts, [new(source, @"content\cars\new\data.acd")]);
    Check(File.Exists(Path.Combine(backup, "server_cfg.ini")), "Backup missing");
    Check(ConfigText.Read(Path.Combine(backup, "server_cfg.ini")) == profile.Ini, "Backup is not original");
    Check(File.ReadAllBytes(Path.Combine(cfg, "server_cfg.ini"))[0] == 239, "BOM lost");
    Check(ServerProfile.Load(server).Name == "Сервер друзей", "Unicode failed");
    Check(File.ReadAllText(Path.Combine(server, "content", "cars", "new", "data.acd")) == "car data", "Content copy failed");
    MustThrow(() => profile.Save(texts), "Stale save accepted");
    var fresh = ServerProfile.Load(server); var originals = fresh.OriginalBytes.ToDictionary(p => p.Key, p => p.Value.ToArray());
    MustThrow(() => fresh.Save(texts, [new(source, @"content\cars\valid\data.acd"), new(Path.Combine(root,"missing"), @"content\cars\bad\data.acd")]), "Staging failure ignored");
    Check(originals.All(p => File.ReadAllBytes(Path.Combine(cfg,p.Key)).SequenceEqual(p.Value)), "Failed staging modified configs");
    Check(!File.Exists(Path.Combine(server,"content","cars","valid","data.acd")), "Partial content leaked");
    var blockedDestination = Path.Combine(server,"content","cars","blocked","data.acd");
    Directory.CreateDirectory(blockedDestination);
    var priorContent = File.ReadAllBytes(Path.Combine(server,"content","cars","new","data.acd"));
    File.WriteAllText(source, "replacement content");
    MustThrow(() => fresh.Save(texts, [new(source, @"content\cars\new\data.acd"), new(source, @"content\cars\blocked\data.acd")]), "Commit failure ignored");
    Check(File.ReadAllBytes(Path.Combine(server,"content","cars","new","data.acd")).SequenceEqual(priorContent), "Replaced content not rolled back");
    Check(originals.All(p => File.ReadAllBytes(Path.Combine(cfg,p.Key)).SequenceEqual(p.Value)), "Failed commit modified configs");
    MustThrow(() => fresh.Save(texts, [new(source, @"..\escaped.acd")]), "Path traversal allowed");
    var game = Path.Combine(root, "game");
    Put(Path.Combine(game,"content","cars","new","ui","ui_car.json"), "{\"name\":\"New car\",\"brand\":\"Test\",\"description\":\"First line\r\nSecond line with \\\"quotes\\\"\"}");
    Put(Path.Combine(game,"content","cars","new","skins","red","ui_skin.json"), "{\"skinname\":\"Red skin\"}");
    Put(Path.Combine(game,"content","cars","new","data.acd"), "data");
    Put(Path.Combine(game,"content","tracks","track","ui","layout","ui_track.json"), "{\"name\":\"My layout\",\"pitboxes\":\"12\"}");
    Put(Path.Combine(game,"content","tracks","track","layout","data","surfaces.ini"), "[SURFACE_0]\nKEY=ROAD");
    Put(Path.Combine(game,"content","tracks","track","ai","fast_lane.aip"), "ai");
    var catalog = ContentCatalog.Load(game);
    Check(catalog.Cars.Count == 1 && catalog.Cars[0].Skins[0].Name == "Red skin", "Car/skin catalog failed");
    Check(catalog.Warnings.Count == 0 && catalog.FindCar("NEW")?.Name == "New car", "Multiline mod metadata or case-insensitive lookup failed");
    Check(catalog.FindTrack("TRACK", "LAYOUT") != null, "Case-insensitive track lookup failed");
    Check(catalog.Tracks.Count == 1 && catalog.Tracks[0].Layout == "layout" && catalog.Tracks[0].PitBoxes == 12, "Track catalog failed");
    var plans = catalog.PrepareMissingContent(fresh, texts);
    Check(plans.Any(p => p.RelativeDestination.EndsWith("surfaces.ini")) && plans.Any(p => p.RelativeDestination.EndsWith("fast_lane.aip")), "Track preparation failed");
    Check(!plans.Any(p => p.RelativeDestination.EndsWith("data.acd")), "Existing checksum would be overwritten");
    Check(catalog.Validate(fresh, texts).Any(w => w.Contains("old")), "Missing client car not detected");
    Check(catalog.Validate(fresh, texts).Any(w => w.Contains("Не совпадает data.acd машины new")), "Existing stale server car checksum not detected");
    Put(Path.Combine(server,"content","tracks","track","models_layout.ini"), "server models");
    Put(Path.Combine(game,"content","tracks","track","models_layout.ini"), "game models");
    Check(catalog.Validate(fresh, texts).Any(w => w.Contains("Не совпадает файл трассы models_layout.ini")), "Track checksum mismatch not detected");
    File.Copy(Path.Combine(game,"content","cars","new","data.acd"), Path.Combine(server,"content","cars","new","data.acd"), true);
    Check(!catalog.Validate(fresh, texts).Any(w => w.Contains("Не совпадает data.acd машины new")), "Matching car checksum incorrectly flagged");
    File.Delete(Path.Combine(game,"content","cars","new","data.acd"));
    Put(Path.Combine(game,"content","cars","new","data","car.ini"), "unpacked");
    Check(catalog.Validate(fresh, texts).Any(w => w.Contains("распакованная папка data")), "Unpacked client data hidden by stale server archive");
    UiText.Language = "en";
    Check(catalog.Validate(fresh, texts).Any(w => w.StartsWith("new has an unpacked data folder")), "English unpacked-data warning");
    Check(catalog.Validate(fresh, texts).Any(w => w.StartsWith("The game and server track file models_layout.ini differ")), "English track mismatch warning");
    Check(fresh.Summary == "2 players · 0 AI", "English profile summary");
    Check(catalog.Validate(fresh, texts).Any(w => w == "Car missing from the game: old"), "English validation warning");
    Check(UiText.T("{0}: допустимо {1}–{2}.", "TCP_PORT", 1, 65535) == "TCP_PORT: allowed range is 1–65535.", "English formatted validation");
    Check(UiText.T("Найдено {0}", 6) == "Found 6", "English count");
    UiText.Language = "ru";
    Check(fresh.Summary == "2 игроков · 0 AI", "Russian profile summary after switch");
    Console.WriteLine("PASS: import, slot add/duplicate/delete/renumber, private fields, YAML/plugin preservation, backup, BOM/Unicode, stale save, staging rollback, path confinement, validation, catalog, missing-content preparation and RU/EN localization.");
}
finally
{
    var expected = Path.Combine(Path.GetTempPath(), "AssettoManager-Tests-");
    if (Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}
