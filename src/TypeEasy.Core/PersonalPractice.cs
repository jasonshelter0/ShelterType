using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TypeEasy.Core;

public enum PersonalPracticeKind { Confusions, Context }
public sealed record PersonalPracticeTarget(string Expected, string Actual, long Errors, long Opportunities, string Before = "", string After = "");

public sealed class PersonalPracticePlan
{
    public Course Course { get; set; } = new();
    public string Mode { get; set; } = "";
    public PersonalPracticeKind Kind { get; set; }
    public List<PersonalPracticeTarget> Targets { get; set; } = new();
    public string FocusSummary { get; set; } = "";
    public string ContentFingerprint { get; set; } = "";
    public int Round { get; set; } = 1;
}

/// <summary>Creates short, deterministic lessons from observed errors without writing a profile.</summary>
public static class PersonalPracticeGenerator
{
    private const int MaximumCharacters = 400, MaximumItems = 18, MaximumTargets = 12;
    private static readonly string[] SupportedModes = ["英文", "数字键盘", "五笔86版", "五笔98版", "拼音编码", "中文输入"];

    public static PersonalPracticePlan? Generate(AnalyticsStore store, IEnumerable<Course> sourceCourses, string mode, PersonalPracticeKind kind)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sourceCourses);
        if (!SupportedModes.Contains(mode, StringComparer.Ordinal) || !Enum.IsDefined(kind)) return null;
        var keys = store.ReadKeys();
        var contexts = store.ReadContextRisks();
        var targets = (kind == PersonalPracticeKind.Context
            ? contexts.Where(c => c.Mode == mode).SelectMany(c => c.ActualCounts.Select(a =>
                new PersonalPracticeTarget(c.Expected, a.Key, a.Value, c.Opportunities, c.Before, c.After)))
            : keys.Where(k => k.Mode == mode).SelectMany(k => k.ActualCounts.Select(a =>
                new PersonalPracticeTarget(k.Expected, a.Key, a.Value, k.Opportunities))))
            .Where(t => t.Errors > 0 && t.Expected != t.Actual && t.Actual != "（其他）" && t.Actual.Length > 0 && IsExpected(mode, t.Expected))
            .OrderByDescending(t => Score(t)).ThenByDescending(t => t.Errors)
            .ThenBy(t => t.Expected, StringComparer.Ordinal).ThenBy(t => t.Actual, StringComparer.Ordinal)
            .ThenBy(t => t.Before, StringComparer.Ordinal).ThenBy(t => t.After, StringComparer.Ordinal).Take(64).ToArray();
        if (targets.Length == 0) return null;

        var encoded = mode is "五笔86版" or "五笔98版" or "拼音编码";
        var sources = sourceCourses.Where(c => !c.Id.StartsWith("personal:", StringComparison.Ordinal)
            && !c.Id.StartsWith("review:", StringComparison.Ordinal)
            && TypingMode.ForCourse(c) == mode).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        // Keep source item boundaries: a Wubi/Pinyin code must never be cut into invented words.
        var sourceItems = encoded ? sources.SelectMany(SourceItems).ToArray() : [];
        var itemsByCourse = sourceItems.GroupBy(s => s.Course).ToDictionary(g => g.Key, g => g.ToArray());
        var fullTexts = encoded && kind == PersonalPracticeKind.Context
            ? sources.ToDictionary(c => c, c => string.Concat(c.Items.Select(i => i.Expected))) : new Dictionary<Course, string>();
        var chosen = new List<(PersonalPracticeTarget Target, ExerciseItem[] Items, Course? Source)>();
        foreach (var target in targets)
        {
            if (chosen.Count >= MaximumTargets) break;
            if (encoded)
            {
                var matches = sourceItems.Where(s => s.Item.Expected.Length > 0 && s.Item.Expected.Contains(target.Expected, StringComparison.Ordinal))
                    .Select(s => (Source: s, Items: kind == PersonalPracticeKind.Confusions ? new[] { s.Item }
                        : OriginalContextBlock(s, fullTexts[s.Course], itemsByCourse[s.Course], target)))
                    .Where(s => s.Items.Length is > 0 and <= MaximumItems && s.Items.Sum(i => i.Expected.Length) <= MaximumCharacters
                        && s.Items.All(i => WellFormedText(i.Expected)))
                    .OrderBy(s => s.Items.Sum(i => i.Expected.Length)).ThenBy(s => s.Source.Course.Id, StringComparer.Ordinal).ThenBy(s => s.Source.Index)
                    .DistinctBy(s => JsonSerializer.Serialize(s.Items))
                    .Take(3).ToArray();
                foreach (var source in matches)
                {
                    if (chosen.Count >= MaximumTargets) break;
                    chosen.Add((target, source.Items.Select(CloneItem).ToArray(), source.Source.Course));
                }
            }
            else
            {
                var text = kind == PersonalPracticeKind.Context ? target.Before + target.Expected + target.After
                    : ConfusionText(mode, target);
                if (text.Length == 0 || text.Length > MaximumCharacters || !WellFormedText(text)) continue;
                chosen.Add((target, [new ExerciseItem { Prompt = text, Expected = text }], null));
            }
        }
        if (chosen.Count == 0) return null;
        var course = MakeCourse(mode, kind, chosen.Select(c => c.Source).FirstOrDefault(c => c is not null));
        var characters = 0;
        var usedTargets = new List<PersonalPracticeTarget>();
        // Repeat a small set for useful practice, while retaining complete source items.
        for (var pass = 0; pass < MaximumItems && course.Items.Count < MaximumItems; pass++)
        {
            var added = false;
            foreach (var item in chosen)
            {
                var blockCharacters = item.Items.Sum(i => i.Expected.Length);
                if (course.Items.Count + item.Items.Length > MaximumItems || characters + blockCharacters > MaximumCharacters) continue;
                course.Items.AddRange(item.Items.Select(CloneItem));
                characters += blockCharacters;
                if (!usedTargets.Contains(item.Target)) usedTargets.Add(item.Target);
                added = true;
            }
            if (!added) break;
        }
        var plan = new PersonalPracticePlan { Course = course, Mode = mode, Kind = kind, Targets = usedTargets };
        plan.FocusSummary = string.Join(" / ", usedTargets.Take(4).Select(t => t.Expected + " → " + t.Actual));
        plan.ContentFingerprint = Fingerprint(plan);
        plan.Course.Id = IdFor(plan, 1);
        return plan;
    }

    private sealed record SourceItem(Course Course, ExerciseItem Item, int Index, int Start);

    private static IEnumerable<SourceItem> SourceItems(Course course)
    {
        var start = 0;
        for (var i = 0; i < course.Items.Count; i++)
        {
            yield return new(course, course.Items[i], i, start);
            start += course.Items[i].Expected.Length;
        }
    }

    private static ExerciseItem[] OriginalContextBlock(SourceItem source, string fullText, SourceItem[] sourceItems, PersonalPracticeTarget target)
    {
        var pattern = target.Before + target.Expected + target.After;
        for (var at = source.Item.Expected.IndexOf(target.Expected, StringComparison.Ordinal); at >= 0;
            at = source.Item.Expected.IndexOf(target.Expected, at + 1, StringComparison.Ordinal))
        {
            var begin = source.Start + at - target.Before.Length;
            if (begin >= 0 && begin + pattern.Length <= fullText.Length
                && fullText.AsSpan(begin, pattern.Length).SequenceEqual(pattern))
            {
                var first = source.Index;
                while (first > 0 && sourceItems[first].Start > begin) first--;
                var last = source.Index;
                var end = begin + pattern.Length;
                while (last + 1 < sourceItems.Length && sourceItems[last].Start + sourceItems[last].Item.Expected.Length < end) last++;
                return sourceItems.Skip(first).Take(last - first + 1).Select(s => s.Item).Where(i => i.Expected.Length > 0).ToArray();
            }
        }
        return [];
    }

    private static double Score(PersonalPracticeTarget target)
        => Math.Log2(target.Errors + 1.0) * (target.Errors + 1.0) / (target.Opportunities + 2.0);

    private static bool IsExpected(string mode, string value)
    {
        if (value.Length != 1 || char.IsSurrogate(value[0])) return false;
        var c = value[0];
        return mode switch
        {
            "数字键盘" => "0123456789.+-*/\n".Contains(c),
            "五笔86版" or "五笔98版" or "拼音编码" => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or ' ' or '\'',
            "英文" => c is >= ' ' and <= '~' or '\n',
            _ => !char.IsControl(c) || c is '\n'
        };
    }

    private static bool WellFormedText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) { if (++i >= text.Length || !char.IsLowSurrogate(text[i])) return false; }
            else if (char.IsLowSurrogate(text[i]) || char.IsControl(text[i]) && text[i] is not '\n') return false;
        }
        return true;
    }

    private static string ConfusionText(string mode, PersonalPracticeTarget target)
    {
        // IME output remains text; no physical key or encoding is inferred.
        if (mode.EndsWith("中文输入") || !IsExpected(mode, target.Actual)) return string.Concat(Enumerable.Repeat(target.Expected, 4));
        return target.Expected + target.Actual + target.Expected + target.Expected + target.Actual + target.Expected;
    }

    private static Course MakeCourse(string mode, PersonalPracticeKind kind, Course? source)
        => new()
        {
            Title = kind == PersonalPracticeKind.Confusions ? "易混输入" : "语境巩固", Group = "专属练习/" + mode,
            Category = mode.StartsWith("五笔", StringComparison.Ordinal) ? "五笔" : mode is "中文输入" or "拼音编码" ? "中文" : "英文",
            Variant = mode.StartsWith("五笔", StringComparison.Ordinal) ? mode[2..].Replace("中文输入","") : mode == "拼音编码" ? "拼音" : "专属练习",
            InputMode = mode == "拼音编码" ? "pinyin" : mode.StartsWith("五笔")&&mode.EndsWith("中文输入")?"wubi-ime":source?.InputMode ?? "",
            TeachingImage = source?.TeachingImage ?? ""
        };

    internal static ExerciseItem CloneItem(ExerciseItem item)
        => new() { Prompt = item.Prompt, Expected = item.Expected, Hint = item.Hint, ImagePath = item.ImagePath,KeySequence=item.KeySequence };

    internal static string Fingerprint(PersonalPracticePlan plan)
        => Hash(JsonSerializer.Serialize(new
        {
            plan.Mode, plan.Kind, plan.Course.Category, plan.Course.Variant, plan.Course.InputMode,
            plan.Course.TeachingImage, Items = plan.Course.Items.Select(i => new { i.Prompt, i.Expected, i.Hint, i.ImagePath })
        }));

    internal static string IdFor(PersonalPracticePlan plan, int round)
        => "personal:" + (plan.Mode == "数字键盘" ? "numpad:" : "") + plan.Kind.ToString().ToLowerInvariant()
            + ":" + plan.ContentFingerprint + ":r" + round;

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>One active snapshot per mode/kind. Replacing a finished snapshot never removes its course or progress.</summary>
public sealed class PersonalPracticeRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _directory;
    public string? RecoveryNotice { get; private set; }
    public PersonalPracticeRepository(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public IReadOnlyList<PersonalPracticePlan> Load()
    {
        RecoveryNotice = null;
        if (!Directory.Exists(_directory)) return [];
        var plans = new List<PersonalPracticePlan>();
        var slotPaths = Directory.EnumerateFiles(_directory, "slot-*.json")
            .Concat(Directory.EnumerateFiles(_directory, "slot-*.json.bak").Select(p => p[..^4]))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal);
        foreach (var path in slotPaths)
        {
            try { plans.Add(Read(path)); }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { RecoveryNotice = "Some personal practice snapshots could not be read. Their files and progress were preserved."; }
        }
        return plans.OrderBy(p => p.Mode, StringComparer.Ordinal).ThenBy(p => p.Kind).ToArray();
    }

    public PersonalPracticePlan Save(PersonalPracticePlan plan, string progressDirectory, bool startNewRound = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(progressDirectory);
        Validate(plan);
        var path = Path.Combine(_directory, "slot-" + PersonalPracticeGenerator.Hash(plan.Mode + "\n" + plan.Kind) + ".json");
        var recovered = false;
        var previous = Exists(path) || Exists(path + ".bak") ? Read(path, out recovered) : null;
        if (previous is not null)
        {
            // A backup may predate an unfinished round. It can be opened, but must not authorize replacement.
            if (recovered) return previous;
            // Unknown progress is protected too: an unreadable checkpoint is not evidence of completion.
            if (PracticeSession.ReadProgress(previous.Course, progressDirectory).Status != CourseProgressStatus.Completed) return previous;
            if (previous.ContentFingerprint == plan.ContentFingerprint && !startNewRound) return previous;
        }
        var snapshot = JsonSerializer.Deserialize<PersonalPracticePlan>(JsonSerializer.Serialize(plan, Options), Options)!;
        snapshot.Round = previous is null ? 1 : checked(previous.Round + 1);
        snapshot.Course.Id = PersonalPracticeGenerator.IdFor(snapshot, snapshot.Round);
        Validate(snapshot);
        Directory.CreateDirectory(_directory);
        var archive = Path.Combine(_directory, "archive");
        Directory.CreateDirectory(archive);
        if (previous is not null) WriteArchive(archive, previous);
        WriteArchive(archive, snapshot);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Options));
            // Back up the validated snapshot, never a corrupt primary recovered from its backup.
            if (previous is not null) File.WriteAllText(path + ".bak", JsonSerializer.Serialize(previous, Options));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return snapshot;
    }

    private static void WriteArchive(string archive, PersonalPracticePlan plan)
    {
        var path = Path.Combine(archive, PersonalPracticeGenerator.Hash(plan.Course.Id) + ".json");
        if (Exists(path)) return;
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(plan, Options));
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static PersonalPracticePlan Read(string path) => Read(path, out _);

    private static PersonalPracticePlan Read(string path, out bool recovered)
    {
        recovered = false;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!Exists(candidate)) continue;
            try
            {
                var plan = JsonSerializer.Deserialize<PersonalPracticePlan>(File.ReadAllText(candidate), Options);
                if (plan is null) continue;
                Validate(plan);
                recovered = candidate != path;
                return plan;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException) { }
        }
        throw new InvalidDataException("The personal practice snapshot and its backup cannot be read: " + path);
    }

    private static void Validate(PersonalPracticePlan plan)
    {
        if (plan.Course is null || plan.Targets is null || !Enum.IsDefined(plan.Kind) || plan.Round < 1
            || string.IsNullOrEmpty(plan.Mode) || string.IsNullOrEmpty(plan.ContentFingerprint) || string.IsNullOrEmpty(plan.Course.Id)
            || plan.Course.Title is null || plan.Course.Group is null || plan.Course.Category is null || plan.Course.Variant is null
            || plan.Course.InputMode is null || plan.Course.TeachingImage is null
            || plan.Course.Items is null || plan.Course.Items.Count is < 1 or > 24
            || plan.Course.Items.Any(i => i is null || string.IsNullOrEmpty(i.Expected) || i.Prompt is null || i.Hint is null || i.ImagePath is null)
            || plan.Course.Items.Sum(i => (long)i.Expected.Length) > 400
            || plan.Targets.Count is < 1 or > 12 || plan.Targets.Any(t => t is null || t.Errors <= 0 || t.Opportunities < t.Errors
                || string.IsNullOrEmpty(t.Expected) || string.IsNullOrEmpty(t.Actual) || t.Before is null || t.After is null
                || t.Expected == t.Actual || t.Actual == "（其他）")
            || TypingMode.ForCourse(plan.Course) != plan.Mode
            || plan.ContentFingerprint != PersonalPracticeGenerator.Fingerprint(plan)
            || plan.Course.Id != PersonalPracticeGenerator.IdFor(plan, plan.Round))
            throw new InvalidDataException("Invalid personal practice snapshot.");
    }

    private static bool Exists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("Snapshot path is a directory: " + path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
