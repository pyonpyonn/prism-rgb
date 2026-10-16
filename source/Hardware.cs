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

public sealed record HidDevice(string Path,ushort VendorId,ushort ProductId,string Name,ushort FeatureLength,ushort UsagePage,ushort Usage)
{
    public string Id => $"{VendorId:X4}:{ProductId:X4}";
}

public static class Hid
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort VendorId,ProductId,Version; }
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
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle,IntPtr buffer,uint length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll",SetLastError=true)] public static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] bytes,int length);
    [DllImport("hid.dll",SetLastError=true)] public static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] bytes,int length);
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
                    ushort feature=0,usage=0,page=0;
                    if(HidD_GetPreparsedData(handle,out var pp))
                    {
                        try { if(HidP_GetCaps(pp,out var caps)==0x110000) {feature=caps.FeatureLength;usage=caps.Usage;page=caps.UsagePage;} } finally {HidD_FreePreparsedData(pp);}
                    }
                    devices.Add(new(path,attrs.VendorId,attrs.ProductId,name,feature,page,usage));
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
        foreach(var name in new[]{"SignalRgb","LedKeeper","LightKeeperService","LightKeeperService2","Mystic_Light_Service"})
            foreach(var p in Process.GetProcessesByName(name)) {p.Dispose(); return true;}
        return false;
    }
}

public sealed class OutputDevice
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Identity { get; set; } = "";
    public int LedCount { get; set; }
    public DevicePlacement Placement { get; set; } = new();
    public Color[] PreviousColors { get; set; } = Array.Empty<Color>();
    public int PreviousMode { get; set; }
    public List<byte[]> Modes { get; set; } = new();
    public bool CustomMode { get; set; }
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

public sealed class MsiBackend : ILightingBackend
{
    SafeFileHandle? handle; byte[]? original; bool wrote;
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"MSI USB · header colors";
    public Task ConnectAsync() => Task.Run(() =>
    {
        if(Hid.ConflictRunning()) throw new InvalidOperationException("Close SignalRGB and MSI lighting tools before connecting to the motherboard.");
        if(!Hid.BoardName().Equals("B550M PRO-VDH WIFI (MS-7C95)",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This adapter is restricted to the inspected B550M PRO-VDH WIFI (MS-7C95).");
        var d=Hid.Enumerate().FirstOrDefault(x=>x.VendorId==0x1462 && x.ProductId==0x7C95 && x.FeatureLength>=185);
        if(d is null) throw new InvalidOperationException("The inspected MSI USB controller is not available.");
        handle=Hid.Open(d.Path,true);
        if(handle.IsInvalid) throw new IOException("Cannot open the MSI lighting interface. Another lighting app may own it.");
        original=new byte[185]; original[0]=0x52;
        if(!Hid.HidD_GetFeature(handle,original,185) || original[0]!=0x52)
            throw new IOException("The controller did not return its 185-byte lighting state. Output remains disabled.");
        original[184]=0;
        var names=new[]{"12V RGB · header 1","12V RGB · header 2","5V ARGB · header 1","5V ARGB · header 2"};
        for(uint i=0;i<4;i++) Devices.Add(new(){Id=i,Name=names[i],Identity=$"MSI:1462:7C95:{i}",LedCount=1});
    });
    public Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors) => Task.Run(() =>
    {
        if(Hid.ConflictRunning()) throw new InvalidOperationException("Another lighting app started. MSI output has stopped.");
        if(handle is null || handle.IsClosed || original is null) throw new IOException("MSI interface is disconnected.");
        var p=MsiProtocol.Frame(original,colors);
        if(!Hid.HidD_SetFeature(handle,p,p.Length)) throw new IOException("The MSI controller rejected the lighting report.");
        wrote=true;
    });
    public Task RestoreAsync() => Task.Run(() =>
    {
        if(wrote && Hid.ConflictRunning())throw new IOException("Another lighting app owns the controller; Prism did not overwrite its state.");
        if(wrote && handle is not null && !handle.IsClosed && original is not null)
        {
            if(!Hid.HidD_SetFeature(handle,original,185)) throw new IOException("Could not restore the previous MSI lighting state.");
            wrote=false;
        }
    });
    public ValueTask DisposeAsync() {handle?.Dispose();handle=null;return ValueTask.CompletedTask;}
}
