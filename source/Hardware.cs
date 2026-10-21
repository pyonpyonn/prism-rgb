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

public sealed record HidDevice(string Path,ushort VendorId,ushort ProductId,string Name,ushort FeatureLength,ushort UsagePage,ushort Usage,ushort Report52Length=0,ushort Report53Length=0)
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
                    ushort feature=0,usage=0,page=0,report52Length=0,report53Length=0;
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
                                        var direct=values.Take(count).Where(v=>v.ReportId==0x53).ToArray();
                                        if(direct.Length==1 && direct[0].BitSize==8)report53Length=(ushort)(direct[0].ReportCount+1);
                                    }
                                }
                            }
                        } finally {HidD_FreePreparsedData(pp);}
                    }
                    devices.Add(new(path,attrs.VendorId,attrs.ProductId,name,feature,page,usage,report52Length,report53Length));
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
        foreach(var name in new[]{"SignalRgb","LedKeeper","LightKeeperService","LightKeeperService2","Mystic_Light_Service","RGBFusion","RGBFusion2"})
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
    public string[] PixelNames {get;set;}=Array.Empty<string>();
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

public static partial class MsiProtocol
{
    public const string Board="B550M PRO-VDH WIFI (MS-7C95)";
    public static bool Supports(string board,HidDevice d)=>board.Equals(Board,StringComparison.OrdinalIgnoreCase) && d.VendorId==0x1462 && d.ProductId==0x7C95 && d.FeatureLength==725 && d.UsagePage==1 && d.Usage==0 && d.Report52Length==185 && d.Report53Length==725;
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
    public static byte[] DirectConfiguration(byte[] original,IReadOnlyDictionary<uint,Color[]> colors)
    {
        foreach(var (id,data) in colors)
        {
            if(id>3 || data.Length<1 || data.Length>(id<2?1:120))throw new InvalidDataException("RGB headers accept one color; ARGB headers accept 1–120 LEDs.");
        }
        int first=colors.TryGetValue(2,out var a)?a.Length:Math.Clamp((int)original[41],1,120);
        int second=colors.TryGetValue(3,out var b)?b.Length:Math.Clamp((int)original[52],1,120);
        return StreamingConfiguration(original,first,second);
    }
    public static byte[] DirectFrame(uint header,Color[] colors)
    {
        if(header>1 || colors.Length<1 || colors.Length>120)throw new InvalidDataException("Unsupported ARGB port or LED count.");
        var packet=new byte[725];packet[0]=0x53;packet[1]=0x25;packet[2]=0x04;packet[3]=(byte)header;
        for(int i=0;i<colors.Length;i++){int o=5+i*3;packet[o]=colors[i].R;packet[o+1]=colors[i].G;packet[o+2]=colors[i].B;}
        return packet;
    }
    public static void ValidateWrite(byte[] packet)
    {
        bool configuration=packet.Length==185 && packet[0]==0x52 && packet[184]==0;
        bool direct=packet.Length==725 && packet[0]==0x53 && packet[1]==0x25 && packet[2]==4 && packet[3]<2 && packet[4]==0 && packet.AsSpan(365).IndexOfAnyExcept((byte)0)<0;
        bool compact=packet.Length==725 && packet[0]==0x53 && packet[1]==0x25 && packet[2]==6 && packet[3]==0 && packet[4]==0 && packet.AsSpan(707).IndexOfAnyExcept((byte)0)<0;
        if(!configuration && !direct && !compact)throw new InvalidDataException("Only volatile MSI configuration and bounded ARGB color packets are allowed.");
    }
    public static byte[] StreamingConfiguration(byte[] original,int first,int second)
    {
        if(first<1 || first>120 || second<1 || second>120)throw new InvalidDataException("ARGB headers accept 1–120 LEDs.");
        var packet=Frame(original,new Dictionary<uint,Color[]>());
        foreach(int o in ZoneOffsets){packet[o]=1;packet[o+8]=0x80;}
        packet[5]=8;packet[178]=0x2A;
        packet[35]=packet[46]=0x29;packet[41]=(byte)first;packet[52]=(byte)second;
        packet[74]=0x25;packet[78]=0xA9;packet[82]=0xBF;
        if(first+second>232){packet[31]=packet[42]=0x25;packet[82]=0xB1;}
        return packet;
    }
    // Native static mode is separate from report 0x53 streaming. In particular,
    // the onboard master must stop routing the direct stream through the headers.
    public static byte[] StaticConfiguration(byte[] original,Color color)
    {
        var packet=Frame(original,new Dictionary<uint,Color[]>());
        foreach(int offset in ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}))
        {
            packet[offset]=1;
            packet[offset+1]=packet[offset+5]=color.R;
            packet[offset+2]=packet[offset+6]=color.G;
            packet[offset+3]=packet[offset+7]=color.B;
            packet[offset+4]=0x28; // Native brightness 10/10; color already includes the chosen level.
            packet[offset+8]=(byte)(offset==74?0x81:0x80);
            packet[offset+9]=0;
        }
        packet[41]=(byte)Math.Clamp((int)packet[41],1,120);
        packet[52]=(byte)Math.Clamp((int)packet[52],1,120);
        return packet;
    }
    public static byte[] StaticSavePacket(byte[] configuration)
    {
        var packet=(byte[])configuration.Clone();
        if(packet.Length!=185)throw new InvalidDataException("Unexpected MSI static configuration.");
        packet[184]=1;ValidateStaticSave(packet);return packet;
    }
    public static void ValidateStaticSave(byte[] packet)
    {
        if(packet.Length!=185 || packet[0]!=0x52 || packet[184]!=1)
            throw new InvalidDataException("Only an explicit MSI static configuration can be saved.");
        var color=Color.FromRgb(packet[2],packet[3],packet[4]);
        var expected=StaticConfiguration(packet,color);expected[184]=1;
        if(!packet.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("Streaming, mixed modes and sync routing cannot be saved as a static boot color.");
    }
    public static bool MatchesStaticConfiguration(byte[] expected,byte[] observed)
    {
        // Firmware may normalize unused fields. Confirm every static field Prism sets,
        // including native mode/routing, both color triplets, brightness and LED counts.
        return expected.Length==185 && observed.Length==185 && expected[0]==0x52 && observed[0]==0x52
            && ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}).SelectMany(offset=>Enumerable.Range(offset,10)).All(index=>StaticReadbackByteMatches(expected,observed,index))
            && expected[41]==observed[41] && expected[52]==observed[52];
    }
    static bool StaticReadbackByteMatches(byte[] expected,byte[] observed,int index)
    {
        // On this 7C95 controller, byte129 reads back as zero in native static mode.
        // It is the unused secondary red of onboard slot124, not its active RGB color.
        // Permit only that observed normalization; the save packet itself stays canonical.
        return expected[index]==observed[index] || (index==129 && expected[124]==1 && observed[124]==1 && observed[129]==0);
    }
    public static string StaticMismatchDetails(byte[] expected,byte[] observed)
    {
        if(expected.Length!=185 || observed.Length!=185)return $"report lengths expected {expected.Length}, observed {observed.Length}";
        var indices=new[]{0,41,52}.Concat(ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}).SelectMany(offset=>Enumerable.Range(offset,10))).Distinct().OrderBy(index=>index);
        var changed=indices.Where(index=>!StaticReadbackByteMatches(expected,observed,index)).Select(index=>$"{index}:{expected[index]:X2}/{observed[index]:X2}").ToArray();
        return "byte expected/observed "+string.Join(", ",changed.Take(24))+(changed.Length>24?$"; {changed.Length-24} more fields differ":"");
    }
    public static byte[] CompactFrame(Color[] first,Color[] second,Color rgb1,Color rgb2)
    {
        if(first.Length<1 || first.Length>120 || second.Length<1 || second.Length>120 || first.Length+second.Length>232)throw new InvalidDataException("Compact ARGB capacity exceeded.");
        var packet=new byte[725];packet[0]=0x53;packet[1]=0x25;packet[2]=6;
        int offset=5;
        foreach(var color in new[]{rgb1,rgb2}.Concat(first).Concat(second))
        {packet[offset++]=color.R;packet[offset++]=color.G;packet[offset++]=color.B;}
        return packet;
    }
}

