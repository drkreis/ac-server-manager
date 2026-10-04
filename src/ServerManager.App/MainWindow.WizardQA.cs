using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    public async Task WizardSmokeTest(string output)
    {
        await InitializeAsync();
        Directory.CreateDirectory(output);
        var root=Path.Combine(Path.GetFullPath(output),"servers");
        var source=ServerList.Items.OfType<ServerRow>().Select(r=>r.Profile.Path).First(p=>File.Exists(Path.Combine(p,"AssettoServer.exe")));
        var wizard=new CreateServerWindow(root,GameRoot.Text,source,catalog) {Owner=this};
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _=Dispatcher.BeginInvoke(async () =>
        {
            try { await wizard.ExerciseWizard(output,args.Contains("--wizard-download-qa")); completion.SetResult(); }
            catch(Exception ex) { wizard.Close(); completion.SetException(ex); }
        });
        if(wizard.ShowDialog()!=true) await completion.Task;
        await completion.Task;
        var created=ServerProfile.Load(wizard.CreatedPath!);
        lastSelectedPath=created.Path; ServerRoot.Text=wizard.SelectedServersRoot; GameRoot.Text=wizard.SelectedGamePath;
        await InitializeAsync();
        if(profile?.Path!=created.Path || !Editor.IsEnabled) throw new InvalidOperationException("Created server was not selected in the editor.");
        var start=new ProcessStartInfo(Path.Combine(created.Path,"AssettoServer.exe")) {WorkingDirectory=created.Path,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using var process=Process.Start(start) ?? throw new InvalidOperationException("QA server did not start.");
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        var healthy=false;
        try
        {
            using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(2)};
            var port=ConfigText.Get(created.Ini,"SERVER","HTTP_PORT");
            for(var i=0;i<30 && !process.HasExited;i++)
            {
                await Task.Delay(1000);
                try
                {
                    using var response=await client.GetAsync("http://127.0.0.1:"+port+"/INFO");
                    var network=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                    var tcp=int.Parse(ConfigText.Get(created.Ini,"SERVER","TCP_PORT"));
                    var udp=int.Parse(ConfigText.Get(created.Ini,"SERVER","UDP_PORT"));
                    if(response.IsSuccessStatusCode && network.GetActiveTcpListeners().Any(p=>p.Port==tcp) && network.GetActiveUdpListeners().Any(p=>p.Port==udp))
                    {
                        var handshake=await HandshakeQA.Request(tcp,created.Slots.First(s=>s.Ai!="fixed").Model);
                        File.WriteAllText(Path.Combine(output,"handshake-result.txt"),"Handshake response: 0x"+handshake[0].ToString("X2"));
                        if(handshake[0]!=0x3e) throw new InvalidOperationException("Created server rejected the handshake: 0x"+handshake[0].ToString("X2"));
                        healthy=true; File.WriteAllText(Path.Combine(output,"server-info.json"),await response.Content.ReadAsStringAsync()); break;
                    }
                }
                catch(HttpRequestException) {} catch(TaskCanceledException) {}
            }
        }
        finally
        {
            if(!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            File.WriteAllText(Path.Combine(output,"created-server.log"),await stdout+"\n"+await stderr);
        }
        if(!healthy) throw new InvalidOperationException("Created server failed its HTTP /INFO and TCP/UDP listener checks. See created-server.log.");
        File.AppendAllText(Path.Combine(output,"wizard-result.txt"),"\nPASS: editor selects the new profile; actual AssettoServer startup, HTTP /INFO, TCP/UDP listeners and accepted AC client handshake; only the QA process was stopped.");
    }
}
