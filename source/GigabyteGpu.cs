using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public static class GigabyteProtocol
{
    public static bool Supports(uint device,uint subsystem)=>device==0x278210DE && subsystem==0x40C61458;
    public static bool Identity(byte[] data)=>data.Length==4 && data[0]==0xAB && data[1] is 0x10 or 0x11 or 0x12 or 0x14;
    public static byte[] Mode(int zone)
    {
        if(zone<0 || zone>7)throw new InvalidDataException("Unsupported GPU lighting zone.");
        return new byte[]{0x88,1,6,0x63,8,(byte)zone,0,0};
    }
    public static List<byte[]> Frame(Color[] colors)
    {
        if(colors.Length!=4)throw new InvalidDataException("This GPU exposes three fan colors and one logo color.");
        var result=new List<byte[]>();
        foreach(int zone in new[]{0,3})
        {
            var payload=new byte[24];int count=zone==0?3:1;
            for(int i=0;i<count;i++)
            {
                var c=colors[zone==0?i:3];payload[i*3]=c.R;payload[i*3+1]=c.G;payload[i*3+2]=c.B;
            }
            for(int part=0;part<4;part++)
            {
                var packet=new byte[8];packet[0]=(byte)(0xB0+zone*4+part);packet[1]=1;
                payload.AsSpan(part*6,6).CopyTo(packet.AsSpan(2));result.Add(packet);
            }
        }
        return result;
    }
    public static void Validate(byte[] packet)
    {
        if(packet.Length!=8)throw new InvalidDataException("Gigabyte lighting writes must be exactly eight bytes.");
        bool mode=packet[0]==0x88 && packet[1]==1 && packet[2]==6 && packet[3]==0x63 && packet[4]==8 && packet[5]<8 && packet[6]==0 && packet[7]==0;
        bool color=(packet[0] is >=0xB0 and <=0xB3 or >=0xBC and <=0xBF) && packet[1]==1;
        if(!mode && !color)throw new InvalidDataException("Unsupported Gigabyte lighting command.");
    }
}

public interface IGpuLightingTransport : IDisposable
{
    byte[] QueryIdentity();
    void Write(byte[] packet);
}

public sealed class NvidiaGigabyteTransport : IGpuLightingTransport
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Simple();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Enumerate([Out] IntPtr[] handles,out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Pci(IntPtr gpu,out uint device,out uint subsystem,out uint revision,out uint extended);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int I2c(IntPtr gpu,ref I2cInfo info,[In,Out] uint[] unknown);
    [StructLayout(LayoutKind.Explicit,Size=64)] struct I2cInfo
    {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4)] public uint DisplayMask;
        [FieldOffset(8)] public byte Ddc;
        [FieldOffset(9)] public byte Address;
        [FieldOffset(16)] public IntPtr Register;
        [FieldOffset(24)] public uint RegisterSize;
        [FieldOffset(32)] public IntPtr Data;
        [FieldOffset(40)] public uint Size;
        [FieldOffset(44)] public uint DeprecatedSpeed;
        [FieldOffset(48)] public uint Speed;
        [FieldOffset(52)] public byte Port;
        [FieldOffset(56)] public uint PortSet;
    }
    readonly IntPtr library;
    readonly Query query;
    readonly Simple unload;
    readonly I2c read,write;
    IntPtr gpu;bool initialized,disposed;
    T Bind<T>(uint id) where T:Delegate
    {
        var pointer=query(id);if(pointer==IntPtr.Zero)throw new IOException("The NVIDIA driver does not expose the required lighting I/O entry point.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    public NvidiaGigabyteTransport()
    {
        if(IntPtr.Size!=8)throw new PlatformNotSupportedException("GPU lighting requires the Windows x64 build.");
        library=NativeLibrary.Load(Path.Combine(Environment.SystemDirectory,"nvapi64.dll"));
        try
        {
            query=Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library,"nvapi_QueryInterface"));
            unload=Bind<Simple>(0xD22BDD7E);
            if(Bind<Simple>(0x0150E828)()!=0)throw new IOException("Cannot initialize the installed NVIDIA driver API.");
            initialized=true;
            var handles=new IntPtr[64];
            if(Bind<Enumerate>(0xE5AC921F)(handles,out uint count)!=0 || count>64)throw new IOException("Cannot enumerate NVIDIA graphics cards.");
            var ids=Bind<Pci>(0x2DDFB66E);
            foreach(var handle in handles.Take((int)count))
                if(ids(handle,out var device,out var subsystem,out _,out _)==0 && GigabyteProtocol.Supports(device,subsystem)){gpu=handle;break;}
            if(gpu==IntPtr.Zero)throw new IOException("Supported Gigabyte RTX 4070 Ti Gaming OC (2782 / 40C6:1458) was not found.");
            read=Bind<I2c>(0x4D7B0709);write=Bind<I2c>(0x283AC65A);
        }
        catch{if(initialized)unload!();NativeLibrary.Free(library);throw;}
    }
    void Transfer(byte[] bytes,bool reading)
    {
        if(disposed)throw new ObjectDisposedException(nameof(NvidiaGigabyteTransport));
        var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try
        {
            var info=new I2cInfo{Version=0x30040,Address=0xE2,Data=pinned.AddrOfPinnedObject(),Size=(uint)bytes.Length,DeprecatedSpeed=0xFFFF,Speed=4,Port=1,PortSet=1};
            int status=(reading?read:write)(gpu,ref info,new uint[]{1,0});
            if(status!=0)throw new IOException($"NVIDIA GPU lighting I/O failed ({status}). Close RGB Fusion and other lighting tools, then reconnect.");
        }
        finally{pinned.Free();}
    }
    public byte[] QueryIdentity()
    {
        Transfer(new byte[]{0xAB,0,0,0,0,0,0,0},false);
        var response=new byte[4];Transfer(response,true);return response;
    }
    public void Write(byte[] packet){GigabyteProtocol.Validate(packet);Transfer(packet,false);}
    public void Dispose(){if(disposed)return;disposed=true;if(initialized)unload();NativeLibrary.Free(library);}
}

