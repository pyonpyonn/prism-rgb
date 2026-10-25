using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PrismRGB;

internal static class BootAnimationLibraryChecks
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject(Action action)
    {
        bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}
        Check(rejected,"An invalid or dark boot preset was accepted.");
    }

    public static Task VerifyAsync()
    {
        var defaults=BootAnimationLibrary.BuiltIns;
        Check(defaults.Count>=19 && defaults.Select(preset=>preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==defaults.Count,"Built-in boot names are missing or duplicated.");
        Check(defaults.Take(2).Select(preset=>preset.Name).SequenceEqual(new[]{"Lava fade","Ocean fade"}),"The existing first preset choices changed order.");
        Check(defaults.Select(preset=>preset.Style).Distinct().OrderBy(style=>style).SequenceEqual(new[]{"Fade","RainbowWave","Steady"}),"The boot library lost a supported effect style.");
        Check(defaults.Select(preset=>preset.MotionSpeed).Distinct().OrderBy(speed=>speed).SequenceEqual(new[]{"Balanced","Fast","Slow"}),"The boot library lost its three supported speed choices.");
        foreach(var preset in defaults)
        {
            preset.Validate();Check(preset.Style is "Fade" or "Steady" or "RainbowWave","An unverified gapped mode entered the boot library.");
            Check(preset.EffectivePalette().Length==2 && preset.EffectivePalette().All(color=>color!="#000000"),"A built-in palette has a black stop.");
            var dimmed=preset.Clone();dimmed.Brightness=.1;dimmed.Validate();
            Check(dimmed.EffectivePalette().Select(Effects.Parse).All(color=>Math.Max(color.R,Math.Max(color.G,color.B))>=8),"A built-in colour became too dark at minimum boot brightness.");
            foreach(double brightness in new[]{.1,.25,.5,1.0})
            {
                var selectedPreset=preset.Clone();selectedPreset.Brightness=brightness;
                var selectedPreferences=new Preferences{BootAnimation=selectedPreset};
                var native=BootAnimationLibrary.CreateRequest(selectedPreferences);
                if(preset.Style=="RainbowWave")
                {
                    Check(native.MotionKind=="RainbowWave" && native.BootBrightness==brightness && native.Palette.SequenceEqual(new[]{"#FFFFFF","#FFFFFF"}),"Filled Wave used custom colours or lost native brightness.");
                    continue;
                }
                var colors=native.Palette.Select(Effects.Parse).ToArray();
                for(int step=0;step<=100;step++)
                {
                    var mixed=Effects.Mix(colors[0],colors[1],step/100.0);
                    Check(mixed.R!=0 || mixed.G!=0 || mixed.B!=0,"A palette transition introduced black between lit endpoints.");
                }
                var gpuPalette=HardwareBootWave.BuildGpuPalette(colors);
                Check(gpuPalette.Length==8 && gpuPalette.All(c=>c.R!=0 || c.G!=0 || c.B!=0),"GPU boot palette contains a black slot.");
                var banks=GigabyteProtocol.WaveHardwareSettings(gpuPalette,native.GpuSpeed).Where(p=>p[0]!=0x88).ToArray();
                Check(banks.Length==16 && banks.All(p=>(p[2]!=0 || p[3]!=0 || p[4]!=0) && (p[5]!=0 || p[6]!=0 || p[7]!=0)),"A transmitted GPU colour bank has an uninitialized black slot.");
            }
        }
        string originalName=defaults[0].Name,originalColor=defaults[0].Color1;
        defaults[0].Name="Edited picker copy";defaults[0].Color1="#000000";
        Check(BootAnimationLibrary.BuiltIns[0].Name==originalName && BootAnimationLibrary.BuiltIns[0].Color1==originalColor,"Editing a selected preset changed the built-in library.");
        var selection=new Preferences{BootAnimation=BootAnimationLibrary.BuiltIns.Single(preset=>preset.Name=="Crimson fast fade").Clone()};
        var fastRequest=BootAnimationLibrary.CreateRequest(selection);
        Check(fastRequest.MotionKind=="Fade" && fastRequest.MotionSpeed=="Fast","Selecting a fast fade lost its stored style or speed.");
        selection.BootAnimation.MotionSpeed="Slow";selection.BootAnimation.Style="Steady";
        var originalFast=BootAnimationLibrary.BuiltIns.Single(preset=>preset.Name=="Crimson fast fade");
        Check(originalFast.MotionSpeed=="Fast" && originalFast.Style=="Fade","Editing selected style or speed changed the built-in preset.");
        selection.BootAnimation=BootAnimationLibrary.BuiltIns.Single(preset=>preset.Name=="Warm white steady").Clone();
        var steadyRequest=BootAnimationLibrary.CreateRequest(selection);
        Check(steadyRequest.MotionKind=="Steady" && steadyRequest.MotionSpeed=="Balanced","Selecting a steady preset retained the previous fade style or speed.");

        Reject(()=>new BootAnimationPreset{Name=" "}.Validate());
        Reject(()=>new BootAnimationPreset{Name="bad\nname"}.Validate());
        Reject(()=>new BootAnimationPreset{Style="Wave"}.Validate());
        Reject(()=>new BootAnimationPreset{MotionSpeed="Unlimited"}.Validate());
        Reject(()=>new BootAnimationPreset{Color1="red"}.Validate());
        Reject(()=>new BootAnimationPreset{Color2="#000000"}.Validate());
        Reject(()=>new BootAnimationPreset{Style="Steady",Color1="#000000"}.Validate());
        Reject(()=>new BootAnimationPreset{Brightness=double.NaN}.Validate());
        Reject(()=>new BootAnimationPreset{Brightness=double.PositiveInfinity}.Validate());
        Reject(()=>new BootAnimationPreset{Brightness=.09}.Validate());
        Reject(()=>new BootAnimationPreset{Brightness=1.01}.Validate());
        Reject(()=>new BootAnimationPreset{Color1="#070000"}.Validate());
        Reject(()=>new BootAnimationPreset{Color2="#400000",Brightness=.1}.Validate());
        var threshold=new BootAnimationPreset{Color1="#500000",Color2="#005000",Brightness=.1};
        Check(threshold.EffectivePalette().SequenceEqual(new[]{"#080000","#000800"}),"The lit-colour threshold is inconsistent with transmitted values.");

        var preferences=new Preferences();var selected=new BootAnimationPreset{Name="  My boot  ",Brightness=.5};
        BootAnimationLibrary.SavePreset(preferences,selected);
        selected.Color1="#00FF00";
        Check(preferences.BootAnimationPresets.Count==1 && preferences.BootAnimationPresets[0].Name=="My boot" && preferences.BootAnimationPresets[0].Color1=="#FF0D00","Saving shared the editable preset or retained surrounding spaces.");
        BootAnimationLibrary.SavePreset(preferences,new(){Name="MY BOOT",Color1="#00FF00"});
        Check(preferences.BootAnimationPresets.Count==1 && preferences.BootAnimationPresets[0].Color1=="#00FF00","Case-insensitive preset replacement created a duplicate.");
        for(int i=1;i<32;i++)BootAnimationLibrary.SavePreset(preferences,new(){Name="Custom "+i});
        Reject(()=>BootAnimationLibrary.SavePreset(preferences,new(){Name="Overflow"}));
        BootAnimationLibrary.SavePreset(preferences,new(){Name="my boot",Color1="#0088FF"});
        Check(preferences.BootAnimationPresets.Count==32,"Updating a full library changed its capacity.");
        Reject(()=>BootAnimationLibrary.SavePreset(preferences,new(){Name="Invalid",Color2="#000000"}));
        Check(preferences.BootAnimationPresets.Count==32,"Invalid preset modified the custom library.");

        preferences.Studio=new(){Effect="Wave",Color="#0088FF",SecondaryColor="#00FFFF",Brightness=.7,Speed=.6};
        Effects.SetPalette(preferences.Studio,new[]{"#0088FF","#00FFFF"});
        string windowsBefore=JsonSerializer.Serialize(preferences.Studio);
        preferences.BootAnimation=new(){Name="Boot only",Color1="#FF0D00",Color2="#FF6401",Brightness=.5};
        var request=BootAnimationLibrary.CreateRequest(preferences);
        Check(request.MotionKind=="Fade" && request.ProfileName=="Boot only" && request.Palette.SequenceEqual(new[]{"#7F0600","#7F3200"}),"The selected boot palette was not scaled exactly once.");
        Check(JsonSerializer.Serialize(request.WindowsState)==windowsBefore && JsonSerializer.Serialize(preferences.Studio)==windowsBefore,"Choosing boot colours changed the current Windows scene.");
        preferences.Studio.Color="#FFFFFF";preferences.Studio.EffectPalettes["Wave"][0]="#FFFFFF";
        Check(JsonSerializer.Serialize(request.WindowsState)==windowsBefore,"The boot request shares mutable Windows scene state.");
        preferences.BootAnimation.Style="Steady";
        Check(BootAnimationLibrary.CreateRequest(preferences).MotionKind=="Steady","Steady was silently converted into an animated mode.");
        preferences.BootAnimation=BootAnimationLibrary.FilledWave;preferences.BootAnimation.Color1="#000000";preferences.BootAnimation.Color2="not a colour";preferences.BootAnimation.Brightness=.4;
        var waveRequest=BootAnimationLibrary.CreateRequest(preferences);
        Check(waveRequest.MotionKind=="RainbowWave" && waveRequest.BootBrightness==.4 && waveRequest.Palette.SequenceEqual(new[]{"#FFFFFF","#FFFFFF"}),"Fixed-rainbow Wave depended on inactive custom colour fields.");
        Check(JsonSerializer.Serialize(waveRequest.WindowsState)==JsonSerializer.Serialize(preferences.Studio),"Selecting filled Wave changed the Windows scene.");
        var waveCopy=BootAnimationLibrary.FilledWave;waveCopy.Style="Steady";waveCopy.Brightness=.1;
        Check(BootAnimationLibrary.FilledWave.Style=="RainbowWave" && BootAnimationLibrary.FilledWave.Brightness==1,"Editing Wave changed its built-in default.");
        Reject(()=>new BootAnimationPreset{Style="RainbowWave",Brightness=0}.Validate());
        return Task.CompletedTask;
    }
}
