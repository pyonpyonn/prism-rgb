using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Windows.Media;

namespace PrismRGB;

public sealed record HidDevice(string Path,ushort VendorId,ushort ProductId,string Name,ushort FeatureLength,ushort UsagePage,ushort Usage,ushort Report52Length=0)
{
    public string Id => $"{VendorId:X4}:{ProductId:X4}";
}

public static class Hid
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort VendorId,ProductId,Version; }
    [StructLayout(LayoutKind.Explicit,Size=72)] struct ValueCaps
    {
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
    }
    [StructLayout(LayoutKind.Sequential)] struct Caps
    {
        public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr hwnd,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid guid,uint index,ref InterfaceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,uint size,out uint required,IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetProductString(SafeFileHandle handle,IntPtr buffer,uint length);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int reportType,[Out] ValueCaps[] caps,ref ushort length,IntPtr data);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] public static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] bytes,int length);
    [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] public static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] bytes,int length);
    public static SafeFileHandle Open(string path,bool write=false) => CreateFile(path,write?0xC0000000u:0,3,IntPtr.Zero,3,0,IntPtr.Zero);
    public static List<HidDevice> Enumerate()
    {
        var devices=new List<HidDevice>(); HidD_GetHidGuid(out var guid);
        var set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1)) return devices;
        try
        {
            for(uint i=0;i<512;i++)
            {
                var d=new InterfaceData{Size=Marshal.SizeOf<InterfaceData>()};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref d)) break;
                SetupDiGetDeviceInterfaceDetail(set,ref d,IntPtr.Zero,0,out var needed,IntPtr.Zero);
                if(needed<8 || needed>65536) continue;
                var memory=Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(memory,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref d,memory,needed,out _,IntPtr.Zero)) continue;
                    string? path=Marshal.PtrToStringUni(memory+4); if(path is null) continue;
                    using var handle=Open(path); if(handle.IsInvalid) continue;
                    var attrs=new Attributes{Size=Marshal.SizeOf<Attributes>()};
                    if(!HidD_GetAttributes(handle,ref attrs)) continue;
                    string name=$"USB device {attrs.VendorId:X4}:{attrs.ProductId:X4}";
                    var text=Marshal.AllocHGlobal(512);
                    try { if(HidD_GetProductString(handle,text,512)) name=Marshal.PtrToStringUni(text) ?? name; } finally {Marshal.FreeHGlobal(text);}
                    ushort feature=0,usage=0,page=0,report52Length=0;
                    if(HidD_GetPreparsedData(handle,out var pp))
                    {
                        try
                        {
                            if(HidP_GetCaps(pp,out var caps)==0x110000)
                            {
                                feature=caps.FeatureLength;usage=caps.Usage;page=caps.UsagePage;
                                if(attrs.VendorId==0x1462 && attrs.ProductId==0x7C95 && caps.FeatureValues>0 && caps.FeatureValues<64)
                                {
                                    ushort count=caps.FeatureValues;var values=new ValueCaps[count];
                                    if(HidP_GetValueCaps(2,values,ref count,pp)==0x110000)
                                    {
                                        var matching=values.Take(count).Where(v=>v.ReportId==0x52).ToArray();
                                        if(matching.Length==1 && matching[0].BitSize==8)report52Length=(ushort)(matching[0].ReportCount+1);
                                    }
                                }
                            }
                        } finally {HidD_FreePreparsedData(pp);}
                    }
                    devices.Add(new(path,attrs.VendorId,attrs.ProductId,name,feature,page,usage,report52Length));
                } finally {Marshal.FreeHGlobal(memory);}
            }
        } finally {SetupDiDestroyDeviceInfoList(set);}
        return devices;
    }
    public static string BoardName()
    {
        using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return key?.GetValue("BaseBoardProduct")?.ToString() ?? "Unknown motherboard";
    }
    public static bool ConflictRunning()
    {
        bool found=false;
        foreach(var name in new[]{"SignalRgb","LedKeeper","LightKeeperService","LightKeeperService2","Mystic_Light_Service"})
            foreach(var p in Process.GetProcessesByName(name)) {found=true;p.Dispose();}
        return found;
    }
}

public sealed class OutputDevice
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Identity { get; set; } = "";
    public int LedCount { get; set; }
    public DevicePlacement Placement { get; set; } = new();
}

public interface ILightingBackend : IAsyncDisposable
{
    List<OutputDevice> Devices {get;}
    string Label {get;}
    Task ConnectAsync();
    Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors);
    Task RestoreAsync();
}

public static class MsiProtocol
{
    public const string Board="B550M PRO-VDH WIFI (MS-7C95)";
    public static bool Supports(string board,HidDevice d)=>board.Equals(Board,StringComparison.OrdinalIgnoreCase) && d.VendorId==0x1462 && d.ProductId==0x7C95 && d.FeatureLength==725 && d.UsagePage==1 && d.Usage==0 && d.Report52Length==185;
    public static readonly int[] ZoneOffsets={1,174,31,42};
    public static byte[] Frame(byte[] original,IReadOnlyDictionary<uint,Color[]> colors)
    {
        if(original.Length!=185 || original[0]!=0x52) throw new InvalidDataException("Unexpected MSI lighting report.");
        var packet=(byte[])original.Clone();
        foreach(var (id,data) in colors)
        {
            if(id>=4 || data.Length!=1) throw new InvalidDataException("MSI adapter accepts one color per header.");
            int offset=ZoneOffsets[id]; var c=data[0];
            packet[offset]=1; packet[offset+1]=c.R;packet[offset+2]=c.G;packet[offset+3]=c.B;
        }
        packet[184]=0; // Volatile lighting only; never request a flash save.
        return packet;
    }
}

