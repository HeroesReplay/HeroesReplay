using System.CommandLine;
using System.CommandLine.Builder;
using System.IO;
using HeroesReplay.Shared;

namespace HeroesReplay.CLI.Options
{
    public class BattlenetOption : Option
    {
        public BattlenetOption() : base(new[] { "--bnet" }, @"The directory that contains Battle.net.")
        {
            Required = !Directory.Exists(Constants.Bnet.BATTLE_NET_DEFAULT_INSTALL_PATH);
            Argument = new Argument<DirectoryInfo>(() => new DirectoryInfo(Constants.Bnet.BATTLE_NET_DEFAULT_INSTALL_PATH)).ExistingOnly();
        }
    }
}