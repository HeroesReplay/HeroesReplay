using System;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public sealed class MediumIntegrityReplayOpener : IReplayOpener
{
    private readonly AppSettings settings;
    private readonly ILogger<MediumIntegrityReplayOpener> logger;

    public MediumIntegrityReplayOpener(
        AppSettings settings,
        ILogger<MediumIntegrityReplayOpener> logger
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RequestAuthenticatedClient()
    {
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
}
