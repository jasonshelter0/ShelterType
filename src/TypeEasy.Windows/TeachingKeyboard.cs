using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>Vector-only ANSI keyboard and finger guide. No bitmap, browser, or external UI dependency.</summary>
public sealed class TeachingKeyboard : UserControl
{
    private sealed record DrawKey(string Id, string Label, double X, double Y, double Width, double Height, TypingFinger? Finger);
    private readonly DispatcherTimer _demoTimer;
    private readonly List<DrawKey> _mainKeys = BuildKeyboard();
    private readonly List<DrawKey> _numberKeys = BuildNumberPad();
    private char? _target;
    private bool _numericPad;
    private char[] _demonstration = Array.Empty<char>();
    private int _demoIndex;
    private bool _demoPressed;
    public bool IsDemonstrating => _demoTimer.IsEnabled;
    private IReadOnlyDictionary<string,KeyboardHeatValue>? _statistics;
    private string? _selectedStatKey;
    private KeyboardErrorPath? _errorPath;
    public event Action<string>? KeySelected;
    public void SetStatistics(IReadOnlyDictionary<string,KeyboardHeatValue>? values,bool numericPad=false,string? selectedKey=null)
    {
        StopDemo(); _statistics=values; _selectedStatKey=selectedKey; _numericPad=numericPad; _target=null;
        IsHitTestVisible=values is not null; Cursor=values is null?Cursors.Arrow:Cursors.Hand;
        UpdateDesiredHeight(); InvalidateVisual();
    }
    public void SetErrorPath(KeyboardErrorPath? path)
    {
        _errorPath = path;
        AutomationProperties.SetName(this, path is null ? UiText.T("统计键盘", "Statistics keyboard", "Clavier statistique")
            : UiText.Format("应按 {0}，误按 {1}", "Expected {0}, entered {1}", "Attendu : {0}, saisi : {1}", UiText.Key(path.Expected), UiText.Key(path.Actual)));
        InvalidateVisual();
    }
    public static Color ErrorPathColor(bool expected, bool dark) => expected
        ? dark ? Color.FromRgb(68,213,178) : Color.FromRgb(0,115,87)
        : dark ? Color.FromRgb(255,149,123) : Color.FromRgb(183,53,35);

    private static readonly Dictionary<TypingFinger, Color> FingerColors = new()
    {
        // Five distinct hues; key text contrast is chosen from the actual opaque fill.
        [TypingFinger.Little] = Color.FromRgb(244, 117, 127),
        [TypingFinger.Ring] = Color.FromRgb(244, 184, 57),
        [TypingFinger.Middle] = Color.FromRgb(170, 139, 250),
        [TypingFinger.Index] = Color.FromRgb(50, 190, 226),
        [TypingFinger.Thumb] = Color.FromRgb(78, 202, 135)
    };

    private bool _showHands = true;
    public bool ShowHands
    {
        get => _showHands;
        set { if (_showHands == value) return; _showHands = value; UpdateDesiredHeight(); InvalidateVisual(); }
    }

