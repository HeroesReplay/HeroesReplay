using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;

namespace HeroesReplay.Core.Services.Twitch;

public class FakeTwitchClient : ITwitchClient
{
    private readonly ILogger<FakeTwitchBot> logger;

    public bool AutoReListenOnException { get; set; }
    public MessageEmoteCollection ChannelEmotes { get; }
    public ConnectionCredentials ConnectionCredentials { get; }
    public bool DisableAutoPong { get; set; }
    public bool IsConnected { get; }
    public bool IsInitialized { get; }
    public IReadOnlyList<JoinedChannel> JoinedChannels { get; }
    public WhisperMessage PreviousWhisper { get; }
    public string TwitchUsername { get; }
    public bool WillReplaceEmotes { get; set; }

#pragma warning disable CS0067
    public event EventHandler<OnChannelStateChangedArgs> OnChannelStateChanged;
    public event EventHandler<OnChatClearedArgs> OnChatCleared;
    public event EventHandler<OnChatColorChangedArgs> OnChatColorChanged;
    public event EventHandler<OnChatCommandReceivedArgs> OnChatCommandReceived;
    public event EventHandler<OnConnectedArgs> OnConnected;
    public event EventHandler<OnConnectionErrorArgs> OnConnectionError;
    public event EventHandler<OnExistingUsersDetectedArgs> OnExistingUsersDetected;
    public event EventHandler<OnGiftedSubscriptionArgs> OnGiftedSubscription;
    public event EventHandler<OnIncorrectLoginArgs> OnIncorrectLogin;
    public event EventHandler<OnJoinedChannelArgs> OnJoinedChannel;
    public event EventHandler<OnLeftChannelArgs> OnLeftChannel;
    public event EventHandler<OnLogArgs> OnLog;
    public event EventHandler<OnMessageReceivedArgs> OnMessageReceived;
    public event EventHandler<OnMessageSentArgs> OnMessageSent;
    public event EventHandler<OnModeratorJoinedArgs> OnModeratorJoined;
    public event EventHandler<OnModeratorLeftArgs> OnModeratorLeft;
    public event EventHandler<OnModeratorsReceivedArgs> OnModeratorsReceived;
    public event EventHandler<OnNewSubscriberArgs> OnNewSubscriber;
    public event EventHandler<OnRaidNotificationArgs> OnRaidNotification;
    public event EventHandler<OnReSubscriberArgs> OnReSubscriber;
    public event EventHandler<OnUserBannedArgs> OnUserBanned;
    public event EventHandler<OnUserJoinedArgs> OnUserJoined;
    public event EventHandler<OnUserLeftArgs> OnUserLeft;
    public event EventHandler<OnUserStateChangedArgs> OnUserStateChanged;
    public event EventHandler<OnUserTimedoutArgs> OnUserTimedout;
    public event EventHandler<OnWhisperCommandReceivedArgs> OnWhisperCommandReceived;
    public event EventHandler<OnWhisperReceivedArgs> OnWhisperReceived;
    public event EventHandler<OnWhisperSentArgs> OnWhisperSent;
    public event EventHandler<OnVIPsReceivedArgs> OnVIPsReceived;
    public event EventHandler<OnCommunitySubscriptionArgs> OnCommunitySubscription;
    public event EventHandler<OnMessageClearedArgs> OnMessageCleared;
    public event EventHandler<OnRequiresVerifiedEmailArgs> OnRequiresVerifiedEmail;
    public event EventHandler<OnRequiresVerifiedPhoneNumberArgs> OnRequiresVerifiedPhoneNumber;
    public event EventHandler<OnBannedEmailAliasArgs> OnBannedEmailAlias;
    public event EventHandler<OnUserIntroArgs> OnUserIntro;
    public event EventHandler<OnAnnouncementArgs> OnAnnouncement;
    public event EventHandler<OnDisconnectedEventArgs> OnDisconnected;
    public event EventHandler<OnSendReceiveDataArgs> OnSendReceiveData;
    public event EventHandler<OnMessageThrottledEventArgs> OnMessageThrottled;
    public event EventHandler<OnWhisperThrottledEventArgs> OnWhisperThrottled;
    public event EventHandler<OnErrorEventArgs> OnError;
    public event EventHandler<OnReconnectedEventArgs> OnReconnected;
#pragma warning restore CS0067

    public FakeTwitchClient(ILogger<FakeTwitchBot> logger)
    {
        this.logger = logger;
    }

    public void AddChatCommandIdentifier(char identifier) { }

    public void AddWhisperCommandIdentifier(char identifier) { }

    public bool Connect()
    {
        logger.LogInformation("Fake Twitch client connect.");
        return true;
    }

    public void Disconnect() { }

    public void Reconnect() { }

    public void JoinChannel(string channel, bool overrideCheck = false) { }

    public void LeaveChannel(string channel) { }

    public void OnReadLineTest(string rawIrc) { }

    public void SendMessage(string channel, string message, bool dryRun = false)
    {
        logger.LogInformation($"fake message: [{channel}]: {message}");
    }

    public void SendReply(string channel, string replyToId, string message, bool dryRun = false) { }

    public void SendQueuedItem(string message) { }

    public void SendRaw(string message) { }

    public void SendWhisper(string receiver, string message, bool dryRun = false) { }

    public void RemoveChatCommandIdentifier(char identifier) { }

    public void RemoveWhisperCommandIdentifier(char identifier) { }

    public void Initialize(
        ConnectionCredentials credentials,
        string channel = null,
        char chatCommandIdentifier = '!',
        char whisperCommandIdentifier = '!',
        bool autoReListenOnExceptions = true
    ) { }

    public void Initialize(
        ConnectionCredentials credentials,
        List<string> channels,
        char chatCommandIdentifier = '!',
        char whisperCommandIdentifier = '!',
        bool autoReListenOnExceptions = true
    ) { }

    public void SetConnectionCredentials(ConnectionCredentials credentials) { }

    public JoinedChannel GetJoinedChannel(string channel) => null;

    public void LeaveChannel(JoinedChannel channel) { }

    public void SendMessage(JoinedChannel channel, string message, bool dryRun = false)
    {
        logger.LogInformation($"fake message: [{channel?.Channel}]: {message}");
    }

    public void SendReply(
        JoinedChannel channel,
        string replyToId,
        string message,
        bool dryRun = false
    ) { }
}
