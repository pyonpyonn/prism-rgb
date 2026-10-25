using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public static partial class MsiProtocol
{
    public static byte[] FilledWaveConfiguration(byte[] original,int speed,int brightness)
    {
        if(brightness is <1 or >10)throw new InvalidDataException("MSI filled-wave brightness must stay lit.");
        // Mode26 was observed to be a filled travelling wave. Its spectrum is fixed;
        // the stored RGB fields are deliberately neutral and are not advertised as a palette.
        var packet=WaveConfiguration(original,Colors.White,speed,Colors.White);
        foreach(int offset in ZoneOffsets.Append(74)){packet[offset]=26;packet[offset+4]=(byte)((brightness<<2)|speed);}
        packet[78]|=0x80;return packet;
    }
    public static byte[] FilledWaveSavePacket(byte[] configuration)
    {
        if(configuration.Length!=185)throw new InvalidDataException("Unexpected MSI filled-wave configuration.");
        var packet=(byte[])configuration.Clone();packet[184]=1;ValidateFilledWaveSave(packet);return packet;
    }
    public static void ValidateFilledWaveSave(byte[] packet)
    {
        if(packet.Length!=185 || packet[0]!=0x52 || packet[184]!=1)throw new InvalidDataException("Only a native filled wave can be saved.");
        var expected=FilledWaveConfiguration(packet,packet[5]&3,(packet[5]>>2)&31);expected[184]=1;
        if(!packet.AsSpan().SequenceEqual(expected))throw new InvalidDataException("Unsupported MSI filled-wave mode, brightness or routing.");
    }
}

public sealed partial class MsiBackend
{
    public Task PreviewFilledWaveAsync(int speed,int brightness)=>ApplyFilledWaveAsync(speed,brightness,false);
    public Task SaveFilledWaveToHardwareAsync(int speed,int brightness)=>ApplyFilledWaveAsync(speed,brightness,true);
    Task ApplyFilledWaveAsync(int speed,int brightness,bool persist)=>Locked(()=>
    {
        if(faulted || transport is null || original is null)throw new IOException("Controller is unavailable. Disconnect and reconnect.");
        CheckConflict(true);
        if(persist && transport is not IMsiHardwareSaveTransport)throw new NotSupportedException("This transport cannot save a filled wave.");
        var current=MsiProtocol.Frame(transport.Read(),new Dictionary<uint,Color[]>());
        var selected=MsiProtocol.FilledWaveConfiguration(current,speed,brightness);
        wrote=true;lastPacket=null;lastDirect.Clear();activeCounts.Clear();
        try
        {
            transport.Write(current);Writes++;transport.Write(selected);Writes++;Thread.Sleep(250);
            var observed=transport.Read();
            if(!MsiProtocol.MatchesStaticConfiguration(selected,observed))throw new IOException("MSI did not confirm the filled wave; no save was sent. "+MsiProtocol.StaticMismatchDetails(selected,observed));
            if(persist)
            {
                try{((IMsiHardwareSaveTransport)transport).SaveFilledWave(MsiProtocol.FilledWaveSavePacket(selected));Writes++;}
                catch(Exception ex){throw new IOException("MSI filled-wave save delivery is uncertain. It was attempted once and will not be retried automatically.",ex);}
                Thread.Sleep(250);original=selected;wrote=false;
            }
        }
        catch{faulted=true;throw;}
    });
}

public static partial class GigabyteProtocol
{
    public static List<byte[]> FilledWaveHardwareSettings(int speed,int brightness)
    {
        if(speed is <0 or >5 || brightness is <1 or >99)throw new InvalidDataException("Unsupported GPU filled-wave speed or brightness.");
        var packets=new List<byte[]>();
        for(int zone=1;zone<=4;zone++)
        {
            packets.Add(new byte[]{0x88,7,(byte)speed,(byte)brightness,0,(byte)zone,0,0});
            packets.Add(zone switch
            {
                1 or 2=>new byte[]{0xB0,7,255,255,255,255,255,255},
                3=>new byte[]{0xB1,7,255,255,255,0,0,0},
                _=>new byte[]{0x40,255,255,255,4,0,0,0}
            });
        }
        return packets;
    }
    public static void ValidateFilledWaveHardwareSetting(byte[] packet)
    {
        if(packet.Length!=8)throw new InvalidDataException("GPU filled-wave settings must contain eight bytes.");
        bool mode=packet[0]==0x88 && packet[1]==7 && packet[2]<=5 && packet[3] is >=1 and <=99 && packet[4]==0 && packet[5] is >=1 and <=4 && packet[6]==0 && packet[7]==0;
        bool paired=packet.AsSpan().SequenceEqual(new byte[]{0xB0,7,255,255,255,255,255,255});
        bool right=packet.AsSpan().SequenceEqual(new byte[]{0xB1,7,255,255,255,0,0,0});
        bool logo=packet.AsSpan().SequenceEqual(new byte[]{0x40,255,255,255,4,0,0,0});
        if(!mode && !paired && !right && !logo)throw new InvalidDataException("Unsupported GPU filled-wave setting.");
    }
}

public sealed partial class GigabyteBackend
{
    public Task PreviewFilledWaveAsync(int speed,int brightness)=>ApplyFilledWaveAsync(speed,brightness,false);
    public Task SaveFilledWaveToHardwareAsync(int speed,int brightness)=>ApplyFilledWaveAsync(speed,brightness,true);
    Task ApplyFilledWaveAsync(int speed,int brightness,bool persist)=>Locked(async token=>
    {
        if(conflict())throw new IOException("Close competing RGB software before applying a GPU filled wave.");
        if(transport is not IGpuHardwareSaveTransport hardware)throw new NotSupportedException("This GPU transport cannot apply a native filled wave.");
        if(!GigabyteProtocol.Identity(hardware.QueryIdentity()))throw new InvalidDataException("GPU identity changed; no wave settings were sent.");
        var packets=GigabyteProtocol.FilledWaveHardwareSettings(speed,brightness);
        previous=null;packetCache.Clear();modeReady=false;wrote=true;
        foreach(var packet in packets)await SendVolatileAsync(packet,hardware.WriteFilledWaveSetting,token);
        if(persist)
        {
            try{hardware.CommitStaticConfiguration();Writes++;}
            catch(Exception ex){if(ex is NvidiaLightingException)TransferFailures++;throw new IOException("GPU filled-wave save delivery is uncertain. It was attempted once and will not be retried automatically.",ex);}
        }
    });
}
