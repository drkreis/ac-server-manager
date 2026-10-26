using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ServerManager.Core;

internal static class TemplateTests
{
    public static void Run(string root)
    {
        void Check(bool result, string message) { if (!result) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
        void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var servers = Path.Combine(root, "servers"); var source = Path.Combine(servers, "source");
        Put(Path.Combine(source, "cfg/server_cfg.ini"), "[SERVER]\nNAME=Original\nTRACK=track\nCONFIG_TRACK=layout\nTCP_PORT=9600\nUDP_PORT=9600\nHTTP_PORT=8081\nMAX_CLIENTS=3\nPASSWORD=private-password\nADMIN_PASSWORD=old-admin-secret\nREGISTER_TO_LOBBY=1\nFUEL_RATE=25\nDAMAGE_MULTIPLIER=10\nTYRE_WEAR_RATE=50\nSUN_ANGLE=80\nUNKNOWN=retained\n[PRACTICE]\nTIME=90\n");
        Put(Path.Combine(source, "cfg/entry_list.ini"), "[CAR_0]\nMODEL=car\nSKIN=red\nAI=none\nGUID=private-guid\nDRIVERNAME=private-driver\nTEAM=private-team\nBALLAST=12\n[CAR_1]\nMODEL=car\nSKIN=generated\nAI=fixed\nGUID=another-guid\n");
        Put(Path.Combine(source, "cfg/extra_cfg.yml"), "EnableAi: true\nEnableWeatherFx: true\n---\n!ExampleConfiguration\nApiToken: private-token\n");
        foreach (var name in new[] { "AssettoServer.exe", "steam_api64.dll", "csp_xxhash3.dll", "LICENSE" }) Put(Path.Combine(source, name), "fixture runtime, never executed");
        foreach (var name in new[] { "logs/session.log", "backups/old/server_cfg.ini", "cache/file.bin", "admins.txt", "whitelist.txt", "blacklist.txt", "notes-private.txt", "plugins/Example/Example.dll", "plugins/Example/config.yml", "cfg/custom.yml", "content/cars/car/data.acd", "content/tracks/track/layout/ai/fast_lane.ai", "welcome.txt" }) Put(Path.Combine(source, name), "fixture " + name);
        var original = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        ServerPortSelection Ports()
        {
            for (var i=0;i<100;i++)
            {
                using var a = new TcpListener(IPAddress.Loopback, 0); a.Start(); using var b = new TcpListener(IPAddress.Loopback, 0); b.Start();
                var tcp = ((IPEndPoint)a.LocalEndpoint).Port; var http = ((IPEndPoint)b.LocalEndpoint).Port;
                a.Stop(); b.Stop();
                if (tcp != 9600 && tcp != 8081 && http != 9600 && http != 8081 && ServerPorts.FindConflicts(tcp,http,tcp).Length == 0) return new(tcp,tcp,http);
            }
            throw new IOException("No fixture ports.");
        }
        var ports = Ports(); var request = new ServerCloneRequest(source, servers, "copy", "Copy", ports.Tcp, ports.Udp, ports.Http);
        var count = 0; var clone = ServerClone.Create(request, ensureStopped: () => count++);
        Check(count == 2 && clone.Slots.Count == 2 && clone.Track == "track" && clone.Layout == "layout", "Clone lost content choices or did not recheck process state.");
        Check(ConfigText.Get(clone.Ini, "SERVER", "NAME") == "Copy" && ConfigText.Get(clone.Ini, "SERVER", "TCP_PORT") == ports.Tcp.ToString() && ConfigText.Get(clone.Ini, "SERVER", "PASSWORD") == ""
            && ConfigText.Get(clone.Ini, "SERVER", "ADMIN_PASSWORD").Length >= 16 && !clone.Ini.Contains("old-admin-secret") && ConfigText.Get(clone.Ini,"SERVER","REGISTER_TO_LOBBY") == "0", "Clone reused network/secret identity.");
        Check(!clone.Entries.Contains("private-") && !clone.Entries.Contains("another-guid") && clone.Entries.Contains("BALLAST=12") && clone.Ini.Contains("UNKNOWN=retained"), "Clone retained player identities or lost unknown INI settings.");
        Check(!clone.Extra.Contains("private-token") && ConfigText.GetYaml(clone.Extra,"EnableAi") == "true" && File.Exists(Path.Combine(clone.Path,"content/cars/car/data.acd")), "Default clone leaked advanced settings or omitted content.");
        foreach (var name in new[] { "logs", "backups", "cache", "admins.txt", "notes-private.txt", "cfg/custom.yml" }) Check(!Path.Exists(Path.Combine(clone.Path, name)), "Clone copied excluded item: " + name);
        Check(Directory.Exists(Path.Combine(clone.Path,"plugins")) && !Directory.EnumerateFileSystemEntries(Path.Combine(clone.Path,"plugins")).Any(), "Default clone needs an empty plugin directory for AssettoServer compatibility.");
        Check(original.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "Clone changed source bytes.");
        var advanced = ServerClone.Create(request with { Folder="advanced", IncludeAdvanced=true });
        Check(advanced.Extra.Contains("!ExampleConfiguration") && advanced.Extra.Contains("private-token") && File.Exists(Path.Combine(advanced.Path,"plugins/Example/config.yml")) && File.Exists(Path.Combine(advanced.Path,"cfg/custom.yml")), "Explicit advanced clone did not preserve plugin documents/settings.");
        Reject(() => ServerClone.Validate(request with {Folder="COM¹"}), "Reserved Unicode device name accepted.");
        Reject(() => ServerClone.Create(request), "Clone overwrote a destination.");
        Reject(() => ServerClone.Create(request with {Folder="unsafe",Tcp=9600}), "Clone reused source TCP port.");
        Reject(() => ServerClone.Create(request with {ServersRoot=source,Folder="inside"}), "Clone nested inside its source.");
        Reject(() => ServerClone.Create(request with {Folder="game"}, source), "Clone accepted the game directory.");
        using (var blocked = new TcpListener(IPAddress.Any, ports.Http))
        {
            blocked.Start(); Reject(() => ServerClone.Create(request with {Folder="blocked"}), "Clone ignored occupied ports.");
        }
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject(() => ServerClone.Create(request with {Folder="cancelled"}, cancellation:cancelled.Token), "Clone ignored cancellation.");
        var cancelDuring = new CancellationTokenSource();
        Reject(() => ServerClone.Create(request with {Folder="mid-cancel"}, progress:new InlineProgress(_ => cancelDuring.Cancel()), cancellation:cancelDuring.Token), "Mid-copy cancellation was ignored.");
        var edited = false;
        Reject(() => ServerClone.Create(request with {Folder="changed"}, progress:new InlineProgress(_ => {if(!edited){edited=true;File.AppendAllText(Path.Combine(source,"cfg/server_cfg.ini"),"; external\n");}})), "Source change during copy was ignored.");
        foreach (var (file,bytes) in original) File.WriteAllBytes(file,bytes);
        Check(!Directory.EnumerateDirectories(servers).Any(p => Path.GetFileName(p).StartsWith(".acsm-clone-")) && !Directory.Exists(Path.Combine(servers,"changed")), "Failed clone left staging or a destination.");
        var profile = ServerProfile.Load(source);
        var inactiveProfile = ServerProfile.Load(source); inactiveProfile.AcceptSaved(new ServerProfile{Path=profile.Path,Ini=profile.Ini,Entries=profile.Entries,Extra="EnableAi: false\n",OriginalBytes=profile.OriginalBytes});
        var inactiveAi = ServerPresets.Capture(inactiveProfile,"No AI");
        // The basic AI switch is independent of stored slot roles.
        Check(!inactiveAi.EnableAi,"Preset enabled traffic which was disabled in its source.");
        var preset = ServerPresets.Capture(profile,"Evening traffic");
        var library = Path.Combine(root,"preferences/presets.json"); ServerPresets.Save(library,preset);
        var json = File.ReadAllText(library);
        Check(!json.Contains("private-") && !json.Contains("old-admin") && !json.Contains("TcpPort") && !json.Contains("HttpPort") && !json.Contains(source.Replace("\\","\\\\")), "Preset contains private/source/network fields.");
        var loaded = ServerPresets.Load(library).Single();
        Check(loaded.Name==preset.Name && loaded.Cars.SequenceEqual(preset.Cars) && loaded.StartMinutes==1080 && loaded.PracticeMinutes==90 && loaded.FuelRate==25 && loaded.SlotLimit==3, "Preset did not round-trip fields or slot order.");
        Reject(()=>ServerPresets.Save(library,preset with{Id=Guid.NewGuid().ToString("N")}),"Duplicate preset name accepted.");
        Reject(()=>ServerPresets.Save(library,preset with{Track="../outside"}),"Unsafe content ID accepted.");
        Reject(()=>ServerPresets.Save(library,preset with{FuelRate=501}),"Invalid rate accepted.");
        ServerPresets.Save(library,preset with{Name="Renamed"});
        File.WriteAllText(library,"broken"); Check(ServerPresets.Load(library).Count==1,"Preset library did not recover its backup.");
        ServerPresets.Save(library,preset with{Name="Recovered"}); ServerPresets.Delete(library,preset.Id); Check(ServerPresets.Load(library).Count==0,"Preset deletion failed.");
        File.WriteAllText(library,"{\"Format\":999}"); File.Delete(library+".bak");
        Reject(()=>ServerPresets.Save(library,preset),"Invalid existing library was overwritten.");
        Check(File.ReadAllText(library)=="{\"Format\":999}","Invalid library was destroyed.");
        var game = new ContentCatalog{GamePath=Path.Combine(root,"game")};
        Reject(()=>ServerPresets.CheckContent(preset,game),"Missing preset content was silently accepted.");
        game.Tracks.Add(new("track","layout","Track",game.GamePath,null,null,8));
        game.Cars.Add(new("car","Car","Brand",game.GamePath,null,[new("red","Red",null)]));
        ServerPresets.CheckContent(preset,game);
        var built = ServerCreation.BuildConfiguration(new(){ServersRoot=servers,Folder="preset",Name="New",Track=preset.Track,Layout=preset.Layout,Cars=preset.Cars,FuelRate=preset.FuelRate,DamageRate=preset.DamageRate,TyreWearRate=preset.TyreWearRate,PracticeMinutes=preset.PracticeMinutes,SlotLimit=preset.SlotLimit,InfinitePractice=preset.InfinitePractice,EnableAi=preset.EnableAi});
        Check(ConfigText.Get(built["server_cfg.ini"],"SERVER","FUEL_RATE")=="25" && ConfigText.Get(built["server_cfg.ini"],"PRACTICE","TIME")=="90" && ConfigText.Get(built["server_cfg.ini"],"SERVER","MAX_CLIENTS")=="3", "Preset settings were not written by creation.");
        Console.WriteLine("PASS: independent clone identity, optional plugin settings, source preservation, path/port/overwrite guards, cancellation and source-change cleanup; private preset DTOs, ordering, validation, recovery, rename/delete and missing-content detection.");
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
}
