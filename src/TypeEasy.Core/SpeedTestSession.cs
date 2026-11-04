namespace TypeEasy.Core;

public interface ITypingClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemTypingClock : ITypingClock
{
    private readonly DateTimeOffset _origin = DateTimeOffset.UtcNow;
    private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();
    public DateTimeOffset UtcNow => _origin + _elapsed.Elapsed;
}
public enum SpeedTestStatus { Completed, Exhausted, Cancelled }

public sealed class SpeedTestResult
{
    public string Id { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string Mode { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceTitle { get; set; } = "";
    public string SourceGroup { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public int DurationSeconds { get; set; }
    public double ElapsedSeconds { get; set; }
    public int NetCharacters { get; set; }
    public long CorrectInputs { get; set; }
    public long Errors { get; set; }
    public long Backspaces { get; set; }
    public SpeedTestStatus Status { get; set; }
    public double CharactersPerMinute => ElapsedSeconds <= 0 ? 0 : NetCharacters * 60.0 / ElapsedSeconds;
    public double EnglishEquivalentWpm => CharactersPerMinute / 5;
    public double Accuracy => CorrectInputs + Errors == 0 ? 1 : (double)CorrectInputs / (CorrectInputs + Errors);
}

/// <summary>A fresh timed run, independent from practice progress. No focus-pause behavior.</summary>
public sealed class SpeedTestSession : IDisposable
{
    private readonly Course _course;
    private readonly AnalyticsStore _store;
    private readonly ITypingClock _clock;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private DateTimeOffset? _started;
    private bool _saved;
    private bool _cancelledBeforeStart;
    private readonly List<TypingAttempt> _attempts = new();
    private string _typedRecent = "";
    private readonly string _sourceFingerprint;
    private readonly ArticleTimingSampler? _heatmap;
    private bool _heatmapSaved;
    public string ReferenceText { get; }
    public int Position { get; private set; }
    public int CompletedCharacters => Position;
    public int DurationSeconds { get; }
    public bool HasStarted => _started.HasValue;
    public bool IsFinished => Result is not null || _cancelledBeforeStart;
    public bool? LastInputCorrect { get; private set; }
    public string LastActual { get; private set; } = "";
    public string LastExpected { get; private set; } = "";
    public long CorrectInputs { get; private set; }
    public long Errors { get; private set; }
    public long Backspaces { get; private set; }
    public double Accuracy => CorrectInputs + Errors == 0 ? 1 : (double)CorrectInputs / (CorrectInputs + Errors);
    public SpeedTestResult? Result { get; private set; }
    public char? ExpectedCharacter => Position < ReferenceText.Length && !IsFinished ? ReferenceText[Position] : null;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(Result?.ElapsedSeconds ?? (!_started.HasValue ? 0 : Math.Clamp((_clock.UtcNow - _started.Value).TotalSeconds, 0, DurationSeconds)));
    public TimeSpan Remaining => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds - Elapsed.TotalSeconds));

    public SpeedTestSession(Course course, int durationSeconds, AnalyticsStore store, ITypingClock? clock = null, string? heatmapDirectory = null)
    {
        if (durationSeconds is not (60 or 180 or 300 or 600)) throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Choose 60, 180, 300 or 600 seconds.");
        _course = course ?? throw new ArgumentNullException(nameof(course));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? new SystemTypingClock();
        DurationSeconds = durationSeconds;
        ReferenceText = string.Concat(course.Items.Select(x => x.Expected));
        _sourceFingerprint = SourceFingerprintFor(course);
        if (ArticleHeatmapStore.IsSupported(course))
        {
            heatmapDirectory ??= Path.Combine(Path.GetDirectoryName(store.DirectoryPath)!, "Heatmaps");
            _heatmap = new(course, new ArticleHeatmapStore(heatmapDirectory), HeatmapSource.Test, _clock, deferPersistence: true);
        }
    }

    public static string SourceFingerprintFor(Course course) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Concat(course.Items.Select(x => x.Expected)))));

    public void Feed(string text)
    {
        Tick();
        if (IsFinished || string.IsNullOrEmpty(text)) return;
        _started ??= _clock.UtcNow;
        if (ReferenceText.Length == 0) { Finish(SpeedTestStatus.Exhausted); return; }
        var observedAt = _clock.UtcNow;
        var heatmapAttempts = _heatmap is null ? null : new List<ArticleInputAttempt>();
        try
        {
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            // A committed text batch arrived at one timestamp; processing cost must not split
            // that event across the deadline or prematurely persist half its timing samples.
            if (IsFinished) break;
            LastExpected = ReferenceText[Position].ToString();
            LastActual = c.ToString();
            LastInputCorrect = c == ReferenceText[Position];
            heatmapAttempts?.Add(new(Position, LastInputCorrect == true));
            var beforeStart = Math.Max(0, Position - 2);
            var afterStart = Position + 1;
            _attempts.Add(new(LastExpected, LastActual, ReferenceText.Substring(beforeStart, Position - beforeStart),
                ReferenceText.Substring(afterStart, Math.Min(2, ReferenceText.Length - afterStart)), _typedRecent));
            _typedRecent += c;
            if (_typedRecent.Length > 2) _typedRecent = _typedRecent[^2..];
            if (LastInputCorrect == true) { CorrectInputs++; Position++; }
            else Errors++;
            if (Position == ReferenceText.Length) break;
        }
        }
        finally
        {
            if (heatmapAttempts is not null) _heatmap!.ObserveBatch(heatmapAttempts, observedAt);
        }
        if (!IsFinished && Position == ReferenceText.Length) Finish(SpeedTestStatus.Exhausted);
    }

    public void Backspace()
    {
        Tick();
        if (!HasStarted || IsFinished) return;
        Backspaces++;
        if (LastInputCorrect != false && Position > 0) { Position--; InterruptTiming(); }
        if (_typedRecent.Length > 0) _typedRecent = _typedRecent[..^1];
        LastInputCorrect = null;
    }

    public void Tick()
    {
        if (Result is not null) { Persist(); return; }
        if (_started.HasValue && (_clock.UtcNow - _started.Value).TotalSeconds >= DurationSeconds)
            Finish(SpeedTestStatus.Completed);
    }

    /// <summary>Drop the next timing interval on focus changes; the test countdown keeps running.</summary>
    public void InterruptTiming() => _heatmap?.InterruptTiming();

    public void Cancel()
    {
        Tick();
        if (IsFinished) return;
        if (HasStarted) Finish(SpeedTestStatus.Cancelled);
        else _cancelledBeforeStart = true;
    }

    private void Finish(SpeedTestStatus status)
    {
        if (Result is null)
        {
            var elapsed = status == SpeedTestStatus.Completed ? DurationSeconds
                : !_started.HasValue ? 0 : Math.Clamp((_clock.UtcNow - _started.Value).TotalSeconds, 0, DurationSeconds);
            Result = new SpeedTestResult
            {
                Id = _id, StartedAt = _started ?? _clock.UtcNow,
                FinishedAt = status == SpeedTestStatus.Completed ? _started!.Value.AddSeconds(DurationSeconds) : _clock.UtcNow,
                Mode = TypingMode.ForCourse(_course), SourceId = _course.Id, SourceTitle = _course.Title, SourceGroup = _course.Group,
                SourceFingerprint = _sourceFingerprint,
                DurationSeconds = DurationSeconds, ElapsedSeconds = elapsed, NetCharacters = Position,
                CorrectInputs = CorrectInputs, Errors = Errors, Backspaces = Backspaces, Status = status
            };
        }
        Persist();
    }
    private void Persist()
    {
        if (Result is null) return;
        if (!_saved)
        {
            _store.SaveSpeedTest(Result, _attempts);
            _saved = true;
        }
        if (!_heatmapSaved)
        {
            if (Result.Status == SpeedTestStatus.Cancelled) _heatmap?.Discard(); else _heatmap?.Flush();
            _heatmapSaved = true;
        }
    }
    public void Dispose() => Cancel();
}
