namespace TypeEasy.Core;

/// <summary>Hierarchy metadata is deliberately independent of exercise content and saved progress.</summary>
internal static class CourseGrouping
{
    public static string For(Course course)
    {
        if (!string.IsNullOrEmpty(course.Group)) return course.Group;
        if (course.Category == "五笔")
            return course.Variant + "/" + (course.Title.Contains("词组") ? "词组" : "单字");
        var path = course.Id.Split('#')[0].Split('/');
        if (course.Category == "英文")
        {
            if (course.Id.Contains("/t_character/standard/")) return "标准键盘";
            if (course.Id.Contains("/t_character/numpad/")) return "数字键盘";
            if (course.Id.Contains("/t_word/")) return "单词";
        }
        if (course.Category == "中文" && course.Id.Contains("/t_word/"))
            return "词语/" + (path.Length > 3 ? path[2] : "综合");
        if (course.Id.Contains("/t_article/"))
            return "文章/" + Translate(path.Length > 3 ? path[2] : "综合");
        if (course.Id.Contains("/professional/"))
            return "专业文章/" + Translate(path.Length > 3 ? path[2] : "综合");
        if (course.Id.Contains("/e_general/")) return "综合文章";
        return string.IsNullOrWhiteSpace(course.Variant) ? "其他" : course.Variant;
    }

    public static void Assign(List<Course> courses)
    {
        foreach (var course in courses) course.Group = For(course);
        // Keep every terminal list short without modifying IDs, exercises or saved fingerprints.
        foreach (var group in courses.GroupBy(c => c.Category + "\0" + c.Group).ToArray())
        {
            var entries = group.ToList();
            if (entries.Count <= 30) continue;
            for (var i = 0; i < entries.Count; i++)
            {
                var begin = i / 25 * 25 + 1;
                var end = Math.Min(begin + 24, entries.Count);
                entries[i].Group += $"/第 {begin:D2}—{end:D2} 课";
            }
        }
    }

    private static string Translate(string name) => name.ToLowerInvariant() switch
    {
        "adage" => "格言", "essay" => "散文", "joke" => "笑话", "novel" => "小说",
        "other" => "杂项", "poesy" => "诗歌", "program" => "程序文本",
        "agriculture" => "农业", "biology" => "生物", "chemistry" => "化学", "computer" => "计算机",
        "economic and  trade" => "经贸", "electron" => "电子", "law" => "法律", "machinery" => "机械",
        "medical" => "医学", "petroleum" => "石油", _ => name
    };
}