public sealed class GigabyteBackend : ILightingBackend
{
    readonly Func<IGpuLightingTransport> open;readonly Func<bool> conflict;readonly SemaphoreSlim gate=new(1,1);
    IGpuLightingTransport? transport;Color[]? previous;readonly Dictionary<byte,byte[]> packetCache=new();bool wrote,faulted,disposed;
    public int Writes {get;private set;}
    public bool NeedsClear=>wrote;
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"Gigabyte RTX 4070 Ti · fan and logo lighting";
    public GigabyteBackend():this(()=>new NvidiaGigabyteTransport(),Hid.ConflictRunning){}
    internal GigabyteBackend(Func<IGpuLightingTransport> open,Func<bool> conflict){this.open=open;this.conflict=conflict;}
    async Task Locked(Action action)
    {
        await gate.WaitAsync();try{if(disposed)throw new ObjectDisposedException(nameof(GigabyteBackend));await Task.Run(action);}finally{gate.Release();}
    }
    void Send(byte[] packet){transport!.Write(packet);Writes++;}
    public Task ConnectAsync()=>Locked(()=>
    {
        if(conflict())throw new IOException("Close competing RGB software before connecting the GPU.");
        var candidate=open();
        try
        {
            if(!GigabyteProtocol.Identity(candidate.QueryIdentity()))throw new IOException("GPU lighting identity verification failed; no mode/color commands were sent.");
            transport=candidate;
            Devices.Add(new(){Id=100,Name="Gigabyte RTX 4070 Ti · fans + logo",Identity="GPU:10DE:2782:1458:40C6",LedCount=4,PixelNames=new[]{"Fan 1","Fan 2","Fan 3","Logo"}});
        }
        catch{candidate.Dispose();throw;}
    });
    void WriteColors(Color[] colors)
    {
        if(!wrote)
        {
            wrote=true;
            for(int zone=0;zone<8;zone++)Send(GigabyteProtocol.Mode(zone));
        }
        foreach(var packet in GigabyteProtocol.Frame(colors))
        {
            if(packetCache.TryGetValue(packet[0],out var cached) && packet.AsSpan().SequenceEqual(cached))continue;
            Send(packet);packetCache[packet[0]]=packet;
        }
    }
    public Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)=>Locked(()=>
    {
        if(!colors.TryGetValue(100,out var values))return;
        if(values.Length!=4)throw new InvalidDataException("The Gigabyte card requires four lighting colors.");
        if(faulted || transport is null)throw new IOException("GPU lighting is unavailable. Reconnect to retry.");
        if(previous is not null && values.AsSpan().SequenceEqual(previous))return;
        try{WriteColors(values);previous=(Color[])values.Clone();}catch{faulted=true;throw;}
    });
    public Task RestoreAsync()=>Locked(()=>
    {
        if(!wrote)return;
        if(conflict())throw new IOException("Another RGB app owns the GPU; clearing was skipped.");
        WriteColors(new Color[4]);previous=null;wrote=false;packetCache.Clear();
    });
    public async ValueTask DisposeAsync(){await gate.WaitAsync();try{if(disposed)return;disposed=true;transport?.Dispose();transport=null;}finally{gate.Release();}}
}

