using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Deletes a recording that will not be published. OBS reports the output stopped before it
/// lets go of the mp4, so the first delete can hit a sharing violation. A locked file is tried
/// again for a few seconds instead of being left on disk for retention to find days later.
/// </summary>
public static class RecordingDiscard
{
    public const int Attempts = 15;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public static async Task<bool> DeleteAsync(
        string path,
        ILogger logger,
        int attempts = Attempts,
        TimeSpan? retryDelay = null
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        TimeSpan delay = retryDelay ?? RetryDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Deleted {Path} after OBS released it ({Attempt} attempts).",
                        path,
                        attempt
                    );
                }

                return true;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Could not delete {Path} after {Attempt} attempts. Retention removes it later.",
                    path,
                    attempt
                );
                return false;
            }
        }
    }
}
