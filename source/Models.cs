using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace PrismRGB;

public sealed class LightItem
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Shape { get; set; } = "strip";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = .3;
    public double Height { get; set; } = .1;
    public bool Enabled { get; set; } = true;
}

public sealed class StudioState
{
    public string Effect { get; set; } = "Aurora";
    public string Color { get; set; } = "#68E0C2";
    public string SecondaryColor { get; set; } = "#A38BFA";
    public Dictionary<string,List<string>> EffectPalettes { get; set; } = new();
    public double Brightness { get; set; } = .7;
    public double Speed { get; set; } = .4;
    public List<LightItem> Layout { get; set; } = Defaults();
    public static List<LightItem> Defaults() => new()
    {
        new() { Key="monitor",Name="Monitor ambience",Shape="monitor",X=.10,Y=.13,Width=.49,Height=.35 },
        new() { Key="keyboard",Name="Keyboard",Shape="keyboard",X=.08,Y=.65,Width=.48,Height=.15 },
        new() { Key="mouse",Name="Mouse",Shape="mouse",X=.60,Y=.67,Width=.07,Height=.14 },
        new() { Key="tower",Name="PC lighting",Shape="tower",X=.73,Y=.20,Width=.18,Height=.53 },
        new() { Key="strip",Name="Desk strip",Shape="strip",X=.12,Y=.89,Width=.60,Height=.018 }
    };
    public StudioState Clone() => JsonSerializer.Deserialize<StudioState>(JsonSerializer.Serialize(this))!;
    public void Normalize()
    {
        if (!Effects.Names.Contains(Effect)) Effect="Aurora";
        Brightness=double.IsFinite(Brightness)?Math.Clamp(Brightness,0,1):.7;
        Speed=double.IsFinite(Speed)?Math.Clamp(Speed,0,1):.4;
        if (!Effects.ValidColor(Color)) Color="#68E0C2";
        if (!Effects.ValidColor(SecondaryColor)) SecondaryColor="#A38BFA";
        EffectPalettes??=new();
        foreach(var name in new List<string>(EffectPalettes.Keys))
        {
            if(!Effects.Names.Contains(name) || EffectPalettes[name] is null){EffectPalettes.Remove(name);continue;}
            var colors=EffectPalettes[name].FindAll(Effects.ValidColor);
            if(colors.Count>8)colors=colors.GetRange(0,8);
            if(colors.Count==0)EffectPalettes.Remove(name);else EffectPalettes[name]=colors;
        }
        if (Layout is null || Layout.Count==0 || Layout.Count>100) Layout=Defaults();
        Layout.RemoveAll(item=>item is null);
        if(Layout.Count==0)Layout=Defaults();
        foreach (var item in Layout)
        {
            item.Name=string.IsNullOrWhiteSpace(item.Name)?"Light":item.Name[..Math.Min(80,item.Name.Length)];
            item.Key ??="light";
            if(item.Shape is not ("monitor" or "keyboard" or "mouse" or "tower" or "strip"))item.Shape="strip";
            item.Width=double.IsFinite(item.Width)?Math.Clamp(item.Width,.01,.95):.3;
            item.Height=double.IsFinite(item.Height)?Math.Clamp(item.Height,.01,.95):.1;
            item.X=double.IsFinite(item.X)?Math.Clamp(item.X,0,1-item.Width):.1;
            item.Y=double.IsFinite(item.Y)?Math.Clamp(item.Y,0,1-item.Height):.1;
        }
    }
}

public sealed class SavedProfile
{
    public string Name { get; set; } = "My setup";
    public StudioState State { get; set; } = new();
}

public sealed class DevicePlacement
{
    public string Identity { get; set; } = "";
    public double X { get; set; } = .2;
    public double Y { get; set; } = .65;
    public double Span { get; set; } = .5;
    public bool Enabled { get; set; } = true;
    public int LedCount { get; set; }
    public bool Reverse { get; set; }
    public Dictionary<int,string> LedColors { get; set; } = new();
}

public sealed class Preferences
{
    public StudioState Studio { get; set; } = new();
    public List<SavedProfile> Profiles { get; set; } = new();
    public List<DevicePlacement> Placements { get; set; } = new();
    public bool MinimizeToTray { get; set; } = false;
    public bool StartInTray { get; set; } = true;
    public bool RestoreLightingOnStartup { get; set; }
    public bool KeepColorsOnExit { get; set; }
    public string? HardwareBootColor { get; set; }
}

