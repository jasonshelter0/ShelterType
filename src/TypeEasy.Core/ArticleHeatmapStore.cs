using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeEasy.Core;

public enum HeatmapSource { Practice, Test }

public class ArticleHeatmapRecord
{
    public string CourseId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Mode { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public HeatmapSource Source { get; set; }
    public int TextLength { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ArticlePositionSummary
{
    public int Offset { get; set; }
    public long Opportunities { get; set; }
    public long Errors { get; set; }
    public long CorrectInputs { get; set; }
    public long TimingSamples { get; set; }
    public double TotalSeconds { get; set; }
    public long EstimatedBatchSamples { get; set; }
    public long SkippedTimingSamples { get; set; }
    public List<double> RecentSeconds { get; set; } = new();
    [JsonIgnore] public double MeanSeconds => TimingSamples == 0 ? 0 : TotalSeconds / TimingSamples;
    /// <summary>Median of the last at most 31 valid samples, not a lifetime median.</summary>
    [JsonIgnore] public double MedianSeconds
    {
        get
        {
            if (RecentSeconds.Count == 0) return 0;
            var values = RecentSeconds.Order().ToArray();
            return values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
        }
    }
}

public sealed class ArticleHeatmapSnapshot : ArticleHeatmapRecord
{
    public int SchemaVersion { get; set; } = 4;
    public string Text { get; set; } = "";
    public List<ArticlePositionSummary> Positions { get; set; } = new();
}

/// <summary>Per-article v0.4 position observations. The original article text is retained for old fingerprints.</summary>
public sealed class ArticleHeatmapStore
{
    public const int MaximumTextLength = 200_000;
    public const int RecentSampleLimit = 31;
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;
    public string? RecoveryNotice { get; private set; }

    public ArticleHeatmapStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public static bool IsSupported(Course course)
    {
        // Original article lessons only: no inferred positional observations for word lists,
        // generated reviews, Wubi, pinyin codes, or historical v0.3 aggregate statistics.
        return course.Category is "英文" or "中文" && course.InputMode != "pinyin"
            && course.Variant is "文章" or "专业文章" or "综合文章"
            && (course.Id.StartsWith("english/", StringComparison.OrdinalIgnoreCase) || course.Id.StartsWith("chinese/", StringComparison.OrdinalIgnoreCase))
            && course.Items.Sum(x => (long)x.Expected.Length) is > 0 and <= MaximumTextLength;
    }

    public IReadOnlyList<ArticleHeatmapRecord> ListArticles()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<ArticleHeatmapRecord>();
        var result = new List<ArticleHeatmapRecord>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            lock (Locks.GetOrAdd(path, _ => new object()))
            {
                var snapshot = Load(path);
                if (snapshot is null) continue;
                result.Add(new ArticleHeatmapRecord
                {
                    CourseId = snapshot.CourseId, Title = snapshot.Title, Mode = snapshot.Mode,
                    SourceFingerprint = snapshot.SourceFingerprint, Source = snapshot.Source,
                    TextLength = snapshot.TextLength, UpdatedAt = snapshot.UpdatedAt
                });
            }
        }
        return result.OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public ArticleHeatmapSnapshot Read(Course course, HeatmapSource source)
    {
        var fingerprint = SpeedTestSession.SourceFingerprintFor(course);
        var loaded = Read(course.Id, fingerprint, source);
        return loaded.Text.Length > 0 ? loaded : NewSnapshot(course, source);
    }

    public ArticleHeatmapSnapshot Read(string courseId, string fingerprint, HeatmapSource source)
    {
        var path = FilePath(courseId, fingerprint, source);
        lock (Locks.GetOrAdd(path, _ => new object()))
        {
            var loaded = Load(path);
            return loaded is not null && loaded.CourseId == courseId && loaded.SourceFingerprint == fingerprint && loaded.Source == source
                ? loaded : new() { CourseId = courseId, SourceFingerprint = fingerprint, Source = source };
        }
    }

    internal void Add(Course course, HeatmapSource source, IReadOnlyList<ArticlePositionSummary> deltas, DateTimeOffset at)
    {
        if (deltas.Count == 0 || !IsSupported(course)) return;
        var fingerprint = SpeedTestSession.SourceFingerprintFor(course);
        var path = FilePath(course.Id, fingerprint, source);
        lock (Locks.GetOrAdd(path, _ => new object()))
        {
            var snapshot = Load(path) ?? NewSnapshot(course, source);
            var positions = snapshot.Positions.ToDictionary(x => x.Offset);
            foreach (var delta in deltas)
            {
                if (delta.Offset < 0 || delta.Offset >= snapshot.TextLength) continue;
                if (!positions.TryGetValue(delta.Offset, out var target))
                {
                    target = new() { Offset = delta.Offset };
                    positions.Add(delta.Offset, target);
                }
                target.Opportunities += delta.Opportunities;
                target.Errors += delta.Errors;
                target.CorrectInputs += delta.CorrectInputs;
                target.TimingSamples += delta.TimingSamples;
                target.TotalSeconds += delta.TotalSeconds;
                target.EstimatedBatchSamples += delta.EstimatedBatchSamples;
                target.SkippedTimingSamples += delta.SkippedTimingSamples;
                target.RecentSeconds = target.RecentSeconds.Concat(delta.RecentSeconds).TakeLast(RecentSampleLimit).ToList();
            }
            snapshot.Positions = positions.Values.OrderBy(x => x.Offset).ToList();
            snapshot.UpdatedAt = at;
            Directory.CreateDirectory(_directory);
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, snapshot);
                stream.Flush(true);
            }
            if (TryRead(path) is not null) File.Copy(path, path + ".bak", true);
            File.Move(path + ".tmp", path, true);
        }
    }

