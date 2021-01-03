﻿using System;
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
        private readonly IAdminChecker adminChecker;

        public CommandLineService(IAdminChecker adminChecker)
        {
            this.adminChecker = adminChecker;
        }

        public Parser GetParser()
        {
            return new CommandLineBuilder(new HeroesReplayCommand())
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
            context.Console.Error.WriteLine(exception.Message);
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
            if (context.ParseResult.CommandResult.Command.Parents[0].Name.Equals("spectate") && !adminChecker.IsAdministrator())
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