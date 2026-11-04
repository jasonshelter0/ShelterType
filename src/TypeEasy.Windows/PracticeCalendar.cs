using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public sealed record PracticeCalendarDay(DateTime Date, int WeekColumn, int Weekday,
    bool IsFuture, bool HasRecord, double Seconds, long CorrectInputs, long Errors)
{
    public bool IsActive => !IsFuture && HasRecord && (Seconds > 0 || CorrectInputs > 0 || Errors > 0);
    public int Level => IsFuture || !HasRecord ? 0 : PracticeCalendarData.LevelForSeconds(Seconds);
}

/// <summary>Calendar mapping and totals are independent of the drawing, with an injectable local date.</summary>
public sealed class PracticeCalendarData
{
    public int Year { get; }
    public DateTime Today { get; }
    public DateTime StartMonday { get; }
    public int WeekCount { get; }
    public IReadOnlyList<PracticeCalendarDay> Days { get; }
    public int ActiveDays => Days.Count(day => day.IsActive);
    public int RecordedDays => Days.Count(day => day.HasRecord && !day.IsFuture);
    public double TotalSeconds => Days.Where(day => !day.IsFuture).Sum(day => day.Seconds);
    public long CorrectInputs => Days.Where(day => !day.IsFuture).Sum(day => day.CorrectInputs);
    public long Errors => Days.Where(day => !day.IsFuture).Sum(day => day.Errors);

    public PracticeCalendarData(int year, IEnumerable<DailyTypingSummary> summaries, DateTime? today = null, string? mode = null)
    {
        Year = year;
        Today = (today ?? DateTime.Today).Date;
        var first = new DateTime(year, 1, 1);
        var last = new DateTime(year, 12, 31);
        StartMonday = first.AddDays(-MondayIndex(first));
        WeekCount = (last - StartMonday).Days / 7 + 1;
        var values = new Dictionary<DateTime, (double Seconds, long Correct, long Errors)>();
        foreach (var entry in summaries)
        {
            if (mode is not null && entry.Mode != mode) continue;
            if (!TryDate(entry.Day, out var date) || date.Year != year || date > Today) continue;
            if (!double.IsFinite(entry.ActiveSeconds) || entry.ActiveSeconds < 0 || entry.CorrectInputs < 0 || entry.Errors < 0) continue;
            values.TryGetValue(date, out var old);
            values[date] = (old.Seconds + entry.ActiveSeconds, old.Correct + entry.CorrectInputs, old.Errors + entry.Errors);
        }
        var days = new List<PracticeCalendarDay>();
        for (var i = 0; i < (last - first).Days + 1; i++)
        {
            var date = first.AddDays(i);
            var recorded = values.TryGetValue(date, out var value);
            days.Add(new(date, (date - StartMonday).Days / 7, MondayIndex(date), date > Today,
                recorded, value.Seconds, value.Correct, value.Errors));
        }
        Days = days;
    }

    public PracticeCalendarDay? At(int column, int weekday)
    {
        if (column < 0 || column >= WeekCount || weekday < 0 || weekday > 6) return null;
        var index = column * 7 + weekday - (new DateTime(Year, 1, 1) - StartMonday).Days;
        return index >= 0 && index < Days.Count ? Days[index] : null;
    }
    public static bool TryDate(string value, out DateTime date)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public static int MondayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7;
    // Fixed thresholds are shared by every year and category; colors do not rescale to the sample.
    public static int LevelForSeconds(double seconds) => !double.IsFinite(seconds) || seconds <= 0 ? 0
        : seconds < 300 ? 1 : seconds < 900 ? 2 : seconds < 1800 ? 3 : seconds < 3600 ? 4 : 5;
    public static string Describe(PracticeCalendarDay day)
    {
        var title = day.Date.ToString("yyyy-MM-dd ddd", UiText.Culture);
        if (day.IsFuture) return title + "\n"+UiText.T("未来日期，不计入统计。","Future date, excluded from totals.","Date future, exclue des totaux.");
        if (!day.HasRecord) return title + "\n"+UiText.T("无记录","No data","Aucune donnée");
        return title + "\n"+UiText.Format("{0:0.00} 分钟\n正确 {1:N0} 次，错误 {2:N0} 次","{0:0.00} minutes\n{1:N0} correct, {2:N0} errors","{0:0.00} minutes\n{1:N0} corrects, {2:N0} erreurs",day.Seconds/60,day.CorrectInputs,day.Errors);
    }
}

