using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
    var root = Path.Combine(Path.GetTempPath(), "ShelterType-progress-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var checks = 0;
    void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    Course Course(string id = "progress:sample", params string[] parts) => new()
    {
        Id = id, Category = "英文", Variant = "键位", Title = "Progress test",
        Items = (parts.Length == 0 ? new[] { "ab", "cdef" } : parts).Select(text => new ExerciseItem { Prompt = text, Expected = text }).ToList()
    };
    string PathFor(Course course, string directory) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(course.Id))).ToLowerInvariant() + ".json");
    void Equal(CourseProgressSummary value, CourseProgressStatus status, int completed, int total, string name)
    {
        Check(value.Status == status, name + ": status");
        Check(value.CompletedCharacters == completed && value.TotalCharacters == total, name + ": positions");
        Check(value.IsAvailable == (status != CourseProgressStatus.Unknown), name + ": availability");
        var ratio = status == CourseProgressStatus.Unknown ? 0 : total == 0 ? 1 : (double)completed / total;
        Check(value.Fraction == ratio, name + ": ratio");
    }
    string Snapshot(string directory)
        => string.Join("\n", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order()
            .Select(file => Path.GetRelativePath(directory,file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) + ":" + File.GetLastWriteTimeUtc(file).Ticks));
    void ReadOnlyCheck(Course course, string directory, Action<CourseProgressSummary> inspect)
    {
        var before = Snapshot(directory);
        inspect(PracticeSession.ReadProgress(course, directory));
        Check(before == Snapshot(directory), "Reading sidebar progress must not change files or write times");
    }
    try
    {
        var course = Course();
        var absent = Path.Combine(root,"not-created");
        Equal(PracticeSession.ReadProgress(course,absent),CourseProgressStatus.NotStarted,0,6,"unopened");
        Check(!Directory.Exists(absent),"ReadProgress creates no directory for new courses");
        var directory = Path.Combine(root,"basic");
        using(var session = PracticeSession.Open(course,directory))
        {
            Equal(session.ProgressSummary,CourseProgressStatus.NotStarted,0,6,"open without input");
            session.Pause();
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"saved without input"));
            session.Feed("x");
            Equal(session.ProgressSummary,CourseProgressStatus.InProgress,0,6,"wrong key is a practice attempt");
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.InProgress,0,6,"wrong key persisted"));
            session.Feed("abc");
            Equal(session.ProgressSummary,CourseProgressStatus.InProgress,3,6,"partial next item");
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.InProgress,3,6,"resumable partial item"));
            session.Backspace(); session.Feed("c");
            Check(session.State.CorrectInputs > session.CompletedCharacters,"Test covers repeated correct keystrokes");
            Equal(session.ProgressSummary,CourseProgressStatus.InProgress,3,6,"backspace does not inflate progress");
            session.Feed("def");
            Equal(session.ProgressSummary,CourseProgressStatus.Completed,6,6,"complete live");
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.Completed,6,6,"complete saved"));
            session.Backspace();
            Equal(session.ProgressSummary,CourseProgressStatus.InProgress,5,6,"backspace from complete");
            session.Reset();
            Equal(session.ProgressSummary,CourseProgressStatus.NotStarted,0,6,"reset live");
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"reset beats completed backup"));
            session.Feed("a"); session.Backspace();
            Equal(session.ProgressSummary,CourseProgressStatus.InProgress,0,6,"rewound to zero remains started");
            ReadOnlyCheck(course,directory,v=>Equal(v,CourseProgressStatus.InProgress,0,6,"rewound state saved"));
        }

        var recovery = Path.Combine(root,"recovery");
        using(var session=PracticeSession.Open(course,recovery)) {session.Feed("abc");}
        var path=PathFor(course,recovery);
        var valid=File.ReadAllText(path);
        File.WriteAllText(path+".bak",valid); File.WriteAllText(path,"{bad json");
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.InProgress,3,6,"corrupt primary uses backup"));
        using(var actual=PracticeSession.Open(course,recovery))
            Check(actual.ProgressSummary==PracticeSession.ReadProgress(course,recovery),"ReadProgress agrees with Open backup recovery");
        var invalid=JsonSerializer.Deserialize<PracticeState>(valid)!; invalid.ItemIndex=-5;
        File.WriteAllText(path,JsonSerializer.Serialize(invalid)); File.WriteAllText(path+".bak",valid);
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.InProgress,3,6,"invalid position uses backup"));
        invalid=JsonSerializer.Deserialize<PracticeState>(valid)!; invalid.Errors=100;
        File.WriteAllText(path,JsonSerializer.Serialize(invalid));
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.InProgress,3,6,"invalid counters use backup"));
        invalid=JsonSerializer.Deserialize<PracticeState>(valid)!; invalid.Fingerprint="changed";
        File.WriteAllText(path,JsonSerializer.Serialize(invalid));
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"fingerprint mismatch does not restore stale progress"));
        File.WriteAllText(path,valid);
        var changed=Course();changed.Items[1].Hint="new lesson content";
        ReadOnlyCheck(changed,recovery,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"course fingerprint changed"));
        File.WriteAllText(path,"corrupt"); File.WriteAllText(path+".bak","corrupt too");
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"unrecoverable record follows Open reset policy"));
        File.Delete(path); File.WriteAllText(path+".bak",valid);
        ReadOnlyCheck(course,recovery,v=>Equal(v,CourseProgressStatus.NotStarted,0,6,"missing primary follows Open policy"));
        File.WriteAllText(path,valid);
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Equal(PracticeSession.ReadProgress(course,recovery),CourseProgressStatus.Unknown,0,6,"unreadable record is not falsely unfinished");
        File.Delete(path);Directory.CreateDirectory(path);
        Equal(PracticeSession.ReadProgress(course,recovery),CourseProgressStatus.Unknown,0,6,"directory at checkpoint path");

        var emptyItems=Course("progress:empty-items","","ab","","cd","");
        var emptyDirectory=Path.Combine(root,"empty-items");
        using(var actual=PracticeSession.Open(emptyItems,emptyDirectory))
        {
            Equal(actual.ProgressSummary,CourseProgressStatus.NotStarted,0,4,"leading empty items are not activity");
            actual.Feed("ab");
            Equal(actual.ProgressSummary,CourseProgressStatus.InProgress,2,4,"empty middle item skipped");
            ReadOnlyCheck(emptyItems,emptyDirectory,v=>Check(v==actual.ProgressSummary,"empty-item saved/live parity"));
            actual.Feed("cd");
            Equal(actual.ProgressSummary,CourseProgressStatus.Completed,4,4,"empty trailing items complete");
        }
        var noText=Course("progress:empty","");
        Equal(PracticeSession.ReadProgress(noText,absent),CourseProgressStatus.Completed,0,0,"zero-length course follows Open");
        var noItems=Course("progress:no-items");noItems.Items.Clear();
        Equal(PracticeSession.ReadProgress(noItems,absent),CourseProgressStatus.Completed,0,0,"no-item course follows Open");
        Check(!Directory.Exists(absent),"Reading empty courses still creates no storage");
        var other=Course("progress:independent");
        Equal(PracticeSession.ReadProgress(other,directory),CourseProgressStatus.NotStarted,0,6,"course IDs are isolated");
        Console.WriteLine($"PASS: {checks} progress-summary checks (read-only, recovery, character position, live state and reset).");
        return 0;
    }
    finally
    {
        // This directory was created with a fresh GUID exclusively for this test.
        Directory.Delete(root,true);
    }
});
