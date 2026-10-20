using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Interop;

namespace PrismRGB;

public partial class App : Application
{
    SingleInstanceLease? instance;
    protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
        int dataDir=Array.IndexOf(e.Args,"--data-dir");
        if(dataDir>=0 && dataDir+1<e.Args.Length)Storage.DirectoryPath=Path.GetFullPath(e.Args[dataDir+1]);
        if(Array.IndexOf(e.Args,"--self-test")>=0)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            int code=await SelfTests.RunAsync();Shutdown(code);return;
        }
        int eyedropper=Array.IndexOf(e.Args,"--verify-eyedropper");
        if(eyedropper>=0 && eyedropper+1<e.Args.Length)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try
            {
                bool capture=await ScreenEyedropper.VerifyCaptureAsync(),overlay=await ScreenEyedropper.VerifyOverlayAsync();
                File.WriteAllText(e.Args[eyedropper+1],System.Text.Json.JsonSerializer.Serialize(new{PhysicalScreenCaptureMatchesKnownColor=capture,FrozenSelectionAndCancellation=overlay}));
                Shutdown(capture&&overlay?0:1);
            }
            catch(Exception ex){File.WriteAllText(e.Args[eyedropper+1],ex.ToString());Shutdown(1);}return;
        }
        int probe=Array.IndexOf(e.Args,"--inspect-msi"),argbTest=Array.IndexOf(e.Args,"--test-argb"),gpuTest=Array.IndexOf(e.Args,"--test-gpu");
        if(probe>=0 || argbTest>=0 || gpuTest>=0)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            int index=probe>=0?probe:argbTest>=0?argbTest:gpuTest;
            if(index+1>=e.Args.Length){Shutdown(2);return;}
            int code=probe>=0?await Diagnostics.InspectMsiAsync(e.Args[index+1]):argbTest>=0?await Diagnostics.TestArgbAsync(e.Args[index+1]):await Diagnostics.TestGpuAsync(e.Args[index+1]);
            Shutdown(code);return;
        }
        DispatcherUnhandledException+=(_,args)=>
        {
            try{Directory.CreateDirectory(Storage.DirectoryPath);File.AppendAllText(Path.Combine(Storage.DirectoryPath,"error.log"),DateTimeOffset.Now+" "+args.Exception+Environment.NewLine);}catch{}
            MessageBox.Show(args.Exception.Message,"Prism RGB",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;
        };
        int preview=Array.IndexOf(e.Args,"--render-preview");
        int smoke=Array.IndexOf(e.Args,"--ui-smoke");
        int benchmark=Array.IndexOf(e.Args,"--benchmark");
        bool startup=Array.IndexOf(e.Args,"--startup")>=0,diagnostic=preview>=0 || smoke>=0 || benchmark>=0;
        if(Array.IndexOf(e.Args,"--quit")>=0){SingleInstanceLease.RequestQuit();Shutdown();return;}
        if(!diagnostic)
        {
            instance=SingleInstanceLease.Acquire(!startup);
            if(instance is null){Shutdown();return;}
        }
        var main=new MainWindow(smoke>=0?new FakeStartupRegistration():null);MainWindow=main;
        instance?.Listen(()=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(main.OpenFromLauncher));},()=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(async()=>await main.QuitFromLauncherAsync()));});
        SessionEnding+=(_,_)=>main.PrepareForWindowsShutdown();
        if(startup && !diagnostic){await main.RunWindowsStartupAsync();return;}
        if(preview>=0 || smoke>=0){main.ShowInTaskbar=false;main.Left=-10000;main.Top=-10000;main.WindowStartupLocation=WindowStartupLocation.Manual;}
        main.Show();
        if(benchmark>=0 && benchmark+2<e.Args.Length)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{await main.RunBenchmarkAsync(e.Args[benchmark+1],e.Args[benchmark+2]);await main.CloseForPreview();Shutdown();}
            catch(Exception ex){File.WriteAllText(e.Args[benchmark+1]+".error.txt",ex.ToString());await main.CloseForPreview();Shutdown(1);}
            return;
        }
        if(smoke>=0 && smoke+1<e.Args.Length)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{await Task.Delay(700);await main.RunUiSmokeAsync(e.Args[smoke+1]);await main.CloseForPreview();Shutdown();}
            catch(Exception ex){File.WriteAllText(Path.Combine(e.Args[smoke+1],"ui-error.txt"),ex.ToString());await main.CloseForPreview();Shutdown(1);}
            return;
        }
        if(preview>=0 && preview+1<e.Args.Length)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            await Task.Delay(1200);main.RenderPreview(e.Args[preview+1]);await main.CloseForPreview();Shutdown();
        }
    }
}
