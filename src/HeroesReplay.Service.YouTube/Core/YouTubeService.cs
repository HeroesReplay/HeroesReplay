using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Service.YouTube.Core
{
    public class YouTubeService : BackgroundService
    {
        private readonly ILogger<YouTubeService> logger;
        private readonly IYouTubeUploader uploader;

        public YouTubeService(ILogger<YouTubeService> logger, IYouTubeUploader uploader, CancellationTokenSource cts) : base(cts)
        {
            this.logger = logger;
            this.uploader = uploader;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await uploader.ListenAsync();
            }
            catch (OperationCanceledException e)
            {

            }
            catch (Exception e)
            {
                
            }
        }
    }
}
