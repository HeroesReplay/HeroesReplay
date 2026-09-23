using System;
using System.Globalization;
using System.Text.Json;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch;

public static class EventSubRedemption
{
    public const string AddType = "channel.channel_points_custom_reward_redemption.add";

    public static bool TryReadWelcome(string json, out string sessionId, out int keepaliveSeconds)
    {
        sessionId = null;
        keepaliveSeconds = 30;
        if (!TryRoot(json, out JsonElement root))
        {
            return false;
        }

        if (!string.Equals(MessageType(root), "session_welcome", StringComparison.Ordinal))
        {
            return false;
        }

        if (
            !root.TryGetProperty("payload", out JsonElement payload)
            || !payload.TryGetProperty("session", out JsonElement session)
        )
        {
            return false;
        }

        sessionId = StringOf(session, "id");
        if (session.TryGetProperty("keepalive_timeout_seconds", out JsonElement keepalive))
        {
            if (keepalive.TryGetInt32(out int seconds) && seconds > 0)
            {
                keepaliveSeconds = seconds;
            }
        }

        return !string.IsNullOrWhiteSpace(sessionId);
    }

    public static bool TryReadReconnect(string json, out string reconnectUrl)
    {
        reconnectUrl = null;
        if (!TryRoot(json, out JsonElement root))
        {
            return false;
        }

        if (!string.Equals(MessageType(root), "session_reconnect", StringComparison.Ordinal))
        {
            return false;
        }

        if (
            !root.TryGetProperty("payload", out JsonElement payload)
            || !payload.TryGetProperty("session", out JsonElement session)
        )
        {
            return false;
        }

        reconnectUrl = StringOf(session, "reconnect_url");
        return !string.IsNullOrWhiteSpace(reconnectUrl);
    }

    public static bool TryReadReward(string json, out OnRewardRedeemedArgs args)
    {
        args = null;
        if (!TryRoot(json, out JsonElement root))
        {
            return false;
        }

        if (!string.Equals(MessageType(root), "notification", StringComparison.Ordinal))
        {
            return false;
        }

        string subscriptionType = null;
        if (root.TryGetProperty("metadata", out JsonElement metadata))
        {
            subscriptionType = StringOf(metadata, "subscription_type");
        }

        if (!string.Equals(subscriptionType, AddType, StringComparison.Ordinal))
        {
            return false;
        }

        if (
            !root.TryGetProperty("payload", out JsonElement payload)
            || !payload.TryGetProperty("event", out JsonElement ev)
            || !ev.TryGetProperty("reward", out JsonElement reward)
        )
        {
            return false;
        }

        args = new OnRewardRedeemedArgs
        {
            ChannelId = StringOf(ev, "broadcaster_user_id"),
            Login = StringOf(ev, "user_login"),
            DisplayName = StringOf(ev, "user_name"),
            Message = StringOf(ev, "user_input") ?? string.Empty,
            RewardTitle = StringOf(reward, "title"),
            RewardPrompt = StringOf(reward, "prompt") ?? string.Empty,
            Status = StringOf(ev, "status"),
            TimeStamp = ReadTime(StringOf(ev, "redeemed_at")),
        };
        if (reward.TryGetProperty("cost", out JsonElement cost) && cost.TryGetInt32(out int points))
        {
            args.RewardCost = points;
        }

        if (Guid.TryParse(StringOf(ev, "id"), out Guid redemptionId))
        {
            args.RedemptionId = redemptionId;
        }

        if (Guid.TryParse(StringOf(reward, "id"), out Guid rewardId))
        {
            args.RewardId = rewardId;
        }

        return !string.IsNullOrWhiteSpace(args.RewardTitle);
    }

    private static bool TryRoot(string json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string MessageType(JsonElement root)
    {
        if (!root.TryGetProperty("metadata", out JsonElement metadata))
        {
            return null;
        }

        return StringOf(metadata, "message_type");
    }

    private static string StringOf(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static DateTime ReadTime(string text)
    {
        if (
            DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed
            )
        )
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }
}
