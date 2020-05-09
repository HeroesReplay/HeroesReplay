using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class AwsAccessKeyOption : Option
    {
        public AwsAccessKeyOption(string name) : base("--aws-access-key", description: $"The AWS Access key. You can also set the environment variable: {name}")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
            Argument = new Argument<string?>(getDefaultValue: () => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
        }
    }
}