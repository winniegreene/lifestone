namespace Lifestone.Loopback.Tests.Session;

/// <summary>M6 7 H: the named ends of a trophy journey (<see cref="TrophyRunRead"/>), plain unit tests (no backend), over the shapes of the live round 1's lines.</summary>
public sealed class TrophyRunReadTests
{
    private const string Died = " INFO  [journey] [live rung-change t=182.7] died on the retreat by Lifestone Recall on the run leg: death_cell=0xF8820029 killer='Your body is shattered by Tusker Crimsonback's attack!' respawned=0xA9B40019";
    private const string Follows = " WARN  [journey] journey station=rung-change died: the death station follows (corpse 0x8000118D)";
    private const string GoFailed = " INFO  [station] [live quest-tusk-female t=90.1] quest_step: n=1 kind=go what=to_Brighteyes ok=no ms=30000 read=none fail=go: off-place";

    [Fact]
    public void A_run_with_no_death_and_no_failed_go_is_named_nothing_so_the_case_can_pass()
    {
        string[] lines =
        [
            " INFO  [station] quest_step: n=1 kind=go what=to_Brighteyes ok=yes ms=91000 via=town-network+walk at=0xF682011C",
            " INFO  [nav] leg door_steps=2 door_uses=2 doors_opened=2 door_wait_s=1.2",
        ];
        Assert.Null(TrophyRunRead.Named(lines));
    }

    [Fact]
    public void A_death_is_named_by_its_station_with_the_killer_and_comes_before_what_followed_it()
    {
        var name = TrophyRunRead.Named([Died, Follows, " FAILED start: start:not-on-ground", GoFailed]);
        Assert.NotNull(name);
        Assert.StartsWith("trophy:died-on-the-run:rung-change: ", name, StringComparison.Ordinal);
        Assert.Contains("Tusker Crimsonback", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_go_names_a_door_the_walk_logged()
    {
        var name = TrophyRunRead.Named([" WARN  [nav] door 0x7F682003 stayed shut: open timeout", " INFO  [nav] leg door_steps=1 door_uses=1 doors_opened=0", GoFailed]);
        Assert.NotNull(name);
        Assert.StartsWith("brighteyes-walk:door: ", name, StringComparison.Ordinal);
        Assert.Contains("0x7F682003", name, StringComparison.Ordinal);
        Assert.DoesNotContain("door_steps=", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_go_with_no_door_line_is_the_go_by_name()
    {
        Assert.StartsWith("brighteyes-walk:go: ", TrophyRunRead.Named([GoFailed]), StringComparison.Ordinal);
    }
}
