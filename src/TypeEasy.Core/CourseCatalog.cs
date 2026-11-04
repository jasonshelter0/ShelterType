using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TypeEasy.Core;

public sealed class Course
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Group { get; set; } = "";
    public string InputMode { get; set; } = "";
    public string TeachingImage { get; set; } = "";
    public List<ExerciseItem> Items { get; set; } = new();
}

public sealed class ExerciseItem
{
    public string Prompt { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Hint { get; set; } = "";
    public string ImagePath { get; set; } = "";
}

/// <summary>Reads the user's existing course files without loading the original executable or DLLs.</summary>
public static class CourseCatalog
{
    public static List<string> Warnings { get; } = new();
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Adage"] = "格言", ["Essay"] = "散文", ["Joke"] = "笑话", ["Novel"] = "小说",
        ["Other"] = "杂项", ["Poesy"] = "诗歌", ["Program"] = "程序文本",
        ["Standard"] = "标准键盘", ["Numpad"] = "数字键盘",
        ["agriculture"] = "农业", ["biology"] = "生物", ["chemistry"] = "化学",
        ["computer"] = "计算机", ["economic and  trade"] = "经贸", ["electron"] = "电子",
        ["law"] = "法律", ["machinery"] = "机械", ["medical"] = "医学", ["petroleum"] = "石油",
        ["4W"] = "大学四级", ["6W"] = "大学六级", ["Grade"] = "小学词汇",
        ["High"] = "高中词汇", ["Middle"] = "初中词汇", ["TOEFL"] = "托福", ["GRE"] = "GRE", ["GMAT"] = "GMAT"
    };

