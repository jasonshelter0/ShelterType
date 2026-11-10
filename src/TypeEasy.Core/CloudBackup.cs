using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
namespace TypeEasy.Core;

public sealed record CloudBackup(string Name,long Bytes)
{
    public string DisplayName=>DateTime.TryParseExact(Name.Substring(12,16),"yyyyMMdd'T'HHmmss'Z'",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var time)?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+"   "+Math.Max(1,Bytes/1024)+" KB":Name;
}
public sealed class NutstoreClient : IDisposable
{
    private readonly HttpClient http;
    private static readonly Uri Folder=new("https://dav.jianguoyun.com/dav/ShelterType/");
    public NutstoreClient(string account,string password,HttpMessageHandler? handler=null){
        if(string.IsNullOrWhiteSpace(account)||account.Contains(':')||string.IsNullOrEmpty(password))throw new ArgumentException("Account and application password are required.");
        http=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Trim()+":"+password)));
    }
    private static void Check(HttpResponseMessage response){if(!response.IsSuccessStatusCode)throw new IOException("Nutstore HTTP "+(int)response.StatusCode+". Check the app password, available space and connection.");}
    public async Task<List<CloudBackup>> ListAsync(CancellationToken token){
        using var request=new HttpRequestMessage(new HttpMethod("PROPFIND"),Folder);request.Headers.Add("Depth","1");
        request.Content=new StringContent("<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>",Encoding.UTF8,"application/xml");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);if(response.StatusCode==HttpStatusCode.NotFound)return new();Check(response);
        var bytes=await ReadLimited(response,4*1024*1024,token);using var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});var xml=XDocument.Load(reader);XNamespace d="DAV:";
        return xml.Descendants(d+"response").Select(x=>{
            var href=(string?)x.Element(d+"href")??"";var uri=new Uri(Folder,href);var name=Uri.UnescapeDataString(uri.Segments.Last());
            return uri.Scheme==Folder.Scheme&&uri.Host==Folder.Host&&uri.AbsolutePath==Folder.AbsolutePath+name&&ValidName(name)?new CloudBackup(name,long.TryParse((string?)x.Descendants(d+"getcontentlength").FirstOrDefault(),out var n)?n:0):null;
        }).Where(x=>x!=null).Cast<CloudBackup>().OrderByDescending(x=>x.Name,StringComparer.Ordinal).ToList();
    }
    public static bool ValidName(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,@"^ShelterType-\d{8}T\d{6}Z-[a-f0-9]{32}\.zip$");
    public async Task UploadAsync(byte[] archive,CancellationToken token){
        if((await ListAsync(token)).Count>=200)throw new IOException("Keep fewer than 200 cloud backups; remove older ones in Nutstore first.");
        if(archive.Length>ProfileBackup.MaxArchive)throw new IOException("Backup exceeds 50 MB.");
        using(var request=new HttpRequestMessage(new HttpMethod("MKCOL"),Folder))using(var response=await http.SendAsync(request,token)){if(response.StatusCode!=HttpStatusCode.MethodNotAllowed)Check(response);}
        var name="ShelterType-"+DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")+"-"+Guid.NewGuid().ToString("N")+".zip";
        using var put=new HttpRequestMessage(HttpMethod.Put,new Uri(Folder,name)){Content=new ByteArrayContent(archive)};put.Headers.TryAddWithoutValidation("If-None-Match","*");
        using var uploaded=await http.SendAsync(put,token);Check(uploaded);
    }
    public async Task<byte[]> DownloadAsync(string name,CancellationToken token){if(!ValidName(name))throw new ArgumentException("Invalid backup name.");using var response=await http.GetAsync(new Uri(Folder,name),HttpCompletionOption.ResponseHeadersRead,token);Check(response);return await ReadLimited(response,ProfileBackup.MaxArchive,token);}
    private static async Task<byte[]> ReadLimited(HttpResponseMessage response,long limit,CancellationToken token){using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[81920];int n;while((n=await input.ReadAsync(buffer,token))>0){if(output.Length+n>limit)throw new IOException("Cloud response exceeds size limit.");output.Write(buffer,0,n);}return output.ToArray();}
    public void Dispose()=>http.Dispose();
}

