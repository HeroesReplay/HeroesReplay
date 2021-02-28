namespace HeroesReplay.Core.Services.YouTube
{
    using Google.Apis.Auth.OAuth2;
    using Google.Apis.Services;
    using Google.Apis.Upload;
    using Google.Apis.YouTube.v3;
    using Google.Apis.YouTube.v3.Data;

    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;

    using Microsoft.Extensions.Logging;

    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// https://developers.google.com/youtube/v3/guides/moving_to_oauth#offlinelong-lived-access-to-the-youtube-api
    /// https://developers.google.com/youtube/v3/code_samples/dotnet#upload_a_video
    /// </summary>
    public class YouTubeUploader : IYouTubeUploader
    {
        private readonly ILogger<YouTubeUploader> logger;
        private readonly AppSettings settings;
        private readonly CancellationTokenSource tokenSource;

        public YouTubeUploader(ILogger<YouTubeUploader> logger, AppSettings settings, CancellationTokenSource tokenSource)
        {
            this.logger = logger;
            this.settings = settings;
            this.tokenSource = tokenSource;
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            await Task.Factory.StartNew(() => ProcessRecording(e.FullPath), tokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        public async Task ListenAsync()
        {
            await Task.Run(() =>
            {
                using (var recordingWatcher = new FileSystemWatcher(settings.ContextsDirectory, "*.mp4") { EnableRaisingEvents = true, IncludeSubdirectories = true })
                {
                    recordingWatcher.Created += FileSystemWatcher_Created;

                    using (var waiter = new ManualResetEventSlim())
                    {
                        logger.LogInformation("File system watcher listening for mp4 files...");
                        waiter.Wait(tokenSource.Token);
                    }

                    recordingWatcher.Created -= FileSystemWatcher_Created;
                }
            });
        }

        public async Task ProcessRecording(string recordingPath)
        {
            try
            {
                var recording = new FileInfo(recordingPath);
                var entryFile = recording.Directory.GetFiles("*.json", SearchOption.TopDirectoryOnly).FirstOrDefault(file => file.Name.Equals(settings.YouTube.EntryFileName, StringComparison.OrdinalIgnoreCase));

                if (entryFile != null && entryFile.Exists)
                {
                    YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(await File.ReadAllTextAsync(entryFile.FullName));

                    GoogleCredential credential;

                    using (var stream = new FileStream(Path.Combine(settings.Location.DataDirectory, "client_secrets.json"), FileMode.Open, FileAccess.Read))
                    {
                        credential = GoogleCredential.FromStream(stream).CreateScoped(YouTubeService.Scope.YoutubeUpload).CreateWithUser(settings.YouTube.UserId);
                    }

                    var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApiKey = settings.YouTube.ApiKey,
                        ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
                    });

                    var video = new Video
                    {
                        Snippet = new VideoSnippet
                        {
                            Title = entry.Title,
                            Description = string.Join(Environment.NewLine, entry.DescriptionLines),
                            Tags = entry.Tags,
                            CategoryId = entry.CategoryId
                        },
                        Status = new VideoStatus
                        {
                            PrivacyStatus = entry.PrivacyStatus
                        }
                    };

                    using (var fileStream = recording.OpenRead())
                    {
                        var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
                        videosInsertRequest.ProgressChanged += videosInsertRequest_ProgressChanged;
                        videosInsertRequest.ResponseReceived += videosInsertRequest_ResponseReceived;
                        await videosInsertRequest.UploadAsync();
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not upload {recordingPath}");
            }
        }

        private void videosInsertRequest_ResponseReceived(Video video)
        {
            logger.LogInformation("video response recieved");
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
}
