using System.Collections.Concurrent;
using System.Text.Json;

namespace TypeEasy.Core;

public static class TypingMode
{
    public static string ForCourse(Course course) => course.InputMode == "pinyin" ? "拼音编码"
        : course.InputMode == "wubi-ime" ? "五笔"+course.Variant+"中文输入"
        : course.Category == "五笔" ? "五笔" + course.Variant
        : course.Category == "中文" ? "中文输入"
        : course.Id.Contains("numpad", StringComparison.OrdinalIgnoreCase) ? "数字键盘" : "英文";
}

public sealed class DailyTypingSummary
{
    public string Day { get; set; } = "";
    public string Mode { get; set; } = "";
    public double ActiveSeconds { get; set; }
    public long CorrectInputs { get; set; }
    public long Errors { get; set; }
    public long Backspaces { get; set; }
    public long Opportunities => CorrectInputs + Errors;
    public double Accuracy => Opportunities == 0 ? 1 : (double)CorrectInputs / Opportunities;
}

public sealed record TypingAttempt(string Expected, string Actual, string Before, string After, string TypedBefore = "");
public sealed record TypingConfusion(string Mode, string Expected, string Actual, long Count);
public sealed record TypingKeySummary(string Mode, string Expected, long Opportunities, long Errors, IReadOnlyDictionary<string,long> ActualCounts);

