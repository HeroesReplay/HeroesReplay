using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Spectate.Commands;

public class SpectateFileCommand : Command
{
    public SpectateFileCommand()
        : base("file", "Spectate one .StormReplay file, or each file in a directory, then exit.")
    {
        var fileOption = new Option<string>("--file")
        {
            Description =
                "Path to a .StormReplay file or a directory of replays. Defaults to Location:ReplaySource.",
        };
        fileOption.Aliases.Add("-f");
        Options.Add(fileOption);

        var playerOption = new Option<string>("--player")
        {
            Description =
                "Hero to follow while they are alive. 1-9, or 0 for the tenth hero. The normal camera is used while that hero is dead.",
        };
        Options.Add(playerOption);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(
                    parseResult.GetValue(fileOption),
                    parseResult.GetValue(playerOption),
                    cancellationToken
                );
            }
        );
    }

    protected async Task CommandAsync(
        string path,
        string player,
        CancellationToken cancellationToken
    )
    {
        int? playerIndex = null;
        if (!string.IsNullOrWhiteSpace(player))
        {
            if (!PlayerPriorityRequest.TrySlot(player, out int slot))
            {
                Console.Error.WriteLine(
                    "Player must be 1-9, or 0 for the tenth hero. Example: --player 1"
                );
                return;
            }

            playerIndex = slot;
        }

        AspireDashboardHost.EnsureRunning();
        var replayPath = new ReplayPathOptions
        {
            Path = path,
            PlayOnce = true,
            PlayerIndex = playerIndex,
        };
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(stop.Token, typeof(ReplayFileProvider), replayPath)
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        await engine.RunAsync();
    }
}