    private string FilePath(string id, string fingerprint, HeatmapSource source)
        => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { id, fingerprint, source.ToString() }))))[..32].ToLowerInvariant() + ".json");
    private static ArticleHeatmapSnapshot NewSnapshot(Course course, HeatmapSource source)
    {
        var text = string.Concat(course.Items.Select(x => x.Expected));
        return new()
        {
            CourseId = course.Id, Title = course.Title, Mode = TypingMode.ForCourse(course), Source = source,
            SourceFingerprint = SpeedTestSession.SourceFingerprintFor(course), Text = text, TextLength = text.Length
        };
    }
    private ArticleHeatmapSnapshot? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var primary = TryRead(path);
        if (primary is not null) return primary;
        var backup = TryRead(path + ".bak");
        RecoveryNotice = backup is null ? "文章位置记录无法读取，未从旧成绩推测位置数据。" : "文章位置记录损坏，已恢复最近备份。";
        return backup;
    }
    private static ArticleHeatmapSnapshot? TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<ArticleHeatmapSnapshot>(File.ReadAllText(path));
            if (data is null || data.SchemaVersion != 4 || data.Text is null || data.TextLength != data.Text.Length
                || data.TextLength > MaximumTextLength || data.CourseId is null || data.Title is null || data.Mode is null
                || data.SourceFingerprint != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.Text)))
                || !Enum.IsDefined(data.Source) || data.Positions is null || data.Positions.Count > data.TextLength
                || data.Positions.Any(p => p is null || p.Offset < 0 || p.Offset >= data.TextLength || p.Opportunities < 0
                    || p.Errors < 0 || p.CorrectInputs < 0 || p.Errors + p.CorrectInputs != p.Opportunities
                    || p.TimingSamples < 0 || p.TimingSamples > p.CorrectInputs || !double.IsFinite(p.TotalSeconds) || p.TotalSeconds < 0
                    || p.SkippedTimingSamples < 0 || p.SkippedTimingSamples + p.TimingSamples != p.CorrectInputs
                    || p.EstimatedBatchSamples < 0 || p.EstimatedBatchSamples > p.TimingSamples
                    || p.RecentSeconds is null || p.RecentSeconds.Count > RecentSampleLimit || p.RecentSeconds.Any(t => !double.IsFinite(t) || t <= 0))
                || data.Positions.Select(p => p.Offset).Distinct().Count() != data.Positions.Count) return null;
            return data;
        }
        catch (JsonException) { return null; }
    }
}

