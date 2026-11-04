namespace TypeEasy.Core;

/// <summary>New teaching sequences, clearly distinguished from imported 2008 course content.</summary>
internal static class BeginnerLessons
{
    public static IEnumerable<Course> CreateFallbackWubi()
    {
        foreach (var variant in new[] { "86", "98" })
        {
            var course = new Course {
                Id = "builtin/wubi" + variant + "/key-zones/v1", Title = "全部键位",
                Category = "五笔", Variant = variant + "版", Group = variant + "版/入门"
            };
            foreach (var keys in new[] { "gfdsa", "hjklm", "trewq", "yuiop", "nbvcx" })
                course.Items.Add(new ExerciseItem { Prompt = keys.ToUpperInvariant(), Expected = keys + " ", Hint = keys + "  空格确认" });
            yield return course;
        }
    }

    public static IEnumerable<Course> Create()
    {
        var keyboard = new[]
        {
            ("01-home-index", "01 · 找到 F 和 J", "两手食指轻放 F、J 的凸点；拇指负责空格。按完回到基准键。", new[] { "ffff ", "jjjj ", "ff jj ", "fj fj ", "jf jf ", "ffjj jjff " }),
            ("02-left-home", "02 · 左手基准键", "左手小指 A、无名指 S、中指 D、食指 F；每次按键后回到原位。", new[] { "aaaa ", "ssss ", "dddd ", "ffff ", "asdf asdf ", "fdsa fdsa ", "as df sa fd " }),
            ("03-right-home", "03 · 右手基准键", "右手食指 J、中指 K、无名指 L、小指分号 ;；分号使用英文半角。", new[] { "jjjj ", "kkkk ", "llll ", ";;;; ", "jkl; jkl; ", ";lkj ;lkj ", "jk l; kj ;l " }),
            ("04-both-home", "04 · 双手交替与空格", "双手停在 ASDF 和 JKL;；拇指按空格。先保持准确，再逐渐加快。", new[] { "a; a; ", "sl sl ", "dk dk ", "fj fj ", "asdf jkl; ", "fj dk sl a; ", "fall salad ask flask " }),
            ("05-upper", "05 · 向上伸指", "左手 Q/A、W/S、E/D、R与T/F；右手 Y与U/J、I/K、O/L、P/分号。按后回位。", new[] { "aq aq ", "sw sw ", "de de ", "fr ft ", "jy ju ", "ki ki ", "lo lo ", ";p ;p ", "qwer tyui op " }),
            ("06-lower", "06 · 向下伸指", "左手 Z/A、X/S、C/D、V与B/F；右手 N与M/J、逗号/K、句号/L、斜线/小指。", new[] { "az az ", "sx sx ", "dc dc ", "fv fb ", "jn jm ", "k, k, ", "l. l. ", ";/ ;/ ", "zxcv bnm, ./ " }),
            ("07-shift", "07 · Shift 与大小写", "大写字母用另一只手的小指按住 Shift。按左侧字母时用右 Shift，反之亦然。", new[] { "aA aA ", "fF fF ", "jJ jJ ", "lL lL ", "Aa Ss Dd Ff ", "Jj Kk Ll ", "Hello World " })
        };
        foreach (var (id, title, hint, exercises) in keyboard)
        {
            var course = new Course { Id = "builtin/keyboard/" + id + "/v1", Title = title + "（重构课程）", Category = "英文", Variant = "入门指法", Group = "入门指法" };
            foreach (var text in exercises) course.Items.Add(new ExerciseItem { Prompt = text, Expected = text, Hint = hint });
            yield return course;
        }

        var pinyin = new[]
        {
            ("01-vowels", "01 · 单韵母与 ü", "输入不带声调的拼音，ü 使用键盘 v；每项结束按空格。", new[] { ("a", "a"), ("o", "o"), ("e", "e"), ("i", "i"), ("u", "u"), ("ü", "v") }),
            ("02-initials", "02 · 声母", "按显示的声母输入小写字母；zh、ch、sh 都需要两个字母。", new[] { ("b", "b"), ("p", "p"), ("m", "m"), ("f", "f"), ("d", "d"), ("t", "t"), ("n", "n"), ("l", "l"), ("g", "g"), ("k", "k"), ("h", "h"), ("j", "j"), ("q", "q"), ("x", "x"), ("zh", "zh"), ("ch", "ch"), ("sh", "sh"), ("r", "r"), ("z", "z"), ("c", "c"), ("s", "s") }),
            ("03-finals", "03 · 常用复韵母", "按顺序输入韵母中的字母；这里的 ü 保留写法，键盘上使用 v。", new[] { ("ai", "ai"), ("ei", "ei"), ("ui", "ui"), ("ao", "ao"), ("ou", "ou"), ("iu", "iu"), ("ie", "ie"), ("üe", "ve"), ("er", "er") }),
            ("04-nasal", "04 · 前后鼻音", "前鼻音以 n 结束，后鼻音以 ng 结束；注意最后的 g。", new[] { ("an", "an"), ("ang", "ang"), ("en", "en"), ("eng", "eng"), ("in", "in"), ("ing", "ing"), ("un", "un"), ("ong", "ong"), ("ün", "vn") }),
            ("05-tones", "05 · 声调与键盘拼写", "本软件练习拼音键盘拼写，不输入声调符号或声调数字；按提示保留字母。", new[] { ("mā", "ma"), ("má", "ma"), ("mǎ", "ma"), ("mà", "ma"), ("nǚ", "nv"), ("lǜ", "lv"), ("jū", "ju"), ("qù", "qu"), ("xǔ", "xu") }),
            ("06-separator", "06 · 隔音符号", "音节边界需要时保留英文单引号 '；这是隔音符号，不是声调。", new[] { ("西安 · xī'ān", "xi'an"), ("平安 · píng'ān", "ping'an"), ("女儿 · nǚ'ér", "nv'er"), ("方案 · fāng'àn", "fang'an") })
        };
        foreach (var (id, title, hint, exercises) in pinyin)
        {
            var course = new Course { Id = "builtin/pinyin/" + id + "/v1", Title = title + "（重构课程）", Category = "中文", Variant = "拼音", InputMode = "pinyin", Group = "拼音/入门" };
            foreach (var (prompt, expected) in exercises)
                course.Items.Add(new ExerciseItem { Prompt = prompt, Expected = expected + " ", Hint = hint + "  输入 " + expected });
            yield return course;
        }
    }

