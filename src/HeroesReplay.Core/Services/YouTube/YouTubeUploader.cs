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
    using Microsoft.Extensions.Options;

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
        private readonly IOptions<AppSettings> settings;
        private readonly CancellationTokenSource cts;

        public YouTubeUploader(ILogger<YouTubeUploader> logger, IOptions<AppSettings> settings, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }        

        public async Task ListenAsync()
        {
            await Task.Run(() =>
            {
                using (var recordingWatcher = new FileSystemWatcher(settings.Value.ContextsDirectory, "*.mp4") { EnableRaisingEvents = true, IncludeSubdirectories = true })
                {
                    recordingWatcher.Created += FileSystemWatcher_Created;

                    using (var waiter = new ManualResetEventSlim())
                    {
                        logger.LogInformation("File system watcher listening for mp4 files...");
                        waiter.Wait(cts.Token);
                    }

                    recordingWatcher.Created -= FileSystemWatcher_Created;
                }
            });
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            await Task.Factory.StartNew(() => ProcessRecording(e.FullPath), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        private async Task ProcessRecording(string recordingPath)
        {
            try
            {
                var recording = new FileInfo(recordingPath);
                var entryFile = recording.Directory.GetFiles("*.json", SearchOption.TopDirectoryOnly).FirstOrDefault(file => file.Name.Equals(settings.Value.YouTube.EntryFileName, StringComparison.OrdinalIgnoreCase));

                if (entryFile != null && entryFile.Exists)
                {
                    YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(await File.ReadAllTextAsync(entryFile.FullName));

                    UserCredential credential;

                    string secretsFile = Path.Combine(settings.Value.Location.DataDirectory, settings.Value.YouTube.SecretsFileName);

                    using (var stream = new FileStream(secretsFile, FileMode.Open, FileAccess.Read))
                    {
                        credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.Load(stream).Secrets, new[] { YouTubeService.Scope.YoutubeUpload }, settings.Value.YouTube.ChannelId, CancellationToken.None);
                    }

                    var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApiKey = settings.Value.YouTube.ApiKey,
                        ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
                    });

                    using (youtubeService)
                    {
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
                            videosInsertRequest.ProgressChanged += VideosInsertRequest_ProgressChanged;
                            videosInsertRequest.ResponseReceived += VideosInsertRequest_ResponseReceived;
                            await videosInsertRequest.UploadAsync(cts.Token);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not upload {recordingPath}");
            }
        }

        private void VideosInsertRequest_ResponseReceived(Video video)
        {
            logger.LogInformation("video response recieved");
        }

        private void VideosInsertRequest_ProgressChanged(IUploadProgress progress)
        {
            if (progress.Exception != null)
            {
                logger.LogError(progress.Exception, "Could not upload video.");
            }

            logger.LogInformation($"video status: {progress.Status}. bytes: ({progress.BytesSent})");
        }
    }
}
