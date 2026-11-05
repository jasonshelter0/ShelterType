using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TypeEasy.Core;

/// <summary>Persist the actual order, so reopening a shuffled lesson resumes that same round.</summary>
public static class ShuffledPractice
{
    public static bool CanShuffle(Course course) => course.Items.Count>1 &&
        !course.Variant.Contains("文章") && !course.Id.Contains("/e_general/") &&
        course.Items.All(i=>i.Expected.Length<=80) &&
        (course.Category=="五笔" || course.InputMode=="pinyin" || course.Id.Contains("/t_word/"));

    public static Course Create(Course source, string directory)
    {
        if(!CanShuffle(source))throw new InvalidOperationException("This lesson cannot be shuffled.");
        var copy=JsonSerializer.Deserialize<Course>(JsonSerializer.Serialize(source))!;
        copy.BaseCourseId=source.BaseCourseId.Length>0?source.BaseCourseId:source.Id;
        copy.Id=source.Id.StartsWith("shuffle:")?source.Id[8..]:source.Id;
        copy.Group=source.Group;
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(copy.Items));
        if(copy.Items.Select(i=>i.Expected).SequenceEqual(source.Items.Select(i=>i.Expected)))
        {var first=copy.Items[0];copy.Items.RemoveAt(0);copy.Items.Add(first);}
        Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(copy.BaseCourseId)))+".json");
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(copy));File.Move(file+".tmp",file,true);
        return copy;
    }
    public static IEnumerable<Course> Load(string directory, IReadOnlyCollection<Course> sources,string? progressDirectory=null)
    {
        if(!Directory.Exists(directory))yield break;
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            Course? course=null;
            try{course=JsonSerializer.Deserialize<Course>(File.ReadAllText(file));}catch(JsonException){}
            if(course is not null && course.Items.Count>0 && sources.Any(c=>c.Id==course.Id || c.Id==course.BaseCourseId)){var original=sources.First(c=>c.Id==course.Id||c.Id==course.BaseCourseId);var old=JsonSerializer.Deserialize<Course>(JsonSerializer.Serialize(course))!;course.Id=original.Id;course.Group=original.Group;
                if(old.Id!=course.Id && progressDirectory is not null){PracticeSession.MigrateCourseIdentity(old,course,progressDirectory);File.WriteAllText(file+".tmp",JsonSerializer.Serialize(course));File.Move(file+".tmp",file,true);}
                yield return course;}
        }
    }
}
