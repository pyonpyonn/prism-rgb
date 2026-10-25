using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

internal static class HardwareFilledWaveChecks
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject(Action action){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}Check(failed,"An unsafe filled-wave packet was accepted.");}
    public static async Task VerifyAsync()
    {
        var original=new byte[185];original[0]=0x52;original[41]=60;original[52]=60;original[62]=0x82;
        var selected=MsiProtocol.FilledWaveConfiguration(original,1,10);
        Check(MsiProtocol.ZoneOffsets.Append(74).All(o=>selected[o]==26) && selected[41]==60 && selected[52]==60 && selected[62]==0x82 && selected[184]==0,"Filled wave lost the native travelling mode or changed LED counts/unrelated settings.");
        Check(selected[78]==0xA9 && selected[82]==0x81,"Filled wave changed the known routing.");
        var saved=MsiProtocol.FilledWaveSavePacket(selected);MsiProtocol.ValidateFilledWaveSave(saved);
        Reject(()=>MsiProtocol.ValidateWrite(saved));Reject(()=>MsiProtocol.ValidateWaveSave(saved));Reject(()=>MsiProtocol.ValidateFadeSave(saved));
        var tampered=(byte[])saved.Clone();tampered[31]=23;Reject(()=>MsiProtocol.ValidateFilledWaveSave(tampered));
        tampered=(byte[])saved.Clone();tampered[82]=0xBF;Reject(()=>MsiProtocol.ValidateFilledWaveSave(tampered));
        Reject(()=>MsiProtocol.FilledWaveConfiguration(original,3,10));Reject(()=>MsiProtocol.FilledWaveConfiguration(original,1,0));
        var msi=new MsiFake(original);
        await using(var backend=new MsiBackend(()=>msi,()=>false))
        {
            await backend.ConnectAsync();await backend.PreviewFilledWaveAsync(1,10);
            Check(msi.Commits==0 && msi.Writes==2,"Filled-wave preview committed motherboard memory.");
            await backend.SaveFilledWaveToHardwareAsync(1,10);
            Check(msi.Commits==1 && msi.Writes==4 && !backend.NeedsRestore,"Filled wave did not save once after readback verification.");
        }
        var bad=new MsiFake(original){Mismatch=true};
        await using(var backend=new MsiBackend(()=>bad,()=>false))
        {
            await backend.ConnectAsync();bool failed=false;
            try{await backend.SaveFilledWaveToHardwareAsync(1,10);}catch(IOException){failed=true;}
            Check(failed && bad.Commits==0,"An unconfirmed motherboard wave was saved.");
        }
        var uncertain=new MsiFake(original){Uncertain=true};
        await using(var backend=new MsiBackend(()=>uncertain,()=>false))
        {
            await backend.ConnectAsync();try{await backend.SaveFilledWaveToHardwareAsync(1,10);}catch(IOException){}
            try{await backend.SaveFilledWaveToHardwareAsync(1,10);}catch(IOException){}
            Check(uncertain.Commits==1,"Uncertain motherboard wave delivery was repeated.");
        }
        var packets=GigabyteProtocol.FilledWaveHardwareSettings(3,99);
        Check(packets.Count==8 && packets.Where(p=>p[0]==0x88).Select(p=>p[5]).SequenceEqual(new byte[]{1,2,3,4}),"GPU wave did not prepare every native zone.");
        foreach(var packet in packets)GigabyteProtocol.ValidateFilledWaveHardwareSetting(packet);
        Reject(()=>GigabyteProtocol.ValidateFilledWaveHardwareSetting(GigabyteProtocol.HardwareSaveCommand()));
        Reject(()=>GigabyteProtocol.ValidateFilledWaveHardwareSetting(new byte[]{0x88,11,3,99,0,1,0,0}));
        Reject(()=>GigabyteProtocol.FilledWaveHardwareSettings(6,99));Reject(()=>GigabyteProtocol.FilledWaveHardwareSettings(3,0));
        var gpu=new GpuFake();
        await using(var backend=new GigabyteBackend(()=>gpu,()=>false))
        {
            await backend.ConnectAsync();await backend.PreviewFilledWaveAsync(3,99);
            Check(gpu.Commits==0 && gpu.Preparations==8,"Filled-wave preview committed GPU memory.");
            await backend.SaveFilledWaveToHardwareAsync(3,99);
            Check(gpu.Commits==1 && gpu.Preparations==16,"GPU filled wave did not commit once.");
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(Colors.Blue,4).ToArray()}});
            Check(gpu.Commits==1 && gpu.Frames==16,"Windows rendering did not resume volatile output after a native wave.");
        }
        var wrongGpu=new GpuFake();
        await using(var backend=new GigabyteBackend(()=>wrongGpu,()=>false))
        {
            await backend.ConnectAsync();wrongGpu.WrongIdentity=true;
            try{await backend.SaveFilledWaveToHardwareAsync(3,99);}catch(InvalidDataException){}
            Check(wrongGpu.Commits==0 && wrongGpu.Preparations==0,"GPU wave bypassed exact identity verification.");
        }
        var uncertainGpu=new GpuFake{Uncertain=true};
        await using(var backend=new GigabyteBackend(()=>uncertainGpu,()=>false))
        {
            await backend.ConnectAsync();try{await backend.SaveFilledWaveToHardwareAsync(3,99);}catch(IOException){}
            Check(uncertainGpu.Commits==1 && backend.PacketRetries==0,"An uncertain GPU commit was automatically repeated.");
        }
    }
    sealed class MsiFake : IMsiTransport,IMsiHardwareSaveTransport
    {
        byte[] state;public int Writes,Commits;public bool Mismatch,Uncertain;
        public MsiFake(byte[] initial)=>state=(byte[])initial.Clone();
        public byte[] Read(){var result=(byte[])state.Clone();result[129]=0;if(Mismatch && Writes>=2)result[31]=23;return result;}
        public void Write(byte[] packet){MsiProtocol.ValidateWrite(packet);Writes++;state=(byte[])packet.Clone();}
        public void SaveStatic(byte[] packet)=>throw new Exception("Filled wave used the static saver.");
        public void SaveFilledWave(byte[] packet){MsiProtocol.ValidateFilledWaveSave(packet);Commits++;if(Uncertain)throw new IOException("Uncertain commit");state=(byte[])packet.Clone();state[184]=0;}
        public void Dispose(){}
    }
    sealed class GpuFake : IGpuHardwareSaveTransport
    {
        public int Preparations,Commits,Frames;public bool WrongIdentity,Uncertain;
        public byte[] QueryIdentity()=>WrongIdentity?new byte[4]:new byte[]{0xAB,0x11,0x52,1};
        public void Write(byte[] packet){GigabyteProtocol.Validate(packet);Frames++;}
        public void WriteStaticSetting(byte[] packet)=>throw new Exception("Filled wave used static preparations.");
        public void WriteFilledWaveSetting(byte[] packet){GigabyteProtocol.ValidateFilledWaveHardwareSetting(packet);Preparations++;}
        public void CommitStaticConfiguration(){Commits++;if(Uncertain)throw new NvidiaLightingException(-1);}
        public void Dispose(){}
    }
}
