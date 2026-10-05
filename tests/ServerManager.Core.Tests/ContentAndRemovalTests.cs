using System.Text;
using ServerManager.Core;

internal static class ContentAndRemovalTests
{
    public static void Run(string root)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception(message); }
        var game = Path.Combine(root, "game"); var car = Path.Combine(game, "content", "cars", "legacy");
        var track = Path.Combine(game, "content", "tracks", "nordschleife", "ui", "tourist");
        Directory.CreateDirectory(track); Directory.CreateDirectory(Path.Combine(car, "ui")); Directory.CreateDirectory(Path.Combine(car, "skins", "blue"));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var carJson = "{\"name\":\"Coupé 90°\",\"brand\":\"Kunos\",\"country\":\"Italy\",\"year\":1999,\"class\":\"street\",\"author\":\"Author\",\"description\":\"First<br>second &amp; third\",\"tags\":[\"manual\",\"street\"],\"specs\":{\"bhp\":\"200 bhp\",\"weight\":\"1200 kg\"}}";
        var carFile=Path.Combine(car,"ui","ui_car.json"); File.WriteAllBytes(carFile, Encoding.GetEncoding(1252).GetBytes(carJson));
        var trackFile=Path.Combine(track,"ui_track.json"); File.WriteAllBytes(trackFile, Encoding.GetEncoding(1252).GetBytes("{\"name\":\"Nordschleife 20°\",\"pitboxes\":16}"));
        File.WriteAllText(Path.Combine(car,"skins","blue","ui_skin.json"), "{\"skinname\":\"Blue\"}", Encoding.Unicode);
        File.WriteAllText(Path.Combine(car,"skins","blue","livery.png"),"icon"); File.WriteAllText(Path.Combine(car,"skins","blue","preview.jpg"),"preview");
        var original = File.ReadAllBytes(carFile); var catalog=ContentCatalog.Load(game); var item=catalog.FindCar("legacy")!;
        Check(catalog.Warnings.Count==0 && item.Name=="Coupé 90°" && catalog.Tracks.Single().Name=="Nordschleife 20°", "Legacy Windows-1252 car/track metadata was not decoded.");
        Check(item.Details.Year=="1999" && item.Details.Country=="Italy" && item.Details.Description=="First\nsecond & third" && item.Details.Specs["bhp"]=="200 bhp" && item.Details.Tags.Count==2, "Car reference metadata lost fields or displayed HTML markup.");
        Check(item.Skins[0].Name=="Blue" && item.Skins[0].Icon!.EndsWith("livery.png") && item.Skins[0].Preview!.EndsWith("preview.jpg"),"Skin livery icons, previews or UTF-16 metadata not discovered.");
        Check(original.SequenceEqual(File.ReadAllBytes(carFile)),"Reading legacy metadata changed game files.");
        File.WriteAllText(carFile, "{\"name\":\"日本語 Машина\"}", new UTF8Encoding(true));
        Check(ContentCatalog.Load(game).Cars.Single().Name=="日本語 Машина", "UTF-8 metadata was incorrectly reinterpreted as legacy ANSI.");
        var steam=Path.Combine(root,"Steam"); var library=Path.Combine(root,"OtherSteamLibrary"); var steamGame=Path.Combine(library,"steamapps","common","Custom AC");
        Directory.CreateDirectory(Path.Combine(steam,"steamapps")); Directory.CreateDirectory(Path.Combine(steamGame,"content","cars")); Directory.CreateDirectory(Path.Combine(steamGame,"content","tracks"));
        File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"1\" { \"path\" \""+library.Replace("\\","\\\\")+"\" } }");
        File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_244210.acf"),"\"AppState\" { \"installdir\" \"Custom AC\" }");
        Check(ContentCatalog.FindGameInSteamLibraries([steam])==steamGame,"Steam discovery ignored an additional library or app manifest.");
        Check(ContentCatalog.FindGameInSteamLibraries([Path.Combine(root,"not-installed")])==null,"Steam discovery fabricated a game path.");
        Reject(() => ServerRemoval.Validate(root,root,game), "Non-server root was accepted for deletion.");
        var servers=Path.Combine(root,"servers"); var server=Path.Combine(servers,"test"); Directory.CreateDirectory(Path.Combine(server,"cfg"));
        foreach(var name in new[]{"server_cfg.ini","entry_list.ini","extra_cfg.yml"}) File.WriteAllText(Path.Combine(server,"cfg",name),"fixture");
        Check(ServerRemoval.Validate(servers,server,game)==Path.GetFullPath(server),"Selected stopped server path was not accepted.");
        Reject(()=>ServerRemoval.Validate(servers+"-other",server,game), "Removal escaped chosen servers root.");
        Reject(()=>ServerRemoval.Validate(servers,server,server), "Game directory was accepted for deletion.");
        Reject(()=>ServerRemoval.Validate(servers,server,Path.Combine(server,"installed-game")), "Ancestor of game directory was accepted for deletion.");
        var nested=Path.Combine(server,"nested","cfg"); Directory.CreateDirectory(nested); File.WriteAllText(Path.Combine(nested,"server_cfg.ini"),"fixture");
        Reject(()=>ServerRemoval.Validate(servers,server,game), "Directory containing another server was accepted for deletion.");
        Console.WriteLine("PASS: legacy Kunos/Unicode metadata, car reference fields, skin icons/previews, read-only game handling and server removal scope/game/nesting guards.");
    }
}
