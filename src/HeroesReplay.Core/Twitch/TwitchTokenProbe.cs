using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.Twitch;

/// <summary>
/// The twitch role's dependency probe (#305): Twitch's token validator
/// (<c>id.twitch.tv/oauth2/validate</c>, <see cref="TwitchTokenScopes"/>), the same read
/// <c>services start</c> makes for the scopes. Twitch asks apps to validate their tokens hourly,
/// and the call changes nothing. A 401 is <see cref="InvalidCode"/>; no answer is
/// <see cref="UnreachableCode"/>. The token is sent only in the Authorization header.
/// </summary>
public sealed class TwitchTokenProbe : IServiceDependencyProbe
{
    public const string InvalidCode = "twitch.token_invalid";
    public const string UnreachableCode = "twitch.unreachable";

    private const string Name = "Twitch token";

    private const string InvalidFix =
        "Generate a new Twitch:AccessToken (twitchtokengenerator; the 1Password item the op-service-account skill names), write it with `pwsh -File tools/fill-secrets-from-op.ps1`, confirm with `heroesreplay check twitch`, then restart the stack (`heroesreplay services stop`, then `heroesreplay services start --supervise`; on the stream PC in a downtime). The role stays up, degraded, until then.";

    private const string UnreachableFix =
        "Nothing to do for a short outage: chat and redemptions reconnect with backoff, and this clears when a probe passes (every 2 minutes while it fails). If it lasts, check that https://id.twitch.tv answers from this machine.";

    private readonly TwitchSettings settings;
    private readonly Func<string, CancellationToken, Task<TwitchTokenValidation>> validate;

    public TwitchTokenProbe(TwitchSettings settings)
        : this(
            settings,
            (token, cancellationToken) =>
                TwitchTokenScopes.ReadAsync(token, TimeSpan.FromSeconds(60), cancellationToken)
        ) { }

    /// <param name="settings">The token, and which Twitch features this role runs.</param>
    /// <param name="validate">The validator call. Tests replace it.</param>
    public TwitchTokenProbe(
        TwitchSettings settings,
        Func<string, CancellationToken, Task<TwitchTokenValidation>> validate
    )
    {
        this.settings = settings;
        this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
    }

    public string Dependency => Name;

    /// <summary>The role makes no authenticated Twitch call when chat, redemptions, and predictions are off (dev).</summary>
    public string NotUsedReason =>
        settings != null
        && (
            settings.EnableChatBot
            || settings.EnablePubSub
            || settings.EnableRequests
            || settings.EnablePredictions
        )
            ? null
            : "Twitch chat, redemptions, and predictions are off, so the twitch role makes no authenticated Twitch call.";

    public ServiceDependencyResult Unreachable(string cause) =>
        ServiceDependencyResult.Unreachable(Name, UnreachableCode, cause, UnreachableFix);

    public async Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings?.AccessToken))
        {
            return ServiceDependencyResult.Rejected(
                Name,
                InvalidCode,
                "Twitch:AccessToken is empty, so chat, redemptions, and predictions cannot sign in.",
                InvalidFix
            );
        }

        TwitchTokenValidation read = await validate(settings.AccessToken, cancellationToken)
            .ConfigureAwait(false);
        if (read?.Status is not int status)
        {
            return Unreachable("Twitch's token validator (id.twitch.tv) did not answer.");
        }

        if (read.Known)
        {
            int scopes = string.IsNullOrWhiteSpace(read.Scopes)
                ? 0
                : read.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return ServiceDependencyResult.Ok(
                Name,
                $"Twitch validated the access token ({scopes} scope(s))."
            );
        }

        return status == 401
            ? ServiceDependencyResult.Rejected(
                Name,
                InvalidCode,
                "Twitch rejected Twitch:AccessToken (HTTP 401): it expired or was revoked, so chat, redemptions, and predictions fail.",
                InvalidFix
            )
            : Unreachable($"Twitch's token validator answered HTTP {status}.");
    }
}
