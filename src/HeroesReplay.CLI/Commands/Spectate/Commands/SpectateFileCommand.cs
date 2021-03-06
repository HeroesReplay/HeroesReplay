﻿using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Spectate.Commands
{
    public class SpectateFileCommand : Command
    {
        public SpectateFileCommand() : base("file", "The individual .StormReplay file to spectate.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddSpectateServices(cancellationToken, typeof(ReplayFileProvider)).BuildServiceProvider())
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
                    await engine.RunAsync();
                }
            }
        }
    }
}