/// <summary>Native year calendar: fixed-size squares, Monday-first weeks and truthful hover details.</summary>
public sealed class PracticeCalendar : Control
{
    public const double CellSize = 11, CellStep = 14, GridLeft = 35, GridTop = 27;
    private readonly PracticeCalendarData data;
    private PracticeCalendarDay? hovered;
    public PracticeCalendarData Data => data;
    public PracticeCalendar(PracticeCalendarData data)
    {
        this.data = data;
        Width = GridLeft + data.WeekCount * CellStep + 5;
        Height = 170;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetName(this, UiText.Format("{0} 年练习日历，周一到周日，{1} 个活跃日。","Practice calendar for {0}, Monday to Sunday, {1} active days.","Calendrier de pratique {0}, du lundi au dimanche, {1} jours actifs.",data.Year,data.ActiveDays));
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetShowDuration(this, 20000);
        MouseMove += Hover;
        MouseLeave += (_, _) => { hovered = null; ToolTip = null; InvalidateVisual(); };
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) InvalidateVisual();
    }
    private Brush Resource(string name, string fallback) => TryFindResource(name) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private bool Dark => (Resource("Panel", "#192225") as SolidColorBrush)?.Color is { } c && c.R + c.G + c.B < 384;
    private Brush LevelBrush(int level)
    {
        var colors = Dark
            ? new[] { "#263236", "#194632", "#236C45", "#299455", "#43B96A", "#78DD95" }
            : new[] { "#EDF1EE", "#CDEFD2", "#91D5A2", "#56B974", "#2C9650", "#176B39" };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[Math.Clamp(level, 0, 5)]));
    }
    public static Rect CellRect(PracticeCalendarDay day) => new(GridLeft + day.WeekColumn * CellStep, GridTop + day.Weekday * CellStep, CellSize, CellSize);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var line = Resource("Line", "#304044");
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateTime(data.Year, month, 1);
            var column = (first - data.StartMonday).Days / 7;
            Label(dc, UiText.Language=="zh-CN"?month+"月":first.ToString("MMM",UiText.Culture), GridLeft + column * CellStep, 4, 10.5);
        }
        for (var row = 0; row < 7; row++) Label(dc, new DateTime(2024,1,1).AddDays(row).ToString("ddd",UiText.Culture), 0, GridTop + row * CellStep - 1, 10);
        foreach (var day in data.Days)
        {
            var rect = CellRect(day);
            if (day.IsFuture)
            {
                dc.PushOpacity(0.32);
                dc.DrawRoundedRectangle(null, new Pen(line, 0.65), rect, 2, 2);
                dc.Pop();
            }
            else dc.DrawRoundedRectangle(LevelBrush(day.Level), new Pen(line, 0.45), rect, 2, 2);
            if (day.Date == data.Today || day.Date == hovered?.Date)
                dc.DrawRoundedRectangle(null, new Pen(Resource(day.Date == hovered?.Date ? "Ink" : "Accent", "#78DCC0"), 1.2), rect, 2, 2);
        }
        Label(dc, UiText.T("分钟","min","min"), GridLeft, 145, 10);
        var x = GridLeft + 34;
        foreach (var (level, label) in new[] { (0, UiText.T("无记录 / 0","No data / 0","Aucune / 0")), (1, "<5"), (2, "5–<15"), (3, "15–<30"), (4, "30–<60"), (5, "≥60") })
        {
            dc.DrawRoundedRectangle(LevelBrush(level), new Pen(line, 0.5), new Rect(x, 147, CellSize, CellSize), 2, 2);
            Label(dc, label, x + 16, 145, 10);
            x += level == 0 ? 103 : 82;
        }
    }
    private void Label(DrawingContext dc, string text, double x, double y, double size)
    {
        var formatted = new FormattedText(text, UiText.Culture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, Resource("Muted", "#99ACA8"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
    private void Hover(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var column = (int)Math.Floor((point.X - GridLeft) / CellStep);
        var row = (int)Math.Floor((point.Y - GridTop) / CellStep);
        var day = data.At(column, row);
        if (day is not null && !CellRect(day).Contains(point)) day = null;
        if (hovered?.Date == day?.Date) return;
        hovered = day;
        ToolTip = day is null ? null : PracticeCalendarData.Describe(day);
        InvalidateVisual();
    }
}
