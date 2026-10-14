using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using PowerLens.Core;

namespace PowerLens.App;

/// <summary>
/// Downloads a release from GitHub, verifies it against the release's SHA256SUMS.txt, swaps the
/// executables in place and restarts. Windows lets a running .exe be renamed (not overwritten),
/// so the current files move to *.old and are deleted by the next launch.
/// </summary>
internal sealed class Updater
{
    public const string AfterUpdateArgument = "--after-update";

    private static readonly string StagingRoot = Path.Combine(Path.GetTempPath(), "PowerLens-update");

    private static readonly HttpClient Http = CreateClient();

    public ReleaseInfo? Available { get; private set; }

    /// <summary>
    /// In-place updates need the published single-file exe. A dev build (PowerLens.dll beside the exe)
    /// falls back to opening the release page.
    /// </summary>
    public static bool CanUpdateInPlace =>
        Environment.ProcessPath is { } exe
        && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "PowerLens.dll"));

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        var latest = await UpdateChecker.GetLatestAsync(Http, ct);
        Available = latest is not null && UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion) ? latest : null;
        return Available;
    }

    /// <summary>Downloads, verifies and extracts the package. Returns the folder holding the new files.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct = default)
    {
        string rid = UpdateChecker.CurrentRid;
        var package = release.PackageFor(rid)
            ?? throw new InvalidOperationException($"Release {release.Tag} has no package for {rid}.");

        string dir = Path.Combine(StagingRoot, release.Tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, package.Name);

        using (var response = await Http.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? package.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        if (release.Checksums is { } sums)
        {
            string text = await Http.GetStringAsync(sums.DownloadUrl, ct);
            string expected = UpdateChecker.FindChecksum(text, package.Name)
                ?? throw new InvalidDataException($"{package.Name} is not listed in SHA256SUMS.txt.");
            string actual;
            await using (var file = File.OpenRead(zip))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            if (actual != expected)
                throw new InvalidDataException("The download doesn't match the release checksum. Nothing was changed.");
        }

        string extracted = Path.Combine(dir, "files");
        ZipFile.ExtractToDirectory(zip, extracted);
        if (!File.Exists(Path.Combine(extracted, "PowerLens.exe")))
            throw new InvalidDataException("The package doesn't contain PowerLens.exe.");
        return extracted;
    }

    /// <summary>Replaces the running executables with the staged ones and starts the new version.</summary>
    public static void ApplyAndRestart(string stagedDir, bool startInTray)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        string folder = Path.GetDirectoryName(exe)!;

        // The app itself keeps whatever name it was saved under; the CLI is only updated if present.
        Replace(Path.Combine(stagedDir, "PowerLens.exe"), exe);
        string cli = Path.Combine(folder, "powerlens-cli.exe");
        if (File.Exists(cli))
        {
            try { Replace(Path.Combine(stagedDir, "powerlens-cli.exe"), cli); }
            catch (IOException) { /* CLI in use; it can be updated next time. */ }
        }

        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            Arguments = AfterUpdateArgument + (startInTray ? " " + StartupRegistration.TrayArgument : ""),
            WorkingDirectory = folder,
        });
    }

    /// <summary>Called at startup: removes leftovers from a previous update.</summary>
    public static void CleanUp()
    {
        if (Environment.ProcessPath is { } exe)
        {
            // Only the two files ApplyAndRestart renames; never other *.old files in the folder.
            string[] leftovers = [exe + ".old", Path.Combine(Path.GetDirectoryName(exe)!, "powerlens-cli.exe.old")];
            foreach (string old in leftovers)
            {
                try { if (File.Exists(old)) File.Delete(old); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        try { if (Directory.Exists(StagingRoot)) Directory.Delete(StagingRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void Replace(string source, string target)
    {
        if (!File.Exists(source)) return;
        string old = target + ".old";
        if (File.Exists(old)) File.Delete(old);

        bool moved = false;
        try
        {
            if (File.Exists(target))
            {
                File.Move(target, old);
                moved = true;
            }
            File.Copy(source, target);
        }
        catch
        {
            // Put the original back so the app keeps working.
            if (moved && !File.Exists(target)) File.Move(old, target);
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PowerLens/{UpdateChecker.Display(UpdateChecker.CurrentVersion)}");
        return client;
    }
}
