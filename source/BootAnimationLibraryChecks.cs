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
        Check(defaults.Count>=7 && defaults.Select(preset=>preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==defaults.Count,"Built-in boot names are missing or duplicated.");
        foreach(var preset in defaults)
        {
            preset.Validate();Check(preset.Style is "Fade" or "Steady","An unverified gapped mode entered the boot library.");
            Check(preset.EffectivePalette().Length==2 && preset.EffectivePalette().All(color=>color!="#000000"),"A built-in palette has a black stop.");
        }
        string originalName=defaults[0].Name,originalColor=defaults[0].Color1;
        defaults[0].Name="Edited picker copy";defaults[0].Color1="#000000";
        Check(BootAnimationLibrary.BuiltIns[0].Name==originalName && BootAnimationLibrary.BuiltIns[0].Color1==originalColor,"Editing a selected preset changed the built-in library.");

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
        return Task.CompletedTask;
    }
}
