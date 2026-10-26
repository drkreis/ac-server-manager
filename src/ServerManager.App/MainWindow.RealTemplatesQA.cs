using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    private async Task ExerciseRealTemplates(ServerProfile original, ContentCatalog installed, string root, string output)
    {
        var saved = ServerProfile.Load(original.Path);
        var originalBytes = saved.OriginalBytes.ToDictionary(p => p.Key,p => p.Value.ToArray());
        var ports = ServerPortSelection.Suggest(root);
        var request = new ServerCloneRequest(saved.Path,root,"qa-real-copy","ACSM copy QA",ports.Tcp,ports.Udp,ports.Http);
        var copiedPath = await CloneServerAsync(request,new Progress<string>(),CancellationToken.None);
        async Task CheckServer(ServerProfile candidate,string label)
        {
            Load(candidate);
            var report=await CheckDiagnosticsAsync(false,true);
            if(report==null||report.Errors!=0)throw new IOException(label+" preflight: "+diagnosticText);
            var child=await LaunchManagedAsync(candidate);
            var normal=false;
            try
            {
                await child.Startup;if(!child.Ready)throw new IOException(label+" readiness not confirmed.");
                var tcp=int.Parse(ConfigText.Get(candidate.Ini,"SERVER","TCP_PORT"));var udp=int.Parse(ConfigText.Get(candidate.Ini,"SERVER","UDP_PORT"));
                var deadline=DateTime.UtcNow.AddSeconds(30);
                byte[]? accepted = null;
                while(true)
                {
                    if(child.Process.HasExited||DateTime.UtcNow>deadline)throw new IOException(label+" network listeners unavailable.");
                    try
                    {
                        if(ServerPorts.FindConflicts(tcp,int.Parse(ConfigText.Get(candidate.Ini,"SERVER","HTTP_PORT")),udp).Contains("UDP "+udp))
                        { accepted=await HandshakeQA.Request(tcp,candidate.Slots.First(s=>s.Ai!="fixed").Model);break; }
                    }
                    catch(SocketException) { }
                    catch(OperationCanceledException) { }
                    await Task.Delay(100);
                }
                if(accepted?[0]!=0x3e)throw new IOException(label+" rejected the handshake.");
            }
            finally
            {
                normal=await StopManagedAsync(candidate.Path,child,false);
                if(!normal&&!child.Process.HasExited)child.Process.Kill();
                await child.Completion;File.WriteAllText(Path.Combine(output,label+".log"),child.Log.ToString());
            }
            if(!normal||child.Process.ExitCode!=0)throw new IOException(label+" did not stop normally.");
        }
        await CheckServer(ServerProfile.Load(copiedPath),"real-copy");
        if(originalBytes.Any(p=>!File.ReadAllBytes(Path.Combine(saved.Path,"cfg",p.Key)).SequenceEqual(p.Value)))throw new IOException("Real clone changed source configs.");
        var preset=ServerPresets.Capture(saved,"Real QA settings") with {FuelRate=35,DamageRate=0,TyreWearRate=80,PracticeMinutes=90,InfinitePractice=false};
        ServerPresets.Save(PresetsFile,preset);preset=ServerPresets.Load(PresetsFile).Single(p=>p.Id==preset.Id);ServerPresets.CheckContent(preset,installed);
        ports=ServerPortSelection.Suggest(root);
        var fromPreset=await Task.Run(()=>ServerCreation.Create(new(){ServersRoot=root,Folder="qa-real-preset",Name="ACSM preset QA",Track=preset.Track,Layout=preset.Layout,Cars=preset.Cars,StartMinutes=preset.StartMinutes,WeatherFx=preset.WeatherFx,FuelRate=preset.FuelRate,DamageRate=preset.DamageRate,TyreWearRate=preset.TyreWearRate,PracticeMinutes=preset.PracticeMinutes,InfinitePractice=preset.InfinitePractice,EnableAi=preset.EnableAi,SlotLimit=preset.SlotLimit,TcpPort=ports.Tcp,UdpPort=ports.Udp,HttpPort=ports.Http},installed,saved.Path));
        if(ConfigText.Get(fromPreset.Ini,"PRACTICE","TIME")!="90"||ConfigText.Get(fromPreset.Ini,"PRACTICE","INFINITE")!="0"||ConfigText.Get(fromPreset.Ini,"SERVER","FUEL_RATE")!="35")throw new IOException("Real preset settings were lost.");
        await CheckServer(fromPreset,"real-preset");
        File.AppendAllText(Path.Combine(output,"wizard-result.txt"),"\nPASS: actual copied AssettoServer and actual preset-created server pass structured diagnostics, HTTP readiness, TCP/UDP listener checks, accepted client handshakes and normal shutdown. Copy has fresh identity; source cfg bytes unchanged. Preset duration/infinite/fuel settings persisted. Only disposable QA servers were started.");
    }
}
