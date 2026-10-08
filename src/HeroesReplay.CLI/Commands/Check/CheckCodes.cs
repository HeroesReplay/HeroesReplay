using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.CLI.Commands.Check;

/// <summary>
/// The stable codes of <c>check</c> (#311): <c>check.&lt;target&gt;.&lt;reason&gt;</c>, with a
/// hyphen in the target written as an underscore (<c>check.twitch_extension.key_rejected</c>).
/// Every target has <c>.ok</c> and <c>.error</c> (an unexpected exception). A code is never
/// renamed or reused for another reason; a new reason gets a new code.
/// </summary>
public static class CheckCodes
{
    /// <summary>The envelope code when several targets ran and every one passed.</summary>
    public const string AllOk = "check.ok";

    public const string ConfigOk = "check.config.ok";
    public const string ConfigHeroesProfileKeyMissing = "check.config.heroesprofile_key_missing";
    public const string ConfigError = "check.config.error";

    public const string HeroesProfileOk = "check.heroesprofile.ok";
    public const string HeroesProfileKeyMissing = "check.heroesprofile.key_missing";

    /// <summary>GET /replays answered nothing usable (the fallback id): the key or the API.</summary>
    public const string HeroesProfileRequestFailed = "check.heroesprofile.request_failed";
    public const string HeroesProfileError = "check.heroesprofile.error";

    public const string ObsOk = "check.obs.ok";

    /// <summary>No Identify from the websocket within 8 s: OBS closed or the server off.</summary>
    public const string ObsUnreachable = "check.obs.unreachable";
    public const string ObsProfileMismatch = "check.obs.profile_mismatch";
    public const string ObsCollectionMismatch = "check.obs.collection_mismatch";
    public const string ObsSelectionUnreadable = "check.obs.selection_unreadable";

    /// <summary>The packaged collection, the profile, or an asset this install needs is missing.</summary>
    public const string ObsFilesInvalid = "check.obs.files_invalid";
    public const string ObsError = "check.obs.error";

    public const string TwitchOk = "check.twitch.ok";
    public const string TwitchCredentialsMissing = "check.twitch.credentials_missing";
    public const string TwitchUserNotFound = "check.twitch.user_not_found";
    public const string TwitchPredictionsScopeMissing = "check.twitch.predictions_scope_missing";
    public const string TwitchChatScopeMissing = "check.twitch.chat_scope_missing";
    public const string TwitchRedemptionsScopeMissing = "check.twitch.redemptions_scope_missing";
    public const string TwitchError = "check.twitch.error";

    public const string ClientOk = "check.client.ok";
    public const string ClientPresetMismatch = "check.client.preset_mismatch";
    public const string ClientError = "check.client.error";

    public const string ConnectivityOk = "check.connectivity.ok";

    /// <summary>1.1.1.1, Twitch, and Heroes Profile all failed.</summary>
    public const string ConnectivityOffline = "check.connectivity.offline";
    public const string ConnectivityError = "check.connectivity.error";

    public const string TimerOk = "check.timer.ok";
    public const string TimerGameNotRunning = "check.timer.game_not_running";

    /// <summary>The game runs, but two memory reads did not move forward: no match is playing.</summary>
    public const string TimerClockNotRunning = "check.timer.clock_not_running";
    public const string TimerError = "check.timer.error";

    public const string TwitchExtensionOk = "check.twitch_extension.ok";

    /// <summary><c>TwitchExtension:Enabled</c> is false. The check passes.</summary>
    public const string TwitchExtensionDisabled = "check.twitch_extension.disabled";
    public const string TwitchExtensionKeyMissing = "check.twitch_extension.key_missing";

    /// <summary>uploader/whoami answered 401 or 403: the uploader key is not valid.</summary>
    public const string TwitchExtensionKeyRejected = "check.twitch_extension.key_rejected";
    public const string TwitchExtensionRateLimited = "check.twitch_extension.rate_limited";

    /// <summary>No HTTP answer: Heroes Profile was not reached, or its Twitch URI is not set.</summary>
    public const string TwitchExtensionUnreachable = "check.twitch_extension.unreachable";
    public const string TwitchExtensionHttpError = "check.twitch_extension.http_error";
    public const string TwitchExtensionError = "check.twitch_extension.error";

    /// <summary>The Battle.net button reads Play or Playing.</summary>
    public const string BattleNetOk = "check.battlenet.ok";

    /// <summary>The button reads Update or Updating. The check passes; do not click it.</summary>
    public const string BattleNetUpdatePending = "check.battlenet.update_pending";

    /// <summary>Battle.net is open on a page without the button. The check passes.</summary>
    public const string BattleNetButtonHidden = "check.battlenet.button_hidden";
    public const string BattleNetWindowMissing = "check.battlenet.window_missing";
    public const string BattleNetOcrUnavailable = "check.battlenet.ocr_unavailable";
    public const string BattleNetCaptureFailed = "check.battlenet.capture_failed";
    public const string BattleNetButtonUnread = "check.battlenet.button_unread";
    public const string BattleNetError = "check.battlenet.error";

    public const string FfmpegOk = "check.ffmpeg.ok";

    /// <summary>A working build that is not the pinned one. A warning: clips still cut.</summary>
    public const string FfmpegNotPinned = "check.ffmpeg." + FfmpegCheck.NotPinned;
    public const string FfmpegMissing = "check.ffmpeg." + FfmpegCheck.Missing;
    public const string FfmpegNotRunnable = "check.ffmpeg." + FfmpegCheck.NotRunnable;
    public const string FfmpegNoLibx264 = "check.ffmpeg." + FfmpegCheck.NoLibx264;
    public const string FfmpegError = "check.ffmpeg.error";

    /// <summary>Every code above, for the docs and their tests.</summary>
    public static IReadOnlyList<string> All { get; } =
        typeof(CheckCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue())
            .ToList();

    /// <summary><c>check.&lt;target&gt;.&lt;reason&gt;</c> for a target name such as <c>twitch-extension</c>.</summary>
    public static string For(string target, string reason) =>
        "check." + target.Replace('-', '_') + "." + reason;
}
