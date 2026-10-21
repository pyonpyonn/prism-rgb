using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public sealed class HardwareWaveRequest
{
    public string ProfileName {get;init;}="";
    public string Color {get;init;}="#FF0D00";
    public string[] Palette {get;init;}=Array.Empty<string>();
    public double Speed {get;init;}
    public string MotionSpeed {get;init;}="Balanced";
    public int MotherboardSpeed=>MotionSpeed switch{"Slow"=>0,"Balanced"=>1,"Fast"=>2,_=>throw new ArgumentException("Unsupported boot motion speed.")};
    public int GpuSpeed=>MotionSpeed switch{"Slow"=>1,"Balanced"=>3,"Fast"=>5,_=>throw new ArgumentException("Unsupported boot motion speed.")};
    internal StudioState? WindowsState {get;init;}
    public void Validate()
    {
        if(!Effects.ValidColor(Color) || !double.IsFinite(Speed) || Speed<0 || Speed>1)
            throw new ArgumentException("Choose a valid wave colour and speed.");
        if(Palette is null || Palette.Length>8 || Palette.Any(c=>!Effects.ValidColor(c)))throw new ArgumentException("Choose up to eight valid boot palette colours.");
        if(Palette.Length>0 && !Palette[0].Equals(Color,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The first boot colour must match the selected palette.");
        if(MotionSpeed is not ("Slow" or "Balanced" or "Fast"))throw new ArgumentException("Choose a supported boot motion speed.");
    }
    public static HardwareWaveRequest FromProfile(Preferences preferences,string name,string? motionSpeed=null)
    {
        var profile=preferences.Profiles.SingleOrDefault(p=>p.Name==name)
            ??throw new ArgumentException("The selected wave profile was not found.");
        var state=profile.State.Clone();state.Normalize();
        if(state.Effect!="Wave")throw new ArgumentException("Choose a saved profile that uses the Wave effect.");
        var palette=Effects.Palette(state).Select(c=>ColorValues.Hex(Effects.Scale(Effects.Parse(c),state.Brightness))).ToArray();
        return new(){ProfileName=name,Color=palette[0],Palette=palette,Speed=state.Speed,WindowsState=state,MotionSpeed=motionSpeed??preferences.HardwareBootMotionSpeed};
    }
    public static void ApplySaved(Preferences preferences,HardwareWaveRequest request)
    {
        request.Validate();
        preferences.Studio=(request.WindowsState??preferences.Profiles.Single(p=>p.Name==request.ProfileName).State).Clone();preferences.Studio.Normalize();
        preferences.HardwareBootEffect="Wave";preferences.HardwareBootWaveProfile=request.ProfileName;
        preferences.HardwareBootWaveSaved=true;
        preferences.HardwareBootMotionSpeed=request.MotionSpeed;
        preferences.RestoreLightingOnStartup=true;
    }
}

public static partial class MsiProtocol
{
    public static byte[] WaveConfiguration(byte[] original,Color color,int speed,Color? secondary=null)
    {
        if(speed is <0 or >2)throw new InvalidDataException("MSI wave speed must be between 0 and 2.");
        var packet=StaticConfiguration(original,color);
        var second=secondary??color;
        foreach(int offset in ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}))
        {packet[offset+5]=second.R;packet[offset+6]=second.G;packet[offset+7]=second.B;}
        foreach(int offset in ZoneOffsets.Append(74)){packet[offset]=23;packet[offset+4]=(byte)(0x28|speed);}
        // This exact board has JRGB but no JPIPE outputs. Its firmware clears the
        // unsupported JPIPE sync bits; keep only onboard sync and custom colour.
        // ARGB headers retain their own native Color Wave mode, without direct streaming.
        packet[78]|=0x80;packet[82]=0x81;
        return packet;
    }
    public static byte[] WaveSavePacket(byte[] configuration)
    {
        if(configuration.Length!=185)throw new InvalidDataException("Unexpected MSI wave configuration.");
        var packet=(byte[])configuration.Clone();packet[184]=1;ValidateWaveSave(packet);return packet;
    }
    public static void ValidateWaveSave(byte[] packet)
    {
        if(packet.Length!=185 || packet[0]!=0x52 || packet[184]!=1)
            throw new InvalidDataException("Only a native MSI wave configuration can be saved.");
        var expected=WaveConfiguration(packet,Color.FromRgb(packet[2],packet[3],packet[4]),packet[5]&3,Color.FromRgb(packet[6],packet[7],packet[8]));expected[184]=1;
        if(!packet.AsSpan().SequenceEqual(expected))throw new InvalidDataException("Unsupported MSI wave modes, routing or colours.");
    }
}

