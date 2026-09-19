using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

public class ReportCommand : Command
{
    public ReportCommand()
        : base("report", "Generate a spectator report for a .StormReplay file.")
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
        var replayPath = new ReplayPathOptions { Path = path, PlayOnce = true };
        using ServiceProvider provider = new ServiceCollection()
            .AddReportServices(cancellationToken, typeof(ReplayFileProvider), replayPath)
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        ISpectateReportWriter reportWriter =
            scope.ServiceProvider.GetRequiredService<ISpectateReportWriter>();
        await reportWriter.OutputReportAsync();
    }
}
