using System.Linq;
using System.Xml.Linq;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceLogonTaskTests
{
    private static readonly XNamespace Task =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Xml_StartsTheSupervisedStackAtThisUsersLogon_Unelevated()
    {
        XDocument task = XDocument.Parse(
            ServiceLogonTask.Xml(
                @"STREAM-PC\stream",
                @"C:\heroesreplay\app\heroesreplay.exe",
                null,
                null
            )
        );

        XElement trigger = task.Descendants(Task + "LogonTrigger").Single();
        Assert.Equal(@"STREAM-PC\stream", (string)trigger.Element(Task + "UserId"));
        Assert.Equal(ServiceLogonTask.LogonDelay, (string)trigger.Element(Task + "Delay"));
        XElement principal = task.Descendants(Task + "Principal").Single();
        Assert.Equal("InteractiveToken", (string)principal.Element(Task + "LogonType"));
        Assert.Equal("LeastPrivilege", (string)principal.Element(Task + "RunLevel"));
        Assert.Equal("PT0S", (string)task.Descendants(Task + "ExecutionTimeLimit").Single());
        Assert.Equal(
            "IgnoreNew",
            (string)task.Descendants(Task + "MultipleInstancesPolicy").Single()
        );
        XElement exec = task.Descendants(Task + "Exec").Single();
        Assert.Equal(
            @"C:\heroesreplay\app\heroesreplay.exe",
            (string)exec.Element(Task + "Command")
        );
        Assert.Equal("services start --supervise", (string)exec.Element(Task + "Arguments"));
        Assert.Equal(@"C:\heroesreplay\app", (string)exec.Element(Task + "WorkingDirectory"));
    }

    [Fact]
    public void Xml_WithAnEnvironment_SetsItForThisStartOnly()
    {
        XElement exec = XDocument
            .Parse(
                ServiceLogonTask.Xml(
                    @"ASA-SERVER\admin",
                    @"C:\heroesreplay\e2e\app\heroesreplay.exe",
                    "dev",
                    "download,youtube"
                )
            )
            .Descendants(Task + "Exec")
            .Single();

        Assert.Equal("cmd.exe", (string)exec.Element(Task + "Command"));
        Assert.Equal(
            "/d /c \"set HEROES_REPLAY_ENV=dev&& \"C:\\heroesreplay\\e2e\\app\\heroesreplay.exe\" services start --supervise --roles download,youtube\"",
            (string)exec.Element(Task + "Arguments")
        );
    }
}
