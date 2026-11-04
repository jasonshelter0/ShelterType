using System.Text.RegularExpressions;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>Presentation-only labels. Never changes lesson IDs, source text, expected input or saved fingerprints.</summary>
public static class CourseLabels
{
    private static readonly Dictionary<string, (string En, string Fr)> Names = new(StringComparer.Ordinal)
    {
        ["五笔"]=("Wubi","Wubi"), ["英文"]=("English","Anglais"), ["中文"]=("Chinese","Chinois"),
        ["86版"]=("86","86"), ["98版"]=("98","98"), ["单字"]=("Characters","Caractères"), ["词组"]=("Phrases","Mots composés"),
        ["入门"]=("Getting started","Premiers pas"), ["入门指法"]=("Touch-typing basics","Bases de la dactylographie"),
        ["入门字根"]=("Root learning cards","Fiches des radicaux"), ["字根辨识"]=("Root recognition","Reconnaissance des radicaux"), ["字根识记"]=("Root learning cards","Fiches des radicaux"),
        ["一级简码"]=("Level 1 short codes","Codes courts de niveau 1"), ["二级简码"]=("Level 2 short codes","Codes courts de niveau 2"),
        ["常用字"]=("Common characters","Caractères courants"), ["识别码"]=("Identification codes","Codes d’identification"),
        ["双字词组"]=("Two-character phrases","Mots de deux caractères"), ["三字词组"]=("Three-character phrases","Mots de trois caractères"), ["多字词组"]=("Longer phrases","Expressions longues"),
        ["横区"]=("Horizontal strokes","Traits horizontaux"), ["竖区"]=("Vertical strokes","Traits verticaux"), ["撇区"]=("Left-falling strokes","Traits obliques à gauche"), ["捺区"]=("Right-falling strokes","Traits obliques à droite"), ["折区"]=("Turning strokes","Traits tournants"), ["全部键位"]=("All keys","Toutes les touches"),
        ["标准键盘"]=("Main keyboard","Clavier principal"), ["数字键盘"]=("Number pad","Pavé numérique"),
        ["键盘布局(KeyBoard Layout)"]=("Keyboard layout","Disposition du clavier"), ["手指分区练习"]=("Finger zones","Zones des doigts"), ["键位纠错练习"]=("Key accuracy","Précision des touches"),
        ["单词"]=("Words","Vocabulaire"), ["文章"]=("Articles","Textes"), ["专业文章"]=("Specialist articles","Textes spécialisés"), ["综合文章"]=("General articles","Textes généraux"),
        ["格言"]=("Adages","Maximes"), ["散文"]=("Essays","Essais"), ["笑话"]=("Jokes","Histoires drôles"), ["小说"]=("Fiction","Fiction"), ["杂项"]=("Miscellaneous","Divers"), ["诗歌"]=("Poetry","Poésie"), ["程序文本"]=("Code samples","Exemples de code"),
        ["农业"]=("Agriculture","Agriculture"), ["生物"]=("Biology","Biologie"), ["化学"]=("Chemistry","Chimie"), ["化工"]=("Chemical industry","Industrie chimique"), ["计算机"]=("Computing","Informatique"), ["经贸"]=("Trade and economics","Commerce et économie"), ["电子"]=("Electronics","Électronique"), ["法律"]=("Law","Droit"), ["机械"]=("Mechanics","Mécanique"), ["医学"]=("Medicine","Médecine"), ["石油"]=("Petroleum","Pétrole"),
        ["大学四级"]=("CET-4","CET-4"), ["大学六级"]=("CET-6","CET-6"), ["小学词汇"]=("Primary-school vocabulary","Vocabulaire du primaire"), ["初中词汇"]=("Middle-school vocabulary","Vocabulaire du collège"), ["高中词汇"]=("High-school vocabulary","Vocabulaire du lycée"), ["托福"]=("TOEFL","TOEFL"), ["常用单词1000"]=("1,000 common words","1 000 mots courants"),
        ["拼音"]=("Pinyin","Pinyin"), ["单字·HSK"]=("HSK characters","Caractères HSK"), ["HSK词汇"]=("HSK vocabulary","Vocabulaire HSK"), ["HSK_汉语水平考试"]=("HSK","HSK"),
        ["模糊音与方言"]=("Sound contrasts and dialects","Contrastes phonétiques et dialectes"), ["模糊音和地区方言"]=("Sound contrasts and dialects","Contrastes phonétiques et dialectes"),
        ["连音与异读词"]=("Syllable boundaries and variants","Limites syllabiques et variantes"), ["连音词和异读词"]=("Syllable boundaries and variants","Limites syllabiques et variantes"), ["连音词练习"]=("Syllable boundaries","Limites syllabiques"), ["普通话异读词"]=("Pronunciation variants","Variantes de prononciation"),
        ["词语"]=("Vocabulary","Vocabulaire"), ["常用词汇"]=("Common vocabulary","Vocabulaire courant"), ["专业词汇"]=("Specialist vocabulary","Vocabulaire spécialisé"), ["常用词"]=("Common words","Mots courants"),
        ["综合"]=("Review","Révision"), ["其他"]=("Other","Autres"), ["错键巩固"]=("Mistake review","Révision des erreurs"), ["键位"]=("Keys","Touches"),
        ["找到 F 和 J"]=("Find F and J","Repérer F et J"), ["左手基准键"]=("Left-hand home keys","Touches de repos de la main gauche"), ["右手基准键"]=("Right-hand home keys","Touches de repos de la main droite"), ["双手交替与空格"]=("Alternating hands and Space","Alterner les mains et utiliser Espace"),
        ["向上伸指"]=("Reach the upper row","Atteindre la rangée supérieure"), ["向下伸指"]=("Reach the lower row","Atteindre la rangée inférieure"), ["Shift 与大小写"]=("Shift and capital letters","Maj et lettres majuscules"), ["Capital-大写"]=("Capital letters","Lettres majuscules"), ["英文标点符号"]=("English punctuation","Ponctuation anglaise"),
        ["单韵母与 ü"]=("Simple finals and ü","Voyelles simples et ü"), ["声母"]=("Initial consonants","Consonnes initiales"), ["常用复韵母"]=("Common compound finals","Finales composées courantes"), ["前后鼻音"]=("Final n and ng","Finales n et ng"), ["声调与键盘拼写"]=("Tones and keyboard spelling","Tons et saisie au clavier"), ["隔音符号"]=("Syllable separator","Séparateur syllabique")
    };
    private static readonly HashSet<string> Structural = new(StringComparer.Ordinal)
    { "五笔", "英文", "中文", "文章", "专业文章", "综合文章", "词语", "单词", "标准键盘", "数字键盘", "格言", "散文", "笑话", "小说", "杂项", "诗歌", "程序文本", "农业", "生物", "化学", "化工", "计算机", "经贸", "电子", "法律", "机械", "医学", "石油" };
    private static string Word(string raw) => Names.TryGetValue(raw, out var value) ? UiText.T(raw, value.En, value.Fr) : raw;
    private static string Number(string raw) => raw switch { "一"=>"1", "二"=>"2", "三"=>"3", "四"=>"4", "五"=>"5", "六"=>"6", "七"=>"7", "八"=>"8", "九"=>"9", "十"=>"10", "十一"=>"11", "十二"=>"12", "十三"=>"13", "十四"=>"14", _=>raw };

