using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs.Recording;
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

        var playerOption = new Option<string>("--player")
        {
            Description =
                "BattleTag of the player to follow while they are alive, for example Name#1234. The normal camera is used while they are dead.",
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

    private static string ParsePlayer(ArgumentResult result)
    {
        string text = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
        if (PlayerPriorityRequest.TryBattleTag(text, out string battleTag))
        {
            return battleTag;
        }

        result.AddError(
            $"--player must be a BattleTag. '{text}' is not a BattleTag. Example: --player Name#1234"
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
        string battleTag,
        CancellationToken cancellationToken
    )
    {
        AspireDashboardHost.EnsureRunning();
        var replayPath = new ReplayPathOptions
        {
            Path = path,
            PlayOnce = true,
            BattleTag = battleTag,
        };
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(stop.Token, typeof(ReplayFileProvider), replayPath)
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        SpectateReleaseVersion.Write(scope.ServiceProvider);
        SpectateClipTools.Check(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<BattleNetAgentReaper>().Reap("spectate start");
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        // Before the first replay: a recording an earlier spectate left running (#342).
        scope.ServiceProvider.GetRequiredService<OrphanRecordingOnStart>().Run();
        return await engine.RunAsync() ? 0 : 1;
    }
}
