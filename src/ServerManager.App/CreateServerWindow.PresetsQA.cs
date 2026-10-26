using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ServerManager.Core;

namespace ServerManager.App;

public partial class CreateServerWindow
{
    internal async Task ExercisePresetsAsync(Func<FrameworkElement,string,Task> capture)
    {
        void Check(bool ok,string reason) { if (!ok) throw new InvalidOperationException(reason); }
        ServerName.Text = "Wizard name stays"; FolderName.Text = "wizard-new"; JoinPassword.Password = "wizard-private"; AdminPassword.Password = "wizard-admin-private";
        var originalPorts = (TcpPort.Text,UdpPort.Text,HttpPort.Text); var root = ServersPath.Text; var source = SourcePath.Text; var mode = SourceMode.SelectedIndex;
        PresetChoice.SelectedIndex = 0; var preset = (ServerPreset)PresetChoice.SelectedItem; Check(preset.ToString() == preset.Name, "Preset combo exposes record internals.");
        await ApplyPresetAsync();
        Check(chosen.Count == preset.Cars.Count && Layouts.SelectedItem is TrackInfo { Layout:"layout" } && FuelRate.Text == "25", "Preset did not populate wizard controls.");
        Check(ServerName.Text == "Wizard name stays" && FolderName.Text == "wizard-new" && JoinPassword.Password == "wizard-private" && AdminPassword.Password == "wizard-admin-private"
            && originalPorts == (TcpPort.Text,UdpPort.Text,HttpPort.Text) && root == ServersPath.Text && source == SourcePath.Text && mode == SourceMode.SelectedIndex, "Applying a preset changed identity, paths, runtime or ports.");
        await capture(WizardRoot,"wizard-preset-en.png");
        async Task ApplyWithAnswer(bool yes)
        {
            var timer = new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=> {if(Application.Current.Windows.OfType<AppDialog>().FirstOrDefault(d=>d.IsLoaded&&d.IsVisible) is not {} d)return; timer.Stop();(yes?d.AcceptButton:d.CancelButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
            timer.Start();try{await ApplyPresetAsync();}finally{timer.Stop();}
        }
        FuelRate.Text="66";await ApplyWithAnswer(false);Check(FuelRate.Text=="66","Preset Cancel discarded edits.");
        await ApplyWithAnswer(true);Check(FuelRate.Text=="25","Confirmed preset replacement failed.");
        InfinitePractice.IsChecked=true;PracticeMinutes.Text="unused";
        Check(BuildRequest().InfinitePractice && BuildRequest().PracticeMinutes==120,"An unused duration blocked infinite practice.");
        InfinitePractice.IsChecked=preset.InfinitePractice;PracticeMinutes.Text=preset.PracticeMinutes.ToString();
        var before=BuildRequest();var missing=preset with{Id=Guid.NewGuid().ToString("N"),Name="Missing",Track="missing-track"};
        PresetChoice.ItemsSource=new[]{preset,missing};PresetChoice.SelectedItem=missing;await ApplyPresetAsync();
        var after=BuildRequest();Check(before.Track==after.Track && before.Cars.SequenceEqual(after.Cars) && before.FuelRate==after.FuelRate && Message.Text.Contains("missing-track"),"Missing content changed the draft or did not identify the missing ID.");
        // Going through Basics again must preserve the layout and preset settings.
        async Task Advance()
        {
            var prior=step;NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline=DateTime.UtcNow.AddSeconds(10);while(busy){if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(10);}
            Check(step==prior+1,"Wizard step blocked: "+Message.Text);
        }
        await Advance();Check((Layouts.SelectedItem as TrackInfo)?.Layout=="layout","Advancing Basics reset the selected layout.");
        await Advance();await Advance();await capture(WizardRoot,"wizard-preset-settings-en.png");await Advance();
        Check(reviewedRequest?.PracticeMinutes==90 && !reviewedRequest.InfinitePractice && reviewedRequest.SlotLimit==3 && reviewedRequest.FuelRate==25,"Review request lost preset settings.");
    }
}
