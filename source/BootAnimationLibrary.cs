using System;
using System.Collections.Generic;
using System.Linq;

namespace PrismRGB;

public sealed class BootAnimationPreset
{
    public string Name {get;set;}="Lava fade";
    public string Style {get;set;}="Fade";
    public string Color1 {get;set;}="#FF0D00";
    public string Color2 {get;set;}="#FF6401";
    public string MotionSpeed {get;set;}="Balanced";
    public double Brightness {get;set;}=1;

    public BootAnimationPreset Clone()=>new()
    {
        Name=Name,Style=Style,Color1=Color1,Color2=Color2,
        MotionSpeed=MotionSpeed,Brightness=Brightness
    };

    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Name) || Name.Trim().Length>64 || Name.Any(char.IsControl))
            throw new ArgumentException("Give the boot preset a name of up to 64 characters.");
        if(Style is not ("Fade" or "Steady"))
            throw new ArgumentException("Choose Fade or Steady for lighting without black gaps.");
        if(MotionSpeed is not ("Slow" or "Balanced" or "Fast"))
            throw new ArgumentException("Choose a supported boot speed.");
        if(!Effects.ValidColor(Color1) || !Effects.ValidColor(Color2))
            throw new ArgumentException("Choose two valid boot colours.");
        if(!double.IsFinite(Brightness) || Brightness<.1 || Brightness>1)
            throw new ArgumentException("Boot brightness must be between 10% and 100%.");
        foreach(var color in ScaledColors())
            if(Math.Max(color.R,Math.Max(color.G,color.B))<8)
                throw new ArgumentException("Choose brighter boot colours so both remain lit at this brightness.");
    }

    System.Windows.Media.Color[] ScaledColors()=>new[]
    {
        Effects.Scale(Effects.Parse(Color1),Brightness),
        Effects.Scale(Effects.Parse(Color2),Brightness)
    };

    public string[] EffectivePalette()
    {
        Validate();return ScaledColors().Select(ColorValues.Hex).ToArray();
    }
}

public static class BootAnimationLibrary
{
    static readonly BootAnimationPreset[] builtIns=
    {
        new(){Name="Lava fade",Color1="#FF0D00",Color2="#FF6401"},
        new(){Name="Ocean fade",Color1="#0099FF",Color2="#00FFCC"},
        new(){Name="Cyberpunk fade",Color1="#FF00D4",Color2="#00D5FF"},
        new(){Name="Forest fade",Color1="#16E020",Color2="#A6FF14"},
        new(){Name="Sunset fade",Color1="#FF3500",Color2="#FF0080"},
        new(){Name="Ice fade",Color1="#54BFFF",Color2="#D8F4FF"},
        new(){Name="Lava steady",Style="Steady",Color1="#FF3000",Color2="#FF6401"},
        new(){Name="Ember slow fade",Color1="#FF1800",Color2="#FF9800",MotionSpeed="Slow"},
        new(){Name="Aurora fade",Color1="#00FF99",Color2="#8866FF"},
        new(){Name="Royal slow fade",Color1="#8A20FF",Color2="#2048FF",MotionSpeed="Slow"},
        new(){Name="Rose fade",Color1="#FF3080",Color2="#FF9670"},
        new(){Name="Gold fade",Color1="#FFAA00",Color2="#FFF04A"},
        new(){Name="Mint fade",Color1="#00FFC0",Color2="#66FF80"},
        new(){Name="Crimson fast fade",Color1="#FF1428",Color2="#FF4088",MotionSpeed="Fast"},
        new(){Name="Candy fast fade",Color1="#FF44CC",Color2="#44DDFF",MotionSpeed="Fast"},
        new(){Name="Deep sea slow fade",Color1="#164CFF",Color2="#00C898",MotionSpeed="Slow"},
        new(){Name="Sunrise fade",Color1="#FF5860",Color2="#FFC040"},
        new(){Name="Warm white steady",Style="Steady",Color1="#FFE4BC",Color2="#FFF4DC"},
        new(){Name="Ice blue steady",Style="Steady",Color1="#6ACFFF",Color2="#D8F4FF"},
        new(){Name="Emerald steady",Style="Steady",Color1="#18E880",Color2="#70FFBC"},
        new(){Name="Violet steady",Style="Steady",Color1="#AA55FF",Color2="#D099FF"}
    };

    // Pickers receive independent values, so editing or saving one cannot change
    // the built-in palette returned on the next visit to the library.
    public static IReadOnlyList<BootAnimationPreset> BuiltIns=>builtIns.Select(preset=>preset.Clone()).ToArray();

    public static HardwareWaveRequest CreateRequest(Preferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var preset=preferences.BootAnimation??throw new ArgumentException("Choose a boot animation preset.");
        preset.Validate();var palette=preset.EffectivePalette();
        var request=new HardwareWaveRequest
        {
            ProfileName=preset.Name.Trim(),Color=palette[0],Palette=palette,
            MotionKind=preset.Style,MotionSpeed=preset.MotionSpeed,Speed=.15,
            WindowsState=preferences.Studio.Clone()
        };
        request.Validate();return request;
    }

    public static void SavePreset(Preferences preferences,BootAnimationPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preferences);ArgumentNullException.ThrowIfNull(preset);
        preset.Validate();var saved=preset.Clone();saved.Name=saved.Name.Trim();
        preferences.BootAnimationPresets??=new List<BootAnimationPreset>();
        var list=preferences.BootAnimationPresets;
        int index=list.FindIndex(item=>item is not null && string.Equals(item.Name,saved.Name,StringComparison.OrdinalIgnoreCase));
        if(index>=0)list[index]=saved;
        else
        {
            if(list.Count>=32)throw new ArgumentException("You can save up to 32 custom boot presets. Replace an existing name to update it.");
            list.Add(saved);
        }
    }
}
