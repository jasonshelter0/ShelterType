using TypeEasy.Core;
using TypeEasy.Windows;

return TestGuard.Run(args, () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "ShelterType-keyboard-statistics-tests-" + Guid.NewGuid().ToString("N"));
    var store = new AnalyticsStore(folder);
    var checks = 0;
    void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    void Record(string mode, string expected, string actual, string before, string after, int count, string typedBefore = "")
    {
        var attempts = Enumerable.Range(0, count).Select(_ => new TypingAttempt(expected, actual, before, after, typedBefore)).ToArray();
        store.RecordPractice(mode, 0, expected == actual ? count : 0, expected != actual ? count : 0, 0, attempts);
    }
    try
    {
        // Source contexts and previously typed text are different observations.
        Record("英文", "g", "g", "ab", "cd", 25, "xy");
        Record("英文", "g", "f", "ab", "cd", 3, "zz");
        Record("英文", "g", "t", "ab", "cd", 2, "zz");
        Record("英文", "g", "g", "qq", "rr", 70);
        Record("英文", "G", "G", "ab", "cd", 17);
        Record("英文", "G", "g", "ab", "cd", 4);
        Record("中文输入", "g", "f", "ab", "cd", 3);
        Record("数字键盘", "1", "2", "00", "33", 5);
        Record("英文", "1", "2", "00", "33", 7);

        var risks = store.ReadContextRisks();
        var risk = risks.Single(c => c.Mode == "英文" && c.Expected == "g" && c.Before == "ab");
        Check(risk.Opportunities == 30 && risk.Errors == 5, "Context denominator includes correct attempts and all wrong outputs");
        Check(risk.BaselineOpportunities == 100 && risk.BaselineErrors == 5, "Baseline preserves the exact expected character and mode");
        Check(risk.ActualCounts["g"] == 25 && risk.ActualCounts["f"] == 3 && risk.ActualCounts["t"] == 2,
            "Each wrong output remains associated with its original before/expected/after context");
        Check(risk.ActualCounts.Where(p => p.Key != risk.Expected).Sum(p => p.Value) == risk.Errors,
            "Correct outputs must be excluded from typo counts");
        Check(risk.IsEligible && risk.IsRisk, "Context meets the sample and risk thresholds");
        Check(Math.Abs(risk.RateDifference - (6.0 / 32 - 6.0 / 102)) < 1e-9, "Context comparison uses smoothed denominators");
        Check(risk.TypedBeforeCounts["zz"] == 5, "Prior input counts are marginal; they cannot distinguish the f and t errors");
        var caps = risks.Single(c => c.Mode == "英文" && c.Expected == "G");
        Check(caps.BaselineOpportunities == 21 && caps.BaselineErrors == 4, "Uppercase baseline does not merge lowercase attempts");

        var keys = store.ReadKeys();
        var confusions = store.ReadConfusions();
        var data = KeyboardStatistics.Build(keys, confusions, risks);
        var gKey = KeyboardStatistics.PhysicalKey("英文", "g")!;
        var g = data.Single(k => k.KeyId == gKey);
        Check(g.Opportunities == 121 && g.Errors == 9, "Physical-key totals merge case variants once");
        Check(Math.Abs(g.ErrorRate - 9.0 / 121) < 1e-9, "Merged key error rate divides summed counts, not averaged rates");
        Check(g.ShiftOpportunities == 21, "Shift metadata counts expected character requirements, not observed Shift presses");
        Check(data.Sum(k => k.Opportunities) == keys.Sum(k => k.Opportunities), "Context data must not double target opportunities");
        Check(data.Sum(k => k.Submitted) == keys.Sum(k => k.ActualCounts.Values.Sum()), "Context and confusion data must not double submissions");
        Check(g.Contexts.Count == 3, "Distinct original contexts survive physical-key merging");
        Check(KeyboardStatistics.PhysicalKey("中文输入", "g") is null, "ASCII IME commits do not identify a physical key");
        Check(KeyboardStatistics.PhysicalKey("英文", "（其他）") is null, "Other overflow bucket is not a physical key");
        Check(KeyboardStatistics.PhysicalKey("英文", "g") == KeyboardStatistics.PhysicalKey("英文", "G"), "Case variants share a standard key");
        Check(KeyboardStatistics.PhysicalKey("数字键盘", "1") != KeyboardStatistics.PhysicalKey("英文", "1"), "Number pad and main row remain distinct");
        Check(KeyboardStatistics.PhysicalKey("英文", "你好") is null, "Multi-character text cannot identify one physical key");
        var ime = KeyboardStatistics.Build(keys, confusions, risks, "中文输入");
        Check(ime.All(k => k.Unmapped) && ime.Sum(k => k.Opportunities) == 3, "IME mode retains text-only representation");

        var paths = KeyboardStatistics.ContextPaths(risks);
        var aggregates = KeyboardStatistics.AggregatePaths(confusions);
        Check(paths.Count == 6 && paths.Sum(p => p.Count) == 24, "Context paths omit correct values and count each error once");
        Check(aggregates.Count == 6 && aggregates.Sum(p => p.Count) == 24, "Aggregate paths are a separate view of the same errors");
        Check(paths.All(p => p.Context is not null) && aggregates.All(p => p.Context is null), "Path provenance distinguishes context and aggregate views");
        Check(paths.All(p => p.Expected != p.Actual && p.Count > 0), "No correct or zero-count context paths");
        var fPath = paths.Single(p => p.Mode == "英文" && p.Expected == "g" && p.Actual == "f");
        Check(ReferenceEquals(fPath.Context, risk) && fPath.Count == 3,
            "A path keeps its specific typo count alongside the entire context, whose error count may be larger");
        Check(fPath.MatchesKey(gKey) && fPath.MatchesKey(KeyboardStatistics.PhysicalKey("英文", "f")!), "Both relation endpoints can filter a path");
        Check(!fPath.MatchesKey(KeyboardStatistics.PhysicalKey("英文", "t")!), "Unrelated physical keys must not match a path");
        Check(fPath.Id != aggregates.Single(p => p.Mode == "英文" && p.Expected == "g" && p.Actual == "f").Id,
            "Context and aggregate path identities cannot collide");
        var capsPath = paths.Single(p => p.Mode == "英文" && p.Expected == "G");
        Check(capsPath.ExpectedPhysicalKey == capsPath.ActualPhysicalKey && capsPath.ExpectedId == capsPath.ActualId,
            "Wrong-case paths preserve both roles on the same physical key");
        Check(capsPath.ExpectedRequiresShift && !capsPath.ActualCharacterRequiresShift,
            "Case metadata describes standard character requirements without inferring observed Shift or Caps Lock");
        var imePath = paths.Single(p => p.Mode == "中文输入");
        Check(imePath.ExpectedPhysicalKey is null && imePath.ActualPhysicalKey is null, "IME paths cannot invent physical endpoints");
        Check(imePath.ExpectedId.StartsWith("text:中文输入:") && !imePath.MatchesKey(gKey), "IME ASCII paths do not leak into physical-key filters");
        Check(KeyboardStatistics.ContextPaths(risks, "数字键盘").Single().Count == 5, "Context mode filter keeps number pad independent");

        var specialContext = new ContextRisk
        {
            Mode = "英文", Expected = "a", Before = "aa", After = "aa", Opportunities = 20, Errors = 5,
            ActualCounts = new Dictionary<string, long> { ["a"] = 15, ["b"] = 3, ["（其他）"] = 2, ["z"] = 0 }
        };
        var special = KeyboardStatistics.ContextPaths(new[] { specialContext });
        Check(special.Count == 2 && special.Sum(p => p.Count) == 5, "Other bucket remains text while correct and zero counts are omitted");
        var other = special.Single(p => p.Actual == "（其他）");
        Check(other.ActualPhysicalKey is null && other.ExpectedPhysicalKey == KeyboardStatistics.PhysicalKey("英文", "a"),
            "Other overflow bucket never invents a specific wrong key");
        var otherContext = new KeyboardErrorPath("英文", "a", "b", 2,
            new ContextRisk { Mode = "英文", Expected = "a", Before = "a", After = "aaa" });
        Check(otherContext.Id != special.Single(p => p.Actual == "b").Id, "Context identity preserves before/after boundaries");

        var filteredAggregates = KeyboardStatistics.AggregatePaths(new[]
        {
            new TypingConfusion("英文", "a", "a", 15), new TypingConfusion("英文", "a", "b", 3),
            new TypingConfusion("英文", "a", "z", 0), new TypingConfusion("中文输入", "a", "b", 7)
        }, "英文");
        Check(filteredAggregates.Count == 1 && filteredAggregates[0].Count == 3, "Aggregate paths also filter mode, correct values, and zero counts");
        Check(KeyboardStatistics.ContextPaths(risks, "不存在").Count == 0
            && KeyboardStatistics.AggregatePaths(confusions, "不存在").Count == 0, "Absent mode yields no paths");
        var symbolPath = new KeyboardErrorPath("英文", "!", "1", 1, null);
        Check(symbolPath.ExpectedPhysicalKey == symbolPath.ActualPhysicalKey && symbolPath.ExpectedRequiresShift
            && !symbolPath.ActualCharacterRequiresShift, "Shifted symbol confusion supports both roles on one standard key");
        var emptyPath = new KeyboardErrorPath("英文", "", "", 1, null);
        Check(emptyPath.ExpectedPhysicalKey is null && emptyPath.ActualPhysicalKey is null
            && !emptyPath.ExpectedRequiresShift && !emptyPath.ActualCharacterRequiresShift, "Empty text never indexes or invents a physical key");

        Console.WriteLine($"PASS: {checks} keyboard statistics assertions.");
        return 0;
    }
    finally
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
});
