using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShelterType.Update;

internal static class Program
{
    private static int checks;
    private static string root = "";
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--qa-verify-failure")) throw new InvalidOperationException("Intentional QA failure-boundary verification.");
            root = Path.Combine(Path.GetTempPath(), "ShelterType-Update-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await Checks();
            Console.WriteLine($"PASS {checks} update checks. Isolated fixtures: {root}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("QA FAILED (caught): " + error); return 1; }
        finally { /* Retain isolated fixtures and rollback logs for inspection. No user profile is ever opened. */ }
    }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception e) when (e is InvalidDataException or HttpRequestException or OperationCanceledException) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    private static byte[] Zip(params (string Name, string Content)[] extras)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var item in new[] { ("ShelterType.exe", "new exe"), ("ShelterType.dll", "new dll"), ("ShelterType.runtimeconfig.json", "{}") }.Concat(extras))
            {
                var entry = archive.CreateEntry(item.Item1);
                using var writer = new StreamWriter(entry.Open()); writer.Write(item.Item2);
            }
        }
        return output.ToArray();
    }
    private static byte[] Release(byte[] zip, string tag = "v0.8.0", bool draft = false, bool prerelease = false, string owner = UpdateClient.Repository, bool checksum = true)
    {
        var assets = new List<object> { new { name = UpdateClient.PackageName, browser_download_url = $"https://github.com/{owner}/releases/download/{tag}/{UpdateClient.PackageName}", size = zip.Length, state = "uploaded" } };
        if (checksum) assets.Add(new { name = UpdateClient.PackageName + ".sha256", browser_download_url = $"https://github.com/{owner}/releases/download/{tag}/{UpdateClient.PackageName}.sha256", size = 96, state = "uploaded" });
        return JsonSerializer.SerializeToUtf8Bytes(new { tag_name = tag, draft, prerelease, body = "Release notes", assets });
    }
    private static string WriteZip(byte[] bytes, string? directory = null)
    {
        directory ??= Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, UpdateClient.PackageName); File.WriteAllBytes(path, bytes); return path;
    }
    private static async Task Checks()
    {
        foreach (var tag in new[] { "0.8.0", "v0.8.0", "v10.25.100" }) Check(UpdateClient.ParseStableVersion(tag) is not null, "stable version " + tag);
        foreach (var tag in new[] { "v00.8.0", "v0.8", "v0.8.0.0", "v0.8.0-beta", "v0.8.0+meta", " v0.8.0", "../v0.8.0", "V0.8.0", "v999999999999999.1.0" })
            Check(UpdateClient.ParseStableVersion(tag) is null, "reject unstable/ambiguous version " + tag);
        var zip = Zip(("Data/course.txt", "original lesson"), ("Teaching/keyboard.png", "picture"));
        Check(UpdateClient.ParseRelease(Release(zip), new(0, 7, 0))?.Version == new Version(0, 8, 0), "new stable release");
        Check(UpdateClient.ParseRelease(Release(zip), new(0, 8, 0, 0)) is null, "equal normalized assembly version");
        Check(UpdateClient.ParseRelease(Release(zip), new(0, 9, 0)) is null, "never downgrade");
        Check(UpdateClient.ParseRelease(Release(zip, draft: true), new(0, 7, 0)) is null, "draft excluded");
        Check(UpdateClient.ParseRelease(Release(zip, prerelease: true), new(0, 7, 0)) is null, "prerelease excluded");
        Reject(() => UpdateClient.ParseRelease(Release(zip, owner: "evil/fork"), new(0, 7, 0)), "other repository");
        Reject(() => UpdateClient.ParseRelease(Release(zip, checksum: false), new(0, 7, 0)), "missing checksum");
        Check(PackageValidator.Validate(WriteZip(zip)).Count == 5, "valid resources and flat root");
        foreach (var path in new[] { "../escape.txt", "nested/../../escape.txt", "/absolute", "C:/Windows/escape", "nested\\file", "UserData/preferences.json", "userdata/Progress/x.json", "TestData/a", ".git/config", "file:ads", "a./b", "a /b", "AUX.txt", "foo/COM1", "CON", "foo/*", "a//b" })
            Reject(() => PackageValidator.Validate(WriteZip(Zip((path, "bad")))), "unsafe zip path " + path);
        Reject(() => PackageValidator.Validate(WriteZip(Zip(("sheltertype.DLL", "duplicate")))), "case duplicate");
        Reject(() => PackageValidator.Validate(WriteZip(Zip(("nested", "file"), ("nested/child", "file")))), "file/folder collision");
        var linkPath = WriteZip(Zip(("link", "target")));
        using (var archive = ZipFile.Open(linkPath, ZipArchiveMode.Update)) archive.GetEntry("link")!.ExternalAttributes = unchecked((int)0xa1ff0000);
        Reject(() => PackageValidator.Validate(linkPath), "symlink entry");
        var goodHash = Convert.ToHexString(SHA256.HashData(zip));
        Check(UpdateClient.ParseChecksum(goodHash.ToLowerInvariant() + "  " + UpdateClient.PackageName + "\n") == goodHash, "checksum filename");
        Reject(() => UpdateClient.ParseChecksum(goodHash + "  wrong.zip"), "checksum wrong asset");
        Reject(() => UpdateClient.ParseChecksum(goodHash + "\n" + goodHash), "multiple checksums");
        await UpdateClient.VerifyChecksumAsync(WriteZip(zip), goodHash); checks++;
        await RejectAsync(() => UpdateClient.VerifyChecksumAsync(WriteZip(zip), new string('0', 64)), "wrong checksum");
        using (var client = new UpdateClient(new FakeHandler(zip)))
        {
            var release = await client.CheckAsync(new(0, 7, 0));
            Check(release is not null, "check transport");
            var update = await client.DownloadAsync(release!, Path.Combine(root, "cache"));
            Check(File.ReadAllBytes(update.PackagePath).SequenceEqual(zip), "download intact");
        }
        foreach (var behavior in new[] { "bad-hash", "short", "long", "evil-redirect", "http-redirect", "metadata-huge", "hash-huge", "redirect-loop", "rate-limit", "unsafe-zip" })
        {
            using var client = new UpdateClient(new FakeHandler(zip, behavior));
            await RejectAsync(async () => { var release = await client.CheckAsync(new(0, 7, 0)); if (release is not null) await client.DownloadAsync(release, Path.Combine(root, "cache")); }, behavior);
        }
        using (var client = new UpdateClient(new FakeHandler(zip, "no-release"))) Check(await client.CheckAsync(new(0, 7, 0)) is null, "empty release feed");
        using (var client = new UpdateClient(new FakeHandler(zip)))
        {
            var release = await client.CheckAsync(new(0, 7, 0));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await RejectAsync(() => client.DownloadAsync(release!, Path.Combine(root, "cache"), cancellationToken: canceled.Token), "cancelled download");
        }
        if (OperatingSystem.IsWindows()) await InstallerChecks(zip);
    }

    private static async Task InstallerChecks(byte[] zip)
    {
        async Task<(string Install, string Cache, int ExitCode)> Run(byte[] package, string scenario)
        {
            var directory = Path.Combine(root, "installer-" + scenario);
            var install = Path.Combine(directory, "app"); var cache = Path.Combine(directory, "cache");
            Directory.CreateDirectory(install); Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(install, "ShelterType.exe"), "old exe");
            File.WriteAllText(Path.Combine(install, "ShelterType.dll"), "old dll");
            Directory.CreateDirectory(Path.Combine(install, "UserData")); File.WriteAllText(Path.Combine(install, "UserData", "preferences.json"), "personal preferences");
            Directory.CreateDirectory(Path.Combine(install, "Data")); File.WriteAllText(Path.Combine(install, "Data", "custom.txt"), "local custom lesson"); File.WriteAllText(Path.Combine(install, "Data", "course.txt"), "old lesson");
            if (scenario == "success")
            {
                foreach (var old in new[] { ("update-" + new string('a',32), true, install), ("update-" + new string('b',32), false, install), ("update-" + new string('c',32), true, install + "-other") })
                {
                    var oldFolder = Path.Combine(directory, old.Item1); Directory.CreateDirectory(oldFolder);
                    File.WriteAllText(Path.Combine(oldFolder, "result.json"), JsonSerializer.Serialize(new { Success = old.Item2 }));
                    File.WriteAllText(Path.Combine(oldFolder, "install-job.json"), JsonSerializer.Serialize(new { InstallDirectory = old.Item3 }));
                    Directory.CreateDirectory(Path.Combine(oldFolder, "backup")); File.WriteAllText(Path.Combine(oldFolder, "backup", "previous.dll"), "old backup");
                }
            }
            var packagePath = WriteZip(package, cache);
            var script = Path.Combine(cache, "InstallUpdate.ps1"); UpdateInstaller.WriteInstallerScript(script);
            FileStream? locked = null;
            if (scenario == "rollback") locked = new FileStream(Path.Combine(install, "ShelterType.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (scenario == "profile-busy") locked = new FileStream(Path.Combine(install, "UserData", "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (scenario == "junction")
            {
                var outside = Path.Combine(directory, "outside"); Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "keyboard.png"), "outside untouched");
                var junctionScript = Path.Combine(cache, "create-junction.ps1");
                File.WriteAllText(junctionScript, "param([string]$Link,[string]$Target)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n");
                var junctionStart = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", junctionScript, "-Link", Path.Combine(install, "Teaching"), "-Target", outside }) junctionStart.ArgumentList.Add(argument);
                using var junctionProcess = Process.Start(junctionStart)!; await junctionProcess.WaitForExitAsync();
                Check(junctionProcess.ExitCode == 0, "junction fixture creation: " + await junctionProcess.StandardError.ReadToEndAsync());
            }
            var job = Path.Combine(cache, "install-job.json");
            File.WriteAllText(job, JsonSerializer.Serialize(new { InstallDirectory = install, PackagePath = packagePath, Sha256 = scenario is "hash" or "running-parent" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(package)), ParentPid = scenario == "running-parent" ? Environment.ProcessId : 0, ParentStartUtcTicks = scenario == "running-parent" ? Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks : 0L, Version = "0.8.0", Restart = false }));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-JobPath", job }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var errorsTask = process.StandardError.ReadToEndAsync(); var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            locked?.Dispose();
            var errors = await errorsTask; var output = await outputTask;
            File.WriteAllText(Path.Combine(cache, "process.txt"), output + errors);
            Check(File.ReadAllText(Path.Combine(install, "UserData", "preferences.json")) == "personal preferences", scenario + " preserves profile");
            Check(File.ReadAllText(Path.Combine(install, "Data", "custom.txt")) == "local custom lesson", scenario + " preserves unlisted course");
            if (scenario == "junction") Check(File.ReadAllText(Path.Combine(directory, "outside", "keyboard.png")) == "outside untouched", "junction destination untouched");
            return (install, cache, process.ExitCode);
        }
        var success = await Run(zip, "success");
        Check(success.ExitCode == 0, "installer success: " + File.ReadAllText(Path.Combine(success.Cache, "result.json")));
        Check(File.ReadAllText(Path.Combine(success.Install, "ShelterType.exe")) == "new exe", "program replaced");
        Check(File.ReadAllText(Path.Combine(success.Install, "Data", "course.txt")) == "original lesson", "bundled course updated");
        Check(!File.Exists(Path.Combine(success.Cache, UpdateClient.PackageName)), "successful update removes its ZIP");
        Check(!Directory.EnumerateDirectories(success.Cache, "stage-*").Any(), "successful update removes its extraction stage");
        Check(Directory.EnumerateDirectories(success.Cache, "backup-*").Count() == 1, "current recovery backup retained");
        Check(!Directory.Exists(Path.Combine(Path.GetDirectoryName(success.Cache)!, "update-" + new string('a',32))), "older success cache removed");
        Check(Directory.Exists(Path.Combine(Path.GetDirectoryName(success.Cache)!, "update-" + new string('b',32))), "failed update cache retained");
        Check(Directory.Exists(Path.Combine(Path.GetDirectoryName(success.Cache)!, "update-" + new string('c',32))), "another installation cache retained");
        foreach (var scenario in new[] { "rollback", "profile-busy", "hash", "malicious", "junction", "running-parent" })
        {
            var result = await Run(scenario == "malicious" ? Zip(("UserData/preferences.json", "overwrite")) : zip, scenario);
            Check(result.ExitCode != 0, scenario + " rejected");
            Check(File.ReadAllText(Path.Combine(result.Install, "ShelterType.exe")) == "old exe", scenario + " keeps/restores old exe");
            Check(File.ReadAllText(Path.Combine(result.Install, "ShelterType.dll")) == "old dll", scenario + " keeps/restores old dll");
            Check(File.ReadAllText(Path.Combine(result.Install, "Data", "course.txt")) == "old lesson", scenario + " keeps/restores old resources");
            using var log = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Cache, "result.json")).TrimStart('\uFEFF'));
            Check(log.RootElement.GetProperty("RollbackComplete").GetBoolean(), scenario + " rollback complete");
            Check(log.RootElement.GetProperty("ParentExited").GetBoolean() == (scenario != "running-parent"), scenario + " original process exit assessed");
        }
    }

    private sealed class FakeHandler(byte[] originalZip, string behavior = "normal") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var zip = behavior == "unsafe-zip" ? Zip(("../escape", "bad")) : originalZip;
            var uri = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage response;
            if (behavior == "rate-limit") response = new(HttpStatusCode.Forbidden);
            else if (uri == UpdateClient.LatestReleaseUri.AbsoluteUri)
                response = behavior == "no-release" ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(behavior == "metadata-huge" ? new byte[1024 * 1024 + 1] : Release(zip)) };
            else if (uri.EndsWith(".sha256")) response = new(HttpStatusCode.OK) { Content = new StringContent(behavior == "hash-huge" ? new string('A', 5000) : behavior == "bad-hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(zip)) + "  " + UpdateClient.PackageName) };
            else if (behavior is "evil-redirect" or "http-redirect" or "redirect-loop")
            {
                response = new(HttpStatusCode.Redirect);
                response.Headers.Location = new(behavior == "evil-redirect" ? "https://evil.example/payload.zip" : behavior == "http-redirect" ? "http://github.com/payload.zip" : uri);
            }
            else response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(behavior == "short" ? zip[..^1] : behavior == "long" ? zip.Concat(new byte[] { 0 }).ToArray() : zip) };
            return Task.FromResult(response);
        }
    }
}
