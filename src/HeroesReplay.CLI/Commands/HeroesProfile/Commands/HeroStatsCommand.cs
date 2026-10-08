using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

/// <summary>
/// Fetches the hero statistics behind YouTube title hooks once, now, whatever their age and
/// whether or not <c>YouTube:Titles:StatHooks:Enabled</c> is on. The download role does the same
/// every <c>HeroesProfileApi:HeroStats:RefreshInterval</c> while hooks are on.
/// </summary>
public class HeroStatsCommand : Command
{
    public HeroStatsCommand()
        : base(
            "hero-stats",
            "Fetch the Heroes Profile hero statistics for YouTube title hooks now, for the newest patch and each HeroesProfileApi:HeroStats:GameTypes entry, into Data\\HeroesProfile\\hero-stats."
        )
    {
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await RunAsync(cancellationToken);
            }
        );
    }

    private static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken, "heroesreplay-hero-stats")
            .AddHeroStatsRefresh()
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        HeroStatsRefresh refresh = scope.ServiceProvider.GetRequiredService<HeroStatsRefresh>();

        IReadOnlyList<string> written;
        try
        {
            written = await refresh
                .RefreshDueAsync(force: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HeroStatsException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        foreach (string path in written)
        {
            Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length:N0} bytes).");
        }

        if (refresh.Stopped)
        {
            Console.Error.WriteLine(
                "Heroes Profile refused the statistics calls (401 or 403). Check the API key and plan."
            );
            return 1;
        }

        if (written.Count == 0)
        {
            Console.Error.WriteLine(
                $"No hero statistics were written to {refresh.Store.Folder}. The log above says why."
            );
            return 1;
        }

        return 0;
    }
}