public static class ProfileBackup
{
    public const int MaxArchive=50*1024*1024;
    private const long MaxExpanded=200*1024*1024;
    private static readonly string[] Areas={"Progress","Analytics","Heatmaps","Shuffled","PersonalPractice","Reviews","Articles"};
    private sealed record Manifest(int Version,bool IncludeChats,DateTimeOffset Created);
    public static bool Allowed(string path,bool chats){var parts=path.Split('/');if(parts.Length!=2||parts.Any(x=>x.Length==0||x is "." or ".."||x.IndexOfAny(Path.GetInvalidFileNameChars())>=0||x.Contains('\\')||x.Contains(':')))return false;
        if(parts[1].EndsWith(".")||parts[1].EndsWith(" ")||System.Text.RegularExpressions.Regex.IsMatch(parts[1],@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return false;
        if(path=="Dictionary/favorites.json")return true;
        if(chats&&parts[0]=="AI")return System.Text.RegularExpressions.Regex.IsMatch(parts[1],@"^chat-[a-f0-9]{32}\.json$");
        return Areas.Contains(parts[0])&&(parts[1].EndsWith(".json",StringComparison.Ordinal)||parts[0]=="Articles"&&parts[1].EndsWith(".txt",StringComparison.Ordinal));}
    private static IEnumerable<string> Files(string root,bool chats){if(!Directory.Exists(root))yield break;foreach(var area in Areas.Concat(new[]{"Dictionary","AI"})){var folder=Path.Combine(root,area);if(!Directory.Exists(folder))continue;if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profile folders are not supported.");foreach(var file in Directory.EnumerateFiles(folder)){var relative=area+"/"+Path.GetFileName(file);if(!Allowed(relative,chats))continue;if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profile files are not supported.");yield return relative;}}}
    public static byte[] Create(string root,bool chats){using var memory=new MemoryStream();long total=0;using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){
        using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(new Manifest(1,chats,DateTimeOffset.UtcNow)));
        var files=Files(root,chats).ToArray();if(files.Length>5000)throw new IOException("Too many backup files.");
        foreach(var file in files){var path=Path.Combine(root,file);total+=new FileInfo(path).Length;if(total>MaxExpanded)throw new IOException("Profile exceeds backup size limit.");using var input=File.OpenRead(path);using var output=zip.CreateEntry(file,CompressionLevel.Optimal).Open();input.CopyTo(output);}}
        if(memory.Length>MaxArchive)throw new IOException("Backup exceeds 50 MB.");return memory.ToArray();}
    private static (Dictionary<string,byte[]> Files,bool Chats) Read(byte[] bytes){if(bytes.Length>MaxArchive)throw new InvalidDataException("Backup exceeds 50 MB.");using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);if(zip.Entries.Count>5001)throw new InvalidDataException("Too many backup files.");if(zip.Entries.Count(e=>e.FullName=="manifest.json")!=1)throw new InvalidDataException("Invalid manifest count.");var manifest=zip.GetEntry("manifest.json")??throw new InvalidDataException("Not a ShelterType backup.");if(manifest.Length>4096)throw new InvalidDataException("Invalid manifest.");Manifest value;using(var stream=manifest.Open())value=JsonSerializer.Deserialize<Manifest>(stream)??throw new InvalidDataException("Invalid manifest.");if(value.Version!=1)throw new InvalidDataException("Unsupported backup version.");
        var result=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);long total=0;foreach(var entry in zip.Entries){if(entry.FullName=="manifest.json")continue;if(!Allowed(entry.FullName,value.IncludeChats)||!result.TryAdd(entry.FullName,Array.Empty<byte>()))throw new InvalidDataException("Unsafe or duplicate backup path.");total+=entry.Length;if(total>MaxExpanded)throw new InvalidDataException("Expanded backup is too large.");using var input=entry.Open();using var output=new MemoryStream();var buffer=new byte[81920];int n;while((n=input.Read(buffer))>0){if(output.Length+n>entry.Length)throw new InvalidDataException("Invalid entry length.");output.Write(buffer,0,n);}var content=output.ToArray();if(entry.FullName.EndsWith(".json",StringComparison.Ordinal)){using var doc=JsonDocument.Parse(content);}result[entry.FullName]=content;}return(result,value.IncludeChats);}
    public static void Stage(string root,byte[] bytes){_ = Read(bytes);var folder=Path.Combine(root,"CloudBackup");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"pending.zip");File.WriteAllBytes(path+".tmp",bytes);File.Move(path+".tmp",path,true);}
    // Called after acquiring the app's exclusive profile lock, before loading any records.
    public static void ApplyPending(string root){var folder=Path.Combine(root,"CloudBackup");var pending=Path.Combine(folder,"pending.zip");if(!File.Exists(pending))return;var incoming=Read(File.ReadAllBytes(pending));var before=Create(root,incoming.Chats);var backup=Path.Combine(folder,"before-restore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".zip");File.WriteAllBytes(backup,before);
        try{Replace(root,incoming.Files,incoming.Chats);File.Delete(pending);}catch{Replace(root,Read(before).Files,incoming.Chats);throw;}}
    private static void Replace(string root,Dictionary<string,byte[]> files,bool chats){var old=Files(root,chats).ToArray();foreach(var file in old)if(!files.ContainsKey(file))File.Delete(Path.Combine(root,file));foreach(var pair in files){var path=Path.Combine(root,pair.Key);var parent=Path.GetDirectoryName(path)!;Directory.CreateDirectory(parent);if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0||File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked restore destination.");File.WriteAllBytes(path+".restore-tmp",pair.Value);File.Move(path+".restore-tmp",path,true);}}
}