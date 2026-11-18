using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.World;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 Q1 (docs/automation-engine.md E2, <c>TODO(engine-threat-flip)</c>): the three detectors that moved onto the board (the keeper's and the fight's awake reads, the heal station's pre-check) give the answer their old scans
/// gave, on mixed states: the same creature of the same distance, the later of two at one distance, the reach as the old scan narrowed it, an attack heard before the listening began or before its last clear not counted.
/// The old scans stay in the code (<see cref="WorldObjectTable.NearestAttackableCreature"/>, <see cref="HostileReach.TryNearest"/>) for their other callers, and are the reference here.
/// </summary>
public sealed class ThreatFlipQ1Tests
{
    private static void Mixed(ThreatRig t)
    {
        t.Creature("Gold Phyntos Wasp", t.Listed, 9f); // nearest of two at 9 m
        t.Creature("Shadow Child", t.Listed, 9f); // the same distance, met later
        t.Creature("Brutish Monouga", t.Listed, 15f);
        t.Creature("Shopkeeper Renald the Elder", t.Listed, 3f, flags: 0); // an NPC: may not be attacked
        t.Creature("Another Player", t.Listed, 4f, flags: WorldObjectTable.AttackableFlag | WorldObjectTable.PlayerFlag);
        var dead = t.Creature("Dead Drudge", t.Listed, 2f);
        t.DeadMotion(dead);
        t.Creature("Distant Tusker", t.Listed, 80f);
    }

    [Theory]
    [InlineData(24.0)]
    [InlineData(9.0)] // exactly at two creatures
    [InlineData(8.99)]
    [InlineData(14.9)]
    [InlineData(15.0)]
    [InlineData(13.37)] // a reach a single narrows: the old scan used the narrowed number
    [InlineData(79.99999)]
    public void The_awake_query_names_what_the_old_scan_names(double reach)
    {
        var t = new ThreatRig();
        _ = t.Board;
        var here = t.View.State.Self.Position!.Value;
        Assert.False(t.View.State.Objects.NearestAttackableCreature(in here, (float)reach, t.View.State.Self.Guid, out _, t.View.Live)); // nobody yet

        var empty = ThreatQuery.AwakeNear((float)reach) with { TiesToLast = true };
        Assert.Equal(ThreatAnswerKind.None, t.Board.Ask(t.View, in empty, t.Now).Kind);

        Mixed(t);
        var found = t.View.State.Objects.NearestAttackableCreature(in here, (float)reach, t.View.State.Self.Guid, out var guid, t.View.Live);
        var query = ThreatQuery.AwakeNear((float)reach) with { TiesToLast = true };
        var answer = t.Board.Ask(t.View, in query, t.Now);
        Assert.Equal(found, answer.Kind == ThreatAnswerKind.Standing);
        Assert.Equal(guid, answer.Guid);
    }

    [Fact]
    public void Two_creatures_at_one_distance_name_the_later_only_when_asked_to()
    {
        var t = new ThreatRig();
        var first = t.Creature("Gold Phyntos Wasp", t.Listed, 6f);
        var later = t.Creature("Shadow Child", t.Listed, 6f);
        var plain = ThreatQuery.AwakeNear(24);
        Assert.Equal(first, t.Board.Ask(t.View, in plain, t.Now).Guid); // the board's own rule is unchanged
        var q1 = plain with { TiesToLast = true };
        Assert.Equal(later, t.Board.Ask(t.View, in q1, t.Now).Guid);
    }

    /// <summary>The pre-check's question as the station asks it: the names heard since the listening last began, within the reach.</summary>
    private static ThreatAnswer PreCheck(ThreatRig t, double listenFrom)
    {
        var now = t.Now;
        var query = ThreatQuery.OnHer(HostileReach.DefaultMetres, now - listenFrom) with { Since = listenFrom, TiesToLast = true };
        return t.Board.Ask(t.View, in query, now);
    }

    private static void SameAsTheOldRead(ThreatRig t, AttackWatch watch, double listenFrom, string state)
    {
        var near = HostileReach.TryNearest(t.View, HostileReach.DefaultMetres, watch.Names, out var guid, out var name, out var metres);
        var answer = PreCheck(t, listenFrom);
        Assert.True(near == (answer.Kind == ThreatAnswerKind.Standing), $"{state}: the old read says {near}, the board {answer}");
        if (near)
        {
            Assert.True(guid == answer.Guid && name == answer.Name && metres == answer.Metres, $"{state}: the old read names 0x{guid:X8} {name} at {metres}, the board {answer}");
        }
    }

    [Fact]
    public void The_pre_check_question_names_what_the_old_read_names_on_mixed_states()
    {
        var t = new ThreatRig();
        _ = t.Board;
        Mixed(t);
        t.Creature("Unheard Banderling", t.Listed, 5f); // nearest of all the attackable, never heard
        t.Hit("Distant Tusker"); // heard before the listening began: neither read counts it
        t.Wait(1);
        var began = Math.BitIncrement(t.Now);
        var watch = t.LegWatch();
        SameAsTheOldRead(t, watch, began, "nobody heard since the start");

        t.Wait(1);
        t.Hit("Brutish Monouga");
        SameAsTheOldRead(t, watch, began, "one heard, at 15 m");

        t.Evade("Gold Phyntos Wasp");
        t.Wait(2);
        SameAsTheOldRead(t, watch, began, "an evade of the nearest of a tie: the later of the two at one distance");

        t.Magic("You resist the spell cast by Shadow Child");
        SameAsTheOldRead(t, watch, began, "both of the tie heard");

        t.Hit("Distant Tusker"); // heard, but 80 m off
        SameAsTheOldRead(t, watch, began, "one heard past the reach");

        t.Wait(5);
        t.Hit("Unheard Banderling");
        SameAsTheOldRead(t, watch, began, "a long listen: the window is the time it has listened");

        watch.Clear();
        began = Math.BitIncrement(t.Now);
        SameAsTheOldRead(t, watch, began, "after a clear: the names heard before it are forgotten by both");
        Assert.Equal(ThreatAnswerKind.None, PreCheck(t, began).Kind);

        t.Wait(1);
        t.Evade("Brutish Monouga");
        SameAsTheOldRead(t, watch, began, "heard again after the clear");
        Assert.Equal(ThreatAnswerKind.Standing, PreCheck(t, began).Kind);
        watch.Dispose();
    }
}
