using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public sealed class OpenRgbBackend : ILightingBackend
{
    readonly int port; readonly TcpClient client=new(); readonly CancellationTokenSource life=new();
    NetworkStream? stream; Task? receiver; uint protocol=3; volatile bool invalidated; Exception? failure;
    readonly Channel<(uint Id,uint Device,byte[] Data)> responses=Channel.CreateUnbounded<(uint,uint,byte[])>();
    public List<OutputDevice> Devices {get;}=new();
    public string Label=>"OpenRGB · local connection";
    public OpenRgbBackend(int port=6742) {this.port=port;}
    public async Task ConnectAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await client.ConnectAsync("127.0.0.1",port,timeout.Token); client.NoDelay=true;
        stream=client.GetStream(); receiver=ReceiveAsync();
        await SendAsync(0,40,BitConverter.GetBytes(3u));
        var version=await ResponseAsync(40,0);
        if(version.Length!=4) throw new InvalidDataException("Invalid OpenRGB protocol reply.");
        protocol=Math.Min(3,BinaryPrimitives.ReadUInt32LittleEndian(version));
        if(protocol<1) throw new InvalidDataException("OpenRGB SDK protocol 1 or newer is required.");
        await SendAsync(0,50,Encoding.UTF8.GetBytes("PrismRGB\0"));
        await SendAsync(0,0,Array.Empty<byte>());
        var countData=await ResponseAsync(0,0);
        if(countData.Length!=4) throw new InvalidDataException("Unexpected OpenRGB controller list format.");
        uint count=BinaryPrimitives.ReadUInt32LittleEndian(countData);
        if(count>256) throw new InvalidDataException("Too many lighting controllers.");
        for(uint i=0;i<count;i++)
        {
            await SendAsync(i,1,BitConverter.GetBytes(protocol));
            Devices.Add(ParseDevice(i,await ResponseAsync(1,i),protocol));
        }
        EnsureValid();
    }
    async Task ReceiveAsync()
    {
        try
        {
            while(!life.IsCancellationRequested)
            {
                byte[] header=new byte[16]; await stream!.ReadExactlyAsync(header,life.Token);
                if(!header.AsSpan(0,4).SequenceEqual("ORGB"u8)) throw new InvalidDataException("Invalid OpenRGB packet header.");
                uint device=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)),id=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)),length=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
                if(length>16*1024*1024) throw new InvalidDataException("OpenRGB reply exceeds the size limit.");
                byte[] data=new byte[length]; await stream.ReadExactlyAsync(data,life.Token);
                if(id==100) {invalidated=true; continue;}
                if(!responses.Writer.TryWrite((id,device,data))) break;
            }
        }
        catch(Exception e) {failure=e;responses.Writer.TryComplete(e);}
    }
    void EnsureValid()
    {
        if(invalidated) throw new IOException("OpenRGB's device list changed. Disconnect and reconnect before continuing.");
        if(failure is not null) throw new IOException("The OpenRGB connection ended.",failure);
    }
    async Task<byte[]> ResponseAsync(uint id,uint device)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while(await responses.Reader.WaitToReadAsync(timeout.Token))
        {
            while(responses.Reader.TryRead(out var p)) if(p.Id==id && p.Device==device) return p.Data;
        }
        throw new IOException("OpenRGB did not return the requested data.");
    }
    async Task SendAsync(uint device,uint id,byte[] data)
    {
        EnsureValid(); byte[] p=new byte[16+data.Length]; "ORGB"u8.CopyTo(p);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4),device);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8),id);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12),(uint)data.Length);
        data.CopyTo(p,16);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await stream!.WriteAsync(p,timeout.Token);
    }
    public async Task WriteAsync(IReadOnlyDictionary<uint,Color[]> colors)
    {
        EnsureValid();
        foreach(var d in Devices)
        {
            if(!colors.TryGetValue(d.Id,out var values)) continue;
            if(values.Length!=d.LedCount) throw new InvalidDataException("LED count changed. Reconnect the device.");
            if(!d.CustomMode) {await SendAsync(d.Id,1100,Array.Empty<byte>());d.CustomMode=true;}
            await SendAsync(d.Id,1050,PackColors(values));
        }
    }
    public async Task RestoreAsync()
    {
        EnsureValid();
        foreach(var d in Devices.Where(x=>x.CustomMode))
        {
            await SendAsync(d.Id,1050,PackColors(d.PreviousColors));
            if(d.PreviousMode>=0 && d.PreviousMode<d.Modes.Count)
            {
                byte[] mode=d.Modes[d.PreviousMode],payload=new byte[8+mode.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(payload,(uint)payload.Length);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4),d.PreviousMode);
                mode.CopyTo(payload,8);await SendAsync(d.Id,1101,payload);
            }
            d.CustomMode=false;
        }
    }
    public static byte[] PackColors(Color[] colors)
    {
        if(colors.Length>65535) throw new InvalidDataException("Too many LEDs.");
        byte[] p=new byte[6+colors.Length*4]; BinaryPrimitives.WriteUInt32LittleEndian(p,(uint)p.Length); BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4),(ushort)colors.Length);
        for(int i=0;i<colors.Length;i++){p[6+i*4]=colors[i].R;p[7+i*4]=colors[i].G;p[8+i*4]=colors[i].B;}
        return p;
    }
    public static OutputDevice ParseDevice(uint id,byte[] data,uint version)
    {
        if(version<1 || version>3) throw new InvalidDataException("Unsupported controller data version.");
        using var ms=new MemoryStream(data);using var r=new BinaryReader(ms,Encoding.UTF8);
        void Skip(long bytes) { if(bytes<0 || ms.Position+bytes>ms.Length) throw new InvalidDataException("Truncated controller data.");ms.Position+=bytes; }
        string Str() {int n=r.ReadUInt16();if(n==0 || n>ms.Length-ms.Position) throw new InvalidDataException("Invalid controller string.");var bytes=r.ReadBytes(n);if(bytes[^1]!=0) throw new InvalidDataException("Unterminated controller string.");return Encoding.UTF8.GetString(bytes,0,n-1);}
        if(data.Length<8 || r.ReadUInt32()!=data.Length) throw new InvalidDataException("Invalid controller packet length.");
        Skip(4);string name=Str(),vendor=Str();Str();Str();Str();string location=Str();
        int modeCount=r.ReadUInt16(),active=r.ReadInt32();var modes=new List<byte[]>();
        for(int i=0;i<modeCount;i++)
        {
            int start=(int)ms.Position;Str();Skip(version>=3?48:36);int modeColors=r.ReadUInt16();Skip(modeColors*4L);
            modes.Add(data.AsSpan(start,(int)ms.Position-start).ToArray());
        }
        int zones=r.ReadUInt16();
        for(int i=0;i<zones;i++){Str();Skip(16);int matrix=r.ReadUInt16();Skip(matrix);}
        int leds=r.ReadUInt16();for(int i=0;i<leds;i++){Str();Skip(4);}
        int colorCount=r.ReadUInt16();var colors=new Color[colorCount];
        for(int i=0;i<colorCount;i++){byte red=r.ReadByte(),green=r.ReadByte(),blue=r.ReadByte();r.ReadByte();colors[i]=Color.FromRgb(red,green,blue);}
        if(colorCount!=leds || ms.Position!=ms.Length) throw new InvalidDataException("Inconsistent OpenRGB LED data.");
        return new(){Id=id,Name=name,Identity=$"{vendor}:{name}:{location}",LedCount=leds,PreviousColors=colors,PreviousMode=active,Modes=modes};
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel();client.Dispose();if(receiver is not null) try{await receiver;}catch{} life.Dispose();
    }
}
