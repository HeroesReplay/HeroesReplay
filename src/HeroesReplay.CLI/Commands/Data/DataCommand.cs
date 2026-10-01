using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Data;

public class DataCommand : Command
{
    public DataCommand()
        : base(
            "data",
            "The heroes-data2 cache (hero, unit, and map data) under Location:DataDirectory\\HeroesData."
        )
    {
        Subcommands.Add(StatusCommand());
        Subcommands.Add(DownloadCommand());
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Print the cache folder and the herodata build the spectator will load. Exit 1 when the cache is empty."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
                return Task.FromResult(PrintStatus(settings.HeroesDataPath));
            }
        );
        return command;
    }

    private static Command DownloadCommand()
    {
        var command = new Command(
            "download",
            "Download the latest HeroesToolChest/heroes-data2 release when the cache is empty. --force downloads it again after a game patch."
        );
        Option<bool> force = new("--force")
        {
            Description = "Download even when a cache is already present.",
        };
        command.Options.Add(force);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
                using ILoggerFactory loggers = LoggerFactory.Create(builder =>
                    builder.AddSimpleConsole(options => options.SingleLine = true)
                );
                var gameData = new GameData(loggers.CreateLogger<GameData>(), settings);
                try
                {
                    bool downloaded = await gameData.EnsureDownloadedAsync(
                        parseResult.GetValue(force)
                    );
                    Console.WriteLine(
                        downloaded
                            ? "Downloaded heroes-data2."
                            : "heroes-data2 is already present. Use --force to download it again."
                    );
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"heroes-data2 download failed: {e.Message}");
                    return 1;
                }

                return PrintStatus(settings.HeroesDataPath);
            }
        );
        return command;
    }

    private static int PrintStatus(string path)
    {
        string newest = GameData.NewestHeroData(path);
        Console.WriteLine($"HeroesData: {path}");
        if (newest == null)
        {
            Console.WriteLine(
                "No herodata_*.json. Run `heroesreplay data download` before `services start`."
            );
            return 1;
        }

        Console.WriteLine(
            $"Hero data: {Path.GetFileName(newest)} ({File.GetLastWriteTime(newest):yyyy-MM-dd})"
        );
        return 0;
    }
}
