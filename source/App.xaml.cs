using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PrismRGB;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int dataDir=Array.IndexOf(e.Args,"--data-dir");
        if(dataDir>=0 && dataDir+1<e.Args.Length)Storage.DirectoryPath=Path.GetFullPath(e.Args[dataDir+1]);
        if(Array.IndexOf(e.Args,"--self-test")>=0)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            int code=await SelfTests.RunAsync();Shutdown(code);return;
        }
        DispatcherUnhandledException+=(_,args)=>
        {
            try{Directory.CreateDirectory(Storage.DirectoryPath);File.AppendAllText(Path.Combine(Storage.DirectoryPath,"error.log"),DateTimeOffset.Now+" "+args.Exception+Environment.NewLine);}catch{}
            MessageBox.Show(args.Exception.Message,"Prism RGB",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;
        };
        int preview=Array.IndexOf(e.Args,"--render-preview");
        int smoke=Array.IndexOf(e.Args,"--ui-smoke");
        var main=new MainWindow();MainWindow=main;
        if(preview>=0 || smoke>=0){main.ShowInTaskbar=false;main.Left=-10000;main.Top=-10000;main.WindowStartupLocation=WindowStartupLocation.Manual;}
        main.Show();
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
