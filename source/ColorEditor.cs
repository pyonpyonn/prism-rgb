using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrismRGB;

public static class ColorValues
{
    public static string Hex(Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
    public static bool TryHex(string text,out Color color)
    {
        text=text.Trim();if(!text.StartsWith('#'))text="#"+text;
        color=Colors.Black;if(!Effects.ValidColor(text))return false;color=Effects.Parse(text);return true;
    }
    public static (double Hue,double Saturation,double Value) Hsv(Color c)
    {
        double r=c.R/255.0,g=c.G/255.0,b=c.B/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double hue=delta==0?0:max==r?((g-b)/delta)%6:max==g?(b-r)/delta+2:(r-g)/delta+4;
        return ((hue/6+1)%1,max==0?0:delta/max,max);
    }
}

public sealed class SaturationValueSurface : FrameworkElement
{
    public double Hue,Saturation,Value=1;
    public event Action<double,double>? Changed;
    public SaturationValueSurface()
    {
        Height=215;Focusable=true;Cursor=Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetName(this,"Saturation and value. Use arrow keys to adjust.");
        MouseLeftButtonDown+=(_,e)=>{Focus();CaptureMouse();Choose(e.GetPosition(this));};
        MouseMove+=(_,e)=>{if(IsMouseCaptured)Choose(e.GetPosition(this));};
        MouseLeftButtonUp+=(_,_)=>ReleaseMouseCapture();
        KeyDown+=(_,e)=>
        {
            double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:.01;
            if(e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))return;
            Saturation=Math.Clamp(Saturation+(e.Key==Key.Right?step:e.Key==Key.Left?-step:0),0,1);
            Value=Math.Clamp(Value+(e.Key==Key.Up?step:e.Key==Key.Down?-step:0),0,1);
            Changed?.Invoke(Saturation,Value);InvalidateVisual();e.Handled=true;
        };
    }
    void Choose(Point point)
    {
        Saturation=Math.Clamp(point.X/Math.Max(1,ActualWidth),0,1);Value=1-Math.Clamp(point.Y/Math.Max(1,ActualHeight),0,1);
        Changed?.Invoke(Saturation,Value);InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var rect=new Rect(RenderSize);
        dc.DrawRectangle(new SolidColorBrush(Effects.Hsv(Hue,1,1)),null,rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White,Colors.Transparent,new Point(0,0),new Point(1,0)),null,rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,new Point(0,0),new Point(0,1)),null,rect);
        var point=new Point(Saturation*ActualWidth,(1-Value)*ActualHeight);
        dc.DrawEllipse(null,new Pen(Brushes.Black,4),point,6,6);dc.DrawEllipse(null,new Pen(Brushes.White,2),point,6,6);
    }
}

