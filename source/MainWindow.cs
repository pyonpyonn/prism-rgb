using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PrismRGB;

public sealed class MainWindow : Window
{
    readonly Preferences prefs=Storage.Load();
    readonly Grid content=new(); readonly TextBlock connectionText=new(),footer=new(),title=new(),subtitle=new();
    readonly LightingSurface surface=new(); readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly List<Button> navButtons=new();
    ILightingBackend? backend; System.Windows.Forms.NotifyIcon? tray;
    List<HidDevice> usbDevices=new(); List<CatalogEntry> catalog=new();
    bool busy,writing,output,paused,closing,refreshing,screenBusy; double lastWrite,lastScreen,seconds,previousTick;
    Color? screen; string page="Studio"; string note="Preview only. Connect a lighting controller to use real devices.";
    Button? startButton,connectButton; TextBlock? effectLabel,brightnessLabel,speedLabel; Slider? brightness,speed;
    TextBox? primary,secondary;ListBox? effects;bool syncing=false;
    static SolidColorBrush B(string h)=>new((Color)ColorConverter.ConvertFromString(h));
    static TextBlock T(string text,double size=13,string color="#E8EFEC",bool bold=false)=>new(){Text=text,FontSize=size,Foreground=B(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
    static StackPanel V(params UIElement[] elements){var p=new StackPanel();foreach(var e in elements)p.Children.Add(e);return p;}
    static Border Box(UIElement child,string color="#192126",Thickness? padding=null)=>new(){Background=B(color),CornerRadius=new CornerRadius(12),Padding=padding??new Thickness(20),Child=child};
    static Button Btn(string text,Action action,bool accent=false)
    {
        var b=new Button{Content=text};if(accent){b.Background=B("#83E1C2");b.Foreground=B("#11261F");}b.Click+=(_,_)=>action();return b;
    }
    static FrameworkElement Gap(double h=16)=>new Border{Height=h};
    static TextBlock Small(string text)=>T(text,11,"#90A29F");
    public MainWindow()
    {
        Title="Prism RGB";Width=1340;Height=875;MinWidth=1120;MinHeight=730;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=B("#101417");Foreground=B("#EFF3F1");FontFamily=new FontFamily("Segoe UI");FontSize=13;
        if(prefs.Profiles.Count==0)
        {
            prefs.Profiles.Add(new(){Name="Northern lights",State=new()});
            prefs.Profiles.Add(new(){Name="Warm desk",State=new(){Effect="Ember",Brightness=.55}});
            prefs.Profiles.Add(new(){Name="Deep focus",State=new(){Effect="Solid",Color="#578BC0",Brightness=.35}});
        }
        var root=new Grid{Background=B("#101417")};root.ColumnDefinitions.Add(new(){Width=new GridLength(208)});root.ColumnDefinitions.Add(new());Content=root;
        var rail=new DockPanel{LastChildFill=true,Margin=new Thickness(0)};
        var railBox=new Border{Background=B("#151C20"),BorderBrush=B("#283338"),BorderThickness=new Thickness(0,0,1,0),Child=rail};root.Children.Add(railBox);
        var logo=V(T("◈  PRISM",25,"#DCEFE6",true),Small("YOUR LIGHT. YOUR SPACE."));logo.Margin=new Thickness(25,30,0,35);DockPanel.SetDock(logo,Dock.Top);rail.Children.Add(logo);
        var bottom=V(Box(V(T("LOCAL BY DESIGN",10,"#83E1C2",true),Gap(7),Small("No account needed.\nYour profiles stay on this PC.")),"#1D2A2B",new Thickness(13)),Gap(12),Small("PRISM RGB   /   0.1.0"));bottom.Margin=new Thickness(16,0,16,22);DockPanel.SetDock(bottom,Dock.Bottom);rail.Children.Add(bottom);
        var navigation=V();foreach(var (name,icon) in new[]{("Studio","◉"),("Devices","▦"),("Profiles","▤"),("Compatibility","⌕"),("Settings","⚙")})
        {
            var nameCopy=name;var button=Btn($"{icon}    {name}",()=>Navigate(nameCopy));button.Tag=name;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Background=Brushes.Transparent;button.Margin=new Thickness(12,3,12,3);navButtons.Add(button);navigation.Children.Add(button);
        }
        rail.Children.Add(navigation);
        var main=new DockPanel{Margin=new Thickness(30,25,30,18)};Grid.SetColumn(main,1);root.Children.Add(main);
        var heading=new Grid{Margin=new Thickness(0,0,0,25)};heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        title.FontSize=28;title.FontWeight=FontWeights.SemiBold;subtitle.Foreground=B("#91A39F");subtitle.Margin=new Thickness(0,7,0,0);
        heading.Children.Add(V(Small("WORKSPACE  /  PERSONAL"),Gap(8),title,subtitle));
        connectionText.FontSize=11;connectionText.Foreground=B("#92A6A0");connectionText.VerticalAlignment=VerticalAlignment.Center;
        var chip=Box(connectionText,"#202C2D",new Thickness(14,10,14,10));chip.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(chip,1);heading.Children.Add(chip);
        DockPanel.SetDock(heading,Dock.Top);main.Children.Add(heading);
        footer.FontSize=11;footer.TextWrapping=TextWrapping.Wrap;footer.Foreground=B("#94AAA2");footer.Margin=new Thickness(0,16,0,0);DockPanel.SetDock(footer,Dock.Bottom);main.Children.Add(footer);
        main.Children.Add(content);
        surface.State=prefs.Studio;surface.LayoutChanged+=ScheduleSave;
        timer.Tick+=Tick;saveTimer.Tick+=(_,_)=>{saveTimer.Stop();Save();};timer.Start();
        LoadCatalog();Navigate("Studio");
        Loaded+=async(_,_)=>await ScanUsbAsync();
        Closing+=async(_,e)=>
        {
            if(closing)return;e.Cancel=true;
            if(prefs.MinimizeToTray){Hide();return;}
            await ExitAsync();
        };
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized && prefs.MinimizeToTray)Hide();};
        SetupTray();
    }
    void SetupTray()
    {
        tray=new(){Text="Prism RGB",Icon=System.Drawing.SystemIcons.Application,Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Prism RGB",null,(_,_)=>Dispatcher.Invoke(()=>{Show();WindowState=WindowState.Normal;Activate();}));
        menu.Items.Add("Stop lighting and restore",null,(_,_)=>Dispatcher.Invoke(async()=>await StopAsync()));
        menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(async()=>await ExitAsync()));tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=>{Show();WindowState=WindowState.Normal;Activate();});
    }
    void LoadCatalog()
    {
        try
        {
            string path=Path.Combine(AppContext.BaseDirectory,"compatibility-catalog.json");
            if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"..","compatibility-catalog.json");
            if(File.Exists(path))catalog=JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(path))??new();
        }catch(Exception e) when(e is IOException or JsonException){note="Compatibility catalog could not be loaded.";}
    }
    void Navigate(string name)
    {
        page=name;content.Children.Clear();startButton=null;connectButton=null;effectLabel=null;brightnessLabel=null;speedLabel=null;primary=null;secondary=null;effects=null;brightness=null;speed=null;
        foreach(var b in navButtons){bool selected=(string)b.Tag==name;b.Background=selected?B("#254139"):Brushes.Transparent;b.Foreground=selected?B("#93EBC9"):B("#A7B8B2");}
        title.Text=name;subtitle.Text=name switch{"Studio"=>"Create a lighting scene that moves with your setup.","Devices"=>"Connect your hardware and place its lights in your scene.","Profiles"=>"Keep a different atmosphere ready for every moment.","Compatibility"=>"Explore the included device-reference descriptions.",_=>"Make Prism RGB feel at home on your PC."};
        content.Children.Add(name switch{"Studio"=>StudioPage(),"Devices"=>DevicesPage(),"Profiles"=>ProfilesPage(),"Compatibility"=>CatalogPage(),_=>SettingsPage()});UpdateStatus();
    }
    UIElement StudioPage()
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(270)});
        var left=new Grid{Margin=new Thickness(0,0,20,0)};left.RowDefinitions.Add(new(){Height=GridLength.Auto});left.RowDefinitions.Add(new());left.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.Children.Add(left);
        var top=new Grid{Margin=new Thickness(0,0,0,14)};top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        effectLabel=T(prefs.Studio.Effect,18,"#EDF2EF",true);top.Children.Add(V(effectLabel,Small("Lighting canvas")));
        var edit=new CheckBox{Content="Edit layout",IsChecked=surface.Editing};edit.Checked+=(_,_)=>{surface.Editing=true;surface.InvalidateVisual();};edit.Unchecked+=(_,_)=>{surface.Editing=false;surface.InvalidateVisual();};Grid.SetColumn(edit,1);top.Children.Add(edit);left.Children.Add(top);
        if(surface.Parent is Panel oldPanel)oldPanel.Children.Remove(surface);else if(surface.Parent is Border oldBorder)oldBorder.Child=null;
        Grid.SetRow(surface,1);left.Children.Add(surface);
        var below=new Grid{Margin=new Thickness(0,15,0,0)};below.ColumnDefinitions.Add(new());below.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(below,2);left.Children.Add(below);
        var preview=Btn(paused?"Resume preview":"Pause preview",()=>{paused=!paused;Navigate("Studio");});below.Children.Add(preview);preview.HorizontalAlignment=HorizontalAlignment.Left;
        startButton=Btn(output?"Stop & restore":"Start lighting",async()=>{if(output)await StopAsync();else StartOutput();},true);Grid.SetColumn(startButton,1);below.Children.Add(startButton);
        var controls=V(T("EFFECT LIBRARY",10,"#8EA59D",true),Gap(10));
        effects=new ListBox{Height=285};foreach(var name in Effects.Names)effects.Items.Add(name);effects.SelectedItem=prefs.Studio.Effect;
        effects.SelectionChanged+=(_,_)=>{if(syncing || effects.SelectedItem is not string selected)return;prefs.Studio.Effect=selected;effectLabel!.Text=selected;ScheduleSave();};controls.Children.Add(effects);controls.Children.Add(Gap(14));
        brightnessLabel=T($"Brightness                                      {prefs.Studio.Brightness:P0}",12);controls.Children.Add(brightnessLabel);
        brightness=new Slider{Value=prefs.Studio.Brightness*100};brightness.ValueChanged+=(_,_)=>{if(syncing)return;prefs.Studio.Brightness=brightness.Value/100;brightnessLabel.Text=$"Brightness                                      {prefs.Studio.Brightness:P0}";ScheduleSave();};controls.Children.Add(brightness);
        speedLabel=T($"Motion speed                                  {prefs.Studio.Speed:P0}",12);controls.Children.Add(speedLabel);
        speed=new Slider{Value=prefs.Studio.Speed*100};speed.ValueChanged+=(_,_)=>{if(syncing)return;prefs.Studio.Speed=speed.Value/100;speedLabel.Text=$"Motion speed                                  {prefs.Studio.Speed:P0}";ScheduleSave();};controls.Children.Add(speed);
        controls.Children.Add(T("Palette",12));controls.Children.Add(Gap(8));
        var palette=new Grid();palette.ColumnDefinitions.Add(new());palette.ColumnDefinitions.Add(new());primary=new TextBox{Text=prefs.Studio.Color,Margin=new Thickness(0,0,5,0)};secondary=new TextBox{Text=prefs.Studio.SecondaryColor,Margin=new Thickness(5,0,0,0)};
        primary.ToolTip="Primary color, for example #68E0C2";secondary.ToolTip="Secondary color, for example #A38BFA";palette.Children.Add(primary);Grid.SetColumn(secondary,1);palette.Children.Add(secondary);controls.Children.Add(palette);
        primary.TextChanged+=(_,_)=>UpdatePalette();secondary.TextChanged+=(_,_)=>UpdatePalette();
        var swatches=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};
        foreach(var color in new[]{"#68E0C2","#A38BFA","#F3A65F","#5FADFA","#FA739C","#EEF3EC"})
        {var c=color;var swatch=Btn("",()=>{primary.Text=c;});swatch.Width=27;swatch.Height=25;swatch.Padding=new Thickness(0);swatch.Background=B(c);swatch.Margin=new Thickness(0,0,6,0);swatch.ToolTip=c;swatches.Children.Add(swatch);}
        controls.Children.Add(swatches);controls.Children.Add(Gap());controls.Children.Add(Small("The virtual setup previews the scene. Real devices use the positions set under Devices."));
        var box=Box(new ScrollViewer{Content=controls});Grid.SetColumn(box,1);grid.Children.Add(box);return grid;
    }
    void UpdatePalette()
    {
        if(syncing || primary is null || secondary is null)return;
        primary.BorderBrush=Effects.ValidColor(primary.Text)?B("#344147"):B("#EFA275");secondary.BorderBrush=Effects.ValidColor(secondary.Text)?B("#344147"):B("#EFA275");
        if(Effects.ValidColor(primary.Text))prefs.Studio.Color=primary.Text;
        if(Effects.ValidColor(secondary.Text))prefs.Studio.SecondaryColor=secondary.Text;ScheduleSave();
    }
    UIElement DevicesPage()
    {
        var stack=V();var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());
        var open=V(T("OpenRGB",18,"#E7F3EC",true),Gap(8),Small("Connect to an OpenRGB SDK server on this PC. Controls the devices it detects."),Gap(14));
        var openBtn=Btn(backend is OpenRgbBackend?"Connected":"Connect OpenRGB",async()=>await ConnectAsync(false));openBtn.IsEnabled=!busy && backend is null;open.Children.Add(openBtn);connectButton=openBtn;
        var direct=V(T("MSI motherboard",18,"#E7F3EC",true),Gap(8),Small("Experimental adapter for B550M PRO-VDH WIFI. Four whole-header colors; no per-LED control."),Gap(14));
        var directBtn=Btn(backend is MsiBackend?"Connected":"Connect MSI headers",async()=>await ConnectAsync(true));directBtn.IsEnabled=!busy && backend is null;direct.Children.Add(directBtn);
        var a=Box(open);a.Margin=new Thickness(0,0,8,0);var b=Box(direct);b.Margin=new Thickness(8,0,0,0);Grid.SetColumn(b,1);row.Children.Add(a);row.Children.Add(b);stack.Children.Add(row);
        stack.Children.Add(Gap(12));stack.Children.Add(Small("Close SignalRGB and other lighting software before connecting. Connecting reads the current state; Start lighting enables output."));stack.Children.Add(Gap(20));
        if(backend is not null)
        {
            var header=new Grid();header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.Children.Add(T($"{backend.Devices.Count} lighting outputs",17,"#E6F0EA",true));
            var disconnect=Btn("Disconnect",async()=>await DisconnectAsync());disconnect.IsEnabled=!busy;Grid.SetColumn(disconnect,1);header.Children.Add(disconnect);stack.Children.Add(header);stack.Children.Add(Gap(8));
            foreach(var d in backend.Devices)
            {
                var line=V();var enabled=new CheckBox{Content=$"{d.Name}   ·   {d.LedCount} LED{(d.LedCount==1?"":"s")}",IsChecked=d.Placement.Enabled};
                enabled.IsEnabled=!output;enabled.ToolTip=output?"Stop lighting to change the selected outputs.":null;
                enabled.Checked+=(_,_)=>{d.Placement.Enabled=true;ScheduleSave();};enabled.Unchecked+=(_,_)=>{d.Placement.Enabled=false;ScheduleSave();};line.Children.Add(enabled);
                line.Children.Add(Small("POSITION IN SCENE"));var sliders=new Grid();for(int i=0;i<3;i++)sliders.ColumnDefinitions.Add(new());
                AddPosition(sliders,"Horizontal",d.Placement.X,v=>d.Placement.X=v,0);AddPosition(sliders,"Vertical",d.Placement.Y,v=>d.Placement.Y=v,1);AddPosition(sliders,"LED span",d.Placement.Span,v=>d.Placement.Span=v,2);
                line.Children.Add(sliders);var card=Box(line,"#192126",new Thickness(15,8,15,0));card.Margin=new Thickness(0,4,0,4);stack.Children.Add(card);
            }
            startButton=Btn(output?"Stop & restore":"Start lighting",async()=>{if(output)await StopAsync();else StartOutput();},true);startButton.Margin=new Thickness(0,12,0,0);stack.Children.Add(startButton);
        }
        stack.Children.Add(Gap(22));var scan=Btn(refreshing?"Scanning…":"Refresh USB devices",async()=>await ScanUsbAsync());scan.IsEnabled=!refreshing;scan.HorizontalAlignment=HorizontalAlignment.Left;stack.Children.Add(scan);stack.Children.Add(Gap(10));
        foreach(var group in usbDevices.GroupBy(x=>x.Id))
        {
            var d=group.First();string match=d.VendorId==0x1462&&d.ProductId==0x7C95?"MSI header adapter available":catalog.Any(x=>x.VendorId==d.VendorId && x.ProductId==d.ProductId)?"SignalRGB reference match; use OpenRGB if supported":"No native RGB adapter included";
            var card=Box(V(T(d.Name,13,"#DCE8E2",true),Gap(4),Small($"USB {d.Id}   ·   {match}")),"#172025",new Thickness(15));card.Margin=new Thickness(0,4,0,4);stack.Children.Add(card);
        }
        stack.Children.Add(Gap(8));stack.Children.Add(Small("USB detection identifies interfaces, not whether a device has controllable RGB. Keyboard and mouse input are never modified."));
        return new ScrollViewer{Content=stack};
    }
    void AddPosition(Grid parent,string name,double value,Action<double> set,int col)
    {
        var s=new Slider{Value=value*100,Margin=new Thickness(0,5,12,10)};s.ValueChanged+=(_,_)=>{set(s.Value/100);ScheduleSave();};var p=V(Small(name),s);Grid.SetColumn(p,col);parent.Children.Add(p);
    }
    UIElement ProfilesPage()
    {
        var panel=V();var name=new TextBox{Text="My setup",MaxLength=80,Width=290,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(Box(V(T("Save the current scene",18,"#E2EDE7",true),Gap(10),name,Gap(12),Btn("Save profile",()=>
        {
            string text=name.Text.Trim();if(text.Length==0){SetNote("Give your profile a name.");return;}
            var existing=prefs.Profiles.FirstOrDefault(x=>x.Name.Equals(text,StringComparison.OrdinalIgnoreCase));
            if(existing is not null)existing.State=prefs.Studio.Clone();else prefs.Profiles.Add(new(){Name=text,State=prefs.Studio.Clone()});Save();Navigate("Profiles");SetNote($"Saved {text}.");
        },true))));panel.Children.Add(Gap(18));
        foreach(var profile in prefs.Profiles.ToList())
        {
            var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(V(T(profile.Name,16,"#E5EDE8",true),Gap(6),Small($"{profile.State.Effect}   ·   {profile.State.Brightness:P0} brightness")));
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};
            var load=Btn("Apply",()=>{prefs.Studio=profile.State.Clone();surface.State=prefs.Studio;ScheduleSave();Navigate("Studio");SetNote($"Applied {profile.Name}.");});buttons.Children.Add(load);
            var export=Btn("Export",()=>ExportProfile(profile));export.Margin=new Thickness(8,0,0,0);buttons.Children.Add(export);
            var delete=Btn("Delete",()=>{prefs.Profiles.Remove(profile);Save();Navigate("Profiles");});delete.Margin=new Thickness(8,0,0,0);buttons.Children.Add(delete);
            Grid.SetColumn(buttons,1);row.Children.Add(buttons);var card=Box(row);card.Margin=new Thickness(0,4,0,4);panel.Children.Add(card);
        }
        var import=Btn("Import profile",()=>
        {
            var dialog=new OpenFileDialog{Filter="Prism RGB profile (*.json)|*.json"};if(dialog.ShowDialog()!=true)return;
            try{var p=Storage.Import(dialog.FileName);prefs.Profiles.Add(p);Save();Navigate("Profiles");SetNote($"Imported {p.Name}.");}catch(Exception e){SetNote("Import failed: "+e.Message);}
        });import.HorizontalAlignment=HorizontalAlignment.Left;import.Margin=new Thickness(0,15,0,0);panel.Children.Add(import);return new ScrollViewer{Content=panel};
    }
    void ExportProfile(SavedProfile profile)
    {
        var dialog=new SaveFileDialog{Filter="Prism RGB profile (*.json)|*.json",FileName="Prism-profile.json"};if(dialog.ShowDialog()!=true)return;
        try{Storage.Export(profile,dialog.FileName);SetNote("Profile exported.");}catch(Exception e){SetNote("Export failed: "+e.Message);}
    }
    UIElement CatalogPage()
    {
        var dock=new DockPanel();var search=new TextBox{Margin=new Thickness(0,0,0,12)};search.ToolTip="Search device name, publisher, or USB ID";
        var info=V(T($"{catalog.Count} plugin descriptions",19,"#E3F0E7",true),Gap(8),Small("Reference metadata derived from readable SignalRGB device descriptions. These entries describe research candidates; they are not working Prism RGB drivers."),Gap(12),search);
        DockPanel.SetDock(info,Dock.Top);dock.Children.Add(info);var list=new ListBox();dock.Children.Add(list);
        void Filter()
        {
            string query=search.Text.Trim();list.Items.Clear();
            foreach(var c in catalog.Where(x=>query.Length==0 || $"{x.Name} {x.Publisher} {x.VendorId:X4}:{x.ProductId:X4}".Contains(query,StringComparison.OrdinalIgnoreCase)))
                list.Items.Add(V(T(c.Name,13,"#D9E8E0",true),Gap(4),Small($"{c.Publisher}  ·  {c.Kind}  ·  {(c.VendorId.HasValue?$"{c.VendorId:X4}":"dynamic ID")}:{(c.ProductId.HasValue?$"{c.ProductId:X4}":"multiple IDs")}")));
        }
        search.TextChanged+=(_,_)=>Filter();Filter();return Box(dock);
    }
    UIElement SettingsPage()
    {
        var minimize=new CheckBox{Content="Keep running in the system tray when I close the window",IsChecked=prefs.MinimizeToTray};minimize.Checked+=(_,_)=>{prefs.MinimizeToTray=true;ScheduleSave();};minimize.Unchecked+=(_,_)=>{prefs.MinimizeToTray=false;ScheduleSave();};
        var panel=V(Box(V(T("Desktop behavior",18,"#E5EEE8",true),Gap(12),minimize,Small("Use Quit in the tray menu to stop lighting, restore the previous state, and exit."))),Gap(18),
            Box(V(T("Your workspace",18,"#E5EEE8",true),Gap(10),Small("Profiles and preferences are stored locally. Hardware output always starts disabled."),Gap(12),Btn("Open profile folder",()=>{Directory.CreateDirectory(Storage.DirectoryPath);Process.Start(new ProcessStartInfo(Storage.DirectoryPath){UseShellExecute=true});}),Gap(12),Btn("Reset virtual layout",()=>{prefs.Studio.Layout=StudioState.Defaults();surface.State=prefs.Studio;ScheduleSave();SetNote("Virtual layout reset.");}))),Gap(18),
            Box(V(T("About Prism RGB",18,"#E5EEE8",true),Gap(10),Small("Version 0.1.0 · Windows desktop\nIndependent effects and interface, informed by local SignalRGB plugin analysis.\n\nIncluded: seven effects, draggable preview, profiles, USB discovery, OpenRGB output, and an experimental MSI header adapter.\n\nNot included: fan control, hardware monitoring, game integrations, paid SignalRGB content, or general SignalRGB plugin execution."),Gap(12),Btn("Open inspection report",()=>OpenReport()))));
        return new ScrollViewer{Content=panel};
    }
    void OpenReport()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"..","docs","INSPECTION.md");
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"..","INSPECTION.md");
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"INSPECTION.md");
        if(File.Exists(path))Process.Start(new ProcessStartInfo(Path.GetFullPath(path)){UseShellExecute=true});else SetNote("The inspection report is included alongside the app download.");
    }
    async Task ScanUsbAsync()
    {
        if(refreshing)return;refreshing=true;
        try{usbDevices=await Task.Run(Hid.Enumerate);}catch(Exception e){SetNote("USB scan failed: "+e.Message);}finally{refreshing=false;if(page=="Devices")Navigate("Devices");}
    }
    async Task ConnectAsync(bool msi)
    {
        if(busy || backend is not null)return;busy=true;SetNote("Reading controller information…");Navigate("Devices");
        ILightingBackend candidate=msi?new MsiBackend():new OpenRgbBackend();
        try
        {
            if(Hid.ConflictRunning())throw new InvalidOperationException("Close SignalRGB and MSI lighting tools before enabling another lighting controller.");
            await candidate.ConnectAsync();backend=candidate;
            for(int i=0;i<backend.Devices.Count;i++)
            {
                var d=backend.Devices[i];var placement=prefs.Placements.FirstOrDefault(x=>x.Identity==d.Identity);
                if(placement is null){placement=new(){Identity=d.Identity,X=.1,Y=.25+i*.15,Span=backend is MsiBackend?0:.65};placement.Y=Math.Min(.9,placement.Y);prefs.Placements.Add(placement);}
                d.Placement=placement;
            }
            prefs.Backend=msi?"MSI":"OpenRGB";ScheduleSave();SetNote($"Connected to {backend.Devices.Count} outputs. Start lighting when ready.");
        }
        catch(Exception e){await candidate.DisposeAsync();SetNote("Connection failed: "+e.Message+(msi?"":" Enable OpenRGB's SDK server on 127.0.0.1:6742 first."));}
        finally{busy=false;Navigate("Devices");}
    }
    void StartOutput()
    {
        if(busy)return;
        if(backend is null){SetNote("Choose a controller under Devices first.");Navigate("Devices");return;}
        if(!backend.Devices.Any(x=>x.Placement.Enabled && x.LedCount>0)){SetNote("Select at least one output with LEDs.");return;}
        if(Hid.ConflictRunning()){SetNote("Close SignalRGB and MSI lighting tools before starting output.");return;}
        output=true;SetNote("Lighting is active. Stop & restore returns the previous controller state.");Navigate(page);
    }
    async Task WaitForFrameAsync(){while(writing)await Task.Delay(15);}
    async Task StopAsync()
    {
        if(busy)return;busy=true;output=false;
        try{await WaitForFrameAsync();if(backend is not null)await backend.RestoreAsync();SetNote("Lighting stopped. Previous controller state restored.");}
        catch(Exception e){SetNote("Lighting stopped; restore was incomplete: "+e.Message);}
        finally{busy=false;if(!closing)Navigate(page);}
    }
    async Task DisconnectAsync()
    {
        if(busy)return;await StopAsync();busy=true;
        if(backend is not null)await backend.DisposeAsync();backend=null;busy=false;Navigate("Devices");
    }
    async void Tick(object? sender,EventArgs args)
    {
        double now=watch.Elapsed.TotalSeconds,delta=now-previousTick;previousTick=now;if(!paused)seconds+=delta;
        surface.Seconds=seconds;surface.ScreenColor=screen;if(page=="Studio")surface.InvalidateVisual();
        if(prefs.Studio.Effect=="Screen ambience" && !screenBusy && now-lastScreen>.5)
        {
            screenBusy=true;lastScreen=now;
            try{screen=await Task.Run(ScreenSampler.Capture);}catch(Exception e){screen=Colors.Black;SetNote("Screen capture unavailable: "+e.Message);}finally{screenBusy=false;}
        }
        if(!output || backend is null || writing || now-lastWrite<.1 || busy)return;
        writing=true;lastWrite=now;
        try
        {
            if(Hid.ConflictRunning())throw new InvalidOperationException("Another lighting application is running. Stop it and reconnect.");
            var colors=new Dictionary<uint,Color[]>();
            foreach(var d in backend.Devices.Where(x=>x.Placement.Enabled && x.LedCount>0))
            {
                var values=new Color[d.LedCount];for(int i=0;i<values.Length;i++)values[i]=Effects.Sample(prefs.Studio,Math.Clamp(d.Placement.X+d.Placement.Span*i/Math.Max(1.0,values.Length-1),0,1),d.Placement.Y,seconds,screen);
                colors.Add(d.Id,values);
            }
            await backend.WriteAsync(colors);
        }
        catch(Exception e)
        {
            output=false;SetNote("Output stopped: "+e.Message+" Disconnect and reconnect to retry.");
            try{await backend.RestoreAsync();}catch{}UpdateStatus();if(page=="Devices"||page=="Studio")Navigate(page);
        }
        finally{writing=false;}
    }
    void UpdateStatus()
    {
        connectionText.Text=output?"●  LIGHTING ACTIVE":backend is null?"○  PREVIEW MODE":"●  CONTROLLER READY";
        connectionText.Foreground=B(output?"#9CE8C5":"#A2B7AE");footer.Text=note;
        if(startButton is not null){startButton.Content=output?"Stop & restore":"Start lighting";startButton.IsEnabled=!busy;}
    }
    void SetNote(string message){note=message;UpdateStatus();}
    void ScheduleSave(){saveTimer.Stop();saveTimer.Start();}
    void Save(){try{Storage.Save(prefs);}catch(Exception e){SetNote("Could not save preferences: "+e.Message);}}
    async Task ExitAsync()
    {
        if(closing)return;if(busy){SetNote("Wait for the current connection operation to finish before quitting.");return;}
        closing=true;timer.Stop();saveTimer.Stop();await StopAsync();if(backend is not null)await backend.DisposeAsync();Save();tray?.Dispose();Close();
    }
    public void RenderPreview(string path)
    {
        Navigate("Studio");UpdateLayout();surface.Seconds=2.5;surface.InvalidateVisual();UpdateLayout();
        RenderCurrentPage(path);
    }
    void RenderCurrentPage(string path)
    {
        UpdateLayout();
        var visual=(FrameworkElement)Content;
        var image=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var f=File.Create(path);encoder.Save(f);
    }
    public async Task CloseForPreview(){prefs.MinimizeToTray=false;await ExitAsync();}
    public async Task RunUiSmokeAsync(string folder)
    {
        Directory.CreateDirectory(folder);var checks=new List<string>();
        foreach(var name in new[]{"Studio","Devices","Profiles","Compatibility","Settings"})
        {
            navButtons.First(b=>(string)b.Tag==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
            if(title.Text!=name || content.Children.Count!=1)throw new Exception("Navigation failed: "+name);
            RenderCurrentPage(Path.Combine(folder,name.ToLowerInvariant()+".png"));checks.Add("Opened and rendered "+name);
        }
        Navigate("Studio");effects!.SelectedItem="Rainbow";brightness!.Value=51;speed!.Value=67;
        primary!.Text="#234567";secondary!.Text="#ABCDEF";
        if(prefs.Studio.Effect!="Rainbow" || prefs.Studio.Brightness!=.51 || prefs.Studio.Speed!=.67 || prefs.Studio.Color!="#234567")throw new Exception("Lighting controls did not update state.");
        primary.Text="invalid";if(prefs.Studio.Color!="#234567")throw new Exception("Invalid color entered the render engine.");checks.Add("Effect, speed, brightness and palette controls update state; invalid colors remain outside the engine");
        Navigate("Profiles");
        static IEnumerable<TChild> Find<TChild>(DependencyObject root) where TChild:DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);if(child is TChild typed)yield return typed;
                foreach(var descendant in Find<TChild>(child))yield return descendant;
            }
        }
        UpdateLayout();var input=Find<TextBox>(content).First();input.Text="UI smoke profile";
        var save=Find<Button>(content).First(x=>x.Content is string s && s=="Save profile");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(!prefs.Profiles.Any(p=>p.Name=="UI smoke profile" && p.State.Effect=="Rainbow"))throw new Exception("Save profile control failed.");checks.Add("Saved a profile through the actual interface");
        if(output || backend is not null)throw new Exception("UI smoke test unexpectedly enabled hardware.");checks.Add("Hardware output remains disabled throughout");
        if(catalog.Count!=462)throw new Exception("The shipped compatibility catalog is missing or incomplete.");checks.Add("Loaded the complete 462-entry reference catalog");
        SetNote("Preview only. Connect a lighting controller to use real devices.");
        prefs.Studio=new();surface.State=prefs.Studio;Navigate("Studio");RenderPreview(Path.Combine(folder,"studio-final.png"));
        File.WriteAllText(Path.Combine(folder,"ui-test-results.json"),JsonSerializer.Serialize(new{Passed=checks.Count,Failed=0,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}

public sealed class CatalogEntry
{
    public string Name {get;set;}="";
    public string Publisher {get;set;}="";
    public string Kind {get;set;}="";
    public int? VendorId {get;set;}
    public int? ProductId {get;set;}
}
