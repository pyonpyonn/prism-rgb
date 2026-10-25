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

public static partial class GigabyteProtocol
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
    // Native static mode uses a different routing flag from Prism's streaming mode.
    // These packets only prepare the volatile configuration; the separate AA command commits it.
    public static List<byte[]> StaticHardwareSettings(Color color)
    {
        var result=new List<byte[]>();
        for(int zone=1;zone<=4;zone++)
        {
            result.Add(new byte[]{0x88,1,2,0x63,0,(byte)zone,0,0});
            result.Add(zone switch
            {
                1 or 2=>new byte[]{0xB0,1,color.R,color.G,color.B,color.R,color.G,color.B},
                3=>new byte[]{0xB1,1,color.R,color.G,color.B,0,0,0},
                _=>new byte[]{0x40,color.R,color.G,color.B,4,0,0,0}
            });
        }
        return result;
    }
    public static void ValidateStaticHardwareSetting(byte[] packet)
    {
        if(packet.Length!=8)throw new InvalidDataException("GPU static settings must contain eight bytes.");
        bool mode=packet[0]==0x88 && packet[1]==1 && packet[2]==2 && packet[3]==0x63 && packet[4]==0 && packet[5] is >=1 and <=4 && packet[6]==0 && packet[7]==0;
        bool paired=packet[0]==0xB0 && packet[1]==1 && packet.AsSpan(2,3).SequenceEqual(packet.AsSpan(5,3));
        bool right=packet[0]==0xB1 && packet[1]==1 && packet[5]==0 && packet[6]==0 && packet[7]==0;
        bool logo=packet[0]==0x40 && packet[4]==4 && packet[5]==0 && packet[6]==0 && packet[7]==0;
        if(!mode && !paired && !right && !logo)throw new InvalidDataException("Unsupported GPU static hardware setting.");
    }
    public static byte[] HardwareSaveCommand()=>new byte[]{0xAA,0,0,0,0,0,0,0};
}

public interface IGpuLightingTransport : IDisposable
{
    byte[] QueryIdentity();
    void Write(byte[] packet);
}

// Deliberately absent from the ordinary frame writer: committing nonvolatile memory
// is only available to the explicit hardware-save operation.
public interface IGpuHardwareSaveTransport : IGpuLightingTransport
{
    void WriteStaticSetting(byte[] packet);
    void WriteWaveSetting(byte[] packet)=>throw new NotSupportedException("Wave hardware settings are unavailable on this transport.");
    void CommitStaticConfiguration();
}

public sealed class NvidiaLightingException : IOException
{
    public int Status {get;}
    public bool RetryPacket=>Status is -1 or -108;
    public bool CanReconnect=>Status is -1 or -4 or -6 or -8 or -10 or -108 or -156;
    public NvidiaLightingException(int status):base($"NVIDIA GPU lighting I/O failed ({status})."){Status=status;}
}

public sealed class NvidiaGigabyteTransport : IGpuHardwareSaveTransport
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
            if(status!=0)throw new NvidiaLightingException(status);
        }
        finally{pinned.Free();}
    }
    public byte[] QueryIdentity()
    {
        Transfer(new byte[]{0xAB,0,0,0,0,0,0,0},false);
        var response=new byte[4];Transfer(response,true);return response;
    }
    public void Write(byte[] packet){GigabyteProtocol.Validate(packet);Transfer(packet,false);}
    public void WriteStaticSetting(byte[] packet){GigabyteProtocol.ValidateStaticHardwareSetting(packet);Transfer(packet,false);}
    public void WriteWaveSetting(byte[] packet){GigabyteProtocol.ValidateWaveHardwareSetting(packet);Transfer(packet,false);}
    public void CommitStaticConfiguration()=>Transfer(GigabyteProtocol.HardwareSaveCommand(),false);
    public void Dispose(){if(disposed)return;disposed=true;if(initialized)unload();NativeLibrary.Free(library);}
}

