using System;
using System.Collections.Generic;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient.Firewall;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.GameClient;

public sealed class MediumIntegrityReplayOpener : IReplayOpener
{
    private readonly AppSettings settings;
    private readonly IGameFirewall firewall;
    private readonly BattleNetAgentReaper agentReaper;
    private readonly ILogger<MediumIntegrityReplayOpener> logger;

    public MediumIntegrityReplayOpener(
        AppSettings settings,
        IGameFirewall firewall,
        BattleNetAgentReaper agentReaper,
        ILogger<MediumIntegrityReplayOpener> logger
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        this.agentReaper = agentReaper ?? throw new ArgumentNullException(nameof(agentReaper));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RequestAuthenticatedClient()
    {
        // Each `--exec` leaves a new Agent.exe behind (#251): reap the earlier launches' agents.
        agentReaper.Reap("before a Battle.net launch");
        AllowInstalledClients();
        ReplayStartCommand command = ReplayStartCommand.HeroClient(
            settings.Location?.BattlenetPath
        );
        string source = MediumIntegrityProcess.Start(
            command.FileName,
            command.Arguments,
            command.WorkingDirectory
        );
        logger.LogInformation("Asked Battle.net to launch Heroes using {Source}.", source);
    }

    public void Open(string replayPath)
    {
        AllowInstalledClients();
        ReplayStartCommand command = ReplayStartCommand.For(
            settings.Location?.GameInstallDirectory,
            replayPath
        );
        string source = MediumIntegrityProcess.Start(
            command.FileName,
            command.Arguments,
            command.WorkingDirectory
        );
        logger.LogInformation(
            "Opened {Replay} with {FileName} using {Source}.",
            replayPath,
            command.FileName,
            source
        );
    }

    public void OpenMatching(string exePath, string replayPath)
    {
        AllowInstalledClients();
        ReplayStartCommand command = ReplayStartCommand.MatchingExe(exePath, replayPath);
        string source = MediumIntegrityProcess.Start(
            command.FileName,
            command.Arguments,
            command.WorkingDirectory
        );
        logger.LogInformation(
            "Opened {Replay} with the matching client {FileName} using {Source}.",
            replayPath,
            command.FileName,
            source
        );
    }

    private void AllowInstalledClients()
    {
        IReadOnlyList<InstalledClient> clients = InstalledClientCatalog.Clients(
            settings.Location?.GameInstallDirectory
        );
        var paths = new List<string>(clients.Count);
        foreach (InstalledClient client in clients)
        {
            paths.Add(client.ExePath);
        }

        firewall.AllowInboundClients(paths);
    }
}
