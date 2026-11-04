using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeEasy.Windows;

/// <summary>The ShelterType keycap mark, drawn natively at any DPI.</summary>
public sealed class BrandMark : Control
{
    private static readonly Geometry Symbol = Geometry.Parse("M128 28 L32 84 L128 140 Z M128 116 L224 172 L128 228 Z");
    private static readonly Brush Mint = new SolidColorBrush(Color.FromRgb(103, 203, 176));

    public BrandMark()
    {
        SetResourceReference(ForegroundProperty, "BrandInk");
        Focusable = false;
        IsHitTestVisible = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "ShelterType");
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double scale = Math.Min(ActualWidth, ActualHeight) / 256;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 256 * scale) / 2, (ActualHeight - 256 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRoundedRectangle(null, new Pen(Foreground, 12), new Rect(25, 25, 206, 206), 37, 37);
        dc.PushTransform(new TranslateTransform(39, 25));
        dc.PushTransform(new ScaleTransform(.695, .695));
        dc.DrawGeometry(Mint, null, Symbol);
        dc.Pop(); dc.Pop();
        dc.DrawLine(new Pen(Foreground, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(97, 202), new Point(159, 202));
        dc.Pop(); dc.Pop();
    }
}
