using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

// File sharing is enforced by Windows across sessions. The handle, rather than a
// PID file or a thread-owned mutex, is the ownership lease across async calls.
public sealed class ControllerLease : IDisposable
{
    readonly FileStream stream;
    ControllerLease(FileStream stream)=>this.stream=stream;
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PrismRGB");
    static string DirectoryFor(string? directory)=>Path.GetFullPath(directory??DefaultDirectory);
    public static ControllerLease? TryAcquire(string? directory=null)
    {
        string folder=DirectoryFor(directory);Directory.CreateDirectory(folder);
        try{return new(new FileStream(Path.Combine(folder,"controller.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
        catch(IOException ex) when((ex.HResult&0xFFFF) is 32 or 33){return null;}
    }
    public void Dispose()=>stream.Dispose();

    sealed class HandoffRecord
    {
        public HandoffRecord(){}
        public int ProcessId {get;set;}
        public long StartedUtcTicks {get;set;}
        public string Nonce {get;set;}="";
    }
    sealed class HandoffLease : IDisposable
    {
        readonly string path,nonce;bool disposed;
        public HandoffLease(string path,string nonce){this.path=path;this.nonce=nonce;}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{if(ReadHandoff(path)?.Nonce==nonce)File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    static HandoffRecord? ReadHandoff(string path)
    {
        try
        {
            if(!File.Exists(path) || new FileInfo(path).Length>4096)return null;
            return JsonSerializer.Deserialize<HandoffRecord>(File.ReadAllText(path));
        }
        catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(JsonException){return null;}
    }
    public static IDisposable RequestHandoff(string? directory=null)
    {
        string folder=DirectoryFor(directory);Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"controller-handoff.json");
        using var process=Process.GetCurrentProcess();
        var record=new HandoffRecord{ProcessId=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks,Nonce=Guid.NewGuid().ToString("N")};
        BootLighting.WriteAtomic(path,JsonSerializer.Serialize(record));return new HandoffLease(path,record.Nonce);
    }
    public static bool HandoffRequested(string? directory=null)
    {
        var record=ReadHandoff(Path.Combine(DirectoryFor(directory),"controller-handoff.json"));
        if(record is null || record.ProcessId<=0 || record.Nonce is not {Length:32})return false;
        try
        {
            using var process=Process.GetProcessById(record.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==record.StartedUtcTicks;
        }
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}
    }
}

public sealed class BootOutputFrame
{
    public uint Id {get;set;}
    public bool Enabled {get;set;}=true;
    public string[] Colors {get;set;}=Array.Empty<string>();
}

public sealed class BootSnapshot
{
    public int Version {get;set;}=1;
    public List<BootOutputFrame> Outputs {get;set;}=new();
}

public sealed class BootLightingResult
{
    public int Version {get;set;}=1;
    public int SessionId {get;set;}=Process.GetCurrentProcess().SessionId;
    public bool UserInteractive {get;set;}=Environment.UserInteractive;
    public DateTimeOffset ProcessStarted {get;set;}=Started();
    public DateTimeOffset? MotherboardAppliedAt {get;set;}
    public DateTimeOffset? GpuAppliedAt {get;set;}
    public DateTimeOffset Finished {get;set;}
    public bool MotherboardApplied {get;set;}
    public bool GpuApplied {get;set;}
    public int MotherboardAttempts {get;set;}
    public int GpuAttempts {get;set;}
    public string MotherboardError {get;set;}="";
    public string GpuError {get;set;}="";
    public string Error {get;set;}="";
    public bool OwnershipBusy {get;set;}
    public bool Handoff {get;set;}
    public bool Cancelled {get;set;}
    public bool TimedOut {get;set;}
    public int ExitCode {get;set;}
    static DateTimeOffset Started(){using var process=Process.GetCurrentProcess();return new DateTimeOffset(process.StartTime);}
}

internal sealed class BootRunOptions
{
    public Func<ILightingBackend> MotherboardFactory {get;init;}=()=>new MsiBackend();
    public Func<ILightingBackend> GpuFactory {get;init;}=()=>new GigabyteBackend();
    public Func<TimeSpan,CancellationToken,Task> Delay {get;init;}=(time,token)=>Task.Delay(time,token);
    public TimeSpan Budget {get;init;}=TimeSpan.FromSeconds(45);
    public string? LeaseDirectory {get;init;}
}

public static class BootLighting
{
    static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    const int MaxSnapshotBytes=32768;
    static readonly int[] RetrySeconds={0,2,5,10,15};
    public static string DefaultSnapshotPath=>Path.Combine(ControllerLease.DefaultDirectory,"boot","boot-frame.json");
    public static string DefaultResultPath=>Path.Combine(ControllerLease.DefaultDirectory,"boot","boot-result.json");

    internal static void WriteAtomic(string path,string text)
    {
        path=Path.GetFullPath(path);string folder=Path.GetDirectoryName(path)!;Directory.CreateDirectory(folder);
        string temporary=Path.Combine(folder,"."+Path.GetFileName(path)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {byte[] bytes=new UTF8Encoding(false).GetBytes(text);file.Write(bytes);file.Flush(true);}
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Validate(BootSnapshot snapshot)
    {
        if(snapshot is null || snapshot.Version!=1 || snapshot.Outputs is null || snapshot.Outputs.Count>5)throw new InvalidDataException("Unsupported boot lighting snapshot.");
        var seen=new HashSet<uint>();
        foreach(var output in snapshot.Outputs)
        {
            if(output is null || !seen.Add(output.Id) || output.Colors is null)throw new InvalidDataException("Boot lighting contains a missing or duplicate output.");
            bool count=output.Id switch{0 or 1=>output.Colors.Length==1,2 or 3=>output.Colors.Length is >=1 and <=120,100=>output.Colors.Length==4,_=>false};
            if(!count || output.Colors.Any(color=>!Effects.ValidColor(color)))throw new InvalidDataException("Boot lighting contains an unsupported output, LED count or color.");
        }
    }
    public static BootSnapshot LoadSnapshot(string path)
    {
        // Bound the actual read, including a file that grows after it is opened.
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(file.Length>MaxSnapshotBytes)throw new InvalidDataException("The boot lighting snapshot is too large.");
        var bytes=new byte[MaxSnapshotBytes+1];int length=0,read;
        while(length<bytes.Length && (read=file.Read(bytes,length,bytes.Length-length))>0)length+=read;
        if(length>MaxSnapshotBytes)throw new InvalidDataException("The boot lighting snapshot is too large.");
        using(var document=JsonDocument.Parse(bytes.AsMemory(0,length)))RejectDuplicateProperties(document.RootElement);
        var snapshot=JsonSerializer.Deserialize<BootSnapshot>(bytes.AsSpan(0,length),JsonOptions)??throw new InvalidDataException("The boot lighting snapshot is empty.");
        Validate(snapshot);return snapshot;
    }
    static void RejectDuplicateProperties(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject())
            {if(!names.Add(property.Name))throw new InvalidDataException("Duplicate boot snapshot property.");RejectDuplicateProperties(property.Value);}
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())RejectDuplicateProperties(item);
    }
    static DevicePlacement CopyPlacement(DevicePlacement? saved,uint id)
    {
        double Finite(double value,double fallback,double max)=>double.IsFinite(value)?Math.Clamp(value,0,max):fallback;
        var placement=new DevicePlacement
        {
            Identity=id==100?"GPU:10DE:2782:1458:40C6":$"MSI:1462:7C95:{id}",
            X=Finite(saved?.X??.1,.1,1),Y=Finite(saved?.Y??(.25+(id==100?4:id)*.15),.65,1),
            Span=Finite(saved?.Span??(id==100?.65:0),0,1),Enabled=saved?.Enabled??true,Reverse=saved?.Reverse??false,
            LedCount=id is 2 or 3?(saved?.LedCount is >=1 and <=120?saved.LedCount:60):id==100?4:1
        };
        if(id is 2 or 3 && (saved is null || saved.LedCount==0) && placement.Span<.05)placement.Span=.75;
        if(saved?.LedColors is not null)foreach(var pair in saved.LedColors)
            if(pair.Key>=0 && pair.Key<placement.LedCount && Effects.ValidColor(pair.Value))placement.LedColors[pair.Key]=pair.Value;
        return placement;
    }
    public static BootSnapshot CreateSnapshot(Preferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var state=preferences.Studio?.Clone()??new StudioState();state.Normalize();
        // No desktop exists before sign-in. Freeze the first chosen color rather
        // than attempting screen capture or storing desktop-derived information.
        if(state.Effect=="Screen ambience")
        {state.Color=Effects.Palette(state)[0];state.Effect="Solid";Effects.SetPalette(state,new[]{state.Color});}
        var snapshot=new BootSnapshot();
        foreach(uint id in new uint[]{0,1,2,3,100})
        {
            string identity=id==100?"GPU:10DE:2782:1458:40C6":$"MSI:1462:7C95:{id}";
            var placement=CopyPlacement(preferences.Placements?.FirstOrDefault(p=>p is not null && p.Identity==identity),id);
            var device=new OutputDevice{Id=id,Identity=identity,LedCount=placement.LedCount,Placement=placement};
            var colors=placement.Enabled?OutputColors.Sample(state,device,0):new Color[device.LedCount];
            snapshot.Outputs.Add(new(){Id=id,Enabled=placement.Enabled,Colors=colors.Select(ColorValues.Hex).ToArray()});
        }
        Validate(snapshot);return snapshot;
    }
    public static void SaveSnapshot(Preferences preferences,string path)
    {
        var snapshot=CreateSnapshot(preferences);string json=JsonSerializer.Serialize(snapshot,JsonOptions);
        if(Encoding.UTF8.GetByteCount(json)>MaxSnapshotBytes)throw new InvalidDataException("The boot lighting snapshot is too large.");
        WriteAtomic(path,json);
    }
    public static Task<int> RunAsync(string snapshotPath,string resultPath,CancellationToken token=default,string? leaseDirectory=null)
        =>RunAsync(snapshotPath,resultPath,new BootRunOptions{LeaseDirectory=leaseDirectory},token);
    internal static async Task<int> RunAsync(string snapshotPath,string resultPath,BootRunOptions options,CancellationToken token=default)
    {
        BootLightingResult result;
        try{result=await ApplyAsync(LoadSnapshot(snapshotPath),options,token);}
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {result=new(){Error="Boot lighting could not load its configuration: "+ex.Message,ExitCode=2};}
        result.Finished=DateTimeOffset.Now;
        try{WriteAtomic(resultPath,JsonSerializer.Serialize(result,JsonOptions));}
        catch(IOException){return result.ExitCode==0?2:result.ExitCode;}catch(UnauthorizedAccessException){return result.ExitCode==0?2:result.ExitCode;}
        return result.ExitCode;
    }
    sealed class ControllerAttempt
    {
        public readonly bool Gpu;
        public readonly Func<ILightingBackend> Factory;
        public readonly List<BootOutputFrame> Frames;
        public bool Applied,Permanent;
        public DateTimeOffset? AppliedAt;
        public int Attempts;
        public string Error="";
        public ControllerAttempt(bool gpu,Func<ILightingBackend> factory,List<BootOutputFrame> frames){Gpu=gpu;Factory=factory;Frames=frames;}
        public bool Requested=>Frames.Any(frame=>frame.Enabled);
    }
    internal static async Task<BootLightingResult> ApplyAsync(BootSnapshot snapshot,BootRunOptions options,CancellationToken token=default)
    {
        Validate(snapshot);
        if(options.Budget<=TimeSpan.Zero || options.Budget>TimeSpan.FromMinutes(2))throw new ArgumentException("Boot lighting needs a bounded startup budget.");
        var result=new BootLightingResult();
        if(ControllerLease.HandoffRequested(options.LeaseDirectory)){result.Handoff=true;return result;}
        using var lease=ControllerLease.TryAcquire(options.LeaseDirectory);
        if(lease is null){result.OwnershipBusy=true;return result;}
        var controllers=new[]{new ControllerAttempt(false,options.MotherboardFactory,snapshot.Outputs.Where(x=>x.Id<4).ToList()),new ControllerAttempt(true,options.GpuFactory,snapshot.Outputs.Where(x=>x.Id==100).ToList())};
        using var deadline=new CancellationTokenSource(options.Budget);
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,deadline.Token);
        var active=cancellation.Token;
        bool handoff=false;
        void Check()
        {
            if(ControllerLease.HandoffRequested(options.LeaseDirectory)){handoff=true;cancellation.Cancel();}
            active.ThrowIfCancellationRequested();
        }
        // This watcher exists only while this bounded helper is active. Cancellation
        // interrupts GPU retry delays; it never abandons a live native I/O call.
        async Task WatchHandoff()
        {
            try{while(true){await Task.Delay(100,active);if(ControllerLease.HandoffRequested(options.LeaseDirectory)){handoff=true;cancellation.Cancel();return;}}}
            catch(OperationCanceledException) when(active.IsCancellationRequested){}
        }
        var watcher=WatchHandoff();
        try
        {
            for(int round=0;round<RetrySeconds.Length;round++)
            {
                Check();
                if(round>0)await options.Delay(TimeSpan.FromSeconds(RetrySeconds[round]),active);
                foreach(var controller in controllers)
                {
                    Check();if(!controller.Requested || controller.Applied || controller.Permanent)continue;
                    ILightingBackend? backend=null;controller.Attempts++;
                    try
                    {
                        backend=controller.Factory();Check();await backend.ConnectAsync();Check();
                        foreach(var device in backend.Devices)
                        {
                            var frame=controller.Frames.FirstOrDefault(x=>x.Id==device.Id);
                            if(frame is not null)device.LedCount=frame.Colors.Length;
                        }
                        var colors=controller.Frames.Where(frame=>frame.Enabled).ToDictionary(frame=>frame.Id,frame=>frame.Colors.Select(Effects.Parse).ToArray());
                        // Use direct GPU delivery, never the combined backend's
                        // asynchronous queue, which disposal is allowed to cancel.
                        if(backend is GigabyteBackend gpu)await gpu.WriteAsync(colors,active);else await backend.WriteAsync(colors);
                        controller.Applied=true;controller.AppliedAt=DateTimeOffset.Now;controller.Error="";Check();
                    }
                    catch(OperationCanceledException) when(active.IsCancellationRequested){throw;}
                    catch(Exception ex)
                    {
                        controller.Error=ex.Message;
                        controller.Permanent=ex is not IOException || ex is InvalidDataException || ex is NvidiaLightingException nvidia && !nvidia.CanReconnect;
                    }
                    finally
                    {
                        // Always await completion and teardown before releasing the
                        // lease. Restore would black out the frame we just applied.
                        if(backend is not null)
                        {
                            try{await backend.DisposeAsync();}
                            catch(Exception ex){controller.Error="Controller release failed: "+ex.Message;controller.Permanent=true;}
                        }
                    }
                }
                if(controllers.All(controller=>!controller.Requested || controller.Applied || controller.Permanent))break;
            }
        }
        catch(OperationCanceledException) when(active.IsCancellationRequested)
        {result.Handoff=handoff;result.TimedOut=deadline.IsCancellationRequested && !handoff && !token.IsCancellationRequested;result.Cancelled=token.IsCancellationRequested;}
        finally{cancellation.Cancel();await watcher;}
        result.MotherboardApplied=controllers[0].Applied;result.GpuApplied=controllers[1].Applied;
        result.MotherboardAppliedAt=controllers[0].AppliedAt;result.GpuAppliedAt=controllers[1].AppliedAt;
        result.MotherboardAttempts=controllers[0].Attempts;result.GpuAttempts=controllers[1].Attempts;
        result.MotherboardError=controllers[0].Error;result.GpuError=controllers[1].Error;
        result.ExitCode=result.Handoff || result.Cancelled?0:controllers.Any(controller=>controller.Requested && !controller.Applied)?1:0;
        return result;
    }

    internal static async Task VerifyAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Reject(Action action,string message)
        {bool failed=false;try{action();}catch(Exception ex) when(ex is InvalidDataException or JsonException){failed=true;}Check(failed,message);}
        string scope=Path.Combine(directory,"leases"),snapshotPath=Path.Combine(directory,"snapshot.json"),resultPath=Path.Combine(directory,"result.json");
        var preferences=new Preferences{Studio=new(){Effect="Solid",Color="#804020",Brightness=.5},Placements=new()
        {
            new(){Identity="MSI:1462:7C95:2",LedCount=3,Reverse=true,LedColors=new(){{1,"#0000FF"}}},
            new(){Identity="MSI:1462:7C95:3",LedCount=5,Enabled=false},
            new(){Identity="GPU:10DE:2782:1458:40C6",Enabled=false,Span=.018617021276595994}
        }};
        string original=JsonSerializer.Serialize(preferences);var snapshot=CreateSnapshot(preferences);
        Check(original==JsonSerializer.Serialize(preferences),"Export mutated user settings.");
        Check(snapshot.Outputs.Single(x=>x.Id==2).Colors.SequenceEqual(new[]{"#402010","#00007F","#402010"}),"Boot LED overrides or brightness changed.");
        Check(snapshot.Outputs.Single(x=>x.Id==3).Colors.Length==5 && snapshot.Outputs.Single(x=>x.Id==3).Colors.All(x=>x=="#000000") && !snapshot.Outputs.Single(x=>x.Id==100).Enabled,"Disabled outputs or counts changed.");
        var defaults=CreateSnapshot(new());Check(defaults.Outputs.Single(x=>x.Id==2).Colors.Length==60 && defaults.Outputs.Single(x=>x.Id==3).Colors.Length==60,"Unknown ARGB counts are not 60.");
        preferences.Studio.Effect="Screen ambience";Effects.SetPalette(preferences.Studio,new[]{"#008000","#FFFFFF"});
        Check(CreateSnapshot(preferences).Outputs[0].Colors[0]=="#004000","Screen ambience did not use the first chosen boot color.");
        SaveSnapshot(preferences,snapshotPath);Check(LoadSnapshot(snapshotPath).Outputs.Count==5,"Boot snapshot did not round trip.");
        string saved=File.ReadAllText(snapshotPath);
        Reject(()=>Validate(new(){Outputs=new(){new(){Id=4,Colors=new[]{"#FFFFFF"}}}}),"Unknown boot output accepted.");
        Reject(()=>Validate(new(){Outputs=new(){new(){Id=2,Colors=Enumerable.Repeat("#FFFFFF",121).ToArray()}}}),"Oversized boot output accepted.");
        Reject(()=>Validate(new(){Outputs=new(){new(){Id=100,Colors=new[]{"#FFFFFF"}}}}),"Wrong GPU count accepted.");
        Reject(()=>Validate(new(){Outputs=new(){new(){Id=0,Colors=new[]{"bad"}}}}),"Invalid boot color accepted.");
        Reject(()=>Validate(new(){Outputs=new(){new(){Id=0,Colors=new[]{"#FFFFFF"}},new(){Id=0,Colors=new[]{"#FFFFFF"}}}}),"Duplicate boot output accepted.");
        File.WriteAllText(snapshotPath,"{\"Version\":1,\"Version\":1,\"Outputs\":[]}");Reject(()=>LoadSnapshot(snapshotPath),"Duplicate JSON properties accepted.");
        File.WriteAllText(snapshotPath,"{\"Version\":1,\"Outputs\":[],\"Command\":\"unexpected\"}");Reject(()=>LoadSnapshot(snapshotPath),"Unknown JSON properties accepted.");
        File.WriteAllText(snapshotPath,new string(' ',MaxSnapshotBytes+1));Reject(()=>LoadSnapshot(snapshotPath),"Oversized boot file accepted.");
        int opens=0;
        var noHardware=new BootRunOptions{LeaseDirectory=scope,MotherboardFactory=()=>{opens++;return new FakeBootBackend();},GpuFactory=()=>{opens++;return new FakeBootBackend();}};
        int invalid=await RunAsync(snapshotPath,resultPath,noHardware);Check(invalid==2 && opens==0,"Invalid snapshot opened hardware.");
        File.WriteAllText(snapshotPath,saved);
        using(var lease=ControllerLease.TryAcquire(scope))
        {Check(lease is not null && ControllerLease.TryAcquire(scope) is null,"Controller lease did not exclude another writer.");var busy=await ApplyAsync(defaults,noHardware);Check(busy.OwnershipBusy && opens==0,"Busy boot helper opened hardware.");}
        using(var lease=ControllerLease.TryAcquire(scope))Check(lease is not null,"Controller lease did not release.");
        using(var request=ControllerLease.RequestHandoff(scope))
        {Check(ControllerLease.HandoffRequested(scope),"Live UI handoff was missed.");var handed=await ApplyAsync(defaults,noHardware);Check(handed.Handoff && opens==0,"Handoff opened hardware.");}
        Check(!ControllerLease.HandoffRequested(scope),"Disposed handoff stayed active.");
        File.WriteAllText(Path.Combine(scope,"controller-handoff.json"),"{\"ProcessId\":2147483647,\"StartedUtcTicks\":1,\"Nonce\":\"00000000000000000000000000000000\"}");
        Check(!ControllerLease.HandoffRequested(scope),"Stale UI process blocked boot lighting.");

        var msiSuccess=new FakeBootBackend();var gpuSuccess=new FakeBootBackend();int msiAttempts=0,gpuAttempts=0;var waits=new List<int>();
        var independent=await ApplyAsync(defaults,new()
        {
            LeaseDirectory=scope,MotherboardFactory=()=>++msiAttempts==1?new FakeBootBackend{ConnectError=new IOException("MSI not ready")}:msiSuccess,
            GpuFactory=()=>++gpuAttempts<3?new FakeBootBackend{ConnectError=new IOException("GPU not ready")}:gpuSuccess,
            Delay=(time,token)=>{token.ThrowIfCancellationRequested();waits.Add((int)time.TotalSeconds);return Task.CompletedTask;}
        });
        Check(independent.MotherboardApplied && independent.GpuApplied && independent.MotherboardAttempts==2 && independent.GpuAttempts==3 && waits.SequenceEqual(new[]{2,5}),"Independent driver readiness retries failed.");
        Check(independent.MotherboardAppliedAt is not null && independent.GpuAppliedAt is not null && independent.MotherboardAppliedAt>=independent.ProcessStarted && independent.GpuAppliedAt>=independent.MotherboardAppliedAt,"Boot timing did not record successful controller delivery.");
        Check(msiSuccess.Writes==1 && gpuSuccess.Writes==1 && msiSuccess.Restores==0 && gpuSuccess.Restores==0 && msiSuccess.Disposed && gpuSuccess.Disposed,"Boot output was cleared or repeated after success.");
        Check(msiSuccess.Devices.Single(device=>device.Id==2).LedCount==60 && msiSuccess.LastColors?.Count==4,"Motherboard snapshot counts or selected outputs were not applied.");
        var failures=new List<int>();
        var exhausted=await ApplyAsync(defaults,new(){LeaseDirectory=scope,MotherboardFactory=()=>new FakeBootBackend{ConnectError=new IOException("not ready")},GpuFactory=()=>new FakeBootBackend{ConnectError=new InvalidDataException("wrong identity")},Delay=(time,token)=>{failures.Add((int)time.TotalSeconds);return Task.CompletedTask;}});
        Check(exhausted.MotherboardAttempts==5 && exhausted.GpuAttempts==1 && failures.SequenceEqual(new[]{2,5,10,15}) && exhausted.ExitCode==1,"Boot retry budget was not bounded or identity failure retried.");

        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedWrite=new FakeBootBackend{WriteGate=async()=>{entered.SetResult();await release.Task;}};
        var gpuOnly=new BootSnapshot{Outputs=new(){new(){Id=100,Colors=Enumerable.Repeat("#123456",4).ToArray()}}};
        var writing=ApplyAsync(gpuOnly,new(){LeaseDirectory=scope,GpuFactory=()=>blockedWrite});await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!writing.IsCompleted && !blockedWrite.Disposed && ControllerLease.TryAcquire(scope) is null,"Boot GPU was not awaited or ownership released during a write.");
        using(var request=ControllerLease.RequestHandoff(scope))
        {
            await Task.Delay(150);Check(!writing.IsCompleted && !blockedWrite.Disposed,"Handoff disposed an active native call.");release.SetResult();var handed=await writing;
            Check(handed.Handoff && handed.GpuApplied && blockedWrite.Disposed && blockedWrite.Restores==0,"Handoff cleared successful output or did not finish.");
        }
        using(var lease=ControllerLease.TryAcquire(scope))Check(lease is not null,"Handoff leaked its ownership lease.");
        var nativeGpu=new FakeBootGpuTransport();
        var delivered=await ApplyAsync(gpuOnly,new(){LeaseDirectory=scope,GpuFactory=()=>new GigabyteBackend(()=>nativeGpu,()=>false)});
        Check(delivered.GpuApplied && nativeGpu.Queries==1 && nativeGpu.Packets.Count==16 && nativeGpu.Disposed && nativeGpu.Packets[8].Skip(2).Take(3).SequenceEqual(new byte[]{0x12,0x34,0x56}),"Direct GPU frame was queued, not fully delivered and retained.");
        using var cancel=new CancellationTokenSource();var delayEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelling=ApplyAsync(gpuOnly,new(){LeaseDirectory=scope,GpuFactory=()=>new FakeBootBackend{ConnectError=new IOException("driver starting")},Delay=async(_,token)=>{delayEntered.SetResult();await Task.Delay(Timeout.Infinite,token);}},cancel.Token);
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();var cancelled=await cancelling.WaitAsync(TimeSpan.FromSeconds(2));
        Check(cancelled.Cancelled && cancelled.GpuAttempts==1,"Cancelled startup repeated hardware access.");
        var timedOut=await ApplyAsync(gpuOnly,new(){LeaseDirectory=scope,Budget=TimeSpan.FromMilliseconds(40),GpuFactory=()=>new FakeBootBackend{ConnectError=new IOException("driver unavailable")},Delay=(_,token)=>Task.Delay(Timeout.Infinite,token)});
        Check(timedOut.TimedOut && timedOut.GpuAttempts==1 && timedOut.ExitCode==1,"Boot startup deadline did not cancel readiness retries.");
        var disabled=new BootSnapshot{Outputs=new(){new(){Id=2,Enabled=false,Colors=new[]{"#000000"}},new(){Id=100,Enabled=false,Colors=Enumerable.Repeat("#000000",4).ToArray()}}};
        var skipped=await ApplyAsync(disabled,noHardware);Check(opens==0 && skipped.ExitCode==0,"Disabled lighting opened hardware.");
        var partial=await ApplyAsync(snapshot,new(){LeaseDirectory=scope,MotherboardFactory=()=>msiSuccess=new FakeBootBackend(),GpuFactory=()=>throw new InvalidOperationException("Disabled GPU must not open")});
        Check(partial.MotherboardApplied && !partial.GpuApplied && msiSuccess.Devices.Single(device=>device.Id==3).LedCount==5 && msiSuccess.LastColors is not null && !msiSuccess.LastColors.ContainsKey(3),"Disabled header count was lost or sent as an enabled output.");
    }

    sealed class FakeBootBackend : ILightingBackend
    {
        public Exception? ConnectError;
        public Func<Task>? WriteGate;
        public int Writes,Restores;
        public bool Disposed;
        public IReadOnlyDictionary<uint,Color[]>? LastColors;
        public List<OutputDevice> Devices {get;}=new uint[]{0,1,2,3,100}.Select(id=>new OutputDevice{Id=id,LedCount=id<2?1:id==100?4:1}).ToList();
        public string Label=>"Boot test";
        public Task ConnectAsync(){if(ConnectError is not null)throw ConnectError;return Task.CompletedTask;}
        public async Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors){if(WriteGate is not null)await WriteGate();LastColors=colors;Writes++;}
        public Task RestoreAsync(){Restores++;return Task.CompletedTask;}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    sealed class FakeBootGpuTransport : IGpuLightingTransport
    {
        public int Queries;
        public bool Disposed;
        public List<byte[]> Packets {get;}=new();
        public byte[] QueryIdentity(){Queries++;return new byte[]{0xAB,0x11,0x52,1};}
        public void Write(byte[] packet){if(Disposed)throw new ObjectDisposedException(nameof(FakeBootGpuTransport));GigabyteProtocol.Validate(packet);Packets.Add((byte[])packet.Clone());}
        public void Dispose()=>Disposed=true;
    }
}
