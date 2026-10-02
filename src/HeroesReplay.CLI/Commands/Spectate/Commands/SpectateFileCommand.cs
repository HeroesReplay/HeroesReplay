using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.ServiceHost;
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
        fileOption.Validators.Add(ValidateReplayPath);
        Options.Add(fileOption);

        var playerOption = new Option<int?>("--player")
        {
            Description =
                "Hero to follow while they are alive: 1-10, or 0 for the tenth hero. The normal camera is used while that hero is dead.",
            CustomParser = ParsePlayer,
        };
        Options.Add(playerOption);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandAsync(
                    parseResult.GetValue(fileOption),
                    parseResult.GetValue(playerOption),
                    cancellationToken
                );
            }
        );
    }

    /// <summary>
    /// Maps <c>--player</c> to a zero-based hero index. 1-9 and 10 are the heroes in order;
    /// 0 is also the tenth hero, like the spectator key.
    /// </summary>
    public static bool TryPlayerIndex(string text, out int playerIndex)
    {
        if (string.Equals(text?.Trim(), "10", StringComparison.Ordinal))
        {
            playerIndex = 9;
            return true;
        }

        return PlayerPriorityRequest.TrySlot(text, out playerIndex);
    }

    private static int? ParsePlayer(ArgumentResult result)
    {
        string text = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
        if (TryPlayerIndex(text, out int playerIndex))
        {
            return playerIndex;
        }

        result.AddError(
            $"--player must be 1-10, or 0 for the tenth hero. '{text}' is not a hero. Example: --player 1"
        );
        return null;
    }

    private static void ValidateReplayPath(OptionResult result)
    {
        string path = result.GetValueOrDefault<string>();
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path))
        {
            result.AddError($"Replay path does not exist: {path}");
        }
    }

    protected async Task<int> CommandAsync(
        string path,
        int? playerIndex,
        CancellationToken cancellationToken
    )
    {
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
        return await engine.RunAsync() ? 0 : 1;
    }
}
