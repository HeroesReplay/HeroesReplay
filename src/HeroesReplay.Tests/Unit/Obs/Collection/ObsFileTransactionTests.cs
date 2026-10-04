using System;
using System.IO;
using HeroesReplay.Core.Obs.Collection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsFileTransactionTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Write_BacksUpTheCurrentFileBeforeReplacingIt()
    {
        string root = TempRoot();
        try
        {
            string file = Path.Combine(root, "scenes", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "old");

            string backup = ObsFileTransaction.Write(file, "new", Backups(root), Now);

            Assert.Equal("new", File.ReadAllText(file));
            Assert.Equal("old", File.ReadAllText(backup));
            Assert.Equal(
                Path.Combine(Backups(root), "scenes-HeroesReplay.json.20261003T093000000Z.bak"),
                backup
            );
            Assert.Equal(new[] { file }, Directory.GetFiles(Path.GetDirectoryName(file)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_NewFile_HasNoBackup()
    {
        string root = TempRoot();
        try
        {
            string file = Path.Combine(root, "profiles", "HeroesReplay", "basic.ini");

            Assert.Null(ObsFileTransaction.Write(file, "[General]", Backups(root), Now));
            Assert.Equal("[General]", File.ReadAllText(file));
            Assert.Empty(ObsFileTransaction.Backups(Backups(root), file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_FailedReplace_LeavesTheOriginalAndNoTempFile()
    {
        string root = TempRoot();
        try
        {
            string file = Path.Combine(root, "scenes", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "old");

            // OBS (or anything) holding the file open without delete sharing blocks the replace.
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() =>
                    ObsFileTransaction.Write(file, "new", Backups(root), Now)
                );
            }

            Assert.Equal("old", File.ReadAllText(file));
            Assert.Equal(new[] { file }, Directory.GetFiles(Path.GetDirectoryName(file)));
            string backup = Assert.Single(ObsFileTransaction.Backups(Backups(root), file));
            Assert.Equal("old", File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_KeepsTheNewestBackupsOfEachFile()
    {
        string root = TempRoot();
        try
        {
            string collection = Path.Combine(root, "scenes", "HeroesReplay.json");
            string profile = Path.Combine(root, "HeroesReplay", "basic.ini");
            ObsFileTransaction.Write(profile, "profile", Backups(root), Now);
            ObsFileTransaction.Write(profile, "profile 2", Backups(root), Now);
            for (int i = 0; i <= ObsFileTransaction.BackupsKept + 2; i++)
            {
                ObsFileTransaction.Write(collection, "v" + i, Backups(root), Now.AddMinutes(i));
            }

            string[] backups = ObsFileTransaction.Backups(Backups(root), collection);

            Assert.Equal(ObsFileTransaction.BackupsKept, backups.Length);
            Assert.Equal("v" + (ObsFileTransaction.BackupsKept + 1), File.ReadAllText(backups[0]));
            Assert.Single(ObsFileTransaction.Backups(Backups(root), profile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Backups(string root) => Path.Combine(root, "backups");

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-tx-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }
}
