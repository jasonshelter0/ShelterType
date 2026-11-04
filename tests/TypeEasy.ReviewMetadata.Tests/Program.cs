using TypeEasy.Core;

return TestGuard.Run(args, () =>
{
var directory = Path.Combine(Path.GetTempPath(), "TypeEasyReviewMetadata-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var assertions = 0;
void Assert(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}
try
{
    foreach (var course in new[]
    {
        new Course { Id = "test/pinyin", Category = "中文", Title = "拼音", Variant = "拼音", Group = "拼音/入门", InputMode = "pinyin",
            Items = new() { new() { Prompt = "妈妈", Expected = "ma ma ", Hint = "直接敲拼音" } } },
        new Course { Id = "test/roots", Category = "五笔", Title = "字根", Variant = "86版", Group = "86版/入门字根", TeachingImage = "Teaching/wubi86-keyboard.png",
            Items = new() { new() { Prompt = "工字旁", Expected = "a ", Hint = "按对应键", ImagePath = "Teaching/roots86/A.png" } } }
    })
    {
        Course review;
        using (var session = PracticeSession.Open(course, directory))
        {
            session.Feed("z");
            review = session.BuildReviewCourse();
            Assert(review.InputMode == course.InputMode, "review must preserve explicit input mode instead of choosing IME by category");
            Assert(review.TeachingImage == course.TeachingImage, "review must preserve keyboard teaching image");
            Assert(review.Items[0].ImagePath == course.Items[0].ImagePath, "review must preserve root-card path");
            Assert(review.Items[0].Expected == course.Items[0].Expected && review.Items[0].Prompt == course.Items[0].Prompt, "review must preserve actual exercise");
            Assert(review.Category == course.Category && review.Variant == course.Variant, "review preserves category and variant");
        }
        using (var session = PracticeSession.Open(review, directory)) session.Feed(review.Items[0].Expected[..1]);
        using (var source = PracticeSession.Open(course, directory)) review = source.BuildReviewCourse();
        using (var restored = PracticeSession.Open(review, directory))
            Assert(restored.State.CharacterIndex == 1 && restored.ResumeNotice is null, "metadata-bearing review still resumes exactly");
    }
    Console.WriteLine($"PASS: {assertions} review metadata assertions (pinyin input mode, root teaching images, restored progress).");
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
finally { Directory.Delete(directory, recursive: true); }
});

