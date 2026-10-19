using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    readonly LightingSurface surface=new(); readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(125)};
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly HardwareFrameClock frameClock=new();readonly object hardwareTick=new();int pendingHardwareTick;
    readonly List<Button> navButtons=new();
    ILightingBackend? backend; System.Windows.Forms.NotifyIcon? tray;
    List<HidDevice> usbDevices=new(); List<CatalogEntry> catalog=new();
    bool busy,writing,output,paused,closing,windowClosed,refreshing,screenBusy,outputDirty; double lastScreen,lastPreview,seconds;
    public long PreviewFrames {get;private set;}
    public long OutputFrames {get;private set;}
    public long ScreenCaptures {get;private set;}
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
        var bottom=V(Box(V(T("STANDALONE · LOW POWER",10,"#83E1C2",true),Gap(7),Small("Direct MSI USB control.\nYour profiles stay on this PC.")),"#1D2A2B",new Thickness(13)),Gap(12),Small("PRISM RGB   /   0.3.0"));bottom.Margin=new Thickness(16,0,16,22);DockPanel.SetDock(bottom,Dock.Bottom);rail.Children.Add(bottom);
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
        frameClock.Failed+=exception=>
        {
            if(closing || Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(async()=>{await StopAsync();SetNote("Lighting timer stopped: "+exception.Message);}));
        };
        timer.Tick+=Tick;saveTimer.Tick+=(_,_)=>{saveTimer.Stop();Save();};
        LoadCatalog();Navigate("Studio");
        Loaded+=async(_,_)=>await ScanUsbAsync();
        Closing+=(_,e)=>
        {
            if(closing)return;e.Cancel=true;
            // Let WPF finish the cancelled Closing event before Hide/Close is called again.
            Dispatcher.BeginInvoke(new Action(async()=>
            {
                if(closing)return;
                if(prefs.MinimizeToTray){Hide();return;}
                await ExitAsync();
            }));
        };
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized && prefs.MinimizeToTray)Hide();RefreshActivity();};
        IsVisibleChanged+=(_,_)=>RefreshActivity();
        Closed+=(_,_)=>windowClosed=true;
        SetupTray();
    }
    void SetupTray()
    {
        tray=new(){Text="Prism RGB",Icon=System.Drawing.SystemIcons.Application,Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Prism RGB",null,(_,_)=>Dispatcher.Invoke(Reopen));
        menu.Items.Add("Stop lighting and restore",null,(_,_)=>Dispatcher.Invoke(async()=>await StopAsync()));
        menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(async()=>await ExitAsync()));tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Reopen);
    }
    void Reopen(){if(closing)return;Show();WindowState=WindowState.Normal;Activate();}
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
        content.Children.Add(name switch{"Studio"=>StudioPage(),"Devices"=>DevicesPage(),"Profiles"=>ProfilesPage(),"Compatibility"=>CatalogPage(),_=>SettingsPage()});UpdateStatus();RefreshActivity();
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
        var stack=V();
        var direct=V(T("MSI ARGB + Gigabyte GPU",18,"#E7F3EC",true),Gap(8),Small("Direct control of B550M PRO-VDH WIFI headers and the supported Gigabyte RTX 4070 Ti. Individual ARGB colors, saved LED counts, and 30 Hz hardware effects. Preview rendering stays at 8 Hz."),Gap(14));
        var directBtn=Btn(backend is not null?"Connected":"Connect lighting",async()=>await ConnectAsync());directBtn.IsEnabled=!busy && backend is null;direct.Children.Add(directBtn);connectButton=directBtn;
        stack.Children.Add(Box(direct));
        stack.Children.Add(Gap(12));stack.Children.Add(Small("Close other RGB apps before connecting. Start lighting enables output. Unchecked motherboard headers are dark while Prism controls the motherboard; Stop clears streamed pixels and restores its configuration."));stack.Children.Add(Gap(20));
        if(backend is not null)
        {
            var header=new Grid();header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.Children.Add(T($"{backend.Devices.Count} lighting outputs",17,"#E6F0EA",true));
            var disconnect=Btn("Disconnect",async()=>await DisconnectAsync());disconnect.IsEnabled=!busy;Grid.SetColumn(disconnect,1);header.Children.Add(disconnect);stack.Children.Add(header);stack.Children.Add(Gap(8));
            foreach(var d in backend.Devices.OrderBy(d=>d.Id==100?0:d.Id>=2?1:2).ThenBy(d=>d.Id))
            {
                string units=d.Id==100?"lighting zones":d.LedCount==1?"LED":"LEDs";
                var line=V();var enabled=new CheckBox{Content=$"{d.Name}   ·   {d.LedCount} {units}",IsChecked=d.Placement.Enabled};
                enabled.IsEnabled=!output;enabled.ToolTip=output?"Stop lighting to change the selected outputs.":null;
                enabled.Checked+=(_,_)=>{d.Placement.Enabled=true;ScheduleSave();};enabled.Unchecked+=(_,_)=>{d.Placement.Enabled=false;ScheduleSave();};line.Children.Add(enabled);
                line.Children.Add(Small("POSITION IN SCENE"));var sliders=new Grid();for(int i=0;i<3;i++)sliders.ColumnDefinitions.Add(new());
                AddPosition(sliders,"Horizontal",d.Placement.X,v=>d.Placement.X=v,0);AddPosition(sliders,"Vertical",d.Placement.Y,v=>d.Placement.Y=v,1);
                if(d.Id>=2)AddPosition(sliders,"LED span",d.Placement.Span,v=>d.Placement.Span=v,2);
                line.Children.Add(sliders);var card=Box(line,"#192126",new Thickness(15,8,15,0));card.Margin=new Thickness(0,4,0,4);stack.Children.Add(card);
                if(d.Id is 2 or 3)
                {
                    var count=new TextBox{Text=d.LedCount.ToString(),Width=65,MaxLength=3,Margin=new Thickness(8,0,8,0),IsEnabled=!output};
                    var apply=Btn("Apply count",()=>
                    {
                        if(!int.TryParse(count.Text,out var value) || value<1 || value>120){SetNote("Enter an LED count from 1 to 120 for this header.");return;}
                        d.LedCount=value;d.Placement.LedCount=value;
                        foreach(int index in d.Placement.LedColors.Keys.Where(i=>i>=value).ToArray())d.Placement.LedColors.Remove(index);
                        ScheduleSave();Navigate("Devices");
                    });apply.IsEnabled=!output;
                    var countRow=new StackPanel{Orientation=Orientation.Horizontal};countRow.Children.Add(T("LED count",12));countRow.Children.Add(count);countRow.Children.Add(apply);line.Children.Add(countRow);
                    var reverse=new CheckBox{Content="Reverse effect direction",IsChecked=d.Placement.Reverse};
                    reverse.Checked+=(_,_)=>{d.Placement.Reverse=true;ScheduleSave();};reverse.Unchecked+=(_,_)=>{d.Placement.Reverse=false;ScheduleSave();};line.Children.Add(reverse);
                    var editor=new Expander{Header="Choose colors for individual LEDs",Foreground=B("#DCE8E2"),Margin=new Thickness(0,5,0,12)};
                    editor.Expanded+=(_,_)=>{if(editor.Content is null)editor.Content=PixelEditor(d);};line.Children.Add(editor);
                }
                else if(d.Id==100)
                {
                    var editor=new Expander{Header="Choose individual fan and logo colors",Foreground=B("#DCE8E2"),Margin=new Thickness(0,5,0,12)};
                    editor.Expanded+=(_,_)=>{if(editor.Content is null)editor.Content=PixelEditor(d);};line.Children.Add(editor);
                }
            }
            startButton=Btn(output?"Stop & restore":"Start lighting",async()=>{if(output)await StopAsync();else StartOutput();},true);startButton.Margin=new Thickness(0,12,0,0);stack.Children.Add(startButton);
        }
        stack.Children.Add(Gap(22));var scan=Btn(refreshing?"Scanning…":"Refresh USB devices",async()=>await ScanUsbAsync());scan.IsEnabled=!refreshing;scan.HorizontalAlignment=HorizontalAlignment.Left;stack.Children.Add(scan);stack.Children.Add(Gap(10));
        foreach(var group in usbDevices.GroupBy(x=>x.Id))
        {
            var d=group.First();string match=MsiProtocol.Supports(Hid.BoardName(),d)?"Standalone MSI header adapter available":"No native RGB adapter included";
            var card=Box(V(T(d.Name,13,"#DCE8E2",true),Gap(4),Small($"USB {d.Id}   ·   {match}")),"#172025",new Thickness(15));card.Margin=new Thickness(0,4,0,4);stack.Children.Add(card);
        }
        stack.Children.Add(Gap(8));stack.Children.Add(Small("USB detection identifies interfaces, not whether a device has controllable RGB. Keyboard and mouse input are never modified."));
        return new ScrollViewer{Content=stack};
    }
    void AddPosition(Grid parent,string name,double value,Action<double> set,int col)
    {
        var s=new Slider{Value=value*100,Margin=new Thickness(0,5,12,10)};s.ValueChanged+=(_,_)=>{set(s.Value/100);ScheduleSave();};var p=V(Small(name),s);Grid.SetColumn(p,col);parent.Children.Add(p);
    }
    UIElement PixelEditor(OutputDevice device)
    {
        var wrap=new WrapPanel{Margin=new Thickness(0,8,0,8)};
        for(int i=0;i<device.LedCount;i++)
        {
            int index=i;
            string name=i<device.PixelNames.Length?device.PixelNames[i]:(i+1).ToString();
            var button=Btn(name,()=>{});button.MinWidth=40;button.Height=32;button.Padding=new Thickness(7,0,7,0);button.Margin=new Thickness(2);
            if(device.Placement.LedColors.TryGetValue(i,out var hex))button.Background=B(hex);
            button.ToolTip=$"{name}: click to override its effect color";
            button.Click+=(_,_)=>
            {
                using var picker=new System.Windows.Forms.ColorDialog{FullOpen=true};
                var initial=device.Placement.LedColors.TryGetValue(index,out var color)?Effects.Parse(color):Effects.Parse(prefs.Studio.Color);
                picker.Color=System.Drawing.Color.FromArgb(initial.R,initial.G,initial.B);
                if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
                string selected=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
                device.Placement.LedColors[index]=selected;button.Background=B(selected);ScheduleSave();
            };
            wrap.Children.Add(button);
        }
        return V(Small("Each entry overrides one LED or named GPU lighting zone. Global brightness applies to these colors."),wrap,Btn("Return all LEDs to the effect",()=>{device.Placement.LedColors.Clear();ScheduleSave();Navigate("Devices");}));
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
            Box(V(T("About Prism RGB",18,"#E5EEE8",true),Gap(10),Small("Version 0.3.0 · Standalone Windows desktop\nDirect MSI control with independent effects and interface.\n\nLow-power mode is always on: up to 30 hardware updates/second with an 8 Hz preview, software-rendered preview, no hidden preview animation, and no repeated USB writes for unchanged colors. Solid lighting sleeps between changes. Screen ambience captures a small desktop sample twice/second.\n\nIncluded: seven effects, draggable preview, profiles, USB discovery, individual MSI ARGB LEDs, per-LED color overrides, and Gigabyte RTX 4070 Ti lighting. Other devices require future native adapters."),Gap(12),Btn("Open inspection report",()=>OpenReport()))));
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
    async Task ConnectAsync()
    {
        if(busy || backend is not null)return;busy=true;SetNote("Reading controller information…");Navigate("Devices");
        ILightingBackend candidate=new NativeLightingBackend();
        ((NativeLightingBackend)candidate).OutputFaulted+=exception=>
        {
            if(closing || Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(async()=>
            {
                if(backend!=candidate || closing)return;
                await StopAsync();SetNote("GPU lighting stopped: "+exception.Message+" Reconnect to retry.");
            }));
        };
        try
        {
            if(Hid.ConflictRunning())throw new InvalidOperationException("Close SignalRGB and MSI lighting tools before enabling another lighting controller.");
            await candidate.ConnectAsync();backend=candidate;
            for(int i=0;i<backend.Devices.Count;i++)
            {
                var d=backend.Devices[i];var placement=prefs.Placements.FirstOrDefault(x=>x.Identity==d.Identity);
                if(placement is null){placement=new(){Identity=d.Identity,X=.1,Y=.25+i*.15,Span=0};placement.Y=Math.Min(.9,placement.Y);prefs.Placements.Add(placement);}
                d.Placement=placement;
                if(d.Id is 2 or 3)
                {
                    if(placement.LedCount==0)
                    {
                        placement.LedCount=d.LedCount;
                        if(placement.Span<.05)placement.Span=.75;
                    }
                    d.LedCount=Math.Clamp(placement.LedCount,1,120);
                }
                else if(d.Id==100 && placement.Span<.05)placement.Span=.65;
            }
            ScheduleSave();SetNote($"Connected to {backend.Devices.Count} lighting outputs. {((NativeLightingBackend)backend).GpuStatus} Start lighting when ready.");
        }
        catch(Exception e){await candidate.DisposeAsync();SetNote("Connection failed: "+e.Message);}
        finally{busy=false;Navigate("Devices");}
    }
    void StartOutput()
    {
        if(busy)return;
        if(backend is null){SetNote("Choose a controller under Devices first.");Navigate("Devices");return;}
        if(!backend.Devices.Any(x=>x.Placement.Enabled && x.LedCount>0)){SetNote("Select at least one output with LEDs.");return;}
        if(Hid.ConflictRunning()){SetNote("Close SignalRGB and MSI lighting tools before starting output.");return;}
        output=true;outputDirty=true;SetNote("Lighting is active. Stop restores motherboard configuration and clears streamed motherboard/GPU colors.");Navigate(page);
    }
    async Task WaitForFrameAsync(){while(writing)await Task.Delay(15);}
    async Task StopAsync()
    {
        if(busy)return;busy=true;output=false;RefreshActivity();
        try{await WaitForFrameAsync();if(backend is not null)await backend.RestoreAsync();SetNote("Lighting stopped. Streamed motherboard/GPU colors cleared and original motherboard configuration restored.");}
        catch(Exception e){SetNote("Lighting stopped; restore was incomplete: "+e.Message);}
        finally{busy=false;if(!closing)Navigate(page);}
    }
    async Task DisconnectAsync()
    {
        if(busy)return;await StopAsync();busy=true;
        if(backend is not null)await backend.DisposeAsync();backend=null;busy=false;Navigate("Devices");
    }
    bool PreviewActive=>IsVisible && WindowState!=WindowState.Minimized && page=="Studio" && !paused;
    bool Animated=>prefs.Studio.Effect is not ("Solid" or "Screen ambience");
    void RefreshActivity()
    {
        frameClock.Set(output && Animated && !closing,()=>
        {
            if(Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref pendingHardwareTick,1)!=0)return;
            Dispatcher.BeginInvoke(new Action(()=>
            {
                Interlocked.Exchange(ref pendingHardwareTick,0);
                if(output && Animated && !closing)Tick(hardwareTick,EventArgs.Empty);
            }));
        });
        if(closing){timer.Stop();return;}
        if(PreviewActive){surface.ScreenColor=screen;surface.InvalidateVisual();}
        bool needed=ActivityPolicy.NeedsTicks(PreviewActive,output,Animated,prefs.Studio.Effect=="Screen ambience",outputDirty);
        if(needed && !frameClock.Active)timer.Start();else timer.Stop();
    }
    async void Tick(object? sender,EventArgs args)
    {
        double now=watch.Elapsed.TotalSeconds;seconds=now;
        if(PreviewActive && Animated && (!output || now-lastPreview>=.125)){lastPreview=now;surface.Seconds=seconds;surface.InvalidateVisual();PreviewFrames++;}
        if(prefs.Studio.Effect=="Screen ambience" && (PreviewActive || output) && !screenBusy && now-lastScreen>=.5)
        {
            screenBusy=true;lastScreen=now;
            try
            {
                screen=await Task.Run(ScreenSampler.Capture);ScreenCaptures++;outputDirty=true;
                if(PreviewActive){surface.ScreenColor=screen;surface.InvalidateVisual();PreviewFrames++;}
            }
            catch(Exception e){screen=Colors.Black;outputDirty=true;SetNote("Screen capture unavailable: "+e.Message);}
            finally{screenBusy=false;}
        }
        if(!output || backend is null || writing || busy || (Animated && sender!=hardwareTick) || (!Animated && !outputDirty)){RefreshTickState();return;}
        writing=true;outputDirty=false;
        try
        {
            var colors=new Dictionary<uint,Color[]>();
            foreach(var d in backend.Devices.Where(x=>x.Placement.Enabled && x.LedCount>0))
            {
                var values=OutputColors.Sample(prefs.Studio,d,seconds,screen);
                colors.Add(d.Id,values);
            }
            await backend.WriteAsync(colors);OutputFrames++;
        }
        catch(Exception e)
        {
            output=false;SetNote("Output stopped: "+e.Message+" Disconnect and reconnect to retry.");
            try{await backend.RestoreAsync();}catch{}UpdateStatus();if(page=="Devices"||page=="Studio")Navigate(page);
        }
        finally{writing=false;RefreshTickState();}
    }
    void RefreshTickState()
    {
        if(!ActivityPolicy.NeedsTicks(PreviewActive,output,Animated,prefs.Studio.Effect=="Screen ambience",outputDirty))timer.Stop();
    }
    void UpdateStatus()
    {
        connectionText.Text=output?"●  LIGHTING ACTIVE":backend is null?"○  PREVIEW MODE":"●  CONTROLLER READY";
        connectionText.Foreground=B(output?"#9CE8C5":"#A2B7AE");footer.Text=note;
        if(startButton is not null){startButton.Content=output?"Stop & restore":"Start lighting";startButton.IsEnabled=!busy;}
    }
    void SetNote(string message){note=message;UpdateStatus();}
    void ScheduleSave(){outputDirty=true;RefreshActivity();saveTimer.Stop();saveTimer.Start();}
    void Save(){try{Storage.Save(prefs);}catch(Exception e){SetNote("Could not save preferences: "+e.Message);}}
    async Task ExitAsync()
    {
        if(closing)return;if(busy){SetNote("Wait for the current connection operation to finish before quitting.");return;}
        closing=true;timer.Stop();saveTimer.Stop();frameClock.Dispose();await StopAsync();if(backend is not null)await backend.DisposeAsync();Save();tray?.Dispose();Close();
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
    public async Task CloseForPreview()
    {
        if(windowClosed)return;
        prefs.MinimizeToTray=false;
        var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed+=(_,_)=>closed.TrySetResult();
        if(!closing)Close();await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    public async Task RunBenchmarkAsync(string path,string mode)
    {
        if(mode is not ("animated" or "solid" or "hidden" or "screen" or "hardware-animated" or "hardware-solid" or "hardware-rainbow"))throw new ArgumentException("Unknown benchmark scenario.");
        prefs.Studio=new(){Effect=mode=="hardware-rainbow"?"Rainbow":mode.Contains("solid")?"Solid":mode=="screen"?"Screen ambience":"Aurora",Brightness=mode=="hardware-rainbow"?.7:.15};
        surface.State=prefs.Studio;Navigate("Studio");RefreshActivity();
        bool hardware=mode.StartsWith("hardware-",StringComparison.Ordinal);
        if(hardware)
        {
            await ConnectAsync();if(backend is null)throw new IOException(note);
            foreach(var d in backend.Devices)d.Placement.Enabled=true;
            StartOutput();if(!output)throw new IOException(note);Hide();
        }
        if(mode=="hidden")Hide();
        await Task.Delay(4000);
        using var process=Process.GetCurrentProcess();
        var cpu=process.TotalProcessorTime;var wall=Stopwatch.StartNew();
        long frames=PreviewFrames,outputs=OutputFrames,captures=ScreenCaptures;
        int writes=(backend as NativeLightingBackend)?.UsbWrites??0,i2c=(backend as NativeLightingBackend)?.I2cWrites??0;
        try
        {
            await Task.Delay(20000);process.Refresh();
            double core=(process.TotalProcessorTime-cpu).TotalSeconds/wall.Elapsed.TotalSeconds*100;
            if(hardware && !output)throw new IOException("Output stopped during the benchmark: "+note);
            var result=new{Version="0.3.0",Scenario=mode,Effect=prefs.Studio.Effect,Brightness=prefs.Studio.Brightness,Seconds=wall.Elapsed.TotalSeconds,LogicalProcessors=Environment.ProcessorCount,CpuPercent=core/Environment.ProcessorCount,OneCorePercent=core,WorkingSetMiB=process.WorkingSet64/1048576.0,PreviewFrames=PreviewFrames-frames,OutputFrames=OutputFrames-outputs,UsbWrites=((backend as NativeLightingBackend)?.UsbWrites??0)-writes,I2cWrites=((backend as NativeLightingBackend)?.I2cWrites??0)-i2c,ScreenCaptures=ScreenCaptures-captures,Renderer="WPF SoftwareOnly",HardwareOutput=hardware};
            File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(hardware)
            {
                await StopAsync();
                if(backend is NativeLightingBackend native && native.NeedsRestore)throw new IOException("Hardware benchmark restoration was incomplete: "+note);
            }
        }
    }
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
        prefs.Studio.Effect="Solid";RefreshActivity();
        if(timer.IsEnabled)throw new Exception("Static preview has a periodic wakeup.");
        prefs.Studio.Effect="Aurora";Hide();RefreshActivity();await Task.Delay(350);
        if(timer.IsEnabled)throw new Exception("Hidden preview is still animating.");
        Show();Navigate("Settings");
        if(timer.IsEnabled)throw new Exception("Preview animates on another page.");
        checks.Add("Static, hidden and inactive preview pages stop the effect timer");
        prefs.Studio.Effect="Screen ambience";Hide();RefreshActivity();long count=ScreenCaptures;await Task.Delay(700);
        if(timer.IsEnabled || ScreenCaptures!=count)throw new Exception("Hidden idle screen capture is still running.");
        Show();checks.Add("Screen capture sleeps while hidden with hardware output off");
        prefs.Studio.Effect="Rainbow";
        var fakeTransport=new FakeMsiTransport();fakeTransport.Original[41]=fakeTransport.Original[52]=60;
        var fakeBackend=new MsiBackend(()=>fakeTransport,()=>false);await fakeBackend.ConnectAsync();backend=fakeBackend;
        foreach(var d in backend.Devices)d.Placement=new(){Identity=d.Identity,Span=.75};
        backend.Devices.Add(new(){Id=100,Name="Gigabyte RTX 4070 Ti · fans + logo",LedCount=4,PixelNames=new[]{"Fan 1","Fan 2","Fan 3","Logo"}});
        Navigate("Devices");UpdateLayout();RenderCurrentPage(Path.Combine(folder,"native-devices.png"));
        checks.Add("Rendered motherboard and GPU controls using fake hardware only");
        var chain=backend.Devices.First(d=>d.Id==2);
        var countInput=Find<TextBox>(content).First();countInput.Text="999";
        Find<Button>(content).First(b=>b.Content is string s && s=="Apply count").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(chain.LedCount!=60)throw new Exception("Oversized LED count entered the native backend.");
        countInput.Text="17";Find<Button>(content).First(b=>b.Content is string s && s=="Apply count").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(chain.LedCount!=17 || chain.Placement.LedCount!=17)throw new Exception("LED count control failed.");
        checks.Add("LED count control applies valid counts and rejects oversized chains");
        UpdateLayout();var direction=Find<CheckBox>(content).First(c=>c.Content is string s && s=="Reverse effect direction");direction.IsChecked=true;
        if(!chain.Placement.Reverse)throw new Exception("Reverse direction control failed.");checks.Add("Reverse direction control updates real output placement");
        var expand=Find<Expander>(content).First(e=>e.Header is string s && s=="Choose colors for individual LEDs");expand.IsExpanded=true;UpdateLayout();
        if(Find<Button>(expand).Count(b=>b.ToolTip is string s && s.Contains("click to override"))!=17)throw new Exception("Individual LED editor does not match its configured count.");
        var gpuEditor=Find<Expander>(content).First(e=>e.Header is string s && s=="Choose individual fan and logo colors");gpuEditor.IsExpanded=true;UpdateLayout();
        if(!Find<Button>(gpuEditor).Any(b=>b.Content is string s && s=="Logo"))throw new Exception("GPU logo control is missing.");
        checks.Add("Expanded individual ARGB pixels and named GPU fan/logo color controls");
        if(fakeTransport.Packets.Count!=0)throw new Exception("UI editing unexpectedly wrote lighting.");
        backend=null;await fakeBackend.DisposeAsync();
        SetNote("Preview only. Connect a lighting controller to use real devices.");
        prefs.Studio=new();surface.State=prefs.Studio;Navigate("Studio");RenderPreview(Path.Combine(folder,"studio-final.png"));
        File.WriteAllText(Path.Combine(folder,"ui-test-results.json"),JsonSerializer.Serialize(new{Passed=checks.Count,Failed=0,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}

public static class ActivityPolicy
{
    public static bool NeedsTicks(bool preview,bool output,bool animated,bool screen,bool dirty)=>((preview || output) && (animated || screen)) || (output && dirty);
}

public sealed class CatalogEntry
{
    public string Name {get;set;}="";
    public string Publisher {get;set;}="";
    public string Kind {get;set;}="";
    public int? VendorId {get;set;}
    public int? ProductId {get;set;}
}