public sealed class ColorEditor : Window
{
    public Color SelectedColor {get;private set;}
    internal readonly TextBox HexInput=new(),RedInput=new(),GreenInput=new(),BlueInput=new();
    internal readonly Slider HueInput=new(){Minimum=0,Maximum=360,Margin=new Thickness(0,7,0,16)};
    internal readonly SaturationValueSurface Spectrum=new();
    internal Func<System.Threading.Tasks.Task<Color?>> PickScreenColor {get;set;}=ScreenEyedropper.PickAsync;
    readonly Border current=new(),original=new();readonly TextBlock message=new(){Foreground=new SolidColorBrush(Color.FromRgb(239,162,117)),TextWrapping=TextWrapping.Wrap};
    readonly Button apply=new(){Content="Use color",IsDefault=true},dropper=new(){Content="Eyedropper · pick from screen"};
    bool syncing,closed;
    static TextBlock Label(string text)=>new(){Text=text,Foreground=new SolidColorBrush(Color.FromRgb(163,181,175)),Margin=new Thickness(0,5,0,7)};
    static StackPanel Stack(params UIElement[] elements){var panel=new StackPanel();foreach(var e in elements)panel.Children.Add(e);return panel;}
    public ColorEditor(Color initial,string title="Choose color")
    {
        Title=title;Width=610;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Closed+=(_,_)=>closed=true;
        Background=new SolidColorBrush(Color.FromRgb(16,20,23));Foreground=new SolidColorBrush(Color.FromRgb(239,243,241));FontFamily=new FontFamily("Segoe UI");FontSize=13;
        var root=new StackPanel{Margin=new Thickness(24),Background=Background};Content=root;
        root.Children.Add(new TextBlock{Text=title,FontSize=21,FontWeight=FontWeights.SemiBold});root.Children.Add(Label("Choose a shade, enter a value, or pick a color from your screen."));
        root.Children.Add(Spectrum);
        var hueColors=new GradientStopCollection();for(int i=0;i<=6;i++)hueColors.Add(new GradientStop(Effects.Hsv(i/6.0,1,1),i/6.0));
        root.Children.Add(new Border{Height=10,Margin=new Thickness(0,14,0,0),Background=new LinearGradientBrush(hueColors,0)});root.Children.Add(HueInput);
        var row=new Grid();for(int i=0;i<5;i++)row.ColumnDefinitions.Add(new(){Width=new GridLength(i==0?2:1,GridUnitType.Star)});
        var fields=new[]{("Hex",HexInput),("R",RedInput),("G",GreenInput),("B",BlueInput)};
        for(int i=0;i<fields.Length;i++)
        {
            var (name,input)=fields[i];var panel=Stack(Label(name),input);panel.Margin=new Thickness(0,0,8,0);Grid.SetColumn(panel,i);row.Children.Add(panel);
            System.Windows.Automation.AutomationProperties.SetName(input,name=="Hex"?"Hex color":name+" color channel");
        }
        current.CornerRadius=original.CornerRadius=new CornerRadius(5);original.Background=new SolidColorBrush(initial);original.Height=current.Height=24;
        var compare=Stack(Label("Old / new"),original,current);Grid.SetColumn(compare,4);row.Children.Add(compare);root.Children.Add(row);
        var swatches=new WrapPanel{Margin=new Thickness(0,14,0,10)};
        foreach(var hex in new[]{"#FF365E","#FF9B38","#FFE45C","#65E878","#68E0C2","#3BC7FA","#6278FF","#AA69FF","#FA76D4","#FFFFFF","#808080","#000000"})
        {
            string c=hex;var button=new Button{Width=35,Height=28,Margin=new Thickness(0,0,8,6),Padding=new Thickness(0),Background=new SolidColorBrush(Effects.Parse(c)),ToolTip=c};
            System.Windows.Automation.AutomationProperties.SetName(button,"Choose "+c);button.Click+=(_,_)=>SetColor(Effects.Parse(c));swatches.Children.Add(button);
        }
        root.Children.Add(swatches);root.Children.Add(dropper);message.Margin=new Thickness(0,8,0,4);root.Children.Add(message);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,10,0)};actions.Children.Add(cancel);actions.Children.Add(apply);root.Children.Add(actions);
        apply.Background=new SolidColorBrush(Color.FromRgb(131,225,194));apply.Foreground=new SolidColorBrush(Color.FromRgb(17,38,31));
        cancel.Click+=(_,_)=>DialogResult=false;apply.Click+=(_,_)=>{if(InputsValid())DialogResult=true;};
        HexInput.TextChanged+=(_,_)=>{if(syncing)return;if(ColorValues.TryHex(HexInput.Text,out var color))SetColor(color,true);ValidateInputs();};
        foreach(var field in new[]{RedInput,GreenInput,BlueInput})field.TextChanged+=(_,_)=>
        {
            if(syncing)return;
            if(TryChannel(RedInput,out var r) && TryChannel(GreenInput,out var g) && TryChannel(BlueInput,out var b))SetColor(Color.FromRgb(r,g,b));
            ValidateInputs();
        };
        HueInput.ValueChanged+=(_,_)=>{if(!syncing)SetColor(Effects.Hsv(HueInput.Value/360,Spectrum.Saturation,Spectrum.Value),keepHue:true);};
        Spectrum.Changed+=(s,v)=>SetColor(Effects.Hsv(HueInput.Value/360,s,v),keepHue:true);
        dropper.Click+=async(_,_)=>
        {
            dropper.IsEnabled=false;Opacity=0;IsEnabled=false;
            try
            {
                await System.Threading.Tasks.Task.Delay(140);
                var color=await PickScreenColor();if(!closed && color is Color picked)SetColor(picked);
            }
            catch(Exception e){message.Text="Could not pick a screen color: "+e.Message;}
            finally{if(!closed){Opacity=1;IsEnabled=true;Activate();dropper.IsEnabled=true;}}
        };
        SetColor(initial);
    }
    static bool TryChannel(TextBox box,out byte value)=>byte.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value);
    bool InputsValid()=>ColorValues.TryHex(HexInput.Text,out _) && TryChannel(RedInput,out _) && TryChannel(GreenInput,out _) && TryChannel(BlueInput,out _);
    void ValidateInputs()
    {
        bool valid=InputsValid();apply.IsEnabled=valid;message.Text=valid?"":"Use six hex digits and RGB numbers from 0 to 255.";
        HexInput.BorderBrush=new SolidColorBrush(ColorValues.TryHex(HexInput.Text,out _)?Color.FromRgb(52,65,71):Color.FromRgb(239,162,117));
    }
    internal void SetColor(Color color,bool keepHex=false,bool keepHue=false)
    {
        syncing=true;SelectedColor=color;var hsv=ColorValues.Hsv(color);
        if(!keepHue)HueInput.Value=hsv.Hue*360;
        Spectrum.Hue=HueInput.Value/360;Spectrum.Saturation=hsv.Saturation;Spectrum.Value=hsv.Value;Spectrum.InvalidateVisual();
        if(!keepHex)HexInput.Text=ColorValues.Hex(color);
        RedInput.Text=color.R.ToString(CultureInfo.InvariantCulture);GreenInput.Text=color.G.ToString(CultureInfo.InvariantCulture);BlueInput.Text=color.B.ToString(CultureInfo.InvariantCulture);
        current.Background=new SolidColorBrush(color);syncing=false;ValidateInputs();
    }
}

public static class ColorPresets
{
    public static readonly System.Collections.Generic.Dictionary<string,string[]> All=new()
    {
        ["Mint & lilac"]=new[]{"#68E0C2","#A38BFA"},["Ocean"]=new[]{"#052FA8","#04BFFF","#65FFD5"},
        ["Sunset"]=new[]{"#FF385C","#FFAD45","#8B44FF"},["Cherry blossom"]=new[]{"#FF4686","#FFC7E2","#FFFFFF"},
        ["Fire"]=new[]{"#FF170A","#FF8F12","#FFE762"},["Ice"]=new[]{"#3E74FF","#8FEAFF","#FFFFFF"},
        ["Monochrome"]=new[]{"#FFFFFF"},["Full spectrum"]=new[]{"#FF0000","#FFFF00","#00FF00","#00FFFF","#0000FF","#FF00FF"}
    };
}
