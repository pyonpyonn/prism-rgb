using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public static class SelfTests
{
    static readonly List<object> results=new();
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    static void Test(string name,Action run)
    {
        try{run();results.Add(new{Name=name,Passed=true,Error=(string?)null});}
        catch(Exception e){results.Add(new{Name=name,Passed=false,Error=e.ToString()});}
    }
    static async Task TestAsync(string name,Func<Task> run)
    {
        try{await run();results.Add(new{Name=name,Passed=true,Error=(string?)null});}
        catch(Exception e){results.Add(new{Name=name,Passed=false,Error=e.ToString()});}
    }
    public static async Task<int> RunAsync()
    {
        Test("All effects obey zero brightness",()=>
        {
            foreach(var n in Effects.Names)for(int i=0;i<12;i++)Check(Effects.Sample(new(){Effect=n,Brightness=0},i/12.0,.3,i,Colors.Coral)==Colors.Black,n);
        });
        Test("Solid color and global brightness",()=>
        {
            var s=new StudioState{Effect="Solid",Color="#804020",Brightness=.5};Check(Effects.Sample(s,.1,.2,5)==Color.FromRgb(64,32,16),"Incorrect solid output");
        });
        Test("Animated effects change with time",()=>
        {
            foreach(var n in Effects.Names.Where(n=>n!="Solid" && n!="Screen ambience"))
            {
                var samples=new HashSet<Color>();
                foreach(double t in new[]{0,.2,.7,1.3,2.4,4.0})samples.Add(Effects.Sample(new(){Effect=n},.3,.4,t));
                Check(samples.Count>2,n+" did not animate across the observed interval");
            }
        });
        Test("Screen ambience uses sampled screen color",()=>
        {
            Check(Effects.Sample(new(){Effect="Screen ambience",Brightness=1},0,0,0,Colors.OrangeRed)==Colors.OrangeRed,"Screen sample ignored");
        });
        Test("MSI frame preserves all unselected bytes and never saves flash",()=>
        {
            byte[] baseline=Enumerable.Range(0,185).Select(x=>(byte)(x%256)).ToArray();baseline[0]=0x52;
            var p=MsiProtocol.Frame(baseline,new Dictionary<uint,Color[]>{{2,new[]{Color.FromRgb(12,34,56)}}});
            Check(p.Length==185 && p[0]==0x52 && p[184]==0,"Wrong framing");
            Check(p[31]==1 && p[32]==12 && p[33]==34 && p[34]==56,"Wrong zone offset or order");
            for(int i=0;i<185;i++)if(i!=31 && i!=32 && i!=33 && i!=34 && i!=184)Check(p[i]==baseline[i],"Unselected byte changed: "+i);
            Check(baseline[31]==31 && baseline[184]==184,"Baseline was mutated");
        });
        Test("MSI rejects malformed reports and unsupported zones",()=>
        {
            bool a=false,b=false;try{MsiProtocol.Frame(new byte[185],new Dictionary<uint,Color[]>());}catch(InvalidDataException){a=true;}
            var original=new byte[185];original[0]=0x52;
            try{MsiProtocol.Frame(original,new Dictionary<uint,Color[]>{{4,new[]{Colors.Red}}});}catch(InvalidDataException){b=true;}
            Check(a&&b,"Invalid MSI data accepted");
        });
        Test("Profiles round-trip and normalize imported settings",()=>
        {
            string dir=Path.Combine(Directory.GetCurrentDirectory(),"work","profile-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);Storage.DirectoryPath=dir;
            var s=new StudioState{Effect="Wave",Color="#AABBCC",Speed=.8};s.Layout[0].X=.3;
            var prefs=new Preferences{Studio=s,Profiles=new(){new(){Name="Test",State=s.Clone()}}};Storage.Save(prefs);
            Check(Storage.Load().Profiles[0].State.Layout[0].X==.3,"Layout persistence failed");
            string path=Path.Combine(dir,"profile.json");Storage.Export(prefs.Profiles[0],path);Check(Storage.Import(path).State.Effect=="Wave","Profile import failed");
            File.WriteAllText(path,"{\"Name\":\"Broken\",\"State\":{\"Effect\":\"invalid\",\"Color\":\"bad\",\"Brightness\":900,\"Layout\":[]}}");
            var imported=Storage.Import(path);Check(imported.State.Effect=="Aurora" && imported.State.Brightness==1 && imported.State.Layout.Count==5,"Unsafe imported settings survived");
            var layoutState=new StudioState();layoutState.Layout[0].Name=null!;layoutState.Layout[0].Shape=null!;layoutState.Normalize();
            Check(layoutState.Layout[0].Name=="Light" && layoutState.Layout[0].Shape=="strip","Incomplete imported layout cannot render safely");
            File.WriteAllText(Path.Combine(dir,"settings.json"),"broken");Check(Storage.Load().Studio.Effect=="Aurora","Corrupt preferences crashed load");
        });
        Test("Exact MSI board and descriptor whitelist",()=>
        {
            var d=new HidDevice("fixture",0x1462,0x7C95,"MSI",725,1,0,185);
            Check(MsiProtocol.Supports(MsiProtocol.Board,d),"Target rejected");
            Check(!MsiProtocol.Supports("Other board",d),"Different board accepted");
            Check(!MsiProtocol.Supports(MsiProtocol.Board,d with {Report52Length=725}),"Different report size accepted");
            Check(!MsiProtocol.Supports(MsiProtocol.Board,d with {UsagePage=0xFF00}),"Different collection accepted");
            Check(!MsiProtocol.Supports(MsiProtocol.Board,d with {ProductId=0x1234}),"Different PID accepted");
        });
        Test("Low-power scheduling sleeps when no work is needed",()=>
        {
            Check(!ActivityPolicy.NeedsTicks(false,false,true,true,true),"Hidden idle loop runs");
            Check(!ActivityPolicy.NeedsTicks(true,true,false,false,false),"Solid output does not sleep");
            Check(ActivityPolicy.NeedsTicks(false,true,true,false,false),"Hidden hardware animation stopped");
            Check(ActivityPolicy.NeedsTicks(false,true,false,false,true),"Changed solid output did not wake");
            Check(ActivityPolicy.NeedsTicks(true,false,false,true,false),"Visible screen capture stopped");
        });
        await TestAsync("Native connection only reads; duplicate colors are suppressed; restore resets deduplication",async()=>
        {
            var transport=new FakeMsiTransport();await using var backend=new MsiBackend(()=>transport,()=>false);
            await backend.ConnectAsync();Check(backend.Devices.Count==4 && transport.Packets.Count==0,"Connect wrote lighting");
            var colors=new Dictionary<uint,Color[]>{{2,new[]{Colors.Coral}}};
            await backend.WriteAsync(colors);await backend.WriteAsync(colors);Check(transport.Packets.Count==1,"Duplicate USB write");
            await backend.RestoreAsync();Check(transport.Packets.Count==2 && transport.Packets[1].SequenceEqual(transport.Original),"Restoration failed");
            await backend.WriteAsync(colors);Check(transport.Packets.Count==3,"Restart did not reapply color");await backend.RestoreAsync();
        });
        await TestAsync("Native failures stop further writes and still attempt restoration",async()=>
        {
            var transport=new FakeMsiTransport{FailWrite=true};await using var backend=new MsiBackend(()=>transport,()=>false);await backend.ConnectAsync();
            bool failed=false;try{await backend.WriteAsync(new Dictionary<uint,Color[]>{{0,new[]{Colors.Red}}});}catch(IOException){failed=true;}
            Check(failed,"USB failure ignored");transport.FailWrite=false;
            failed=false;try{await backend.WriteAsync(new Dictionary<uint,Color[]>{{0,new[]{Colors.Blue}}});}catch(IOException){failed=true;}
            Check(failed && transport.Packets.Count==0,"Faulted backend resumed");
            transport.BadReport=true;failed=false;try{await backend.RestoreAsync();}catch(IOException){failed=true;}
            Check(failed && backend.NeedsRestore,"Bad restore readback was accepted");
            transport.BadReport=false;await backend.RestoreAsync();Check(transport.Packets.Count==2 && !backend.NeedsRestore,"Failed write was not restored");
        });
        await TestAsync("Conflicting lighting software blocks connection and restoration",async()=>
        {
            bool conflict=true,opened=false;var transport=new FakeMsiTransport();
            await using(var blocked=new MsiBackend(()=>{opened=true;return transport;},()=>conflict))
            {
                bool failed=false;try{await blocked.ConnectAsync();}catch(IOException){failed=true;}Check(failed&&!opened,"Conflict was ignored");
            }
            conflict=false;await using var backend=new MsiBackend(()=>transport,()=>conflict);await backend.ConnectAsync();
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{1,new[]{Colors.Green}}});conflict=true;
            bool restoreFailed=false;try{await backend.RestoreAsync();}catch(IOException){restoreFailed=true;}
            Check(restoreFailed && transport.Packets.Count==1,"Restore overwrote another controller");
        });
        await TestAsync("Failed reads dispose the controller without lighting writes",async()=>
        {
            var transport=new FakeMsiTransport{BadReport=true};await using var backend=new MsiBackend(()=>transport,()=>false);
            bool failed=false;try{await backend.ConnectAsync();}catch(InvalidDataException){failed=true;}
            Check(failed && transport.Disposed && transport.Packets.Count==0,"Read failure cleanup failed");
        });
        await TestAsync("Concurrent native writes are serialized with restoration",async()=>
        {
            var transport=new FakeMsiTransport();await using var backend=new MsiBackend(()=>transport,()=>false);await backend.ConnectAsync();
            await Task.WhenAll(Enumerable.Range(0,12).Select(i=>backend.WriteAsync(new Dictionary<uint,Color[]>{{0,new[]{Color.FromRgb((byte)i,0,0)}}})));
            await backend.RestoreAsync();Check(transport.MaxConcurrent==1 && transport.Packets.Last().SequenceEqual(transport.Original),"HID operations overlapped or restore was not last");
        });
        Test("USB discovery reads interface descriptors without lighting writes",()=>
        {
            var devices=Hid.Enumerate();Check(devices.All(d=>d.VendorId>0 && d.Path.Length>0),"Invalid HID metadata");
        });
        int failures=results.Count(x=>!(bool)x.GetType().GetProperty("Passed")!.GetValue(x)!);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-results.json"),JsonSerializer.Serialize(new{Passed=results.Count-failures,Failed=failures,HardwareLightingWritesPerformed=false,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}

sealed class FakeMsiTransport : IMsiTransport
{
    public readonly byte[] Original=Enumerable.Range(0,185).Select(i=>(byte)i).ToArray();
    public readonly List<byte[]> Packets=new();
    public bool FailWrite,BadReport,Disposed;
    public int MaxConcurrent;int concurrent;
    public FakeMsiTransport(){Original[0]=0x52;Original[184]=0;}
    public byte[] Read()=>BadReport?new byte[185]:(byte[])Original.Clone();
    public void Write(byte[] bytes)
    {
        int n=Interlocked.Increment(ref concurrent);MaxConcurrent=Math.Max(n,MaxConcurrent);
        try{Thread.Sleep(2);if(FailWrite)throw new IOException("Simulated USB failure");Packets.Add((byte[])bytes.Clone());}
        finally{Interlocked.Decrement(ref concurrent);}
    }
    public void Dispose()=>Disposed=true;
}
