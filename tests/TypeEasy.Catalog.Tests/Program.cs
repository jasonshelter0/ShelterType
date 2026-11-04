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
    Assert(full.Count == 721, "The complete distribution must retain all 721 courses.");
    Assert(full.Select(c => c.Id).Distinct().Count() == 721, "Course IDs remain unique.");
    var catalogHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(full)));
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
    Console.WriteLine($"PASS: {checks} catalog checks; {full.Count} full courses; {fallback.Count} fallback courses.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("CATALOG QA FAILED (caught): " + error);
    return 1;
}
