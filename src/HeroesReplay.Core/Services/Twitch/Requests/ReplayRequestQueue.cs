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
            fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.ReplayRequestsFileName));
        }

        public async Task<int> GetTotalQueuedItems()
        {
            if (fileInfo.Exists)
            {
                List<ReplayRequest> requests = JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName));
                return requests.Count;
            }

            return 0;
        }

        public async Task<bool> EnqueueRequestAsync(ReplayRequest request)
        {
            if (!fileInfo.Exists)
            {
                // validate replay ID = supported version
                await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<ReplayRequest>() { request }, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            else
            {
                List<ReplayRequest> requests = JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName));
                requests.Add(request);
                await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
        }

        public async Task<ReplayRequest> GetNextRequestAsync()
        {
            if (fileInfo.Exists)
            {
                List<ReplayRequest> requests = JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName));

                if (requests.Count > 0)
                {
                    ReplayRequest request = requests[0];

                    if (requests.Remove(request))
                    {
                        await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                        return request;
                    }
                }
            }

            return null;
        }
    }
}
