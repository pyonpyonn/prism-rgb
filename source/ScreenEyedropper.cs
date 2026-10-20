using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Forms=System.Windows.Forms;
using Media=System.Windows.Media;

namespace PrismRGB;

public static class ScreenEyedropper
{
    // A temporary STA/DPI context keeps physical-pixel picking independent of WPF window scaling.
    static Task<T> OnPickerThread<T>(Func<T> run)
    {
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            IntPtr previous=IntPtr.Zero;
            try
            {
                previous=SetThreadDpiAwarenessContext(new IntPtr(-4));if(previous==IntPtr.Zero)throw new Win32Exception();
                Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.ThrowException,true);result.TrySetResult(run());
            }
            catch(Exception e){result.TrySetException(e);}
            finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }){IsBackground=true,Name="Prism color eyedropper"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    public static Task<Media.Color?> PickAsync()=>OnPickerThread<Media.Color?>(()=>
    {
        var snapshots=new List<ScreenImage>();var overlays=new List<PickerOverlay>();Media.Color? selected=null;
        try
        {
            // Capture before showing any overlay. Pictures exist only in memory until this selection ends.
            foreach(var screen in Forms.Screen.AllScreens)snapshots.Add(ScreenImage.Capture(screen.Bounds));
            if(snapshots.Count==0)throw new InvalidOperationException("No display is available.");
            bool finishing=false;
            void Finish(Media.Color? color)
            {
                if(finishing)return;finishing=true;selected=color;
                for(int i=overlays.Count-1;i>=0;i--)overlays[i].Close();
            }
            foreach(var image in snapshots)overlays.Add(new PickerOverlay(image,Finish));
            var first=overlays[0];first.Shown+=(_,_)=>{for(int i=1;i<overlays.Count;i++)overlays[i].Show(first);first.Activate();};
            first.ShowDialog();return selected;
        }
        finally{foreach(var overlay in overlays)overlay.Dispose();foreach(var image in snapshots)image.Dispose();}
    });
    internal sealed class ScreenImage : IDisposable
    {
        public Rectangle Bounds {get;}
        public Bitmap Bitmap {get;}
        public ScreenImage(Rectangle bounds,Bitmap bitmap){Bounds=bounds;Bitmap=bitmap;}
        public static ScreenImage Capture(Rectangle bounds)
        {
            var bitmap=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try{using var graphics=Graphics.FromImage(bitmap);graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);return new(bounds,bitmap);}
            catch{bitmap.Dispose();throw;}
        }
        public Media.Color? Sample(int physicalX,int physicalY)
        {
            if(!Bounds.Contains(physicalX,physicalY))return null;
            var pixel=Bitmap.GetPixel(physicalX-Bounds.X,physicalY-Bounds.Y);return Media.Color.FromRgb(pixel.R,pixel.G,pixel.B);
        }
        public void Dispose()=>Bitmap.Dispose();
    }
    sealed class PickerOverlay : Forms.Form
    {
        readonly ScreenImage image;readonly Action<Media.Color?> finish;Rectangle lastHud;Point pointer=new(25,25);bool completed;
        public PickerOverlay(ScreenImage snapshot,Action<Media.Color?> complete)
        {
            image=snapshot;finish=complete;AutoScaleMode=Forms.AutoScaleMode.None;FormBorderStyle=Forms.FormBorderStyle.None;
            StartPosition=Forms.FormStartPosition.Manual;Bounds=image.Bounds;TopMost=true;ShowInTaskbar=false;KeyPreview=true;Cursor=Forms.Cursors.Cross;
            BackgroundImage=image.Bitmap;BackgroundImageLayout=Forms.ImageLayout.None;DoubleBuffered=true;
            Shown+=(_,_)=>
            {
                SetWindowPos(Handle,new IntPtr(-1),image.Bounds.X,image.Bounds.Y,image.Bounds.Width,image.Bounds.Height,0x10);
                if(GetPhysicalCursorPos(out var p))pointer=new Point(p.X-image.Bounds.X,p.Y-image.Bounds.Y);Invalidate();
            };
            FormClosing+=(_,_)=>Complete(null);
            KeyDown+=(_,e)=>{if(e.KeyCode==Forms.Keys.Escape){e.Handled=true;CancelPick();}};
            MouseMove+=(_,e)=>{pointer=e.Location;Invalidate(lastHud);Invalidate(Hud());};
            MouseLeave+=(_,_)=>Invalidate(lastHud);
            MouseDown+=(_,e)=>
            {
                if(e.Button==Forms.MouseButtons.Right){CancelPick();return;}
                if(e.Button==Forms.MouseButtons.Left && GetPhysicalCursorPos(out var p))ChoosePhysicalPoint(p.X,p.Y);
            };
        }
        void Complete(Media.Color? color){if(completed)return;completed=true;finish(color);}
        internal void ChoosePhysicalPoint(int x,int y){var color=image.Sample(x,y);if(color is not null)Complete(color);}
        internal void CancelPick()=>Complete(null);
        protected override void Dispose(bool disposing){if(disposing)BackgroundImage=null;base.Dispose(disposing);}
        Rectangle Hud()
        {
            int x=pointer.X+24,y=pointer.Y+24;
            if(x+255>ClientSize.Width)x=pointer.X-279;if(y+136>ClientSize.Height)y=pointer.Y-160;
            return new(Math.Clamp(x,0,Math.Max(0,ClientSize.Width-255)),Math.Clamp(y,0,Math.Max(0,ClientSize.Height-136)),255,136);
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);if(GetPhysicalCursorPos(out var p) && !image.Bounds.Contains(p.X,p.Y))return;
            var hud=Hud();lastHud=hud;var g=e.Graphics;
            using var dark=new SolidBrush(Color.FromArgb(245,20,28,32));g.FillRectangle(dark,hud);
            int x=Math.Clamp(pointer.X,0,image.Bitmap.Width-1),y=Math.Clamp(pointer.Y,0,image.Bitmap.Height-1);
            var selected=image.Bitmap.GetPixel(x,y);var magnifier=new Rectangle(hud.X+10,hud.Y+10,90,90);
            g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
            g.DrawImage(image.Bitmap,magnifier,new Rectangle(x-4,y-4,9,9),GraphicsUnit.Pixel);
            using var white=new Pen(Color.White,1);using var black=new Pen(Color.Black,3);
            var center=new Rectangle(magnifier.X+40,magnifier.Y+40,10,10);g.DrawRectangle(black,center);g.DrawRectangle(white,center);
            using var color=new SolidBrush(selected);g.FillRectangle(color,new Rectangle(hud.X+112,hud.Y+12,130,32));
            Forms.TextRenderer.DrawText(g,$"#{selected.R:X2}{selected.G:X2}{selected.B:X2}",Font,new Point(hud.X+112,hud.Y+54),Color.White);
            Forms.TextRenderer.DrawText(g,"Click to use this color",Font,new Point(hud.X+112,hud.Y+78),Color.White);
            Forms.TextRenderer.DrawText(g,"Esc / right-click cancels",Font,new Point(hud.X+10,hud.Y+110),Color.White);
        }
    }
    internal static Task<bool> VerifyOverlayAsync()=>OnPickerThread(()=>
    {
        using var bitmap=new Bitmap(300,180);using(var g=Graphics.FromImage(bitmap))g.Clear(Color.FromArgb(91,37,207));
        using var image=new ScreenImage(new Rectangle(60,60,300,180),bitmap);
        Media.Color? selected=null;
        PickerOverlay? active=null;
        using(var overlay=new PickerOverlay(image,color=>{selected=color;active!.Close();}))
        {
            active=overlay;overlay.Shown+=(_,_)=>overlay.BeginInvoke(new Action(()=>overlay.ChoosePhysicalPoint(100,100)));overlay.ShowDialog();
        }
        if(selected!=Media.Color.FromRgb(91,37,207))return false;
        selected=Media.Colors.Red;
        using(var overlay=new PickerOverlay(image,color=>{selected=color;active!.Close();}))
        {
            active=overlay;overlay.Shown+=(_,_)=>overlay.BeginInvoke(new Action(()=>overlay.CancelPick()));overlay.ShowDialog();
        }
        return selected is null;
    });
    internal static Task<bool> VerifyCaptureAsync()=>OnPickerThread(()=>
    {
        using var target=new Forms.Form{FormBorderStyle=Forms.FormBorderStyle.None,StartPosition=Forms.FormStartPosition.Manual,Bounds=new Rectangle(80,80,180,120),TopMost=true,BackColor=Color.FromArgb(35,117,203),ShowInTaskbar=false};
        target.Show();target.Refresh();Forms.Application.DoEvents();
        var point=target.PointToScreen(new Point(50,50));using var snapshot=ScreenImage.Capture(new Rectangle(point.X,point.Y,1,1));
        bool matches=snapshot.Sample(point.X,point.Y)==Media.Color.FromRgb(35,117,203);target.Close();return matches;
    });
    [StructLayout(LayoutKind.Sequential)]struct NativePoint {public int X,Y;}
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]static extern bool GetPhysicalCursorPos(out NativePoint point);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