public static class Storage
{
    public static string DirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PrismRGB");
    static readonly JsonSerializerOptions Options = new() { WriteIndented=true, PropertyNameCaseInsensitive=true };
    public static Preferences Load()
    {
        try
        {
            var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath,"settings.json")),Options) ?? new();
            p.Studio ??=new(); p.Studio.Normalize(); p.Profiles ??=new(); p.Placements ??=new();
            if(!Effects.ValidColor(p.HardwareBootColor))p.HardwareBootColor=null;
            p.Profiles.RemoveAll(item=>item is null);p.Placements.RemoveAll(item=>item is null);
            foreach (var s in p.Profiles) { s.Name=string.IsNullOrWhiteSpace(s.Name)?"Saved profile":s.Name; s.State ??=new(); s.State.Normalize(); }
            foreach(var d in p.Placements)
            {
                d.X=double.IsFinite(d.X)?Math.Clamp(d.X,0,1):.2;d.Y=double.IsFinite(d.Y)?Math.Clamp(d.Y,0,1):.65;d.Span=double.IsFinite(d.Span)?Math.Clamp(d.Span,0,1):.5;
                d.LedCount=Math.Clamp(d.LedCount,0,120);d.LedColors??=new();
                foreach(var key in new List<int>(d.LedColors.Keys))if(key<0 || key>=120 || !Effects.ValidColor(d.LedColors[key]))d.LedColors.Remove(key);
            }
            return p;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static void Save(Preferences p)
    {
        Directory.CreateDirectory(DirectoryPath);
        string path=Path.Combine(DirectoryPath,"settings.json"),tmp=path+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(p,Options));
        File.Move(tmp,path,true);
    }
    public static void Export(SavedProfile profile,string path) => File.WriteAllText(path,JsonSerializer.Serialize(profile,Options));
    public static SavedProfile Import(string path)
    {
        if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Profile is too large.");
        var p=JsonSerializer.Deserialize<SavedProfile>(File.ReadAllText(path),Options) ?? throw new InvalidDataException("Invalid profile.");
        if(p.State is null || string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException("The profile needs a name and lighting settings.");
        p.State.Normalize(); return p;
    }
}

public static class OutputColors
{
    public static Color[] Sample(StudioState state,OutputDevice device,double seconds,Color? screen=null)
    {
        var colors=new Color[device.LedCount];var placement=device.Placement;
        for(int i=0;i<colors.Length;i++)
        {
            int position=placement.Reverse?colors.Length-1-i:i;
            colors[i]=placement.LedColors.TryGetValue(i,out var hex)
                ?Effects.Scale(Effects.Parse(hex),state.Brightness)
                :Effects.Sample(state,Math.Clamp(placement.X+placement.Span*position/Math.Max(1.0,colors.Length-1),0,1),placement.Y,seconds,screen);
        }
        return colors;
    }
}

