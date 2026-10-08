using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Reflection;
using System.Text;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Client;
using HeroesReplay.CLI.Commands.Deps;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.YouTube;

namespace HeroesReplay.CLI.Commands;

/// <summary>
/// What one command needs first (<see cref="Before"/>), what it changes (<see cref="Changes"/>),
/// what its exit codes mean (<see cref="Exit"/>), and the stable codes it can print
/// (<see cref="Codes"/>).
/// </summary>
public sealed record CommandFacts(
    string Path,
    string Before,
    string Changes,
    string Exit,
    IReadOnlyList<string> Codes = null
);

/// <summary>
/// The command reference (#313): the command tree (description and options) joined with the
/// hand-kept <see cref="Facts"/>, rendered to <see cref="RelativePath"/>. <c>AgentDocsTests</c>
/// fails when a command that runs has no entry, an entry names no command, a stable code is in no
/// entry, or the checked-in file differs from <see cref="Render"/>.
/// </summary>
public static class CommandReference
{
    public const string RelativePath = ".agents/skills/heroes-replay-cli/commands.md";

    /// <summary>Set to 1 to let the docs test rewrite <see cref="RelativePath"/>.</summary>
    public const string WriteVariable = "HEROESREPLAY_WRITE_COMMAND_REFERENCE";

    private const string Nothing = "Nothing. Read only.";
    private const string Finishes =
        "0 when it finishes. 1 on a parse error or an unexpected error.";
    private const string Blocks =
        "Runs until stopped (Ctrl+C, or `services stop` for a role). 1 on a parse error or an unexpected error.";
    private const string CheckExit =
        "0 when the check passes (a warning passes), 1 when it fails or on a parse error.";
    private const string ReleaseOnly =
        "Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.";
    private const string LiveAsk =
        "On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.";

