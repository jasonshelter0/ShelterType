using System.Text.Json.Nodes;
using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
// Pass the shipped app directory, never its UserData folder. Every write is isolated in testRoot.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TypeEasy.Integration.Tests <app-directory>");
    return 2;
}
var testRoot = Path.Combine(Path.GetTempPath(), "TypeEasyIntegration-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var assertions = 0;
void Assert(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}

try
{
    var catalog = CourseCatalog.Load(Path.GetFullPath(args[0]));
    Assert(catalog.Count > 600, "packaged original lessons loaded");
    Assert(catalog.Select(x => x.Id).Distinct().Count() == catalog.Count, "unique IDs across all categories and variants");
    Assert(catalog.All(x => x.Items.Count > 0 && x.Items.All(item => item.Expected.Length > 0)), "no empty courses or exercise targets");
    var firstLevel = catalog.Single(x => x.Category == "五笔" && x.Variant == "86版" && x.Title == "一级简码");
    Assert(firstLevel.Items.Count == 25 && firstLevel.Items.Select(x => x.Expected).SequenceEqual(Enumerable.Range('a', 25).Select(c => (char)c + " ")), "original 86 one-level code order and space confirmation");
    var four = catalog.Single(x => x.Id == "english/t_word/dictionary/4w.id");
    Assert(four.Items.Count == 4543, "all original level-four English index entries imported");
    Assert(catalog.Any(x => x.Category == "五笔" && x.Variant == "98版" && x.Items.Count > 100), "98 dictionary-backed courses present");
    var longCourse = catalog.Where(x => x.Category == "五笔" && x.Variant == "86版").MaxBy(x => x.Items.Count)!;
    Assert(longCourse.Items.Count > 1000, "real long Wubi course selected");
    Assert(longCourse.Items.All(x => x.Expected.EndsWith(' ') && x.Expected[..^1].All(c => c is >= 'a' and <= 'y')), "Wubi code alphabet plus explicit commit space");

    var expectedItem = 17;
    var expectedCharacter = 2;
    Assert(longCourse.Items[expectedItem].Expected.Length > expectedCharacter, "test checkpoint lies inside a real code");
    using (var session = PracticeSession.Open(longCourse, testRoot))
    {
        foreach (var item in longCourse.Items.Take(expectedItem)) session.Feed(item.Expected);
        session.Feed(longCourse.Items[expectedItem].Expected[..expectedCharacter]);
        session.Feed("z");
        Assert(session.State.ItemIndex == expectedItem && session.State.CharacterIndex == expectedCharacter, "long-course cursor before leaving");
        Assert(session.State.Errors == 1, "mistyped physical key counted");
    }
    using (var another = PracticeSession.Open(four, testRoot))
    {
        another.Feed(four.Items[0].Expected[..1]);
        Assert(another.State.CharacterIndex == 1, "another course independently practiced");
    }

    Course review;
    using (var restored = PracticeSession.Open(longCourse, testRoot))
    {
        Assert(restored.State.ItemIndex == expectedItem && restored.State.CharacterIndex == expectedCharacter, "switch back resumes exact code cursor");
        Assert(restored.ExpectedCharacter == longCourse.Items[expectedItem].Expected[expectedCharacter], "resumed next key is exact");
        Assert(restored.State.Errors == 1 && restored.State.Confusions.Single().Actual == "z", "switch preserves error analytics");
        review = restored.BuildReviewCourse();
        Assert(review.Items.Count > 0 && review.Items[0].Prompt == longCourse.Items[expectedItem].Prompt, "review begins with missed original Wubi item");
        Assert(review.Items[0].Expected == longCourse.Items[expectedItem].Expected && review.Variant == longCourse.Variant, "review retains full code and Wubi variant");
    }
    using (var reviewSession = PracticeSession.Open(review, testRoot))
    {
        reviewSession.Feed(review.Items[0].Expected[..2]);
        Assert(reviewSession.State.CharacterIndex == 2, "review checkpoint created inside code");
    }
    // Simulate a new process rebuilding the catalog and the review from the saved source session.
    var reloadedCatalog = CourseCatalog.Load(Path.GetFullPath(args[0]));
    var reloadedCourse = reloadedCatalog.Single(x => x.Id == longCourse.Id);
    Course regeneratedReview;
    using (var source = PracticeSession.Open(reloadedCourse, testRoot)) regeneratedReview = source.BuildReviewCourse();
    using (var reviewSession = PracticeSession.Open(regeneratedReview, testRoot))
    {
        Assert(reviewSession.State.CharacterIndex == 2 && reviewSession.ResumeNotice is null, "same regenerated review resumes after app restart");
    }
    using (var source = PracticeSession.Open(reloadedCourse, testRoot))
    {
        Assert(source.State.ItemIndex == expectedItem && source.State.CharacterIndex == expectedCharacter, "review progress never overwrites source progress");
        source.Feed(source.CurrentItem!.Expected[expectedCharacter..]);
        source.Feed("zz");
        regeneratedReview = source.BuildReviewCourse();
    }
    using (var reviewSession = PracticeSession.Open(regeneratedReview, testRoot))
    {
        Assert(reviewSession.State.CharacterIndex == 0 && reviewSession.ResumeNotice is not null, "changed review ordering uses content fingerprint and discloses restart");
    }

    // Valid JSON can still contain corrupt nested entries. It must recover instead of dereferencing null.
    string savePath;
    using (var valid = PracticeSession.Open(reloadedCourse, testRoot)) savePath = valid.SavePath;
    var json = JsonNode.Parse(File.ReadAllText(savePath))!;
    json["Keys"] = JsonNode.Parse("[null]");
    File.WriteAllText(savePath, json.ToJsonString());
    using (var recovered = PracticeSession.Open(reloadedCourse, testRoot))
    {
        Assert(recovered.ResumeNotice is not null && recovered.State.ItemIndex == expectedItem + 1, "null nested key statistic falls back to valid backup");
        Assert(recovered.State.Keys.All(x => x is not null), "recovered statistics safe to render");
    }

    Console.WriteLine($"PASS: {assertions} real-data integration assertions; {catalog.Count} courses; long Wubi '{longCourse.Title}' has {longCourse.Items.Count} items.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    Directory.Delete(testRoot, recursive: true);
}
});