public interface IMsiTransport : IDisposable
{
    byte[] Read();
    void Write(byte[] packet);
}

// The frame writer has no access to persistent saves through IMsiTransport.Write.
public interface IMsiHardwareSaveTransport
{
    void SaveStatic(byte[] packet);
    void SaveWave(byte[] packet)=>throw new NotSupportedException("Wave hardware saves are unavailable on this transport.");
}

public sealed class NativeMsiTransport : IMsiTransport,IMsiHardwareSaveTransport
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
        MsiProtocol.ValidateWrite(packet);
        Send(packet);
    }
    public void SaveStatic(byte[] packet)
    {
        MsiProtocol.ValidateStaticSave(packet);
        // A failed call can still have reached flash: this command must never be retried here.
        Send(packet);
    }
    public void SaveWave(byte[] packet){MsiProtocol.ValidateWaveSave(packet);Send(packet);}
    void Send(byte[] packet)
    {
        // Allocate the collection maximum, and transfer the selected report's actual length.
        var buffer=new byte[725];packet.CopyTo(buffer,0);
        if(!Hid.HidD_SetFeature(handle,buffer,packet.Length))throw new IOException("The MSI controller rejected the lighting report (Windows error "+Marshal.GetLastWin32Error()+").");
    }
    public void Dispose()=>handle.Dispose();
}

