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
    public static async Task<int> InspectMsiAsync(string path,bool test)
    {
        var log=new List<string>();bool success=false;
        try
        {
            var descriptors=Hid.Enumerate().Where(d=>d.VendorId==0x1462 && d.ProductId==0x7C95)
                .Select(d=>new{d.Id,d.FeatureLength,d.UsagePage,d.Usage,d.Report52Length}).ToArray();
            log.Add(JsonSerializer.Serialize(new{Board=Hid.BoardName(),Interfaces=descriptors}));
            if(Hid.ConflictRunning())throw new IOException("Close competing lighting tools first.");
            using var transport=new NativeMsiTransport();var original=transport.Read();
            log.Add("Read original 0x52 lighting report: "+original.Length+" bytes.");
            if(test)
            {
                // One dim static frame, then restoration in finally; no firmware or flash commands.
                var colors=new Dictionary<uint,Color[]>();for(uint i=0;i<4;i++)colors.Add(i,new[]{Color.FromRgb(12,24,32)});
                var packet=MsiProtocol.Frame(original,colors);
                try
                {
                    transport.Write(packet);log.Add("Controller accepted one dim static frame.");
                    await Task.Delay(1500);
                    var readback=transport.Read();
                    bool match=MsiProtocol.ZoneOffsets.All(o=>readback.AsSpan(o,4).SequenceEqual(packet.AsSpan(o,4)));
                    log.Add("Color/mode readback matches: "+match);
                    if(!match)throw new IOException("Lighting state readback does not match the requested colors.");
                }
                finally
                {
                    if(Hid.ConflictRunning())throw new IOException("Competing lighting software started; restoration was skipped.");
                    original[184]=0;transport.Write(original);
                    await Task.Delay(250);
                    var restored=transport.Read();
                    bool restoredMatch=MsiProtocol.ZoneOffsets.All(o=>original.AsSpan(o,4).SequenceEqual(restored.AsSpan(o,4)));
                    log.Add("Restored all four original header colors and modes; readback matches: "+restoredMatch);
                    log.Add("Other report byte differences after controller processing: "+string.Join(",",Enumerable.Range(0,184).Where(i=>original[i]!=restored[i])));
                    if(!restoredMatch)throw new IOException("Original header colors/modes did not match after restore.");
                }
            }
            success=true;
        }
        catch(Exception ex){log.Add(ex.Message);}
        File.WriteAllText(path,JsonSerializer.Serialize(new{Success=success,LightingWritesRequested=test,Log=log},new JsonSerializerOptions{WriteIndented=true}));
        return success?0:1;
    }
}
