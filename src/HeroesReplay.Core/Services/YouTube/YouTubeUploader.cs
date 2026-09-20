using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// https://developers.google.com/youtube/v3/guides/moving_to_oauth#offlinelong-lived-access-to-the-youtube-api
/// https://developers.google.com/youtube/v3/code_samples/dotnet#upload_a_video
/// </summary>
namespace HeroesReplay.Core.Services.YouTube;

public class YouTubeUploader : IYouTubeUploader
{
    private readonly ILogger<YouTubeUploader> logger;
    private readonly AppSettings settings;
    private readonly CancellationTokenSource cancellationTokenSource;

    public YouTubeUploader(
        ILogger<YouTubeUploader> logger,
        AppSettings settings,
        CancellationTokenSource cancellationTokenSource
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.cancellationTokenSource = cancellationTokenSource;
    }

    private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
    {
        try
        {
            await ProcessRecording(e.FullPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not upload {Path}", e.FullPath);
        }
    }

    public async Task ListenAsync()
    {
        await AuthorizeAsync().ConfigureAwait(false);

        using var recordingWatcher = new FileSystemWatcher(settings.ContextsDirectory, "*.mp4")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
        };
        recordingWatcher.Created += FileSystemWatcher_Created;
        logger.LogInformation(
            "YouTube uploader listening for mp4 files in {Directory}.",
            settings.ContextsDirectory
        );
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
        finally
        {
            recordingWatcher.Created -= FileSystemWatcher_Created;
        }
    }

    public async Task ProcessRecording(string recordingPath)
    {
        CancellationToken token = cancellationTokenSource.Token;
        FileInfo recording = await WaitForRecordingReadyAsync(recordingPath, token)
            .ConfigureAwait(false);
        FileInfo entryFile = await WaitForEntryAsync(recording.Directory, token)
            .ConfigureAwait(false);
        if (entryFile == null)
        {
            logger.LogWarning(
                "No {Entry} next to {Path}; skipping upload.",
                settings.YouTube.EntryFileName,
                recordingPath
            );
            return;
        }

        YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(
            await File.ReadAllTextAsync(entryFile.FullName, token)
        );

        UserCredential credential = await AuthorizeAsync().ConfigureAwait(false);

        using var youtubeService = new YouTubeService(
            new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApiKey = settings.YouTube.ApiKey,
                ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            }
        );

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = entry.Title,
                Description = string.Join(Environment.NewLine, entry.DescriptionLines),
                Tags = entry.Tags,
                CategoryId = entry.CategoryId,
            },
            Status = new VideoStatus { PrivacyStatus = entry.PrivacyStatus },
        };

        logger.LogInformation(
            "Uploading {Path} ({Bytes} bytes) as {Title} ({Privacy}).",
            recording.FullName,
            recording.Length,
            entry.Title,
            entry.PrivacyStatus
        );

        using FileStream fileStream = recording.Open(
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        VideosResource.InsertMediaUpload videosInsertRequest = youtubeService.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/*"
        );
        videosInsertRequest.ProgressChanged += videosInsertRequest_ProgressChanged;
        videosInsertRequest.ResponseReceived += videosInsertRequest_ResponseReceived;
        IUploadProgress result = await videosInsertRequest.UploadAsync(token).ConfigureAwait(false);

        if (result.Status == UploadStatus.Completed)
        {
            string uploadedName = settings.YouTube.EntryFileNameUploaded;
            if (!string.IsNullOrWhiteSpace(uploadedName))
            {
                string uploadedPath = Path.Combine(recording.Directory.FullName, uploadedName);
                File.Move(entryFile.FullName, uploadedPath, overwrite: true);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"YouTube upload status {result.Status}: {result.Exception?.Message}"
            );
        }
    }

    private async Task<UserCredential> AuthorizeAsync()
    {
        string secretsPath = Path.Combine(settings.Location.DataDirectory, "client_secrets.json");
        if (!File.Exists(secretsPath))
        {
            throw new FileNotFoundException(
                "Google OAuth client_secrets.json is missing. Run tools/fill-secrets-from-op.ps1.",
                secretsPath
            );
        }

        await using FileStream stream = new(secretsPath, FileMode.Open, FileAccess.Read);
        UserCredential credential = await GoogleWebAuthorizationBroker
            .AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                new[] { YouTubeService.Scope.YoutubeUpload },
                settings.YouTube.ChannelId,
                cancellationTokenSource.Token
            )
            .ConfigureAwait(false);
        logger.LogInformation(
            "YouTube OAuth ready for channel {ChannelId}.",
            settings.YouTube.ChannelId
        );
        return credential;
    }

    private async Task<FileInfo> WaitForRecordingReadyAsync(
        string recordingPath,
        CancellationToken token
    )
    {
        long lastLength = -1;
        int stable = 0;
        while (!token.IsCancellationRequested)
        {
            var recording = new FileInfo(recordingPath);
            recording.Refresh();
            if (recording.Exists && recording.Length > 0 && recording.Length == lastLength)
            {
                if (TryOpenRead(recording.FullName))
                {
                    stable++;
                    if (stable >= 5)
                    {
                        logger.LogInformation(
                            "Recording ready {Path} ({Bytes} bytes).",
                            recording.FullName,
                            recording.Length
                        );
                        return recording;
                    }
                }
                else
                {
                    stable = 0;
                }
            }
            else
            {
                stable = 0;
            }

            lastLength = recording.Exists ? recording.Length : -1;
            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        return new FileInfo(recordingPath);
    }

    private async Task<FileInfo> WaitForEntryAsync(DirectoryInfo directory, CancellationToken token)
    {
        for (int i = 0; i < 30; i++)
        {
            FileInfo entryFile = directory
                .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file =>
                    file.Name.Equals(
                        settings.YouTube.EntryFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (entryFile != null && entryFile.Exists)
            {
                return entryFile;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }

        return null;
    }

    private static bool TryOpenRead(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void videosInsertRequest_ResponseReceived(Video video)
    {
        logger.LogInformation("YouTube upload complete id={VideoId}", video?.Id);
    }

    private void videosInsertRequest_ProgressChanged(IUploadProgress progress)
    {
        if (progress.Exception != null)
        {
            logger.LogError(progress.Exception, "Could not upload video.");
        }

        logger.LogInformation($"video status: {progress.Status}. bytes: ({progress.BytesSent})");
    }
}
