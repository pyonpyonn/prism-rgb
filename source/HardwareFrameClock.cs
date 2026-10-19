using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PrismRGB;

// A dedicated, scoped timer keeps USB cadence independent of WPF's coarse UI timers.
// The thread sleeps in the kernel; it does not spin or request a global timer period.
public sealed class HardwareFrameClock : IDisposable
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,IntPtr completion,IntPtr argument,bool resume);
    sealed class TimerHandle : WaitHandle {public TimerHandle(SafeWaitHandle handle){SafeWaitHandle=handle;}}
    sealed class Worker
    {
        public readonly ManualResetEvent Stop=new(false);
        public readonly Thread Thread;
        readonly TimerHandle timer;
        public Worker(Action tick,Action<Exception> error)
        {
            var handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
            if(handle.IsInvalid){handle.Dispose();handle=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1F0003);}
            if(handle.IsInvalid){handle.Dispose();Stop.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot create the lighting frame timer.");}
            timer=new TimerHandle(handle);
            Thread=new(()=>Run(tick,error)){IsBackground=true,Name="Prism lighting cadence"};Thread.Start();
        }
        void Run(Action tick,Action<Exception> error)
        {
            try
            {
                var watch=Stopwatch.StartNew();double next=1.0/30;
                var waits=new WaitHandle[]{Stop,timer};
                while(true)
                {
                    long due=-(long)Math.Max(1,(next-watch.Elapsed.TotalSeconds)*10000000);
                    if(!SetWaitableTimer(timer.SafeWaitHandle,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new Win32Exception(Marshal.GetLastWin32Error(),"Lighting frame timing failed.");
                    if(WaitHandle.WaitAny(waits)==0)return;
                    tick();next+=1.0/30;
                    if(next<watch.Elapsed.TotalSeconds)next=watch.Elapsed.TotalSeconds+1.0/30;
                }
            }
            catch(Exception ex){error(ex);}
            finally{timer.Dispose();}
        }
        public void Close(){Stop.Set();Thread.Join();Stop.Dispose();}
    }
    Worker? worker;
    public event Action<Exception>? Failed;
    public bool Active=>worker is not null;
    public void Set(bool needed,Action tick)
    {
        if(needed==Active)return;
        if(needed)worker=new Worker(tick,ex=>Failed?.Invoke(ex));else Dispose();
    }
    public void Dispose(){var current=worker;worker=null;current?.Close();}
}