public interface IMsiTransport : IDisposable
{
    byte[] Read();
    void Write(byte[] packet);
}

public sealed class NativeMsiTransport : IMsiTransport
{
    readonly SafeFileHandle handle;
    public NativeMsiTransport()
    {
        var board=Hid.BoardName();
        var d=Hid.Enumerate().FirstOrDefault(d=>MsiProtocol.Supports(board,d));
        if(d is null)throw new IOException("Expected MSI B550M PRO-VDH WIFI controller (1462:7C95, report 0x52 = 185 bytes) was not found.");
        handle=Hid.Open(d.Path,true);
        if(handle.IsInvalid){handle.Dispose();throw new IOException("Cannot open the MSI controller. Close other lighting tools.");}
    }
    public byte[] Read()
    {
        var bytes=new byte[725];bytes[0]=0x52;
        if(!Hid.HidD_GetFeature(handle,bytes,bytes.Length) || bytes[0]!=0x52)throw new IOException("Cannot read the MSI controller's lighting state.");
        return bytes.AsSpan(0,185).ToArray();
    }
    public void Write(byte[] packet)
    {
        if(packet.Length!=185 || packet[0]!=0x52 || packet[184]!=0)throw new InvalidDataException("Only volatile 185-byte MSI lighting reports are allowed.");
        // Windows requires the collection's maximum buffer length. The report ID selects
        // the descriptor-validated 185-byte report; padding is not a 0x53 command.
        var buffer=new byte[725];packet.CopyTo(buffer,0);
        if(!Hid.HidD_SetFeature(handle,buffer,buffer.Length))throw new IOException("The MSI controller rejected the lighting report.");
    }
    public void Dispose()=>handle.Dispose();
}

public sealed class MsiBackend : ILightingBackend
{
    readonly Func<IMsiTransport> open;
    readonly Func<bool> conflict;
    readonly SemaphoreSlim gate=new(1,1);
    readonly Stopwatch conflictClock=new();
    IMsiTransport? transport; byte[]? original,lastPacket; bool wrote,faulted,disposed;
    public int Writes {get;private set;}
    public bool NeedsRestore=>wrote;
    public MsiBackend():this(()=>new NativeMsiTransport(),Hid.ConflictRunning){}
    internal MsiBackend(Func<IMsiTransport> open,Func<bool> conflict){this.open=open;this.conflict=conflict;}
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"Standalone MSI USB · four headers";
    void CheckConflict(bool force=false)
    {
        if(!force && conflictClock.IsRunning && conflictClock.ElapsedMilliseconds<2000)return;
        conflictClock.Restart();
        if(conflict()){faulted=true;throw new IOException("Another lighting app is running. Stop it and reconnect Prism.");}
    }
    async Task Locked(Action action)
    {
        await gate.WaitAsync();try{if(disposed)throw new ObjectDisposedException(nameof(MsiBackend));await Task.Run(action);}finally{gate.Release();}
    }
    public Task ConnectAsync()=>Locked(()=>
    {
        if(transport is not null)throw new InvalidOperationException("Controller already connected.");
        CheckConflict(true);
        var candidate=open();
        try
        {
            var bytes=candidate.Read();MsiProtocol.Frame(bytes,new Dictionary<uint,Color[]>());
            original=(byte[])bytes.Clone();original[184]=0;transport=candidate;
            var names=new[]{"12V RGB · JRGB1","12V RGB · JRGB2","5V ARGB · JRAINBOW1","5V ARGB · JRAINBOW2"};
            for(uint i=0;i<4;i++)Devices.Add(new(){Id=i,Name=names[i],Identity=$"MSI:1462:7C95:{i}",LedCount=1});
        }
        catch{candidate.Dispose();throw;}
    });
    public Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)=>Locked(()=>
    {
        if(faulted || transport is null || original is null)throw new IOException("Controller is unavailable. Disconnect and reconnect.");
        CheckConflict();
        if(colors.Count==0)return;
        var packet=MsiProtocol.Frame(original,colors);
        if(lastPacket is not null && packet.AsSpan().SequenceEqual(lastPacket))return;
        // A failed USB call may still have reached the controller: retain restoration responsibility.
        wrote=true;
        try{transport.Write(packet);lastPacket=packet;Writes++;}catch{faulted=true;throw;}
    });
    public Task RestoreAsync()=>Locked(()=>
    {
        if(!wrote || transport is null || original is null)return;
        CheckConflict(true);transport.Write(original);Writes++;
        Thread.Sleep(250);
        var restored=transport.Read();
        if(restored.Length!=185 || restored[0]!=0x52 || !MsiProtocol.ZoneOffsets.All(o=>original.AsSpan(o,4).SequenceEqual(restored.AsSpan(o,4))))
            throw new IOException("The controller did not confirm restoration of all four header colors and modes.");
        wrote=false;lastPacket=null;
    });
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();try{if(disposed)return;disposed=true;transport?.Dispose();transport=null;}finally{gate.Release();}
    }
}
