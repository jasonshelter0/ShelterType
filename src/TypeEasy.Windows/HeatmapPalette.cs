using System.Windows;
using System.Windows.Media;

namespace TypeEasy.Windows;

/// <summary>Display-only palette. It never changes article observations or combines timing samples.</summary>
internal static class HeatmapPalette
{
    internal static readonly Color[] Stops={Color.FromRgb(119,187,242),Color.FromRgb(146,218,224),Color.FromRgb(218,230,166),Color.FromRgb(247,209,119),Color.FromRgb(242,158,91)};
    internal static double Position(double value,bool errors,bool relative)
    {
        if(!double.IsFinite(value))return 0;
        if(errors)return Math.Clamp(value/.30,0,1);
        if(value<=0)return 0;
        // A logarithmic time scale gives equal space to equal ratios. The article baseline is centered.
        return Math.Clamp(relative?(Math.Log2(value)+1)/2:Math.Log2(value/.10)/4,0,1);
    }
    internal static Color At(double normalized,bool continuous=true)
    {
        var scaled=Math.Clamp(normalized,0,1)*4;
        if(!continuous)return Stops[(int)Math.Round(scaled,MidpointRounding.AwayFromZero)];
        var index=Math.Min(3,(int)Math.Floor(scaled));return Mix(Stops[index],Stops[index+1],scaled-index);
    }
    internal static Color Mix(Color first,Color second,double fraction)=>Color.FromRgb(
        (byte)Math.Round(first.R+(second.R-first.R)*fraction),(byte)Math.Round(first.G+(second.G-first.G)*fraction),(byte)Math.Round(first.B+(second.B-first.B)*fraction));
    internal static Brush Legend(bool continuous)
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5),ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation};
        if(continuous)for(var i=0;i<Stops.Length;i++)brush.GradientStops.Add(new GradientStop(Stops[i],i/4.0));
        else for(var i=0;i<Stops.Length;i++)
        {
            brush.GradientStops.Add(new GradientStop(Stops[i],i==0?0:(i-.5)/4));
            brush.GradientStops.Add(new GradientStop(Stops[i],i==4?1:(i+.5)/4));
        }
        brush.Freeze();return brush;
    }
    internal static LinearGradientBrush CharacterBrush(Color left,Color center,Color right)
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5),ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation};
        brush.GradientStops.Add(new GradientStop(left,0));brush.GradientStops.Add(new GradientStop(center,.12));
        brush.GradientStops.Add(new GradientStop(center,.88));brush.GradientStops.Add(new GradientStop(right,1));brush.Freeze();return brush;
    }
}