public sealed partial class MsiBackend : ILightingBackend
{
    readonly Func<IMsiTransport> open;
    readonly Func<bool> conflict;
    readonly SemaphoreSlim gate=new(1,1);
    readonly Stopwatch conflictClock=new();
    IMsiTransport? transport; byte[]? original,lastPacket;readonly Dictionary<uint,byte[]> lastDirect=new();readonly Dictionary<uint,int> activeCounts=new(); bool wrote,faulted,disposed;
    public int Writes {get;private set;}
    public bool NeedsRestore=>wrote;
    public MsiBackend():this(()=>new NativeMsiTransport(),Hid.ConflictRunning){}
    internal MsiBackend(Func<IMsiTransport> open,Func<bool> conflict){this.open=open;this.conflict=conflict;}
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"Standalone MSI USB · individual ARGB LEDs";
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
            for(uint i=0;i<4;i++)Devices.Add(new(){Id=i,Name=names[i],Identity=$"MSI:1462:7C95:{i}",LedCount=i<2?1:Math.Clamp((int)original[MsiProtocol.ZoneOffsets[i]+10],1,120)});
        }
        catch{candidate.Dispose();throw;}
    });
    public Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)=>Locked(()=>
    {
        if(faulted || transport is null || original is null)throw new IOException("Controller is unavailable. Disconnect and reconnect.");
        CheckConflict();
        if(colors.Count==0)return;
        // The controller routes compact data through all four headers. Unselected outputs
        // are explicitly black while Prism owns it; configuration is restored on Stop.
        foreach(var (id,values) in colors)if(id>3 || values.Length<1 || values.Length>(id<2?1:120))throw new InvalidDataException("Unsupported MSI output or pixel count.");
        var all=new Dictionary<uint,Color[]>();
        foreach(var d in Devices)all[d.Id]=colors.TryGetValue(d.Id,out var values)?values:new Color[d.Id<2?1:d.LedCount];
        var packet=MsiProtocol.DirectConfiguration(original,all);
        // A failed USB call may still have reached the controller: retain restoration responsibility.
        wrote=true;
        foreach(var (id,values) in all.Where(x=>x.Key>=2))activeCounts[id]=values.Length;
        try
        {
            if(lastPacket is null || !packet.AsSpan().SequenceEqual(lastPacket))
            {
                transport.Write(packet);Writes++;
                // Configuration changes invalidate cached color packets, including count changes.
                lastDirect.Clear();
                lastPacket=packet;Thread.Sleep(10);
            }
            var frames=new Dictionary<uint,byte[]>();
            if(all[2].Length+all[3].Length<=232)
                frames[0]=MsiProtocol.CompactFrame(all[2],all[3],all[0][0],all[1][0]);
            else
            {
                frames[2]=MsiProtocol.DirectFrame(0,all[2]);frames[3]=MsiProtocol.DirectFrame(1,all[3]);
                var analog=new byte[725];analog[0]=0x53;analog[1]=0x25;analog[2]=6;
                analog[5]=all[0][0].R;analog[6]=all[0][0].G;analog[7]=all[0][0].B;
                analog[8]=all[1][0].R;analog[9]=all[1][0].G;analog[10]=all[1][0].B;frames[0]=analog;
            }
            foreach(var (id,direct) in frames)
            {
                if(lastDirect.TryGetValue(id,out var previous) && direct.AsSpan().SequenceEqual(previous))continue;
                transport.Write(direct);Writes++;lastDirect[id]=direct;
            }
        }
        catch{faulted=true;throw;}
    });
    public Task RestoreAsync()=>Locked(()=>
    {
        if(!wrote || transport is null || original is null)return;
        CheckConflict(true);
        // Clear the ports we streamed before restoring configuration. An external
        // application's previous per-LED animation cannot be recovered from report 0x52.
        if(activeCounts.TryGetValue(2,out int first) && activeCounts.TryGetValue(3,out int second) && first+second<=232)
        {transport.Write(MsiProtocol.CompactFrame(new Color[first],new Color[second],Colors.Black,Colors.Black));Writes++;}
        else foreach(var (id,count) in activeCounts){transport.Write(MsiProtocol.DirectFrame(id-2,new Color[count]));Writes++;}
        if(activeCounts.Count>0)Thread.Sleep(10);
        transport.Write(original);Writes++;
        Thread.Sleep(250);
        var restored=transport.Read();
        if(restored.Length!=185 || restored[0]!=0x52 || !MsiProtocol.ZoneOffsets.Select((o,i)=>(Offset:o,Length:i<2?10:11)).All(z=>original.AsSpan(z.Offset,z.Length).SequenceEqual(restored.AsSpan(z.Offset,z.Length))))
            throw new IOException("The controller did not confirm restoration of all four header colors and modes.");
        wrote=false;lastPacket=null;lastDirect.Clear();activeCounts.Clear();
    });
    public Task SaveStaticToHardwareAsync(Color color)=>Locked(()=>
    {
        if(faulted || transport is null || original is null)throw new IOException("Controller is unavailable. Disconnect and reconnect.");
        CheckConflict(true);
        if(transport is not IMsiHardwareSaveTransport persistent)
            throw new NotSupportedException("This MSI transport cannot save a static boot color.");
        // Read the live configuration so unrelated, unsupported fields remain intact.
        var current=MsiProtocol.Frame(transport.Read(),new Dictionary<uint,Color[]>());
        var selected=MsiProtocol.StaticConfiguration(current,color);
        wrote=true;lastPacket=null;lastDirect.Clear();activeCounts.Clear();
        try
        {
            // The native controller expects its previous report followed by its new report.
            // Both preparation reports are volatile. Flash is committed exactly once below.
            transport.Write(current);Writes++;
            transport.Write(selected);Writes++;
            Thread.Sleep(250);
            var observed=transport.Read();
            if(observed.Length!=185 || observed[0]!=0x52)
                throw new IOException("MSI did not return a static lighting configuration; hardware save was not attempted.");
            if(!MsiProtocol.MatchesStaticConfiguration(selected,observed))
                throw new IOException("MSI did not confirm the selected static configuration; hardware save was not attempted. "+MsiProtocol.StaticMismatchDetails(selected,observed));
            try{persistent.SaveStatic(MsiProtocol.StaticSavePacket(selected));Writes++;}
            catch(Exception ex)
            {
                throw new IOException("MSI hardware save was not confirmed. It may have reached the controller; Prism will not retry it automatically.",ex);
            }
            Thread.Sleep(250);
            // Stop/restore after this explicit save should return to the user's new static color.
            original=selected;wrote=false;
        }
        catch{faulted=true;throw;}
    });
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();try{if(disposed)return;disposed=true;transport?.Dispose();transport=null;}finally{gate.Release();}
    }
}
