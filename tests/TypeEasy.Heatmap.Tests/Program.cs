using System.Text.Json;
using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
var root = Path.Combine(Path.GetTempPath(), "TypeEasyHeatmaps-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int assertions = 0;
void Assert(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}
Course Article(string id = "english/t_article/test.txt", string text = "abcdef") => new()
{
    Id = id, Category = id.StartsWith("chinese") ? "中文" : "英文", Variant = "文章", Title = "测试文章", Group = "文章",
    Items = Enumerable.Range(0, (text.Length + 1) / 2).Select(i => new ExerciseItem
        { Prompt = text.Substring(i * 2, Math.Min(2, text.Length - i * 2)), Expected = text.Substring(i * 2, Math.Min(2, text.Length - i * 2)) }).ToList()
};
ArticleInputAttempt[] Attempt(int offset, bool correct = true) => new[] { new ArticleInputAttempt(offset, correct) };
try
{
    var course = Article();
    var store = new ArticleHeatmapStore(Path.Combine(root, "Heatmaps"));
    var clock = new ManualClock();
    var sampler = new ArticleTimingSampler(course, store, HeatmapSource.Practice, clock);
    sampler.ObserveBatch(Attempt(0));
    clock.Advance(1); sampler.ObserveBatch(Attempt(1, false));
    clock.Advance(2); sampler.ObserveBatch(Attempt(1));
    clock.Advance(2); sampler.ObserveBatch(new[] { new ArticleInputAttempt(2, true), new ArticleInputAttempt(3, true) });
    clock.Advance(6); sampler.ObserveBatch(Attempt(4));
    clock.Advance(1); sampler.ObserveBatch(Attempt(5));
    sampler.Flush();
    var snapshot = store.Read(course, HeatmapSource.Practice);
    var positions = snapshot.Positions.ToDictionary(x => x.Offset);
    Assert(snapshot.Text == "abcdef" && snapshot.TextLength == 6, "saved text uses exact full concatenation across lesson items");
    Assert(positions[0].TimingSamples == 0 && positions[0].SkippedTimingSamples == 1, "first key has no fabricated timing");
    Assert(positions[1].Opportunities == 2 && positions[1].Errors == 1 && positions[1].CorrectInputs == 1, "wrong retry shares same absolute position and denominator");
    Assert(positions[1].MeanSeconds == 3, "correct completion includes preceding mistake correction time");
    Assert(positions[2].MeanSeconds == 1 && positions[3].MeanSeconds == 1, "multi-character commit apportions event interval");
    Assert(positions[2].EstimatedBatchSamples == 1 && positions[3].EstimatedBatchSamples == 1, "batched estimates explicitly marked");
    Assert(positions[4].TimingSamples == 0 && positions[5].MeanSeconds == 1, "idle gap over five seconds discarded and subsequent timing recovers");
    Assert(store.Read(course, HeatmapSource.Test).Positions.Count == 0, "practice observations never appear in test channel");

    var chinese = Article("chinese/t_article/ime.txt", "你好世界");
    var ime = new ArticleTimingSampler(chinese, store, HeatmapSource.Practice, clock = new ManualClock());
    ime.ObserveBatch(new[] { new ArticleInputAttempt(0, true), new ArticleInputAttempt(1, true) });
    clock.Advance(2); ime.ObserveBatch(new[] { new ArticleInputAttempt(2, true), new ArticleInputAttempt(3, true) });
    ime.Flush();
    var imePositions = store.Read(chinese, HeatmapSource.Practice).Positions;
    Assert(imePositions.Take(2).All(x => x.TimingSamples == 0), "first IME batch entirely has no prior timing anchor");
    Assert(imePositions.Skip(2).All(x => x.MeanSeconds == 1 && x.EstimatedBatchSamples == 1), "IME elapsed shared across committed characters, never claimed physical key times");
    var longIme = Article("chinese/t_article/long-ime.txt", new string('字', 51));
    var longImeSampler = new ArticleTimingSampler(longIme, store, HeatmapSource.Practice, clock = new ManualClock());
    longImeSampler.ObserveBatch(Attempt(0)); clock.Advance(0.5);
    longImeSampler.ObserveBatch(Enumerable.Range(1, 50).Select(i => new ArticleInputAttempt(i, true)).ToArray()); longImeSampler.Flush();
    Assert(store.Read(longIme, HeatmapSource.Practice).Positions.Skip(1).All(p => p.TimingSamples == 1 && Math.Abs(p.MeanSeconds - 0.01) < 0.000001),
        "long committed batch retains small positive apportioned estimates");

    var interrupted = Article("english/t_article/interrupted.txt");
    var interruptionSampler = new ArticleTimingSampler(interrupted, store, HeatmapSource.Practice, clock = new ManualClock());
    interruptionSampler.ObserveBatch(Attempt(0));
    clock.Advance(1); interruptionSampler.InterruptTiming(); clock.Advance(1); interruptionSampler.ObserveBatch(Attempt(1));
    clock.Advance(1); interruptionSampler.ObserveBatch(Attempt(2)); interruptionSampler.Flush();
    var interruptedPositions = store.Read(interrupted, HeatmapSource.Practice).Positions;
    Assert(interruptedPositions.Single(x => x.Offset == 1).TimingSamples == 0, "focus interruption excludes next event even for a short gap");
    Assert(interruptedPositions.Single(x => x.Offset == 2).MeanSeconds == 1, "sampling resumes after new anchor");

    var correction = Article("english/t_article/correction.txt");
    var correctionSampler = new ArticleTimingSampler(correction, store, HeatmapSource.Practice, clock = new ManualClock());
    correctionSampler.ObserveBatch(Attempt(0));
    clock.Advance(4); correctionSampler.ObserveBatch(Attempt(1, false));
    clock.Advance(4); correctionSampler.ObserveBatch(Attempt(1, false));
    clock.Advance(4); correctionSampler.ObserveBatch(Attempt(1)); correctionSampler.Flush();
    Assert(store.Read(correction, HeatmapSource.Practice).Positions.Single(x => x.Offset == 1).MeanSeconds == 12,
        "extended active correction included when no individual idle interval exceeds five seconds");

    var practiceCourse = Article("english/t_article/practice.txt");
    var heatmapDir = Path.Combine(root, "PracticeHeatmaps");
    clock = new ManualClock();
    using (var session = PracticeSession.Open(practiceCourse, Path.Combine(root, "Progress"), null, heatmapDir, clock))
    {
        session.Feed("a"); clock.Advance(1); session.Feed("b"); session.Backspace();
        clock.Advance(2); session.Feed("b"); clock.Advance(1); session.Feed("c");
        session.Pause();
        Assert(session.CompletedCharacters == 3, "backspace and retype do not inflate net practice progress");
    }
    var practicePositions = new ArticleHeatmapStore(heatmapDir).Read(practiceCourse, HeatmapSource.Practice).Positions;
    Assert(practicePositions.Single(x => x.Offset == 1).CorrectInputs == 2, "retyping remains counted at original absolute position");
    Assert(practicePositions.Single(x => x.Offset == 1).TimingSamples == 1, "first retype after rewind excludes ambiguous interval");
    Assert(practicePositions.Single(x => x.Offset == 2).MeanSeconds == 1, "article item boundary does not reset absolute offset or timing");
    using (var reopened = PracticeSession.Open(practiceCourse, Path.Combine(root, "Progress"), null, heatmapDir, clock))
    {
        clock.Advance(1); reopened.Feed("d"); reopened.Pause();
        Assert(reopened.CompletedCharacters == 4, "old progress still resumes into correct article position");
    }
    Assert(new ArticleHeatmapStore(heatmapDir).Read(practiceCourse, HeatmapSource.Practice).Positions.Single(x => x.Offset == 3).TimingSamples == 0,
        "reopening session does not measure time spent outside application");

    var tests = new AnalyticsStore(Path.Combine(root, "Analytics"));
    var speedCourse = Article("english/t_article/speed.txt");
    clock = new ManualClock();
    using (var speed = new SpeedTestSession(speedCourse, 60, tests, clock, heatmapDir))
    {
        speed.Feed("a"); clock.Advance(2); speed.Feed("b"); speed.InterruptTiming();
        clock.Advance(10); speed.Feed("c"); clock.Advance(1); speed.Feed("d"); clock.Advance(60); speed.Tick(); speed.Tick();
        Assert(speed.Result!.ElapsedSeconds == 60 && speed.Result.Status == SpeedTestStatus.Completed, "interrupt sampling never pauses speed countdown");
    }
    var speedPositions = new ArticleHeatmapStore(heatmapDir).Read(speedCourse, HeatmapSource.Test).Positions;
    Assert(speedPositions.Single(x => x.Offset == 1).MeanSeconds == 2 && speedPositions.Single(x => x.Offset == 2).TimingSamples == 0,
        "speed test focus change excludes interval");
    Assert(speedPositions.Sum(x => x.Opportunities) == 4, "repeated completion persists speed heatmap once");
    var exhaustedCourse = Article("english/t_article/exhausted.txt", "ab");
    using (var exhausted = new SpeedTestSession(exhaustedCourse, 60, tests, clock = new ManualClock(), heatmapDir))
    {
        exhausted.Feed("a"); clock.Advance(1); exhausted.Feed("b");
    }
    Assert(new ArticleHeatmapStore(heatmapDir).Read(exhaustedCourse, HeatmapSource.Test).Positions.Single(x => x.Offset == 1).TimingSamples == 1,
        "last character sampled before early-exhaustion result persistence");
    var cancelledCourse = Article("english/t_article/cancelled.txt");
    using (var cancelled = new SpeedTestSession(cancelledCourse, 60, tests, clock = new ManualClock(), heatmapDir))
    {
        cancelled.Feed("a"); clock.Advance(1); cancelled.Feed("b"); cancelled.Cancel();
    }
    Assert(new ArticleHeatmapStore(heatmapDir).Read(cancelledCourse, HeatmapSource.Test).Positions.Count == 0,
        "cancelled tests excluded from article test history");

    var edited = Article(text: "uvwxyz");
    var editedSampler = new ArticleTimingSampler(edited, store, HeatmapSource.Practice, clock);
    editedSampler.ObserveBatch(Attempt(0)); editedSampler.Flush();
    var oldFingerprint = SpeedTestSession.SourceFingerprintFor(course);
    Assert(store.Read(course.Id, oldFingerprint, HeatmapSource.Practice).Text == "abcdef", "historical fingerprint retains original full article text");
    Assert(store.Read(edited, HeatmapSource.Practice).Text == "uvwxyz" && store.Read(edited, HeatmapSource.Practice).Positions.Count == 1,
        "edited article content has fully separate position data");
    Assert(store.ListArticles().Count(x => x.CourseId == course.Id) == 2, "article history selector exposes both source versions");

    var legacy = Article("english/t_article/legacy.txt");
    string progressFingerprint;
    using (var old = PracticeSession.Open(legacy, Path.Combine(root, "LegacyProgress")))
    { old.Feed("ab"); progressFingerprint = old.State.Fingerprint; }
    using (var migrated = PracticeSession.Open(legacy, Path.Combine(root, "LegacyProgress"), null, heatmapDir, clock))
    {
        Assert(migrated.State.Fingerprint == progressFingerprint && migrated.CompletedCharacters == 2, "v0.3 progress fingerprint and position unchanged");
        migrated.Feed("c"); migrated.Pause();
    }
    var legacyPositions = new ArticleHeatmapStore(heatmapDir).Read(legacy, HeatmapSource.Practice).Positions;
    Assert(legacyPositions.Count == 1 && legacyPositions[0].Offset == 2 && legacyPositions[0].TimingSamples == 0,
        "old progress never fabricated into positional timing history");

    var repeated = Article("english/t_article/repeated.txt");
    var repeatedSampler = new ArticleTimingSampler(repeated, store, HeatmapSource.Practice, clock = new ManualClock());
    repeatedSampler.ObserveBatch(Attempt(0));
    for (var i = 0; i < 40; i++) { clock.Advance(1); repeatedSampler.ObserveBatch(Attempt(1)); }
    repeatedSampler.Flush();
    var repeatedPosition = store.Read(repeated, HeatmapSource.Practice).Positions.Single(x => x.Offset == 1);
    Assert(repeatedPosition.TimingSamples == 40 && repeatedPosition.RecentSeconds.Count == 31, "position lifetime totals retained with bounded recent samples");
    Assert(repeatedPosition.MeanSeconds == 1 && repeatedPosition.MedianSeconds == 1, "mean and recent median are well-defined");
    var unsupported = Article(); unsupported.Category = "五笔";
    Assert(!ArticleHeatmapStore.IsSupported(unsupported) && !ArticleHeatmapStore.IsSupported(new Course { Category = "英文", Variant = "单词" }),
        "position storage restricted to supported original article lessons");

    var unicode = Article("chinese/t_article/unicode.txt", "A😀B");
    clock = new ManualClock();
    using (var unicodeSession = PracticeSession.Open(unicode, Path.Combine(root, "UnicodeProgress"), null, heatmapDir, clock))
    {
        unicodeSession.Feed("A"); clock.Advance(1); unicodeSession.Feed("😀"); clock.Advance(1); unicodeSession.Feed("B");
        Assert(unicodeSession.CompletedCharacters == 4, "position units remain UTF-16, matching existing core progress");
    }
    var unicodeSnapshot = new ArticleHeatmapStore(heatmapDir).Read(unicode, HeatmapSource.Practice);
    Assert(unicodeSnapshot.Text == "A😀B" && unicodeSnapshot.Positions.Select(p => p.Offset).SequenceEqual(new[] { 0, 1, 2, 3 }),
        "Unicode source text intact and offsets match prior engine semantics");
    Assert(unicodeSnapshot.Positions.Where(p => p.Offset is 1 or 2).All(p => p.MeanSeconds == 0.5 && p.EstimatedBatchSamples == 1),
        "surrogate-containing committed batch uses explicit apportioned samples");

    var corruptDir = Path.Combine(root, "CorruptHeatmaps");
    var corruptStore = new ArticleHeatmapStore(corruptDir);
    var corruptSampler = new ArticleTimingSampler(course, corruptStore, HeatmapSource.Practice, clock = new ManualClock());
    corruptSampler.ObserveBatch(Attempt(0)); corruptSampler.Flush();
    clock.Advance(0.5); corruptSampler.ObserveBatch(Attempt(1)); corruptSampler.Flush();
    var corruptPath = Directory.GetFiles(corruptDir, "*.json").Single();
    File.WriteAllText(corruptPath, "{truncated");
    Assert(corruptStore.Read(course, HeatmapSource.Practice).Positions.Count == 1 && corruptStore.RecoveryNotice is not null,
        "truncated heatmap recovers last valid atomic backup");
    File.WriteAllText(corruptPath, "{\"SchemaVersion\":4,\"Text\":null}");
    File.WriteAllText(corruptPath + ".bak", "{invalid");
    Assert(corruptStore.Read(course, HeatmapSource.Practice).Positions.Count == 0, "invalid heatmaps cannot fabricate history or crash rendering");

    if (args.Contains("--benchmark"))
    {
        var longArticle = Article("english/t_article/long.txt", new string('a', 10000));
        var longSampler = new ArticleTimingSampler(longArticle, store, HeatmapSource.Practice, clock = new ManualClock());
        longSampler.ObserveBatch(Enumerable.Range(0, 10000).Select(i => new ArticleInputAttempt(i, true)).ToArray());
        longSampler.Flush();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) { clock.Advance(0.05); longSampler.ObserveBatch(Attempt(5000 + i)); }
        longSampler.Flush(); watch.Stop();
        Console.WriteLine($"BENCHMARK: 10,000 existing article positions, 100 timing observations: {watch.Elapsed.TotalMilliseconds / 100:F2} ms/observation incl batch flush.");
    }
    Console.WriteLine($"PASS: {assertions} v0.4 article heatmap assertions.");
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
finally { Directory.Delete(root, recursive: true); }
});

internal sealed class ManualClock : ITypingClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}
