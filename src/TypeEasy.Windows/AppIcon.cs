using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TypeEasy.Windows;

public enum AppIconKind
{
    Settings,
    Statistics,
    SpeedTest,
    SidebarOpen,
    SidebarClosed,
    Focus,
    ExitFocus,
    Info,
    Dictionary,
    Shuffle,
    Articles,
    Ai,
    Update
}

/// <summary>
/// Small vector toolbar icons on a 24-unit grid. Colour is inherited from the button;
/// the button supplies the translated tooltip and accessible name.
/// </summary>
public sealed class AppIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(AppIconKind), typeof(AppIcon),
        new FrameworkPropertyMetadata(AppIconKind.Settings, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(AppIcon), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public AppIconKind Kind
    {
        get => (AppIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private static readonly Geometry Gear = CreateGear();
    private static readonly Geometry ExpandCorners = Shape("M8,3 H3 V8 M16,3 H21 V8 M3,16 V21 H8 M16,21 H21 V16");
    private static readonly Geometry CollapseCorners = Shape("M3,8 H8 V3 M16,3 V8 H21 M3,16 H8 V21 M16,21 V16 H21");
    private static readonly Geometry ChevronLeft = Shape("M15,9 L12,12 15,15");
    private static readonly Geometry ChevronRight = Shape("M12,9 L15,12 12,15");

    static AppIcon()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(false));
    }

    public AppIcon()
    {
        Width = Height = 22;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || Foreground is null) return;
        var pen = new Pen(Foreground, 1.7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        drawing.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawing.PushTransform(new ScaleTransform(size / 24, size / 24));
        switch (Kind)
        {
            case AppIconKind.Dictionary:
                drawing.DrawGeometry(null,pen,Shape("M12,5 C8,2 4,3 2,4 V20 C6,18 9,18 12,21 C15,18 18,18 22,20 V4 C18,2 15,3 12,5 V21"));break;
            case AppIconKind.Shuffle:
                drawing.DrawGeometry(null,pen,Shape("M3,6 H6 C12,6 12,18 18,18 H21 M18,15 L21,18 18,21 M3,18 H6 C12,18 12,6 18,6 H21 M18,3 L21,6 18,9"));break;
            case AppIconKind.Articles:
                drawing.DrawGeometry(null,pen,Shape("M5,2 H16 L21,7 V22 H5 Z M9,10 H17 M9,14 H17 M9,18 H15"));break;
            case AppIconKind.Update:
                drawing.DrawGeometry(null,pen,Geometry.Parse("M12,3 L12,15 M7,10 L12,15 L17,10 M4,17 L4,21 L20,21 L20,17"));break;
            case AppIconKind.Ai:
                drawing.DrawGeometry(null,pen,Shape("M12,2 L15,9 22,12 15,15 12,22 9,15 2,12 9,9 Z"));break;
            case AppIconKind.Settings:
                drawing.DrawGeometry(null, pen, Gear);
                drawing.DrawEllipse(null, pen, new Point(12, 12), 3.1, 3.1);
                break;
            case AppIconKind.Statistics:
                drawing.DrawRoundedRectangle(null, pen, new Rect(3.5, 13, 3.5, 7), 0.8, 0.8);
                drawing.DrawRoundedRectangle(null, pen, new Rect(10.25, 4, 3.5, 16), 0.8, 0.8);
                drawing.DrawRoundedRectangle(null, pen, new Rect(17, 9, 3.5, 11), 0.8, 0.8);
                break;
            case AppIconKind.SpeedTest:
                drawing.DrawLine(pen, new Point(9, 2.5), new Point(15, 2.5));
                drawing.DrawLine(pen, new Point(12, 2.5), new Point(12, 5.5));
                drawing.DrawLine(pen, new Point(17.7, 7.5), new Point(19.3, 5.9));
                drawing.DrawEllipse(null, pen, new Point(12, 13.5), 7.5, 7.5);
                drawing.DrawLine(pen, new Point(12, 13.5), new Point(14.9, 10.6));
                break;
            case AppIconKind.SidebarOpen:
            case AppIconKind.SidebarClosed:
                drawing.DrawRoundedRectangle(null, pen, new Rect(3, 4, 18, 16), 1.7, 1.7);
                drawing.DrawLine(pen, new Point(8, 4), new Point(8, 20));
                // The chevron points to the action available in the current sidebar state.
                drawing.DrawGeometry(null, pen, Kind == AppIconKind.SidebarOpen ? ChevronLeft : ChevronRight);
                break;
            case AppIconKind.Focus:
                drawing.DrawGeometry(null, pen, ExpandCorners);
                break;
            case AppIconKind.ExitFocus:
                drawing.DrawGeometry(null, pen, CollapseCorners);
                break;
            case AppIconKind.Info:
                drawing.DrawEllipse(null, pen, new Point(12, 12), 8.5, 8.5);
                drawing.DrawEllipse(Foreground, null, new Point(12, 7.5), 0.9, 0.9);
                drawing.DrawLine(pen, new Point(12, 11), new Point(12, 16.5));
                break;
        }
        drawing.Pop();
        drawing.Pop();
    }

    private static Geometry Shape(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateGear()
    {
        // Eight compact teeth; rounded joins keep the small outline legible at 100% scaling.
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Point At(double degrees, double radius)
            {
                var radians = degrees * Math.PI / 180;
                return new Point(12 + Math.Cos(radians) * radius, 12 + Math.Sin(radians) * radius);
            }
            var started = false;
            for (var tooth = 0; tooth < 8; tooth++)
            {
                var angle = -90 + tooth * 45;
                foreach (var (offset, radius) in new[] { (-22.5, 7.2), (-12.0, 7.2), (-10.0, 9.3), (10.0, 9.3), (12.0, 7.2) })
                {
                    var point = At(angle + offset, radius);
                    if (!started) { context.BeginFigure(point, false, true); started = true; }
                    else context.LineTo(point, true, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
