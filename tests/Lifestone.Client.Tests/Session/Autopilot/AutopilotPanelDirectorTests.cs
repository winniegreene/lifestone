using Lifestone.Client.Session.Autopilot;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M5 7 G3: the panels a person would keep open by the dial's state (the engine-free part; the play host adapts the retail UI's panel host).</summary>
public sealed class AutopilotPanelDirectorTests
{
    private sealed class Surface : IAutopilotPanelSurface
    {
        public uint Shown { get; set; }

        public List<string> Calls { get; } = [];

        public bool Open(uint panelId)
        {
            Calls.Add("open " + panelId);
            Shown = panelId; // the host replaces the page shown
            return true;
        }

        public void Close(uint panelId)
        {
            Calls.Add("close " + panelId);
            if (Shown == panelId)
            {
                Shown = 0;
            }
        }
    }

    [Theory]
    [InlineData("fight:den", 11u)]
    [InlineData("fight", 11u)]
    [InlineData("known-state", 7u)]
    [InlineData("buff:flame", 7u)]
    [InlineData("trip:supplies", 7u)]
    [InlineData("travel:den", 0u)]
    [InlineData("upkeep", 0u)]
    [InlineData("death:corpse", 0u)]
    [InlineData("stopped", 0u)]
    [InlineData("reconnecting", 0u)]
    [InlineData("", 0u)]
    [InlineData(null, 0u)]
    public void The_state_names_the_page(string? stage, uint expected) => Assert.Equal(expected, AutopilotPanelDirector.PanelFor(stage));

    [Fact]
    public void A_fight_opens_the_attributes_page_and_a_buff_or_a_trip_swaps_it_for_the_paper_doll_and_each_change_logs_once()
    {
        var surface = new Surface();
        var log = new List<string>();
        var director = new AutopilotPanelDirector(surface, log.Add);

        director.Tick(true, "known-state", 1);
        director.Tick(true, "known-state", 2);
        Assert.Equal(7u, surface.Shown);
        director.Tick(true, "buff:flame", 3);
        director.Tick(true, "travel:den", 4);
        director.Tick(true, "fight:den", 5);
        director.Tick(true, "fight:den", 6);
        Assert.Equal(11u, surface.Shown);
        director.Tick(true, "upkeep", 7);
        Assert.Equal(11u, surface.Shown); // an upkeep leaves the page alone
        director.Tick(true, "trip:supplies", 8);
        Assert.Equal(7u, surface.Shown);

        Assert.Equal(["open 7", "open 11", "open 7"], surface.Calls);
        Assert.Equal(3, log.Count);
        Assert.Equal(3, director.Opened);
        Assert.Contains("attributes and experience", log[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_the_person_closed_is_not_opened_again_for_sixty_seconds_and_a_page_the_person_opened_stays()
    {
        var surface = new Surface();
        var director = new AutopilotPanelDirector(surface, _ => { });
        director.Tick(true, "fight:den", 100);
        Assert.Equal(11u, surface.Shown);

        surface.Shown = 0; // the person closes it
        director.Tick(true, "fight:den", 101);
        director.Tick(true, "upkeep", 110);
        director.Tick(true, "fight:den", 120); // a new fight state 19 s after the close: left closed
        Assert.Equal(0u, surface.Shown);
        director.Tick(true, "upkeep", 130);
        director.Tick(true, "fight:den", 170); // 69 s after the close: opens again
        Assert.Equal(11u, surface.Shown);

        surface.Shown = 16; // the person opens the map over it
        director.Tick(true, "buff:flame", 200);
        Assert.Equal(16u, surface.Shown); // the person's page stays
        Assert.DoesNotContain("close 16", surface.Calls);
        Assert.Equal(["open 11", "open 11"], surface.Calls);
    }

    [Fact]
    public void A_dial_that_is_not_running_opens_nothing_and_forgets_its_state()
    {
        var surface = new Surface();
        var director = new AutopilotPanelDirector(surface, _ => { });
        director.Tick(false, "fight:den", 1);
        Assert.Empty(surface.Calls);
        director.Tick(true, "fight:den", 2);
        Assert.Equal(11u, surface.Shown);
        director.Tick(false, "fight:den", 3);
        surface.Shown = 0;
        director.Tick(true, "fight:den", 4); // a new run: the same state opens the page again (the close was not a person's, the run had ended)
        Assert.Equal(11u, surface.Shown);
    }

    [Fact]
    public void A_page_not_wired_yet_is_asked_again_each_frame_and_logged_once_M5_7_round_8()
    {
        // M5 7 lead (round 8): page 7 opened before the login wired its controller brought up the placeholder message; the play
        // surface now refuses a page that is not built yet, and the director asks again each frame, logging once.
        var log = new List<string>();
        var director = new AutopilotPanelDirector(new RefusingSurface(), log.Add);
        director.Tick(true, "fight:den", 1);
        director.Tick(true, "fight:den", 2);
        director.Tick(true, "fight:den", 3);
        Assert.Single(log);
        Assert.Contains("not ready", log[0], StringComparison.Ordinal);
        Assert.Equal(0, director.Opened);
    }

    private sealed class RefusingSurface : IAutopilotPanelSurface
    {
        public uint Shown => 0;

        public bool Open(uint panelId) => false;

        public void Close(uint panelId)
        {
        }
    }
}
