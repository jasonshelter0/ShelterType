using System.Globalization;

namespace TypeEasy.Windows;

/// <summary>Small built-in UI translations. Course content and persisted identifiers stay unchanged.</summary>
public static class UiText
{
    private static string language = "zh-CN";
    public static string Language
    {
        get => language;
        set
        {
            language = value?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) == true ? "fr"
                : value?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "zh-CN";
            CultureInfo.CurrentCulture = Culture;
            CultureInfo.CurrentUICulture = Culture;
        }
    }
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(language == "en" ? "en-US" : language == "fr" ? "fr-FR" : "zh-CN");
    public static string T(string zh, string en, string fr) => language == "en" ? en : language == "fr" ? fr : zh;
    public static string Format(string zh, string en, string fr, params object?[] values) => string.Format(Culture, T(zh, en, fr), values);
    public static string Mode(string raw) => raw switch
    {
        "english" => Mode("英文"),
        "chinese" => Mode("中文输入"),
        "wubi86" => Mode("五笔86版"),
        "wubi98" => Mode("五笔98版"),
        "五笔" => T("五笔", "Wubi", "Wubi"),
        "英文" => T("英文", "English", "Anglais"),
        "中文" or "中文输入" => T(raw, "Chinese", "Chinois"),
        "拼音编码" => T("拼音编码", "Pinyin", "Pinyin"),
        "数字键盘" => T("数字键盘", "Number pad", "Pavé numérique"),
        _ when raw.StartsWith("五笔", StringComparison.Ordinal) => T(raw, "Wubi " + raw[2..].Replace("版", ""), "Wubi " + raw[2..].Replace("版", "")),
        _ => raw
    };
    public static string Key(string raw) => raw switch
    {
        " " or "空格" or "Space" => T("空格", "Space", "Espace"),
        "\n" or "\r" or "回车" => T("回车", "Enter", "Entrée"),
        "\t" => "Tab",
        "\b" => T("退格", "Backspace", "Retour arrière"),
        "" => "∅",
        _ => raw.Replace("\r", "↵").Replace("\n", "↵").Replace("\t", "⇥").Replace(" ", "␣")
    };
}
