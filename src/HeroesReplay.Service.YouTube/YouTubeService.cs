using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.YouTube;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Service.YouTube
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
