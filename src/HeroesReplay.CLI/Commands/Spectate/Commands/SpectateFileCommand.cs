using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
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

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(parseResult.GetValue(fileOption), cancellationToken);
            }
        );
    }

    protected async Task CommandAsync(string path, CancellationToken cancellationToken)
    {
        AspireDashboardHost.EnsureRunning();
        var replayPath = new ReplayPathOptions { Path = path, PlayOnce = true };
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(stop.Token, typeof(ReplayFileProvider), replayPath)
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        await engine.RunAsync();
    }
}
