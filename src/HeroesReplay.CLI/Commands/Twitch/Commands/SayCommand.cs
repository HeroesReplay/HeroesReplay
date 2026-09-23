using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.Client.Interfaces;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class SayCommand : Command
{
    public SayCommand()
        : base("say", "Connect chat and send one message to the configured channel.")
    {
        Option<string> message = new("--message")
        {
            Description = "Text to send in the channel.",
            Required = true,
        };
        Options.Add(message);
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandAsync(parseResult.GetValue(message), cancellationToken);
            }
        );
    }

    private static async Task<int> CommandAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Console.Error.WriteLine("A --message is required.");
            return 1;
        }

        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken)
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        ITwitchBot bot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
        ITwitchClient client = scope.ServiceProvider.GetRequiredService<ITwitchClient>();
        await bot.InitializeAsync();
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until && !HasJoined(client))
        {
            await Task.Delay(250, cancellationToken);
        }

        if (!HasJoined(client))
        {
            Console.Error.WriteLine("Twitch chat connected but did not join the channel.");
            return 1;
        }

        client.SendMessage(settings.Twitch.Channel, message.Trim(), settings.Twitch.DryRunMode);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        Console.WriteLine("Sent chat message to " + settings.Twitch.Channel + ".");
        return 0;
    }

    private static bool HasJoined(ITwitchClient client)
    {
        return client.IsConnected && client.JoinedChannels != null && client.JoinedChannels.Count > 0;
    }
}
