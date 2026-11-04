using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TypeEasy.Windows;

internal static class StatsUi
{
    public static void Window(Window window, string title, double width=1000, double height=790)
    {
        window.Title=title; window.Width=width; window.Height=height; window.MinWidth=820;window.MinHeight=620;
        window.FontFamily=new FontFamily("Microsoft YaHei UI");window.FontSize=14;
        window.Language=XmlLanguage.GetLanguage(UiText.Culture.IetfLanguageTag);
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Ink");
        window.SourceInitialized+=(_,_)=>TitleTheme(window);window.Activated+=(_,_)=>TitleTheme(window);
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    private static void TitleTheme(Window window)
    {
        var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
        var color=(window.TryFindResource("Bg") as SolidColorBrush)?.Color??Colors.Black;
        int dark=color.R+color.G+color.B<384?1:0;DwmSetWindowAttribute(handle,20,ref dark,4);
    }
    public static TextBlock Text(string text,double size=14,string color="Ink")
    {
        var t=new TextBlock{Text=text,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=size,TextWrapping=TextWrapping.Wrap,LineHeight=size*1.55,UseLayoutRounding=true};
        TextOptions.SetTextFormattingMode(t,TextFormattingMode.Display);
        t.SetResourceReference(TextBlock.ForegroundProperty,color);return t;
    }
    public static Button Button(string text,RoutedEventHandler click)
    {var b=new Button{Content=text,Margin=new Thickness(0,0,8,0)};b.Click+=click;return b;}
    public static Border Card(UIElement child,Thickness? margin=null)
    {var b=new Border{Child=child,Padding=new Thickness(18),CornerRadius=new CornerRadius(9),Margin=margin??new Thickness(0,0,0,14)};b.SetResourceReference(Border.BackgroundProperty,"Panel");return b;}
    public static ComboBox Choice(double width=150)
    {
        var box=new ComboBox{Width=width,Margin=new Thickness(0,4,12,0),MinHeight=37,VerticalContentAlignment=VerticalAlignment.Center};
        box.SetResourceReference(Control.ForegroundProperty,"Ink");
        box.Template=(ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox"><Grid>
 <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}" ClickMode="Press"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource Field}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="5" Padding="10,7"><DockPanel><TextBlock Text="▾" DockPanel.Dock="Right" Foreground="{DynamicResource Muted}"/><ContentPresenter/></DockPanel></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
 <ContentPresenter Margin="10,6,27,6" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" VerticalAlignment="Center"/>
 <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Line}" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="320"><ScrollViewer VerticalScrollBarVisibility="Auto"><ItemsPresenter/></ScrollViewer></Border></Popup>
</Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
""");
        var style=new Style(typeof(ComboBoxItem));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        style.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));
        style.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Panel")));
        box.ItemContainerStyle=style;return box;
    }
    public static void NamedItems(ComboBox combo,string property)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(property));
        text.SetBinding(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding(property));
        text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        combo.ItemTemplate=new DataTemplate{VisualTree=text};
    }
    public static Expander Explanation(string text,string? title=null)
    {var content=Text(text,12,"Muted");content.Margin=new Thickness(0,10,0,0);var e=new Expander{Header=Text(title??UiText.T("计算说明","How it is calculated","Mode de calcul"),12,"Muted"),Content=new ScrollViewer{Content=content,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Margin=new Thickness(0,8,0,10)};e.SetResourceReference(Control.ForegroundProperty,"Ink");return e;}
    public static StackPanel Field(string name,UIElement element)
    {var s=new StackPanel{Margin=new Thickness(0,0,0,6)};s.Children.Add(Text(name,11,"Muted"));s.Children.Add(element);return s;}
    public static string Key(string s)=>UiText.Key(s);
    public static string Mode(string mode)=>UiText.Mode(mode);
}

internal sealed record ChartValue(string Label,double Value,string Detail);
/// <summary>Small native WPF chart with real values and hover details. No browser or chart dependency.</summary>
internal sealed class NativeChart : FrameworkElement
{
    public IReadOnlyList<ChartValue> Values {get;set;}=Array.Empty<ChartValue>();
    public bool Bars {get;set;}
    public double? Maximum {get;set;}
    public string Unit {get;set;}="";
    public string EmptyText {get;set;}=UiText.T("暂无记录。","No records yet.","Aucun résultat pour le moment.");
    public NativeChart(){Height=210;MinWidth=250;MouseMove+=Hover;MouseLeave+=(_,_)=>ToolTip=null;}
    private Brush Brush(string name)=>TryFindResource(name) as Brush??Brushes.Gray;
    private void Label(DrawingContext dc,string text,double x,double y,double size=11,string color="Muted")
    {var ft=new FormattedText(text,UiText.Culture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(ft,new Point(x,y));}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var width=ActualWidth;var height=ActualHeight;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,width,height));
        if(Values.Count==0){Label(dc,EmptyText,12,65,13);return;}
        const double left=43,top=20,bottom=36,right=14;
        var plotW=Math.Max(1,width-left-right);var plotH=Math.Max(1,height-top-bottom);
        var maximum=Maximum??Math.Max(1,Values.Max(x=>x.Value)*1.15);
        for(var i=0;i<=3;i++){var y=top+plotH*i/3;dc.DrawLine(new Pen(Brush("Line"),.7),new Point(left,y),new Point(width-right,y));Label(dc,(maximum*(3-i)/3).ToString("0.#",UiText.Culture),2,y-7);}
        Label(dc,Unit,left,0,10);
        Point? previous=null;
        for(var i=0;i<Values.Count;i++)
        {
            var x=Bars?left+plotW*(i+.5)/Values.Count:left+(Values.Count==1?plotW/2:plotW*i/(Values.Count-1));
            var y=top+plotH*(1-Math.Clamp(Values[i].Value/maximum,0,1));
            if(Bars){var bw=Math.Max(2,Math.Min(30,plotW/Values.Count*.65));dc.DrawRoundedRectangle(Brush("Accent"),null,new Rect(x-bw/2,y,bw,Math.Max(1,top+plotH-y)),2,2);}
            else{if(previous is {} p)dc.DrawLine(new Pen(Brush("Accent"),2),p,new Point(x,y));dc.DrawEllipse(Brush("Accent"),null,new Point(x,y),3.5,3.5);previous=new Point(x,y);}
            var every=Math.Max(1,(int)Math.Ceiling(Values.Count/6.0));
            var spacing=Bars?plotW/Values.Count:plotW/Math.Max(1,Values.Count-1);
            if(i==Values.Count-1||(i%every==0&&(Values.Count-1-i)*spacing>=60))Label(dc,Values[i].Label,Math.Clamp(x-18,0,Math.Max(0,width-60)),height-bottom+10,10);
        }
    }
    private void Hover(object sender,MouseEventArgs e)
    {
        if(Values.Count==0)return;var fraction=Math.Clamp((e.GetPosition(this).X-43)/Math.Max(1,ActualWidth-57),0,1);
        var index=Bars?Math.Min(Values.Count-1,(int)(fraction*Values.Count)):(int)Math.Round(fraction*(Values.Count-1));
        ToolTip=Values[index].Detail;
    }
}






