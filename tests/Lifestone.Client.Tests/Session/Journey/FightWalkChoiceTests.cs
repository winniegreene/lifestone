using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 body (live ladder49 2026-11-17 01:58:53 UTC, <c>m6-body-apart</c>): the station's choice of a fight's walk. The game's host draws the player, so its own walk serves every ground (a quest's hunt-until ground included) and,
/// when it gives none, the fight has no walk at all: never the offline <see cref="GroundWalk"/>, whose own walker moved the server player 40.7 m from the drawn body in the Crater Caves. The offline walker is for a host that draws nothing.
/// </summary>
public sealed class FightWalkChoiceTests
{
    /// <summary>A host's walks: <see cref="IJourneyWalks.FightWalk"/> answers what the test says.</summary>
    private sealed class Walks(bool drawsBody, IFightWalk? walk) : IJourneyWalks
    {
        public int Asked { get; private set; }

        public bool DrawsBody => drawsBody;

        public IFightWalk? FightWalk(JourneyContext context, HuntingGround ground)
        {
            Asked++;
            return walk;
        }

        public IHuntFollow? HuntFollow(JourneyContext context) => null;
    }

    [Fact]
    public void A_host_that_draws_the_player_gives_its_own_walk_for_any_ground()
    {
        using var own = new ScriptedRecoveringWalk(_ => false);
        var offlineMade = 0;
        var walks = new Walks(drawsBody: true, own);
        var (kind, why) = FightWalkChoice.Choose(walks, out var walk, () => walks.FightWalk(null!, null!), () => { offlineMade++; return null; });
        Assert.Equal(FightWalkChoice.Kind.Host, kind);
        Assert.Same(own, walk);
        Assert.Equal(0, offlineMade);
        Assert.Contains("drawn player", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_draws_the_player_and_gives_no_walk_gets_none_and_never_the_offline_walker()
    {
        using var offline = new ScriptedRecoveringWalk(_ => false);
        var offlineMade = 0;
        var walks = new Walks(drawsBody: true, null);
        var (kind, why) = FightWalkChoice.Choose(walks, out var walk, () => walks.FightWalk(null!, null!), () => { offlineMade++; return offline; });
        Assert.Equal(FightWalkChoice.Kind.None, kind);
        Assert.Null(walk);
        Assert.Equal((1, 0), (walks.Asked, offlineMade)); // the offline walker is not even made: it binds the session's movement when made
        Assert.Contains("TODO(" + FightWalkChoice.NoDrawnWalkTodo + ")", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)] // the headless host's own body
    [InlineData(false, false)] // a headless host that gave none: the offline walker
    public void A_host_that_draws_nothing_keeps_its_walk_or_the_offline_walker(bool drawsBody, bool hostGives)
    {
        using var own = new ScriptedRecoveringWalk(_ => false);
        using var offline = new ScriptedRecoveringWalk(_ => false);
        var walks = new Walks(drawsBody, hostGives ? own : null);
        var (kind, _) = FightWalkChoice.Choose(walks, out var walk, () => walks.FightWalk(null!, null!), () => offline);
        Assert.Equal(hostGives ? FightWalkChoice.Kind.Host : FightWalkChoice.Kind.Offline, kind);
        Assert.Same(hostGives ? own : offline, walk);
    }

    [Fact]
    public void A_scripted_host_with_no_walks_gets_the_offline_walker_and_none_without_dats()
    {
        using var offline = new ScriptedRecoveringWalk(_ => false);
        Assert.Equal(FightWalkChoice.Kind.Offline, FightWalkChoice.Choose(null, out var walk, () => throw new InvalidOperationException("no host walk is asked of a host with none"), () => offline).Kind);
        Assert.Same(offline, walk);
        Assert.Equal(FightWalkChoice.Kind.None, FightWalkChoice.Choose(null, out walk, () => null, () => null).Kind);
        Assert.Null(walk);
    }
}
