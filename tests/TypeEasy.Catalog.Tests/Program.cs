using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TypeEasy.Core;

// Every failure is caught before leaving Main, including the deliberate failure-boundary probe.
try
{
    if (args.Contains("--qa-verify-failure")) throw new InvalidOperationException("Intentional failure-boundary check.");
    var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var checks = 0;
    void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    var full = CourseCatalog.Load(root);
    var legacy=full.Where(c=>c.Origin!="public-domain").ToList();
    Assert(legacy.Count == 721, "The complete distribution must retain all 721 existing courses.");
    Assert(full.Select(c => c.Id).Distinct().Count() == full.Count, "Course IDs remain unique.");
    var learning=legacy.Where(c=>c.Id.Contains("/root-zone-")).ToList();
    Assert(learning.Count==12 && learning.All(c=>c.Id.EndsWith("/v2")),"Both variants use the revised per-root learning sequence.");
    foreach(var lesson in learning){
        var variant=lesson.Variant[..2];
        var drill=full.Single(c=>c.Id==lesson.Id.Replace("root-zone-","root-drill-").Replace("/v2","/v1"));
        Assert(lesson.Items.Select(i=>i.ImagePath).SequenceEqual(drill.Items.Select(i=>i.ImagePath)),"Every root is shown once, in key order.");
        Assert(lesson.Items.All(i=>i.ImagePath.StartsWith("Teaching/glyphs"+variant+"/") && i.Expected==Path.GetFileNameWithoutExtension(i.ImagePath)[0]+" "),"Shown root and expected key match.");
    }
    // Reconstruct only the deliberately replaced v1 cards for the unchanged-content baseline.
    legacy=JsonSerializer.Deserialize<List<Course>>(JsonSerializer.Serialize(legacy))!;
    foreach(var lesson in legacy.Where(c=>c.Id.Contains("/root-zone-"))){
        var variant=lesson.Variant[..2];var number=lesson.Id.Split("root-zone-")[1][..2];
        var title=new Dictionary<string,string>{{"01","横区"},{"02","竖区"},{"03","撇区"},{"04","捺区"},{"05","折区"},{"06","全部键位"}}[number];
        lesson.Id=lesson.Id.Replace("/v2","/v1");lesson.Title=number+" · "+title+"字根识记（原版图）";
        lesson.Items=lesson.Items.Select(i=>i.Expected[0]).Distinct().Select(key=>new ExerciseItem{Prompt=title+" · 字根识记",Expected=key+" ",ImagePath="Teaching/roots"+variant+"/"+char.ToUpperInvariant(key)+".png",Hint="原版字根卡片（保留字母标注）：观察字根，按 "+char.ToUpperInvariant(key)+" 键，再按空格。"}).ToList();
    }
    var catalogHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(legacy.Select(c=>new {c.Id,c.Title,c.Category,c.Variant,c.Group,c.InputMode,c.TeachingImage,c.Items}))));
    const string expectedHash = "ABD492880F4BB2E590FA5142F25FD24E147A6CA26E33C694A1A1A838C4F6F8F0";
    Assert(catalogHash == expectedHash, "Full course content, IDs, grouping, hints and image references must match version 0.6.");
    foreach (var course in full)
        foreach (var item in course.Items.Where(i => i.ImagePath.Length > 0))
            Assert(File.Exists(Path.Combine(root, item.ImagePath.Replace('/', Path.DirectorySeparatorChar))), "Missing teaching image: " + item.ImagePath);
    var absent = Path.Combine(Path.GetTempPath(), "ShelterType-missing-" + Guid.NewGuid().ToString("N"));
    var fallback = CourseCatalog.Load(absent);
    Assert(fallback.Count == 15, "Missing resources retain 13 teaching lessons and two Wubi keyboard exercises.");
    Assert(fallback.All(c => c.Id.StartsWith("builtin/") && c.Items.Count > 0), "Fallback courses are self-contained.");
    Assert(new[] { "五笔", "英文", "中文" }.All(category => fallback.Any(c => c.Category == category)), "Every category remains usable without resources.");
    Assert(!Directory.Exists(absent), "Loading never creates or modifies the selected course directory.");
    Assert(!full.Any(c => c.Id.Contains("/key-zones/")), "Fallback courses do not change a complete catalog.");
    var added=full.Where(c=>c.Origin=="public-domain").ToList();
    Assert(added.Count>=30 && added.All(c=>c.SourceUrl.StartsWith("https://") && c.License.Length>0 && c.Author.Length>0),"New articles retain provenance.");
    Assert(added.All(c=>c.Items.All(i=>!i.Expected.Any(char.IsControl)&&!i.Expected.Contains('\u200b'))),"Articles contain no invisible input traps.");
    var clean=CourseCatalog.Load(root,false);
    Assert(clean.All(c=>c.Origin!="kingsoft-2008" && c.TeachingImage.Length==0 && c.Items.All(i=>i.ImagePath.Length==0)),"Kingsoft filter removes all tagged course/resource dependencies.");
    var scratch=Path.Combine(Path.GetTempPath(),"ShelterType-shuffle-"+Guid.NewGuid().ToString("N"));
    var original=full.First(c=>c.Id.Contains("root-drill")&&c.Items.Select(i=>i.Expected).Distinct().Count()>1);
    var before=JsonSerializer.Serialize(original);
    var shuffled=ShuffledPractice.Create(original,scratch);
    Assert(JsonSerializer.Serialize(original)==before,"Shuffle leaves the original untouched.");
    Assert(shuffled.Items.Select(i=>i.ImagePath).Order().SequenceEqual(original.Items.Select(i=>i.ImagePath).Order()),"Shuffle retains every root exactly once.");
    var saved=ShuffledPractice.Load(scratch,full).Single();
    Assert(saved.Items.Select(i=>i.ImagePath).SequenceEqual(shuffled.Items.Select(i=>i.ImagePath)),"Saved order survives reopening.");
    using(var practice=PracticeSession.Open(shuffled,Path.Combine(scratch,"Progress")))practice.Feed(shuffled.Items[0].Expected);
    using(var resumed=PracticeSession.Open(saved,Path.Combine(scratch,"Progress")))Assert(resumed.State.ItemIndex==1,"Shuffled lesson resumes the same next root.");
    Assert(!ShuffledPractice.CanShuffle(added.First()),"Articles cannot be scrambled.");
    foreach(var variant in new[]{"86","98"}){
        var fold=full.Single(c=>c.Id=="builtin/wubi"+variant+"/root-zone-05/v2");
        Assert(fold.Items.Count(i=>i.Expected=="n ")==10,"Fold-zone lesson includes all ten N roots: "+variant);
        Assert(fold.Items.Select(i=>i.Expected.Trim()).Distinct().SequenceEqual(new[]{"n","b","v","c","x"}),"Fold keys remain NBVCX: "+variant);
    }
    var imeCourses=full.Select(WubiInput.ForCourse).ToList();
    var converted=imeCourses.Where(c=>c.InputMode=="wubi-ime").ToList();
    Assert(converted.Count>0 && converted.Any(c=>c.Variant=="86版")&&converted.Any(c=>c.Variant=="98版"),"Both Wubi variants support Chinese commits.");
    foreach(var lesson in converted){
        var originalCourse=full.Single(c=>c.Id==lesson.BaseCourseId);
        Assert(lesson.Items.Select(i=>i.Expected).SequenceEqual(originalCourse.Items.Select(i=>i.Prompt)),"IME lessons expect the displayed Chinese, without a synthetic commit space.");
        Assert(lesson.Items.Select(i=>i.KeySequence).SequenceEqual(originalCourse.Items.Select(i=>i.Expected.Trim())),"Original codes retained as hints.");
        Assert(TypingMode.ForCourse(lesson).EndsWith("中文输入"),"IME output stats are separate from code keys.");
    }
    var wordLesson=converted.First(c=>c.Items[0].Expected.Length>1);
    using(var practice=PracticeSession.Open(wordLesson,Path.Combine(scratch,"ImeProgress"))){practice.Feed("#");Assert(practice.State.ItemIndex==0,"Wrong committed text does not advance.");practice.Backspace();practice.Feed(wordLesson.Items[0].Expected);Assert(practice.State.ItemIndex==1,"A Chinese phrase commit advances without an extra space.");}
    Assert(imeCourses.Where(c=>c.Items.Any(i=>i.ImagePath.Length>0)).All(c=>c.InputMode!="wubi-ime"),"Unencoded root shapes keep their letter target.");
    var oldCourse=full.Single(c=>c.Id==wordLesson.BaseCourseId);var migration=Path.Combine(scratch,"Migration");
    using(var oldSession=PracticeSession.Open(oldCourse,migration))oldSession.Feed(oldCourse.Items[0].Expected);
    var oldFile=Directory.GetFiles(migration,"*.json").Single();var originalRecord=File.ReadAllText(oldFile);
    Assert(PracticeSession.MigrateWubiPosition(oldCourse,wordLesson,migration),"Existing code course migrates its completed-item position.");
    Assert(File.ReadAllText(oldFile)==originalRecord,"Original code record is untouched.");
    using(var migrated=PracticeSession.Open(wordLesson,migration)){Assert(migrated.State.ItemIndex==1&&migrated.State.CharacterIndex==0&&migrated.State.TotalInputs==0,"Position resumes without mixing physical-key counts into committed text statistics.");}
    Assert(!PracticeSession.MigrateWubiPosition(oldCourse,wordLesson,migration),"Existing Chinese progress is never overwritten.");
    Console.WriteLine($"IME: {converted.Count} converted courses; remaining non-image Wubi courses: "+string.Join(", ",imeCourses.Where(c=>c.Category=="五笔"&&c.InputMode!="wubi-ime"&&c.Items.All(i=>i.ImagePath.Length==0)).Select(c=>c.Title)));
    Console.WriteLine($"PASS: {checks} catalog checks; {full.Count} full courses; {fallback.Count} fallback courses.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CATALOG QA FAILED (caught): " + error);
    return 1;
}
