using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class AwsSecretKeyOption : Option
    {
        public AwsSecretKeyOption(string name) : base("--aws-secret-key", description: $"The AWS Secret key of the account. You can also set the environment variable: {name}")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_SECRET_KEY));
            Argument = new Argument<string?>(getDefaultValue: () => Environment.GetEnvironmentVariable(Constants.HEROES_REPLAY_AWS_SECRET_KEY, EnvironmentVariableTarget.Process));
        }
    }
}