public sealed class NativeLightingBackend : ILightingBackend
{
    readonly MsiBackend msi;readonly Func<GigabyteBackend> openGpu;GigabyteBackend? gpu;
    readonly object gpuSync=new();Task gpuWork=Task.CompletedTask;Color[]? pendingGpu;Exception? gpuFailure;bool gpuRunning,gpuStopping;
    readonly Stopwatch gpuClock=Stopwatch.StartNew();double lastGpu=-1;
    public NativeLightingBackend():this(new MsiBackend(),()=>new GigabyteBackend()){}
    internal NativeLightingBackend(MsiBackend msi,Func<GigabyteBackend> openGpu){this.msi=msi;this.openGpu=openGpu;}
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>gpu is null?msi.Label:"MSI motherboard + Gigabyte GPU";
    public string GpuStatus {get;private set;}="";
    public int UsbWrites=>msi.Writes;
    public int I2cWrites=>gpu?.Writes??0;
    public bool NeedsRestore=>msi.NeedsRestore || gpu?.NeedsClear==true;
    public event Action<Exception>? OutputFaulted;
    public async Task ConnectAsync()
    {
        await msi.ConnectAsync();Devices.AddRange(msi.Devices);
        var candidate=openGpu();
        try{await candidate.ConnectAsync();gpu=candidate;Devices.AddRange(candidate.Devices);GpuStatus="GPU lighting is ready.";}
        catch(Exception ex){await candidate.DisposeAsync();GpuStatus="GPU unavailable: "+ex.Message;}
    }
    public async Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)
    {
        lock(gpuSync)if(gpuFailure is not null)throw new IOException("GPU output stopped. Reconnect to retry.",gpuFailure);
        await msi.WriteAsync(colors.Where(x=>x.Key<4).ToDictionary(x=>x.Key,x=>x.Value));
        if(gpu is not null && colors.TryGetValue(100,out var values))
        {
            lock(gpuSync)
            {
                gpuStopping=false;pendingGpu=(Color[])values.Clone();
                if(!gpuRunning){gpuRunning=true;gpuWork=Task.Run(PumpGpuAsync);}
            }
        }
    }
    async Task PumpGpuAsync()
    {
        try
        {
            while(true)
            {
                double wait=1.0/15-(gpuClock.Elapsed.TotalSeconds-lastGpu);
                if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait));
                Color[]? values;
                lock(gpuSync)
                {
                    if(gpuStopping || pendingGpu is null){gpuRunning=false;return;}
                    values=pendingGpu;pendingGpu=null;
                }
                lastGpu=gpuClock.Elapsed.TotalSeconds;
                await gpu!.WriteAsync(new Dictionary<uint,Color[]>{{100,values}});
            }
        }
        catch(Exception ex)
        {
            lock(gpuSync){gpuFailure=ex;pendingGpu=null;gpuRunning=false;}
            OutputFaulted?.Invoke(ex);
        }
    }
    async Task StopGpuPumpAsync()
    {
        Task work;lock(gpuSync){gpuStopping=true;pendingGpu=null;work=gpuWork;}await work;
    }
    public async Task RestoreAsync()
    {
        var errors=new List<Exception>();
        await StopGpuPumpAsync();
        try{await msi.RestoreAsync();}catch(Exception ex){errors.Add(ex);}
        if(gpu is not null)try{await gpu.RestoreAsync();}catch(Exception ex){errors.Add(ex);}
        if(errors.Count>0)throw new AggregateException("Lighting cleanup was incomplete.",errors);
    }
    public async ValueTask DisposeAsync(){await StopGpuPumpAsync();await msi.DisposeAsync();if(gpu is not null)await gpu.DisposeAsync();}
}
