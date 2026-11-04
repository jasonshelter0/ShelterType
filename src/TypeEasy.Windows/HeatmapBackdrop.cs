using System.Windows;
using System.Windows.Media;

namespace TypeEasy.Windows;

internal sealed record HeatBand(Rect Bounds,Brush Brush);
/// <summary>One painted band per contiguous sampled line region avoids per-glyph antialias seams.</summary>
internal sealed class HeatmapBackdrop : FrameworkElement
{
    internal IReadOnlyList<HeatBand> Bands {get;private set;}=Array.Empty<HeatBand>();
    internal HeatmapBackdrop(){IsHitTestVisible=false;}
    internal void SetBands(IReadOnlyList<HeatBand> bands){Bands=bands;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        foreach(var band in Bands)drawing.DrawRectangle(band.Brush,null,band.Bounds);
    }
}
