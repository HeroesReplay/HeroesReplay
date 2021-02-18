using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Reports;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class ReportCommand : Command
    {
        public ReportCommand() : base("report", "Generate a spectator report for a .StormReplay file.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddReportServices(cancellationToken, typeof(ReplayFileProvider)).BuildServiceProvider().CreateScope())
            {
                var reportWriter = scope.ServiceProvider.GetRequiredService<ISpectateReportWriter>();

                await reportWriter.OutputReportAsync();
            }
        }
    }
}