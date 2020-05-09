using System;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.CommandLine.Rendering;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI
{
    public class CommandLineService
    {
        private readonly AdminChecker adminChecker;

        public CommandLineService(AdminChecker adminChecker)
        {
            this.adminChecker = adminChecker;
        }

        public Parser GetParser()
        {
            return new CommandLineBuilder(new HeroesReplayRootCommand())
                .UseMiddleware(CheckAdminMiddlewareAsync)
                .UseMiddleware(CheckOsRequirementAsync)
                .UseParseErrorReporting()
                .CancelOnProcessTermination()
                .UseVersionOption()
                .UseHelp()
                .UseTypoCorrections()
                .UseSuggestDirective()
                .UseExceptionHandler(OnException)
                .UseAnsiTerminalWhenAvailable()
                .Build();
        }

        private void OnException(Exception exception, InvocationContext context)
        {

        }

        private async Task CheckOsRequirementAsync(InvocationContext context, Func<InvocationContext, Task> next)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                context.Console.Out.WriteLine("Windows is the only supported OS.");
            }
            else
            {
                await next(context);
            }
        }

        private async Task CheckAdminMiddlewareAsync(InvocationContext context, Func<InvocationContext, Task> next)
        {
            if (!adminChecker.IsAdministrator())
            {
                context.Console.Out.WriteLine("You must be running this application as an administrator.");
            }
            else
            {
                await next(context);
            }
        }
    }
}