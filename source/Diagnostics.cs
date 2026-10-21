using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public static class Diagnostics
{
    public static async Task<int> TestArgbAsync(string path)
    {
        var log=new List<string>();bool success=false;
        await using var backend=new MsiBackend();
        try
        {
            await backend.ConnectAsync();
            var headers=backend.Devices.Where(d=>d.Id>=2).ToArray();
            log.Add("Read LED counts: "+string.Join(", ",headers.Select(d=>$"{d.Name}: {d.LedCount}")));
            foreach(var color in new[]{Color.FromRgb(64,0,0),Color.FromRgb(0,64,0),Color.FromRgb(0,0,64)})
            {
                await backend.WriteAsync(headers.ToDictionary(d=>d.Id,d=>Enumerable.Repeat(color,d.LedCount).ToArray()));
                log.Add("Sent solid test color "+color);await Task.Delay(5000);
            }
            for(int frame=0;frame<200;frame++)
            {
                await backend.WriteAsync(headers.ToDictionary(d=>d.Id,d=>Enumerable.Range(0,d.LedCount).Select(i=>Effects.Scale(Effects.Hsv((i/(double)d.LedCount+frame*.02)%1,1,1),.25)).ToArray()));
                await Task.Delay(125);
            }
            log.Add("Sent distinct moving pixel colors to each configured LED; visible result requires user confirmation.");success=true;
        }
        catch(Exception ex){log.Add(ex.Message);}
        finally
        {
            try{await backend.RestoreAsync();log.Add("Cleared streamed pixels and verified original header configuration readback.");}
            catch(Exception ex){success=false;log.Add("Restore failed: "+ex.Message);}
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{TransportSucceeded=success,VisibleResultRequiresUserConfirmation=true,Log=log,Writes=backend.Writes},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
    public static async Task<int> TestGpuAsync(string path)
    {
        var log=new List<string>();bool success=false;
        await using var backend=new GigabyteBackend();
        try
        {
            await backend.ConnectAsync();
            foreach(var color in new[]{Color.FromRgb(64,0,0),Color.FromRgb(0,64,0),Color.FromRgb(0,0,64)})
            {
                await backend.WriteAsync(new Dictionary<uint,Color[]>{{100,Enumerable.Repeat(color,4).ToArray()}});
                log.Add("GPU test color: "+color);await Task.Delay(5000);
            }
            success=true;
        }
        catch(Exception ex){log.Add(ex.Message);}
        finally{try{await backend.RestoreAsync();log.Add("GPU test colors cleared.");}catch(Exception ex){success=false;log.Add(ex.Message);}}
        File.WriteAllText(path,JsonSerializer.Serialize(new{TransportSucceeded=success,VisibleResultRequiresUserConfirmation=true,Log=log},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
    public static Task<int> InspectMsiAsync(string path)
    {
        var log=new List<string>();bool success=false;
        try
        {
            var descriptors=Hid.Enumerate().Where(d=>d.VendorId==0x1462 && d.ProductId==0x7C95)
                .Select(d=>new{d.Id,d.FeatureLength,d.UsagePage,d.Usage,d.Report52Length,d.Report53Length}).ToArray();
            log.Add(JsonSerializer.Serialize(new{Board=Hid.BoardName(),Interfaces=descriptors}));
            if(Hid.ConflictRunning())throw new IOException("Close competing lighting tools first.");
            using var transport=new NativeMsiTransport();var original=transport.Read();
            log.Add("Read report 0x52: "+original.Length+" bytes.");
            log.Add("ARGB configured lengths: "+original[41]+", "+original[52]);
            log.Add("Mode fields: "+string.Join(",",MsiProtocol.ZoneOffsets.Select(o=>original[o])));success=true;
            foreach(int offset in MsiProtocol.ZoneOffsets.Concat(new[]{74,84,94,104,114,124,134}))
                log.Add($"Static zone {offset}: "+Convert.ToHexString(original.AsSpan(offset,10)));
        }
        catch(Exception ex){log.Add(ex.Message);}
        File.WriteAllText(path,JsonSerializer.Serialize(new{Success=success,LightingWritesRequested=false,Log=log},new JsonSerializerOptions{WriteIndented=true}));return Task.FromResult(success?0:1);
    }
}
