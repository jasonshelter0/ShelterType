using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TypeEasy.Windows;

// Transitional asset adapter: these root outlines still have kingsoft-2008 provenance.
// The keyboard and layout are native vectors. Replacing this loader replaces all legacy roots.
internal static class VectorRoots
{
    private sealed record Entry(string Id,string Key,string ImagePath);
    private static readonly Dictionary<string,IReadOnlyDictionary<string,List<Geometry>>> cache=new();
    private static readonly Dictionary<string,Geometry> glyphsByPath=new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string,List<Geometry>> Load(string variant)
    {
        if(cache.TryGetValue(variant,out var cached))return cached;
        var result=new Dictionary<string,List<Geometry>>();
        var path=Path.Combine(AppContext.BaseDirectory,"Teaching","root-glyphs"+variant+".json");
        if(!File.Exists(path))return result;
        foreach(var entry in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path))??new())
        {
            if(entry.Key.Length!=1 || entry.Key[0]<'a'||entry.Key[0]>'y' || entry.ImagePath!="Teaching/glyphs"+variant+"/"+entry.Id+".png" || entry.Id.Length!=2)continue;
            var file=Path.Combine(AppContext.BaseDirectory,entry.ImagePath);
            if(!File.Exists(file))continue;
            using var input=File.OpenRead(file);
            var bitmap=new FormatConvertedBitmap(BitmapFrame.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad),PixelFormats.Bgra32,null,0);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            var geometry=new StreamGeometry{FillRule=FillRule.EvenOdd};
            bool Ink(int x,int y){if(x<0||y<0||x>=bitmap.PixelWidth||y>=bitmap.PixelHeight)return false;var p=(y*bitmap.PixelWidth+x)*4;return pixels[p+3]>128 && pixels[p]+pixels[p+1]+pixels[p+2]<384;}
            var edges=new Dictionary<Point,List<Point>>();
            void Edge(Point a,Point b){if(!edges.ContainsKey(a))edges[a]=new();edges[a].Add(b);}
            for(int y=0;y<bitmap.PixelHeight;y++)for(int x=0;x<bitmap.PixelWidth;x++)
            {
                if(!Ink(x,y))continue;
                if(!Ink(x,y-1))Edge(new(x,y),new(x+1,y));
                if(!Ink(x+1,y))Edge(new(x+1,y),new(x+1,y+1));
                if(!Ink(x,y+1))Edge(new(x+1,y+1),new(x,y+1));
                if(!Ink(x-1,y))Edge(new(x,y+1),new(x,y));
            }
            using(var context=geometry.Open())
            {
                while(edges.Count>0)
                {
                    var first=edges.Keys.First();var point=first;var contour=new List<Point>{first};
                    do{var next=edges[point][0];edges[point].RemoveAt(0);if(edges[point].Count==0)edges.Remove(point);point=next;contour.Add(point);}while(point!=first&&edges.ContainsKey(point));
                    var simplified=Simplify(contour,0.7);
                    context.BeginFigure(simplified[0],true,true);
                    foreach(var vertex in simplified.Skip(1))context.LineTo(vertex,true,false);
                }
            }
            geometry.Freeze();var key=entry.Key.ToUpperInvariant();
            glyphsByPath[entry.ImagePath]=geometry;
            if(!result.ContainsKey(key))result[key]=new();result[key].Add(geometry);
        }
        cache[variant]=result;return result;
    }
    public static bool TryLessonImage(string path,Brush ink,out ImageSource? image)
    {
        image=null;
        var match=System.Text.RegularExpressions.Regex.Match(path.Replace('\\','/'),@"^Teaching/(roots|glyphs)(86|98)/([a-yA-Y][0-9]?)\.png$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(!match.Success)return false;
        var roots=Load(match.Groups[2].Value);
        var shapes=new List<Geometry>();
        if(match.Groups[1].Value.Equals("glyphs",StringComparison.OrdinalIgnoreCase))
        {if(glyphsByPath.TryGetValue(path.Replace('\\','/'),out var glyph))shapes.Add(glyph);}
        else if(roots.TryGetValue(match.Groups[3].Value.ToUpperInvariant(),out var group))shapes.AddRange(group);
        if(shapes.Count==0)return true;
        ink=ink.CloneCurrentValue();
        var drawing=new DrawingGroup();
        using(var context=drawing.Open())
        {
            // A stable cell preserves whitespace and prevents a one-stroke root from stretching.
            context.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,shapes.Count*56,56));
            for(int i=0;i<shapes.Count;i++)Draw(context,shapes[i],new Rect(i*56+6,6,44,44),ink);
        }
        drawing.Freeze();image=new DrawingImage(drawing);image.Freeze();return true;
    }
    private static List<Point> Simplify(List<Point> points,double tolerance)
    {
        if(points.Count<=3)return points;
        var a=points[0];var b=points[^1];var vector=b-a;var max=0d;var split=0;
        for(int i=1;i<points.Count-1;i++)
        {
            var t=vector.LengthSquared==0?0:Math.Clamp(Vector.Multiply(points[i]-a,vector)/vector.LengthSquared,0,1);
            var distance=(points[i]-(a+t*vector)).Length;if(distance>max){max=distance;split=i;}
        }
        if(max<=tolerance)return new(){a,b};
        var left=Simplify(points.GetRange(0,split+1),tolerance);var right=Simplify(points.GetRange(split,points.Count-split),tolerance);
        left.RemoveAt(left.Count-1);left.AddRange(right);return left;
    }
    public static void Draw(DrawingContext dc,Geometry geometry,Rect cell,Brush brush)
    {
        var b=geometry.Bounds;if(b.IsEmpty||b.Width==0||b.Height==0)return;
        var scale=Math.Min(cell.Width/b.Width,cell.Height/b.Height);
        dc.PushTransform(new TranslateTransform(cell.X+(cell.Width-b.Width*scale)/2-b.X*scale,cell.Y+(cell.Height-b.Height*scale)/2-b.Y*scale));
        dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawGeometry(brush,null,geometry);dc.Pop();dc.Pop();
    }
}

