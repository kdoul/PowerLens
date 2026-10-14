using PowerLens.Core;

namespace PowerLens.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.1.1", "0.1.1")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    [InlineData("v1.4.2+sha.abc", "1.4.2")]
    public void ParsesTags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void RejectsNonVersionTags(string? tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void ComparesIgnoringRevision()
    {
        Assert.True(UpdateChecker.IsNewer(new Version(0, 2, 0), new Version(0, 1, 1, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 1, 1), new Version(0, 1, 1, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(0, 1, 0), new Version(0, 1, 1)));
    }

    [Fact]
    public void ParsesGitHubReleaseJson()
    {
        const string json = """
        {
          "tag_name": "v0.2.0", "name": "PowerLens 0.2.0", "draft": false, "prerelease": false,
          "html_url": "https://github.com/kdoul/PowerLens/releases/tag/v0.2.0",
          "published_at": "2026-10-20T10:00:00Z", "body": "- Updates",
          "assets": [
            { "name": "PowerLens-win-x64.zip", "size": 1234, "browser_download_url": "https://github.com/kdoul/PowerLens/releases/download/v0.2.0/PowerLens-win-x64.zip" },
            { "name": "SHA256SUMS.txt", "size": 200, "browser_download_url": "https://github.com/kdoul/PowerLens/releases/download/v0.2.0/SHA256SUMS.txt" }
          ]
        }
        """;

        var release = UpdateChecker.ParseRelease(json)!;

        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("PowerLens 0.2.0", release.Name);
        Assert.Equal(1234, release.PackageFor("win-x64")!.Size);
        Assert.Null(release.PackageFor("win-arm64"));
        Assert.NotNull(release.Checksums);
    }

    [Fact]
    public void IgnoresPrereleases() =>
        Assert.Null(UpdateChecker.ParseRelease("""{ "tag_name": "v9.0.0", "prerelease": true }"""));

    [Fact]
    public void FindsChecksumInSha256SumsOutput()
    {
        string hash = new string('a', 64);
        string sums = $"{new string('b', 64)}  PowerLens-win-arm64.zip\n{hash}  PowerLens-win-x64.zip\n";
        Assert.Equal(hash, UpdateChecker.FindChecksum(sums, "PowerLens-win-x64.zip"));
        Assert.Equal(hash, UpdateChecker.FindChecksum($"{hash} *PowerLens-win-x64.zip", "PowerLens-win-x64.zip"));
        Assert.Null(UpdateChecker.FindChecksum(sums, "missing.zip"));
    }
}