public sealed record ArticleInputAttempt(int Offset, bool Correct);

/// <summary>
/// Measures completion time since the preceding correct advancement, including intervening mistakes.
/// Timing is based on committed text events; multi-character batches apportion their event interval.
/// </summary>
public sealed class ArticleTimingSampler
{
    private readonly Course _course;
    private readonly ArticleHeatmapStore _store;
    private readonly HeatmapSource _source;
    private readonly ITypingClock _clock;
    private readonly bool _deferPersistence;
    private readonly int _textLength;
    private readonly Dictionary<int, ArticlePositionSummary> _pending = new();
    private DateTimeOffset? _lastEvent;
    private DateTimeOffset _lastFlush;
    private bool _hasAnchor;
    private double _pendingSeconds;
    private bool _pendingEstimated;
    private int _pendingAttempts;

    public ArticleTimingSampler(Course course, ArticleHeatmapStore store, HeatmapSource source, ITypingClock? clock = null, bool deferPersistence = false)
    {
        _course = course; _store = store; _source = source; _clock = clock ?? new SystemTypingClock();
        _deferPersistence = deferPersistence;
        _textLength = course.Items.Sum(x => x.Expected.Length);
        _lastFlush = _clock.UtcNow;
    }
    public DateTimeOffset CaptureTime() => _clock.UtcNow;

    public void ObserveBatch(IReadOnlyList<ArticleInputAttempt> attempts, DateTimeOffset? observedAt = null)
    {
        if (attempts.Count == 0 || !ArticleHeatmapStore.IsSupported(_course)) return;
        var at = observedAt ?? _clock.UtcNow;
        var gap = _lastEvent.HasValue ? (at - _lastEvent.Value).TotalSeconds : 0;
        if (!_lastEvent.HasValue || gap < 0 || gap > 5)
        {
            _hasAnchor = false;
            _pendingSeconds = 0;
            _pendingEstimated = false;
            gap = 0;
        }
        var share = gap / attempts.Count;
        var estimated = attempts.Count > 1;
        foreach (var attempt in attempts)
        {
            if (attempt.Offset < 0 || attempt.Offset >= _textLength) continue;
            if (!_pending.TryGetValue(attempt.Offset, out var position))
                _pending[attempt.Offset] = position = new() { Offset = attempt.Offset };
            position.Opportunities++;
            _pendingAttempts++;
            if (_hasAnchor)
            {
                _pendingSeconds += share;
                if (estimated && share > 0) _pendingEstimated = true;
            }
            if (attempt.Correct)
            {
                position.CorrectInputs++;
                if (_hasAnchor && _pendingSeconds > 0)
                {
                    position.TimingSamples++;
                    position.TotalSeconds += _pendingSeconds;
                    if (_pendingEstimated) position.EstimatedBatchSamples++;
                    position.RecentSeconds.Add(_pendingSeconds);
                    if (position.RecentSeconds.Count > ArticleHeatmapStore.RecentSampleLimit) position.RecentSeconds.RemoveAt(0);
                }
                else position.SkippedTimingSamples++;
                _pendingSeconds = 0;
                _pendingEstimated = false;
                _hasAnchor = true;
            }
            else position.Errors++;
        }
        _lastEvent = at;
        if (!_deferPersistence && (_pendingAttempts >= 32 || (at - _lastFlush).TotalSeconds >= 1)) Flush();
    }

    /// <summary>Discard any unfinished interval, including correction time, after focus/pause/backspace.</summary>
    public void InterruptTiming()
    {
        _lastEvent = null;
        _hasAnchor = false;
        _pendingSeconds = 0;
        _pendingEstimated = false;
    }
    public void Flush()
    {
        if (_pending.Count == 0) return;
        var at = _clock.UtcNow;
        _store.Add(_course, _source, _pending.Values.ToArray(), at);
        _pending.Clear();
        _pendingAttempts = 0;
        _lastFlush = at;
    }
    public void Discard()
    {
        _pending.Clear(); _pendingAttempts = 0;
        InterruptTiming();
    }
}
