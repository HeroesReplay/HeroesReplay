using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.SelfUpdate;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Update;

public sealed class ReleaseUpdateGate : IReleaseUpdateGate
{
    private readonly ILogger<ReleaseUpdateGate> logger;
    private readonly AppSettings settings;

    public ReleaseUpdateGate(ILogger<ReleaseUpdateGate> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<bool> TryStageAsync(CancellationToken cancellationToken)
    {
        ReleaseSettings release = settings.Release;
        if (release?.Enabled != true)
        {
            return false;
        }

        string install = Path.GetDirectoryName(Environment.ProcessPath);
        if (ReleaseInstall.LooksLikeSourceBuild(install))
        {
            logger.LogInformation("Release update skipped. This process is a source build.");
            return false;
        }

        string repository = string.IsNullOrWhiteSpace(release.Repository)
            ? ReleaseSettings.DefaultRepository
            : release.Repository;
        string assetName = string.IsNullOrWhiteSpace(release.AssetName)
            ? ReleaseSettings.DefaultAssetName
            : release.AssetName;
        string local = ReleaseInstall.ReadVersion(install);
        string latestUrl = $"https://api.github.com/repos/{repository}/releases/latest";

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("HeroesReplay", "1")
            );
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
            );
            string json = await http.GetStringAsync(latestUrl, cancellationToken)
                .ConfigureAwait(false);
            ReleaseOffer? offer = GitHubReleaseJson.Read(json, assetName, local);
            if (offer is not ReleaseOffer found)
            {
                return false;
            }

            string staging = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "updates",
                found.Version
            );
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            string zipPath = Path.Combine(staging, assetName);
            await using (
                Stream zip = await http.GetStreamAsync(found.DownloadUrl, cancellationToken)
                    .ConfigureAwait(false)
            )
            await using (FileStream file = File.Create(zipPath))
            {
                await zip.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            string extracted = Path.Combine(staging, "extract");
            ZipFile.ExtractToDirectory(zipPath, extracted);
            string publish = ReleaseInstall.FindPublishRoot(extracted);
            if (publish == null)
            {
                logger.LogWarning(
                    "Release {Version} did not contain heroesreplay.exe.",
                    found.Version
                );
                return false;
            }

            string prepared = Path.Combine(staging, "prepared");
            ReleaseInstall.CopyPublish(publish, prepared);
            ReleaseInstall.PreserveSecrets(release.SecretsPath, install, prepared);
            StartHelper(install, prepared);
            ServiceStopFile.Request();
            logger.LogInformation(
                "Release {Version} is staged. Services will stop and the new build will start.",
                found.Version
            );
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Release update did not stage. This version keeps running.");
            return false;
        }
    }

    private static void StartHelper(string install, string prepared)
    {
        string script = Path.Combine(install, "apply-release.ps1");
        if (!File.Exists(script))
        {
            script = Path.Combine(AppContext.BaseDirectory, "apply-release.ps1");
        }

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -InstallDir \"{install}\" -StagingDir \"{prepared}\" -WaitForPid {Environment.ProcessId}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(start);
    }
}
