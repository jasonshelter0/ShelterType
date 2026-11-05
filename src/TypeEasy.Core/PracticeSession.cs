using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TypeEasy.Core;

public sealed class ConfusionStat
{
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public long Count { get; set; }
}

public sealed class KeyStat
{
    public string Key { get; set; } = "";
    public long Attempts { get; set; }
    public long Errors { get; set; }
    public long TimedInputs { get; set; }
    public double TotalSeconds { get; set; }
    public double ErrorRate => Attempts == 0 ? 0 : (double)Errors / Attempts;
    public double AverageSeconds => TimedInputs == 0 ? 0 : TotalSeconds / TimedInputs;
}

public sealed class PairStat
{
    public string Pair { get; set; } = "";
    public long Attempts { get; set; }
    public long Errors { get; set; }
    public double TotalSeconds { get; set; }
    public long TimedInputs { get; set; }
    public double AverageSeconds => TimedInputs == 0 ? 0 : TotalSeconds / TimedInputs;
}

public sealed class ItemStat
{
    public int ItemIndex { get; set; }
    public long Attempts { get; set; }
    public long Errors { get; set; }
}

public sealed class PracticeState
{
    public int SchemaVersion { get; set; } = 1;
    public string CourseId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int ItemIndex { get; set; }
    public int CharacterIndex { get; set; }
    public long TotalInputs { get; set; }
    public long CorrectInputs { get; set; }
    public long Errors { get; set; }
    public long Backspaces { get; set; }
    public double ElapsedSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ConfusionStat> Confusions { get; set; } = new();
    public List<KeyStat> Keys { get; set; } = new();
    public List<PairStat> Pairs { get; set; } = new();
    public List<ItemStat> Items { get; set; } = new();
}

public sealed record InputResult(int Accepted, int Rejected, bool CompletedItem, bool CompletedCourse);

