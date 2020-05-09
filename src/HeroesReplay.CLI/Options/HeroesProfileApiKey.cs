using System;
using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class HeroesProfileApiKey : Option
    {
        public HeroesProfileApiKey(string variable) : base("--heroes-profile-apikey", description: $"The API Key used for calls to Heroes Profile. You can also set the environment variable: {variable}")
        {
            Required = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
            Argument = new Argument<string?>(getDefaultValue: () => Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Process));
        }
    }
}