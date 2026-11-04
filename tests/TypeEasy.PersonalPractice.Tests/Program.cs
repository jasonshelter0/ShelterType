using System.Diagnostics;
using System.Text.Json;
using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
    var root = Path.Combine(Path.GetTempPath(), "ShelterType-personal-tests-" + Guid.NewGuid().ToString("N"));
    var checks = 0;
    void Check(bool condition, string name) { checks++; if (!condition) throw new InvalidOperationException(name); }
    void Record(AnalyticsStore store, string mode, string expected, string actual, int count, string before = "", string after = "")
        => store.RecordPractice(mode, 0, expected == actual ? count : 0, expected == actual ? 0 : count, 0,
            Enumerable.Range(0, count).Select(_ => new TypingAttempt(expected, actual, before, after)).ToArray());
    Course Encoded(string id, string variant, string expected, string prompt, bool pinyin = false) => new()
    {
        Id = id, Category = pinyin ? "中文" : "五笔", Variant = variant, InputMode = pinyin ? "pinyin" : "",
        TeachingImage = "Teaching/" + variant + ".png",
        Items = [new() { Prompt = prompt, Expected = expected, Hint = "original hint", ImagePath = "original-image.png" },
            new() { Prompt = "next", Expected = "abcd ", Hint = "next hint" }]
    };
    try
    {
        var source = new[] { Encoded("wubi86", "86版", "ggll ", "原字"), Encoded("wubi98", "98版", "gx ", "另一字"),
            Encoded("pinyin", "拼音", "ni ", "你", true) };
        var sourceBefore = JsonSerializer.Serialize(source);
        var emptyStore = new AnalyticsStore(Path.Combine(root, "empty-analytics"));
        Check(PersonalPracticeGenerator.Generate(emptyStore, source, "英文", PersonalPracticeKind.Confusions) is null, "No errors invent no drill");
        Check(!Directory.Exists(root), "Reading empty analytics and generating candidates does not create directories");
        var repository = new PersonalPracticeRepository(Path.Combine(root, "plans"));
        Check(repository.Load().Count == 0 && !Directory.Exists(root), "Loading missing personal plans does not create files");

        var store = new AnalyticsStore(Path.Combine(root, "analytics"));
        Record(store, "英文", "g", "f", 5, "ab", "cd");
        Record(store, "英文", "g", "g", 15, "ab", "cd");
        Record(store, "数字键盘", "1", "2", 4, "00", "33");
        Record(store, "五笔86版", "g", "h", 6, "g", "ll");
        Record(store, "五笔98版", "g", "h", 4, "", "x ");
        Record(store, "拼音编码", "i", "u", 3, "n", " a");
        Record(store, "中文输入", "中", "钟", 3, "学习", "语言");
        var modes = new[] { "英文", "数字键盘", "五笔86版", "五笔98版", "拼音编码", "中文输入" };
        var allPlans = new List<PersonalPracticePlan>();
        foreach (var mode in modes)
        foreach (var kind in Enum.GetValues<PersonalPracticeKind>())
        {
            var plan = PersonalPracticeGenerator.Generate(store, source, mode, kind);
            Check(plan is not null, mode + " " + kind + " has observed training material");
            Check(plan!.Mode == mode && TypingMode.ForCourse(plan.Course) == mode, mode + " preserves analytics and input mode");
            Check(plan.Course.Items.Count is >= 12 and <= 24 && plan.Course.Items.Sum(i => i.Expected.Length) <= 400, "Short bounded lesson");
            Check(plan.Course.Group == "专属练习/" + mode && plan.Course.Id.StartsWith("personal:"), "Personal grouping and identity");
            Check(plan.Course.Title == (kind == PersonalPracticeKind.Confusions ? "易混输入" : "语境巩固"), "Stable localizable title");
            var again = PersonalPracticeGenerator.Generate(store, source.Reverse(), mode, kind)!;
            Check(JsonSerializer.Serialize(plan) == JsonSerializer.Serialize(again), "Same data and reordered source enumeration are deterministic");
            allPlans.Add(plan);
        }
        Check(JsonSerializer.Serialize(source) == sourceBefore, "Generating all plans never mutates source courses");
        foreach (var mode in modes.Where(m => m.StartsWith("五笔") || m == "拼音编码"))
        {
            var original = source.Single(c => TypingMode.ForCourse(c) == mode);
            foreach (var plan in allPlans.Where(p => p.Mode == mode))
            {
                Check(plan.Course.Items.All(i => original.Items.Any(o => JsonSerializer.Serialize(o) == JsonSerializer.Serialize(i))), "Encoded practice uses complete original item metadata");
                Check(plan.Course.Items.All(i => !original.Items.Any(o => ReferenceEquals(i, o))), "Encoded practice deep clones items");
                Check(plan.Course.TeachingImage == original.TeachingImage, "Encoded teaching image preserved");
                if (plan.Kind == PersonalPracticeKind.Context)
                    Check(plan.Targets.All(t => string.Concat(plan.Course.Items.Select(i => i.Expected)).Contains(t.Before + t.Expected + t.After, StringComparison.Ordinal)),
                        "Complete source blocks actually preserve every advertised context across item boundaries");
            }
        }
        var englishContext = allPlans.Single(p => p.Mode == "英文" && p.Kind == PersonalPracticeKind.Context);
        Check(englishContext.Course.Items.All(i => i.Expected == "abgcd"), "Context drills practice the correct original text, not typed mistakes");
        var chineseContext = allPlans.Single(p => p.Mode == "中文输入" && p.Kind == PersonalPracticeKind.Context);
        Check(chineseContext.Course.Items.All(i => i.Expected == "学习中语言"), "IME context remains source text without inferred codes");
        Check(PersonalPracticeGenerator.Generate(store, [], "五笔86版", PersonalPracticeKind.Context) is null, "Absent code source cannot fabricate complete code items");
        Check(PersonalPracticeGenerator.Generate(store, source, "五笔86", PersonalPracticeKind.Context) is null, "Modes are exact, not inferred or merged");
        var boundaryStore = new AnalyticsStore(Path.Combine(root, "boundary-analytics"));
        Record(boundaryStore, "拼音编码", "a", "z", 3, "i ", "bc");
        var boundaryPlan = PersonalPracticeGenerator.Generate(boundaryStore, source, "拼音编码", PersonalPracticeKind.Context)!;
        Check(string.Concat(boundaryPlan.Course.Items.Select(i => i.Expected)).Contains("i abc", StringComparison.Ordinal), "Context before the target can require the preceding complete source item");
        Check(boundaryPlan.Course.Items.Select(i => i.Expected).Chunk(2).All(block => block.SequenceEqual(new[] { "ni ", "abcd " })), "Cross-item context blocks retain original order on every repetition");
        var excluded = new[] { Encoded("personal:fake", "86版", "ggll ", "invented"), Encoded("review:fake", "86版", "ggll ", "invented") };
        Check(PersonalPracticeGenerator.Generate(store, excluded, "五笔86版", PersonalPracticeKind.Confusions) is null, "Generated and old review courses cannot recursively become sources");
        var otherStore = new AnalyticsStore(Path.Combine(root, "other-analytics"));
        Record(otherStore, "英文", "a", "（其他）", 20);
        Record(otherStore, "英文", "a", "a", 20);
        Check(Enum.GetValues<PersonalPracticeKind>().All(kind => PersonalPracticeGenerator.Generate(otherStore, source, "英文", kind) is null), "Other bucket and correct attempts never invent a confusion");
        var tabStore = new AnalyticsStore(Path.Combine(root, "tab-analytics"));
        Record(tabStore, "英文", "\t", "a", 2);
        Record(tabStore, "英文", "a", "b", 2, "\t", "c");
        Check(PersonalPracticeGenerator.Generate(tabStore, source, "英文", PersonalPracticeKind.Context) is null, "Tab targets and source contexts are skipped because the UI uses Tab for focus");
        var rankStore = new AnalyticsStore(Path.Combine(root, "rank-analytics"));
        Record(rankStore, "英文", "a", "z", 40); Record(rankStore, "英文", "a", "a", 960);
        Record(rankStore, "英文", "b", "z", 10);
        var ranked = PersonalPracticeGenerator.Generate(rankStore, [], "英文", PersonalPracticeKind.Confusions)!;
        Check(ranked.Targets[0].Expected == "b", "Priority considers error rate as well as raw error count");

        var candidate = allPlans.Single(p => p.Mode == "英文" && p.Kind == PersonalPracticeKind.Confusions);
        var candidateBefore = JsonSerializer.Serialize(candidate);
        var progress = Path.Combine(root, "progress");
        var saved = repository.Save(candidate, progress);
        Check(saved.Course.Id == candidate.Course.Id && !ReferenceEquals(saved.Course, candidate.Course), "Save returns a deep snapshot without changing identity");
        Check(repository.Load().Single().Course.Id == saved.Course.Id, "Load and Save agree on current slot");
        Check(repository.Save(candidate, progress, true).Course.Id == saved.Course.Id, "Not-started current snapshot is protected even when requesting a new round");
        using (var session = PracticeSession.Open(saved.Course, progress)) session.Feed(saved.Course.Items[0].Expected[..1]);
        Record(store, "英文", "x", "z", 100);
        var changed = PersonalPracticeGenerator.Generate(store, source, "英文", PersonalPracticeKind.Confusions)!;
        Check(changed.ContentFingerprint != candidate.ContentFingerprint, "Changed priorities can generate different material");
        Check(repository.Save(changed, progress, true).Course.Id == saved.Course.Id, "New suggestions cannot replace an unfinished snapshot");
        string checkpoint;
        using (var session = PracticeSession.Open(saved.Course, progress)) checkpoint = session.SavePath;
        using (var locked = new FileStream(checkpoint, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(repository.Save(changed, progress, true).Course.Id == saved.Course.Id, "Unreadable progress is protected, not mistaken for completion");
        using (var session = PracticeSession.Open(saved.Course, progress))
        {
            while (!session.IsComplete) session.Feed(session.ExpectedCharacter!.Value.ToString());
        }
        Check(repository.Save(candidate, progress).Course.Id == saved.Course.Id, "Ordinary Save never resets a completed identical course");
        var next = repository.Save(candidate, progress, true);
        Check(next.Round == 2 && next.Course.Id != saved.Course.Id && next.ContentFingerprint == saved.ContentFingerprint, "Explicit next group can repeat identical material with a fresh round identity");
        Check(PracticeSession.ReadProgress(saved.Course, progress).Status == CourseProgressStatus.Completed, "New round preserves completed old progress");
        Check(PracticeSession.ReadProgress(next.Course, progress).Status == CourseProgressStatus.NotStarted, "New round has independent progress");
        Check(JsonSerializer.Serialize(candidate) == candidateBefore, "Saving and creating a new round never mutate the caller's candidate");
        Check(repository.Load().Count == 1 && repository.Load()[0].Course.Id == next.Course.Id, "Only latest slot is listed");
        Check(Directory.GetFiles(Path.Combine(root, "plans", "archive"), "*.json").Length == 2, "Both immutable snapshots retained in archive");
        repository.Save(englishContext, progress);
        Check(repository.Load().Count == 2, "Independent kind gets its own slot");

        var slots = Directory.GetFiles(Path.Combine(root, "plans"), "slot-*.json");
        var roundSlot = slots.Single(p => File.ReadAllText(p).Contains(next.Course.Id));
        var originalSlot = File.ReadAllText(roundSlot);
        File.WriteAllText(roundSlot, "broken");
        Check(repository.Load().Single(p => p.Kind == PersonalPracticeKind.Confusions).Course.Id == saved.Course.Id, "Corrupt primary recovers a validated backup");
        Check(repository.Save(changed, progress, true).Course.Id == saved.Course.Id && File.ReadAllText(roundSlot) == "broken",
            "A recovered older backup cannot authorize overwriting a potentially unfinished newer round");
        File.WriteAllText(roundSlot + ".bak", "also broken");
        Check(repository.Load().Count == 1 && repository.RecoveryNotice is not null, "One corrupt slot cannot block other courses");
        var rejected = false;
        try { repository.Save(candidate, progress, true); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(roundSlot) == "broken", "A corrupt slot cannot silently be replaced as if it were empty");
        File.WriteAllText(roundSlot, originalSlot);
        File.WriteAllText(roundSlot + ".bak", originalSlot);
        File.Delete(roundSlot);
        Check(repository.Load().Single(p => p.Kind == PersonalPracticeKind.Confusions).Course.Id == next.Course.Id,
            "A missing primary still discovers the unfinished backup slot");
        Check(repository.Save(changed, progress, true).Course.Id == next.Course.Id && !File.Exists(roundSlot)
            && File.ReadAllText(roundSlot + ".bak") == originalSlot,
            "A missing primary cannot make Save overwrite an unfinished backup as a new slot");
        File.WriteAllText(roundSlot, originalSlot);
        var badPlan = JsonSerializer.Deserialize<PersonalPracticePlan>(candidateBefore)!;
        badPlan.Course.Items[0].Expected += "tampered";
        rejected = false;
        try { repository.Save(badPlan, progress); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Content fingerprint rejects changed snapshots");
        badPlan = JsonSerializer.Deserialize<PersonalPracticePlan>(candidateBefore)!;
        badPlan.Course.Group = null!;
        rejected = false;
        try { repository.Save(badPlan, progress); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Missing group metadata cannot reach course label rendering");

        if (args.Length > 0)
        {
            var catalog = CourseCatalog.Load(args[0]);
            var longCourse = catalog.Where(c => TypingMode.ForCourse(c) == "五笔86版").OrderByDescending(c => c.Items.Count).First();
            var fullText = string.Concat(longCourse.Items.Select(i => i.Expected));
            var stress = new AnalyticsStore(Path.Combine(root, "stress-analytics"));
            var attempts = Enumerable.Range(2, Math.Min(10000, fullText.Length - 4)).Where(i => char.IsAsciiLetter(fullText[i]))
                .Select(i => new TypingAttempt(fullText[i].ToString(), fullText[i] == 'z' ? "x" : "z", fullText.Substring(i - 2, 2), fullText.Substring(i + 1, 2))).ToArray();
            stress.RecordPractice("五笔86版", 0, 0, attempts.Length, 0, attempts);
            var watch = Stopwatch.StartNew();
            var stressPlan = PersonalPracticeGenerator.Generate(stress, catalog, "五笔86版", PersonalPracticeKind.Context);
            watch.Stop();
            Check(stressPlan is not null, "Real catalog with many contexts produces a valid complete-item practice");
            Console.WriteLine($"Catalog performance: {catalog.Count} courses, longest Wubi {longCourse.Items.Count} items, {stress.ReadContextRisks().Count} contexts, generation {watch.ElapsedMilliseconds} ms.");
        }
        Console.WriteLine($"PASS: {checks} personal practice assertions.");
        return 0;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
