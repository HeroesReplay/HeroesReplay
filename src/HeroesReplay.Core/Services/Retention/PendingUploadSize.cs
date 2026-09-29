using System;
using System.IO;
using HeroesReplay.Core.Services.YouTube;

namespace HeroesReplay.Core.Services.Retention;

public static class PendingUploadSize
{
    public static long Bytes(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    )
    {
        long total = 0;
        foreach (
            string path in PendingYouTubeUpload.Find(
                contextsDirectory,
                entryFileName,
                uploadedFileName
            )
        )
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0)
                {
                    total += info.Length;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return total;
    }
}
