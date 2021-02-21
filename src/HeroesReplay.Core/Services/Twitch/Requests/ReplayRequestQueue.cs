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
                // Replay ID Request
                if (request.ReplayId.HasValue)
                {
                    HeroesProfileReplay heroesProfileReplay = await heroesProfileService.GetReplayAsync(request.ReplayId.Value);

                    if (heroesProfileReplay != null)
                    {
                        request.ReplayId = heroesProfileReplay.Id;

                        if (!fileInfo.Exists)
                        {
                            await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<ReplayRequest>() { request }, new JsonSerializerOptions { WriteIndented = true }));
                            return new ReplayRequestResponse(success: true, message: $"Request has been queued. Position: {1}");
                        }
                        else
                        {
                            List<ReplayRequest> requests = new List<ReplayRequest>(JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName))) { request };
                            await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                            return new ReplayRequestResponse(success: true, message: $"Request has been queued. Position: {requests.Count}");
                        }
                    }
                }
                else
                {
                    // Map Request
                    HeroesProfileReplay heroesProfileReplay = await heroesProfileService.GetReplayAsync(mode: request.GameMode, tier: request.Tier, map: request.Map);

                    if (heroesProfileReplay != null)
                    {
                        request.ReplayId = heroesProfileReplay.Id;

                        if (!fileInfo.Exists)
                        {
                            await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<ReplayRequest>() { request }, new JsonSerializerOptions { WriteIndented = true }));
                            return new ReplayRequestResponse(success: true, message: $"Request has been queued. Position: {1}");
                        }
                        else
                        {
                            List<ReplayRequest> requests = new List<ReplayRequest>(JsonSerializer.Deserialize<List<ReplayRequest>>(await File.ReadAllTextAsync(fileInfo.FullName))) { request };
                            await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                            return new ReplayRequestResponse(success: true, message: $"Request has been queued. Position: {requests.Count}");
                        }
                    }
                }

                return new ReplayRequestResponse(success: false, message: "Request not handled.");
            }
            catch (Exception e)
            {
                return new ReplayRequestResponse(success: false, message: e.Message);
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
