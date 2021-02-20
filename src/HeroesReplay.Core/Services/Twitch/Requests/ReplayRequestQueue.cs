using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{

    public class ReplayRequestQueue : IReplayRequestQueue
    {
        private readonly FileInfo fileInfo;
        private readonly IHeroesProfileService heroesProfileService;

        public ReplayRequestQueue(IHeroesProfileService heroesProfileService, AppSettings settings)
        {
            fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.ReplayRequestsFileName));
            this.heroesProfileService = heroesProfileService;
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

        public async Task<ReplayRequestResponse> EnqueueRequestAsync(ReplayRequest request)
        {
            try
            {
                HeroesProfileReplay heroesProfileReplay = await heroesProfileService.GetReplayAsync(request.ReplayId);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<ReplayRequest>() { request }, new JsonSerializerOptions { WriteIndented = true }));
                    return new ReplayRequestResponse(success: true, message: "Request has been requeued.", 1);
                }
                else
                {
                    List<ReplayRequest> requests = new List<ReplayRequest>(JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName))) { request };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                    return new ReplayRequestResponse(success: true, message: "Request has been requeued.", requests.Count);
                }
            }
            catch (Exception e)
            {
                return new ReplayRequestResponse(success: false, message: e.Message, null);
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
