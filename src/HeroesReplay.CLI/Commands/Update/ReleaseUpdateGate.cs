using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Update;

public sealed class ReleaseUpdateGate : IReleaseUpdateGate
{
    /// <summary>The next replay waits for this answer, so a slow GitHub cannot hold it long.</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ReleaseUpdateGate> logger;
    private readonly AppSettings settings;
    private string loggedSkip;
    private StagedRelease staged;

    public ReleaseUpdateGate(ILogger<ReleaseUpdateGate> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<bool> TryStageAsync(CancellationToken cancellationToken)
    {
        staged = null;
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
            string json;
            using (var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                lookup.CancelAfter(LookupTimeout);
                json = await http.GetStringAsync(latestUrl, lookup.Token).ConfigureAwait(false);
            }

            ReleaseOffer? offer = Pick(
                GitHubReleaseJson.Read(json, assetName, local),
                ReleaseSkipList.Load(),
                out string skipped
            );
            if (offer is not ReleaseOffer found)
            {
                if (skipped != null && skipped != loggedSkip)
                {
                    loggedSkip = skipped;
                    logger.LogWarning(
                        "Release {Version} failed its health gate on this machine and was rolled back, so it is not installed again. Remove it from {SkipList} to allow it.",
                        skipped,
                        ReleaseSkipList.DefaultPath
                    );
                }

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
            ReleaseInstall.PreserveMinReplayId(
                Path.Combine(install, "appsettings.json"),
                Path.Combine(prepared, "appsettings.json")
            );
            staged = new StagedRelease(install, prepared, found.Version);
            logger.LogInformation(
                "Release {Version} is staged. The next replay is not launched. The report finishes on the waiting scene, then this install is replaced.",
                found.Version
            );
            return true;
        }
        // A lookup timeout is a cancellation too. Only a stop request ends the check that way.
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Release update did not stage. This version keeps running.");
            return false;
        }
    }

    public bool HandOff()
    {
        StagedRelease release = staged;
        staged = null;
        if (release == null)
        {
            return false;
        }

        try
        {
            // The stop file ends the supervisor too. Tell the helper so the restart is supervised again.
            StartHelper(
                release.Install,
                release.Prepared,
                release.Version,
                ServiceSupervisorFile.IsRunning()
            );
            ServiceStopFile.Request();
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Release {Version} was not handed to the installer. This version keeps running.",
                release.Version
            );
            return false;
        }

        logger.LogInformation(
            "Release {Version} is installing. Services will stop and the new build will start the next replay.",
            release.Version
        );
        return true;
    }

    /// <summary>
    /// The offer, unless this machine rolled that tag back. Then null, and
    /// <paramref name="skipped"/> names it.
    /// </summary>
    public static ReleaseOffer? Pick(
        ReleaseOffer? offer,
        ReleaseSkipList skipList,
        out string skipped
    )
    {
        skipped = null;
        if (offer is ReleaseOffer found && skipList?.Contains(found.Version) == true)
        {
            skipped = found.Version;
            return null;
        }

        return offer;
    }

    /// <summary>
    /// The helper's command line. <paramref name="supervised"/> passes <c>-Supervise</c>, so the
    /// stack comes back under a supervisor after the install or a rollback.
    /// </summary>
    public static string HelperArguments(
        string script,
        string install,
        string prepared,
        int waitForPid,
        string version,
        bool supervised
    ) =>
        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -InstallDir \"{install}\" -StagingDir \"{prepared}\" -WaitForPid {waitForPid} -Version \"{version}\""
        + (supervised ? " -Supervise" : string.Empty);

    private sealed record StagedRelease(string Install, string Prepared, string Version);

    private static void StartHelper(
        string install,
        string prepared,
        string version,
        bool supervised
    )
    {
        string script = Path.Combine(prepared, "apply-release.ps1");
        if (!File.Exists(script))
        {
            script = Path.Combine(install, "apply-release.ps1");
        }

        if (!File.Exists(script))
        {
            script = Path.Combine(AppContext.BaseDirectory, "apply-release.ps1");
        }

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = HelperArguments(
                script,
                install,
                prepared,
                Environment.ProcessId,
                version,
                supervised
            ),
            UseShellExecute = true,
            // Minimized, never hidden: the operator can open the update's window from the taskbar.
            WindowStyle = ProcessWindowStyle.Minimized,
        };
        Process.Start(start);
    }
}
