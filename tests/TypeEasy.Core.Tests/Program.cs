using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
var testRoot = Path.Combine(Path.GetTempPath(), "TypeEasyTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var assertions = 0;
void Assert(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}
Course MakeCourse(string id = "wubi:test", string first = "abcd") => new()
{
    Id = id, Title = "测试课程", Category = "五笔", Variant = "86",
    Items = new()
    {
        new() { Prompt = "测试", Expected = first, Hint = "编码提示" },
        new() { Prompt = "下一项", Expected = "ef", Hint = "" }
    }
};

try
{
    var progressDir = Path.Combine(testRoot, "resume");
    var course = MakeCourse();
    using (var first = PracticeSession.Open(course, progressDir))
    {
        Assert(first.State.ItemIndex == 0 && first.State.CharacterIndex == 0, "new session starts at zero");
        Assert(!first.IsRunning, "opening session does not run timer");
        first.Feed("abx");
        using (var checkpointReader = PracticeSession.Open(course, progressDir))
        {
            Assert(checkpointReader.State.CharacterIndex == 2 && checkpointReader.State.Errors == 1,
                "each input is persisted before a normal exit or pause");
        }
        Assert(first.State.CharacterIndex == 2, "wrong key never advances cursor");
        Assert(first.State.CorrectInputs == 2 && first.State.Errors == 1, "input accounting");
        Assert(Math.Abs(first.Accuracy - 2.0 / 3) < 0.0001, "accuracy denominator includes mistakes");
        Assert(first.State.Confusions.Single().Expected == "c" && first.State.Confusions.Single().Actual == "x", "confusion expected and actual direction");
        first.Backspace();
        Assert(first.State.CharacterIndex == 2, "backspace after rejection dismisses rejected character");
        first.Backspace();
        Assert(first.State.CharacterIndex == 1 && first.State.Backspaces == 2, "next backspace rewinds accepted character");
        first.Feed("b");
        var review = first.BuildReviewCourse();
        Assert(review.Items.Count > 0 && review.Items[0].Expected == "abcd", "review preserves full item context");
        Assert(review.Variant == "86", "review keeps five-stroke variant");
    }
    using (var restored = PracticeSession.Open(course, progressDir))
    {
        Assert(restored.State.CharacterIndex == 2 && restored.ExpectedCharacter == 'c', "resume exact character within item");
        Assert(restored.State.Errors == 1 && restored.State.Backspaces == 2, "resume statistics");
        restored.Feed("cd");
        Assert(restored.State.ItemIndex == 1 && restored.State.CharacterIndex == 0, "advance to next item");
        restored.Feed("efextra");
        Assert(restored.IsComplete && restored.Progress == 1, "end of course");
        Assert(restored.State.TotalInputs == 8, "characters after completion ignored");
        Assert(!restored.IsRunning && restored.State.CompletedAt.HasValue, "completion stops timer");
    }
    using (var completed = PracticeSession.Open(course, progressDir))
    {
        Assert(completed.IsComplete && completed.Progress == 1, "completed state survives restart");
    }
    using (var other = PracticeSession.Open(MakeCourse("wubi:other"), progressDir))
    {
        Assert(other.State.TotalInputs == 0 && other.State.CharacterIndex == 0, "courses have isolated checkpoints");
        Assert(other.BuildReviewCourse().Items.Count == 0, "no fabricated review when no mistakes");
    }
    using (var changed = PracticeSession.Open(MakeCourse(first: "different"), progressDir))
    {
        Assert(changed.State.TotalInputs == 0 && changed.ResumeNotice is not null, "content change prevents stale resume");
    }

    var corruptionDir = Path.Combine(testRoot, "corruption");
    string checkpoint;
    using (var source = PracticeSession.Open(course, corruptionDir))
    {
        source.Feed("ab");
        checkpoint = source.SavePath;
    }
    File.WriteAllText(checkpoint, "{unfinished");
    using (var recovered = PracticeSession.Open(course, corruptionDir))
    {
        Assert(recovered.State.CharacterIndex >= 1 && recovered.State.CharacterIndex <= 2, "malformed JSON recovers recent backup");
        Assert(recovered.ResumeNotice is not null, "recovery is disclosed");
    }
    File.WriteAllText(checkpoint, "garbage");
    File.WriteAllText(checkpoint + ".bak", "garbage");
    using (var safe = PracticeSession.Open(course, corruptionDir))
    {
        Assert(safe.State.TotalInputs == 0 && safe.ResumeNotice is not null, "both corrupt files start clean with notice");
        safe.Feed("a");
        safe.Reset();
        Assert(safe.State.TotalInputs == 0 && safe.State.CharacterIndex == 0 && safe.State.Confusions.Count == 0, "explicit reset clears course and stats");
    }

    using (var timing = PracticeSession.Open(course, Path.Combine(testRoot, "timing")))
    {
        timing.Resume();
        Thread.Sleep(80);
        timing.Pause();
        var paused = timing.Elapsed.TotalSeconds;
        Thread.Sleep(100);
        Assert(Math.Abs(timing.Elapsed.TotalSeconds - paused) < 0.005, "paused wall time is excluded");
        timing.Resume();
        Thread.Sleep(80);
        timing.Pause();
        Assert(timing.Elapsed.TotalSeconds >= paused + 0.05, "resuming accumulates active time");
    }

    var chinese = new Course
    {
        Id = "chinese:ime", Title = "中文测试", Category = "中文", Variant = "",
        Items = new() { new() { Prompt = "你好世界", Expected = "你好世界", Hint = "" } }
    };
    using (var ime = PracticeSession.Open(chinese, Path.Combine(testRoot, "ime")))
    {
        ime.Feed("你好");
        Assert(ime.State.CharacterIndex == 2, "committed IME text advances multiple characters");
        ime.Feed("世界");
        Assert(ime.IsComplete && ime.State.Errors == 0, "IME batch reaches completion");
    }

    var empty = MakeCourse("empty");
    empty.Items.Clear();
    using (var session = PracticeSession.Open(empty, Path.Combine(testRoot, "empty")))
    {
        Assert(session.IsComplete && session.Progress == 1 && session.CurrentItem is null, "empty course safe");
        session.Feed("abc");
        session.Backspace();
        Assert(session.State.TotalInputs == 0, "empty course does not count inputs");
    }
    if (args.Contains("--benchmark"))
    {
        var longCourse = MakeCourse("long-course");
        longCourse.Items = Enumerable.Range(0, 6000).Select(i => new ExerciseItem
        {
            Prompt = "五笔词组 " + i, Expected = "abcd ", Hint = "abcd"
        }).ToList();
        using var large = PracticeSession.Open(longCourse, Path.Combine(testRoot, "large"));
        // Model a late-course checkpoint with statistics for thousands of practiced items.
        large.State.Items = Enumerable.Range(0, 6000).Select(i => new ItemStat { ItemIndex = i, Attempts = 5, Errors = 1 }).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        large.Feed(string.Concat(Enumerable.Repeat("abcd ", 40)));
        watch.Stop();
        Console.WriteLine($"BENCHMARK: 6000-item course, 200 persisted inputs, {watch.Elapsed.TotalMilliseconds / 200:F2} ms/input, checkpoint {new FileInfo(large.SavePath).Length:N0} bytes.");
    }
    Console.WriteLine($"PASS: {assertions} assertions (resume, isolation, fingerprint, recovery, pause, reset, IME, review).");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    // Only this test's unique temporary directory is removed.
    Directory.Delete(testRoot, recursive: true);
}
});