public sealed class RootKeyboardWindow:Window
{
    private readonly Action<string> selectRoot;
    public bool SelectKey(string key)
    {
        key=key.ToUpperInvariant();if(key.Length!=1||key[0]<'A'||key[0]>'Z')return false;
        selectRoot(key);return true;
    }
    public RootKeyboardWindow(string variant="86")
    {
        StatsUi.Window(this,UiText.T("五笔字根","Wubi roots","Radicaux Wubi"),1200,720);
        var body=new StackPanel{Margin=new Thickness(24)};
        var mode=StatsUi.Choice(150);mode.Items.Add("86");mode.Items.Add("98");
        body.Children.Add(mode);
        var keyboard=new TeachingKeyboard{ShowHands=false,IsHitTestVisible=true,Cursor=System.Windows.Input.Cursors.Hand};body.Children.Add(keyboard);
        var title=StatsUi.Text("",22);body.Children.Add(title);
        var detail=new WrapPanel{Margin=new Thickness(0,16,0,0)};body.Children.Add(detail);
        void Select(string key)
        {
            var zone=TeachingKeyboard.WubiZone(key);
            title.Text=key+(zone==4?UiText.T("  折区 NBVCX","  Zone 5 NBVCX","  Zone 5 NBVCX"):"");detail.Children.Clear();
            keyboard.SetTarget(char.ToLowerInvariant(key[0]));
            if(keyboard.RootGlyphs is null||!keyboard.RootGlyphs.TryGetValue(key,out var roots))return;
            foreach(var root in roots){var shape=new System.Windows.Shapes.Path{Data=root,Stretch=Stretch.Uniform,Width=48,Height=48,Margin=new Thickness(0,0,22,16)};shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Ink");detail.Children.Add(shape);}
        }
        selectRoot=Select;
        keyboard.KeySelected+=Select;
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this,false);
        PreviewKeyDown+=(_,e)=>{
            if(System.Windows.Input.Keyboard.Modifiers!=System.Windows.Input.ModifierKeys.None)return;
            var key=e.Key==System.Windows.Input.Key.ImeProcessed?e.ImeProcessedKey:e.Key;
            if(key>=System.Windows.Input.Key.A&&key<=System.Windows.Input.Key.Z)
            {SelectKey(((char)('A'+key-System.Windows.Input.Key.A)).ToString());e.Handled=true;}
        };
        mode.SelectionChanged+=(_,_)=>{try{keyboard.RootGlyphs=VectorRoots.Load(mode.SelectedIndex==1?"98":"86");keyboard.InvalidateVisual();Select("G");}catch(Exception e)when(e is IOException or JsonException or NotSupportedException){title.Text=e.Message;}};
        Content=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};mode.SelectedIndex=variant=="98"?1:0;
    }
}
