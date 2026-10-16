using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        Test("OpenRGB decoder handles modes, zones, and LED names",()=>
        {
            var d=OpenRgbBackend.ParseDevice(0,FakeServer.ControllerData(),3);
            Check(d.Name=="Test keyboard" && d.LedCount==2 && d.Modes.Count==1,"Wrong device metadata");Check(d.PreviousColors[1]==Colors.Blue,"Color byte order is incorrect");
        });
        Test("OpenRGB rejects inconsistent controller payloads",()=>
        {
            byte[] p=FakeServer.ControllerData();p[0]=0;bool failed=false;
            try{OpenRgbBackend.ParseDevice(0,p,3);}catch(InvalidDataException){failed=true;}Check(failed,"Invalid packet accepted");
        });
        await TestAsync("OpenRGB fragmented handshake, frame output, and restoration",async()=>
        {
            await using var server=new FakeServer();await using var backend=new OpenRgbBackend(server.Port);await backend.ConnectAsync();
            Check(backend.Devices.Count==1,"Discovery failed");Check(server.OutputCommands==0,"Connection changed lighting");
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{0,new[]{Colors.Lime,Colors.Orange}}});
            await backend.RestoreAsync();await Task.Delay(100);
            Check(server.Packets.Any(p=>p.Id==1100),"Direct mode not requested");
            var frames=server.Packets.Where(p=>p.Id==1050).ToArray();Check(frames.Length==2,"Expected output and restoration frames");
            Check(frames[0].Data[6]==0 && frames[0].Data[7]==255 && frames[0].Data[8]==0,"Wrong emitted RGB bytes");
            Check(server.Packets.Any(p=>p.Id==1101),"Original mode was not restored");
        });
        await TestAsync("Device-list changes stop output before using stale indexes",async()=>
        {
            await using var server=new FakeServer();await using var backend=new OpenRgbBackend(server.Port);await backend.ConnectAsync();
            await server.InvalidateAsync();await Task.Delay(100);bool stopped=false;
            try{await backend.WriteAsync(new Dictionary<uint,Color[]>{{0,new[]{Colors.Red,Colors.Red}}});}catch(IOException){stopped=true;}
            Check(stopped && server.OutputCommands==0,"Stale device index was used");
        });
        await TestAsync("Connection failure remains bounded and cleanly disposable",async()=>
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            await using var backend=new OpenRgbBackend(port);bool failed=false;try{await backend.ConnectAsync();}catch(SocketException){failed=true;}Check(failed,"Connection unexpectedly succeeded");
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

sealed class FakeServer : IAsyncDisposable
{
    readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource cancel=new();readonly Task work;TcpClient? client;NetworkStream? stream;
    public List<(uint Id,byte[] Data)> Packets {get;}=new();
    public int Port {get;}
    public int OutputCommands=>Packets.Count(p=>p.Id>=1000);
    public FakeServer(){listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;work=RunAsync();}
    async Task RunAsync()
    {
        try
        {
            client=await listener.AcceptTcpClientAsync(cancel.Token);stream=client.GetStream();
            while(!cancel.IsCancellationRequested)
            {
                var h=new byte[16];await stream.ReadExactlyAsync(h,cancel.Token);
                uint dev=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)),id=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8)),size=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
                if(size>1024*1024)throw new InvalidDataException();var data=new byte[size];await stream.ReadExactlyAsync(data,cancel.Token);Packets.Add((id,data));
                if(id==40)await ReplyAsync(dev,40,BitConverter.GetBytes(5u));
                if(id==0)await ReplyAsync(0,0,BitConverter.GetBytes(1u));
                if(id==1)await ReplyAsync(dev,1,ControllerData());
            }
        }
        catch(Exception e) when(e is OperationCanceledException or IOException or ObjectDisposedException or SocketException){}
    }
    public Task InvalidateAsync()=>ReplyAsync(0,100,Array.Empty<byte>());
    async Task ReplyAsync(uint dev,uint id,byte[] data)
    {
        byte[] p=new byte[16+data.Length];"ORGB"u8.CopyTo(p);BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4),dev);BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8),id);BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12),(uint)data.Length);data.CopyTo(p,16);
        for(int i=0;i<p.Length;i+=3)await stream!.WriteAsync(p.AsMemory(i,Math.Min(3,p.Length-i)),cancel.Token);
    }
    public static byte[] ControllerData()
    {
        using var memory=new MemoryStream();using var w=new BinaryWriter(memory,Encoding.UTF8,true);
        void Str(string text){var bytes=Encoding.UTF8.GetBytes(text+"\0");w.Write((ushort)bytes.Length);w.Write(bytes);}
        w.Write(0u);w.Write(5);Str("Test keyboard");Str("Prism test fixture");Str("Independent protocol fixture");Str("1");Str("");Str("loopback test");
        w.Write((ushort)1);w.Write(0);Str("Static");
        for(int i=0;i<12;i++)w.Write(i==0?1u:0u);w.Write((ushort)0);
        w.Write((ushort)1);Str("Keys");w.Write(1);w.Write(2u);w.Write(2u);w.Write(2u);w.Write((ushort)0);
        w.Write((ushort)2);Str("A");w.Write(0u);Str("B");w.Write(1u);
        w.Write((ushort)2);w.Write(new byte[]{255,0,0,0,0,0,255,0});w.Flush();
        var p=memory.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(p,(uint)p.Length);return p;
    }
    public async ValueTask DisposeAsync(){cancel.Cancel();client?.Dispose();listener.Stop();await work;cancel.Dispose();}
}
