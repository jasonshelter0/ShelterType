using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ShelterType.Update;

namespace TypeEasy.Windows;

internal static class DictionaryMigration
{
    // Only remove original glyph files whose bytes match the validated replacement pack.
    public static void CleanLegacy(string root)
    {
        try
        {
            var folder=Path.Combine(root,"Dictionary","Glyphs");
            var pack=Path.Combine(root,"Dictionary","Glyphs.zip");
            if(!Directory.Exists(folder)||!File.Exists(pack))return;
            UpdateInstaller.RejectReparseAncestors(folder);
            if((File.GetAttributes(pack)&FileAttributes.ReparsePoint)!=0)return;
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"required-resources.json")));
            var expected=manifest.RootElement.GetProperty("GlyphsSha256").GetString();
            using var input=File.OpenRead(pack);
            if(!string.Equals(Convert.ToHexString(SHA256.HashData(input)),expected,StringComparison.OrdinalIgnoreCase))return;
            input.Position=0;
            using var archive=new ZipArchive(input,ZipArchiveMode.Read);
            foreach(var entry in archive.Entries)
            {
                if(entry.FullName.Length!=5||!entry.FullName.EndsWith(".png",StringComparison.Ordinal)||"\\/:*?\"<>|".Contains(entry.FullName[0]))continue;
                var path=Path.Combine(folder,entry.FullName);
                if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                try
                {
                    bool same;
                    using(var old=File.OpenRead(path))
                    using(var replacement=entry.Open())
                        same=old.Length==entry.Length&&SHA256.HashData(old).SequenceEqual(SHA256.HashData(replacement));
                    if(same)File.Delete(path);
                }
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
            }
            // Preserve any customized or unknown files, and never recurse.
            if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder,false);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or KeyNotFoundException) { }
    }
}
