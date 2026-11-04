using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShelterType.Update;

public sealed record ReleaseUpdate(Version Version, string Tag, string ReleaseUrl, string Notes,
    Uri PackageUrl, Uri ChecksumUrl, long PackageSize);
public sealed record DownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}
public sealed record PreparedUpdate(string Directory, string PackagePath, string Sha256, ReleaseUpdate Release);

/// <summary>Public GitHub Releases only. No credentials, telemetry, prereleases or arbitrary update endpoints.</summary>
public sealed class UpdateClient : IDisposable
{
    public const string Repository = "jasonshelter0/ShelterType";
    public const string PackageName = "ShelterType-win-x64.zip";
    public const long MaximumPackageBytes = 256L * 1024 * 1024;
    public static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private readonly HttpClient client;

    public UpdateClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    // Injectable transport makes offline failure/redirect/size checks deterministic.
    public UpdateClient(HttpMessageHandler handler)
    {
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShelterType-Updater/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await GetAsync(LatestReleaseUri, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // Repository has no release yet.
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false);
        return ParseRelease(bytes, current);
    }

    public static Version? ParseStableVersion(string? tag)
    {
        if (tag is null || !Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)) return null;
        return Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;
    }

    public static ReleaseUpdate? ParseRelease(ReadOnlyMemory<byte> json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseStableVersion(tag);
        var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        if (version is null || version <= normalizedCurrent) return null;
        var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/";
        Uri? zip = null, checksum = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name is not (PackageName or PackageName + ".sha256")) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (url != expectedUrl + name || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidDataException("The update asset does not belong to the expected repository and release.");
            if (asset.TryGetProperty("state", out var state) && state.GetString() != "uploaded") continue;
            if (name == PackageName)
            {
                if (zip is not null) throw new InvalidDataException("Duplicate update package.");
                zip = uri; size = asset.GetProperty("size").GetInt64();
            }
            else
            {
                if (checksum is not null) throw new InvalidDataException("Duplicate update checksum.");
                checksum = uri;
            }
        }
        if (zip is null || checksum is null || size <= 0 || size > MaximumPackageBytes)
            throw new InvalidDataException("This release has no complete, compatible Windows update package.");
        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
        return new(version, tag, $"https://github.com/{Repository}/releases/tag/{tag}", notes, zip, checksum, size);
    }

    public async Task<PreparedUpdate> DownloadAsync(ReleaseUpdate release, string cacheRoot,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // Validate even when the caller constructs a ReleaseUpdate instead of using CheckAsync.
        var prefix = $"https://github.com/{Repository}/releases/download/{release.Tag}/";
        if (ParseStableVersion(release.Tag) != release.Version || release.PackageUrl.AbsoluteUri != prefix + PackageName ||
            release.ChecksumUrl.AbsoluteUri != prefix + PackageName + ".sha256" ||
            release.PackageSize <= 0 || release.PackageSize > MaximumPackageBytes)
            throw new InvalidDataException("Invalid update metadata.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var token = timeout.Token;
        using var hashResponse = await GetAsync(release.ChecksumUrl, token).ConfigureAwait(false);
        hashResponse.EnsureSuccessStatusCode();
        var hashBytes = await ReadLimitedAsync(hashResponse, 4096, token).ConfigureAwait(false);
        var expectedHash = ParseChecksum(System.Text.Encoding.UTF8.GetString(hashBytes));
        var directory = Path.Combine(Path.GetFullPath(cacheRoot), "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var partial = Path.Combine(directory, PackageName + ".partial");
            using var response = await GetAsync(release.PackageUrl, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.PackageSize)
                throw new InvalidDataException("The downloaded package length differs from the release metadata.");
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                    if (received > release.PackageSize || received > MaximumPackageBytes) throw new InvalidDataException("Update package exceeds the expected size.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    progress?.Report(new(received, release.PackageSize));
                }
                if (received != release.PackageSize) throw new InvalidDataException("Incomplete update download.");
            }
            await VerifyChecksumAsync(partial, expectedHash, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            PackageValidator.Validate(partial);
            var packagePath = Path.Combine(directory, PackageName);
            File.Move(partial, packagePath);
            return new(directory, packagePath, expectedHash, release);
        }
        catch
        {
            // Only the unique directory created by this call is removed, never a caller-supplied folder.
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static string ParseChecksum(string text)
    {
        var lines = text.Trim('\uFEFF', ' ', '\r', '\n', '\t').Split('\n');
        if (lines.Length != 1) throw new InvalidDataException("Expected one SHA-256 checksum.");
        var match = Regex.Match(lines[0].Trim(), @"^([a-fA-F0-9]{64})(?:\s+\*?ShelterType-win-x64\.zip)?$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("Invalid SHA-256 checksum file.");
        return match.Groups[1].Value.ToUpperInvariant();
    }

    public static async Task VerifyChecksumAsync(string packagePath, string expectedHash, CancellationToken token = default)
    {
        if (!Regex.IsMatch(expectedHash, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid SHA-256 checksum.");
        await using var file = File.OpenRead(packagePath);
        var hash = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(expectedHash)))
            throw new InvalidDataException("Update checksum verification failed. The existing app has not been changed.");
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (!IsAllowedUri(uri)) throw new InvalidDataException("Untrusted update download location.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            if (next is null) throw new InvalidDataException("Update download redirect has no location.");
            uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
        }
        throw new InvalidDataException("Too many update download redirects.");
    }

    private static bool IsAllowedUri(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host is "api.github.com" or "github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com";

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Update metadata is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (result.Length + read > maximum) throw new InvalidDataException("Update metadata is too large.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }
    public void Dispose() => client.Dispose();
}
