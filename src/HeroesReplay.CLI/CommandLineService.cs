﻿using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Spectator;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.CLI
{
    public class CommandLineService
    {
        private readonly IConfiguration configuration;
        private readonly RootReplayCommand command;
        private readonly StormReplayConsumer replayConsumer;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly AdminChecker adminChecker;

        public CommandLineService(IConfiguration configuration, RootReplayCommand command, CancellationTokenProvider tokenProvider, StormReplayConsumer replayConsumer, AdminChecker adminChecker)
        {
            this.configuration = configuration;
            this.command = command;
            this.replayConsumer = replayConsumer;
            this.tokenProvider = tokenProvider;
            this.adminChecker = adminChecker;
        }

        public Parser CreateCliParser()
        {
            this.command.Handler = CommandHandler.Create((DirectoryInfo bnet, string path, bool launch, CancellationToken token) => InvokeAsync(bnet, path, launch, token));

            return new CommandLineBuilder(command)
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

        // ENTRY POINT TO SERVICE EXECUTION
        private Task InvokeAsync(DirectoryInfo bnet, string path, bool launch, CancellationToken token)
        {
            configuration["bnet"] = bnet.FullName;

            tokenProvider.Token = token;

            return replayConsumer.ReplayAsync(path, launch);
        }

        private void OnException(Exception exception, InvocationContext context)
        {
            // I dont know what i would put here
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