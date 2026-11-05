using System.Text.Json;
namespace TypeEasy.Core;

public static class ArticleLibrary
{
    public static bool IsArticle(Course c)=>c.Category is "英文" or "中文" && (c.Variant.Contains("文章")||c.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase));
    public static HashSet<string> Hidden(string root){var p=Path.Combine(root,"Articles","hidden.json");return File.Exists(p)?JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(p))??new():new();}
    public static List<Course> Apply(IEnumerable<Course> courses,string root)
    {
        var hidden=Hidden(root);var list=courses.Where(c=>!hidden.Contains(c.Id)).ToList();var folder=Path.Combine(root,"Articles");
        if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.txt")){
            if(new FileInfo(file).Length>1024*1024)continue;
            var text=File.ReadAllText(file).Replace("\r\n","\n").Replace('\r','\n').Replace('\t',' ');if(string.IsNullOrWhiteSpace(text)||text.Length>200000)continue;
            var id="user-article:"+Path.GetFileName(file);if(hidden.Contains(id))continue;
            list.Add(new Course{Id=id,Title=Path.GetFileNameWithoutExtension(file),Category=text.Any(c=>c>='\u4e00'&&c<='\u9fff')?"中文":"英文",Variant="文章",Group="我的文章",Origin="user",Items=new(){new ExerciseItem{Expected=text,Prompt=text}}});
        }return list;
    }
    public static void SetHidden(string root,string id,bool hidden){var all=Hidden(root);if(hidden)all.Add(id);else all.Remove(id);var folder=Path.Combine(root,"Articles");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"hidden.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(all));File.Move(path+".tmp",path,true);}
}