public sealed partial class MsiBackend
{
    public Task SaveWaveToHardwareAsync(Color color,int speed,Color? secondary=null)=>ApplyWaveAsync(color,speed,true,secondary);
    public Task PreviewWaveAsync(Color color,int speed,Color? secondary=null)=>ApplyWaveAsync(color,speed,false,secondary);
    Task ApplyWaveAsync(Color color,int speed,bool persist,Color? secondary)=>Locked(()=>
    {
        if(faulted || transport is null || original is null)throw new IOException("Controller is unavailable. Disconnect and reconnect.");
        CheckConflict(true);
        if(persist && transport is not IMsiHardwareSaveTransport)throw new NotSupportedException("This MSI transport cannot save a boot wave.");
        var current=MsiProtocol.Frame(transport.Read(),new Dictionary<uint,Color[]>());
        var selected=MsiProtocol.WaveConfiguration(current,color,speed,secondary);
        wrote=true;lastPacket=null;lastDirect.Clear();activeCounts.Clear();
        try
        {
            transport.Write(current);Writes++;transport.Write(selected);Writes++;Thread.Sleep(250);
            var observed=transport.Read();
            if(!MsiProtocol.MatchesStaticConfiguration(selected,observed))
                throw new IOException("MSI did not confirm the native wave configuration; no hardware save was sent. "+MsiProtocol.StaticMismatchDetails(selected,observed));
            if(persist)
            {
                try{((IMsiHardwareSaveTransport)transport).SaveWave(MsiProtocol.WaveSavePacket(selected));Writes++;}
                catch(Exception ex){throw new IOException("MSI wave-save delivery is uncertain. It was attempted once and will not be retried automatically.",ex);}
                Thread.Sleep(250);original=selected;wrote=false;
            }
        }
        catch{faulted=true;throw;}
    });
}

public static partial class GigabyteProtocol
{
    public static List<byte[]> WaveHardwareSettings(IReadOnlyList<Color> palette,int speed)
    {
        if(speed is <0 or >5)throw new InvalidDataException("GPU wave speed must be between 0 and 5.");
        if(palette is null || palette.Count is <1 or >8)throw new InvalidDataException("GPU native colour flow accepts one to eight colours.");
        var packets=new List<byte[]>();
        // Native Tricolor supports custom colours. Native Wave (7) ignores
        // them, while Color Shift (6) cycles whole zones rather than a wave.
        for(int zone=0;zone<4;zone++)
        {
            packets.Add(new byte[]{0x88,11,(byte)speed,99,8,(byte)(zone+1),0,0});
            for(int part=0;part<4;part++)
            {
                var packet=new byte[8];packet[0]=(byte)(0xB0+zone*4+part);packet[1]=11;
                for(int slot=0;slot<2;slot++)
                {
                    int index=part*2+slot;if(index>=palette.Count)continue;
                    var color=palette[index];int offset=2+slot*3;packet[offset]=color.R;packet[offset+1]=color.G;packet[offset+2]=color.B;
                }
                packets.Add(packet);
            }
        }
        return packets;
    }
    public static void ValidateWaveHardwareSetting(byte[] packet)
    {
        if(packet.Length!=8)throw new InvalidDataException("GPU wave settings must contain eight bytes.");
        if(packet[0]==0x88)
        {
            if(packet[1]!=11 || packet[2]>5 || packet[3]!=0x63 || packet[4]!=8 || packet[5] is <1 or >4 || packet[6]!=0 || packet[7]!=0)
                throw new InvalidDataException("Unsupported GPU wave mode or routing.");
            return;
        }
        if(packet[0] is <0xB0 or >0xBF || packet[1]!=11)throw new InvalidDataException("Unsupported GPU custom-palette bank.");
    }
}

