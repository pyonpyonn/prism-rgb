using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PrismRGB;

public sealed class HardwareBootDeviceResult
{
    public bool Requested {get;set;}=true;
    public bool Saved {get;set;}
    public string Message {get;set;}="Not attempted.";
}

public sealed class HardwareBootSaveResult
{
    public string Color {get;set;}="";
    public DateTimeOffset Finished {get;set;}
    public HardwareBootDeviceResult Motherboard {get;set;}=new();
    public HardwareBootDeviceResult Gpu {get;set;}=new();
    public bool Success=>(!Motherboard.Requested || Motherboard.Saved) && (!Gpu.Requested || Gpu.Saved);
    // A successful USB/I2C call cannot establish retention across a power cycle.
    public bool PowerCycleVerified=>false;
}

internal sealed class HardwareBootSaveOptions
{
    public Func<Color,Task> SaveMotherboard {get;init;}=HardwareBootColor.SaveMotherboardAsync;
    public Func<Color,Task> SaveGpu {get;init;}=HardwareBootColor.SaveGpuAsync;
    public string? LeaseDirectory {get;init;}
    public bool MotherboardRequested {get;init;}=true;
    public bool GpuRequested {get;init;}=true;
}

// Only an explicit user action calls this coordinator. Startup, scene rendering,
// preference saves and transport recovery never enter this persistent write path.
public static class HardwareBootColor
{
    public static Task<HardwareBootSaveResult> SaveAsync(Color color)=>SaveAsync(color,new());
    internal static async Task SaveMotherboardAsync(Color color)
    {
        await using var backend=new MsiBackend();
        await backend.ConnectAsync();
        await backend.SaveStaticToHardwareAsync(color);
        // Dispose releases ownership without restoring the previous volatile mode.
    }
    internal static async Task SaveGpuAsync(Color color)
    {
        await using var backend=new GigabyteBackend();
        await backend.ConnectAsync();
        await backend.SaveStaticToHardwareAsync(color);
    }
    internal static async Task<HardwareBootSaveResult> SaveAsync(Color color,HardwareBootSaveOptions options)
    {
        if(color.A!=255)throw new ArgumentException("Choose an opaque RGB boot colour.",nameof(color));
        if(!options.MotherboardRequested && !options.GpuRequested)throw new ArgumentException("Choose at least one lighting controller.");
        var result=new HardwareBootSaveResult{Color=ColorValues.Hex(color)};
        result.Motherboard.Requested=options.MotherboardRequested;result.Gpu.Requested=options.GpuRequested;
        if(!options.MotherboardRequested)result.Motherboard.Message="Not requested; no save was sent.";
        if(!options.GpuRequested)result.Gpu.Message="Not requested; no save was sent.";
        using var handoff=ControllerLease.RequestHandoff(options.LeaseDirectory);
        ControllerLease? controller=null;
        for(int attempt=0;attempt<100 && controller is null;attempt++)
        {
            controller=ControllerLease.TryAcquire(options.LeaseDirectory);
            if(controller is null)await Task.Delay(50);
        }
        using(controller)
        {
            if(controller is null)
            {
                result.Motherboard.Message=result.Gpu.Message="Another Prism instance owns the lights. Stop it before saving to hardware; no save was sent.";
            }
            else
            {
                // Each controller gets one attempt. In particular an uncertain
                // persistent commit is never resubmitted by a retry loop.
                if(options.MotherboardRequested)result.Motherboard=await SaveDeviceAsync(options.SaveMotherboard,color);
                if(options.GpuRequested)result.Gpu=await SaveDeviceAsync(options.SaveGpu,color);
            }
        }
        result.Finished=DateTimeOffset.Now;return result;
    }
    static async Task<HardwareBootDeviceResult> SaveDeviceAsync(Func<Color,Task> save,Color color)
    {
        try{await save(color);return new(){Saved=true,Message="Hardware save command accepted. Power-off verification pending."};}
        catch(Exception ex){return new(){Message=ex.Message};}
    }
}