    public static IReadOnlyList<CommandFacts> Facts { get; } =
    [
        new(
            "spectate file",
            "Not elevated. Heroes of the Storm installed; a current-patch replay needs Battle.net signed in. OBS is optional. On ASA-SERVER only to prove a change (AGENTS.md phases). "
                + LiveAsk,
            "Launches Battle.net, HeroesSwitcher and Heroes of the Storm, sends spectator keys, and writes `Data\\Contexts\\<id>` and `status.json`. With OBS it changes scenes and records (`OBS:RecordingEnabled`), and streams only with `OBS:StreamingEnabled` and the arm. Starts the Aspire dashboard when OTLP :4317 is down.",
            "0 after the queue has played. 1 on a parse error (`--player` not a BattleTag, a `--file` that does not exist) or when the engine stops on an unexpected error."
        ),
        new(
            "spectate heroesprofile",
            "As `spectate file`, with replays already in `Data\\Standard` and `Data\\Requests` (`heroesprofile download`). `services start` runs it as the spectate role.",
            "As `spectate file`, over the cached replays, in a loop. Does not call Heroes Profile.",
            Blocks
        ),
        new(
            "calculators report",
            "A `.StormReplay` file or folder (`--file`, default `Location:ReplaySource`).",
            "Writes `<replay>.StormReplay.csv` to `SpectateReportPath`. Does not launch the game.",
            Finishes
        ),
        new(
            "calculators coordinates",
            "A latest-client `.StormReplay` (`--file`).",
            Nothing,
            "0 when coordinates parse and the focus timeline has entries. 1 file not found, 2 no replay object, 3 parse failed, 4 coordinates not parseable, 5 empty focus timeline."
        ),
        new(
            "calculators units",
            "A folder of replays (`--directory`).",
            "Writes the unit CSV reports (`--output`). Reads one file at a time; does not launch the game.",
            Finishes
        ),
        new(
            "calculators compositions",
            "A folder of replays and the heroes-data2 catalog under `Location:DataDirectory`.",
            "Writes `--output` (`.md` or `.csv`) when given; otherwise prints only. Read-only for the replays.",
            "0 when it finishes. 1 for a missing folder, an `--output` that is not `.md` or `.csv`, or a parse error."
        ),
        new(
            "check",
            "Settings readable from the current directory or the exe folder. Read-only, so safe on both machines.",
            "Nothing in OBS, Twitch, or Heroes Profile: Get-style reads only (Helix GetUsers, GET /replays, an OBS Identify and GetVersion, the Battle.net window capture). Logs go to stderr.",
            "0 when every target passes (a warning passes), 1 when one fails or on a parse error.",
            [CheckCodes.AllOk]
        ),
        new(
            "check config",
            "Settings readable. Resolves `op://` secrets through `op`.",
            "Nothing. Prints which secrets are present, never their values.",
            CheckExit,
            CheckCodesFor("config")
        ),
        new(
            "check heroesprofile",
            "`HeroesProfileApi:ApiKey` set (the v1 Bearer key).",
            "Nothing. One GET /replays (counts against the Heroes Profile allowance).",
            CheckExit,
            CheckCodesFor("heroesprofile")
        ),
        new(
            "check obs",
            "OBS running with its WebSocket server on `OBS:WebSocketEndpoint`.",
            "Nothing. Identify, GetVersion, and the profile and collection reads, then disconnect.",
            CheckExit,
            CheckCodesFor("obs")
        ),
        new(
            "check twitch",
            "`Twitch:AccessToken` and `Twitch:ClientId` set.",
            "Nothing. Helix GetUsers, GetPredictions when predictions are on, and the token validation.",
            CheckExit,
            CheckCodesFor("twitch")
        ),
        new(
            "check client",
            "None.",
            "Nothing. Reads `Variables.txt` and the AhliObs interface.",
            CheckExit,
            CheckCodesFor("client")
        ),
        new(
            "check connectivity",
            "None.",
            "Nothing. Probes 1.1.1.1, Twitch, and Heroes Profile; never starts a stream.",
            CheckExit,
            CheckCodesFor("connectivity")
        ),
        new(
            "check timer",
            "Heroes of the Storm running; a match playing for a pass.",
            "Nothing. Reads the client's memory five times, a second apart; samples go to stderr in JSON mode.",
            CheckExit,
            CheckCodesFor("timer")
        ),
        new(
            "check twitch-extension",
            "`TwitchExtension:Enabled`; when on, `TwitchExtension:ApiKey` (the uploader key, not the v1 key).",
            "Nothing. One uploader/whoami call when the extension is on.",
            CheckExit,
            CheckCodesFor("twitch-extension")
        ),
        new(
            "check battlenet",
            "Battle.net open with a visible window; Windows OCR available.",
            "Nothing. Captures the window with PrintWindow and reads it; never clicks.",
            CheckExit,
            CheckCodesFor("battlenet")
        ),
        new(
            "check ffmpeg",
            "None. Reads no secret.",
            "Nothing. Runs `ffmpeg -version`, `ffmpeg -encoders`, and `ffprobe -version`.",
            "0 when both tools work (a build that is not the pinned one is a warning and passes), 1 when one is missing, does not run, or cannot encode libx264.",
            CheckCodesFor("ffmpeg")
        ),
        new(
            "client configure",
            "Heroes of the Storm closed (it rewrites `Variables.txt` on exit).",
            "Writes root and account `Variables.txt` (windowed 1080p, AhliObs) and copies the AhliObs `.StormInterface` into Documents.",
            "0 when the interface was copied, 1 otherwise or on an unexpected error."
        ),
        new(
            "client status",
            "None.",
            "Nothing. Reads `Variables.txt` and the interface.",
            "0 when the preset matches, 1 on a mismatch, when the status cannot be read, or on a parse error.",
            [ClientCommand.PresetOk, ClientCommand.PresetMismatch, ClientCommand.StatusError]
        ),
        new(
            "client firewall",
            "An elevated shell (the only command that needs one).",
            "Adds the inbound rule `HeroesReplay inbound Base<build>` for each installed client exe that no enabled Allow rule covers; replaces duplicates of its own rule.",
            "0 when every installed client has an Allow rule, 1 when not elevated or a rule could not be added."
        ),
        new(
            "otel up",
            "The local Aspire CLI tool (`dotnet tool restore`).",
            "Starts the Aspire dashboard (UI :18888, OTLP :4317) and records its pid.",
            "0 when the dashboard listens, 1 otherwise."
        ),
        new(
            "otel down",
            "None. It stops only a dashboard this CLI started.",
            "Stops the dashboard this CLI started (its recorded pid tree).",
            "0 when it stopped it or nothing was running, 1 when it could not stop it."
        ),
        new("otel status", "None.", Nothing, "0 when OTLP :4317 listens, 1 otherwise."),
        new(
            "mcp",
            "Run by an MCP client over stdio (`.mcp.json`).",
            "Nothing: every tool is read-only, and the OBS tools send only Get requests and never return the stream key. Logs go to stderr.",
            "Runs until the client closes stdin.",
            [
                ObsUnavailableException.Unreachable,
                ObsUnavailableException.AuthenticationFailed,
                ObsLiveRead.PasswordUnresolved,
                ObsLiveRead.SettingsUnreadable,
                ObsLiveRead.RequestFailed,
                ObsLiveRead.SourceNotFound,
            ]
        ),
        new(
            "twitch connect",
            "Twitch token with the chat and redemption scopes. Dev turns every side effect off (`DryRunMode`). "
                + LiveAsk,
            "Joins chat, syncs channel-point rewards, queues redemptions in `Data\\requests.json`, and opens and resolves Blue/Red predictions.",
            Blocks
        ),
        new(
            "twitch say",
            "`--message`. Sends to the live channel's chat when the settings name it.",
            "Sends one chat message.",
            "0 when sent, 1 without `--message` or when chat did not join the channel."
        ),
        new(
            "twitch rewards generate",
            "None.",
            "Writes the default catalog to `Data\\custom-rewards.json`. Does not call Twitch.",
            Finishes
        ),
        new(
            "twitch rewards submit",
            "A broadcaster token with `channel:manage:redemptions`.",
            "Creates or updates the channel-point rewards on the channel and deletes leftover Unranked Draft rewards.",
            Finishes
        ),
        new("twitch rewards list", "A broadcaster token.", Nothing, Finishes),
        new(
            "twitch rewards remove-unranked-draft",
            "A broadcaster token with `channel:manage:redemptions`.",
            "Deletes `(UD)` / Unranked Draft rewards only.",
            Finishes
        ),
        new(
            "twitch rewards test",
            "None.",
            "Runs the local reward handler as if a viewer redeemed (`--title`, `--message`): it can queue a request in `Data\\requests.json`.",
            Finishes
        ),
        new(
            "twitch predictions test",
            "A token with `channel:manage:predictions`. Creates a real prediction on the configured channel.",
            "Creates a 30 s Blue/Red prediction, then resolves (`--outcome Blue|Red`) or cancels it (default).",
            "0 when it finishes. 1 on any other `--outcome` (a parse error) or an unexpected error."
        ),
        new(
            "youtube uploader",
            "`Data\\client_secrets.json` and consent for real uploads; dev and base settings are a dry run. "
                + LiveAsk,
            "Uploads recordings from `Data\\Contexts` (private, scheduled by the publication budget), deletes each mp4 after its insert, and runs the library pass. Dry run writes `youtube-dry-run.json` instead.",
            Blocks
        ),
        new(
            "youtube library",
            "The `{ChannelId}-library` consent; run once at the machine to grant it.",
            "Records missing videos in `Data\\youtube-library.jsonl` and inserts playlist items (50 units each). Dry run writes `Data\\youtube-library-dry-run.json` and calls nothing.",
            "With `--once`: 0 when the pass finished, 1 when it failed. Without it, runs until stopped."
        ),
        new(
            "heroesprofile download",
            "`HeroesProfileApi:ApiKey`.",
            "Downloads Storm League replays into `Data\\Standard` and requested ones into `Data\\Requests`; keeps the hero statistics current while `YouTube:Titles:StatHooks:Enabled`. A Standard replay whose download Heroes Profile refuses with an HTTP status is skipped (replay id and status logged), not counted as an outage. Does not launch the game.",
            Blocks
        ),
        new(
            "heroesprofile hero-stats",
            "`HeroesProfileApi:ApiKey` with a plan that allows the statistics calls. About 92 calls and 15 to 20 minutes.",
            "Writes `Data\\HeroesProfile\\hero-stats\\<patch>-<code>.json` and deletes files older than the newest two patches.",
            "0 when a file was written, 1 on a 401 or 403, a 422, or when nothing was written."
        ),
        new(
            "heroesprofile patch-index",
            "`HeroesProfileApi:ApiKey`.",
            "Nothing, unless `--write`: then it stores `MinReplayId` in `appsettings.json`.",
            "0 when it finishes, 1 when the latest replay's version or (with `--write`) `appsettings.json` or its `MinReplayId` cannot be found."
        ),
        new(
            "heroesprofile sample",
            "`HeroesProfileApi:ApiKey`; `--output` must not be a spectate queue folder.",
            "Downloads the newest listed replays of `--map` into `--output` until `--count` are there or the listing runs out. A download that still fails after the Heroes Profile retries (a 429 waits for `Retry-After`) is skipped: its replay id and HTTP status are logged, its partial file is deleted, and the summary lists each skip with its reason.",
            "0 when at least one listed replay is in `--output` (downloaded or already there), even with skips. 1 when `--count` is out of 1 to 20, `--output` is a queue folder, nothing was listed, or every listed replay was skipped."
        ),
        new(
            "services start",
            "No supervisor running in any session (with `--supervise`). On ASA-SERVER prove supervision with `--roles download,youtube` only. "
                + LiveAsk,
            "Starts the roles as separate processes (spectate, twitch, download, youtube), updates the OBS collection paths while OBS is closed, starts the Aspire dashboard when needed, and with `--supervise` keeps this console as the supervisor. Never starts Twitch ingest.",
            "0 when every role is ready (with `--supervise`: when supervision ends). 1 on an unknown role, a running supervisor, or a role that did not start."
        ),
        new(
            "services ensure",
            "This install's build. Starts nothing while another build, a stop, a stale role, or a supervisor that owns the restart is in the way.",
            "Starts only the requested roles that are down, through the same checks as `services start`; never stops a running role. With `--supervise`, becomes the supervisor when none runs.",
            "0 for `service.ensure_noop` or `service.ensure_started`; 1 for every other code or a parse error.",
            CodesIn(typeof(ServiceEnsureCodes))
        ),
        new(
            "services stop",
            LiveAsk,
            "Writes `services.stop`, stops the supervisor, then the roles (kills any still running after 20 s), and closes Heroes of the Storm. Once every role has exited, sends `StopRecord` for a recording spectate claimed in `obs-recording.json` and left running, when its duration matches the claim (#318). Never stops an OBS stream.",
            "0 when every role and the supervisor exited, the game closed, OBS is closed or not streaming, and no recording spectate started is left running. 1 otherwise, including a running OBS whose websocket does not answer on an install that streams, and a claimed recording that OBS refused to stop or that could not be checked."
        ),
        new(
            "services status",
            "None. Over SSH the supervisor's mutex is not visible, so `supervisor.json` decides.",
            Nothing,
            "0 when no role is failed, stale, or degraded; 1 otherwise.",
            [
                .. CodesIn(typeof(ServiceHealthCodes)),
                HeroesProfileApiProbe.RejectedCode,
                HeroesProfileApiProbe.UnreachableCode,
                TwitchTokenProbe.InvalidCode,
                TwitchTokenProbe.UnreachableCode,
                YouTubeOAuthProbe.InvalidCode,
                YouTubeOAuthProbe.MissingCode,
                YouTubeOAuthProbe.UnreachableCode,
                YouTubeUploaderHealth.QuotaBlockedCode,
                YouTubeUploaderHealth.NotPublishingCode,
                ObsWebsocketProbe.RejectedCode,
                ObsWebsocketProbe.UnreachableCode,
            ]
        ),
        new(
            "services supervise",
            "Roles recorded by `services start`; no other supervisor in any session.",
            "Restarts failed roles with backoff, kills and restarts stale ones, writes `supervisor.json` and its log, and makes a live stream safe when spectate stays down (`ServiceRestart:SpectateDownObs`).",
            "Runs until `services stop` or Ctrl+C (which leaves the roles unsupervised). 1 when a supervisor already runs.",
            [ServiceHealthCodes.RestartBudgetExhausted]
        ),
        new(
            "services install-task",
            "Run once from the release install on the stream PC. No administrator rights.",
            "Registers (or with `--remove` deletes) the scheduled task `HeroesReplay-live` that runs `services start --supervise` at logon.",
            Finishes
        ),
        new(
            "obs arm",
            "Only on the stream PC, or on ASA-SERVER for a stream proof.",
            "Writes `%LOCALAPPDATA%\\HeroesReplay\\stream-armed`. OBS is not touched.",
            "0 when armed, 1 when the file could not be written."
        ),
        new(
            "obs disarm",
            "None.",
            "Deletes the arm file. A live stream keeps running.",
            "0 when disarmed or already not armed, 1 when the file could not be deleted."
        ),
        new(
            "obs status",
            "None. Does not connect to OBS or resolve secrets.",
            Nothing,
            "0 unless the settings cannot be loaded (1).",
            [
                ObsCommand.IngestReady,
                ObsStreamArm.NotArmedReason,
                ObsCommand.StreamingDisabled,
                ObsLiveRead.SettingsUnreadable,
            ]
        ),
        new(
            "obs pages",
            "None. OBS optional.",
            "Writes `Data\\queue.html` and `Data\\prediction-report.html`, then reloads the browser sources that show them (`refreshnocache`) unless `--no-reload` or OBS is closed. The reload is the only OBS change.",
            "0 when every page was written, 1 when one could not be."
        ),
        new(
            "obs inspect",
            "OBS running with its WebSocket server. Safe on the live box.",
            "Nothing. Get requests only; the stream key is never read out.",
            "0 when OBS was read, 1 when it could not be.",
            [
                ObsUnavailableException.Unreachable,
                ObsUnavailableException.AuthenticationFailed,
                ObsLiveRead.PasswordUnresolved,
                ObsLiveRead.SettingsUnreadable,
                ObsLiveRead.RequestFailed,
            ]
        ),
        new(
            "obs validate",
            "OBS running with its WebSocket server. Safe on the live box; over SSH, `C:\\heroesreplay` paths read as missing (skill `heroes-replay-obs`).",
            "Nothing. Get requests only.",
            "0 when no finding is an error, 1 otherwise or when OBS cannot be read.",
            [
                .. CodesIn(typeof(ObsValidator)),
                .. CodesIn(typeof(ObsSelection)),
                ObsUnavailableException.Unreachable,
                ObsUnavailableException.AuthenticationFailed,
                ObsLiveRead.PasswordUnresolved,
                ObsLiveRead.SettingsUnreadable,
                ObsLiveRead.RequestFailed,
            ]
        ),
        new(
            "obs plan",
            "None. Reads files only (no websocket), so it is safe while OBS runs.",
            "Nothing. Compares the live collection with the install's `obs/Default.json` and the template it was last written from.",
            "0 when nothing conflicts, 1 on a conflict or when the collection or the template cannot be read.",
            CodesIn(typeof(ObsPlanCodes))
        ),
        new(
            "obs backup",
            "None. Reads the live collection only, so it is safe while OBS runs.",
            "Copies the live collection into `%LOCALAPPDATA%\\HeroesReplay\\obs\\backups` (the newest 10 are kept), unless `--list`.",
            "0 when backed up or listed, 1 when there is no collection or the copy failed.",
            [
                ObsBackupCodes.BackedUp,
                ObsBackupCodes.Listed,
                ObsBackupCodes.CollectionMissing,
                ObsBackupCodes.Failed,
            ]
        ),
        new(
            "obs restore",
            "OBS closed (refused while it runs). A backup of this collection. " + LiveAsk,
            "Backs up the current collection, then writes the backup over it atomically and clears a waiting release rollback. `managed-collections.json` is not changed.",
            "0 when restored or already the same, 1 when nothing was restored.",
            [
                ObsBackupCodes.Restored,
                ObsBackupCodes.AlreadyRestored,
                ObsBackupCodes.ObsRunning,
                ObsBackupCodes.BackupMissing,
                ObsBackupCodes.BackupOther,
                ObsBackupCodes.BackupInvalid,
                ObsBackupCodes.Failed,
            ]
        ),
        new(
            "config effective",
            "None. Resolves no `op://` reference.",
            "Nothing. Secrets are always redacted.",
            "0 when printed, 1 when `appsettings.json` is missing or unreadable, or `--section` matches nothing.",
            CodesIn(typeof(ConfigurationProvenance))
        ),
        new(
            "obs bundle",
            "An install or publish folder with `obs\\Default.json` (`--install`, default this exe's folder). No OBS needed.",
            "Nothing, unless `--write` (packaging only, refused in a source checkout): then it writes `obs\\bundle.manifest`.",
            "0 when the files match (or there is no manifest), 1 on `obs.bundle_invalid`, `obs.bundle_missing`, or a failed `--write`.",
            [ObsValidator.BundleInvalid, ObsValidator.BundleMissing]
        ),
        new(
            "update check",
            "Network access to the GitHub API.",
            "Nothing. Does not download or restart.",
            "0 when the latest release was read, 1 when it could not be or has no `heroesreplay-win-x64.zip`."
        ),
        new(
            "update preserve-min-replay-id",
            ReleaseOnly,
            "Writes the higher `MinReplayId` into the new install's `appsettings.json`.",
            "0 when done, 1 when a file cannot be read or written."
        ),
        new(
            "update release-health",
            ReleaseOnly,
            Nothing,
            "0 healthy; 1 while `Release:HealthWindow` is open (without `--wait`); 2 a role down or stale, or every replay failed (roll back); 3 a stop was requested (no verdict); 4 nothing playable (inconclusive, keep)."
        ),
        new(
            "update migrate-stream-arm",
            ReleaseOnly,
            "Arms this machine once when the replaced install streamed (`OBS:StreamingEnabled` true).",
            "0 when done or not needed, 1 when the settings or the arm file could not be read or written."
        ),
        new(
            "update install-obs",
            ReleaseOnly,
            "Checks the release's `obs` folder against `obs\\bundle.manifest`, then replaces a managed collection (with a backup), keeps a custom one, and installs the profile template only when the machine has none. Writes nothing while OBS runs.",
            "0 when done or deferred, 1 on `obs.bundle_invalid` or a failed copy.",
            [ObsValidator.BundleInvalid]
        ),
        new(
            "update restore-obs",
            ReleaseOnly + " Runs on a rollback.",
            "Puts back the collection the restored install ran with: written while OBS is closed, or swapped in through `{collection}-next` while it runs, or left pending in `restore-pending.json`.",
            "0 when restored or nothing to do, 1 when the collection was kept (custom, unreadable, another install's record) or OBS stayed on the spare."
        ),
        new(
            "update launcher",
            ReleaseOnly,
            "Rewrites `%LOCALAPPDATA%\\HeroesReplay\\start-live.cmd` to `services start --supervise` (keeping `start-live.cmd.previous`), or puts the previous one back with `--restore`.",
            "0 when done, 1 without `--install` (and no `--restore`) or when the file could not be written."
        ),
        new(
            "deps install",
            "Network access to the pinned download URL. CI never runs it.",
            "Downloads, checks, and installs `ffmpeg.exe` and `ffprobe.exe` into `<dir>\\ffmpeg` (staged, then moved into place); nothing when the pinned build is already there.",
            "0 when installed or already installed, 1 when the download, the hash, or the copy failed.",
            [DepsCommand.Installed, DepsCommand.AlreadyInstalled, DepsCommand.Failed]
        ),
    ];

