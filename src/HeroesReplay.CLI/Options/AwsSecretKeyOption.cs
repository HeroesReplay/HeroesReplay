using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class AwsSecretKeyOption : Option
    {
        public AwsSecretKeyOption(string variable) : base("--aws-secret-key", description: $"The AWS Secret key of the account. You can also set the environment variable: {variable}")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
            Argument = new Argument<string?>(getDefaultValue: () => Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process));
        }
    }
}