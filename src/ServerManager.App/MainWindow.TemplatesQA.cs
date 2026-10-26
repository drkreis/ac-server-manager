using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    internal async Task TemplatesUiTest(string output)
    {
        output=Path.GetFullPath(output);if(Directory.Exists(output))throw new IOException("Use fresh output.");Directory.CreateDirectory(output);
        void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
        void Put(string file,string text){Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,text);}
        async Task Wait(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(12);while(!done()){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
        async Task Capture(FrameworkElement element,string name)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);element.UpdateLayout();var visual=new DrawingVisual();var area=new Rect(0,0,element.ActualWidth,element.ActualHeight);
            using(var drawing=visual.RenderOpen()){drawing.DrawRectangle((Brush)FindResource("Bg"),null,area);drawing.DrawRectangle(new VisualBrush(element),null,area);}
            var image=new RenderTargetBitmap((int)Math.Ceiling(area.Width),(int)Math.Ceiling(area.Height),96,96,PixelFormats.Pbgra32);image.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name));png.Save(file);
        }
        var root=Path.Combine(output,"servers");var source=Path.Combine(root,"source");var game=Path.Combine(output,"game");
        Put(Path.Combine(source,"cfg/server_cfg.ini"),"[SERVER]\nNAME=Template fixture\nTRACK=track\nCONFIG_TRACK=layout\nMAX_CLIENTS=3\nTCP_PORT=9600\nUDP_PORT=9600\nHTTP_PORT=8081\nFUEL_RATE=25\nDAMAGE_MULTIPLIER=10\nTYRE_WEAR_RATE=50\nPASSWORD=fixture-secret\n[PRACTICE]\nTIME=90\n");
        Put(Path.Combine(source,"cfg/entry_list.ini"),"[CAR_0]\nMODEL=car\nSKIN=red\nAI=none\nGUID=private-test-guid\n[CAR_1]\nMODEL=car\nSKIN=generated\nAI=fixed\n");
        Put(Path.Combine(source,"cfg/extra_cfg.yml"),"EnableAi: true\nEnableWeatherFx: true\n");
        foreach(var name in new[]{"AssettoServer.exe","steam_api64.dll","csp_xxhash3.dll"})Put(Path.Combine(source,name),"Fixture only; never executed");
        Put(Path.Combine(source,"content/cars/car/data.acd"),"data");Put(Path.Combine(game,"content/cars/car/data.acd"),"data");
        Put(Path.Combine(game,"content/cars/car/ui/ui_car.json"),"{\"name\":\"Fixture Car\"}");Put(Path.Combine(game,"content/cars/car/skins/red/ui_skin.json"),"{\"skinname\":\"Red\"}");
        Put(Path.Combine(game,"content/tracks/track/ui/layout/ui_track.json"),"{\"name\":\"Fixture Track - Layout\",\"pitboxes\":\"8\"}");
        Put(Path.Combine(game,"content/tracks/track/layout/data/surfaces.ini"),"[SURFACE_0]\nKEY=ROAD\n");Put(Path.Combine(game,"content/tracks/track/layout/ai/fast_lane.ai"),"fixture");
        ServerRoot.Text=root;GameRoot.Text=game;await InitializeAsync();await RefreshProcessStatesAsync();
        var original=ServerBackups.ConfigurationFiles.ToDictionary(n=>n,n=>File.ReadAllBytes(Path.Combine(source,"cfg",n)));
        var row=(ServerRow)ServerList.SelectedItem;
        void Answer(Action action,bool accept,string? input=null)
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>{if(Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d=>d.IsLoaded&&d.IsVisible) is not {} d)return;timer.Stop();if(input!=null)d.DialogInput.Text=input;(accept?d.AcceptButton:d.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            timer.Start();try{action();}finally{timer.Stop();}
        }
        Answer(()=>SavePreset(new MenuItem{Tag=row},new RoutedEventArgs()),true,"Evening traffic");
        var preset=ServerPresets.Load(PresetsFile).Single();Check(preset.Name=="Evening traffic"&&!File.ReadAllText(PresetsFile).Contains("fixture-secret"),"Save preset included private data or failed.");
        var library=new PresetsWindow(PresetsFile){Owner=this};library.Show();library.PresetList.SelectedIndex=0;
        await Capture((FrameworkElement)library.Content,"presets-en.png");
        Answer(()=>library.RenameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),true,"Evening renamed");Check(ServerPresets.Load(PresetsFile).Single().Name=="Evening renamed","Preset rename failed.");
        library.PresetList.SelectedIndex=0;Answer(()=>library.RemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),false);Check(ServerPresets.Load(PresetsFile).Count==1,"Cancelled deletion removed preset.");
        Answer(()=>library.RemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),true);Check(ServerPresets.Load(PresetsFile).Count==0,"Confirmed preset removal failed.");
        library.Close();ServerPresets.Save(PresetsFile,preset);
        var wizard=new CreateServerWindow(root,game,source,catalog,PresetsFile){Owner=this};wizard.Show();await wizard.ExercisePresetsAsync(Capture);wizard.Close();
        // Exercise the actual context-menu path, dialog and asynchronous selection of the new server.
        var copied=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloneTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        cloneTimer.Tick+=async(_,_)=>
        {
            if(Application.Current.Windows.OfType<CloneServerWindow>().FirstOrDefault(w=>w.IsLoaded&&w.IsVisible&&w.TcpPort.Text.Length>0) is not {} w)return;
            cloneTimer.Stop();
            try{w.FolderName.Text="ui-copy";w.ServerName.Text="UI copy";await Capture((FrameworkElement)w.Content,"clone-en.png");await w.CopyAsync();if(w.CreatedPath==null)throw new IOException(w.CloneMessage.Text);copied.SetResult();}
            catch(Exception ex){w.Close();copied.SetException(ex);}
        };
        cloneTimer.Start();CloneServer(new MenuItem{Tag=row},new RoutedEventArgs());await copied.Task;
        await Wait(()=>profile?.Folder=="ui-copy"&&!folderOperation);
        Check(ServerList.Items.Count==2&&managed.Count==0&&!dirty,"Successful copy did not select a stopped independent server.");
        Check(original.All(p=>File.ReadAllBytes(Path.Combine(source,"cfg",p.Key)).SequenceEqual(p.Value)),"Template UI modified source configurations.");
        LanguagePicker.SelectedIndex=0;var ru=new PresetsWindow(PresetsFile){Owner=this};ru.Show();await Capture((FrameworkElement)ru.Content,"presets-ru.png");ru.Close();
        var cloneRu=new CloneServerWindow(ServerProfile.Load(source),root,game,CloneServerAsync){Owner=this};cloneRu.Show();await Wait(()=>cloneRu.TcpPort.Text.Length>0);await Capture((FrameworkElement)cloneRu.Content,"clone-ru.png");cloneRu.Close();
        File.WriteAllText(Path.Combine(output,"templates-result.txt"),"PASS: saved-version preset capture/privacy, library rename/delete/Cancel, wizard application and identity/port preservation, replacement Cancel/confirm, missing-content draft preservation, review settings/layout retention, actual context-menu cloning and stopped-copy selection, unchanged source bytes, isolated library storage and RU/EN dialogs. No real server or game files modified.");
    }
}
