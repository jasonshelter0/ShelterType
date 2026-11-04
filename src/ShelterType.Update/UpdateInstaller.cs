using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ShelterType.Update;

public static class UpdateInstaller
{
    /// <summary>Starts the local installer. The caller must then close the app to flush and release its profile.</summary>
    public static void Start(PreparedUpdate update, string installDirectory, int parentPid, DateTime parentStartUtc, string language = "zh-CN")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This update package is for Windows.");
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        var cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(update.Directory));
        if (cache.Equals(install, StringComparison.OrdinalIgnoreCase) || cache.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Update cache must be outside the application folder.");
        if (!File.Exists(Path.Combine(install, "ShelterType.exe"))) throw new IOException("The application folder is invalid.");
        RejectReparseAncestors(install); RejectReparseAncestors(cache);
        var probe = Path.Combine(install, ".sheltertype-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(probe);
        if (!Path.GetFullPath(update.PackagePath).Equals(Path.Combine(cache, UpdateClient.PackageName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged update package path is invalid.");
        var script = Path.Combine(cache, "InstallUpdate.ps1");
        WriteInstallerScript(script);
        var job = Path.Combine(cache, "install-job.json");
        File.WriteAllText(job, JsonSerializer.Serialize(new
        {
            InstallDirectory = install, PackagePath = update.PackagePath, Sha256 = update.Sha256,
            ParentPid = parentPid, ParentStartUtcTicks = parentStartUtc.ToUniversalTime().Ticks,
            Version = update.Release.Version.ToString(), Language = language, Restart = true,
            FailureIntro = language == "fr" ? "La mise à jour n’a pas pu être installée." : language == "en" ? "The update could not be installed." : "未能安装更新。",
            KeptMessage = language == "fr" ? "La version précédente a été conservée." : language == "en" ? "The previous version was kept." : "已保留原版本。",
            RecoveryMessage = language == "fr" ? "Certains fichiers n’ont pas pu être restaurés. Les sauvegardes sont dans le dossier de mise à jour." : language == "en" ? "Some files could not be restored. Backups are available in the update folder." : "部分文件无法恢复，可从更新目录中的备份恢复。",
            LogLabel = language == "fr" ? "Journal et sauvegardes :" : language == "en" ? "Log and backups:" : "日志与备份："
        }));
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = cache
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-JobPath", job })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the update installer.");
    }

    public static void WriteInstallerScript(string path)
    {
        using var source = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("ShelterType.Update.InstallUpdate.ps1")
            ?? throw new InvalidOperationException("The local update installer is missing.");
        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    public static void RejectReparseAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Updates are not supported in linked application or cache folders.");
    }
}