    private bool _showFingerLabels;
    public bool ShowFingerLabels
    {
        get => _showFingerLabels;
        set { _showFingerLabels = value; InvalidateVisual(); }
    }
    public TeachingKeyboard()
    {
        Height = 450;
        MinWidth = 430;
        Focusable = false;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        // A dynamic property invalidates our drawing when the parent replaces theme resources.
        SetResourceReference(ForegroundProperty, "Ink");
        _demoTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(680) };
        _demoTimer.Tick += (_, _) =>
        {
            _demoPressed = !_demoPressed;
            if (_demoPressed) _demoIndex = (_demoIndex + 1) % _demonstration.Length;
            InvalidateVisual();
        };
        Unloaded += (_, _) => StopDemo();
        MouseMove += (_, e) =>
        {
            if(_statistics is null)return;
            var key=StatisticsKeyAt(e.GetPosition(this));
            var text=key is not null&&_statistics.TryGetValue(key.Id,out var value)?value.Tooltip:UiText.T("无记录","No data","Aucune donnée");
            if(ToolTip as string != text)ToolTip=text;
        };
        MouseLeftButtonDown += (_,e) => { if(_statistics is not null&&StatisticsKeyAt(e.GetPosition(this)) is { } key)KeySelected?.Invoke(key.Id); };
    }

    private DrawKey? StatisticsKeyAt(Point point)
    {
        var scale=ActualWidth/LogicalWidth;
        if(scale<=0)return null;
        var logical=new Point(point.X/scale,point.Y/scale);
        return _mainKeys.Concat(_numericPad?_numberKeys:Enumerable.Empty<DrawKey>()).FirstOrDefault(k=>new Rect(k.X,k.Y,k.Width-3,k.Height-3).Contains(logical));
    }

    public void SetTarget(char? expected, bool numericPad = false)
    {
        _errorPath = null;
        var changedMode = numericPad != _numericPad;
        _target = expected;
        _numericPad = numericPad;
        if (changedMode) { StopDemo(); UpdateDesiredHeight(); }
        var guide = FingerGuide.Lookup(expected, numericPad);
        AutomationProperties.SetName(this, guide is null ? UiText.T("指法键盘","Typing keyboard","Clavier de frappe") : GuideLabel(guide));
        InvalidateVisual();
    }

    public void ToggleDemo()
    {
        if (IsDemonstrating) { StopDemo(); return; }
        // Non-home-row targets demonstrate the current reach; home-row lessons cycle anchors.
        if (_numericPad)
        {
            _demonstration = "7894561230.".ToCharArray();
        }
        else if (_target.HasValue && FingerGuide.Lookup(_target) is not null && !"asdfjkl;".Contains(_target.Value))
        {
            _demonstration = new[] { _target.Value };
        }
        else
        {
            _demonstration = "asdfjkl;".ToCharArray();
        }
        _demoIndex = 0;
        _demoPressed = true;
        _demoTimer.Start();
        InvalidateVisual();
    }

    public void StopDemo()
    {
        _demoTimer.Stop();
        _demoPressed = false;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) InvalidateVisual();
    }

    private double LogicalWidth => _numericPad ? 1000 : 800;
    public double GetHeightForWidth(double width) => (_showHands ? 450 : 275) * Math.Max(1, width) / LogicalWidth;
    private void UpdateDesiredHeight()
    {
        var width = ActualWidth > 0 ? ActualWidth : double.IsFinite(Width) ? Width : 800;
        var height = Math.Ceiling(GetHeightForWidth(width));
        if (Math.Abs(Height - height) > 0.5) Height = height;
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged) UpdateDesiredHeight();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0) return;
        var scale = ActualWidth / LogicalWidth;
        // Equal X/Y scale preserves square keycaps and anatomical hand proportions at every size.
        dc.PushTransform(new ScaleTransform(scale, scale));
        var guide = FingerGuide.Lookup(IsDemonstrating ? _demonstration[_demoIndex] : _target, _numericPad);
        var lit = !IsDemonstrating || _demoPressed;
        var label = guide is null ? "" : GuideLabel(guide);
        DrawText(dc, (IsDemonstrating ? UiText.T("示范  ","Demo  ","Démo  ") : "") + label, new Point(25, 0), 13, ResourceBrush("Ink", "#E9F0EE"));
        if (ShowHands)
        {
            DrawHandConnections(dc, false, guide, lit, _numericPad);
            DrawHandConnections(dc, true, guide, lit, false);
        }
        foreach (var key in _mainKeys) DrawKeyCap(dc, key, guide, lit);
        if (_numericPad) foreach (var key in _numberKeys) DrawKeyCap(dc, key, guide, lit);
        if (ShowHands)
        {
            DrawNaturalHand(dc, false, guide, lit, _numericPad);
            DrawNaturalHand(dc, true, guide, lit, false);
        }
        dc.Pop();
    }
    private Color FieldColor => (ResourceBrush("Field", "#111A1D") as SolidColorBrush)?.Color ?? Color.FromRgb(17, 26, 29);
    private static Color Mix(Color background, Color color, double amount)
        => Color.FromRgb((byte)Math.Round(background.R + (color.R - background.R) * amount),
            (byte)Math.Round(background.G + (color.G - background.G) * amount),
            (byte)Math.Round(background.B + (color.B - background.B) * amount));
    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
    private static Brush ContrastingInk(Color color) => Luminance(color) > 0.179 ? Brushes.Black : Brushes.White;
    private Color ZoneFill(TypingFinger finger, bool active, bool subdued = false)
        => active ? FingerColors[finger] : Mix(FieldColor, FingerColors[finger], subdued ? 0.13 : Luminance(FieldColor) < 0.3 ? 0.56 : 0.43);

    private void DrawKeyCap(DrawingContext dc, DrawKey key, KeyGuidance? guide, bool lit)
    {
        if(_statistics is not null){DrawStatisticsKey(dc,key);return;}
        var active = lit && guide is not null && key.Id == guide.KeyId;
        var shift = lit && guide?.ShiftKeyId == key.Id;
        var subdued = _numericPad && !key.Id.StartsWith("Num", StringComparison.Ordinal) && !active && !shift;
        var field = FieldColor;
        var line = ResourceBrush("Line", "#304044");
        var rect = new Rect(key.X, key.Y, key.Width - 3, key.Height - 3);
        var fill = key.Finger is { } finger ? ZoneFill(finger, active || shift, subdued) : field;
        var border = key.Finger is { } f ? new SolidColorBrush(Mix(fill, FingerColors[f], active || shift ? 1 : subdued ? 0.1 : 0.46)) : line;
        dc.DrawRoundedRectangle(new SolidColorBrush(fill), new Pen(border, active || shift ? 2 : 0.8), rect, 4, 4);
        var foreground = ContrastingInk(fill);
        var keyLabel=KeyLabel(key);
        if (keyLabel.Contains("  ", StringComparison.Ordinal))
        {
            var symbols = keyLabel.Split("  ", StringSplitOptions.RemoveEmptyEntries);
            var upper = FormatText(symbols[0], 10.5, foreground);
            var lower = FormatText(symbols[^1], 14, foreground);
            dc.DrawText(upper, new Point(rect.Left + 8, rect.Top + 4));
            dc.DrawText(lower, new Point(rect.Left + (rect.Width - lower.Width) / 2, rect.Bottom - lower.Height - 7));
        }
        else
        {
            var text = FormatText(keyLabel, keyLabel.Length > 5 ? 11 : keyLabel.Length > 1 ? 12 : 14, foreground);
            dc.DrawText(text, new Point(rect.Left + (rect.Width - text.Width) / 2, rect.Top + (rect.Height - text.Height) / 2));
        }        if (key.Id is "F" or "J" or "Num5")
            dc.DrawLine(new Pen(foreground, 1.5), new Point(rect.Left + rect.Width / 2 - 4, rect.Bottom - 4), new Point(rect.Left + rect.Width / 2 + 4, rect.Bottom - 4));
        if (active)
        {
            dc.DrawLine(new Pen(foreground, 2), new Point(rect.Left + 6, rect.Bottom - 2), new Point(rect.Right - 6, rect.Bottom - 2));
            dc.DrawEllipse(foreground, null, new Point(rect.Right - 5, rect.Top + 5), 1.8, 1.8);
        }
    }

    private void DrawStatisticsKey(DrawingContext dc,DrawKey key)
    {
        _statistics!.TryGetValue(key.Id,out var value);
        var rect=new Rect(key.X,key.Y,key.Width-3,key.Height-3);
        var color=StatisticsColor(FieldColor,value);
        var selected=key.Id==_selectedStatKey;
        var expected = key.Id == _errorPath?.ExpectedPhysicalKey;
        var actual = key.Id == _errorPath?.ActualPhysicalKey;
        var dark = FieldColor.R + FieldColor.G + FieldColor.B < 384;
        var expectedBrush = new SolidColorBrush(ErrorPathColor(true,dark));
        var actualBrush = new SolidColorBrush(ErrorPathColor(false,dark));
        if(expected || actual) color=Mix(FieldColor,ErrorPathColor(expected,dark),.25);
        dc.DrawRoundedRectangle(new SolidColorBrush(color),new Pen(ResourceBrush(selected?"Accent":"Line","#304044"),selected?2.5:0.8),rect,4,4);
        if(expected) dc.DrawRoundedRectangle(null,new Pen(expectedBrush,2.8),rect,4,4);
        if(actual)
        {
            var outline=rect;
            if(expected)outline.Inflate(-4,-4);
            dc.DrawRoundedRectangle(null,new Pen(actualBrush,2.3){DashStyle=DashStyles.Dash},outline,3,3);
        }
        var label=FormatText(KeyLabel(key),key.Width<60?11:12,ContrastingInk(color));
        dc.DrawText(label,new Point(rect.Left+(rect.Width-label.Width)/2,rect.Top+(rect.Height-label.Height)/2));
        if(selected)dc.DrawLine(new Pen(ContrastingInk(color),2),new Point(rect.Left+7,rect.Bottom-4),new Point(rect.Right-7,rect.Bottom-4));
        if(expected)dc.DrawEllipse(expectedBrush,null,new Point(rect.Left+7,rect.Top+7),2.3,2.3);
        if(actual)
        {
            var mark=new Pen(actualBrush,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            dc.DrawLine(mark,new Point(rect.Right-10,rect.Top+4),new Point(rect.Right-4,rect.Top+10));
            dc.DrawLine(mark,new Point(rect.Right-10,rect.Top+10),new Point(rect.Right-4,rect.Top+4));
        }
    }
    public static Color StatisticsColor(Color background,KeyboardHeatValue? value)
    {
        if(value is null||!value.HasData)return background;
        var t=Math.Clamp(value.Value,0,1);
        var blue=Color.FromRgb(119,187,242);var yellow=Color.FromRgb(247,209,119);var red=Color.FromRgb(242,136,108);
        var color=t<.5?Mix(blue,yellow,t*2):Mix(yellow,red,(t-.5)*2);
        return Mix(background,color,value.LowSample ? .38 : .88);
    }
    private static string KeyLabel(DrawKey key)=>key.Id switch
    {
        "Space"=>UiText.T("空格","Space","Espace"),"Backspace"=>UiText.T("退格","Backspace","Retour"),
        "Enter" or "NumEnter"=>UiText.T("回车","Enter","Entrée"),"CapsLock"=>UiText.T("大写","Caps","Verr.maj"),
        "LeftShift" or "RightShift"=>UiText.T("Shift","Shift","Maj"),_=>key.Label
    };
    private static string GuideLabel(KeyGuidance guide)
    {
        var hand=guide.Hand==HandSide.Left?UiText.T("左手","Left","Gauche"):guide.Hand==HandSide.Right?UiText.T("右手","Right","Droite"):UiText.T("任一手","Either hand","Une main");
        var label=hand+" "+FingerName(guide.Finger)+"  "+UiText.Key(guide.KeyId=="Space"?" ":guide.KeyLabel);
        if(guide.RequiresShift)label+="  + "+(guide.Hand==HandSide.Left?UiText.T("右 Shift","Right Shift","Maj droite"):UiText.T("左 Shift","Left Shift","Maj gauche"));
        return label;
    }

    // A single continuous dorsal-hand silhouette, based on the original application's teaching
    // proportions: long middle finger, descending ring/index/little fingers, inward thumb and wrist.
    // It is vector geometry, not a traced bitmap or a collection of finger columns.
    private static readonly Geometry HandSilhouette = Geometry.Parse(
        "M32,276 C30,255 23,233 19,210 C15,185 12,164 11,142 L7,87 " +
        "C6,74 8,64 15,63 C23,62 26,71 27,85 L33,139 C34,150 39,151 40,138 " +
        "L43,51 C43,34 47,24 54,23 C63,22 67,34 67,49 L70,137 C70,151 75,151 76,137 " +
        "L80,29 C81,15 85,6 92,6 C101,6 105,17 104,32 L108,137 C108,151 112,151 115,137 " +
        "L129,52 C131,38 136,30 143,31 C151,33 152,44 150,58 L139,160 " +
        "C136,179 139,190 145,187 C150,185 156,172 162,160 C169,146 180,140 190,142 " +
        "C195,142 198,144 199,146 C189,158 180,175 174,191 C167,213 162,232 149,248 " +
        "C138,260 130,269 128,276 Z");
    private const double HandScale = 0.59, HandTop = 280;
    private sealed record HandFinger(TypingFinger Finger, Point Tip, Point Pad, double Angle);
    private static readonly HandFinger[] HandFingers =
    {
        new(TypingFinger.Little, new(16, 64), new(18, 83), -5),
        new(TypingFinger.Ring, new(55, 24), new(55, 45), 0),
        new(TypingFinger.Middle, new(92, 7), new(92, 29), 0),
        new(TypingFinger.Index, new(140, 32), new(139, 53), 8),
        new(TypingFinger.Thumb, new(188, 143), new(179, 159), 35)
    };
    private string[] HomeIds(bool right) => right
        ? (_numericPad ? new[] { "Num4", "Num5", "Num6", "NumAdd" } : new[] { "J", "K", "L", ";" })
        : new[] { "A", "S", "D", "F" };
    private double HandOrigin(bool right)
    {
        var keys = HomeIds(right).Select(id => _mainKeys.Concat(_numberKeys).Single(k => k.Id == id));
        var center = keys.Average(k => k.X + (k.Width - 3) / 2);
        return center + (right ? 1 : -1) * 75.75 * HandScale;
    }
    private Point HandPoint(Point point, bool right) => new(HandOrigin(right) + (right ? -1 : 1) * point.X * HandScale, HandTop + point.Y * HandScale);
    private static bool FingerActive(TypingFinger finger, bool right, KeyGuidance? guide, bool lit)
    {
        var side = right ? HandSide.Right : HandSide.Left;
        return lit && guide is not null && ((guide.Finger == finger && (guide.Hand == side || guide.Hand == HandSide.Either))
            || (guide.RequiresShift && finger == TypingFinger.Little && guide.Hand != side));
    }

    private void DrawHandConnections(DrawingContext dc, bool right, KeyGuidance? guide, bool lit, bool subdued)
    {
        if (subdued) return;
        var homes = HomeIds(right);
        foreach (var finger in HandFingers)
        {
            var active = FingerActive(finger.Finger, right, guide, lit);
            if (finger.Finger == TypingFinger.Thumb && !active) continue;
            var index = finger.Finger switch { TypingFinger.Little => right ? 3 : 0, TypingFinger.Ring => right ? 2 : 1, TypingFinger.Middle => right ? 1 : 2, _ => right ? 0 : 3 };
            var id = homes[index];
            if (active && guide is not null)
                id = guide.RequiresShift && finger.Finger == TypingFinger.Little && guide.Hand != (right ? HandSide.Right : HandSide.Left) ? guide.ShiftKeyId! : guide.KeyId;
            var key = _mainKeys.Concat(_numberKeys).FirstOrDefault(k => k.Id == id);
            if (key is null) continue;
            var start = new Point(key.X + (key.Width - 3) / 2, key.Y + key.Height - 3);
            var end = HandPoint(finger.Tip, right);
            end.Y -= 3;
            var color = FingerColors[finger.Finger];
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(active ? (byte)190 : (byte)75, color.R, color.G, color.B)), active ? 1.5 : 0.75)
                { DashStyle = active ? DashStyles.Solid : new DashStyle(new[] { 2.0, 4.0 }, 0) };
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(start, false, false);
                context.BezierTo(new Point(start.X, 277), new Point(end.X, 284), end, true, false);
            }
            dc.DrawGeometry(null, pen, path);
        }
    }

    private void DrawNaturalHand(DrawingContext dc, bool right, KeyGuidance? guide, bool lit, bool subdued)
    {
        if (subdued) dc.PushOpacity(0.28);
        dc.PushTransform(new TranslateTransform(HandOrigin(right), HandTop));
        dc.PushTransform(new ScaleTransform(right ? -HandScale : HandScale, HandScale));
        var dark = Luminance(FieldColor) < 0.3;
        var skin = dark ? Color.FromRgb(179, 172, 163) : Color.FromRgb(233, 224, 210);
        var edge = dark ? Color.FromRgb(117, 118, 112) : Color.FromRgb(170, 159, 144);
        var palm = new LinearGradientBrush(Mix(skin, Colors.White, 0.12), Mix(skin, Colors.Black, 0.06), new Point(0.3, 0), new Point(0.7, 1));
        dc.PushTransform(new TranslateTransform(0, 2));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(dark ? (byte)35 : (byte)18, 0, 0, 0)), null, HandSilhouette);
        dc.Pop();
        dc.DrawGeometry(palm, new Pen(new SolidColorBrush(edge), 1.3), HandSilhouette);
        dc.PushClip(HandSilhouette);
        foreach (var finger in HandFingers)
        {
            var active = FingerActive(finger.Finger, right, guide, lit);
            var color = FingerColors[finger.Finger];
            if (active)
            {
                var knuckle = finger.Finger switch {
                    TypingFinger.Little => new Point(25, 153), TypingFinger.Ring => new Point(59, 151),
                    TypingFinger.Middle => new Point(94, 151), TypingFinger.Index => new Point(125, 159),
                    _ => new Point(154, 208)
                };
                var wash = new Pen(new SolidColorBrush(Color.FromArgb(115, color.R, color.G, color.B)), 17)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(wash, finger.Pad, knuckle);
            }
            var pad = new SolidColorBrush(active ? color : Mix(skin, color, 0.62));
            dc.PushTransform(new RotateTransform(finger.Angle, finger.Pad.X, finger.Pad.Y));
            dc.DrawRoundedRectangle(pad, new Pen(new SolidColorBrush(Mix(edge, color, 0.65)), active ? 2.5 : 0.8),
                new Rect(finger.Pad.X - 8, finger.Pad.Y - 14, 16, 27), 7.5, 7.5);
            if (active)
            {
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(color), 2.8), new Rect(finger.Pad.X - 11, finger.Pad.Y - 17, 22, 33), 10, 10);
                dc.DrawEllipse(ContrastingInk(color), null, finger.Pad, 2.4, 2.4);
            }
            else dc.DrawLine(new Pen(new SolidColorBrush(Mix(skin, Colors.White, 0.45)), 1),
                new Point(finger.Pad.X - 4, finger.Pad.Y - 9), new Point(finger.Pad.X + 4, finger.Pad.Y - 9));
            dc.Pop();
        }
        var crease = new SolidColorBrush(Mix(skin, edge, 0.40));
        var creaseGeometry = Geometry.Parse("M43,183 C65,194 91,199 116,189 M137,196 C122,209 116,228 114,244 M49,249 C69,253 93,254 112,250");
        dc.DrawGeometry(null, new Pen(crease, 0.8), creaseGeometry);
        dc.Pop();
        dc.Pop();
        dc.Pop();
        if (subdued) dc.Pop();
        if (ShowFingerLabels)
        {
            foreach (var finger in HandFingers.Where(f => f.Finger != TypingFinger.Thumb))
            {
                var point = HandPoint(finger.Tip, right);
                var text = FormatText(FingerName(finger.Finger), 9, ResourceBrush("Muted", "#99ACA8"));
                dc.DrawText(text, new Point(point.X - text.Width / 2, point.Y - 17));
            }
        }
    }
    private Brush ResourceBrush(string name, string fallback) => TryFindResource(name) as Brush
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private FormattedText FormatText(string text, double size, Brush brush) => new(text, UiText.Culture,
        FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void DrawText(DrawingContext dc, string text, Point position, double size, Brush brush) => dc.DrawText(FormatText(text, size, brush), position);
    private static string FingerName(TypingFinger finger) => finger switch
    {
        TypingFinger.Little => UiText.T("小指","little finger","auriculaire"), TypingFinger.Ring => UiText.T("无名指","ring finger","annulaire"),
        TypingFinger.Middle => UiText.T("中指","middle finger","majeur"), TypingFinger.Index => UiText.T("食指","index finger","index"), _ => UiText.T("拇指","thumb","pouce")
    };

    private static List<DrawKey> BuildKeyboard()
    {
        const double unit = 50, rowHeight = 50;
        var keys = new List<DrawKey>();
        void Row(int row, params (string Id, string Label, double Units, TypingFinger? Finger)[] specs)
        {
            double x = 25;
            foreach (var (id, label, units, finger) in specs)
            {
                keys.Add(new(id, label, x, 23 + row * rowHeight, units * unit, rowHeight, finger));
                x += units * unit;
            }
        }
        (string, string, double, TypingFinger?) Letter(char c, string? label = null)
        {
            var info = FingerGuide.Lookup(c)!;
            return (info.KeyId, label ?? info.KeyLabel, 1, info.Finger);
        }
        var r0 = "`1234567890-=".Select((c, i) => Letter(c, new[] { "~  `", "!  1", "@  2", "#  3", "$  4", "%  5", "^  6", "&  7", "*  8", "(  9", ")  0", "_  -", "+  =" }[i])).ToList();
        r0.Add(("Backspace", "Backspace", 2, TypingFinger.Little)); Row(0, r0.ToArray());
        var r1 = new List<(string, string, double, TypingFinger?)> { ("Tab", "Tab", 1.5, TypingFinger.Little) };
        r1.AddRange("qwertyuiop[]".Select(c => Letter(c, c == '[' ? "{  [" : c == ']' ? "}  ]" : null))); r1.Add(("\\", "|  \\", 1.5, TypingFinger.Little)); Row(1, r1.ToArray());
        var r2 = new List<(string, string, double, TypingFinger?)> { ("CapsLock", "Caps", 1.75, TypingFinger.Little) };
        r2.AddRange("asdfghjkl;'".Select(c => Letter(c, c == ';' ? ":  ;" : c == '\'' ? "\"  '" : null))); r2.Add(("Enter", "Enter", 2.25, TypingFinger.Little)); Row(2, r2.ToArray());
        var r3 = new List<(string, string, double, TypingFinger?)> { ("LeftShift", "Shift", 2.25, TypingFinger.Little) };
        r3.AddRange("zxcvbnm,./".Select(c => Letter(c, c == ',' ? "<  ," : c == '.' ? ">  ." : c == '/' ? "?  /" : null))); r3.Add(("RightShift", "Shift", 2.75, TypingFinger.Little)); Row(3, r3.ToArray());
        Row(4, ("LeftCtrl", "Ctrl", 1.25, null), ("LeftWin", "Win", 1.25, null), ("LeftAlt", "Alt", 1.25, null),
            ("Space", "Space  /  空格", 6.25, TypingFinger.Thumb), ("RightAlt", "Alt", 1.25, null),
            ("RightWin", "Win", 1.25, null), ("Menu", "Menu", 1.25, null), ("RightCtrl", "Ctrl", 1.25, null));
        return keys;
    }

    private static List<DrawKey> BuildNumberPad()
    {
        var keys = new List<DrawKey>();
        void Add(string id, string label, int column, int row, TypingFinger finger, int width = 1, int height = 1)
            => keys.Add(new(id, label, 795 + column * 50, 23 + row * 50, width * 50, height * 50, finger));
        Add("NumLock", "Num", 0, 0, TypingFinger.Index);
        Add("NumDivide", "/", 1, 0, TypingFinger.Middle); Add("NumMultiply", "*", 2, 0, TypingFinger.Ring); Add("NumSubtract", "−", 3, 0, TypingFinger.Little);
        for (var row = 0; row < 3; row++) for (var column = 0; column < 3; column++)
        {
            var digit = (char)('7' - row * 3 + column);
            Add("Num" + digit, digit.ToString(), column, row + 1, FingerGuide.Lookup(digit, true)!.Finger);
        }
        Add("NumAdd", "+", 3, 1, TypingFinger.Little, height: 2);
        Add("NumEnter", "Enter", 3, 3, TypingFinger.Little, height: 2);
        Add("Num0", "0", 0, 4, TypingFinger.Thumb, width: 2);
        Add("NumDecimal", ".", 2, 4, TypingFinger.Ring);
        return keys;
    }
}




