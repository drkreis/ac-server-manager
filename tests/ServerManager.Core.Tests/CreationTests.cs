using System.IO.Compression;
using ServerManager.Core;

internal static class CreationTests
{
    public static void Run(string root)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
        void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var game=Path.Combine(root, "game"); var source=Path.Combine(root, "runtime"); var servers=Path.Combine(root, "servers");
        Put(Path.Combine(game,"content","cars","car","ui","ui_car.json"), "{\"name\":\"Test Car\"}");
        Put(Path.Combine(game,"content","cars","car","skins","red","ui_skin.json"), "{\"skinname\":\"Red\"}");
        Put(Path.Combine(game,"content","cars","car","data.acd"), "car-data");
        Put(Path.Combine(game,"content","tracks","track","ui","layout","ui_track.json"), "{\"name\":\"Test Track\",\"pitboxes\":8}");
        Put(Path.Combine(game,"content","tracks","track","layout","data","surfaces.ini"), "surfaces");
        Put(Path.Combine(game,"content","tracks","track","models_layout.ini"), "models");
        Put(Path.Combine(game,"content","tracks","track","layout","ai","fast_lane.aip"), "traffic");
        foreach (var file in new[]{"AssettoServer.exe","steam_api64.dll","csp_xxhash3.dll","LICENSE"}) Put(Path.Combine(source,file), "runtime-" + file);
        Put(Path.Combine(source,"cfg","server_cfg.ini"), "PRIVATE-CONFIG"); Put(Path.Combine(source,"admins.txt"), "PRIVATE-ADMIN");
        Put(Path.Combine(source,"plugins","ExamplePlugin","ExamplePlugin.dll"), "runtime-plugin");
        Put(Path.Combine(source,"plugins","ExamplePlugin","config.yml"), "PRIVATE-PLUGIN-CONFIG");
        Put(Path.Combine(source,"content","cars","private","data.acd"), "PRIVATE-CONTENT");
        var catalog=ContentCatalog.Load(game);
        var request=new ServerCreationRequest { ServersRoot=servers, Folder="fresh", Name="Новый сервер", Track="track", Layout="layout",
            TcpPort=19600, UdpPort=19600, HttpPort=18081, StartMinutes=30,
            Cars=[new("car","red","none",2), new("car","generated","fixed",2)] };
        var profile=ServerCreation.Create(request,catalog,source);
        Check(profile.PlayerSlots==2 && profile.TrafficSlots==2 && profile.Name==request.Name, "New server configuration/slot counts");
        Check(profile.Slots.Select(s=>s.EntrySection).SequenceEqual(new[]{"CAR_0","CAR_1","CAR_2","CAR_3"}), "Creation entry numbering");
        Check(ConfigText.Get(profile.Ini,"SERVER","MAX_CLIENTS")=="4" && ConfigText.Get(profile.Ini,"SERVER","CARS")=="car", "Creation limits/CARS");
        Check(ConfigText.Get(profile.Ini,"SERVER","ADMIN_PASSWORD").Length>=8, "Generated admin password");
        Check(ConfigText.Get(profile.Ini,"SERVER","SUN_ANGLE")=="-200" && ConfigText.Get(profile.Ini,"PRACTICE","INFINITE")=="1", "Creation time/practice");
        Check(ConfigText.Get(profile.Ini,"PRACTICE","IS_OPEN")=="1", "Created practice must accept players joining");
        Check(profile.Extra.Contains("EnableAi: true") && profile.Extra.Contains("MaxPlayerCount: 2"), "AI configuration");
        Check(File.ReadAllText(Path.Combine(profile.Path,"content","cars","car","data.acd"))=="car-data", "Fresh car checksum copy");
        Check(File.ReadAllText(Path.Combine(profile.Path,"content","tracks","track","models_layout.ini"))=="models", "Fresh model checksum copy");
        Check(File.ReadAllText(Path.Combine(profile.Path,"content","tracks","track","layout","data","surfaces.ini"))=="surfaces", "Fresh surfaces copy");
        Check(File.Exists(Path.Combine(profile.Path,"content","tracks","track","layout","ai","fast_lane.aip")), "Traffic spline copy");
        Check(!File.Exists(Path.Combine(profile.Path,"admins.txt")) && !Directory.Exists(Path.Combine(profile.Path,"content","cars","private")) && !profile.Ini.Contains("PRIVATE"), "Private data copied from runtime source");
        Check(Directory.Exists(Path.Combine(profile.Path,"plugins")) && File.ReadAllText(Path.Combine(profile.Path,"plugins","ExamplePlugin","ExamplePlugin.dll"))=="runtime-plugin"
            && !File.Exists(Path.Combine(profile.Path,"plugins","ExamplePlugin","config.yml")), "Plugin runtime installation/privacy");
        Reject(()=>ServerCreation.Create(request,catalog,source), "Existing server overwritten");
        Check(ServerProfile.Load(profile.Path).OriginalBytes.All(p=>p.Value.SequenceEqual(profile.OriginalBytes[p.Key])), "Rejected overwrite changed original files");
        foreach(var folder in new[]{"..","../escape","CON","bad:folder","trailing."," padded"}) Reject(()=>ServerCreation.Validate(request with{Folder=folder},catalog), "Invalid folder accepted: " + folder);
        Reject(()=>ServerCreation.Validate(request with{Folder="inside",ServersRoot=game},catalog), "Server inside game accepted");
        Reject(()=>ServerCreation.Validate(request with{Folder="ports",TcpPort=18081},catalog), "Shared TCP/HTTP accepted");
        Reject(()=>ServerCreation.Validate(request with{Folder="slots",Cars=[new("car","red","none",9)]},catalog), "Pit capacity exceeded");
        Reject(()=>ServerCreation.Validate(request with{Folder="no-player",Cars=[new("car","red","fixed",1)]},catalog), "No player slots accepted");
        Reject(()=>ServerCreation.Validate(request with{Folder="bad-admin",AdminPassword="short"},catalog), "Short admin password accepted");
        Reject(()=>ServerCreation.Validate(request with{Folder="bad-skin",Cars=[new("car","missing","none",1)]},catalog), "Missing skin accepted");
        File.Move(Path.Combine(source,"steam_api64.dll"),Path.Combine(source,"steam_api64.bak"));
        Reject(()=>ServerCreation.Create(request with{Folder="incomplete"},catalog,source), "Incomplete runtime accepted");
        Check(!Directory.Exists(Path.Combine(servers,"incomplete")), "Failed runtime published a server");
        File.Move(Path.Combine(source,"steam_api64.bak"),Path.Combine(source,"steam_api64.dll"));
        using(var canceled=new CancellationTokenSource()) { canceled.Cancel(); Reject(()=>ServerCreation.Create(request with{Folder="canceled"},catalog,source,null,canceled.Token), "Cancellation ignored"); }
        Check(!Directory.Exists(Path.Combine(servers,"canceled")), "Canceled server leaked");
        void Zip(string path, bool malicious)
        {
            using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) archive.CreateEntryFromFile(file,"package/"+Path.GetRelativePath(source,file).Replace('\\','/'));
            using(var privateEntry=new StreamWriter(archive.CreateEntry("package/cfg/server_cfg.ini").Open())) privateEntry.Write("PRIVATE");
            if(malicious) { using var bad=new StreamWriter(archive.CreateEntry("../../escape.txt").Open()); bad.Write("escape"); }
        }
        var zip=Path.Combine(root,"runtime.zip"); Zip(zip,false);
        var zipped=ServerCreation.Create(request with{Folder="from-zip"},catalog,zip);
        Check(zipped.Name==request.Name && !zipped.Ini.Contains("PRIVATE"), "Wrapped ZIP import");
        Check(File.Exists(Path.Combine(zipped.Path,"plugins","ExamplePlugin","ExamplePlugin.dll")) && !File.Exists(Path.Combine(zipped.Path,"plugins","ExamplePlugin","config.yml")), "ZIP plugin runtime installation/privacy");
        var unsafeZip=Path.Combine(root,"unsafe.zip"); Zip(unsafeZip,true);
        Reject(()=>ServerCreation.Create(request with{Folder="unsafe"},catalog,unsafeZip), "ZIP traversal accepted");
        Check(!Directory.Exists(Path.Combine(servers,"unsafe")) && !File.Exists(Path.Combine(root,"escape.txt")), "Unsafe ZIP escaped staging");
        var collision=request with{Folder="race"};
        var progress=new InlineProgress(text=> { if(text==UiText.T("Устанавливаю AssettoServer…")) Put(Path.Combine(servers,"race","marker.txt"),"keep"); });
        Reject(()=>ServerCreation.Create(collision,catalog,source,progress), "Concurrent target creation overwritten");
        Check(File.ReadAllText(Path.Combine(servers,"race","marker.txt"))=="keep" && !Directory.Exists(Path.Combine(servers,"race","cfg")), "Concurrent target altered");
        File.Delete(Path.Combine(game,"content","tracks","track","layout","ai","fast_lane.aip"));
        Reject(()=>ServerCreation.Validate(request with{Folder="no-spline"},catalog), "Missing traffic spline accepted");
        Put(Path.Combine(game,"content","tracks","track","layout","ai","fast_lane.ai"), "game-spline");
        var customSpline=Path.Combine(root,"custom.aip"); Put(customSpline,"custom-traffic");
        var custom=ServerCreation.Create(request with{Folder="custom-spline",AiSpline=customSpline},catalog,source);
        Check(File.ReadAllText(Path.Combine(custom.Path,"content","tracks","track","layout","ai","fast_lane.aip"))=="custom-traffic", "Custom traffic spline copy");
        Check(!File.Exists(Path.Combine(custom.Path,"content","tracks","track","layout","ai","fast_lane.ai")) && File.ReadAllText(Path.Combine(game,"content","tracks","track","layout","ai","fast_lane.ai"))=="game-spline", "Custom spline did not take precedence or altered game content");
        ServerCreation.Validate(request with{Folder="without-ai",Cars=[new("car","red","none",1)]},catalog);
        Check(!Directory.EnumerateDirectories(servers,".acsm-create-*").Any(), "Staging directories leaked");
        UiText.Language="en";
        Check(UiText.T("Создать сервер")=="Create server", "English wizard action missing");
        UiText.Language="ru";
        Console.WriteLine("PASS: fresh server folder/ZIP installation, configs, checksums, AI, private-data isolation, overwrite refusal, input validation, cancellation, incomplete runtime rollback, ZIP confinement and concurrent target protection.");
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value)=>action(value); }
}