    /// <summary>Every command that runs something: a leaf, or a group with its own action (<c>check</c>).</summary>
    public static IReadOnlyList<(string Path, Command Command)> Runnable(Command root)
    {
        var commands = new List<(string, Command)>();
        void Walk(Command command, string prefix)
        {
            foreach (Command child in command.Subcommands.Where(child => !child.Hidden))
            {
                string path = prefix == null ? child.Name : prefix + " " + child.Name;
                if (child.Action != null || child.Subcommands.Count == 0)
                {
                    commands.Add((path, child));
                }

                Walk(child, path);
            }
        }

        Walk(root, null);
        return commands;
    }

    /// <summary>The Markdown for <see cref="RelativePath"/>, in command tree order.</summary>
    public static string Render(Command root)
    {
        Dictionary<string, CommandFacts> facts = Facts.ToDictionary(
            fact => fact.Path,
            StringComparer.Ordinal
        );
        var text = new StringBuilder();
        text.Append("# heroesreplay command reference\n\n");
        text.Append(
            "Generated from the command tree and `CommandReference.Facts` (`src/HeroesReplay.CLI/Commands/CommandReference.cs`). Do not edit by hand: change the command or its entry, then run `dotnet test heroes-replay.slnx --filter CommandReference` with `"
                + WriteVariable
                + "=1`. `AgentDocsTests` fails when a command or a stable code has no entry, or when this file is stale.\n\n"
        );
        text.Append(
            "Each command: what must be true first, what it changes, its exit codes, and the stable codes it prints (`--output json` `code`, or a finding or role `code`). The `--output text|json` contract is in `SKILL.md`.\n"
        );
        foreach ((string path, Command command) in Runnable(root))
        {
            text.Append("\n## `").Append(path).Append("`\n\n");
            text.Append(OneLine(command.Description)).Append("\n\n");
            string options = Options(command);
            if (options.Length > 0)
            {
                text.Append("- **Options:** ").Append(options).Append('\n');
            }

            if (facts.TryGetValue(path, out CommandFacts fact))
            {
                text.Append("- **Before:** ").Append(fact.Before).Append('\n');
                text.Append("- **Changes:** ").Append(fact.Changes).Append('\n');
                text.Append("- **Exit:** ").Append(fact.Exit).Append('\n');
                if (fact.Codes is { Count: > 0 })
                {
                    text.Append("- **Codes:** ")
                        .Append(
                            string.Join(
                                ", ",
                                fact.Codes.Distinct().Select(code => "`" + code + "`")
                            )
                        )
                        .Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>The public string constants of <paramref name="type"/> that look like stable codes.</summary>
    public static IReadOnlyList<string> CodesIn(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue())
            .Where(value => value.Contains('.', StringComparison.Ordinal))
            .ToList();

    private static IReadOnlyList<string> CheckCodesFor(string target) =>
        CheckCodes
            .All.Where(code =>
                code.StartsWith(CheckCodes.For(target, string.Empty), StringComparison.Ordinal)
            )
            .ToList();

    private static string Options(Command command)
    {
        IEnumerable<Option> own = command.Options;
        IEnumerable<Option> inherited = Ancestors(command)
            .SelectMany(parent => parent.Options)
            .Where(option => option.Recursive);
        return string.Join(
            ", ",
            own.Concat(inherited)
                .Where(option => !option.Hidden && option.Name is not ("--help" or "--version"))
                .Select(Describe)
        );
    }

    /// <summary><c>`--name`</c>, then its aliases and whether it is required in parentheses.</summary>
    private static string Describe(Option option)
    {
        List<string> notes = option.Aliases.Order().Select(alias => "`" + alias + "`").ToList();
        if (option.Required)
        {
            notes.Add("required");
        }

        return "`"
            + option.Name
            + "`"
            + (notes.Count == 0 ? string.Empty : " (" + string.Join(", ", notes) + ")");
    }

    private static IEnumerable<Command> Ancestors(Command command)
    {
        for (
            Command parent = command.Parents.OfType<Command>().FirstOrDefault();
            parent != null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault()
        )
        {
            yield return parent;
        }
    }

    private static string OneLine(string text) =>
        string.Join(
            " ",
            (text ?? string.Empty).Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
}