public sealed partial class GigabyteBackend : ILightingBackend
{
    readonly Func<IGpuLightingTransport> open;readonly Func<bool> conflict;readonly SemaphoreSlim gate=new(1,1);
    IGpuLightingTransport? transport;Color[]? previous;readonly Dictionary<byte,byte[]> packetCache=new();bool wrote,modeReady,disposed;
    public int Writes {get;private set;}
    public int PacketRetries {get;private set;}
    public int TransferFailures {get;private set;}
    public bool NeedsClear=>wrote;
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"Gigabyte RTX 4070 Ti · fan and logo lighting";
    public GigabyteBackend():this(()=>new NvidiaGigabyteTransport(),Hid.ConflictRunning){}
    internal GigabyteBackend(Func<IGpuLightingTransport> open,Func<bool> conflict){this.open=open;this.conflict=conflict;}
    async Task Locked(Func<CancellationToken,Task> action,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try{if(disposed)throw new ObjectDisposedException(nameof(GigabyteBackend));await Task.Run(()=>action(token),token);}finally{gate.Release();}
    }
    Task SendAsync(byte[] packet,CancellationToken token)=>SendVolatileAsync(packet,p=>transport!.Write(p),token);
    async Task SendVolatileAsync(byte[] packet,Action<byte[]> writePacket,CancellationToken token)
    {
        for(int attempt=0;;attempt++)
        {
            token.ThrowIfCancellationRequested();
            try{writePacket(packet);Writes++;return;}
            catch(NvidiaLightingException ex)
            {
                TransferFailures++;
                if(!ex.RetryPacket || attempt>=2)throw;
                PacketRetries++;
                await Task.Delay(attempt==0?40:160,token);
            }
        }
    }
    void OpenTransport()
    {
        if(conflict())throw new IOException("Close competing RGB software before connecting the GPU.");
        var candidate=open();
        try
        {
            if(!GigabyteProtocol.Identity(candidate.QueryIdentity()))throw new InvalidDataException("GPU lighting identity verification failed; no mode/color commands were sent.");
            transport=candidate;
        }
        catch{candidate.Dispose();throw;}
    }
    public Task ConnectAsync()=>Locked(_=>
    {
        OpenTransport();
        Devices.Clear();Devices.Add(new(){Id=100,Name="Gigabyte RTX 4070 Ti · fans + logo",Identity="GPU:10DE:2782:1458:40C6",LedCount=4,PixelNames=new[]{"Fan 1","Fan 2","Fan 3","Logo"}});
        return Task.CompletedTask;
    });
    internal Task ReconnectAsync(CancellationToken token)=>Locked(_=>
    {
        transport?.Dispose();transport=null;previous=null;packetCache.Clear();modeReady=false;
        OpenTransport();return Task.CompletedTask;
    },token);
    async Task WriteColorsAsync(Color[] colors,CancellationToken token)
    {
        if(!modeReady)
        {
            wrote=true;
            for(int zone=0;zone<8;zone++)await SendAsync(GigabyteProtocol.Mode(zone),token);
            modeReady=true;
        }
        foreach(var packet in GigabyteProtocol.Frame(colors))
        {
            if(packetCache.TryGetValue(packet[0],out var cached) && packet.AsSpan().SequenceEqual(cached))continue;
            await SendAsync(packet,token);packetCache[packet[0]]=packet;
        }
    }
    public Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)=>WriteAsync(colors,CancellationToken.None);
    internal Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors,CancellationToken token)=>Locked(async cancellation=>
    {
        if(!colors.TryGetValue(100,out var values))return;
        if(values.Length!=4)throw new InvalidDataException("The Gigabyte card requires four lighting colors.");
        if(transport is null)throw new IOException("GPU lighting is unavailable.");
        if(previous is not null && values.AsSpan().SequenceEqual(previous))return;
        try{await WriteColorsAsync(values,cancellation);previous=(Color[])values.Clone();}
        catch{previous=null;throw;}
    },token);
    public Task SaveStaticToHardwareAsync(Color color)=>ApplyStaticAsync(color,true);
    public Task PreviewStaticAsync(Color color)=>ApplyStaticAsync(color,false);
    Task ApplyStaticAsync(Color color,bool persist)=>Locked(async token=>
    {
        if(conflict())throw new IOException("Close competing RGB software before saving the GPU boot colour.");
        if(transport is not IGpuHardwareSaveTransport hardware)throw new NotSupportedException("This GPU transport does not support explicit hardware saves.");
        if(!GigabyteProtocol.Identity(hardware.QueryIdentity()))throw new InvalidDataException("GPU identity changed; the boot colour was not saved.");
        previous=null;packetCache.Clear();modeReady=false;wrote=true;
        foreach(var packet in GigabyteProtocol.StaticHardwareSettings(color))
            await SendVolatileAsync(packet,hardware.WriteStaticSetting,token);
        // A successful driver return acknowledges delivery, not retention across loss of power.
        // Never retry this commit: an I/O error may happen after the controller accepted it.
        if(!persist)return;
        try{hardware.CommitStaticConfiguration();Writes++;}
        catch(Exception ex)
        {
            if(ex is NvidiaLightingException)TransferFailures++;
            throw new IOException("GPU hardware-save delivery is uncertain. It was attempted once and was not retried; check the colour after a full power-off before saving again.",ex);
        }
    });
    public Task RestoreAsync()=>Locked(async token=>
    {
        if(!wrote)return;
        if(conflict())throw new IOException("Another RGB app owns the GPU; clearing was skipped.");
        if(transport is null)OpenTransport();
        await WriteColorsAsync(new Color[4],token);previous=null;wrote=false;modeReady=false;packetCache.Clear();
    });
    public async ValueTask DisposeAsync(){await gate.WaitAsync();try{if(disposed)return;disposed=true;transport?.Dispose();transport=null;}finally{gate.Release();}}
}

