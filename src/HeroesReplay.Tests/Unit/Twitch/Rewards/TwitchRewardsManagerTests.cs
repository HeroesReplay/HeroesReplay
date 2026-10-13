using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Helix;
using TwitchLib.Api.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Rewards;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchRewardsManagerTests
{
    [Fact]
    public async Task DeleteUnrankedDraftRewards_DeletesOnlyUdTitles()
    {
        var http = new RecordingHttpHandler();
        ITwitchAPI api = FakeTwitchApi.Create(http);
        var manager = new TwitchRewardsManager(
            NullLogger<TwitchRewardsManager>.Instance,
            api,
            new EmptyRewards(),
            new AppSettings
            {
                Twitch = new TwitchSettings { Channel = "saltysadism", AccessToken = "test-token" },
            }
        );

        UnrankedDraftRewardRemoval removed = await manager.DeleteUnrankedDraftRewardsAsync();

        Assert.Equal(
            new[]
            {
                "Random (UD)",
                "Infernal Shrines (UD)",
                "Infernal Shrines (Rank UD)",
                "Unranked Draft",
            },
            removed.Deleted
        );
        Assert.Equal(new[] { "Rank (UD)" }, removed.Failed);
        Assert.DoesNotContain(http.DeletedIds, id => id == "qm-map");
        Assert.DoesNotContain(http.DeletedIds, id => id == "sl-random");
        Assert.DoesNotContain(http.DeletedIds, id => id == "aram-rank");
        Assert.Contains("ud-random", http.DeletedIds);
        Assert.Contains("ud-map", http.DeletedIds);
        Assert.Contains("ud-rank", http.DeletedIds);
        Assert.Contains("ud-named", http.DeletedIds);
        Assert.DoesNotContain("ud-fail", http.DeletedIds);
    }

    /// <summary>
    /// #425: the sync deletes the Storm League rewards of maps out of the pool before it creates
    /// or updates the catalog, and leaves every other reward alone.
    /// </summary>
    [Fact]
    public async Task CreateOrUpdate_DeletesRetiredRewards_AndUpdatesTheRest()
    {
        var http = new SyncHttpHandler();
        var manager = new TwitchRewardsManager(
            NullLogger<TwitchRewardsManager>.Instance,
            FakeTwitchApi.Create(http),
            new RetiringRewards(),
            new AppSettings
            {
                Twitch = new TwitchSettings { Channel = "saltysadism", AccessToken = "test-token" },
            }
        );

        await manager.CreateOrUpdateAsync();

        Assert.Equal(new[] { "retired-sl", "retired-rank" }, http.Calls("DELETE"));
        Assert.Equal(new[] { "qm", "kept" }, http.Calls("PATCH"));
        Assert.Empty(http.Calls("POST"));
    }

    private sealed class RetiringRewards : ICustomRewardsHolder
    {
        public List<HeroesReplay.Core.Twitch.Rewards.SupportedReward> Rewards { get; } =
            new()
            {
                new HeroesReplay.Core.Twitch.Rewards.SupportedReward(
                    RewardType.QM | RewardType.Map,
                    "Hanamura Temple (QM)",
                    "Hanamura Temple",
                    HeroesReplay.Core.Shared.GameType.QuickMatch,
                    250
                ),
                new HeroesReplay.Core.Twitch.Rewards.SupportedReward(
                    RewardType.SL | RewardType.Map,
                    "Cursed Hollow (SL)",
                    "Cursed Hollow",
                    HeroesReplay.Core.Shared.GameType.StormLeague,
                    250
                ),
            };

        public IReadOnlyList<string> RetiredTitles { get; } =
            new[] { "Hanamura Temple (SL)", "Hanamura Temple (Rank SL)" };

        public bool TryGetReward(
            TwitchLib.PubSub.Events.OnRewardRedeemedArgs args,
            out HeroesReplay.Core.Twitch.Rewards.SupportedReward reward
        )
        {
            reward = null;
            return false;
        }
    }

    private sealed class SyncHttpHandler : IHttpCallHandler
    {
        private readonly List<(string Method, string Id)> calls = new();

        public IReadOnlyList<string> Calls(string method) =>
            calls
                .Where(call =>
                    string.Equals(call.Method, method, StringComparison.OrdinalIgnoreCase)
                )
                .Select(call => call.Id)
                .ToList();

        public Task<KeyValuePair<int, string>> GeneralRequestAsync(
            string url,
            string method,
            string payload = null,
            ApiVersion api = ApiVersion.Helix,
            string clientId = null,
            string accessToken = null
        )
        {
            if (url.Contains("/users", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":[{\"id\":\"487238352\",\"login\":\"saltysadism\"}]}"
                    )
                );
            }

            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":["
                            + "{\"id\":\"retired-sl\",\"title\":\"Hanamura Temple (SL)\"},"
                            + "{\"id\":\"retired-rank\",\"title\":\"Hanamura Temple (Rank SL)\"},"
                            + "{\"id\":\"qm\",\"title\":\"Hanamura Temple (QM)\"},"
                            + "{\"id\":\"kept\",\"title\":\"Cursed Hollow (SL)\"},"
                            + "{\"id\":\"other\",\"title\":\"Hydrate\"}"
                            + "]}"
                    )
                );
            }

            calls.Add((method, IdOf(url)));
            return Task.FromResult(
                new KeyValuePair<int, string>(
                    string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase) ? 204 : 200,
                    string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : "{\"data\":[]}"
                )
            );
        }

        public Task PutBytesAsync(string url, byte[] payload) => Task.CompletedTask;

        public Task<int> RequestReturnResponseCodeAsync(
            string url,
            string method,
            List<KeyValuePair<string, string>> getParams = null
        ) => Task.FromResult(200);

        private static string IdOf(string url)
        {
            string query = new Uri(url).Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                if (part.StartsWith("id=", StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(part.Substring(3));
                }
            }

            return url;
        }
    }

    private sealed class EmptyRewards : ICustomRewardsHolder
    {
        public List<HeroesReplay.Core.Twitch.Rewards.SupportedReward> Rewards { get; } = new();

        public bool TryGetReward(
            TwitchLib.PubSub.Events.OnRewardRedeemedArgs args,
            out HeroesReplay.Core.Twitch.Rewards.SupportedReward reward
        )
        {
            reward = null;
            return false;
        }
    }

    private sealed class RecordingHttpHandler : IHttpCallHandler
    {
        public List<string> DeletedIds { get; } = new();

        public Task<KeyValuePair<int, string>> GeneralRequestAsync(
            string url,
            string method,
            string payload = null,
            ApiVersion api = ApiVersion.Helix,
            string clientId = null,
            string accessToken = null
        )
        {
            if (url.Contains("/users", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":[{\"id\":\"487238352\",\"login\":\"saltysadism\"}]}"
                    )
                );
            }

            if (
                url.Contains("custom_rewards", StringComparison.Ordinal)
                && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":["
                            + "{\"id\":\"ud-random\",\"title\":\"Random (UD)\"},"
                            + "{\"id\":\"ud-map\",\"title\":\"Infernal Shrines (UD)\"},"
                            + "{\"id\":\"ud-rank\",\"title\":\"Infernal Shrines (Rank UD)\"},"
                            + "{\"id\":\"ud-named\",\"title\":\"Unranked Draft\"},"
                            + "{\"id\":\"ud-fail\",\"title\":\"Rank (UD)\"},"
                            + "{\"id\":\"qm-map\",\"title\":\"Garden of Terror (QM)\"},"
                            + "{\"id\":\"sl-random\",\"title\":\"Random (SL)\"},"
                            + "{\"id\":\"aram-rank\",\"title\":\"Lost Cavern (Rank ARAM)\"}"
                            + "]}"
                    )
                );
            }

            if (
                url.Contains("custom_rewards", StringComparison.Ordinal)
                && string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (url.Contains("id=ud-fail", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("delete refused");
                }

                DeletedIds.Add(IdFrom(url));
                return Task.FromResult(new KeyValuePair<int, string>(204, ""));
            }

            return Task.FromResult(new KeyValuePair<int, string>(200, "{}"));
        }

        public Task PutBytesAsync(string url, byte[] payload) => Task.CompletedTask;

        public Task<int> RequestReturnResponseCodeAsync(
            string url,
            string method,
            List<KeyValuePair<string, string>> getParams = null
        ) => Task.FromResult(200);

        private static string IdFrom(string url)
        {
            string query = new Uri(url).Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                if (part.StartsWith("id=", StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(part.Substring(3));
                }
            }

            return url;
        }
    }

    private class FakeTwitchApi : DispatchProxy
    {
        private Helix helix;

        public static ITwitchAPI Create(IHttpCallHandler http)
        {
            object created = DispatchProxy.Create<ITwitchAPI, FakeTwitchApi>();
            var proxy = (FakeTwitchApi)created;
            proxy.helix = new Helix(
                settings: new TwitchLib.Api.Core.ApiSettings
                {
                    ClientId = "test-client",
                    AccessToken = "test-token",
                },
                http: http
            );
            return (ITwitchAPI)created;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == "get_Helix")
            {
                return helix;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
