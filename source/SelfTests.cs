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
        Test("All seven effects accept a custom color and obey brightness",()=>
        {
            foreach(var name in Effects.Names)
            {
                var state=new StudioState{Effect=name,Brightness=1};Effects.SetPalette(state,new[]{"#0000FF"});
                var color=Effects.Sample(state,.4,.2,.8,Colors.White);
                Check(color.R==0 && color.G==0 && color.B>0,name+" ignored the custom palette");
                state.Brightness=0;Check(Effects.Sample(state,.4,.2,.8,Colors.White)==Colors.Black,name+" ignored brightness");
            }
            var screen=new StudioState{Effect="Screen ambience",Brightness=1};Effects.SetPalette(screen,new[]{"#FFFFFF","#FF0000"});
            Check(Effects.Sample(screen,0,0,0,Colors.Black)==Colors.Black,"Custom screen palette lit a black screen");
        });
        Test("Custom gradients loop continuously and refresh cached colors",()=>
        {
            var stops=new[]{Colors.Red,Colors.Blue,Colors.Lime};
            Check(Effects.PaletteAt(stops,0)==Colors.Red && Effects.PaletteAt(stops,1)==Colors.Lime,"Gradient endpoints changed");
            Check(Effects.PaletteAt(stops,.25)==Color.FromRgb(127,0,127),"Gradient interpolation changed");
            Check(Effects.PaletteAt(stops,1,true)==Effects.PaletteAt(stops,0,true) && Effects.PaletteAt(stops,-.25,true)==Effects.PaletteAt(stops,.75,true),"Loop seam or negative phase failed");
            var state=new StudioState{Effect="Rainbow",Brightness=1};Effects.SetPalette(state,new[]{"#FF0000"});
            Check(Effects.Sample(state,0,0,0)==Colors.Red,"Initial palette failed");Effects.SetPalette(state,new[]{"#0000FF"});
            Check(Effects.Sample(state,0,0,0)==Colors.Blue,"Edited palette used stale parsed colors");
            state.Effect="Ember";Effects.SetPalette(state,new[]{"#00FF00"});state.Effect="Rainbow";
            Check(Effects.Sample(state,0,0,0)==Colors.Blue,"Changing effects overwrote a different palette");
        });
        Test("Custom palettes normalize, clone and survive profile export",()=>
        {
            var state=new StudioState{Effect="Wave"};Effects.SetPalette(state,new[]{"#123456","#ABCDEF","#22AAEE"});
            var clone=state.Clone();clone.EffectPalettes["Wave"][0]="#FFFFFF";
            Check(state.EffectPalettes["Wave"][0]=="#123456","Cloning shared palette lists");
            string dir=Path.Combine(Directory.GetCurrentDirectory(),"work","palette-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string path=Path.Combine(dir,"profile.json");Storage.Export(new(){Name="Custom",State=state},path);
            Check(Storage.Import(path).State.EffectPalettes["Wave"].SequenceEqual(state.EffectPalettes["Wave"]),"Profile lost palette stops");
            state.EffectPalettes["Rainbow"]=Enumerable.Repeat("#FFFFFF",20).ToList();state.EffectPalettes["Ember"]=new(){"bad"};state.EffectPalettes["unknown"]=new(){"#000000"};state.EffectPalettes["Solid"]=null!;state.Normalize();
            Check(state.EffectPalettes["Rainbow"].Count==8 && !state.EffectPalettes.ContainsKey("Ember") && !state.EffectPalettes.ContainsKey("unknown") && !state.EffectPalettes.ContainsKey("Solid"),"Malformed palettes survived normalization");
            bool rejected=false;try{Effects.SetPalette(state,new[]{"bad"});}catch(ArgumentException){rejected=true;}Check(rejected,"Malformed edited colors accepted");
        });
        Test("Screen color coordinates retain negative monitor origins and reject outside pixels",()=>
        {
            using var bitmap=new System.Drawing.Bitmap(2,2);bitmap.SetPixel(0,0,System.Drawing.Color.Red);bitmap.SetPixel(1,1,System.Drawing.Color.Blue);
            using var image=new ScreenEyedropper.ScreenImage(new System.Drawing.Rectangle(-1920,-120,2,2),bitmap);
            Check(image.Sample(-1920,-120)==Colors.Red && image.Sample(-1919,-119)==Colors.Blue,"Physical pixel mapping failed");
            Check(image.Sample(-1921,-120) is null && image.Sample(-1918,-119) is null,"Outside display pixel accepted");
        });
        Test("Color editor converts HSV and accepts only complete RGB hex colors",()=>
        {
            foreach(var color in new[]{Colors.Red,Colors.Lime,Colors.Blue,Colors.Black,Colors.White,Color.FromRgb(34,170,238)})
            {
                var hsv=ColorValues.Hsv(color);var round=Effects.Hsv(hsv.Hue,hsv.Saturation,hsv.Value);
                Check(Math.Abs(round.R-color.R)<=1 && Math.Abs(round.G-color.G)<=1 && Math.Abs(round.B-color.B)<=1,"HSV conversion drifted");
                Check(ColorValues.TryHex(ColorValues.Hex(color),out var parsed) && parsed==color,"Hex round-trip failed");
            }
            Check(ColorValues.TryHex(" 22aaee ",out var selected) && selected==Color.FromRgb(34,170,238),"Hex shorthand input failed");
            Check(!ColorValues.TryHex("#AABBCCDD",out _) && !ColorValues.TryHex("#12",out _),"Incomplete or alpha hex accepted");
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
            var d=new HidDevice("fixture",0x1462,0x7C95,"MSI",725,1,0,185,725);
            Check(MsiProtocol.Supports(MsiProtocol.Board,d),"Target rejected");
            Check(!MsiProtocol.Supports("Other board",d),"Different board accepted");
            Check(!MsiProtocol.Supports(MsiProtocol.Board,d with {Report52Length=725}),"Different report size accepted");
            Check(!MsiProtocol.Supports(MsiProtocol.Board,d with {Report53Length=0}),"Missing per-LED descriptor accepted");
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
            await backend.WriteAsync(colors);await backend.WriteAsync(colors);Check(transport.Packets.Count==2,"Expected configuration and one color packet without duplicates");
            await backend.RestoreAsync();Check(transport.Packets.Count==4 && transport.Packets[2].Skip(5).All(b=>b==0) && transport.Packets[3].SequenceEqual(transport.Original),"Pixel clearing or configuration restoration failed");
            await backend.WriteAsync(colors);Check(transport.Packets.Count==6,"Restart did not reapply configuration and pixels");await backend.RestoreAsync();
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
            transport.BadReport=false;await backend.RestoreAsync();Check(transport.Packets.Count==4 && transport.Packets.Last().SequenceEqual(transport.Original) && !backend.NeedsRestore,"Failed write was not restored");
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
            Check(restoreFailed && transport.Packets.Count==2,"Restore overwrote another controller");
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
        Test("ARGB packets address independent ports and preserve RGB ordering and boundaries",()=>
        {
            var colors=Enumerable.Repeat(Colors.Black,120).ToArray();colors[0]=Color.FromRgb(1,2,3);colors[119]=Color.FromRgb(4,5,6);
            var packet=MsiProtocol.DirectFrame(1,colors);MsiProtocol.ValidateWrite(packet);
            Check(packet.Length==725 && packet.Take(5).SequenceEqual(new byte[]{0x53,0x25,4,1,0}),"Incorrect port packet");
            Check(packet.Skip(5).Take(3).SequenceEqual(new byte[]{1,2,3}) && packet.Skip(362).Take(3).SequenceEqual(new byte[]{4,5,6}),"Pixel order or final boundary incorrect");
            Check(packet.Skip(365).All(b=>b==0),"Trailing bytes contain commands or extra pixels");
        });
        Test("Compact routing includes the controller-wide selector and preserves unrelated fields",()=>
        {
            var original=new FakeMsiTransport().Original;
            var packet=MsiProtocol.DirectConfiguration(original,new Dictionary<uint,Color[]>{{3,new Color[60]}});
            Check(packet[42]==1 && packet[46]==0x29 && packet[50]==0x80 && packet[52]==60 && packet[74]==0x25 && packet[78]==0xA9 && packet[82]==0xBF && packet[184]==0,"Missing compact controller routing or length");
            var changed=new HashSet<int>{1,9,174,182,31,39,42,50,5,178,35,46,41,52,74,78,82,184};
            for(int i=0;i<185;i++)if(!changed.Contains(i))Check(packet[i]==original[i],"Unrelated byte changed: "+i);
        });
        Test("ARGB rejects oversized chains, invalid ports and unsupported commands",()=>
        {
            int rejected=0;
            foreach(var data in new[]{Array.Empty<Color>(),new Color[121]})try{MsiProtocol.DirectFrame(0,data);}catch(InvalidDataException){rejected++;}
            try{MsiProtocol.DirectFrame(2,new Color[1]);}catch(InvalidDataException){rejected++;}
            var packet=MsiProtocol.DirectFrame(0,new Color[1]);packet[2]=0xFF;
            try{MsiProtocol.ValidateWrite(packet);}catch(InvalidDataException){rejected++;}
            Check(rejected==4,"Unsupported direct data accepted");
        });
        Test("Per-LED effects, reverse order and explicit pixel colors obey brightness",()=>
        {
            var scene=new StudioState{Effect="Rainbow",Brightness=.5};var d=new OutputDevice{LedCount=4,Placement=new(){X=.1,Span=.7}};
            var normal=OutputColors.Sample(scene,d,0);d.Placement.Reverse=true;var reversed=OutputColors.Sample(scene,d,0);
            Check(normal.Distinct().Count()==4 && normal.SequenceEqual(reversed.Reverse()),"Pixels are not independently sampled or reversed");
            d.Placement.LedColors[1]="#804020";Check(OutputColors.Sample(scene,d,0)[1]==Color.FromRgb(64,32,16),"Physical pixel override or brightness incorrect");
        });
        await TestAsync("Changing ARGB counts reapplies routing and unselected output pixels stay black",async()=>
        {
            var transport=new FakeMsiTransport();await using var backend=new MsiBackend(()=>transport,()=>false);await backend.ConnectAsync();
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{2,new[]{Colors.Red,Colors.Green}}});
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{2,new[]{Colors.Red}}});
            Check(transport.Packets.Count==4 && transport.Packets[2][41]==1 && transport.Packets[3].Skip(14).All(b=>b==0),"Count not updated or disabled port has non-black pixels");
            await backend.RestoreAsync();
        });
        Test("Compact frame orders analog headers before both ARGB chains",()=>
        {
            var packet=MsiProtocol.CompactFrame(new[]{Colors.Red,Colors.Green},new[]{Colors.Blue},Colors.White,Colors.Black);MsiProtocol.ValidateWrite(packet);
            Check(packet.Take(5).SequenceEqual(new byte[]{0x53,0x25,6,0,0}),"Incorrect compact prefix");
            Check(packet.Skip(5).Take(15).SequenceEqual(new byte[]{255,255,255,0,0,0,255,0,0,0,128,0,0,0,255}),"Wrong analog/ARGB pixel offsets");
        });
        Test("High capacity routing switches the global selector with both ARGB ports",()=>
        {
            var packet=MsiProtocol.StreamingConfiguration(new FakeMsiTransport().Original,120,120);
            Check(packet[31]==0x25 && packet[42]==0x25 && packet[41]==120 && packet[52]==120 && packet[82]==0xB1 && packet[184]==0,"Incomplete high-capacity routing");
        });
        Test("Gigabyte GPU identity and packet allowlist reject other hardware and commands",()=>
        {
            Check(GigabyteProtocol.Supports(0x278210DE,0x40C61458) && !GigabyteProtocol.Supports(0x278210DE,0x99991458),"GPU whitelist failed");
            Check(GigabyteProtocol.Identity(new byte[]{0xAB,0x11,0x52,1}) && !GigabyteProtocol.Identity(new byte[]{0xAB,0,0,0}),"GPU signature validation failed");
            int rejected=0;
            try{GigabyteProtocol.Mode(8);}catch(InvalidDataException){rejected++;}
            try{GigabyteProtocol.Validate(new byte[9]);}catch(InvalidDataException){rejected++;}
            try{GigabyteProtocol.Validate(new byte[]{0xFF,1,0,0,0,0,0,0});}catch(InvalidDataException){rejected++;}
            Check(rejected==3,"Unknown GPU packet accepted");
        });
        Test("Gigabyte RGB colors map to three fans and the separate logo using eight-byte writes",()=>
        {
            var packets=GigabyteProtocol.Frame(new[]{Color.FromRgb(1,2,3),Color.FromRgb(4,5,6),Color.FromRgb(7,8,9),Color.FromRgb(10,11,12)});
            Check(packets.Count==8 && packets.All(p=>p.Length==8),"Incorrect write size/count");
            foreach(var packet in packets)GigabyteProtocol.Validate(packet);
            Check(packets[0].SequenceEqual(new byte[]{0xB0,1,1,2,3,4,5,6}) && packets[1].Take(5).SequenceEqual(new byte[]{0xB1,1,7,8,9}) && packets[4].Take(5).SequenceEqual(new byte[]{0xBC,1,10,11,12}),"GPU fan/logo mapping failed");
        });
        await TestAsync("GPU connection does not set colors; duplicate frames sleep and Stop clears LEDs",async()=>
        {
            var transport=new FakeGpuTransport();await using var backend=new GigabyteBackend(()=>transport,()=>false);await backend.ConnectAsync();
            Check(transport.Queries==1 && transport.Packets.Count==0 && backend.Devices[0].LedCount==4,"Connect changed GPU colors");
            var colors=new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(Colors.Red,4).ToArray()}};
            await backend.WriteAsync(colors);await backend.WriteAsync(colors);Check(transport.Packets.Count==16,"GPU duplicate frame or missing initialization");
            await backend.RestoreAsync();Check(transport.Packets.Count==19 && transport.Packets.TakeLast(3).All(p=>p.Skip(2).All(b=>b==0)) && !backend.NeedsClear,"GPU cleanup failed");
        });
        await TestAsync("Invalid GPU identity is disposed before mode or color writes",async()=>
        {
            var transport=new FakeGpuTransport{BadIdentity=true};await using var backend=new GigabyteBackend(()=>transport,()=>false);bool failed=false;
            try{await backend.ConnectAsync();}catch(IOException){failed=true;}
            Check(failed && transport.Disposed && transport.Packets.Count==0,"Invalid GPU received lighting commands");
        });
        await TestAsync("GPU coalescing drains before Stop and never relights after cleanup",async()=>
        {
            var usb=new FakeMsiTransport();var gpu=new FakeGpuTransport{Delay=2};
            await using var backend=new NativeLightingBackend(new MsiBackend(()=>usb,()=>false),()=>new GigabyteBackend(()=>gpu,()=>false));await backend.ConnectAsync();
            for(int i=0;i<20;i++)await backend.WriteAsync(new Dictionary<uint,Color[]>{{2,new[]{Colors.Green,Colors.Blue}},{100,Enumerable.Repeat(Color.FromRgb((byte)i,0,0),4).ToArray()}});
            await backend.RestoreAsync();int sent=gpu.Packets.Count;await Task.Delay(150);
            Check(gpu.Packets.Count==sent && !backend.NeedsRestore && usb.Packets.Last().SequenceEqual(usb.Original),"A pending frame relit a device after Stop");
        });
        await TestAsync("A queued GPU failure raises an error even when a static scene stops its timer",async()=>
        {
            var gpu=new FakeGpuTransport{Fail=true};var usb=new FakeMsiTransport();
            await using var backend=new NativeLightingBackend(new MsiBackend(()=>usb,()=>false),()=>new GigabyteBackend(()=>gpu,()=>false));await backend.ConnectAsync();
            var fault=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);backend.OutputFaulted+=_=>fault.TrySetResult();
            await backend.WriteAsync(new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(Colors.Blue,4).ToArray()}});
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));gpu.Fail=false;await backend.RestoreAsync();
            Check(!backend.NeedsRestore,"Failed queued GPU frame was not cleaned up");
        });
        await TestAsync("Hardware frame timer stops all callbacks and can restart without hardware writes",async()=>
        {
            int callbacks=0;using var clock=new HardwareFrameClock();
            var ticked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clock.Set(true,()=>{Interlocked.Increment(ref callbacks);ticked.TrySetResult();});
            await ticked.Task.WaitAsync(TimeSpan.FromSeconds(2));clock.Set(false,()=>{});int stopped=callbacks;
            await Task.Delay(100);Check(callbacks==stopped && !clock.Active,"Idle frame timer continued waking");
            var resumed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clock.Set(true,()=>resumed.TrySetResult());await resumed.Task.WaitAsync(TimeSpan.FromSeconds(2));clock.Set(false,()=>{});
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

sealed class FakeGpuTransport : IGpuLightingTransport
{
    public List<byte[]> Packets {get;}=new();
    public bool BadIdentity,Disposed,Fail;
    public int Queries,Delay;
    public byte[] QueryIdentity(){Queries++;return BadIdentity?new byte[4]:new byte[]{0xAB,0x11,0x52,1};}
    public void Write(byte[] packet){GigabyteProtocol.Validate(packet);if(Fail)throw new IOException("Simulated GPU I/O failure");if(Delay>0)Thread.Sleep(Delay);Packets.Add((byte[])packet.Clone());}
    public void Dispose()=>Disposed=true;
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