public sealed class ContextRisk
{
    public string Mode { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public long Opportunities { get; init; }
    public long Errors { get; init; }
    public long BaselineOpportunities { get; init; }
    public long BaselineErrors { get; init; }
    public double SmoothedErrorRate => (Errors + 1.0) / (Opportunities + 2.0);
    public double BaselineErrorRate => (BaselineErrors + 1.0) / (BaselineOpportunities + 2.0);
    public double RateDifference => SmoothedErrorRate - BaselineErrorRate;
    public bool IsEligible => Opportunities >= 20 && Errors >= 3;
    public bool IsRisk => IsEligible && BaselineOpportunities >= 20 && RateDifference >= 0.10;
    public IReadOnlyDictionary<string, long> ActualCounts { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> TypedBeforeCounts { get; init; } = new Dictionary<string, long>();
}

/// <summary>Local, bounded v0.3 observations; no historic scores are reinterpreted as context samples.</summary>
public sealed class AnalyticsStore
{
    public const string ContextSchemaLabel = "实验性上下文分析 · 仅统计 v0.3 起的新输入";
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly string _path;
    private readonly object _sync;
    private readonly TimeZoneInfo _timeZone;
    public string? RecoveryNotice { get; private set; }
    public string DirectoryPath => Path.GetDirectoryName(_path)!;

    public AnalyticsStore(string directory, TimeZoneInfo? timeZone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(Path.GetFullPath(directory), "analytics-v3.json");
        _sync = Locks.GetOrAdd(_path, _ => new object());
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public IReadOnlyList<DailyTypingSummary> ReadDays()
    {
        lock (_sync) return Read().Days.OrderBy(x => x.Day).ThenBy(x => x.Mode).ToArray();
    }

    public IReadOnlyList<SpeedTestResult> ReadSpeedTests(bool includeCancelled = false)
    {
        lock (_sync) return Read().SpeedTests.Where(x => includeCancelled || x.Status != SpeedTestStatus.Cancelled)
            .OrderByDescending(x => x.FinishedAt).ToArray();
    }

    public IReadOnlyList<TypingConfusion> ReadConfusions()
    {
        lock (_sync) return Read().Keys.SelectMany(k => k.ActualCounts.Where(a => a.Key != k.Expected)
            .Select(a => new TypingConfusion(k.Mode, k.Expected, a.Key, a.Value))).OrderByDescending(x => x.Count).ToArray();
    }

    public IReadOnlyList<TypingKeySummary> ReadKeys()
    {
        lock (_sync) return Read().Keys.Select(k => new TypingKeySummary(k.Mode, k.Expected,
            k.Opportunities, k.Errors, new Dictionary<string,long>(k.ActualCounts))).ToArray();
    }

    public IReadOnlyList<ContextRisk> ReadContextRisks()
    {
        lock (_sync)
        {
            var data = Read();
            return data.Contexts.Select(c =>
            {
                var baseline = data.Keys.Find(k => k.Mode == c.Mode && k.Expected == c.Expected);
                return new ContextRisk
                {
                    Mode = c.Mode, Expected = c.Expected, Before = c.Before, After = c.After,
                    Opportunities = c.Opportunities, Errors = c.Errors,
                    BaselineOpportunities = baseline?.Opportunities ?? 0, BaselineErrors = baseline?.Errors ?? 0,
                    ActualCounts = c.ActualCounts, TypedBeforeCounts = c.TypedBeforeCounts
                };
            }).OrderByDescending(x => x.IsRisk).ThenByDescending(x => x.Errors).ThenByDescending(x => x.Opportunities).ToArray();
        }
    }

    /// <summary>Delta accounting, not cumulative checkpoint totals. Empty batches do no I/O.</summary>
    public void RecordPractice(string mode, double activeSeconds, long correctInputs, long errors, long backspaces,
        IReadOnlyList<TypingAttempt> attempts, DateTimeOffset? occurredAt = null)
    {
        if (!double.IsFinite(activeSeconds) || activeSeconds < 0 || correctInputs < 0 || errors < 0 || backspaces < 0)
            throw new ArgumentOutOfRangeException(nameof(activeSeconds));
        if (activeSeconds == 0 && correctInputs == 0 && errors == 0 && backspaces == 0 && attempts.Count == 0) return;
        var at = occurredAt ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            var data = Read();
            AddTime(data, mode, activeSeconds, at);
            var day = GetDay(data, mode, LocalDay(at));
            day.CorrectInputs += correctInputs; day.Errors += errors; day.Backspaces += backspaces;
            AddAttempts(data, mode, attempts, at);
            Write(data);
        }
    }

    private static void AddAttempts(Document data, string mode, IReadOnlyList<TypingAttempt> attempts, DateTimeOffset at)
    {
            foreach (var attempt in attempts)
            {
                var key = data.Keys.Find(k => k.Mode == mode && k.Expected == attempt.Expected);
                if (key is null)
                {
                    key = new KeyAggregate { Mode = mode, Expected = attempt.Expected };
                    data.Keys.Add(key);
                }
                key.Opportunities++;
                if (attempt.Expected != attempt.Actual) key.Errors++;
                key.LastSeen = at;
                AddBounded(key.ActualCounts, attempt.Actual, 24, attempt.Actual == attempt.Expected);
                var context = data.Contexts.Find(c => c.Mode == mode && c.Expected == attempt.Expected && c.Before == attempt.Before && c.After == attempt.After);
                if (context is null)
                {
                    context = new ContextAggregate { Mode = mode, Expected = attempt.Expected, Before = attempt.Before, After = attempt.After };
                    data.Contexts.Add(context);
                }
                context.Opportunities++;
                if (attempt.Expected != attempt.Actual) context.Errors++;
                context.LastSeen = at;
                AddBounded(context.ActualCounts, attempt.Actual, 16, attempt.Actual == attempt.Expected);
                AddBounded(context.TypedBeforeCounts, attempt.TypedBefore, 8);
            }
    }

    /// <summary>Idempotent by result ID. Cancelled tests are stored but never added to daily totals.</summary>
    public bool SaveSpeedTest(SpeedTestResult result, IReadOnlyList<TypingAttempt>? attempts = null)
    {
        lock (_sync)
        {
            var data = Read();
            if (data.SpeedTests.Any(x => x.Id == result.Id)) return false;
            data.SpeedTests.Add(result);
            if (result.Status != SpeedTestStatus.Cancelled)
            {
                AddTime(data, result.Mode, result.ElapsedSeconds, result.FinishedAt);
                var day = GetDay(data, result.Mode, LocalDay(result.FinishedAt));
                day.CorrectInputs += result.CorrectInputs; day.Errors += result.Errors; day.Backspaces += result.Backspaces;
                if (attempts is not null) AddAttempts(data, result.Mode, attempts, result.FinishedAt);
            }
            Write(data);
            return true;
        }
    }

    private string LocalDay(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, _timeZone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static DailyTypingSummary GetDay(Document data, string mode, string day)
    {
        var entry = data.Days.Find(x => x.Day == day && x.Mode == mode);
        if (entry is null) { entry = new() { Day = day, Mode = mode }; data.Days.Add(entry); }
        return entry;
    }
    private void AddTime(Document data, string mode, double seconds, DateTimeOffset at)
    {
        var cursor = at.AddSeconds(-seconds);
        while (cursor < at)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, _timeZone);
            var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
            // Some time zones skip midnight; advance to the next valid local minute.
            while (_timeZone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
            var next = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, _timeZone));
            var end = next < at ? next : at;
            if (end <= cursor) end = at;
            GetDay(data, mode, LocalDay(cursor)).ActiveSeconds += (end - cursor).TotalSeconds;
            cursor = end;
        }
    }
    private static void AddBounded(Dictionary<string, long> values, string key, int capacity, bool preserveKey = false)
    {
        if (!preserveKey && !values.ContainsKey(key) && values.Count >= capacity) key = "（其他）";
        values[key] = values.GetValueOrDefault(key) + 1;
    }

