using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ServerManager.Core;

namespace ServerManager.App;

public partial class MainWindow
{
    internal async Task DetailsUiTest(string output)
    {
        Directory.CreateDirectory(output);
        if (!File.Exists(settingsFile) && (ServerRoot.Text != "" || UiText.Language != "en")) throw new InvalidOperationException("Fresh startup must have no assumed servers folder and use English.");
        await InitializeAsync();
        if (catalog == null) throw new InvalidOperationException("UI QA requires installed game content.");
        if (catalog.Warnings.Any(w => w.Contains("ks_nordschleife", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Kunos Nordschleife metadata still has decoding warnings.");
        var car = catalog.FindCar("ks_nissan_skyline_r34") ?? catalog.Cars.First(c => c.Skins.Count >= 2 && c.Skins.Any(s => s.Icon != null));
        var root=Path.Combine(Path.GetFullPath(output),"fixture-servers"); var cfg=Path.Combine(root,"fixture","cfg"); Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg,"server_cfg.ini"),"[SERVER]\nNAME=UI QA\nTRACK=imola\nMAX_CLIENTS=2\nTCP_PORT=9600\nUDP_PORT=9600\nHTTP_PORT=8081\nFUEL_RATE=100\nDAMAGE_MULTIPLIER=0\nTYRE_WEAR_RATE=100\n");
        File.WriteAllText(Path.Combine(cfg,"entry_list.ini"),$"[CAR_0]\nMODEL={car.Id}\nSKIN={car.Skins[0].Id}\nAI=none\n[CAR_1]\nMODEL={car.Id}\nSKIN={car.Skins[0].Id}\nAI=none\n");
        File.WriteAllText(Path.Combine(cfg,"extra_cfg.yml"),"EnableAi: false\n");
        ServerRoot.Text=root; Scan(); await RefreshProcessStatesAsync();
        var slot=profile!.Slots[0]; var original=slot.Skin;
        async Task Capture(FrameworkElement element,string name)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); element.UpdateLayout();
            var visual=new DrawingVisual();
            using(var drawing=visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
            var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32); image.Render(visual);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file=File.Create(Path.Combine(output,name)); png.Save(file);
        }
        Pages.SelectedIndex=2; CatalogPages.SelectedIndex=0; CarList.SelectedItem=car;
        CarSearch.Text=""; CarSearch.Focus(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        CarSearch.ApplyTemplate();
        var placeholder=(TextBlock)CarSearch.Template.FindName("Placeholder",CarSearch);
        void CheckSearchCaret(TextBox search)
        {
            search.UpdateLayout(); search.ApplyTemplate();
            var hint=(TextBlock)search.Template.FindName("Placeholder",search);
            var caret=search.GetRectFromCharacterIndex(0);
            var origin=hint.TransformToAncestor(search).Transform(new Point());
            File.AppendAllText(Path.Combine(output,"search-geometry.txt"),$"{search.Name}: caret={caret}; hint={origin}; height={search.ActualHeight}\n");
            if(caret.IsEmpty || Math.Abs(search.ActualHeight-34)>1 || Math.Abs(caret.X-origin.X)>0.5)
                throw new InvalidOperationException($"Search caret and hint differ: caret={caret}, hint={origin}.");
        }
        CheckSearchCaret(CarSearch);
        await Capture(RootContent,"search-empty-en.png");
        CarSearch.Text="Skyline"; CarSearch.CaretIndex=0; CheckSearchCaret(CarSearch);
        await Capture(RootContent,"search-entered-en.png"); CarSearch.Text="";
        async Task Inspect(bool apply)
        {
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _=Dispatcher.BeginInvoke(async () =>
            {
                var dialog=Application.Current.Windows.OfType<CarDetailsWindow>().Single();
                try
                {
                    if (dialog.SelectedSkin?.Id != original || dialog.Preview.Source == null || dialog.Facts.Children.Count==0) throw new InvalidOperationException("Car dialog lost selected skin, preview or metadata.");
                    dialog.SkinSwatches.SelectedItem=car.Skins[1];
                    if (dialog.SelectedSkin?.Id != car.Skins[1].Id) throw new InvalidOperationException("Skin swatch selection failed.");
                    await Capture((FrameworkElement)dialog.Content,apply?"car-details-en.png":"car-details-cancel.png");
                    if(apply) dialog.UseSkinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else dialog.DialogResult=false;
                    completion.SetResult();
                }
                catch(Exception ex) { dialog.DialogResult=false; completion.SetException(ex); }
            });
            ShowSlotCar(slot); await completion.Task;
        }
        await Inspect(false); if(slot.Skin!=original || dirty) throw new InvalidOperationException("Cancel changed the slot draft.");
        await Inspect(true); if(slot.Skin!=car.Skins[1].Id || profile.Slots[1].Skin!=original || !dirty) throw new InvalidOperationException("Skin selection did not change only the chosen slot draft.");
        if(ConfigText.Read(Path.Combine(cfg,"entry_list.ini")).Contains($"SKIN={car.Skins[1].Id}\n")) throw new InvalidOperationException("Dialog wrote server configuration before Save.");
        var generated = new CarDetailsWindow(car,"generated") {Owner=this}; generated.Show();
        if(generated.SelectedSkin!=null || generated.UseSkinButton.IsEnabled || generated.Preview.Source!=null) throw new InvalidOperationException("Generated AI skin was replaced by a real skin/preview.");
        await Capture((FrameworkElement)generated.Content,"car-details-generated.png"); generated.Close();
        LanguagePicker.SelectedIndex=0; var translated=new CarDetailsWindow(car,slot.Skin){Owner=this}; translated.Show(); await Capture((FrameworkElement)translated.Content,"car-details-ru.png"); translated.Close();
        Pages.SelectedIndex=1; await Capture(RootContent,"slots-details-ru.png");
        var listSource=ServerList.ItemsSource; var gridSource=SlotGrid.ItemsSource; var oldProfile=profile;
        var savedRow=ServerList.SelectedItem;
        ServerList.UpdateLayout();
        var savedContainer=ServerList.ItemContainerGenerator.ContainerFromItem(savedRow);
        void StableSaveFrame(object? sender,EventArgs e)
        {
            if(!Editor.IsEnabled || !ServerList.IsEnabled || ServerList.ItemsSource!=listSource || SlotGrid.ItemsSource!=gridSource)
                throw new InvalidOperationException("Saving reset or disabled a live interface panel.");
        }
        CompositionTarget.Rendering+=StableSaveFrame;
        try { await SaveProfileAsync(); } finally { CompositionTarget.Rendering-=StableSaveFrame; }
        if(profile!=oldProfile || ServerList.SelectedItem!=savedRow || ServerList.ItemContainerGenerator.ContainerFromItem(savedRow)!=savedContainer || SlotGrid.ItemsSource!=gridSource)
            throw new InvalidOperationException("Saving recreated the list or editor objects.");
        if(dirty || profile!.Slots[0].Skin!=car.Skins[1].Id || ConfigText.Get(ConfigText.Read(Path.Combine(cfg,"entry_list.ini")),"CAR_0","SKIN")!=car.Skins[1].Id || !Directory.EnumerateDirectories(Path.Combine(root,"fixture","backups")).Any())
            throw new InvalidOperationException("Save did not persist and reload the draft with a backup.");
        var count=Directory.EnumerateDirectories(Path.Combine(root,"fixture","backups")).Count();
        var timestamps=Directory.EnumerateFiles(cfg).ToDictionary(f=>f,File.GetLastWriteTimeUtc);
        await SaveProfileAsync(); await SaveProfileAsync();
        if(Directory.EnumerateDirectories(Path.Combine(root,"fixture","backups")).Count()!=count || timestamps.Any(p=>File.GetLastWriteTimeUtc(p.Key)!=p.Value)) throw new InvalidOperationException("Repeated unchanged saves wrote files or created backups.");
        var oldName=NameInput.Text; NameInput.Text="Transient edit"; NameInput.Text=oldName;
        await SaveProfileAsync();
        if(dirty || Directory.EnumerateDirectories(Path.Combine(root,"fixture","backups")).Count()!=count) throw new InvalidOperationException("An edit reverted to the baseline created a backup.");
        TrackInput.Text="qa_missing_track";
        await SaveProfileAsync();
        if(dirty || !Diagnostics.Text.Contains(UiText.T("Не найдены данные выбранной трассы в игре и на сервере.")) || !Status.Text.Contains("замечаний")) throw new InvalidOperationException("Save notices are not shown inline.");
        var iniPath=Path.Combine(cfg,"server_cfg.ini"); var savedIni=File.ReadAllText(iniPath);
        NameInput.Text="Draft must survive failed save";
        File.AppendAllText(iniPath,"\n; external QA edit\n");
        try { await SaveProfileAsync(); throw new InvalidOperationException("Concurrent modification was not rejected."); }
        catch(InvalidOperationException ex) when(ex.Message.Contains("Перечитайте") || ex.Message.Contains("Reload")) { }
        if(!dirty || NameInput.Text!="Draft must survive failed save" || saving || !Editor.IsEnabled || !ServerList.IsEnabled) throw new InvalidOperationException("Failed save lost the draft or left controls disabled.");
        File.WriteAllText(iniPath,savedIni);
        await SaveProfileAsync();
        var secondCfg=Path.Combine(root,"other","cfg"); Directory.CreateDirectory(secondCfg);
        foreach(var file in new[]{"server_cfg.ini","entry_list.ini","extra_cfg.yml"}) File.Copy(Path.Combine(cfg,file),Path.Combine(secondCfg,file),true);
        Scan(); await RefreshProcessStatesAsync();
        NameInput.Text="Keep this unsaved draft";
        var draftProfile=profile;
        var otherRow=ServerList.Items.OfType<ServerRow>().Single(r=>r.Folder=="other");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ServerList.UpdateLayout();
        var container=(ListBoxItem)ServerList.ItemContainerGenerator.ContainerFromItem(otherRow);
        PrepareServerMenu(container);
        var menu=container.ContextMenu; menu.PlacementTarget=container; menu.IsOpen=true;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var delete=menu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==UiText.T("Удалить сервер"));
        var rename=menu.Items.OfType<MenuItem>().Single(m=>(string)m.Header==UiText.T("Переименовать папку…"));
        if(!delete.IsEnabled || delete.Tag!=otherRow || profile!=draftProfile || !dirty) throw new InvalidOperationException("Context menu did not target the clicked server independently of the selection.");
        await Capture(menu,"server-context-menu.png");
        otherRow.SetStatus(ServerRunState.External,[123]);
        if(delete.IsEnabled || rename.IsEnabled) throw new InvalidOperationException("Folder actions were enabled for a running server.");
        menu.IsOpen=false;
        RemoveServerRow(otherRow.Profile.Path);
        if(profile!=draftProfile || !dirty || NameInput.Text!="Keep this unsaved draft") throw new InvalidOperationException("Removing another row lost the selected server draft.");
        if(DeleteServerButton.Margin!=StartButton.Margin) throw new InvalidOperationException("Delete button spacing differs from adjacent buttons.");
        var renameCompletion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _=Dispatcher.BeginInvoke(async()=>
        {
            var dialog=Application.Current.Windows.OfType<AppDialog>().Single();
            try
            {
                dialog.DialogInput.Text="bad/name"; dialog.AcceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!dialog.IsVisible || dialog.InputError.Visibility!=Visibility.Visible) throw new InvalidOperationException("Invalid folder name closed the rename dialog.");
                dialog.DialogInput.Text="fixture-renamed";
                await Capture((FrameworkElement)dialog.Content,"rename-folder.png");
                dialog.AcceptButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); renameCompletion.SetResult();
            }
            catch(Exception ex) { dialog.DialogResult=false; renameCompletion.SetException(ex); }
        });
        var chosenName=AppDialog.Prompt(this,UiText.T("Имя папки меняет название в левом списке. Название сервера в игре остаётся прежним.\nКонфиги, контент и бэкапы сохраняются."),UiText.T("Переименовать папку сервера"),profile!.Folder,
            value=>ServerRename.Validate(root,profile.Path,value,GameRoot.Text));
        await renameCompletion.Task;
        var chosenRow=(ServerRow)ServerList.SelectedItem;
        var diskName=ServerProfile.Load(profile.Path).Name;
        var oldPath=profile.Path;
        await RenameServerAsync(chosenRow,chosenName!);
        if(Directory.Exists(oldPath) || !Directory.Exists(profile.Path) || profile.Folder!="fixture-renamed" || ServerProfile.Load(profile.Path).Name!=diskName || NameInput.Text!="Keep this unsaved draft" || !dirty || profile!=draftProfile || chosenRow!=(ServerRow)ServerList.SelectedItem)
            throw new InvalidOperationException("Folder rename lost the draft, changed the game name or recreated the selected row.");
        await SaveProfileAsync();
        if(ServerProfile.Load(profile.Path).Name!="Keep this unsaved draft" || dirty) throw new InvalidOperationException("The renamed server's draft could not be saved.");
        var migrations=Path.Combine(output,"settings-migration"); var previous=Path.Combine(migrations,"desktop-0.4.1"); var current=Path.Combine(migrations,"desktop-0.5.2");
        Directory.CreateDirectory(previous); Directory.CreateDirectory(current);
        var cm=Path.Combine(migrations,"Content Manager.exe"); File.WriteAllText(cm,"fixture");
        File.WriteAllText(Path.Combine(previous,"settings.json"),System.Text.Json.JsonSerializer.Serialize(new UserSettings("old root","old game",cm,"ru")));
        File.WriteAllText(Path.Combine(current,"settings.json"),System.Text.Json.JsonSerializer.Serialize(new UserSettings("current root","current game","","en")));
        var shared=Path.Combine(migrations,"shared","settings.json");
        var migrated=ManagerSettings.Load(current,shared,false);
        if(migrated?.ContentManager!=cm || migrated.ServersRoot!="current root" || migrated.Language!="en") throw new InvalidOperationException("Legacy Content Manager recovery replaced current preferences.");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        File.WriteAllText(shared,System.Text.Json.JsonSerializer.Serialize(new UserSettings("shared root","shared game",cm,"ru")));
        if(ManagerSettings.Load(current,shared,false)?.ServersRoot!="shared root") throw new InvalidOperationException("Shared preferences did not take priority.");
        var actualSettings=ManagerSettings.Load(AppContext.BaseDirectory,ManagerSettings.SharedFile,false);
        if(actualSettings?.ContentManager is {Length:>0} actualCm && File.Exists(actualCm))
        {
            cmPath=actualCm;
            if(FindContentManager()!=actualCm) throw new InvalidOperationException("Installed Content Manager recovery failed.");
        }
        File.WriteAllText(Path.Combine(output,"details-result.txt"),$"PASS: stable list/grid/container identities across save, rendered-frame control state, unchanged/reverted saves without writes/backups, inline notices and failed-save recovery, rename dialog validation, real fixture rename with draft/game-name preservation and subsequent save, shared settings/legacy Content Manager recovery, process guards, car details and search geometry. {catalog.Cars.Count} cars; {catalog.Tracks.Count} layouts; {catalog.Warnings.Count} metadata warnings. Writes and rename confined to QA fixtures; no live server files changed; no server or Content Manager launched.");
    }
}
