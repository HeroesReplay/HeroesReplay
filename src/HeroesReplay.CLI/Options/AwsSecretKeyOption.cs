using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class AwsSecretKeyOption : Option
    {
        public AwsSecretKeyOption() : base("--aws-secret-key", "The AWS Secret key of the account that is charged for accessing the HotsApi S3 service. Alternatively set an environment variable HEROES_REPLAY_AWS_SECRET_KEY")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_SECRET_KEY));
            Argument = new Argument<string?>(() => Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_SECRET_KEY, EnvironmentVariableTarget.Process));
        }
    }
}