using System;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Spectating.Control;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Control;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PanelDirectorTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Chat = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Rule = TimeSpan.FromSeconds(30);

    [Fact]
    public void ChatRequestWhileTheRulePanelIsUp_StaysUpForItsDurationAndIsMarkedHidden()
    {
        var panels = new PanelDirector();
        Assert.Equal(
            new PanelStep(Panel.None, false, Panel.Talents),
            Step(panels, Panel.None, Panel.Talents)
        );

        // The request arrives while the automatic Talents panel is up, and the rule wants nothing.
        PanelStep swap = Step(panels, Panel.DeathDamageRole, Panel.None);
        Assert.Equal(new PanelStep(Panel.Talents, false, Panel.DeathDamageRole), swap);
        Assert.True(panels.ChatRequested);

        for (int second = 1; second < 10; second++)
        {
            Assert.Equal(
                default(PanelStep) with
                {
                    Hide = Panel.None,
                    Show = Panel.None,
                },
                Step(panels, Panel.None, Panel.None)
            );
            Assert.Equal(Panel.DeathDamageRole, panels.Current);
        }

        PanelStep end = Step(panels, Panel.None, Panel.None);
        Assert.Equal(new PanelStep(Panel.DeathDamageRole, true, Panel.None), end);
        Assert.Equal(Panel.None, panels.Current);
        Assert.False(panels.ChatRequested);
    }

    [Fact]
    public void ChatRequestForTheRulePanelAlreadyUp_SendsNoKeyAndStartsItsOwnDuration()
    {
        var panels = new PanelDirector();
        Step(panels, Panel.None, Panel.Talents);

        Assert.Equal(
            new PanelStep(Panel.None, false, Panel.None),
            Step(panels, Panel.Talents, Panel.Talents)
        );
        Assert.True(panels.ChatRequested);
        for (int second = 1; second < 10; second++)
        {
            Step(panels, Panel.None, Panel.None);
        }

        // Its time is up and the rule wants nothing: hidden once, as the viewer's panel.
        Assert.Equal(
            new PanelStep(Panel.Talents, true, Panel.None),
            Step(panels, Panel.None, Panel.None)
        );
    }

    [Fact]
    public void ANewRequestOverAViewersPanel_MarksTheOldOneHidden()
    {
        var panels = new PanelDirector();
        Step(panels, Panel.DeathDamageRole, Panel.None);

        PanelStep swap = Step(panels, Panel.Experience, Panel.None);

        Assert.Equal(new PanelStep(Panel.DeathDamageRole, true, Panel.Experience), swap);
        Assert.True(panels.ChatRequested);
    }

    [Fact]
    public void RulePanel_IsHiddenWhenTheRuleStopsWantingIt_AndToggledAtItsLimit()
    {
        var panels = new PanelDirector();
        Step(panels, Panel.None, Panel.Talents);
        for (int second = 1; second < 30; second++)
        {
            Assert.Equal(
                new PanelStep(Panel.None, false, Panel.None),
                Step(panels, Panel.None, Panel.Talents)
            );
        }

        Assert.Equal(
            new PanelStep(Panel.Talents, false, Panel.Talents),
            Step(panels, Panel.None, Panel.Talents)
        );
        Assert.Equal(
            new PanelStep(Panel.Talents, false, Panel.None),
            Step(panels, Panel.None, Panel.None)
        );
    }

    private static PanelStep Step(PanelDirector panels, Panel request, Panel automatic) =>
        panels.Step(request, automatic, Chat, Rule, Second);
}
