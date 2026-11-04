using TypeEasy.Core;

namespace TypeEasy.Windows;

public enum KeyboardMetric { Frequency, Errors, ErrorRate, Confusions, Context }
public sealed record KeyboardHeatValue(double Value, bool HasData, bool LowSample, string Tooltip);

public sealed class KeyboardKeySummary
{
    public string KeyId { get; init; } = "";
    public bool Unmapped { get; init; }
    public string Label { get; init; } = "";
    public long Submitted { get; set; }
    public long Opportunities { get; set; }
    public long Errors { get; set; }
    public long ShiftOpportunities { get; set; }
    public List<TypingKeySummary> Targets { get; } = new();
    public List<TypingConfusion> Confusions { get; } = new();
    public List<ContextRisk> Contexts { get; } = new();
    public double ErrorRate => Opportunities == 0 ? 0 : (double)Errors / Opportunities;
    public double Value(KeyboardMetric metric) => metric switch
    {
        KeyboardMetric.Frequency => Submitted,
        KeyboardMetric.Errors => Errors,
        KeyboardMetric.ErrorRate => ErrorRate,
        KeyboardMetric.Confusions => Confusions.Count == 0 ? 0 : Confusions.Max(c => c.Count),
        _ => Contexts.Where(c => c.IsEligible).Select(c => Math.Max(0, c.RateDifference)).DefaultIfEmpty(0).Max()
    };
    public bool HasData(KeyboardMetric metric) => metric switch
    {
        KeyboardMetric.Frequency => Submitted > 0 || Opportunities > 0,
        KeyboardMetric.Context => Contexts.Count > 0,
        _ => Opportunities > 0
    };
    public bool LowSample(KeyboardMetric metric) => metric switch
    {
        KeyboardMetric.ErrorRate => Opportunities < 20,
        KeyboardMetric.Context => !Contexts.Any(c => c.IsEligible),
        KeyboardMetric.Frequency => Submitted < 3,
        _ => Opportunities < 3
    };
}

/// <summary>Read-only aggregation of submitted text and expected opportunities onto standard key positions.</summary>
public static class KeyboardStatistics
{
    public static string? PhysicalKey(string mode, string value)
    {
        // IME commits do not reveal physical keys, even when the committed character is ASCII.
        if (mode == "中文输入" || value.Length != 1) return null;
        return FingerGuide.Lookup(value[0], mode == "数字键盘")?.KeyId;
    }

    public static IReadOnlyList<KeyboardKeySummary> Build(IEnumerable<TypingKeySummary> keys,
        IEnumerable<TypingConfusion> confusions, IEnumerable<ContextRisk> contexts, string? mode = null)
    {
        var buckets = new Dictionary<string,KeyboardKeySummary>(StringComparer.Ordinal);
        KeyboardKeySummary Bucket(string sourceMode,string value)
        {
            var physical = PhysicalKey(sourceMode,value);
            var id = physical ?? "text:" + sourceMode + ":" + value;
            if (!buckets.TryGetValue(id,out var bucket)) buckets[id] = bucket = new()
                { KeyId=id,Unmapped=physical is null,Label=physical ?? value };
            return bucket;
        }
        foreach (var key in keys.Where(x => mode is null || x.Mode == mode))
        {
            var target=Bucket(key.Mode,key.Expected);
            target.Targets.Add(key); target.Opportunities+=key.Opportunities; target.Errors+=key.Errors;
            if (!target.Unmapped && key.Expected.Length==1 && FingerGuide.Lookup(key.Expected[0],key.Mode=="数字键盘")?.RequiresShift==true)
                target.ShiftOpportunities+=key.Opportunities;
            foreach(var actual in key.ActualCounts) Bucket(key.Mode,actual.Key).Submitted+=actual.Value;
        }
        foreach(var confusion in confusions.Where(x=>mode is null||x.Mode==mode)) Bucket(confusion.Mode,confusion.Expected).Confusions.Add(confusion);
        foreach(var context in contexts.Where(x=>mode is null||x.Mode==mode)) Bucket(context.Mode,context.Expected).Contexts.Add(context);
        return buckets.Values.OrderBy(x=>x.Unmapped).ThenBy(x=>x.KeyId,StringComparer.Ordinal).ToArray();
    }
}
