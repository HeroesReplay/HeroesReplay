using System;
using System.IO;
using System.Threading;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>Cleanup for a test's own temp folder (#331).</summary>
internal static class TestTemp
{
    /// <summary>
    /// Deletes a test's temp folder. A folder that is already gone is not a failure, and a file
    /// the indexer or antivirus still holds is retried briefly: cleanup must not fail a test
    /// whose assertions passed. Under parallel runs on ASA-SERVER an empty test folder was gone
    /// before <c>Dispose</c> ran, and <c>Directory.Delete</c> threw DirectoryNotFoundException.
    /// </summary>
    public static void Delete(string directory)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                {
                    return;
                }

                Thread.Sleep(50 * attempt);
            }
        }
    }
}
