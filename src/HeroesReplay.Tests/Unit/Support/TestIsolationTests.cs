using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// #331: unit tests run in parallel worktrees on one machine, and on ASA-SERVER next to a
/// running stack. They failed when they shared machine state. These scans fail on the patterns
/// that did it, so a new test cannot bring one back.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class TestIsolationTests
{
    /// <summary>
    /// The marker for a line that only builds the expected text of a machine path and never
    /// opens it.
    /// </summary>
    public const string PathTextOnly = "isolation: path text only";

    [Fact]
    public void UnitTests_LeaveTheMachinesHeroesReplayFoldersAlone()
    {
        // %LOCALAPPDATA%\HeroesReplay belongs to the running stack: it rewrites status.json
        // every few seconds, and ObsDesiredStateTests compared that file's timestamp before and
        // after. A test that needs such a file gives the code a temp path.
        IEnumerable<string> found = Find(
                UnitTestFiles(),
                new Regex(
                    @"SpecialFolder\.LocalApplicationData|GetEnvironmentVariable\(\s*""LOCALAPPDATA"""
                )
            )
            .Concat(
                Find(
                    UnitTestFiles(),
                    new Regex(
                        @"\b(File|Directory)\.\w+\(\s*@""C:\\heroesreplay",
                        RegexOptions.IgnoreCase
                    )
                )
            )
            .Where(hit => !hit.Contains(PathTextOnly, StringComparison.Ordinal));

        Assert.Empty(found);
    }

    [Fact]
    public void UnitTests_DoNotSearchTheSharedTempFolder()
    {
        // Another test process's files are in the same %TEMP% at the same moment: a plan test
        // that looked for any recent heroesreplay-obs-plan-* folder there failed whenever a
        // parallel run was inside its own plan. Search a folder of the test's own.
        IEnumerable<string> found = Find(
            UnitTestFiles(),
            new Regex(
                @"\b(GetDirectories|GetFiles|GetFileSystemEntries|EnumerateDirectories|EnumerateFiles|EnumerateFileSystemEntries)\(\s*Path\.GetTempPath\(\)"
            )
        );

        Assert.Empty(found);
    }

    [Fact]
    public void ProductionCode_HasNoProcessWideNamedMutex()
    {
        // A static mutex with a fixed name is shared by every instance in every process of the
        // session: a test's temp file waited on the running spectator's lock. Name the mutex
        // from the file it guards (FileMutexName) or take the name from the constructor.
        IEnumerable<string> found = Find(
            SourceFiles("HeroesReplay.Core").Concat(SourceFiles("HeroesReplay.CLI")),
            new Regex(@"static\s+readonly\s+Mutex\s+\w+\s*=\s*new")
        );

        Assert.Empty(found);
    }

    [Fact]
    public void TheScans_FindWhatTheyLookFor()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-isolation-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            string sample = Path.Combine(root, "Sample.cs");
            File.WriteAllLines(
                sample,
                [
                    "string live = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);",
                    "string text = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); // "
                        + PathTextOnly,
                    "var any = Directory.GetDirectories(Path.GetTempPath(), \"x-*\");",
                    "private static readonly Mutex Gate = new(false, @\"Local\\Shared\");",
                ]
            );

            Assert.Equal(
                2,
                Find([sample], new Regex(@"SpecialFolder\.LocalApplicationData")).Count()
            );
            Assert.Single(
                Find([sample], new Regex(@"SpecialFolder\.LocalApplicationData")),
                hit => !hit.Contains(PathTextOnly, StringComparison.Ordinal)
            );
            Assert.Single(Find([sample], new Regex(@"\bGetDirectories\(\s*Path\.GetTempPath\(\)")));
            Assert.Single(Find([sample], new Regex(@"static\s+readonly\s+Mutex\s+\w+\s*=\s*new")));
        }
        finally
        {
            TestTemp.Delete(root);
        }
    }

    /// <summary>Each match as <c>file:line: text</c>, with the whole line it starts on.</summary>
    private static IEnumerable<string> Find(IEnumerable<string> files, Regex pattern)
    {
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(text))
            {
                int start = text.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                int end = text.IndexOf('\n', match.Index);
                string line = text[start..(end < 0 ? text.Length : end)].Trim();
                int number = text.AsSpan(0, match.Index).Count('\n') + 1;
                yield return $"{Path.GetFileName(file)}:{number}: {line}";
            }
        }
    }

    private static IEnumerable<string> UnitTestFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "src", "HeroesReplay.Tests", "Unit"),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(file =>
                !string.Equals(
                    Path.GetFileName(file),
                    nameof(TestIsolationTests) + ".cs",
                    StringComparison.Ordinal
                )
            );

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "src", project),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(file =>
                !file.Contains(
                    Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
                && !file.Contains(
                    Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            );

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
