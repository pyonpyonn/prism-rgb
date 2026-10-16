using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PrismRGB;

public sealed class LightingSurface : FrameworkElement
{
    public StudioState State {get;set;}=new();
    public double Seconds {get;set;}
    public Color? ScreenColor {get;set;}
    public bool Editing {get;set;}
    public event Action? LayoutChanged;
    LightItem? dragging; Point grab; Rect scene;
    static SolidColorBrush Brush(string h)=>new((Color)ColorConverter.ConvertFromString(h));
    public LightingSurface()
    {
        ClipToBounds=true; MinHeight=300;Cursor=Cursors.Arrow;
        MouseLeftButtonDown+=(_,e)=>
        {
            if(!Editing)return; var p=e.GetPosition(this);
            dragging=State.Layout.LastOrDefault(item=>Box(item).Contains(p));
            if(dragging is not null){grab=new Point(p.X-Box(dragging).X,p.Y-Box(dragging).Y);CaptureMouse();}
        };
        MouseMove+=(_,e)=>
        {
            if(dragging is null)return; var p=e.GetPosition(this);
            dragging.X=Math.Clamp((p.X-grab.X-scene.X)/scene.Width,0,1-dragging.Width);
            dragging.Y=Math.Clamp((p.Y-grab.Y-scene.Y)/scene.Height,0,1-dragging.Height);
            InvalidateVisual();
        };
        MouseLeftButtonUp+=(_,_)=>{if(dragging is not null){dragging=null;ReleaseMouseCapture();LayoutChanged?.Invoke();}};
    }
    Rect Box(LightItem i)=>new(scene.X+i.X*scene.Width,scene.Y+i.Y*scene.Height,i.Width*scene.Width,i.Height*scene.Height);
    void Label(DrawingContext dc,string text,double x,double y,string color="#899195",double size=10)
    {
        var t=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(t,new Point(x,y));
    }
    Color At(double x,double y)=>Effects.Sample(State,x,y,Seconds,ScreenColor);
    LinearGradientBrush Gradient(Rect r)
    {
        var g=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        for(int i=0;i<=6;i++)g.GradientStops.Add(new GradientStop(At((r.X-scene.X)/scene.Width+r.Width/scene.Width*i/6,(r.Y-scene.Y)/scene.Height),i/6.0));
        return g;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRoundedRectangle(Brush("#101619"),null,new Rect(0,0,ActualWidth,ActualHeight),12,12);
        scene=new Rect(25,28,Math.Max(1,ActualWidth-50),Math.Max(1,ActualHeight-65));
        for(double x=14;x<ActualWidth;x+=22) for(double y=15;y<ActualHeight;y+=22)dc.DrawEllipse(Brush("#243033"),null,new Point(x,y),.7,.7);
        foreach(var i in State.Layout)
        {
            var b=Box(i);dc.PushOpacity(i.Enabled?1:.22);
            var g=Gradient(b);var glow=g.Clone();glow.Opacity=.1;
            var halo=b;halo.Inflate(9,9);dc.DrawRoundedRectangle(glow,null,halo,15,15);
            switch(i.Shape)
            {
                case "monitor":
                    dc.DrawRoundedRectangle(Brush("#182126"),new Pen(g,3),b,7,7);
                    var inner=b;inner.Inflate(-8,-8);dc.DrawRoundedRectangle(Brush("#0D1216"),null,inner,3,3);
                    var wash=Gradient(b);wash.Opacity=.15;dc.DrawRoundedRectangle(wash,null,inner,3,3);
                    dc.DrawRectangle(Brush("#2A343A"),null,new Rect(b.X+b.Width*.47,b.Bottom, b.Width*.06,12));
                    dc.DrawRoundedRectangle(Brush("#2A343A"),null,new Rect(b.X+b.Width*.35,b.Bottom+11,b.Width*.3,4),2,2);
                    Label(dc,"PRISM",b.X+b.Width*.43,b.Y+b.Height*.42,"#667C80",Math.Max(9,b.Width*.04));
                    break;
                case "keyboard":
                    dc.DrawRoundedRectangle(Brush("#1D282D"),new Pen(Brush("#344248"),1),b,6,6);
                    double kw=(b.Width-16)/15,kh=(b.Height-16)/5;
                    for(int row=0;row<5;row++) for(int col=0;col<15;col++)
                    {
                        var key=new Rect(b.X+8+col*kw,b.Y+8+row*kh,Math.Max(1,kw-3),Math.Max(1,kh-3));
                        dc.DrawRoundedRectangle(new SolidColorBrush(At(i.X+i.Width*col/15,i.Y+i.Height*row/5)),null,key,2,2);
                    }
                    break;
                case "tower":
                    dc.DrawRoundedRectangle(Brush("#172126"),new Pen(Brush("#344248"),1),b,7,7);
                    for(int fan=0;fan<3;fan++)
                    {
                        double radius=Math.Min(b.Width*.34,b.Height*.13); var center=new Point(b.X+b.Width/2,b.Y+b.Height*(.2+fan*.3));
                        for(int seg=0;seg<32;seg++)
                        {
                            double angle=seg/32.0*Math.PI*2;var p=new Point(center.X+Math.Cos(angle)*radius,center.Y+Math.Sin(angle)*radius);
                            dc.DrawEllipse(new SolidColorBrush(At(i.X+.07+Math.Cos(angle)*.06,i.Y+fan*.15)),null,p,2.4,2.4);
                        }
                        dc.DrawEllipse(Brush("#263238"),null,center,radius*.3,radius*.3);
                    }
                    break;
                case "mouse":
                    dc.DrawRoundedRectangle(Brush("#202A30"),new Pen(g,2),b,b.Width*.45,b.Width*.45);
                    dc.DrawRoundedRectangle(g,null,new Rect(b.X+b.Width*.43,b.Y+b.Height*.2,b.Width*.14,b.Height*.2),2,2);break;
                default:dc.DrawRoundedRectangle(g,null,b,3,3);break;
            }
            if(Editing)dc.DrawRoundedRectangle(null,new Pen(Brush("#80E1C4"),1){DashStyle=DashStyles.Dot},b,5,5);
            if(i.Shape!="strip")Label(dc,i.Name,b.X,b.Bottom+(i.Shape=="monitor"?22:9));
            dc.Pop();
        }
        Label(dc,Editing?"Drag devices to change their position":"VIRTUAL SETUP  /  LIVE EFFECT PREVIEW",22,ActualHeight-24,"#738087",9);
    }
}

public static class ScreenSampler
{
    public static Color Capture()
    {
        var bounds=System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0,0,1920,1080);
        using var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location,System.Drawing.Point.Empty,bounds.Size);
        long r=0,g=0,b=0,count=0;
        for(int y=0;y<bitmap.Height;y+=Math.Max(1,bitmap.Height/18))for(int x=0;x<bitmap.Width;x+=Math.Max(1,bitmap.Width/32))
        {var c=bitmap.GetPixel(x,y);r+=c.R;g+=c.G;b+=c.B;count++;}
        return Color.FromRgb((byte)(r/count),(byte)(g/count),(byte)(b/count));
    }
}
