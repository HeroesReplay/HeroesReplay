using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

public class PatchIndexCommand : Command
{
    public PatchIndexCommand()
        : base(
            "patch-index",
            "Find the first Heroes Profile replay id that uses the same game client as the latest replay."
        )
    {
        Option<bool> write = new("--write")
        {
            Description = "Write that id into MinReplayId in appsettings.json.",
        };
        Options.Add(write);
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await RunAsync(parseResult.GetValue(write), cancellationToken);
            }
        );
    }

    private static async Task<int> RunAsync(bool write, CancellationToken cancellationToken)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken, "heroesreplay-patch-index")
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        IHeroesProfileService heroesProfile =
            scope.ServiceProvider.GetRequiredService<IHeroesProfileService>();
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();

        int maxId = await heroesProfile.GetMaxReplayIdAsync().ConfigureAwait(false);
        var latest = await heroesProfile
            .ListAfterAsync(Math.Max(1, maxId - 1), cancellationToken)
            .ConfigureAwait(false);
        HeroesProfileReplay head = latest?.OrderByDescending(replay => replay.Id).FirstOrDefault();
        if (head == null || string.IsNullOrWhiteSpace(head.GameVersion))
        {
            Console.Error.WriteLine("Could not read the game version of the latest replay.");
            return 1;
        }

        int first = await FindFirstAsync(
                heroesProfile,
                head.Id,
                head.GameVersion,
                cancellationToken
            )
            .ConfigureAwait(false);
        bool supported =
            settings.Spectate?.VersionsSupported != null
            && settings.Spectate.VersionsSupported.Contains(head.GameVersion);
        Console.WriteLine($"Latest replay {head.Id} is client {head.GameVersion}.");
        Console.WriteLine($"First replay of that client: {first}.");
        Console.WriteLine(
            supported
                ? $"VersionsSupported already includes {head.GameVersion}."
                : $"VersionsSupported does not include {head.GameVersion}."
        );
        Console.WriteLine($"MinReplayId is {settings.HeroesProfileApi?.MinReplayId ?? 0}.");
        if (!write)
        {
            Console.WriteLine("Re-run with --write to store this id as MinReplayId.");
            return 0;
        }

        string path = SettingsPath();
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Could not find appsettings.json to update.");
            return 1;
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!MinReplayIdFile.TryReplace(json, first, out string updated))
        {
            Console.Error.WriteLine($"MinReplayId was not found in {path}.");
            return 1;
        }

        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Updated MinReplayId to {first} in {path}.");
        Console.WriteLine("Restart heroesreplay so the downloader uses the new index.");
        return 0;
    }

    private static async Task<int> FindFirstAsync(
        IHeroesProfileService heroesProfile,
        int maxId,
        string version,
        CancellationToken cancellationToken
    )
    {
        return CurrentPatchIndex.FindFirst(
            maxId,
            version,
            after =>
            {
                var page = heroesProfile
                    .ListAfterAsync(after, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                HeroesProfileReplay row = page?.FirstOrDefault();
                if (row == null || string.IsNullOrWhiteSpace(row.GameVersion))
                {
                    return null;
                }

                return new CurrentPatchIndex.Row(row.Id, row.GameVersion);
            }
        );
    }

    private static string SettingsPath()
    {
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(cwd))
        {
            return cwd;
        }

        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }
}