/// <summary>
/// A portable strict typing session. Wrong characters count as errors but never advance the cursor.
/// Progress is saved after each character. UI should Pause on loss of focus and Resume on return.
/// The first backspace after a rejected character dismisses that error; otherwise it rewinds a
/// correctly entered character. Statistics count physical attempts and are not erased by rewinding.
/// </summary>
public sealed class PracticeSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly int[] _characterOffsets;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _betweenInputs = new();
    private bool _disposed;
    private bool _havePreviousInput;
    private readonly AnalyticsStore? _analytics;
    private readonly string _analyticsMode;
    private readonly string _analyticsText;
    private readonly List<TypingAttempt> _pendingAttempts = new();
    private string _typedRecent = "";
    private double _pendingActiveSeconds;
    private long _pendingCorrect, _pendingErrors, _pendingBackspaces;
    private readonly Stopwatch _analyticsFlushWatch = Stopwatch.StartNew();
    private readonly ArticleTimingSampler? _heatmap;

    public Course Course { get; }
    public PracticeState State { get; private set; }
    public string? ResumeNotice { get; private set; }
    public string SavePath => _path;
    public bool IsRunning => _clock.IsRunning;
    public bool IsComplete => State.ItemIndex >= Course.Items.Count;
    public ExerciseItem? CurrentItem => IsComplete ? null : Course.Items[State.ItemIndex];
    public char? ExpectedCharacter => CurrentItem is { } item && State.CharacterIndex < item.Expected.Length
        ? item.Expected[State.CharacterIndex] : null;
    public bool? LastInputCorrect { get; private set; }
    public string LastActual { get; private set; } = "";
    public string LastExpected { get; private set; } = "";
    public double Accuracy => State.TotalInputs == 0 ? 1 : (double)State.CorrectInputs / State.TotalInputs;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(State.ElapsedSeconds + _clock.Elapsed.TotalSeconds);
    public int TotalCharacters => _characterOffsets[^1];
    public int CompletedCharacters => _characterOffsets[State.ItemIndex] + State.CharacterIndex;
    public double Progress => TotalCharacters == 0 ? 1 : (double)CompletedCharacters / TotalCharacters;
    public CourseProgressSummary ProgressSummary => CourseProgressSummary.FromState(State, CompletedCharacters, TotalCharacters, IsComplete);
    public double CharactersPerMinute => Elapsed.TotalMinutes <= 0 ? 0 : State.CorrectInputs / Elapsed.TotalMinutes;

    private PracticeSession(Course course, string dataDirectory, string? analyticsDirectory, string? heatmapDirectory, ITypingClock? timingClock)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Course = course;
        _analytics = analyticsDirectory is null ? null : new AnalyticsStore(analyticsDirectory);
        _analyticsMode = TypingMode.ForCourse(course);
        _analyticsText = _analytics is null ? "" : string.Concat(course.Items.Select(x => x.Expected));
        if (heatmapDirectory is not null && ArticleHeatmapStore.IsSupported(course))
            _heatmap = new(course, new ArticleHeatmapStore(heatmapDirectory), HeatmapSource.Practice, timingClock);
        // Course items are immutable for an open session. Cache offsets so UI refreshes stay O(1).
        _characterOffsets = new int[course.Items.Count + 1];
        for (var i = 0; i < course.Items.Count; i++)
            _characterOffsets[i + 1] = checked(_characterOffsets[i] + course.Items[i].Expected.Length);
        Directory.CreateDirectory(dataDirectory);
        _path = ProgressPath(course, dataDirectory);
        State = ReadSavedState(course, _path, out var notice);
        ResumeNotice = notice;
        SkipEmptyItems();
    }

    public static PracticeSession Open(Course course, string dataDirectory, string? analyticsDirectory = null,
        string? heatmapDirectory = null, ITypingClock? timingClock = null) => new(course, dataDirectory, analyticsDirectory, heatmapDirectory, timingClock);

    /// <summary>
    /// Reads the same validated checkpoint used by Open without opening a session, creating a
    /// directory, or writing a checkpoint. I/O failures are reported as Unknown rather than 0%.
    /// </summary>
    public static CourseProgressSummary ReadProgress(Course course, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var total = course.Items.Sum(item => item.Expected.Length);
        try
        {
            var path = ProgressPath(course, dataDirectory);
            // Most courses have never been opened. Avoid serializing their contents merely
            // to discover that no saved checkpoint needs fingerprint validation.
            if (!CheckpointExists(path)) return new(total == 0 ? CourseProgressStatus.Completed : CourseProgressStatus.NotStarted, 0, total);
            var state = ReadSavedState(course, path, out _);
            var index = state.ItemIndex;
            while (index < course.Items.Count && course.Items[index].Expected.Length == 0) index++;
            var completed = state.CharacterIndex;
            for (var i = 0; i < index; i++) completed = checked(completed + course.Items[i].Expected.Length);
            return CourseProgressSummary.FromState(state, completed, total, index >= course.Items.Count);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(CourseProgressStatus.Unknown, 0, total);
        }
    }

    private static string ProgressPath(Course course, string dataDirectory)
        => Path.Combine(dataDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(course.Id))).ToLowerInvariant() + ".json");

    public static void MigrateCourseIdentity(Course source,Course target,string directory)
    {
        var from=ProgressPath(source,directory);var to=ProgressPath(target,directory);
        if(!CheckpointExists(from)||Fingerprint(source)!=Fingerprint(target))return;
        var saved=ReadSavedState(source,from,out var notice);if(notice is not null)return;
        if(CheckpointExists(to)&&ReadSavedState(target,to,out var existingNotice).Fingerprint==saved.Fingerprint&&existingNotice is null)return;
        Directory.CreateDirectory(directory);if(File.Exists(to)&&!File.Exists(to+".before-shuffle"))File.Copy(to,to+".before-shuffle");
        saved.CourseId=target.Id;File.WriteAllText(to+".tmp",JsonSerializer.Serialize(saved));File.Move(to+".tmp",to,true);
    }
    public static bool MigrateWubiPosition(Course source,Course target,string directory)
    {
        if(target.InputMode!="wubi-ime"||target.BaseCourseId!=source.Id||source.Items.Count!=target.Items.Count||
            !source.Items.Select(i=>i.Prompt).SequenceEqual(target.Items.Select(i=>i.Prompt))||
            CheckpointExists(ProgressPath(target,directory)))return false;
        var saved=ReadSavedState(source,ProgressPath(source,directory),out _);
        if(saved.ItemIndex==0)return false;
        using var session=Open(target,directory);
        session.State.ItemIndex=saved.ItemIndex;
        session.State.CharacterIndex=0;
        if(session.IsComplete)session.State.CompletedAt=saved.CompletedAt??DateTimeOffset.UtcNow;
        session.Save();return true;
    }

    private static PracticeState ReadSavedState(Course course, string path, out string? notice)
    {
        var state = CreateState(course);
        notice = null;
        if (CheckpointExists(path))
        {
            var loaded = TryRead(path);
            if (loaded is null)
            {
                loaded = TryRead(path + ".bak");
                notice = loaded is null ? "进度文件和备份无法读取，已开始新练习。" : "进度文件损坏，已从最近的备份恢复。";
            }
            if (loaded is not null)
            {
                if (loaded.CourseId != course.Id || loaded.Fingerprint != state.Fingerprint)
                    notice = "课程内容已变化，已开始新进度，避免接续到错误位置。";
                else if (ValidState(course, loaded))
                    state = loaded;
                else
                {
                    var backup = TryRead(path + ".bak");
                    if (backup is not null && backup.CourseId == course.Id && backup.Fingerprint == state.Fingerprint && ValidState(course, backup))
                    {
                        state = backup;
                        notice = "保存的进度无效，已从最近的备份恢复。";
                    }
                    else notice = "保存的进度位置无效，已开始新练习。";
                }
            }
        }
        return state;
    }

    private PracticeState NewState() => CreateState(Course);
    private static PracticeState CreateState(Course course) => new()
    {
        CourseId = course.Id,
        Fingerprint = Fingerprint(course),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string Fingerprint(Course course)
    {
        // Length-prefixed JSON encoding avoids ambiguous concatenations of items.
        var contents = JsonSerializer.Serialize(course.Items.Select(x => new[] { x.Prompt, x.Expected, x.Hint }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
    }

    private static PracticeState? TryRead(string path)
    {
        try
        {
            return CheckpointExists(path) ? JsonSerializer.Deserialize<PracticeState>(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (JsonException) { return null; }
        // Access and disk failures are surfaced to the caller, rather than silently losing progress.
    }

    private static bool CheckpointExists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new IOException("A directory occupies the progress checkpoint path.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private bool ValidState(PracticeState state) => ValidState(Course, state);
    private static bool ValidState(Course course, PracticeState state)
    {
        return state.SchemaVersion == 1 && state.ItemIndex >= 0 && state.ItemIndex <= course.Items.Count
            && state.CharacterIndex >= 0
            && (state.ItemIndex == course.Items.Count ? state.CharacterIndex == 0
                : state.CharacterIndex < Math.Max(1, course.Items[state.ItemIndex].Expected.Length))
            && state.TotalInputs >= 0 && state.CorrectInputs >= 0 && state.Errors >= 0
            && state.CorrectInputs <= state.TotalInputs && state.Errors == state.TotalInputs - state.CorrectInputs
            && double.IsFinite(state.ElapsedSeconds) && state.ElapsedSeconds >= 0
            && state.Backspaces >= 0
            && state.Confusions is not null && state.Keys is not null && state.Pairs is not null && state.Items is not null
            && state.Confusions.All(x => x is not null && x.Expected is not null && x.Actual is not null && x.Count >= 0)
            && state.Keys.All(x => x is not null && x.Key is not null && x.Attempts >= 0 && x.Errors >= 0 && x.Errors <= x.Attempts)
            && state.Pairs.All(x => x is not null && x.Pair is not null && x.Attempts >= 0 && x.Errors >= 0 && x.Errors <= x.Attempts)
            && state.Items.All(x => x is not null && x.ItemIndex >= 0 && x.ItemIndex < course.Items.Count && x.Attempts >= 0 && x.Errors >= 0 && x.Errors <= x.Attempts);
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsComplete && !_clock.IsRunning)
        {
            InterruptTiming();
            _clock.Start();
            _betweenInputs.Restart();
            _havePreviousInput = false;
        }
    }

    public void Pause()
    {
        if (_disposed) return;
        AccumulateTime();
        _betweenInputs.Reset();
        _havePreviousInput = false;
        Save();
        InterruptTiming();
        _heatmap?.Flush();
    }

    public void InterruptTiming() => _heatmap?.InterruptTiming();

    private void AccumulateTime()
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        State.ElapsedSeconds += elapsed;
        if (_analytics is not null) _pendingActiveSeconds += elapsed;
        _clock.Reset();
    }

    public InputResult Feed(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(text) || IsComplete) return new(0, 0, false, IsComplete);
        Resume();
        var accepted = 0;
        var rejected = 0;
        var startItem = State.ItemIndex;
        // A committed IME string is handled as text; composition keystrokes never reach this method.
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var observedAt = _heatmap?.CaptureTime();
        var heatmapAttempts = _heatmap is null ? null : new List<ArticleInputAttempt>();
        try
        {
        for (var textIndex = 0; textIndex < normalized.Length; textIndex++)
        {
            if (IsComplete) break;
            var actual = normalized[textIndex];
            var expected = ExpectedCharacter!.Value;
            heatmapAttempts?.Add(new(CompletedCharacters, actual == expected));
            if (_analytics is not null)
            {
                var position = CompletedCharacters;
                var beforeStart = Math.Max(0, position - 2);
                var afterStart = position + 1;
                _pendingAttempts.Add(new TypingAttempt(expected.ToString(), actual.ToString(),
                    _analyticsText.Substring(beforeStart, position - beforeStart),
                    _analyticsText.Substring(afterStart, Math.Min(2, _analyticsText.Length - afterStart)), _typedRecent));
                _typedRecent += actual;
                if (_typedRecent.Length > 2) _typedRecent = _typedRecent[^2..];
                if (expected == actual) _pendingCorrect++; else _pendingErrors++;
            }
            LastExpected = expected.ToString();
            LastActual = actual.ToString();
            LastInputCorrect = actual == expected;
            State.TotalInputs++;
            var key = State.Keys.Find(x => x.Key == LastExpected);
            if (key is null) { key = new() { Key = LastExpected }; State.Keys.Add(key); }
            key.Attempts++;
            var itemStat = State.Items.Find(x => x.ItemIndex == State.ItemIndex);
            if (itemStat is null) { itemStat = new() { ItemIndex = State.ItemIndex }; State.Items.Add(itemStat); }
            itemStat.Attempts++;

            PairStat? pair = null;
            if (State.CharacterIndex > 0)
            {
                var pairText = CurrentItem!.Expected.Substring(State.CharacterIndex - 1, 2);
                pair = State.Pairs.Find(x => x.Pair == pairText);
                if (pair is null) { pair = new() { Pair = pairText }; State.Pairs.Add(pair); }
                pair.Attempts++;
            }
            // Ignore pause-sized gaps and intra-IME-batch gaps; neither measures a key transition.
            var seconds = _betweenInputs.Elapsed.TotalSeconds;
            if (_havePreviousInput && textIndex == 0 && seconds is >= 0.025 and <= 5)
            {
                key.TotalSeconds += seconds;
                key.TimedInputs++;
                if (pair is not null) { pair.TotalSeconds += seconds; pair.TimedInputs++; }
            }
            _betweenInputs.Restart();
            _havePreviousInput = true;
            if (LastInputCorrect == true)
            {
                accepted++;
                State.CorrectInputs++;
                State.CharacterIndex++;
                if (State.CharacterIndex >= CurrentItem!.Expected.Length)
                {
                    State.ItemIndex++;
                    State.CharacterIndex = 0;
                    SkipEmptyItems();
                }
            }
            else
            {
                rejected++;
                State.Errors++;
                key.Errors++;
                itemStat.Errors++;
                if (pair is not null) pair.Errors++;
                var confusion = State.Confusions.Find(x => x.Expected == LastExpected && x.Actual == LastActual);
                if (confusion is null) { confusion = new() { Expected = LastExpected, Actual = LastActual }; State.Confusions.Add(confusion); }
                confusion.Count++;
            }
            if (IsComplete)
            {
                State.CompletedAt = DateTimeOffset.UtcNow;
                AccumulateTime();
            }
            Save();
        }
        }
        finally
        {
            if (heatmapAttempts is not null)
            {
                _heatmap!.ObserveBatch(heatmapAttempts, observedAt);
                if (IsComplete) _heatmap.Flush();
            }
        }
        return new(accepted, rejected, State.ItemIndex != startItem, IsComplete);
    }

    private void SkipEmptyItems()
    {
        while (State.ItemIndex < Course.Items.Count && Course.Items[State.ItemIndex].Expected.Length == 0)
            State.ItemIndex++;
        if (IsComplete) State.CompletedAt ??= DateTimeOffset.UtcNow;
    }

    public void Backspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        State.Backspaces++;
        if (_analytics is not null) _pendingBackspaces++;
        if (_typedRecent.Length > 0) _typedRecent = _typedRecent[..^1];
        if (LastInputCorrect != false)
        {
            InterruptTiming();
            if (State.CharacterIndex > 0) State.CharacterIndex--;
            else
            {
                while (State.ItemIndex > 0)
                {
                    State.ItemIndex--;
                    if (CurrentItem!.Expected.Length > 0)
                    {
                        State.CharacterIndex = CurrentItem.Expected.Length - 1;
                        break;
                    }
                }
            }
        }
        if (!IsComplete) State.CompletedAt = null;
        LastInputCorrect = null;
        _havePreviousInput = false;
        Save();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_analytics is not null || _heatmap is not null) Pause();
        _clock.Reset();
        _betweenInputs.Reset();
        _havePreviousInput = false;
        _typedRecent = "";
        State = NewState();
        LastInputCorrect = null;
        LastActual = LastExpected = "";
        ResumeNotice = null;
        SkipEmptyItems();
        Save();
    }

    public Course BuildReviewCourse(int maximumItems = 24)
    {
        var missed = State.Keys.Where(x => x.Errors > 0).Select(x => x.Key).ToHashSet();
        var selected = State.Items.Where(x => x.Errors > 0 && x.ItemIndex >= 0 && x.ItemIndex < Course.Items.Count)
            .OrderByDescending(x => x.Errors).ThenByDescending(x => (double)x.Errors / Math.Max(1, x.Attempts))
            .Select(x => x.ItemIndex)
            .Concat(Enumerable.Range(0, Course.Items.Count)
                .Where(i => Course.Items[i].Expected.Any(c => missed.Contains(c.ToString()))))
            .Distinct().Take(Math.Max(0, maximumItems)).ToArray();
        return new Course
        {
            Id = "review:" + Course.Id,
            Origin=Course.Origin,SourceUrl=Course.SourceUrl,Author=Course.Author,License=Course.License,BaseCourseId=Course.Id,
            Title = "错键巩固 · " + Course.Title,
            Category = Course.Category,
            Variant = Course.Variant,
            Group = "错键巩固/" + Course.Variant,
            InputMode = Course.InputMode,
            TeachingImage = Course.TeachingImage,
            Items = selected.Select(i => new ExerciseItem
            {
                Prompt = Course.Items[i].Prompt,
                Expected = Course.Items[i].Expected,
                Hint = Course.Items[i].Hint,
                ImagePath = Course.Items[i].ImagePath
                ,KeySequence=Course.Items[i].KeySequence
            }).ToList()
        };
    }

    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var running = _clock.IsRunning;
        AccumulateTime();
        if (running && !IsComplete) _clock.Start();
        State.UpdatedAt = DateTimeOffset.UtcNow;
        var temporary = _path + ".tmp";
        // Flush the temporary file before atomically replacing the live checkpoint.
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, State, JsonOptions);
            stream.Flush(true);
        }
        // Do not overwrite a usable backup with a corrupt primary after recovery.
        if (File.Exists(_path) && TryRead(_path) is { } previous && ValidState(previous)) File.Copy(_path, _path + ".bak", true);
        File.Move(temporary, _path, true);
        // Checkpoints remain per-character. Analytics batch up to 8 attempts to avoid a large
        // historical context file blocking every keystroke; pause, completion and exit flush.
        if (_analytics is not null && (!_clock.IsRunning || IsComplete || _pendingAttempts.Count >= 8
            || _analyticsFlushWatch.Elapsed.TotalSeconds >= 1))
        {
            _analytics.RecordPractice(_analyticsMode, _pendingActiveSeconds, _pendingCorrect, _pendingErrors,
                _pendingBackspaces, _pendingAttempts);
            _pendingActiveSeconds = 0;
            _pendingCorrect = _pendingErrors = _pendingBackspaces = 0;
            _pendingAttempts.Clear();
            _analyticsFlushWatch.Restart();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Pause();
        _disposed = true;
    }
}