    public static string Title(Course course)
    {
        var parents = course.Group.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var parts = course.Title.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && (parents.Contains(parts[0]) || Structural.Contains(parts[0]))) parts.RemoveAt(0);
        return CleanTitle(string.Join(" · ", parts));
    }
    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = raw.Replace("（重构课程）", "").Replace("（原版图）", "").Replace("（原版字根）", "").Trim();
        var parts = text.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && Structural.Contains(parts[0])) parts.RemoveAt(0);
        if (parts.Count > 1 && parts[^1] == "编码") parts.RemoveAt(parts.Count - 1);
        text = string.Join(" ", parts.Select(TranslateTitlePart));
        return text;
    }
    private static string TranslateTitlePart(string raw)
    {
        if (UiText.Language == "zh-CN") return raw;
        if (Names.ContainsKey(raw)) return Word(raw);
        var root = Regex.Match(raw, @"^(横区|竖区|撇区|捺区|折区|全部键位|综合)(字根识记|字根辨识)$");
        if (root.Success) return Word(root.Groups[1].Value) + " — " + Word(root.Groups[2].Value);
        var lesson = Regex.Match(raw, @"^(?:数字键盘课程|键位课程)([一二三四五六七八九十\d]+)\s*[:：]\s*(.*)$");
        if (lesson.Success) return UiText.Format("课程 {0}：{1}", "Lesson {0}: {1}", "Leçon {0} : {1}", Number(lesson.Groups[1].Value), Word(lesson.Groups[2].Value));
        var hsk = Regex.Match(raw, @"^HSK([一二三四])级字$");
        if (hsk.Success) return UiText.Format("HSK{0}级字", "HSK {0} characters", "Caractères HSK {0}", Number(hsk.Groups[1].Value));
        var sound = Regex.Match(raw, @"^(模糊音|音节练习)[:：]\s*(.*)$");
        if (sound.Success) return (sound.Groups[1].Value == "模糊音" ? UiText.T("模糊音：", "Sound contrasts: ", "Contrastes phonétiques : ") : UiText.T("音节练习：", "Syllables: ", "Syllabes : "))
            + TranslateSound(sound.Groups[2].Value);
        return raw;
    }
    private static string TranslateSound(string raw) => raw switch
    {
        "双音节" => UiText.T(raw,"Two syllables","Deux syllabes"), "轻声" => UiText.T(raw,"Neutral tone","Ton neutre"), "儿化韵" => UiText.T(raw,"Erhua endings","Finales erhua"),
        _ => raw.Replace("，", ", ").Replace("和", UiText.T("和", " and ", " et "))
    };
    public static string Group(string raw) => string.Join(" / ", (raw ?? "").Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(segment =>
    {
        var range = Regex.Match(segment, @"^第\s*(\d+)\s*[—–-]\s*(\d+)\s*课$");
        if (range.Success) return UiText.Format("第 {0}—{1} 课", "Lessons {0}–{1}", "Cours {0}–{1}", range.Groups[1].Value, range.Groups[2].Value);
        return Word(segment).Replace("·", " ");
    }));
    public static string Path(Course course) => string.Join(" / ", new[] { Word(course.Category), Group(course.Group), Title(course) }.Where(s => s.Length > 0));
    public static string ContextTitle(Course course)
    {
        var fragments = course.Group.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(s => !Regex.IsMatch(s, @"^第\s*\d+.*课$")).ToArray();
        var last = fragments.Contains("错键巩固") ? "错键巩固" : fragments.LastOrDefault() ?? course.Variant;
        var prefix = course.Category == "五笔" ? UiText.Mode(course.Category + course.Variant) : Word(course.Category);
        var context = string.Join(" / ", new[] { prefix, Group(last) }.Where(s => s.Length > 0).Distinct());
        return Title(course) + (context.Length == 0 ? "" : UiText.T("（", " (", " (") + context + UiText.T("）", ")", ")"));
    }
    public static string Prompt(string raw)
    {
        var root = Regex.Match(raw, @"^字根\s+(\d+)$");
        if (root.Success) return UiText.Format("字根 {0}", "Root {0}", "Radical {0}", root.Groups[1].Value);
        var card = Regex.Match(raw, @"^(横区|竖区|撇区|捺区|折区|全部键位)\s*·\s*字根识记$");
        return card.Success ? Word(card.Groups[1].Value) + " " + Word("字根识记") : raw;
    }
    private static readonly Dictionary<string, (string En, string Fr)> Hints = new(StringComparer.Ordinal)
    {
        ["按屏幕文字跟打，空格也需输入"]=("Type the displayed text, including spaces.","Saisissez le texte affiché, espaces compris."),
        ["输入单词后按空格"]=("Type the word, then press Space.","Saisissez le mot, puis appuyez sur Espace."),
        ["按原文跟打；本段完成后自动继续"]=("Copy the text. The next section appears automatically.","Recopiez le texte. La section suivante apparaît automatiquement."),
        ["用系统中文输入法输入；提交文字后自动继续"]=("Use your Chinese input method. Continue after committing the text.","Utilisez votre méthode de saisie chinoise. La suite apparaît après validation du texte."),
        ["用系统中文输入法输入，标点需与原文一致"]=("Use your Chinese input method and match the punctuation.","Utilisez votre méthode de saisie chinoise et respectez la ponctuation."),
        ["两手食指轻放 F、J 的凸点；拇指负责空格。按完回到基准键。"]=("Rest your index fingers on the bumps of F and J. Use a thumb for Space, then return to the home keys.","Posez les index sur les repères de F et J. Utilisez un pouce pour Espace, puis revenez aux touches de repos."),
        ["左手小指 A、无名指 S、中指 D、食指 F；每次按键后回到原位。"]=("Left hand: little finger on A, ring on S, middle on D, index on F. Return after each keystroke.","Main gauche : auriculaire sur A, annulaire sur S, majeur sur D, index sur F. Revenez après chaque frappe."),
        ["右手食指 J、中指 K、无名指 L、小指分号 ;；分号使用英文半角。"]=("Right hand: index on J, middle on K, ring on L, little finger on the ASCII semicolon ;.","Main droite : index sur J, majeur sur K, annulaire sur L, auriculaire sur le point-virgule ASCII ;."),
        ["双手停在 ASDF 和 JKL;；拇指按空格。先保持准确，再逐渐加快。"]=("Rest on ASDF and JKL;. Use a thumb for Space. Build accuracy before speed.","Placez les mains sur ASDF et JKL;. Utilisez un pouce pour Espace. Privilégiez la précision avant la vitesse."),
        ["左手 Q/A、W/S、E/D、R与T/F；右手 Y与U/J、I/K、O/L、P/分号。按后回位。"]=("Reach from A to Q, S to W, D to E, F to R/T; from J to Y/U, K to I, L to O, and ; to P. Return home.","Depuis A, atteignez Q ; S → W, D → E, F → R/T ; J → Y/U, K → I, L → O et ; → P. Revenez au repos."),
        ["左手 Z/A、X/S、C/D、V与B/F；右手 N与M/J、逗号/K、句号/L、斜线/小指。"]=("Reach from A to Z, S to X, D to C, F to V/B; from J to N/M, K to comma, L to period. Use the right little finger for slash.","Depuis A, atteignez Z ; S → X, D → C, F → V/B ; J → N/M, K → virgule, L → point. L’auriculaire droit frappe la barre oblique."),
        ["大写字母用另一只手的小指按住 Shift。按左侧字母时用右 Shift，反之亦然。"]=("For capitals, hold Shift with the opposite little finger: right Shift for left-hand letters, and vice versa.","Pour les majuscules, maintenez Maj avec l’auriculaire opposé : Maj droit pour les lettres de la main gauche, et inversement."),
        ["输入不带声调的拼音，ü 使用键盘 v；每项结束按空格。"]=("Type pinyin without tone marks; use v for ü. Press Space after each item.","Saisissez le pinyin sans tons ; utilisez v pour ü. Appuyez sur Espace après chaque élément."),
        ["按显示的声母输入小写字母；zh、ch、sh 都需要两个字母。"]=("Type the displayed initial in lowercase. zh, ch and sh each use two letters.","Saisissez l’initiale affichée en minuscules. zh, ch et sh comportent chacun deux lettres."),
        ["按顺序输入韵母中的字母；这里的 ü 保留写法，键盘上使用 v。"]=("Type the final in order. The displayed ü is entered as v on the keyboard.","Saisissez les lettres de la finale dans l’ordre. Le ü affiché se saisit avec v au clavier."),
        ["前鼻音以 n 结束，后鼻音以 ng 结束；注意最后的 g。"]=("Distinguish endings in n from endings in ng. Watch the final g.","Distinguez les finales en n de celles en ng. Attention au g final."),
        ["本软件练习拼音键盘拼写，不输入声调符号或声调数字；按提示保留字母。"]=("Practise pinyin spelling without tone marks or tone numbers. Type only the indicated letters.","Entraînez-vous à saisir le pinyin sans signes ni chiffres de ton. Tapez uniquement les lettres indiquées."),
        ["音节边界需要时保留英文单引号 '；这是隔音符号，不是声调。"]=("Keep the ASCII apostrophe ' where a syllable separator is needed. It is not a tone mark.","Conservez l’apostrophe ASCII ' lorsqu’elle sépare des syllabes. Ce n’est pas un signe de ton.")
    };
    public static string Hint(string raw)
    {
        if (UiText.Language == "zh-CN" || string.IsNullOrWhiteSpace(raw)) return raw;
        if (Hints.TryGetValue(raw, out var exact)) return UiText.T(raw, exact.En, exact.Fr);
        var typing = Regex.Match(raw, @"^(.*?)\s{2,}输入 ([a-z']+)$");
        if (typing.Success && Hints.ContainsKey(typing.Groups[1].Value)) return Hint(typing.Groups[1].Value) + UiText.Format(" 输入 {0}", " Type {0}", " Saisissez {0}", typing.Groups[2].Value);
        var code = Regex.Match(raw, @"^([a-z']+)\s+空格确认$");
        if (code.Success) return UiText.Format("{0} 空格确认", "{0} — Space to confirm", "{0} — Espace pour valider", code.Groups[1].Value);
        const string phoneticSuffix = "空格确认；ü 输入 v，保留隔音符号 '";
        if (raw.EndsWith(phoneticSuffix, StringComparison.Ordinal)) return raw[..^phoneticSuffix.Length].Replace("·", "—") + UiText.T(phoneticSuffix, "Space to confirm; use v for ü and keep the apostrophe '", "Espace pour valider ; utilisez v pour ü et conservez l’apostrophe '");
        var card = Regex.Match(raw, @"^原版字根卡片（保留字母标注）：观察字根，按 ([A-Y]) 键，再按空格。$");
        if (card.Success) return UiText.Format("", "Original root card with key label: examine the roots, press {0}, then Space.", "Fiche d’origine avec la lettre : observez les radicaux, appuyez sur {0}, puis sur Espace.", card.Groups[1].Value);
        var root = Regex.Match(raw, @"^识别这个原版字根，按所属的 ([A-Y]) 键，再按空格。可关闭提示独立练习。$");
        if (root.Success) return UiText.Format("", "Identify this root, press its key {0}, then Space. Hide hints to practise from memory.", "Identifiez ce radical, appuyez sur sa touche {0}, puis sur Espace. Masquez les indications pour vous exercer de mémoire.", root.Groups[1].Value);
        var invalidCode = Regex.Match(raw, @"^(\d+)版 (.*?)：跳过 (\d+) 个未能验证编码的项目。$");
        if (invalidCode.Success) return UiText.Format("", "Wubi {0}, {1}: skipped {2} entries with unverified codes.", "Wubi {0}, {1} : {2} entrées aux codes non vérifiés ont été ignorées.", invalidCode.Groups[1].Value, CleanTitle(invalidCode.Groups[2].Value), invalidCode.Groups[3].Value);
        var invalidPinyin = Regex.Match(raw, @"^(.*?)：跳过 (\d+) 条格式不完整的原版拼音记录。$");
        if (invalidPinyin.Success) return UiText.Format("", "{0}: skipped {1} malformed original pinyin entries.", "{0} : {1} entrées pinyin d’origine mal formées ont été ignorées.", CleanTitle(invalidPinyin.Groups[1].Value), invalidPinyin.Groups[2].Value);
        return raw;
    }
}


