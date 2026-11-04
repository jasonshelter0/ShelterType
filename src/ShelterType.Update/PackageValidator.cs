using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ShelterType.Update;

/// <summary>The ZIP is flat-rooted and cannot carry personal/imported data, links or ambiguous Windows paths.</summary>
public static class PackageValidator
{
    public const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        { "UserData", "TestData", ".git", ".sheltertype-update.lock" };

    public static IReadOnlyList<string> Validate(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 20000) throw new InvalidDataException("Update package contains too many files.");
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var isDirectory = name.EndsWith('/');
            ValidateRelativePath(name.TrimEnd('/'));
            if (!names.Add(name.TrimEnd('/'))) throw new InvalidDataException("Duplicate update package path.");
            var attributes = unchecked((uint)entry.ExternalAttributes);
            var unixType = (attributes >> 16) & 0xf000;
            if ((attributes & 0x400) != 0 || unixType is not (0 or 0x8000 or 0x4000))
                throw new InvalidDataException("Update package links and special files are not allowed.");
            if (entry.Length < 0 || entry.Length > MaximumExpandedBytes - expanded) throw new InvalidDataException("Expanded update package is too large.");
            expanded += entry.Length;
            if (!isDirectory) files.Add(name);
        }
        foreach (var file in files)
        {
            var parts = file.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (files.Contains(string.Join('/', parts.Take(i)))) throw new InvalidDataException("Update package path is both a file and a directory.");
        }
        foreach (var required in new[] { "ShelterType.exe", "ShelterType.dll", "ShelterType.runtimeconfig.json" })
            if (!files.Contains(required)) throw new InvalidDataException("Update package is missing " + required + ".");
        return files.ToArray();
    }

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 220 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
            throw new InvalidDataException("Unsafe update package path.");
        var parts = path.Split('/');
        if (ProtectedNames.Contains(parts[0])) throw new InvalidDataException("Updates cannot replace personal data.");
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Unsafe update package path.");
        }
    }
}
