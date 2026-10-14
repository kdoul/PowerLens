using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerLens.Core;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Name,
    string HtmlUrl,
    string Notes,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The zip for a runtime identifier, e.g. "PowerLens-win-x64.zip".</summary>
    public ReleaseAsset? PackageFor(string rid) =>
        Assets.FirstOrDefault(a => a.Name.Equals($"PowerLens-{rid}.zip", StringComparison.OrdinalIgnoreCase));

    public ReleaseAsset? Checksums =>
        Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Looks up the latest GitHub release of PowerLens.</summary>
public static class UpdateChecker
{
    public const string Repository = "kdoul/PowerLens";
    public static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    /// <summary>Version of the running app (set from the release tag at build time).</summary>
    public static Version CurrentVersion =>
        (Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly).GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Release package matching this process: win-arm64 on ARM, otherwise win-x64.</summary>
    public static string CurrentRid =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    public static string Display(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>"v1.2.3", "1.2.3-beta+abc" → 1.2.3.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    /// <summary>Parses a GitHub "release" JSON object. Drafts and pre-releases return null.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

        string tag = Str(root, "tag_name");
        if (ParseVersion(tag) is not { } version) return null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                string name = Str(a, "name"), url = Str(a, "browser_download_url");
                if (name.Length > 0 && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    assets.Add(new ReleaseAsset(name, url, a.TryGetProperty("size", out var size) && size.TryGetInt64(out long n) ? n : 0));
            }
        }

        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), out var date) ? date : null;

        return new ReleaseInfo(version, tag, Str(root, "name") is { Length: > 0 } title ? title : tag,
            Str(root, "html_url"), Str(root, "body"), published, assets);
    }

    public static async Task<ReleaseInfo?> GetLatestAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // No releases yet.
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Finds a file's hash in sha256sum output ("&lt;hex&gt;  name" or "&lt;hex&gt; *name").</summary>
    public static string? FindChecksum(string sha256Sums, string fileName)
    {
        foreach (var raw in sha256Sums.Split('\n'))
        {
            var line = raw.Trim();
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            string hash = line[..space];
            string name = line[(space + 1)..].Trim().TrimStart('*');
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && hash.Length == 64)
                return hash.ToLowerInvariant();
        }
        return null;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
