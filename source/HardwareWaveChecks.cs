using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

internal static class HardwareWaveChecks
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject(Action action){bool failed=false;try{action();}catch(ArgumentException){failed=true;}catch(InvalidDataException){failed=true;}Check(failed,"Invalid native wave input was accepted.");}
    public static async Task VerifyAsync()
    {
        var studio=new StudioState{Effect="Wave",Speed=.1,Brightness=.5};
        Effects.SetPalette(studio,new[]{"#FF0D00","#FF6401","#8B3B00","#000000"});
        var preferences=new Preferences{Profiles=new(){new(){Name="LAVA WAVE",State=studio}}};
        string profileBefore=JsonSerializer.Serialize(preferences.Profiles);
        var request=HardwareWaveRequest.FromProfile(preferences,"LAVA WAVE");
        Check(request.Color=="#7F0600" && request.Speed==.1,"Boot wave did not use the selected profile brightness, first colour and speed.");
        Check(request.Palette.Length==4 && request.Palette[1]=="#7F3200" && request.Palette[2]=="#451D00" && request.Palette[3]=="#000000","Hardware request dropped non-primary lava palette stops.");
        Check(request.MotherboardSpeed==1 && request.GpuSpeed==3,"Balanced boot speed reverted to the lowest firmware speed.");
        HardwareWaveRequest.ApplySaved(preferences,request);
        Check(preferences.Studio.Effect=="Wave" && Effects.Palette(preferences.Studio).Count==4 && preferences.HardwareBootWaveSaved && preferences.RestoreLightingOnStartup,"The exact full Windows profile was not selected after saving.");
        preferences.Studio.EffectPalettes["Wave"][0]="#FFFFFF";
        Check(JsonSerializer.Serialize(preferences.Profiles)==profileBefore,"Applying a wave altered the saved source profile.");
        Reject(()=>HardwareWaveRequest.FromProfile(preferences,"Missing"));
        Reject(()=>new HardwareWaveRequest{Color="wrong"}.Validate());
        Reject(()=>new HardwareWaveRequest{Speed=double.NaN}.Validate());
        Reject(()=>new HardwareWaveRequest{MotionSpeed="Infinite"}.Validate());
        Reject(()=>new HardwareWaveRequest{MotionKind="RainbowWave",Color="#FF0000",Palette=new[]{"#FF0000","#FF6401"}}.Validate());
        Reject(()=>new HardwareWaveRequest{MotionKind="RainbowWave",Color="#FFFFFF",Palette=Array.Empty<string>()}.Validate());
        new HardwareWaveRequest{MotionKind="RainbowWave",Color="#FFFFFF",Palette=new[]{"#FFFFFF","#FFFFFF"},BootBrightness=.1}.Validate();
        Reject(()=>new HardwareWaveRequest{Color="#FF0000",Palette=new[]{"#0000FF"}}.Validate());

        var original=new byte[185];original[0]=0x52;original[41]=original[52]=60;
        var color=Color.FromRgb(255,13,0);
        preferences.HardwareBootMotionKind="Fade";
        var fadeRequest=HardwareWaveRequest.FromProfile(preferences,"LAVA WAVE");fadeRequest.Validate();
        HardwareWaveRequest.ApplySaved(preferences,fadeRequest);
        Check(preferences.Studio.Effect=="Color fade" && preferences.HardwareBootEffect=="Fade" && fadeRequest.Palette.SequenceEqual(new[]{"#7F0600","#7F3200"}),"Fade retained black stops or lost brightness.");
        Check(JsonSerializer.Serialize(preferences.Profiles)==profileBefore,"Selecting fade damaged the source wave profile.");
        var dimState=new StudioState{Effect="Wave",Brightness=.5};Effects.SetPalette(dimState,new[]{"#010000","#FF0D00","#FF6401"});
        var dimPrefs=new Preferences{HardwareBootMotionKind="Fade",Profiles=new(){new(){Name="dim",State=dimState}}};
        var dimRequest=HardwareWaveRequest.FromProfile(dimPrefs,"dim");
        Check(dimRequest.Palette.SequenceEqual(new[]{"#7F0600","#7F3200"}) && Effects.Palette(dimRequest.WindowsState!).SequenceEqual(new[]{"#FF0D00","#FF6401"}),"Quantization selected different fade endpoints for boot and Windows.");
        for(int tick=0;tick<300;tick++)
        {
            var sample=Effects.Sample(preferences.Studio,0,0,tick*.1);
            Check(sample==Effects.Sample(preferences.Studio,1,1,tick*.1) && sample.R==127 && sample.G>=6 && sample.G<=50,"Windows fade is spatially out of sync or darkens between lit endpoints.");
        }
        var fade=MsiProtocol.FadeConfiguration(original,color,1,Color.FromRgb(255,100,1));
        var fadeSave=MsiProtocol.FadeSavePacket(fade);MsiProtocol.ValidateFadeSave(fadeSave);
        Check(fade[31]==22 && fade[42]==22 && fade[74]==22 && fade[184]==0,"Fade did not select the owner-verified native mode.");
        Reject(()=>MsiProtocol.ValidateWaveSave(fadeSave));
        Reject(()=>MsiProtocol.FadeConfiguration(original,Colors.Black,1,color));
        Reject(()=>MsiProtocol.FadeConfiguration(original,Color.FromRgb(7,0,0),1,color));
        Reject(()=>new HardwareWaveRequest{MotionKind="Fade",Color="#070000",Palette=new[]{"#070000","#FF6401"}}.Validate());
        Reject(()=>MsiProtocol.ValidateWrite(fadeSave));
        var fadeFake=new MsiFake(original);
        await using(var fadeBackend=new MsiBackend(()=>fadeFake,()=>false))
        {
            await fadeBackend.ConnectAsync();await fadeBackend.PreviewFadeAsync(color,1,Color.FromRgb(255,100,1));
            Check(fadeFake.Commits==0,"Fade preview committed memory.");
            await fadeBackend.SaveFadeToHardwareAsync(color,1,Color.FromRgb(255,100,1));
            Check(fadeFake.Commits==1 && !fadeBackend.NeedsRestore,"Fade did not commit once and retain the accepted native baseline.");
        }
        var badFade=new MsiFake(original){BadReadback=true};
        await using(var fadeBackend=new MsiBackend(()=>badFade,()=>false))
        {
            await fadeBackend.ConnectAsync();try{await fadeBackend.SaveFadeToHardwareAsync(color,1,Color.FromRgb(255,100,1));}catch(IOException){}
            Check(badFade.Commits==0,"Fade saved despite failed readback.");
        }
        var fadeBoot=await BootLighting.ApplyAsync(BootLighting.CreateSnapshot(preferences),new(){MotherboardFactory=()=>throw new Exception("Fade boot must not open a controller"),GpuFactory=()=>throw new Exception("Fade boot must not open a controller")});
        Check(fadeBoot.HardwareWavePreserved && fadeBoot.ExitCode==0,"The boot helper overwrote the saved fade.");
        preferences.HardwareBootMotionKind="Wave";HardwareWaveRequest.ApplySaved(preferences,request);
        var wave=MsiProtocol.WaveConfiguration(MsiProtocol.StreamingConfiguration(original,60,60),color,0);
        Check(wave[31]==23 && wave[42]==23 && wave[74]==23 && wave[78]==0xA8 && wave[82]==0x81 && wave[184]==0,"MSI retained direct streaming or unsupported JPIPE routing.");
        Check(wave[32]==255 && wave[33]==13 && wave[34]==0 && wave[41]==60 && wave[52]==60,"MSI wave lost the profile colour or configured header counts.");
        var commit=MsiProtocol.WaveSavePacket(wave);MsiProtocol.ValidateWaveSave(commit);
        var dual=MsiProtocol.WaveConfiguration(original,color,2,Color.FromRgb(255,100,1));
        Check(dual[36]==255 && dual[37]==100 && dual[38]==1 && dual[32]==255 && dual[33]==13 && dual[35]==0x2A,"MSI secondary palette colour or fast motion was lost.");
        MsiProtocol.ValidateWaveSave(MsiProtocol.WaveSavePacket(dual));
        Reject(()=>MsiProtocol.ValidateWrite(commit));Reject(()=>MsiProtocol.ValidateStaticSave(commit));
        Reject(()=>MsiProtocol.WaveConfiguration(original,color,3));
        var invalid=(byte[])commit.Clone();invalid[82]=0xBF;Reject(()=>MsiProtocol.ValidateWaveSave(invalid));
        var unsupported=(byte[])commit.Clone();foreach(int offset in MsiProtocol.ZoneOffsets.Append(74))unsupported[offset]=39;
        Reject(()=>MsiProtocol.ValidateWaveSave(unsupported));
        var msi=new MsiFake(original);
        await using(var backend=new MsiBackend(()=>msi,()=>false))
        {
            await backend.ConnectAsync();await backend.PreviewWaveAsync(color,0);
            Check(msi.Commits==0 && msi.Writes==2,"Preview committed MSI hardware memory.");
            await backend.SaveWaveToHardwareAsync(color,0);
            Check(msi.Commits==1 && msi.Writes==4 && !backend.NeedsRestore,"MSI did not commit exactly once after verification.");
        }
        Check(msi.Commits==1 && msi.Disposed,"MSI disposal resaved or leaked the native wave.");
        var badMsi=new MsiFake(original){BadReadback=true};
        await using(var backend=new MsiBackend(()=>badMsi,()=>false))
        {
            await backend.ConnectAsync();bool failed=false;try{await backend.SaveWaveToHardwareAsync(color,0);}catch(IOException){failed=true;}
            Check(failed && badMsi.Commits==0,"MSI saved an unverified wave mode.");
        }
        var uncertainMsi=new MsiFake(original){FailCommit=true};
        await using(var backend=new MsiBackend(()=>uncertainMsi,()=>false))
        {
            await backend.ConnectAsync();try{await backend.SaveWaveToHardwareAsync(color,0);}catch(IOException){}
            try{await backend.SaveWaveToHardwareAsync(color,0);}catch(IOException){}
            Check(uncertainMsi.Commits==1,"Uncertain MSI wave commit was retried.");
        }

        var palette=new[]{color,Color.FromRgb(255,100,1),Color.FromRgb(139,59,0),Colors.Black};
        var litPalette=HardwareBootWave.BuildGpuPalette(palette);
        Check(litPalette.Length==8 && !litPalette.Contains(Colors.Black) && litPalette.Take(3).SequenceEqual(palette.Take(3)),"GPU boot palette retained a black stop or lost its three selected colours.");
        var two=HardwareBootWave.BuildGpuPalette(palette.Take(2).ToArray());
        Check(two.All(c=>c.R==255 && c.G>=13 && c.G<=100 && c.B<=1),"Two-colour lava boot palette introduced dark or unrelated shades.");
        Check(HardwareBootWave.BuildGpuPalette(new[]{Colors.Black}).All(c=>c==Colors.Black),"Zero-brightness/off profile was forced on.");
        var gpuSettings=GigabyteProtocol.WaveHardwareSettings(palette,1);
        Check(gpuSettings.Count==20 && gpuSettings[0].SequenceEqual(new byte[]{0x88,11,1,99,8,1,0,0}) && gpuSettings[10][5]==3 && gpuSettings[15][5]==4,"GPU palette mode omitted zones or used the fixed rainbow wave.");
        Check(gpuSettings[1].SequenceEqual(new byte[]{0xB0,11,255,13,0,255,100,1}) && gpuSettings[2].SequenceEqual(new byte[]{0xB1,11,139,59,0,0,0,0}),"GPU lost custom lava colours or their order.");
        Check(gpuSettings[3].AsSpan(2).IndexOfAnyExcept((byte)0)<0 && gpuSettings[4].AsSpan(2).IndexOfAnyExcept((byte)0)<0,"Unused GPU palette slots were not zeroed.");
        foreach(var packet in gpuSettings)GigabyteProtocol.ValidateWaveHardwareSetting(packet);
        Reject(()=>GigabyteProtocol.WaveHardwareSettings(palette,6));Reject(()=>GigabyteProtocol.WaveHardwareSettings(Array.Empty<Color>(),1));Reject(()=>GigabyteProtocol.Validate(GigabyteProtocol.HardwareSaveCommand()));
        Reject(()=>GigabyteProtocol.ValidateWaveHardwareSetting(GigabyteProtocol.HardwareSaveCommand()));
        Reject(()=>GigabyteProtocol.ValidateWaveHardwareSetting(new byte[]{0x88,7,1,99,8,1,0,0}));
        var gpu=new GpuFake();
        await using(var backend=new GigabyteBackend(()=>gpu,()=>false))
        {
            await backend.ConnectAsync();await backend.PreviewWaveAsync(palette,1);
            Check(gpu.Commits==0 && gpu.WaveWrites==20,"Preview committed GPU hardware memory.");
            await backend.SaveWaveToHardwareAsync(palette,1);
            Check(gpu.Commits==1 && gpu.WaveWrites==40,"GPU did not commit once after palette preparation.");
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(Colors.Blue,4).ToArray()}});
            Check(gpu.RuntimeWrites==16 && gpu.Commits==1,"Windows output did not reinitialize streaming after a wave.");
        }
        var uncertainGpu=new GpuFake{FailCommit=true};
        await using(var backend=new GigabyteBackend(()=>uncertainGpu,()=>false))
        {
            await backend.ConnectAsync();bool uncertain=false;
            try{await backend.SaveWaveToHardwareAsync(palette,1);}catch(IOException ex){uncertain=ex.Message.Contains("uncertain");}
            Check(uncertain && uncertainGpu.Commits==1 && backend.PacketRetries==0,"Uncertain GPU wave commit was automatically retried.");
        }
        var wrongGpu=new GpuFake();
        await using(var backend=new GigabyteBackend(()=>wrongGpu,()=>false))
        {
            await backend.ConnectAsync();wrongGpu.WrongIdentity=true;
            bool failed=false;try{await backend.SaveWaveToHardwareAsync(palette,1);}catch(InvalidDataException){failed=true;}
            Check(failed && wrongGpu.WaveWrites==0 && wrongGpu.Commits==0,"GPU wave bypassed the identity guard.");
        }

        var snapshot=BootLighting.CreateSnapshot(preferences);
        int opened=0;
        var boot=await BootLighting.ApplyAsync(snapshot,new(){MotherboardFactory=()=>{opened++;throw new Exception("Must not open");},GpuFactory=()=>{opened++;throw new Exception("Must not open");}});
        Check(boot.HardwareWavePreserved && boot.ExitCode==0 && opened==0 && !boot.MotherboardApplied && !boot.GpuApplied,"The Windows helper overwrote native wave with a static frame.");
        preferences.HardwareBootWaveSaved=false;Check(!BootLighting.CreateSnapshot(preferences).PreserveHardwareWave,"Static boot behaviour stayed disabled after replacing the wave.");
    }
    sealed class MsiFake : IMsiTransport,IMsiHardwareSaveTransport
    {
        byte[] state;public int Writes,Commits;public bool Disposed,BadReadback,FailCommit;
        public MsiFake(byte[] initial)=>state=(byte[])initial.Clone();
        public byte[] Read(){var result=(byte[])state.Clone();result[129]=0;if(BadReadback && Writes>=2)result[31]=1;return result;}
        public void Write(byte[] packet){MsiProtocol.ValidateWrite(packet);Writes++;if(packet.Length==185)state=(byte[])packet.Clone();}
        public void SaveStatic(byte[] packet)=>throw new Exception("Wave used static commit path.");
        public void SaveWave(byte[] packet){MsiProtocol.ValidateWaveSave(packet);Commits++;if(FailCommit)throw new IOException("Uncertain flash result");state=(byte[])packet.Clone();state[184]=0;}
        public void SaveFade(byte[] packet){MsiProtocol.ValidateFadeSave(packet);Commits++;if(FailCommit)throw new IOException("Uncertain flash result");state=(byte[])packet.Clone();state[184]=0;}
        public void Dispose()=>Disposed=true;
    }
    sealed class GpuFake : IGpuHardwareSaveTransport
    {
        public int Commits,WaveWrites,RuntimeWrites;public bool FailCommit,WrongIdentity;
        public byte[] QueryIdentity()=>WrongIdentity?new byte[4]:new byte[]{0xAB,0x11,0x52,1};
        public void Write(byte[] packet){GigabyteProtocol.Validate(packet);RuntimeWrites++;}
        public void WriteStaticSetting(byte[] packet)=>throw new Exception("Wave used static preparation path.");
        public void WriteWaveSetting(byte[] packet){GigabyteProtocol.ValidateWaveHardwareSetting(packet);WaveWrites++;}
        public void CommitStaticConfiguration(){Commits++;if(FailCommit)throw new NvidiaLightingException(-1);}
        public void Dispose(){}
    }
}
