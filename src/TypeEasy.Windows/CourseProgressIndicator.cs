using System.Windows;
using System.Windows.Media;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>A small course status mark; shape and text alternatives make it independent of colour.</summary>
public sealed class CourseProgressIndicator : FrameworkElement
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(CourseProgressStatus), typeof(CourseProgressIndicator), new FrameworkPropertyMetadata(CourseProgressStatus.NotStarted, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(CourseProgressIndicator), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(CourseProgressIndicator), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(CourseProgressIndicator), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public CourseProgressStatus Status { get => (CourseProgressStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public CourseProgressIndicator()
    {
        Width = Height = 18; IsHitTestVisible = false; Focusable = false;
        SetResourceReference(AccentProperty, "Accent"); SetResourceReference(TrackProperty, "Muted");
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var centre = new Point(9, 9);
        var pen = new Pen(Accent, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (Status == CourseProgressStatus.Unknown)
        {
            drawing.DrawLine(new Pen(Track, 1.5), new Point(5, 9), new Point(13, 9)); return;
        }
        if (Status == CourseProgressStatus.Completed)
        {
            drawing.PushOpacity(.10); drawing.DrawEllipse(Accent, null, centre, 6.2, 6.2); drawing.Pop();
            drawing.DrawEllipse(null, pen, centre, 6.2, 6.2);
            var checkPen = new Pen(Accent, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawing.DrawGeometry(null, checkPen, Geometry.Parse("M 5.4,9 L 7.9,11.4 L 12.7,6.8")); return;
        }
        drawing.PushOpacity(.42); drawing.DrawEllipse(null, new Pen(Track, 1.4), centre, 6.2, 6.2); drawing.Pop();
        if (Status != CourseProgressStatus.InProgress) return;
        var fraction = Math.Clamp(double.IsFinite(Fraction) ? Fraction : 0, 0, .9999);
        if (fraction < .005) { drawing.DrawEllipse(Accent, null, new Point(9, 2.8), 1.35, 1.35); return; }
        var angle = fraction * Math.PI * 2 - Math.PI / 2;
        var end = new Point(9 + 6.2 * Math.Cos(angle), 9 + 6.2 * Math.Sin(angle));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(new Point(9, 2.8), false, false);
            context.ArcTo(end, new Size(6.2, 6.2), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze(); drawing.DrawGeometry(null, pen, arc);
    }
}
