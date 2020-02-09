using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class AwsAccessKeyOption : Option
    {
        public AwsAccessKeyOption() : base("--aws-access-key", "The AWS Access key of the account that is charged for accessing the HotsApi S3 service. Alternatively set an environment variable HEROES_REPLAY_AWS_ACCESS_KEY")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_ACCESS_KEY));
            Argument = new Argument<string?>(() => Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_ACCESS_KEY, EnvironmentVariableTarget.Process));
        }
    }
}