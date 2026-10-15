using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The login check's drawn census (m2 wave 3, dod-6-7-1 and dod-10-11-14-15-3): the placed objects of every kind against the guids the layer drew, each object not drawn either
/// excused by a rule (hidden, translucent, part-less, an unshown cell) or counted as missing.
/// </summary>
public sealed class DrawnCensusTests
{
    private const uint Cell = 0x860201AD;
    private const uint Creature = 0x10;
    private const uint Misc = 0x80;

    private static (ReducerHarness Harness, Dictionary<string, uint> Guids) World()
    {
        var h = new ReducerHarness();
        h.EnterWorld(Cell);
        var guids = new Dictionary<string, uint>();
        uint next = 0x80000300;
        void Add(string name, uint itemType, uint flags)
        {
            var guid = next++;
            guids[name] = guid;
            var msg = Create(guid, Stamps(), Position(Cell), name: name);
            h.Reducer.OnCreateObject(h.Context, msg with { Weenie = msg.Weenie with { ItemType = itemType, ObjectDescriptionFlags = flags } }, false);
        }

        Add("Guide", Creature, 0);
        Add("Exit Portal", Misc, 0x40000);
        Add("Door", Misc, 0x1000);
        Add("Lifestone", Misc, 0x4000);
        Add("Training Sign", Misc, 0x4);
        Add("Hidden Statue", 0, 0x4);
        return (h, guids);
    }

    [Fact]
    public void Every_placed_kind_drawn_is_a_match_and_the_counts_are_per_kind()
    {
        var (h, g) = World();
        var drawn = new HashSet<uint>(g.Values);
        var census = DrawnCensus.Compare(h.State, drawn, _ => DrawExpectation.Drawable);

        Assert.True(census.Matches);
        Assert.Equal(census.Placed, census.Drawn);
        Assert.Equal(1, census.Drawn.Portals);
        Assert.Equal(1, census.Drawn.Doors);
        Assert.Equal(1, census.Drawn.Lifestones);
        Assert.Equal(1, census.Drawn.Signs);
        Assert.Equal(1, census.Drawn.Npcs);
        Assert.Contains("portals=1/1", census.Describe());
        Assert.DoesNotContain("missing", census.Describe());
    }

    [Fact]
    public void A_visible_object_that_is_not_drawn_is_missing_and_named_with_its_kind()
    {
        var (h, g) = World();
        var drawn = new HashSet<uint>(g.Values);
        drawn.Remove(g["Exit Portal"]);

        var census = DrawnCensus.Compare(h.State, drawn, _ => DrawExpectation.Drawable);

        Assert.False(census.Matches);
        var missing = Assert.Single(census.Missing);
        Assert.Equal(g["Exit Portal"], missing.Guid);
        Assert.Equal(WorldContentKind.Portal, missing.Kind);
        Assert.Equal(1, census.Placed.Portals);
        Assert.Equal(0, census.Drawn.Portals);
        Assert.Contains("missing 1: Exit Portal (Portal", census.Describe());
    }

    [Theory]
    [InlineData(DrawExpectation.Hidden)]
    [InlineData(DrawExpectation.Translucent)]
    [InlineData(DrawExpectation.NoParts)]
    [InlineData(DrawExpectation.CellNotShown)]
    public void An_object_not_drawn_for_a_rule_is_excused_and_counted_by_reason(DrawExpectation reason)
    {
        var (h, g) = World();
        var drawn = new HashSet<uint>(g.Values);
        drawn.Remove(g["Hidden Statue"]);

        var census = DrawnCensus.Compare(h.State, drawn, guid => guid == g["Hidden Statue"] ? reason : DrawExpectation.Drawable);

        Assert.True(census.Matches);
        Assert.Equal(1, census.Excused[reason]);
        Assert.Contains($"excused {reason}=1", census.Describe());
    }

    [Theory]
    [InlineData(DrawExpectation.NoAvatar)]
    [InlineData(DrawExpectation.NoCell)]
    public void An_object_without_an_avatar_or_a_cell_is_reported_as_missing_never_skipped(DrawExpectation reason)
    {
        var (h, g) = World();
        var drawn = new HashSet<uint>(g.Values);
        drawn.Remove(g["Door"]);

        var census = DrawnCensus.Compare(h.State, drawn, _ => reason);

        Assert.False(census.Matches);
        Assert.Equal(reason, Assert.Single(census.Missing).Reason);
    }

    [Fact]
    public void The_player_and_objects_that_are_not_placed_are_not_counted()
    {
        var h = new ReducerHarness();
        h.EnterWorld(Cell);
        var msg = Create(0x80000400, Stamps(), null, name: "Pack Dagger");
        h.Reducer.OnCreateObject(h.Context, msg with { Weenie = msg.Weenie with { ItemType = 1, ObjectDescriptionFlags = 0 } }, false);

        var census = DrawnCensus.Compare(h.State, new HashSet<uint>(), _ => DrawExpectation.Drawable);

        Assert.True(census.Matches);
        Assert.Equal(0, census.Placed.Total);
    }
}