public static class Effects
{
    public static readonly List<string> Names = new() { "Aurora", "Rainbow", "Solid", "Breathing", "Wave", "Ember", "Screen ambience" };
    public static bool ValidColor(string? text) => text is not null && System.Text.RegularExpressions.Regex.IsMatch(text,"^#[0-9a-fA-F]{6}$");
    // Hex parsing avoids WPF's type converter and its allocations for every LED sample.
    public static Color Parse(string text) => Color.FromRgb(Convert.ToByte(text.Substring(1,2),16),Convert.ToByte(text.Substring(3,2),16),Convert.ToByte(text.Substring(5,2),16));
    [ThreadStatic] static string? lastPrimary,lastSecondary;
    [ThreadStatic] static Color primaryColor,secondaryColor;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StudioState,PaletteCache> paletteCache=new();
    sealed class PaletteCache { public List<string>? Source;public Color[] Colors=Array.Empty<Color>(); }
    public static List<string> Palette(StudioState s)
    {
        if(s.EffectPalettes.TryGetValue(s.Effect,out var colors))return new(colors);
        return s.Effect switch
        {
            "Rainbow"=>new(){"#FF0000","#FFFF00","#00FF00","#00FFFF","#0000FF","#FF00FF"},
            "Ember"=>new(){"#FF300C","#FFBE2D"},
            "Solid" or "Breathing"=>new(){s.Color},
            _=>new(){s.Color,s.SecondaryColor}
        };
    }
    public static void SetPalette(StudioState s,IEnumerable<string> colors)
    {
        var copy=new List<string>(colors);
        if(copy.Count is <1 or >8 || copy.Exists(c=>!ValidColor(c)))throw new ArgumentException("Choose between 1 and 8 valid colors.");
        // Replace lists rather than mutating them so the hot rendering path can reuse parsed RGB stops.
        s.EffectPalettes[s.Effect]=copy;
    }
    public static Color PaletteAt(IReadOnlyList<Color> colors,double position,bool loop=false)
    {
        if(colors.Count==1)return colors[0];
        double p=loop?(position%1+1)%1:Math.Clamp(position,0,1);
        double slot=p*(loop?colors.Count:colors.Count-1);int index=Math.Min((int)slot,colors.Count-1);
        return Mix(colors[index],colors[loop?(index+1)%colors.Count:Math.Min(index+1,colors.Count-1)],slot-index);
    }
    public static Color Sample(StudioState s,double x,double y,double seconds,Color? screen=null)
    {
        if(lastPrimary!=s.Color){primaryColor=Parse(s.Color);lastPrimary=s.Color;}
        if(lastSecondary!=s.SecondaryColor){secondaryColor=Parse(s.SecondaryColor);lastSecondary=s.SecondaryColor;}
        var a=primaryColor; var b=secondaryColor; double t=seconds*(.05+s.Speed*1.5); Color c;
        if(s.EffectPalettes.TryGetValue(s.Effect,out var custom))
        {
            var cache=paletteCache.GetOrCreateValue(s);
            if(!ReferenceEquals(cache.Source,custom)){cache.Colors=custom.ConvertAll(Parse).ToArray();cache.Source=custom;}
            var stops=cache.Colors;
            c=s.Effect switch
            {
                "Rainbow"=>PaletteAt(stops,x*.8+y*.2+t*.18,true),
                "Solid"=>stops[0],
                "Breathing"=>Scale(PaletteAt(stops,t*.12,true),.12+.88*(.5+.5*Math.Sin(t*Math.PI*2))),
                "Wave"=>PaletteAt(stops,.5+.5*Math.Sin((x*2+y-t)*Math.PI*2)),
                "Ember"=>PaletteAt(stops,.5+.5*Math.Sin(x*17+y*11+t*3)*Math.Sin(x*9-t*4)),
                "Screen ambience"=>ColorizeScreen(stops,screen??Colors.Black),
                _=>PaletteAt(stops,Math.Clamp(.5+.35*Math.Sin(x*5+t)+.15*Math.Cos(y*7-t*.7),0,1))
            };
            return Scale(c,s.Brightness);
        }
        switch(s.Effect)
        {
            case "Rainbow": c=Hsv((x*.8+y*.2+t*.18)%1,1,1); break;
            case "Solid": c=a; break;
            case "Breathing": c=Scale(a,.12+.88*(.5+.5*Math.Sin(t*Math.PI*2))); break;
            case "Wave": c=Mix(a,b,.5+.5*Math.Sin((x*2+y-t)*Math.PI*2)); break;
            case "Ember": c=Mix(Color.FromRgb(255,48,12),Color.FromRgb(255,190,45),.5+.5*Math.Sin(x*17+y*11+t*3)*Math.Sin(x*9-t*4)); break;
            case "Screen ambience": c=screen ?? Colors.Black; break;
            default: c=Mix(a,b,Math.Clamp(.5+.35*Math.Sin(x*5+t)+.15*Math.Cos(y*7-t*.7),0,1)); break;
        }
        return Scale(c,s.Brightness);
    }
    static Color ColorizeScreen(Color[] stops,Color screen)
    {
        double level=(.2126*screen.R+.7152*screen.G+.0722*screen.B)/255;
        return Scale(PaletteAt(stops,level),Math.Max(screen.R,Math.Max(screen.G,screen.B))/255.0);
    }
    public static Color Scale(Color c,double f) => Color.FromRgb((byte)Math.Clamp(c.R*f,0,255),(byte)Math.Clamp(c.G*f,0,255),(byte)Math.Clamp(c.B*f,0,255));
    public static Color Mix(Color a,Color b,double f) => Color.FromRgb((byte)(a.R+(b.R-a.R)*f),(byte)(a.G+(b.G-a.G)*f),(byte)(a.B+(b.B-a.B)*f));
    public static Color Hsv(double h,double s,double v)
    {
        h=(h%1+1)%1; double q=h*6; int i=(int)q; double f=q-i;
        double p=v*(1-s),a=v*(1-s*f),b=v*(1-s*(1-f));
        var (r,g,bl)=(i%6) switch { 0=>(v,b,p),1=>(a,v,p),2=>(p,v,b),3=>(p,a,v),4=>(b,p,v),_=>(v,p,a) };
        return Color.FromRgb((byte)(r*255),(byte)(g*255),(byte)(bl*255));
    }
}