    public static List<Course> Load(string root)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Warnings.Clear();
        var data = Directory.Exists(Path.Combine(root, "Data")) ? Path.Combine(root, "Data") : root;
        var courses = new List<Course>();
        if (!Directory.Exists(data)) Warnings.Add("找不到课程目录：" + data);
        else
        {
            LoadWubi(data, courses);
            LoadEnglish(data, courses);
            LoadChinese(data, courses);
            LoadPinyin(data, courses);
        }
        // A missing or moved resource directory must not leave an empty application.
        // These fallback IDs are independent of imported course IDs and saved progress.
        if (courses.Count == 0) courses.AddRange(BeginnerLessons.CreateFallbackWubi());
        courses.AddRange(BeginnerLessons.Create());
        courses.AddRange(BeginnerLessons.CreateRootLessons(data));
        courses.AddRange(BeginnerLessons.CreateRootDrills(data));
        foreach (var course in courses.Where(c => c.Category == "五笔" && c.TeachingImage.Length == 0))
            course.TeachingImage = "Teaching/wubi" + (course.Variant.StartsWith("98") ? "98" : "86") + "-keyboard.png";
        CourseGrouping.Assign(courses);
        return courses;
    }

    public static string GroupFor(Course course) => CourseGrouping.For(course);

    private static void LoadPinyin(string data, List<Course> courses)
    {
        foreach (var path in Files(Path.Combine(data, "Chinese", "T_Character"), ".txt")) TryLoad(path, () =>
        {
            var lines = Lines(ReadText(path));
            var title = lines.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? Path.GetFileNameWithoutExtension(path);
            var subfolder = new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
            var group = subfolder.StartsWith("HSK", StringComparison.OrdinalIgnoreCase) ? "拼音/单字·HSK"
                : subfolder.Contains("模糊音") ? "拼音/模糊音与方言" : "拼音/连音与异读词";
            var course = new Course { Id = Id(data, path), Category = "中文", Variant = "拼音", InputMode = "pinyin", Group = group, Title = title };
            var skipped = 0;
            foreach (var line in lines.Where(s => s.Contains('@')))
            {
                var parts = line.Split('@');
                if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1]) || !Regex.IsMatch(parts[2].Trim(), "^[a-zA-Z']+$"))
                { skipped++; continue; }
                var expected = parts[2].Trim().ToLowerInvariant();
                course.Items.Add(new ExerciseItem {
                    Prompt = parts[1].Trim(), Expected = expected + " ",
                    Hint = parts[0].Trim() + "  ·  " + expected + "  ·  空格确认；ü 输入 v，保留隔音符号 '"
                });
            }
            if (skipped > 0) Warnings.Add(title + "：跳过 " + skipped + " 条格式不完整的原版拼音记录。");
            if (course.Items.Count > 0) courses.Add(course);
        });
    }
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(936).GetString(bytes); }
    }

    private static IEnumerable<string> Files(string path, string extension)
        => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(p => string.Equals(Path.GetExtension(p), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Regex.Replace(p, @"\d+", m => m.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

    private static string Id(string data, string path, string variant = "")
        => Path.GetRelativePath(data, path).Replace('\\', '/').ToLowerInvariant() + (variant.Length == 0 ? "" : "#" + variant);
    private static string Name(string name) => Names.TryGetValue(name, out var n) ? n : name;
    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static void TryLoad(string path, Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException or JsonException or InvalidDataException)
        { Warnings.Add(Path.GetFileName(path) + "：" + e.Message); }
    }

    private static void LoadEnglish(string data, List<Course> courses)
    {
        var root = Path.Combine(data, "English");
        foreach (var path in Files(Path.Combine(root, "T_Character"), ".txt")) TryLoad(path, () =>
        {
            var lines = Lines(ReadText(path));
            var title = lines.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? Path.GetFileNameWithoutExtension(path);
            var course = new Course { Id = Id(data, path), Category = "英文", Variant = "键位", Title = Name(new DirectoryInfo(Path.GetDirectoryName(path)!).Name) + " · " + title };
            var first = Array.FindIndex(lines, s => !string.IsNullOrWhiteSpace(s));
            foreach (var line in lines.Skip(first + 1).Where(s => !string.IsNullOrWhiteSpace(s)))
                AddSegments(course, line.Trim().Replace('\t', ' '), 75, "按屏幕文字跟打，空格也需输入");
            if (course.Items.Count > 0) courses.Add(course);
        });
        foreach (var path in Files(Path.Combine(root, "T_Word"), ".ID")) TryLoad(path, () =>
        {
            // The original ID index stores: uint dictionary offset, ushort GBK byte count,
            // word bytes, uint metadata. Do not treat the compressed DAT dictionary as text.
            using var reader = new BinaryReader(File.OpenRead(path));
            var course = new Course { Id = Id(data, path), Category = "英文", Variant = "单词", Title = "单词 · " + Name(Path.GetFileNameWithoutExtension(path)) };
            var gbk = Encoding.GetEncoding(936);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (reader.BaseStream.Length - reader.BaseStream.Position < 10) throw new InvalidDataException("单词索引尾部不完整");
                var dictionaryOffset = reader.ReadUInt32();
                var length = reader.ReadUInt16();
                // Several shipped indexes end in zero-filled reserve records.
                if (length == 0 && dictionaryOffset == 0)
                {
                    var tail = reader.ReadBytes(checked((int)(reader.BaseStream.Length - reader.BaseStream.Position)));
                    if (tail.All(b => b == 0)) break;
                    throw new InvalidDataException("单词索引空记录之后包含非零数据");
                }
                if (length == 0 || length > 2048 || reader.BaseStream.Length - reader.BaseStream.Position < length + 4)
                    throw new InvalidDataException("单词索引记录无效");
                var word = gbk.GetString(reader.ReadBytes(length)).Trim();
                reader.ReadUInt32();
                if (word.Length > 0 && !word.Any(char.IsControl))
                    course.Items.Add(new ExerciseItem { Prompt = word, Expected = word + " ", Hint = "输入单词后按空格" });
            }
            if (course.Items.Count > 0) courses.Add(course);
        });
        LoadArticles(data, Path.Combine(root, "T_Article"), "英文", "文章", courses);
        LoadArticles(data, Path.Combine(root, "Professional"), "英文", "专业文章", courses);
        LoadArticles(data, Path.Combine(root, "E_General"), "英文", "综合文章", courses);
    }

    private static void LoadChinese(string data, List<Course> courses)
    {
        var root = Path.Combine(data, "Chinese");
        LoadArticles(data, Path.Combine(root, "T_Article"), "中文", "文章", courses);
        LoadArticles(data, Path.Combine(root, "Professional"), "中文", "专业文章", courses);
        LoadArticles(data, Path.Combine(root, "E_General"), "中文", "综合文章", courses);
        foreach (var path in Files(Path.Combine(root, "T_Word"), ".txt")) TryLoad(path, () =>
        {
            var lines = Lines(ReadText(path)).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            var title = lines.FirstOrDefault()?.Trim() ?? Path.GetFileNameWithoutExtension(path);
            var course = new Course { Id = Id(data, path), Category = "中文", Variant = "词语", Title = "词语 · " + title };
            foreach (var line in lines.Skip(1))
            {
                var word = line.Trim();
                // Some source files use pronunciation@word@plain-pinyin records.
                if (word.Contains('@')) { var pieces = word.Split('@'); if (pieces.Length >= 2) word = pieces[1]; }
                if (word.Length > 0) course.Items.Add(new ExerciseItem { Prompt = word, Expected = word, Hint = "用系统中文输入法输入；提交文字后自动继续" });
            }
            if (course.Items.Count > 0) courses.Add(course);
        });
    }

    private static void LoadArticles(string data, string folder, string category, string kind, List<Course> courses)
    {
        foreach (var path in Files(folder, ".txt")) TryLoad(path, () =>
        {
            var subgroup = Path.GetRelativePath(folder, Path.GetDirectoryName(path)!);
            var title = Path.GetFileNameWithoutExtension(path);
            if (subgroup != ".") title = string.Join(" / ", subgroup.Split(Path.DirectorySeparatorChar).Select(Name)) + " · " + title;
            var course = new Course { Id = Id(data, path), Category = category, Variant = kind, Title = kind + " · " + title };
            var paragraphs = Lines(ReadText(path)).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().Replace('\t', ' '));
            var text = string.Join(category == "英文" ? " " : "", paragraphs);
            AddSegments(course, text, category == "英文" ? 76 : 30,
                category == "英文" ? "按原文跟打；本段完成后自动继续" : "用系统中文输入法输入，标点需与原文一致");
            if (course.Items.Count > 0) courses.Add(course);
        });
    }

    private static void AddSegments(Course course, string text, int maxLength, string hint)
    {
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(maxLength, text.Length - offset);
            if (offset + length < text.Length && course.Category == "英文")
            {
                var boundary = text.LastIndexOf(' ', offset + length - 1, length);
                if (boundary >= offset + length / 2) length = boundary - offset + 1;
            }
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
            var segment = text.Substring(offset, length);
            course.Items.Add(new ExerciseItem { Prompt = segment, Expected = segment, Hint = hint });
            offset += length;
        }
    }

    private static void LoadWubi(string data, List<Course> courses)
    {
        var folder = Path.Combine(data, "Wubi", "WuBi");
        var levelOne = Path.Combine(folder, "jianma1.txt");
        if (File.Exists(levelOne)) TryLoad(levelOne, () =>
        {
            var chars = ReadText(levelOne).Where(c => !char.IsWhiteSpace(c)).ToArray();
            if (chars.Length != 25) throw new InvalidDataException("一级简码课程应包含 25 个汉字");
            var course = new Course { Id = Id(data, levelOne, "86"), Category = "五笔", Variant = "86版", Title = "一级简码" };
            for (var i = 0; i < chars.Length; i++)
            {
                var code = ((char)('a' + i)).ToString();
                course.Items.Add(new ExerciseItem { Prompt = chars[i].ToString(), Expected = code + " ", Hint = code + "  空格确认" });
            }
            courses.Add(course);
        });
        // Additional dictionaries are optional converted data, not executable components.
        foreach (var variant in new[] { "86", "98" }) LoadWubiDictionaryCourses(data, folder, variant, courses);
    }

    private static void LoadWubiDictionaryCourses(string data, string folder, string variant, List<Course> courses)
    {
        var candidates = new[] { Path.Combine(data, "Wubi", "wubi" + variant + ".json"),
            Path.Combine(folder, "wubi" + variant + ".json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "Wubi", "wubi" + variant + ".json") };
        var json = candidates.FirstOrDefault(File.Exists);
        if (json is null) { Warnings.Add("五笔 " + variant + " 版全码字典尚未转换，相关课程暂不可用。"); return; }
        Dictionary<string, string> map;
        try { map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(json)) ?? new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        { Warnings.Add("五笔字典加载失败：" + e.Message); return; }
        if (variant == "98")
        {
            var first = Path.Combine(folder, "jianma1.txt");
            if (File.Exists(first)) TryLoad(first, () =>
            {
                var course = new Course { Id = Id(data, first, variant), Category = "五笔", Variant = "98版", Title = "一级简码" };
                foreach (var c in ReadText(first).Where(c => !char.IsWhiteSpace(c)))
                {
                    var code = GetWubiCode(map, c.ToString(), 1);
                    if (code is not null) course.Items.Add(new ExerciseItem { Prompt = c.ToString(), Expected = code + " ", Hint = code + "  空格确认" });
                }
                if (course.Items.Count > 0) courses.Add(course);
            });
        }
        var specs = new[] { ("jianma2" + (variant == "98" ? "98" : "") + ".txt", "二级简码", true),
            ("cyz.txt", "常用字", true), ("sbm.txt", "识别码", true),
            ("cizu2" + (variant == "98" ? "98" : "") + ".txt", "双字词组", false),
            ("cizu3" + (variant == "98" ? "98" : "") + ".txt", "三字词组", false),
            ("cizu4" + (variant == "98" ? "98" : "") + ".txt", "多字词组", false) };
        foreach (var (name, title, single) in specs)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            TryLoad(path, () =>
            {
                var text = ReadText(path);
                var prompts = single ? text.Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()) : Lines(text).Select(s => s.Trim()).Where(s => s.Length > 0);
                var course = new Course { Id = Id(data, path, variant), Category = "五笔", Variant = variant + "版", Title = title + (title == "二级简码" ? "" : " · 编码") };
                int missing = 0;
                foreach (var prompt in prompts)
                {
                    var code = GetWubiCode(map, prompt, title == "二级简码" ? 2 : 0);
                    if (code is null) { missing++; continue; }
                    course.Items.Add(new ExerciseItem { Prompt = prompt, Expected = code + " ", Hint = code + "  空格确认" });
                }
                if (missing > 0) Warnings.Add(variant + "版 " + title + "：跳过 " + missing + " 个未能验证编码的项目。");
                if (course.Items.Count > 0) courses.Add(course);
            });
        }
    }

    private static IEnumerable<string> Codes(Dictionary<string, string> map, string prompt)
        => map.TryGetValue(prompt, out var value)
            ? value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(code => Regex.IsMatch(code, "^[a-y]{1,4}$"))
            : Enumerable.Empty<string>();

    private static string? GetWubiCode(Dictionary<string, string> map, string prompt, int preferredLength = 0)
    {
        // Use only codes actually returned by the original dictionary. A two-key abbreviation
        // is NOT necessarily the first two letters of a full code.
        var codes = Codes(map, prompt);
        if (preferredLength != 0) return codes.FirstOrDefault(c => c.Length == preferredLength);
        var direct = codes.OrderByDescending(c => c.Length).FirstOrDefault();
        if (direct is not null) return direct;
        return null;
    }
}





