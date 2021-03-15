using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Queue
{
    public class RequestQueueDequeuer : IRequestQueueDequeuer, IDisposable
    {
        private readonly FileInfo queueFile;
        private readonly ILogger<RequestQueueDequeuer> logger;
        private readonly LocationOptions locationOptions;
        private readonly QueueOptions queueOptions;
        private readonly JsonSerializerOptions options;
        private readonly SemaphoreSlim semaphore;

        public RequestQueueDequeuer(ILogger<RequestQueueDequeuer> logger, IOptions<QueueOptions> queueOptions, IOptions<LocationOptions> locationOptions)
        {
            this.logger = logger;
            this.locationOptions = locationOptions.Value;
            this.queueOptions = queueOptions.Value;

            semaphore = new SemaphoreSlim(1, maxCount: 1);
            queueFile = new FileInfo(Path.Combine(this.locationOptions.DataDirectory, this.queueOptions.SuccessFileName));
            options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: true) } };
        }

        public async Task<RewardQueueItem> DequeueItemAsync()
        {
            if (queueFile.Exists)
            {
                try
                {
                    await semaphore.WaitAsync();

                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items[0];

                        if (items.Remove(item))
                        {
                            await File.WriteAllTextAsync(queueFile.FullName, JsonSerializer.Serialize(items, options));
                            logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                            return item;
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not dequeue item");
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                semaphore?.Dispose();
            }
            finally
            {

            }
        }
    }
}
