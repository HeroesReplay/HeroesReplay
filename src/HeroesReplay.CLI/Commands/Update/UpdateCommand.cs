using System;
using System.CommandLine;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.SelfUpdate;

namespace HeroesReplay.CLI.Commands.Update;

public class UpdateCommand : Command
{
    public UpdateCommand()
        : base(
            "update",
            "Compare this install with the latest GitHub Release. A source build does not replace itself."
        )
    {
        Subcommands.Add(CheckCommand());
    }

    private static Command CheckCommand()
    {
        var command = new Command(
            "check",
            "Print the installed version and the latest release tag. Does not download or restart."
        );
        command.SetAction((parseResult, cancellationToken) => CheckAsync(cancellationToken));
        return command;
    }

    private static async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        string install = Path.GetDirectoryName(Environment.ProcessPath);
        string local = ReleaseInstall.ReadVersion(install);
        Console.WriteLine(
            string.IsNullOrWhiteSpace(local)
                ? "Installed version: (none)"
                : $"Installed version: {local}"
        );
        if (ReleaseInstall.LooksLikeSourceBuild(install))
        {
            Console.WriteLine("This is a source build. It will not self-update.");
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("HeroesReplay", "1")
            );
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
            );
            string json = await http.GetStringAsync(
                    $"https://api.github.com/repos/{ReleaseSettings.DefaultRepository}/releases/latest",
                    cancellationToken
                )
                .ConfigureAwait(false);
            ReleaseOffer? offer = GitHubReleaseJson.Read(
                json,
                ReleaseSettings.DefaultAssetName,
                localVersion: "\0"
            );
            if (offer is ReleaseOffer found)
            {
                Console.WriteLine($"Latest release: {found.Version}");
                Console.WriteLine(
                    string.Equals(found.Version, local, StringComparison.OrdinalIgnoreCase)
                        ? "This install is current."
                        : "A newer release is available."
                );
                return 0;
            }

            Console.WriteLine("No release asset named heroesreplay-win-x64.zip was found.");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not read the latest release. {e.Message}");
            return 1;
        }
    }
}
