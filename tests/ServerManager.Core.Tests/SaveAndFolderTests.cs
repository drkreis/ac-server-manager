using System.Text;
using ServerManager.Core;

internal static class SaveAndFolderTests
{
    public static void Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
        var server = Path.Combine(root, "server"); var cfg = Path.Combine(server, "cfg"); Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg,"server_cfg.ini"),"[SERVER]\r\nNAME=Visible game name\r\nTRACK=imola\r\nMAX_CLIENTS=1\r\nCARS=car\r\n",new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(cfg,"entry_list.ini"),"[CAR_0]\r\nMODEL=car\r\nSKIN=blue\r\nAI=none\r\nGUID=private\r\n");
        File.WriteAllText(Path.Combine(cfg,"extra_cfg.yml"),"EnableAi: false\nEnableWeatherFx: false\n");
        var profile = ServerProfile.Load(server); var before = profile.OriginalBytes.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        var times = before.Keys.ToDictionary(n=>n,n=>File.GetLastWriteTimeUtc(Path.Combine(cfg,n)));
        Check(profile.Save(profile.SavedTexts)==null && !Directory.Exists(Path.Combine(server,"backups")),"Unchanged save created a backup.");
        Check(times.All(p=>File.GetLastWriteTimeUtc(Path.Combine(cfg,p.Key))==p.Value),"Unchanged save touched files.");
        var changed = profile.SavedTexts; changed["server_cfg.ini"] = ConfigText.Set(changed["server_cfg.ini"],"SERVER","NAME","New game name");
        Check(profile.Save(changed)!=null,"Changed save produced no backup.");
        Check(File.GetLastWriteTimeUtc(Path.Combine(cfg,"entry_list.ini"))==times["entry_list.ini"],"Saving a name rewrote the slot list.");
        profile.AcceptSaved(ServerProfile.Load(server));
        var backups = Directory.EnumerateDirectories(Path.Combine(server,"backups")).Count();
        Check(profile.Save(profile.SavedTexts)==null && Directory.EnumerateDirectories(Path.Combine(server,"backups")).Count()==backups,"Repeated save added a backup.");
        var source = Path.Combine(root,"data.acd"); File.WriteAllText(source,"content");
        var copy = new CopyPlan(source,@"content\cars\car\data.acd");
        Check(profile.Save(profile.SavedTexts,[copy])!=null && File.Exists(Path.Combine(server,copy.RelativeDestination)),"Content-only save was skipped.");
        Check(profile.Save(profile.SavedTexts,[copy])==null,"Identical content copy added a backup.");
        File.AppendAllText(Path.Combine(cfg,"extra_cfg.yml"),"# external edit\n");
        Reject(()=>profile.Save(profile.SavedTexts),"Unchanged save ignored a stale baseline.");
        profile=ServerProfile.Load(server);
        var tree = Directory.EnumerateFiles(server,"*",SearchOption.AllDirectories).ToDictionary(f=>Path.GetRelativePath(server,f),File.ReadAllBytes);
        foreach(var invalid in new[]{"", "..", "../escape", "bad/name", "bad\\name", "CON", "NUL.txt", "COM1", "trailing.", " trailing"})
            Reject(()=>ServerRename.Validate(root,server,invalid,""),"Invalid rename accepted: "+invalid);
        Directory.CreateDirectory(Path.Combine(root,"occupied"));
        Reject(()=>ServerRename.Move(root,server,"occupied",""),"Rename overwrote a directory.");
        Reject(()=>ServerRename.Validate(root,server,"renamed",server),"Game directory could be renamed.");
        var destination=ServerRename.Move(root,server,"renamed"," ".Trim());
        Check(!Directory.Exists(server) && tree.All(p=>File.ReadAllBytes(Path.Combine(destination,p.Key)).SequenceEqual(p.Value)),"Folder rename changed or lost server data.");
        profile.Relocate(destination);
        Check(profile.Name=="New game name" && profile.Folder=="renamed" && profile.Save(profile.SavedTexts)==null,"Rename altered the game name or disk baseline.");
        var uppercase=ServerRename.Move(root,destination,"RENAMED","");
        Check(Path.GetFileName(Directory.EnumerateDirectories(root).Single(p=>Path.GetFileName(p).Equals("renamed",StringComparison.OrdinalIgnoreCase)))=="RENAMED", "Case-only rename failed.");
        Check(tree.All(p=>File.ReadAllBytes(Path.Combine(uppercase,p.Key)).SequenceEqual(p.Value)),"Case-only rename changed data.");
        var cmFolder=Path.Combine(root,"CM Portable"); Directory.CreateDirectory(cmFolder); var cm=Path.Combine(cmFolder,"Content Manager.exe"); File.WriteAllText(cm,"fixture executable");
        Check(ContentManagerLocator.ExecutableFromCommand('"'+cm+"\" \"%1\"")==cm,"Quoted protocol command was not parsed.");
        Check(ContentManagerLocator.ExecutableFromCommand(cm+" \"%1\"")==cm,"Unquoted protocol command was not parsed.");
        Check(ContentManagerLocator.Find("missing.exe",[],['"'+cm+"\" %1"],[])==cm,"Stale saved CM path prevented protocol discovery.");
        Check(ContentManagerLocator.Find("",[cm],[],[])==cm && ContentManagerLocator.Find("",[],[],[cmFolder])==cm,"Running or portable CM discovery failed.");
        Check(ContentManagerLocator.Find("",[],[],[root])==null,"CM discovery fabricated a location.");
        Console.WriteLine("PASS: unchanged and content-only saves, backup counts, untouched file timestamps, stale baseline checks, folder rename/data preservation/collisions/case-only names and Content Manager discovery.");
    }
}
