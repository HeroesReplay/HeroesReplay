using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public class ReplayRequestQueue : IReplayRequestQueue
    {
        private readonly FileInfo fileInfo;

        public ReplayRequestQueue(AppSettings settings)
        {
            this.fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.ReplayRequestsFileName));
        }

        public async Task EnqueueRequestAsync(ReplayRequest request)
        {
            using (Stream stream = this.fileInfo.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                List<ReplayRequest> requests = await JsonSerializer.DeserializeAsync<List<ReplayRequest>>(stream);
                requests.Add(request);
                stream.Position = 0;
                await JsonSerializer.SerializeAsync(stream, requests, new JsonSerializerOptions { WriteIndented = true });
                await stream.FlushAsync();
            }
        }

        public async Task<ReplayRequest> GetNextRequestAsync()
        {
            using (Stream stream = this.fileInfo.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                List<ReplayRequest> requests = await JsonSerializer.DeserializeAsync<List<ReplayRequest>>(stream);

                if (requests.Count > 0)
                {
                    ReplayRequest request = requests[0];

                    if (requests.Remove(request))
                    {
                        await JsonSerializer.SerializeAsync(stream, requests, new JsonSerializerOptions { WriteIndented = true });
                        await stream.FlushAsync();

                        return request;
                    }
                }
            }

            return null;
        }
    }
}
