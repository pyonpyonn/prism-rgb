using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

internal static class MsiHardwareSaveChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static bool Rejects(Action action)
    {
        try{action();return false;}catch(InvalidDataException){return true;}
    }
    public static async Task VerifyAsync()
    {
        var input=new byte[185];input[0]=0x52;input[41]=60;input[52]=60;input[62]=0x82;
        var streamed=MsiProtocol.StreamingConfiguration(input,60,60);
        var color=Color.FromRgb(119,46,22);
        var selected=MsiProtocol.StaticConfiguration(streamed,color);
        foreach(int offset in MsiProtocol.ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}))
        {
            Require(selected[offset]==1 && selected[offset+4]==0x28 && selected[offset+9]==0,"A static zone retained streaming mode, dim brightness or padding.");
            Require(selected[offset+1]==119 && selected[offset+2]==46 && selected[offset+3]==22 && selected[offset+5]==119 && selected[offset+6]==46 && selected[offset+7]==22,"Static color triplets differ.");
            Require(selected[offset+8]==(offset==74?0x81:0x80),"A static zone retained streaming sync flags.");
        }
        Require(selected[184]==0 && selected[41]==60 && selected[52]==60 && selected[62]==0x82,"Static preparation changed unrelated fields or committed flash.");
        var saved=MsiProtocol.StaticSavePacket(selected);
        MsiProtocol.ValidateStaticSave(saved);
        Require(Rejects(()=>MsiProtocol.ValidateWrite(saved)),"The frame transport accepted an MSI flash commit.");
        var invalid=(byte[])saved.Clone();invalid[74]=0x25;
        Require(Rejects(()=>MsiProtocol.ValidateStaticSave(invalid)),"Streaming mode passed the static save validator.");
        invalid=(byte[])saved.Clone();invalid[82]=0xBF;
        Require(Rejects(()=>MsiProtocol.ValidateStaticSave(invalid)),"Streaming routing passed the static save validator.");
        Require(Rejects(()=>MsiProtocol.StaticSavePacket(new byte[725])),"A streaming-size packet passed the static save builder.");
        var normalized=(byte[])selected.Clone();normalized[129]=0;
        Require(MsiProtocol.MatchesStaticConfiguration(selected,normalized),"The observed static-only secondary red normalization was rejected.");
        Require(Rejects(()=>MsiProtocol.StaticSavePacket(normalized)),"Readback normalization relaxed the persistent packet validator.");
        var wrongPrimary=(byte[])normalized.Clone();wrongPrimary[125]=0;
        Require(!MsiProtocol.MatchesStaticConfiguration(selected,wrongPrimary),"Readback normalization hid an active primary RGB mismatch.");
        var wrongMode=(byte[])normalized.Clone();wrongMode[124]=0x25;
        Require(!MsiProtocol.MatchesStaticConfiguration(selected,wrongMode),"Readback normalization hid a native mode mismatch.");

        var normalizing=new Fake(streamed){NormalizeUnusedReadback=true,NormalizeStaticSecondaryRed=true};
        await using(var backend=new MsiBackend(()=>normalizing,()=>false))
        {
            await backend.ConnectAsync();Require(normalizing.Writes.Count==0 && normalizing.Commits==0,"Connecting saved hardware state.");
            await backend.SaveStaticToHardwareAsync(color);
            Require(normalizing.Commits==1 && normalizing.Writes.Count==2 && !backend.NeedsRestore,"A static save did not commit exactly once after two volatile preparations.");
            Require(normalizing.Writes.All(packet=>packet[184]==0),"A preparation report committed flash.");
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{2,new[]{Colors.Blue}},{3,new[]{Colors.Red}}});
            await backend.RestoreAsync();
            Require(normalizing.Commits==1 && MsiProtocol.MatchesStaticConfiguration(selected,normalizing.State),"Normal output or restoration repeated the save or lost the selected static baseline.");
        }
        Require(normalizing.Commits==1 && normalizing.Disposals==1,"Disposal saved hardware again or leaked the transport.");

        var mismatch=new Fake(streamed){MismatchReadback=true};
        await using(var backend=new MsiBackend(()=>mismatch,()=>false))
        {
            await backend.ConnectAsync();bool failed=false;
            try{await backend.SaveStaticToHardwareAsync(color);}catch(IOException){failed=true;}
            Require(failed && mismatch.Commits==0,"MSI committed an unverified static configuration.");
        }
        var uncertain=new Fake(streamed){FailCommit=true};
        await using(var backend=new MsiBackend(()=>uncertain,()=>false))
        {
            await backend.ConnectAsync();bool failed=false;
            try{await backend.SaveStaticToHardwareAsync(color);}catch(IOException ex){failed=ex.Message.Contains("will not retry",StringComparison.Ordinal);}
            Require(failed && uncertain.Commits==1,"An uncertain MSI save was retried or reported as confirmed.");
            try{await backend.SaveStaticToHardwareAsync(color);}catch(IOException){}
            Require(uncertain.Commits==1,"A faulted MSI connection repeated an uncertain commit.");
        }
        var preparationFailure=new Fake(streamed){FailPreparation=true};
        await using(var backend=new MsiBackend(()=>preparationFailure,()=>false))
        {
            await backend.ConnectAsync();bool failed=false;
            try{await backend.SaveStaticToHardwareAsync(color);}catch(IOException){failed=true;}
            Require(failed && preparationFailure.Commits==0,"A failed volatile preparation still committed MSI flash.");
        }
    }
    sealed class Fake : IMsiTransport,IMsiHardwareSaveTransport
    {
        public byte[] State;
        public readonly List<byte[]> Writes=new();
        public int Reads,Commits,Disposals;
        public bool NormalizeUnusedReadback,NormalizeStaticSecondaryRed,MismatchReadback,FailCommit,FailPreparation;
        public Fake(byte[] initial){State=(byte[])initial.Clone();}
        public byte[] Read()
        {
            Reads++;var result=(byte[])State.Clone();
            if(Reads>=3 && NormalizeUnusedReadback)result[62]^=1;
            if(Reads>=3 && NormalizeStaticSecondaryRed && result[124]==1)result[129]=0;
            if(Reads>=3 && MismatchReadback)result[32]^=1;
            return result;
        }
        public void Write(byte[] packet)
        {
            MsiProtocol.ValidateWrite(packet);Writes.Add((byte[])packet.Clone());
            if(FailPreparation && Writes.Count==2)throw new IOException("Simulated preparation failure.");
            if(packet.Length==185)State=(byte[])packet.Clone();
        }
        public void SaveStatic(byte[] packet)
        {
            MsiProtocol.ValidateStaticSave(packet);Commits++;
            if(FailCommit)throw new IOException("Simulated uncertain flash acknowledgement.");
            State=(byte[])packet.Clone();State[184]=0;
        }
        public void Dispose(){Disposals++;}
    }
}
