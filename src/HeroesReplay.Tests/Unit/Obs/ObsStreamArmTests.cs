using System;
using System.IO;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsStreamArmTests
{
    [Fact]
    public void DefaultPath_IsMachineLocalAndOutsideTheRepo()
    {
        string path = ObsStreamArm.DefaultPath();

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "stream-armed"
            ),
            path
        );
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar,
            path,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ArmAndDisarm_WriteAndDeleteTheFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-arm-" + Path.GetRandomFileName());
        var arm = new ObsStreamArm(Path.Combine(root, "HeroesReplay", ObsStreamArm.FileName));
        try
        {
            Assert.False(arm.IsArmed());
            Assert.False(arm.Disarm());

            arm.Arm("unit test");

            Assert.True(arm.IsArmed());
            Assert.Contains("unit test", File.ReadAllText(arm.FilePath), StringComparison.Ordinal);
            Assert.True(arm.Disarm());
            Assert.False(arm.IsArmed());
            Assert.False(File.Exists(arm.FilePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ADirectoryWithTheArmName_IsNotAnArm()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-arm-" + Path.GetRandomFileName());
        string path = Path.Combine(root, ObsStreamArm.FileName);
        try
        {
            Directory.CreateDirectory(path);

            Assert.False(new ObsStreamArm(path).IsArmed());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Settings_HaveNoArmPath()
    {
        Assert.DoesNotContain(
            typeof(OBSSettings).GetProperties(),
            property => property.Name.Contains("Arm", StringComparison.OrdinalIgnoreCase)
        );
    }
}
