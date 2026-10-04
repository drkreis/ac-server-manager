using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ServerManager.Core;

namespace ServerManager.App;

public partial class CreateServerWindow
{
    internal async Task ExerciseWizard(string output, bool download)
    {
        Directory.CreateDirectory(output);
        async Task Capture(string name)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            WizardRoot.UpdateLayout();
            var width=(int)Math.Ceiling(WizardRoot.ActualWidth); var height=(int)Math.Ceiling(WizardRoot.ActualHeight);
            var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(WizardRoot),null,new Rect(0,0,width,height));
            var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(visual);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file=File.Create(Path.Combine(output,name)); png.Save(file);
        }
        async Task Advance()
        {
            var old=step; NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while(busy && DateTime.UtcNow<deadline) await Task.Delay(50);
            if(step!=old+1) throw new InvalidOperationException("Wizard step blocked: " + Message.Text);
        }
        SourceMode.SelectedIndex=download ? 0 : 1;
        FolderName.Text="qa-created"; ServerName.Text="ACSM Wizard QA";
        await Capture("wizard-1-basics.png");
        var before=step; FolderName.Text="..";
        NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(step!=before || Message.Text.Length==0) throw new InvalidOperationException("Wizard accepted invalid folder.");
        FolderName.Text="qa-created"; await Advance();
        Tracks.SelectedItem=Tracks.Items.OfType<TrackGroup>().First(g=>g.Id=="imola");
        await Capture("wizard-2-track.png"); await Advance();
        NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(step!=2 || Message.Text.Length==0) throw new InvalidOperationException("Wizard accepted empty car selection.");
        Cars.SelectedItem=catalog!.FindCar("ks_abarth500") ?? catalog.FindCar("abarth500") ?? catalog.Cars.First(c=>File.Exists(Path.Combine(c.Directory,"data.acd")));
        AddCar(this,new RoutedEventArgs()); chosen[0].Quantity="2";
        if(!SlotCount.Text.Contains('2')) throw new InvalidOperationException("Wizard slot count did not update.");
        if(!download) { AddCar(this,new RoutedEventArgs()); chosen[1].Role="fixed"; }
        await Capture("wizard-3-cars.png"); await Advance();
        var oldHttp=HttpPort.Text; HttpPort.Text=TcpPort.Text;
        NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(step!=3 || Message.Text.Length==0) throw new InvalidOperationException("Wizard accepted duplicate TCP/HTTP ports.");
        HttpPort.Text=oldHttp; Message.Text=""; await Capture("wizard-4-settings.png"); await Advance();
        if(!ReviewText.Text.Contains("qa-created") || !ReviewText.Text.Contains("2")) throw new InvalidOperationException("Wizard review did not match draft.");
        await Capture("wizard-5-review.png");
        UiText.Language="en"; Localization.Current.Apply("en"); ShowStep();
        if((string)NextButton.Content!="Create server") throw new InvalidOperationException("English wizard action missing.");
        if(!ReviewText.Text.StartsWith("Name:")) throw new InvalidOperationException("Wizard review did not switch to English.");
        await Capture("wizard-english-review.png");
        Localization.Current.Apply("ru"); ShowStep();
        await CreateAsync();
        if(CreatedPath==null) throw new InvalidOperationException("Wizard creation failed: " + Message.Text, creationError);
        var created=ServerProfile.Load(CreatedPath);
        if(created.PlayerSlots!=2 || created.TrafficSlots!=(download ? 0 : 1) || created.Track!="imola" || !File.Exists(Path.Combine(CreatedPath,"AssettoServer.exe"))) throw new InvalidOperationException("Wizard created incorrect server.");
        File.WriteAllText(Path.Combine(output,"wizard-result.txt"),"PASS: five wizard steps, invalid folder/empty slots/port collision gates, quantity updates, review, RU/EN resources and creation of a new server from " + (download ? "the official download" : "a local runtime folder") + ".\nCreated: " + CreatedPath);
    }
}
