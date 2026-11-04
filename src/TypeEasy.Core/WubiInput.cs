using System.Text.Json;

namespace TypeEasy.Core;

public static class WubiInput
{
    public static Course ForCourse(Course source)
    {
        if(source.Category!="五笔"||source.InputMode=="wubi-ime"||source.Items.Count==0||
            source.Items.Any(i=>i.ImagePath.Length>0||!i.Prompt.Any(c=>c is >= '\u3400' and <= '\u9fff')||
                i.Expected.Trim().Length==0||i.Expected.Trim().Any(c=>c is < 'a' or > 'z')))return source;
        var course=JsonSerializer.Deserialize<Course>(JsonSerializer.Serialize(source))!;
        course.Id=source.Id+"/ime-v1";course.BaseCourseId=source.Id;course.InputMode="wubi-ime";
        foreach(var item in course.Items){item.KeySequence=item.Expected.Trim();item.Expected=item.Prompt;item.Hint=item.KeySequence;}
        return course;
    }
}
