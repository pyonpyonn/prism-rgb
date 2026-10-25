using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrismRGB;

public sealed partial class MainWindow
{
    static IEnumerable<T> FindBootControls<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match)yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in FindBootControls<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    FrameworkElement BuildBootAnimationLibrary()
    {
        var draft=prefs.BootAnimation;
        bool isWave=draft.Style=="RainbowWave";
        var presets=prefs.BootAnimationPresets.Concat(BootAnimationLibrary.BuiltIns).Where(p=>p.Style!="RainbowWave").GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Select(group=>group.First()).ToList();
        if(!isWave && !presets.Any(p=>string.Equals(p.Name,draft.Name,StringComparison.OrdinalIgnoreCase)))presets.Insert(0,draft.Clone());
        var library=new ComboBox{ItemsSource=presets,DisplayMemberPath="Name",SelectedItem=presets.FirstOrDefault(p=>string.Equals(p.Name,draft.Name,StringComparison.OrdinalIgnoreCase)),IsEnabled=!busy && !isWave};
        System.Windows.Automation.AutomationProperties.SetName(library,"Boot preset library");
        library.SelectionChanged+=(_,_)=>
        {
            if(library.SelectedItem is BootAnimationPreset preset){prefs.BootAnimation=preset.Clone();ScheduleSave();Navigate("Settings");}
        };
        var name=new TextBox{Text=draft.Name,MaxLength=60,IsEnabled=!busy && !isWave};
        System.Windows.Automation.AutomationProperties.SetName(name,"Boot preset name");
        const string waveLabel="Wave · filled rainbow",animatedLabel="Colour transition · independent timing",steadyLabel="Steady · same colour on every light";
        var style=new ComboBox{ItemsSource=new[]{waveLabel,animatedLabel,steadyLabel},SelectedItem=isWave?waveLabel:draft.Style=="Steady"?steadyLabel:animatedLabel,IsEnabled=!busy};
        System.Windows.Automation.AutomationProperties.SetName(style,"Boot effect");
        var speedPicker=new ComboBox{ItemsSource=new[]{"Slow","Balanced","Fast"},SelectedItem=draft.MotionSpeed,IsEnabled=!busy};
        System.Windows.Automation.AutomationProperties.SetName(speedPicker,"Boot motion speed");
        var first=new TextBox{Text=draft.Color1,MaxLength=7,IsEnabled=!busy && !isWave};
        var second=new TextBox{Text=draft.Color2,MaxLength=7,IsEnabled=!busy && !isWave};
        System.Windows.Automation.AutomationProperties.SetName(first,"Boot colour one hex");
        System.Windows.Automation.AutomationProperties.SetName(second,"Boot colour two hex");
        var level=new Slider{Minimum=10,Maximum=100,Value=Math.Clamp(double.IsFinite(draft.Brightness)?draft.Brightness*100:100,10,100),IsEnabled=!busy};
        System.Windows.Automation.AutomationProperties.SetName(level,"Boot brightness");
        var levelLabel=Small($"Boot brightness · {level.Value:F0}%");
        var sample=new Border{Height=28,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,8,0,8)};
        var validation=Small("");
        Button preview=null!,save=null!,savePreset=null!;
        string TimingHint()=>draft.Style=="RainbowWave"?"A travelling wave using the controllers' fixed rainbow colours. The filled wave stays lit between passes; custom colours are not supported in this native mode. Device timing is independent before Windows.":draft.Style=="Steady"?"Every output stays on colour 1. This keeps the colour identical across devices.":"Both colours stay lit: the motherboard fades and the GPU runs its own colour cycle. Their animations have independent timing before Windows.";
        Brush SampleBrush(string[] colors)
        {
            if(draft.Style!="RainbowWave")return new LinearGradientBrush(Effects.Parse(colors[0]),Effects.Parse(draft.Style=="Steady"?colors[0]:colors[1]),0);
            var brush=new LinearGradientBrush();
            for(int i=0;i<=6;i++)brush.GradientStops.Add(new GradientStop(Effects.Hsv(i/6.0,1,1),i/6.0));
            return brush;
        }
        void Update()
        {
            draft.Name=name.Text;draft.Color1=first.Text.ToUpperInvariant();draft.Color2=second.Text.ToUpperInvariant();
            draft.Style=(string?)style.SelectedItem==waveLabel?"RainbowWave":(string?)style.SelectedItem==steadyLabel?"Steady":"Fade";
            draft.MotionSpeed=(string?)speedPicker.SelectedItem??"Balanced";draft.Brightness=level.Value/100;
            levelLabel.Text=$"Boot brightness · {level.Value:F0}%";
            bool valid=true;
            try
            {
                var colors=draft.EffectivePalette();
                sample.Background=SampleBrush(colors);
                validation.Text=TimingHint();
            }
            catch(ArgumentException ex){valid=false;validation.Text=ex.Message;sample.Background=B("#293238");}
            if(preview is not null)preview.IsEnabled=valid && !busy;
            if(save is not null)save.IsEnabled=valid && !busy;
            if(savePreset is not null)savePreset.IsEnabled=valid && !busy && !isWave;
            speedPicker.IsEnabled=!busy && draft.Style!="Steady";
            ScheduleSave();
        }
        Button Pick(string label,TextBox hex)
        {
            var button=Btn(label,()=>
            {
                var color=Effects.ValidColor(hex.Text)?Effects.Parse(hex.Text):Colors.OrangeRed;
                var picker=new ColorEditor(color,label){Owner=this};
                if(picker.ShowDialog()==true)hex.Text=ColorValues.Hex(picker.SelectedColor);
            });
            button.IsEnabled=!busy && !isWave;System.Windows.Automation.AutomationProperties.SetName(button,label);return button;
        }
        preview=Btn("Preview boot lighting · no hardware save",async()=>{Update();await SaveHardwareBootColorAsync(true,true);});
        save=Btn("Save boot lighting to hardware",async()=>{Update();await SaveHardwareBootColorAsync(true);},true);
        preview.IsEnabled=save.IsEnabled=!busy;
        System.Windows.Automation.AutomationProperties.SetName(preview,"Preview boot preset");
        System.Windows.Automation.AutomationProperties.SetName(save,"Save boot preset to hardware");
        savePreset=Btn("Save preset to my library",()=>
        {
            try
            {
                Update();var custom=draft.Clone();
                if(BootAnimationLibrary.BuiltIns.Any(p=>p.Name.Equals(custom.Name,StringComparison.OrdinalIgnoreCase)))custom.Name+=" (custom)";
                BootAnimationLibrary.SavePreset(prefs,custom);prefs.BootAnimation=custom.Clone();Save();
                SetNote("Preset saved in your library. Controller memory was not changed.");Navigate("Settings");
            }
            catch(ArgumentException ex){SetNote(ex.Message);}
        });
        savePreset.IsEnabled=!busy && !isWave;
        System.Windows.Automation.AutomationProperties.SetName(savePreset,"Save boot preset to library");
        var current=Btn("Use my current Studio colours",()=>
        {
            var colors=Effects.Palette(prefs.Studio).Where(c=>Effects.Parse(c)!=Colors.Black).Take(2).ToArray();
            if(colors.Length==0){SetNote("Choose a lit Studio colour first.");return;}
            prefs.BootAnimation=new(){Name="My Studio colours",Style="Fade",Color1=colors[0],Color2=colors.Length>1?colors[1]:colors[0],Brightness=Math.Clamp(prefs.Studio.Brightness,.1,1),MotionSpeed=draft.MotionSpeed};
            ScheduleSave();Navigate("Settings");
        });current.IsEnabled=!busy && !isWave;
        Button Dropper(string label,TextBox hex)
        {
            var button=Btn(label,async()=>
            {
                if(busy || pickingColor)return;pickingColor=true;
                try{var selected=await ScreenEyedropper.PickAsync();if(selected is Color color)hex.Text=ColorValues.Hex(color);}
                catch(Exception ex){SetNote("Could not pick a screen colour: "+ex.Message);}
                finally{pickingColor=false;}
            });button.IsEnabled=!busy && !isWave;return button;
        }
        name.TextChanged+=(_,_)=>Update();first.TextChanged+=(_,_)=>Update();second.TextChanged+=(_,_)=>Update();
        style.SelectionChanged+=(_,_)=>
        {
            string target=(string?)style.SelectedItem==waveLabel?"RainbowWave":(string?)style.SelectedItem==steadyLabel?"Steady":"Fade";
            if(target==draft.Style)return;
            var selected=target=="RainbowWave"?BootAnimationLibrary.FilledWave:draft.Style=="RainbowWave"?BootAnimationLibrary.BuiltIns.First(p=>p.Style=="Fade"):draft.Clone();
            selected.Style=target;selected.Brightness=draft.Brightness;selected.MotionSpeed=draft.MotionSpeed;
            prefs.BootAnimation=selected;ScheduleSave();Navigate("Settings");
        };
        speedPicker.SelectionChanged+=(_,_)=>Update();level.ValueChanged+=(_,_)=>Update();
        // Initial drawing and validation do not schedule a save or touch hardware.
        try{var colors=draft.EffectivePalette();sample.Background=SampleBrush(colors);validation.Text=TimingHint();}
        catch(ArgumentException ex){validation.Text=ex.Message;preview.IsEnabled=save.IsEnabled=savePreset.IsEnabled=false;}
        speedPicker.IsEnabled=!busy && draft.Style!="Steady";
        return Box(V(T("Boot animation library",18,"#E5EEE8",true),Gap(10),
            Small("Choose an animation first. Wave is a travelling, filled rainbow effect. Colour transition fades between chosen colours; Steady holds one colour. Each runs in hardware before Windows, with independent device timing."),Gap(10),
            Small("Animation"),style,Gap(8),Small($"Colour palette presets · {BootAnimationLibrary.BuiltIns.Count(p=>p.Style!="RainbowWave")} choices for transition or steady"),library,Gap(8),
            Pick("Choose boot colour 1",first),first,Dropper("Eyedropper · boot colour 1",first),Pick("Choose boot colour 2",second),second,Dropper("Eyedropper · boot colour 2",second),sample,validation,Gap(8),
            levelLabel,level,Small("Motion speed"),speedPicker,current,Gap(10),Small("Preset name"),name,savePreset,Gap(10),preview,save,Gap(8),
            Small(prefs.HardwareBootWaveSaved?$"Last accepted hardware preset: {prefs.HardwareBootPresetSavedName}. Choosing another preset does not replace it until Save.":"No boot library preset has been saved to both controllers yet."),Gap(8),
            Small("Colour choices and library saves only update this PC. Preview pauses Prism and changes live lighting without storing it. Save writes once to each controller. The boot helper preserves the saved effect; power off and on to check retention.")));
    }

    async Task RunBootLibraryUiChecksAsync(string folder,string bootSnapshot,List<string> checks)
    {
        int saves=0,previews=0;HardwareWaveRequest? captured=null;
        var ready=new TaskCompletionSource<HardwareBootSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child=new MainWindow(new FakeStartupRegistration(),()=>throw new Exception("Unexpected backend"),()=>false,_=>throw new Exception("Unexpected static UI saver"),request=>{saves++;captured=request;return ready.Task;},request=>{previews++;captured=request;return Task.FromResult(new HardwareWavePreviewResult{MotherboardApplied=true,GpuApplied=true});});
        child.prefs.BootAnimation=new();child.prefs.BootAnimationPresets=new();child.prefs.HardwareBootWaveSaved=false;
        string scene=JsonSerializer.Serialize(child.prefs.Studio),profiles=JsonSerializer.Serialize(child.prefs.Profiles);
        child.Show();child.Navigate("Settings");child.UpdateLayout();
        TControl Control<TControl>(string automation) where TControl:DependencyObject=>FindBootControls<TControl>(child.content).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)==automation);
        Control<ComboBox>("Boot preset library").SelectedIndex=1;child.UpdateLayout();
        if(saves!=0 || previews!=0 || child.prefs.BootAnimation.Name==new BootAnimationPreset().Name)throw new Exception("Preset selection wrote hardware or failed to change the draft.");
        Control<TextBox>("Boot colour one hex").Text="#000000";child.UpdateLayout();
        if(Control<Button>("Save boot preset to hardware").IsEnabled || Control<Button>("Preview boot preset").IsEnabled)throw new Exception("Black was accepted for continuous boot lighting.");
        Control<TextBox>("Boot colour one hex").Text="#FF0D00";
        Control<TextBox>("Boot colour two hex").Text="#FF6401";
        Control<TextBox>("Boot preset name").Text="My boot colours";
        Control<Button>("Save boot preset to library").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));child.UpdateLayout();
        if(child.prefs.BootAnimationPresets.Count!=1 || saves!=0 || previews!=0)throw new Exception("Saving a library preset touched hardware.");
        Control<Button>("Preview boot preset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<200 && child.busy;i++)await Task.Delay(10);child.UpdateLayout();
        if(previews!=1 || saves!=0 || captured?.MotionKind!="Fade" || captured.Palette.Length!=2 || child.prefs.HardwareBootWaveSaved || JsonSerializer.Serialize(child.prefs.Studio)!=scene)throw new Exception("Preview changed memory or Windows scene.");
        var save=Control<Button>("Save boot preset to hardware");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(saves!=1 || !child.busy || child.prefs.HardwareBootWaveSaved)throw new Exception("Boot save overlapped or marked unaccepted delivery as saved.");
        ready.SetResult(new(){Effect="Fade",Motherboard=new(){Saved=true},Gpu=new(){Saved=true}});
        for(int i=0;i<200 && child.busy;i++)await Task.Delay(10);child.UpdateLayout();
        if(!child.prefs.HardwareBootWaveSaved || child.prefs.HardwareBootEffect!="Fade" || JsonSerializer.Serialize(child.prefs.Studio)!=scene || JsonSerializer.Serialize(child.prefs.Profiles)!=profiles || !BootLighting.LoadSnapshot(bootSnapshot).PreserveHardwareWave)throw new Exception("Saved preset did not preserve the Windows scene and native boot animation.");
        Control<ComboBox>("Boot effect").SelectedItem="Wave · filled rainbow";child.UpdateLayout();
        if(saves!=1 || previews!=1 || child.prefs.HardwareBootEffect!="Fade" || child.prefs.BootAnimation.Style!="RainbowWave" || Control<TextBox>("Boot colour one hex").IsEnabled || Control<TextBox>("Boot colour two hex").IsEnabled || Control<ComboBox>("Boot preset library").IsEnabled || Control<Button>("Choose boot colour 1").IsEnabled || Control<Button>("Save boot preset to library").IsEnabled)throw new Exception("Wave selection wrote hardware or left ignored custom-colour controls active.");
        Control<Slider>("Boot brightness").Value=50;Control<ComboBox>("Boot motion speed").SelectedItem="Slow";
        Control<Button>("Preview boot preset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<200 && child.busy;i++)await Task.Delay(10);child.UpdateLayout();
        if(previews!=2 || saves!=1 || captured?.MotionKind!="RainbowWave" || captured.BootBrightness!=.5 || captured.MotionSpeed!="Slow" || child.prefs.HardwareBootEffect!="Fade" || JsonSerializer.Serialize(child.prefs.Studio)!=scene)throw new Exception("Wave preview lost native brightness/speed, committed memory, or changed Windows lighting.");
        Control<Button>("Save boot preset to hardware").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<200 && child.busy;i++)await Task.Delay(10);child.UpdateLayout();
        if(saves!=2 || child.prefs.HardwareBootEffect!="RainbowWave" || !child.prefs.HardwareBootWaveSaved || JsonSerializer.Serialize(child.prefs.Studio)!=scene || JsonSerializer.Serialize(child.prefs.Profiles)!=profiles || !BootLighting.LoadSnapshot(bootSnapshot).PreserveHardwareWave)throw new Exception("Explicit Wave save lost the native animation or changed Windows profiles.");
        FindBootControls<ScrollViewer>(child.content).First().ScrollToVerticalOffset(580);child.UpdateLayout();child.RenderCurrentPage(Path.Combine(folder,"boot-library.png"));
        child.RenderCurrentPage(Path.Combine(folder,"boot-filled-wave.png"));
        await child.CloseForPreview();
        checks.Add("Boot library colour editing blocks black, and library selection/save never performs controller I/O.");
        checks.Add("Boot library preview is volatile; hardware save is explicit, single and preserves Windows profiles.");
        checks.Add("Filled Wave is an animation choice with fixed colours, native brightness/speed, volatile preview and explicit save; Windows scene stays unchanged.");
    }
}