public sealed class NativeLightingBackend : ILightingBackend
{
    readonly MsiBackend msi;readonly Func<GigabyteBackend> openGpu;GigabyteBackend? gpu;
    readonly object gpuSync=new();Task gpuWork=Task.CompletedTask;Color[]? pendingGpu;bool gpuRunning,gpuStopping,gpuDisabled,disposed;
    CancellationTokenSource gpuCancellation=new();
    readonly Func<TimeSpan,CancellationToken,Task> recoveryDelay;
    readonly Stopwatch gpuClock=Stopwatch.StartNew();double lastGpu=-1;
    public NativeLightingBackend():this(new MsiBackend(),()=>new GigabyteBackend()){}
    internal NativeLightingBackend(MsiBackend msi,Func<GigabyteBackend> openGpu,Func<TimeSpan,CancellationToken,Task>? recoveryDelay=null){this.msi=msi;this.openGpu=openGpu;this.recoveryDelay=recoveryDelay??((time,token)=>Task.Delay(time,token));}
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>gpu is null?msi.Label:"MSI motherboard + Gigabyte GPU";
    public string GpuStatus {get;private set;}="";
    public int UsbWrites=>msi.Writes;
    public int I2cWrites=>gpu?.Writes??0;
    public int GpuPacketRetries=>gpu?.PacketRetries??0;
    public int GpuTransferFailures=>gpu?.TransferFailures??0;
    public int GpuRecoveries {get;private set;}
    public bool NeedsRestore=>msi.NeedsRestore || gpu?.NeedsClear==true;
    public event Action<string>? GpuStatusChanged;
    public async Task ConnectAsync()
    {
        await msi.ConnectAsync();Devices.AddRange(msi.Devices);
        var candidate=openGpu();
        try{await candidate.ConnectAsync();gpu=candidate;Devices.AddRange(candidate.Devices);GpuStatus="GPU lighting is ready.";}
        catch(Exception ex){await candidate.DisposeAsync();GpuStatus="GPU unavailable: "+ex.Message;}
    }
    public async Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)
    {
        if(disposed)throw new ObjectDisposedException(nameof(NativeLightingBackend));
        await msi.WriteAsync(colors.Where(x=>x.Key<4).ToDictionary(x=>x.Key,x=>x.Value));
        if(gpu is not null && colors.TryGetValue(100,out var values))
        {
            lock(gpuSync)
            {
                if(gpuDisabled || disposed)return;
                if(gpuStopping){gpuCancellation.Dispose();gpuCancellation=new();gpuStopping=false;}
                pendingGpu=(Color[])values.Clone();
                if(!gpuRunning)StartGpuPump();
            }
        }
    }
    // Called with gpuSync held. One worker owns all GPU writes, retries and reconnects.
    void StartGpuPump(){gpuRunning=true;var token=gpuCancellation.Token;gpuWork=Task.Run(()=>PumpGpuAsync(token));}
    void SetGpuStatus(string message){GpuStatus=message;GpuStatusChanged?.Invoke(message);}
    static bool CanRecover(Exception ex)=>ex is NvidiaLightingException native?native.CanReconnect:ex is IOException && ex is not InvalidDataException;
    internal static TimeSpan RecoveryWait(int attempt)=>TimeSpan.FromSeconds(attempt switch{1=>1,2=>2,3=>5,4=>10,_=>30});
    async Task PumpGpuAsync(CancellationToken token)
    {
        bool recovering=false;int attempt=0;
        try
        {
            while(true)
            {
                Color[]? values=null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if(recovering)
                    {
                        await recoveryDelay(RecoveryWait(attempt),token);
                        token.ThrowIfCancellationRequested();
                        await gpu!.ReconnectAsync(token);
                    }
                    double wait=1.0/15-(gpuClock.Elapsed.TotalSeconds-lastGpu);
                    if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait),token);
                    lock(gpuSync)
                    {
                        if(gpuStopping || pendingGpu is null)return;
                        values=pendingGpu;pendingGpu=null;
                    }
                    lastGpu=gpuClock.Elapsed.TotalSeconds;
                    await gpu!.WriteAsync(new Dictionary<uint,Color[]>{{100,values}},token);
                    if(recovering){GpuRecoveries++;SetGpuStatus("GPU lighting recovered automatically. Motherboard lighting stayed active.");recovering=false;attempt=0;}
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested){return;}
                catch(Exception ex)
                {
                    if(token.IsCancellationRequested)return;
                    if(!CanRecover(ex))
                    {
                        lock(gpuSync){gpuDisabled=true;pendingGpu=null;}
                        SetGpuStatus("GPU lighting paused: "+ex.Message+" Motherboard lighting continues. Reconnect the GPU after checking the driver and card.");return;
                    }
                    // Keep only the newest frame, including Solid scenes with no further ticks.
                    lock(gpuSync){if(gpuStopping)return;if(values is not null)pendingGpu??=values;}
                    attempt=Math.Min(attempt+1,5);
                    if(!recovering)SetGpuStatus("GPU lighting is recovering automatically: "+ex.Message+" Motherboard lighting continues.");
                    recovering=true;
                }
            }
        }
        finally
        {
            lock(gpuSync)
            {
                gpuRunning=false;
                if(!gpuStopping && !gpuDisabled && !disposed && pendingGpu is not null)StartGpuPump();
            }
        }
    }
    async Task StopGpuPumpAsync()
    {
        Task work;lock(gpuSync){gpuStopping=true;pendingGpu=null;gpuCancellation.Cancel();work=gpuWork;}await work;
    }
    public async Task RestoreAsync()
    {
        var errors=new List<Exception>();
        await StopGpuPumpAsync();
        try{await msi.RestoreAsync();}catch(Exception ex){errors.Add(ex);}
        if(gpu is not null)try{await gpu.RestoreAsync();}catch(Exception ex){errors.Add(ex);}
        if(errors.Count>0)throw new AggregateException("Lighting cleanup was incomplete.",errors);
    }
    public async ValueTask DisposeAsync(){lock(gpuSync){if(disposed)return;disposed=true;}await StopGpuPumpAsync();await msi.DisposeAsync();if(gpu is not null)await gpu.DisposeAsync();gpuCancellation.Dispose();}
}
