using System;
using System.Collections.Generic;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public sealed class MediumIntegrityReplayOpener : IReplayOpener
{
    private readonly AppSettings settings;
    private readonly IGameFirewall firewall;
    private readonly ILogger<MediumIntegrityReplayOpener> logger;

    public MediumIntegrityReplayOpener(
        AppSettings settings,
        IGameFirewall firewall,
        ILogger<MediumIntegrityReplayOpener> logger
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RequestAuthenticatedClient()
    {
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
