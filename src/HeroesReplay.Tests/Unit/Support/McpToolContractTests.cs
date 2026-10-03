using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.CLI.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class McpToolContractTests
{
    [Fact]
    public void EveryTool_ReturnsStructuredContentNotJsonInAString()
    {
        MethodInfo[] tools = typeof(SpectatorMcpTools)
            .Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
            .ToArray();

        Assert.NotEmpty(tools);
        foreach (MethodInfo tool in tools)
        {
            McpServerToolAttribute attribute = tool.GetCustomAttribute<McpServerToolAttribute>();
            Type result =
                tool.ReturnType.IsGenericType
                && tool.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                    ? tool.ReturnType.GetGenericArguments()[0]
                    : tool.ReturnType;

            Assert.True(result != typeof(string), attribute.Name + " returns a string.");
            Assert.True(
                attribute.UseStructuredContent || result == typeof(CallToolResult),
                attribute.Name + " does not return structured content."
            );
            Assert.True(attribute.ReadOnly, attribute.Name + " is not marked read-only.");
            Assert.False(attribute.Destructive, attribute.Name + " is marked destructive.");
        }
    }

    [Theory]
    [InlineData(".mcp.json", "dotnet")]
    [InlineData(
        "tools/release.mcp.json",
        @"${HEROESREPLAY_APP:-C:\heroesreplay\app}\heroesreplay.exe"
    )]
    public void McpConfigs_StartTheReadOnlyServer(string file, string command)
    {
        using JsonDocument config = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), file))
        );
        JsonElement server = config
            .RootElement.GetProperty("mcpServers")
            .GetProperty("heroesreplay");

        Assert.Equal(command, server.GetProperty("command").GetString());
        Assert.Equal("mcp", server.GetProperty("args").EnumerateArray().Last().GetString());
        // Only the read-only HeroesReplay server; obs-mcp stays a dev-only, per-machine tool.
        Assert.Single(config.RootElement.GetProperty("mcpServers").EnumerateObject());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("heroes-replay.slnx was not found.");
    }
}
