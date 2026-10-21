using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

internal static class GpuHardwareSaveChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Reject(Action action)
    {
        bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}
        Require(rejected,"A restricted GPU command was accepted.");
    }
    static async Task<Exception> Failure(Func<Task> action)
    {
        try{await action();}catch(Exception ex){return ex;}
        throw new InvalidOperationException("Expected the GPU hardware save to fail.");
    }
    internal static async Task VerifyAsync()
    {
        var color=Color.FromRgb(0x12,0x56,0xA3);
        var settings=GigabyteProtocol.StaticHardwareSettings(color);
        Require(settings.Count==8,"GPU static setup omitted a fan/logo zone.");
        for(int zone=1;zone<=4;zone++)
        {
            Require(settings[(zone-1)*2].SequenceEqual(new byte[]{0x88,1,2,0x63,0,(byte)zone,0,0}),"GPU native static mode still uses streaming routing.");
            byte[] expected=zone switch
            {
                1 or 2=>new byte[]{0xB0,1,0x12,0x56,0xA3,0x12,0x56,0xA3},
                3=>new byte[]{0xB1,1,0x12,0x56,0xA3,0,0,0},
                _=>new byte[]{0x40,0x12,0x56,0xA3,4,0,0,0}
            };
            Require(settings[(zone-1)*2+1].SequenceEqual(expected),"GPU native static RGB register mapping changed.");
        }
        foreach(var packet in settings)GigabyteProtocol.ValidateStaticHardwareSetting(packet);
        var commit=GigabyteProtocol.HardwareSaveCommand();
        Require(commit.SequenceEqual(new byte[]{0xAA,0,0,0,0,0,0,0}),"GPU save command contains extra data.");
        Reject(()=>GigabyteProtocol.Validate(commit));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(commit));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(new byte[]{0x88,1,2,0x63,0,0,0,0}));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(new byte[]{0x88,1,2,0x63,0,5,0,0}));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(new byte[]{0x88,1,2,0x63,8,1,0,0}));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(new byte[]{0xB1,1,1,2,3,1,0,0}));
        Reject(()=>GigabyteProtocol.ValidateStaticHardwareSetting(new byte[]{0x40,1,2,3,5,0,0,0}));

        var transport=new SaveTransport();
        var backend=new GigabyteBackend(()=>transport,()=>false);
        await backend.ConnectAsync();
        var frame=new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(Colors.Red,4).ToArray()}};
        await backend.WriteAsync(frame);
        Require(transport.CommitAttempts==0 && transport.StaticAttempts==0,"Ordinary animation attempted a hardware save.");
        await backend.SaveStaticToHardwareAsync(color);
        Require(transport.Queries==2 && transport.CommitAttempts==1 && transport.StaticPackets.Count==8,"GPU save skipped verification or committed more than once.");
        Require(transport.StaticPackets.Zip(settings).All(pair=>pair.First.SequenceEqual(pair.Second)),"The GPU save backend altered the validated native static plan.");
        int runtimeBefore=transport.RuntimePackets.Count;
        await backend.WriteAsync(frame);
        Require(transport.RuntimePackets.Count>runtimeBefore && transport.RuntimePackets[runtimeBefore][0]==0x88 && transport.RuntimePackets[runtimeBefore][4]==8,"Streaming mode was not reinitialized after native static save.");
        await backend.RestoreAsync();
        await backend.DisposeAsync();
        Require(transport.Disposed && transport.CommitAttempts==1,"Cleanup committed hardware memory again or leaked the GPU transport.");

        var failedPrep=new SaveTransport{FailPreparation=true};
        await using(var instance=new GigabyteBackend(()=>failedPrep,()=>false))
        {
            await instance.ConnectAsync();await Failure(()=>instance.SaveStaticToHardwareAsync(color));
            Require(failedPrep.StaticAttempts==3 && failedPrep.CommitAttempts==0,"Failed volatile preparation still committed the GPU configuration.");
        }
        var uncertain=new SaveTransport{FailCommit=true};
        await using(var instance=new GigabyteBackend(()=>uncertain,()=>false))
        {
            await instance.ConnectAsync();var error=await Failure(()=>instance.SaveStaticToHardwareAsync(color));
            Require(error is IOException && error.InnerException is NvidiaLightingException && error.Message.Contains("uncertain") && uncertain.CommitAttempts==1 && instance.PacketRetries==0,"An uncertain GPU commit was retried or incorrectly reported as successful.");
        }
        var changedIdentity=new SaveTransport();
        await using(var instance=new GigabyteBackend(()=>changedIdentity,()=>false))
        {
            await instance.ConnectAsync();changedIdentity.BadIdentity=true;
            await Failure(()=>instance.SaveStaticToHardwareAsync(color));
            Require(changedIdentity.Queries==2 && changedIdentity.StaticAttempts==0 && changedIdentity.CommitAttempts==0,"GPU hardware save bypassed its identity guard.");
        }
        var ownedElsewhere=new SaveTransport();bool conflict=false;
        await using(var instance=new GigabyteBackend(()=>ownedElsewhere,()=>conflict))
        {
            await instance.ConnectAsync();conflict=true;await Failure(()=>instance.SaveStaticToHardwareAsync(color));
            Require(ownedElsewhere.StaticAttempts==0 && ownedElsewhere.CommitAttempts==0,"GPU hardware save ignored competing lighting software.");
        }
    }

    sealed class SaveTransport : IGpuHardwareSaveTransport
    {
        internal readonly List<byte[]> RuntimePackets=new(),StaticPackets=new();
        internal int Queries,StaticAttempts,CommitAttempts;
        internal bool FailPreparation,FailCommit,BadIdentity,Disposed;
        public byte[] QueryIdentity(){Queries++;return BadIdentity?new byte[4]:new byte[]{0xAB,0x11,0x52,1};}
        public void Write(byte[] packet){GigabyteProtocol.Validate(packet);RuntimePackets.Add((byte[])packet.Clone());}
        public void WriteStaticSetting(byte[] packet)
        {
            GigabyteProtocol.ValidateStaticHardwareSetting(packet);StaticAttempts++;
            if(FailPreparation)throw new NvidiaLightingException(-1);
            StaticPackets.Add((byte[])packet.Clone());
        }
        public void CommitStaticConfiguration(){CommitAttempts++;if(FailCommit)throw new NvidiaLightingException(-1);}
        public void Dispose()=>Disposed=true;
    }
}
