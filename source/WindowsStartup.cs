using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PrismRGB;

public interface IStartupRegistration
{
    bool Enabled {get;}
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName="PrismRGB";
    readonly string executable;
    public WindowsStartupRegistration(string? executable=null)=>this.executable=Path.GetFullPath(executable??Environment.ProcessPath??throw new InvalidOperationException("The application path is unavailable."));
    public static string Command(string executable)
    {
        if(!Path.IsPathFullyQualified(executable) || executable.IndexOfAny(new[]{'"','\r','\n'})>=0 || !executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Startup needs an absolute executable path.");
        return "\""+executable+"\" --startup";
    }
    public bool Enabled
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(RunKey);return string.Equals(key?.GetValue(ValueName) as string,Command(executable),StringComparison.OrdinalIgnoreCase);}
    }
    public void SetEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey,true);
        if(enabled){if(!File.Exists(executable))throw new FileNotFoundException("Keep the app in its permanent folder before enabling startup.");key.SetValue(ValueName,Command(executable),RegistryValueKind.String);}
        else key.DeleteValue(ValueName,false);
    }
}

public enum StartupAttempt { Started, Retry, Blocked }

public static class StartupSequence
{
    public static async Task<StartupAttempt> RunAsync(Func<CancellationToken,Task<StartupAttempt>> attempt,CancellationToken token,Func<TimeSpan,CancellationToken,Task>? wait=null)
    {
        wait??=(time,cancellation)=>Task.Delay(time,cancellation);
        foreach(int seconds in new[]{2,3,5})
        {
            await wait(TimeSpan.FromSeconds(seconds),token);token.ThrowIfCancellationRequested();
            var result=await attempt(token);token.ThrowIfCancellationRequested();
            if(result!=StartupAttempt.Retry)return result;
        }
        return StartupAttempt.Retry;
    }
}

public sealed class SingleInstanceLease : IDisposable
{
    readonly Mutex mutex;readonly EventWaitHandle activation,quit;RegisteredWaitHandle? listener,quitListener;bool disposed;
    SingleInstanceLease(Mutex mutex,EventWaitHandle activation,EventWaitHandle quit){this.mutex=mutex;this.activation=activation;this.quit=quit;}
    static string Name(string? scope)=>@"Local\PrismRGB-"+(scope??WindowsIdentity.GetCurrent().User?.Value??Environment.UserName);
    public static SingleInstanceLease? Acquire(bool activateExisting,string? scope=null)
    {
        string name=Name(scope);
        var signal=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-activate");
        var quit=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-quit");
        // Kernel-object lifetime is the lease; no thread-affine mutex ownership crosses awaits.
        var mutex=new Mutex(false,name,out bool created);
        if(!created){if(activateExisting)signal.Set();signal.Dispose();quit.Dispose();mutex.Dispose();return null;}
        return new(mutex,signal,quit);
    }
    public static bool RequestQuit(string? scope=null)
    {
        try{using var signal=EventWaitHandle.OpenExisting(Name(scope)+"-quit");signal.Set();return true;}
        catch(WaitHandleCannotBeOpenedException){return false;}
    }
    public void Listen(Action open,Action? quitAction=null)
    {
        listener=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>open(),null,Timeout.Infinite,false);
        if(quitAction is not null)quitListener=ThreadPool.RegisterWaitForSingleObject(quit,(_,_)=>quitAction(),null,Timeout.Infinite,false);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;listener?.Unregister(null);quitListener?.Unregister(null);activation.Dispose();quit.Dispose();mutex.Dispose();
    }
}

internal sealed class FakeStartupRegistration : IStartupRegistration
{
    public bool Enabled {get;private set;}
    public int Changes {get;private set;}
    public void SetEnabled(bool enabled){Enabled=enabled;Changes++;}
}
