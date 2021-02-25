using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Calculators.Commands
{
    public class ReportCommand : Command
    {
        public ReportCommand() : base("report", "Generate a spectator report for a .StormReplay file.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddReportServices(cancellationToken, typeof(ReplayFileProvider)).BuildServiceProvider())
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    var reportWriter = scope.ServiceProvider.GetRequiredService<ISpectateReportWriter>();

                    await reportWriter.OutputReportAsync();
                }
            }
        }
    }
}