    private Document Read()
    {
        if (!File.Exists(_path)) return new();
        var primary = TryRead(_path);
        if (primary is not null) return primary;
        var backup = TryRead(_path + ".bak");
        RecoveryNotice = backup is null ? "统计文件与备份无法读取，已开始新的统计记录。" : "统计文件损坏，已从备份恢复。";
        return backup ?? new();
    }
    private static Document? TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), JsonOptions);
            return data is { Version: 3, Days: not null, Keys: not null, Contexts: not null, SpeedTests: not null }
                && data.Days.All(x => x is not null && x.Day is not null && x.Mode is not null && double.IsFinite(x.ActiveSeconds)
                    && x.ActiveSeconds >= 0 && x.CorrectInputs >= 0 && x.Errors >= 0 && x.Backspaces >= 0)
                && data.Keys.All(ValidKey)
                && data.Contexts.All(x => ValidKey(x) && x.Before is not null && x.After is not null
                    && x.TypedBeforeCounts is not null && x.TypedBeforeCounts.Values.All(v => v >= 0))
                && data.SpeedTests.All(x => x is not null && x.Id is not null && x.Mode is not null && x.SourceId is not null
                    && x.SourceTitle is not null && double.IsFinite(x.ElapsedSeconds) && x.ElapsedSeconds >= 0 && x.NetCharacters >= 0
                    && x.CorrectInputs >= 0 && x.Errors >= 0 && x.Backspaces >= 0 && Enum.IsDefined(x.Status)) ? data : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool ValidKey(KeyAggregate? x) => x is not null && x.Mode is not null && x.Expected is not null
        && x.Opportunities >= 0 && x.Errors >= 0 && x.Errors <= x.Opportunities
        && x.ActualCounts is not null && x.ActualCounts.Values.All(v => v >= 0);
    private void Write(Document data)
    {
        // Keep bounded recent observations; total daily figures remain independent of context retention.
        data.Days = data.Days.OrderByDescending(x => x.Day).Take(4096).ToList();
        data.Keys = data.Keys.OrderByDescending(x => x.LastSeen).Take(4096).ToList();
        data.Contexts = data.Contexts.OrderByDescending(x => x.LastSeen).Take(4096).ToList();
        data.SpeedTests = data.SpeedTests.OrderByDescending(x => x.FinishedAt).Take(1000).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using (var output = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, data, JsonOptions);
            output.Flush(true);
        }
        if (TryRead(_path) is not null) File.Copy(_path, _path + ".bak", true);
        File.Move(_path + ".tmp", _path, true);
    }

    public sealed class Document
    {
        public int Version { get; set; } = 3;
        public List<DailyTypingSummary> Days { get; set; } = new();
        public List<KeyAggregate> Keys { get; set; } = new();
        public List<ContextAggregate> Contexts { get; set; } = new();
        public List<SpeedTestResult> SpeedTests { get; set; } = new();
    }
    public class KeyAggregate
    {
        public string Mode { get; set; } = "";
        public string Expected { get; set; } = "";
        public long Opportunities { get; set; }
        public long Errors { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public Dictionary<string, long> ActualCounts { get; set; } = new();
    }
    public sealed class ContextAggregate : KeyAggregate
    {
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public Dictionary<string, long> TypedBeforeCounts { get; set; } = new();
    }
}