    public static IEnumerable<Course> CreateRootLessons(string data)
    {
        // Original 86/98 glyph cards retain their printed key labels. These are learning cards,
        // not a hidden-answer quiz; the hints state this explicitly.
        foreach (var variant in new[] { "86", "98" })
        {
            var chart = FindTeachingFile(data, "wubi" + variant + "-keyboard.png");
            var tiles = "abcdefghijklmnopqrstuvwxyz"[..25].ToDictionary(c => c,
                c => FindTeachingFile(data, "roots" + variant + "/" + char.ToUpperInvariant(c) + ".png"));
            if (chart is null || tiles.Values.Any(v => v is null)) continue;
            var zones = new[] { ("01", "横区", "gfdsa"), ("02", "竖区", "hjklm"), ("03", "撇区", "trewq"), ("04", "捺区", "yuiop"), ("05", "折区", "nbvcx"), ("06", "全部键位", "gfdsa" + "hjklm" + "trewq" + "yuiop" + "nbvcx") };
            foreach (var (number, title, keys) in zones)
            {
                var course = new Course {
                    Id = "builtin/wubi" + variant + "/root-zone-" + number + "/v1",
                    Title = number + " · " + title + "字根识记（原版图）", Category = "五笔", Variant = variant + "版",
                    Group = variant + "版/入门字根", TeachingImage = "Teaching/wubi" + variant + "-keyboard.png"
                };
                foreach (var key in keys)
                    course.Items.Add(new ExerciseItem {
                        Prompt = title + " · 字根识记", Expected = key + " ", ImagePath = "Teaching/roots" + variant + "/" + char.ToUpperInvariant(key) + ".png",
                        Hint = "原版字根卡片（保留字母标注）：观察字根，按 " + char.ToUpperInvariant(key) + " 键，再按空格。"
                    });
                yield return course;
            }
        }
    }