public sealed partial class GigabyteBackend
{
    public Task SaveWaveToHardwareAsync(Color[] palette,int speed)=>ApplyWaveAsync(palette,speed,true);
    public Task PreviewWaveAsync(Color[] palette,int speed)=>ApplyWaveAsync(palette,speed,false);
    Task ApplyWaveAsync(Color[] palette,int speed,bool persist)=>Locked(async token=>
    {
        if(conflict())throw new IOException("Close competing RGB software before applying the GPU boot wave.");
        if(transport is not IGpuHardwareSaveTransport hardware)throw new NotSupportedException("This GPU transport cannot apply a native boot wave.");
        if(!GigabyteProtocol.Identity(hardware.QueryIdentity()))throw new InvalidDataException("GPU identity changed; no wave settings were sent.");
        previous=null;packetCache.Clear();modeReady=false;wrote=true;
        foreach(var packet in GigabyteProtocol.WaveHardwareSettings(palette,speed))await SendVolatileAsync(packet,hardware.WriteWaveSetting,token);
        if(persist)
        {
            try{hardware.CommitStaticConfiguration();Writes++;}
            catch(Exception ex)
            {
                if(ex is NvidiaLightingException)TransferFailures++;
                throw new IOException("GPU wave-save delivery is uncertain. It was attempted once and will not be retried automatically.",ex);
            }
        }
    });
}

public sealed class HardwareWavePreviewResult
{
    public string Effect {get;init;}="Native wave preview";
    public string ProfileName {get;init;}="";
    public string Color {get;init;}="";
    public string[] Palette {get;init;}=Array.Empty<string>();
    public double Speed {get;init;}
    public string MotionSpeed {get;init;}="Balanced";
    public bool MotherboardApplied {get;init;}
    public bool GpuApplied {get;init;}
    public string MotherboardError {get;init;}="";
    public string GpuError {get;init;}="";
    public bool PersistentSaveRequested=>false;
    public bool Success=>MotherboardApplied && GpuApplied;
}

public static class HardwareBootWave
{
    internal static Color[] BuildGpuPalette(IReadOnlyList<Color> palette)
    {
        var lit=palette.Where(c=>c.R!=0 || c.G!=0 || c.B!=0).Take(3).ToArray();
        if(lit.Length==0)lit=new[]{Colors.Black};
        var main=lit.Length==2?new[]{lit[0],lit[1],Effects.Mix(lit[0],lit[1],.5)}:lit;
        return Enumerable.Range(0,8).Select(i=>main[i%main.Length]).ToArray();
    }
    static HardwareBootSaveOptions Options(HardwareWaveRequest request,bool persist,string? directory=null)
    {
        request.Validate();
        var palette=(request.Palette.Length==0?new[]{request.Color}:request.Palette).Select(Effects.Parse).ToArray();
        // A whole-zone black stop blanks the GPU during a native cycle. Keep the
        // saved Windows scene intact and omit black only for boot colour motion.
        // Tricolor consumes three principal stops; initialize all transmitted
        // slots so no unspecified padding colour can cause a black interval.
        var gpuPalette=BuildGpuPalette(palette);
        return new()
        {
            LeaseDirectory=directory,
            SaveMotherboard=async color=>
            {
                await using var backend=new MsiBackend();await backend.ConnectAsync();
                int speed=request.MotherboardSpeed;
                var secondary=palette.Length>1?palette[1]:color;
                if(persist)await backend.SaveWaveToHardwareAsync(color,speed,secondary);else await backend.PreviewWaveAsync(color,speed,secondary);
            },
            SaveGpu=async color=>
            {
                await using var backend=new GigabyteBackend();await backend.ConnectAsync();
                int speed=request.GpuSpeed;
                if(persist)await backend.SaveWaveToHardwareAsync(gpuPalette,speed);else await backend.PreviewWaveAsync(gpuPalette,speed);
            }
        };
    }
    public static async Task<HardwareBootSaveResult> SaveAsync(HardwareWaveRequest request)
    {
        var result=await HardwareBootColor.SaveAsync(Effects.Parse(request.Color),Options(request,true));
        result.Effect="Wave";result.ProfileName=request.ProfileName;return result;
    }
    public static async Task<HardwareWavePreviewResult> PreviewAsync(HardwareWaveRequest request)
    {
        var result=await HardwareBootColor.SaveAsync(Effects.Parse(request.Color),Options(request,false));
        return new(){ProfileName=request.ProfileName,Color=request.Color,Palette=request.Palette,Speed=request.Speed,MotionSpeed=request.MotionSpeed,MotherboardApplied=result.Motherboard.Saved,GpuApplied=result.Gpu.Saved,MotherboardError=result.Motherboard.Saved?"":result.Motherboard.Message,GpuError=result.Gpu.Saved?"":result.Gpu.Message};
    }
}
