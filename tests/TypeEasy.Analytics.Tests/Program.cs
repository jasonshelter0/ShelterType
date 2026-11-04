using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
var root = Path.Combine(Path.GetTempPath(), "TypeEasyAnalyticsTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int assertions = 0;
void Assert(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}
Course Make(string id = "test/english", string text = "abcdefghijklmnopqrstuvxyz") => new()
{
    Id = id, Title = "Test", Category = "英文", Variant = "文章", Group = "文章/测试",
    Items = new() { new() { Prompt = text, Expected = text, Hint = "" } }
};
try
{
    var store = new AnalyticsStore(Path.Combine(root, "speed"), TimeZoneInfo.Utc);
    var clock = new ManualClock();
    using (var notStarted = new SpeedTestSession(Make(), 60, store, clock))
    {
        notStarted.Cancel();
        Assert(notStarted.IsFinished && notStarted.Result is null && store.ReadSpeedTests(true).Count == 0, "cancel before first input finishes session without storing a record");
        notStarted.Feed("a");
        Assert(!notStarted.HasStarted && notStarted.Position == 0, "cancelled preparation cannot accept later text");
    }
    var test = new SpeedTestSession(Make(), 60, store, clock);
    clock.Advance(120); test.Tick();
    Assert(!test.HasStarted && !test.IsFinished && test.Remaining.TotalSeconds == 60, "timer must not start while waiting for first input");
    test.Backspace();
    Assert(!test.HasStarted && test.Backspaces == 0, "backspace before first input cannot start test");
    var start = clock.UtcNow;
    test.Feed("ab");
    Assert(test.HasStarted && test.Position == 2 && test.Elapsed.TotalSeconds == 0, "first input starts clock");
    clock.Advance(10); test.Backspace(); test.Feed("b");
    Assert(test.Position == 2 && test.CorrectInputs == 3, "retyping increases attempts but never net output");
    test.Feed("x"); test.Backspace();
    Assert(test.Position == 2 && test.Errors == 1, "wrong input and dismissal don't remove correct output");
    clock.Advance(65); test.Tick();
    var result = test.Result!;
    Assert(result.Status == SpeedTestStatus.Completed && result.ElapsedSeconds == 60, "late Tick clamps elapsed to exact requested duration");
    Assert(result.FinishedAt == start.AddSeconds(60), "expiry timestamp not delayed UI tick");
    Assert(result.NetCharacters == 2 && result.CharactersPerMinute == 2 && result.EnglishEquivalentWpm == 0.4, "speed uses net position rather than cumulative attempts");
    Assert(result.Accuracy == 0.75 && result.Backspaces == 2, "attempt-based accuracy and correction count");
    test.Feed("c"); test.Tick(); test.Dispose(); test.Dispose();
    Assert(test.Position == 2 && store.ReadSpeedTests().Count == 1, "completed run ignores further input and persists exactly once");
    Assert(!store.SaveSpeedTest(result) && store.ReadDays().Single().CorrectInputs == 3, "result ID also deduplicated inside store");
    Assert(store.ReadDays().Single().ActiveSeconds == 60, "no active-time double count from repeated completion");
    Assert(store.ReadConfusions().Single().Expected == "c" && store.ReadConfusions().Single().Actual == "x", "speed test attempts included atomically in context analysis");
    Assert(store.ReadContextRisks().Sum(x => x.Opportunities) == 4, "speed context batches persisted exactly once");

    var shortClock = new ManualClock();
    using (var shortTest = new SpeedTestSession(Make("short", "ab"), 180, store, shortClock))
    {
        shortTest.Feed("a"); shortClock.Advance(10); shortTest.Feed("b");
        Assert(shortTest.Result is { Status: SpeedTestStatus.Exhausted, ElapsedSeconds: 10, NetCharacters: 2 }, "early exhaustion is incomplete with actual elapsed");
        Assert(shortTest.Result!.CharactersPerMinute == 12, "early output measured against actual time");
    }
    var cancelClock = new ManualClock();
    using (var cancel = new SpeedTestSession(Make("cancel"), 300, store, cancelClock))
    {
        cancel.Feed("a"); cancelClock.Advance(15); cancel.Cancel();
        Assert(cancel.Result!.Status == SpeedTestStatus.Cancelled, "explicit cancellation recorded separately");
    }
    Assert(store.ReadSpeedTests().Count == 2 && store.ReadSpeedTests(true).Count == 3, "cancelled runs excluded from default history");
    Assert(store.ReadDays().Sum(x => x.ActiveSeconds) == 70, "cancelled runs excluded from daily trend totals");
    Assert(store.ReadContextRisks().Sum(x => x.Opportunities) == 6, "cancelled input excluded from context sample totals");
    using (var tenMinutes = new SpeedTestSession(Make("ten-minutes"), 600, store, clock = new ManualClock()))
    {
        tenMinutes.Feed("a"); clock.Advance(601); tenMinutes.Tick();
        Assert(tenMinutes.Result is { Status: SpeedTestStatus.Completed, ElapsedSeconds: 600, DurationSeconds: 600 }, "original ten-minute option supported");
    }
    using (var empty = new SpeedTestSession(Make("empty", ""), 60, store, new ManualClock()))
    {
        empty.Feed("a");
        Assert(empty.Result is { Status: SpeedTestStatus.Exhausted, NetCharacters: 0, CharactersPerMinute: 0 }, "empty content cannot fabricate infinite speed");
    }
    using (var boundary = new SpeedTestSession(Make("boundary"), 60, store, clock = new ManualClock()))
    {
        boundary.Feed("a"); clock.Advance(60); boundary.Feed("b");
        Assert(boundary.Position == 1 && boundary.Result!.Status == SpeedTestStatus.Completed, "input exactly at deadline excluded");
    }
    var originalMaterial = Make("same-id", "abcdefgh");
    var editedMaterial = Make("same-id", "xyzabcde");
    Assert(SpeedTestSession.SourceFingerprintFor(originalMaterial) != SpeedTestSession.SourceFingerprintFor(editedMaterial), "same course ID with edited source has distinct material fingerprint");
    using (var fingerprinted = new SpeedTestSession(originalMaterial, 60, store, clock = new ManualClock()))
    {
        fingerprinted.Feed("a"); clock.Advance(60); fingerprinted.Tick();
        Assert(fingerprinted.Result!.SourceFingerprint == SpeedTestSession.SourceFingerprintFor(originalMaterial), "saved result fingerprint matches helper used by comparison UI");
    }

    var contexts = new AnalyticsStore(Path.Combine(root, "contexts"), TimeZoneInfo.Utc);
    var observed = new List<TypingAttempt>();
    for (var i = 0; i < 20; i++) observed.Add(new("x", i < 8 ? "z" : "x", "ab", "cd", "ab"));
    for (var i = 0; i < 80; i++) observed.Add(new("x", "x", "qq", "rr", "qq"));
    contexts.RecordPractice("英文", 100, 92, 8, 0, observed, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    var risk = contexts.ReadContextRisks().Single(x => x.Before == "ab");
    Assert(risk.Opportunities == 20 && risk.Errors == 8 && risk.ActualCounts.Values.Sum() == 20, "context denominators include correct and incorrect opportunities");
    Assert(risk.BaselineOpportunities == 100 && risk.BaselineErrors == 8, "context baseline is same expected key across contexts");
    Assert(Math.Abs(risk.SmoothedErrorRate - 9.0 / 22) < 0.000001 && Math.Abs(risk.BaselineErrorRate - 9.0 / 102) < 0.000001, "Laplace-smoothed rates");
    Assert(risk.IsEligible && risk.IsRisk && risk.TypedBeforeCounts["ab"] == 20, "enough evidence can flag association while preserving typed context");
    contexts.RecordPractice("五笔86版", 0, 0, 100, 0, Enumerable.Repeat(new TypingAttempt("x", "z", "ab", "cd"), 100).ToArray());
    var isolated = contexts.ReadContextRisks().Single(x => x.Mode == "英文" && x.Before == "ab");
    Assert(isolated.BaselineOpportunities == 100 && isolated.BaselineErrors == 8, "other input mode never contaminates baseline");
    contexts.RecordPractice("英文", 0, 16, 3, 0, Enumerable.Range(0, 19).Select(i => new TypingAttempt("y", i < 3 ? "z" : "y", "a", "b")).ToArray());
    contexts.RecordPractice("英文", 0, 38, 2, 0, Enumerable.Range(0, 40).Select(i => new TypingAttempt("z", i < 2 ? "y" : "z", "a", "b")).ToArray());
    Assert(!contexts.ReadContextRisks().Single(x => x.Expected == "y").IsEligible, "19 opportunities insufficient despite three errors");
    Assert(!contexts.ReadContextRisks().Single(x => x.Expected == "z").IsEligible, "two errors insufficient despite many opportunities");
    Assert(contexts.ReadConfusions().Single(x => x.Mode == "英文" && x.Expected == "x").Count == 8, "expected-versus-actual confusion totals");

    var practiceAnalytics = Path.Combine(root, "practice-analytics");
    var practiceStore = new AnalyticsStore(practiceAnalytics);
    var course = Make("boundaries", "ab");
    course.Items.Add(new() { Prompt = "cd", Expected = "cd" });
    using (var practice = PracticeSession.Open(course, Path.Combine(root, "progress"), practiceAnalytics))
    {
        practice.Feed("abxc");
        practice.Pause();
        var beforePause = practiceStore.ReadDays().Sum(x => x.ActiveSeconds);
        Thread.Sleep(50); practice.Pause(); practice.Save();
        Assert(Math.Abs(practiceStore.ReadDays().Sum(x => x.ActiveSeconds) - beforePause) < 0.001, "repeated pause/save cannot double count active time");
        var cross = practiceStore.ReadContextRisks().Single(x => x.Expected == "c");
        Assert(cross.Before == "ab" && cross.After == "d" && cross.Opportunities == 2 && cross.Errors == 1, "context spans item boundaries and counts retries");
        Assert(cross.TypedBeforeCounts.ContainsKey("ab") && cross.TypedBeforeCounts.ContainsKey("bx"), "typed history includes actual mistaken key");
        Assert(practiceStore.ReadContextRisks().Single(x => x.Expected == "b").After == "cd", "following context crosses item boundary");
        practice.Backspace();
        Assert(practiceStore.ReadDays().Sum(x => x.Backspaces) == 1, "correction logged once");
        practice.Reset();
        Assert(practiceStore.ReadDays().Sum(x => x.CorrectInputs) == 3, "reset does not erase historical analytics or replay prior counts");
    }
    using (var old = PracticeSession.Open(Make("legacy"), Path.Combine(root, "legacy-progress"))) old.Feed("a");
    using (var upgraded = PracticeSession.Open(Make("legacy"), Path.Combine(root, "legacy-progress"), Path.Combine(root, "new-analytics"))) upgraded.Feed("b");
    Assert(new AnalyticsStore(Path.Combine(root, "new-analytics")).ReadDays().Sum(x => x.CorrectInputs) == 1, "legacy checkpoint totals are never replayed into new observations");
    var batchedDir = Path.Combine(root, "batch-analytics");
    using (var batched = PracticeSession.Open(Make("batch"), Path.Combine(root, "batch-progress"), batchedDir))
    {
        batched.Feed("abc");
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(batched.SavePath)))
            Assert(json.RootElement.GetProperty("CharacterIndex").GetInt32() == 3, "analytics batching never delays per-character practice checkpoint");
        batched.Pause(); batched.Pause();
        Assert(new AnalyticsStore(batchedDir).ReadDays().Sum(x => x.CorrectInputs) == 3, "pause flushes partial batch once");
        batched.Feed("de");
    }
    Assert(new AnalyticsStore(batchedDir).ReadDays().Sum(x => x.CorrectInputs) == 5, "dispose flushes remaining partial batch without replay");

    var midnight = new AnalyticsStore(Path.Combine(root, "midnight"), TimeZoneInfo.Utc);
    midnight.RecordPractice("英文", 120, 1, 0, 0, Array.Empty<TypingAttempt>(), new DateTimeOffset(2026, 11, 4, 0, 1, 0, TimeSpan.Zero));
    Assert(midnight.ReadDays().Count == 2 && midnight.ReadDays().All(x => x.ActiveSeconds == 60), "active interval split across midnight without duplicated seconds");
    var corruptDir = Path.Combine(root, "corrupt");
    var corrupt = new AnalyticsStore(corruptDir);
    corrupt.RecordPractice("英文", 1, 1, 0, 0, Array.Empty<TypingAttempt>());
    corrupt.RecordPractice("英文", 1, 1, 0, 0, Array.Empty<TypingAttempt>());
    File.WriteAllText(Path.Combine(corruptDir, "analytics-v3.json"), "{truncated");
    Assert(corrupt.ReadDays().Sum(x => x.CorrectInputs) == 1 && corrupt.RecoveryNotice is not null, "analytics backup recovery");
    Assert(TypingMode.ForCourse(new Course { Category = "中文", InputMode = "pinyin" }) == "拼音编码"
        && TypingMode.ForCourse(new Course { Category = "中文" }) == "中文输入", "pinyin not compared to committed Chinese text");
    if (args.Contains("--benchmark"))
    {
        var benchPath = Path.Combine(root, "benchmark");
        var bench = new AnalyticsStore(benchPath);
        bench.RecordPractice("英文", 0, 4000, 0, 0, Enumerable.Range(0, 4000).Select(i =>
            new TypingAttempt("x", "x", ((char)(0x4e00 + i)).ToString(), "ab")).ToArray());
        using var practice = PracticeSession.Open(Make("bench", new string('x', 200)), Path.Combine(root, "bench-progress"), benchPath);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        practice.Feed(new string('x', 100));
        watch.Stop();
        Console.WriteLine($"BENCHMARK: 4000 existing contexts, 100 persisted practice inputs: {watch.Elapsed.TotalMilliseconds / 100:F2} ms/input.");
    }
    Console.WriteLine($"PASS: {assertions} v0.3 analytics/speed assertions.");
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
