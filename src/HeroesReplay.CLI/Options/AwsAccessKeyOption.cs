using System;
using System.CommandLine;

namespace HeroesReplay.CLI.Options
{
    public class AwsAccessKeyOption : Option
    {
        public AwsAccessKeyOption(string variable) : base("--aws-access-key", description: $"The AWS Access key. You can also set the environment variable: {variable}")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
            Argument = new Argument<string?>(getDefaultValue: () => Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process));
        }
    }
}