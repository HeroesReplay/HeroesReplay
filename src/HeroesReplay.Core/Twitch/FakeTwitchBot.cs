using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.ChatMessages;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Twitch;

public class FakeTwitchBot : ITwitchBot
{
    private readonly ILogger<FakeTwitchBot> logger;
    private readonly IOnMessageHandler onMessageHandler;
    private readonly CancellationTokenProvider tokenProvider;

    public FakeTwitchBot(
        ILogger<FakeTwitchBot> logger,
        IOnMessageHandler onMessageHandler,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger;
        this.onMessageHandler = onMessageHandler;
        this.tokenProvider = tokenProvider;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Task.Run(TriggerOnRewardHandler), Task.Run(TriggerOnMessageHandler));
    }

    private async Task TriggerOnMessageHandler()
    {
        var messages = new ChatMessage[]
        {
            new ChatMessage(
                null,
                "userId",
                "delegate_",
                "Delegate_",
                "colorHex",
                System.Drawing.Color.Transparent,
                null,
                "!requests",
                TwitchLib.Client.Enums.UserType.Viewer,
                "SaltySadism",
                "id",
                false,
                0,
                "roomId",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                TwitchLib.Client.Enums.Noisy.NotSet,
                null,
                null,
                null,
                null,
                0,
                0
            ),
            new ChatMessage(
                null,
                "userId",
                "delegate_",
                "Delegate_",
                "colorHex",
                System.Drawing.Color.Transparent,
                null,
                "!requests 1",
                TwitchLib.Client.Enums.UserType.Viewer,
                "SaltySadism",
                "id",
                false,
                0,
                "roomId",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                TwitchLib.Client.Enums.Noisy.NotSet,
                null,
                null,
                null,
                null,
                0,
                0
            ),
            new ChatMessage(
                null,
                "userId",
                "delegate_",
                "Delegate_",
                "colorHex",
                System.Drawing.Color.Transparent,
                null,
                "!requests me",
                TwitchLib.Client.Enums.UserType.Viewer,
                "SaltySadism",
                "id",
                false,
                0,
                "roomId",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                TwitchLib.Client.Enums.Noisy.NotSet,
                null,
                null,
                null,
                null,
                0,
                0
            ),
            new ChatMessage(
                null,
                "userId",
                "userName",
                "displayName",
                "colorHex",
                System.Drawing.Color.Transparent,
                null,
                "message",
                TwitchLib.Client.Enums.UserType.Viewer,
                "SaltySadism",
                "id",
                false,
                0,
                "roomId",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                TwitchLib.Client.Enums.Noisy.NotSet,
                null,
                null,
                null,
                null,
                0,
                0
            ),
        };

        while (!tokenProvider.Token.IsCancellationRequested)
        {
            foreach (var message in messages)
            {
                onMessageHandler.Handle(new OnMessageReceivedArgs() { ChatMessage = message });

                logger.LogDebug("waiting to send messages...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private async Task TriggerOnRewardHandler()
    {
        while (!tokenProvider.Token.IsCancellationRequested)
        {
            logger.LogDebug("waiting to send reward deemed...");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}