    public static IEnumerable<Course> CreateRootDrills(string data)
    {
        foreach (var variant in new[] { "86", "98" })
        {
            var glyphs = ReadRootGlyphs(data, variant);
            if (glyphs.Count == 0) continue;
            var zones = new[] { ("01", "横区", "gfdsa"), ("02", "竖区", "hjklm"), ("03", "撇区", "trewq"), ("04", "捺区", "yuiop"), ("05", "折区", "nbvcx"), ("06", "综合", "gfdsa" + "hjklm" + "trewq" + "yuiop" + "nbvcx") };
            foreach (var (number, title, keys) in zones)
            {
                var course = new Course {
                    Id = "builtin/wubi" + variant + "/root-drill-" + number + "/v1",
                    Title = number + " · " + title + "字根辨识（原版字根）", Category = "五笔", Variant = variant + "版",
                    Group = variant + "版/字根辨识", TeachingImage = "Teaching/wubi" + variant + "-keyboard.png"
                };
                foreach (var key in keys)
                    foreach (var glyph in glyphs.Where(g => g.Key == key.ToString()).OrderBy(g => g.Id, StringComparer.Ordinal))
                        course.Items.Add(new ExerciseItem {
                            Prompt = "字根 " + (course.Items.Count + 1).ToString("D3"),
                            Expected = key + " ", ImagePath = glyph.ImagePath,
                            Hint = "识别这个原版字根，按所属的 " + char.ToUpperInvariant(key) + " 键，再按空格。可关闭提示独立练习。"
                        });
                if (course.Items.Count > 0) yield return course;
            }
        }
    }

    private sealed class RootGlyph
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string ImagePath { get; set; } = "";
    }

    private static List<RootGlyph> ReadRootGlyphs(string data, string variant)
    {
        var path = FindTeachingFile(data, "root-glyphs" + variant + ".json");
        if (path is null) return new();
        try
        {
            var records = System.Text.Json.JsonSerializer.Deserialize<List<RootGlyph>>(File.ReadAllText(path)) ?? new();
            var valid = records.Where(g =>
                g.Key.Length == 1 && g.Key[0] >= 'a' && g.Key[0] <= 'y' &&
                g.Id.Length == 2 && g.Id[0] == g.Key[0] && g.Id[1] >= '0' && g.Id[1] <= '9' &&
                g.ImagePath == "Teaching/glyphs" + variant + "/" + g.Id + ".png" &&
                FindTeachingFile(data, g.ImagePath["Teaching/".Length..]) is not null)
                .DistinctBy(g => g.Id).ToList();
            if (valid.Count != records.Count) CourseCatalog.Warnings.Add("五笔 " + variant + " 版字根图：跳过缺失或无效的图片记录。");
            return valid;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            CourseCatalog.Warnings.Add("五笔 " + variant + " 版字根图读取失败：" + e.Message);
            return new();
        }
    }
    private static string? FindTeachingFile(string data, string relative)
    {
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        var parent = Directory.GetParent(Path.GetFullPath(data))?.FullName ?? data;
        var paths = new[] {
            Path.Combine(AppContext.BaseDirectory, "Teaching", relative),
            Path.Combine(parent, "Teaching", relative),
            Path.Combine(parent, "app", "Teaching", relative)
        };
        return paths.FirstOrDefault(File.Exists);
    }
}

