using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The offline stand-in answers the server mod's <c>spell learn</c> and <c>item</c> as the mod does (M3 1.1 s2b), in the mod's own reply bytes, so the client's typed results are
/// the same against the stand-in and ACE: learned then known, unknown for an id outside the table, an item created with the stack clamped, and the three item refusals.
/// </summary>
public sealed class StandInToolsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(StandInVisit? visit = null)
        {
            var v = visit ?? new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, v);
                    StandInSessions.AnswerWorld(h, v);
                },
                _time,
                new RecordingLogSink());
            Run(2.0);
        }

        public GameSession Session { get; }

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public ToolHandle<T> Done<T>(ToolHandle<T> handle)
            where T : class
        {
            Run(1.5);
            Assert.True(handle.IsCompleted, handle.Summary);
            return handle;
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_spell_is_learned_and_then_known_and_an_id_outside_the_placeholder_range_is_unknown()
    {
        using var rig = new Rig();
        var first = rig.Done(rig.Session.Tools.LearnSpells([2, 3, 2, 99999], ActionSource.Host));

        // The same id twice in one request: the second is already known (the first learned it). 99999 is outside the placeholder rule (TODO(m3-standin-spell-table)).
        Assert.Equal(ActionOutcomeKind.ServerError, first.Outcome!.Value.Kind);
        Assert.Equal("unknown-spell: not in the spell table: 99999", first.Outcome.Value.Detail);
        var result = first.Result!;
        Assert.Equal(
            [SpellOutcomeKind.Learned, SpellOutcomeKind.Learned, SpellOutcomeKind.Known, SpellOutcomeKind.Unknown],
            result.Spells.Select(s => s.Outcome));
        Assert.Equal((2, 1, 1), (result.Learned, result.Known, result.Unknown));

        var second = rig.Done(rig.Session.Tools.LearnSpells([2, 3], ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);
        Assert.True(second.Result!.AllAlreadyKnown);
        Assert.Equal((0, 2, 0), (second.Result.Learned, second.Result.Known, second.Result.Unknown));
    }

    [RequiresDatsFact]
    public void With_the_dats_the_spell_table_decides_which_ids_exist_and_names_them()
    {
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, SpellTable = SpellTableData.Load(fixture.Dats) };
        using var rig = new Rig(visit);
        var learn = rig.Done(rig.Session.Tools.LearnSpells([2, 3, 27, 4400], ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, learn.Outcome!.Value.Kind);
        Assert.Equal((4, 0, 0), (learn.Result!.Learned, learn.Result.Known, learn.Result.Unknown));
        Assert.All(learn.Result.Spells, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));

        var again = rig.Done(rig.Session.Tools.LearnSpells([2, 4400, 7_000_000], ActionSource.Host));
        Assert.Equal((0, 2, 1), (again.Result!.Learned, again.Result.Known, again.Result.Unknown));
        Assert.Equal(new uint[] { 7_000_000 }, again.Result.UnknownIds);
    }

    private static StandInVisit ItemVisit() => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Items = new Dictionary<uint, StandInItemKind>
        {
            [273] = new("Pyreal", 25_000),
            [100] = new("Sword", null),
            [200] = new("Portal Stone", null, Creatable: false),
        },
    };

    [Fact]
    public void An_item_is_created_with_a_fresh_guid_and_the_stack_clamped_as_the_mod_does()
    {
        using var rig = new Rig(ItemVisit());
        var five = rig.Done(rig.Session.Tools.SpawnItem(273, 5, ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, five.Outcome!.Value.Kind);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(five));
        Assert.Equal(new ItemResult(five.Result!.Guid, 273, "Pyreal", 5, 5, 25_000, false), five.Result);
        Assert.True(five.Result.Guid >= 0x80000000);

        var big = rig.Done(rig.Session.Tools.SpawnItem(273, 60_000, ActionSource.Host));
        Assert.Equal((25_000, 60_000, true), (big.Result!.Stack, big.Result.Requested!.Value, big.Result.Clamped));
        Assert.NotEqual(five.Result.Guid, big.Result.Guid);

        var none = rig.Done(rig.Session.Tools.SpawnItem(273, null, ActionSource.Host));
        Assert.Equal((1, (int?)null, false), (none.Result!.Stack, none.Result.Requested, none.Result.Clamped));

        // An item that does not stack: a stack asked for is ignored, one is made, and that is reported as clamped; a stack of 1 is not.
        var sword = rig.Done(rig.Session.Tools.SpawnItem(100, 3, ActionSource.Host));
        Assert.Equal((1, 3, (int?)null, true), (sword.Result!.Stack, sword.Result.Requested!.Value, sword.Result.MaxStack, sword.Result.Clamped));
        var oneSword = rig.Done(rig.Session.Tools.SpawnItem(100, 1, ActionSource.Host));
        Assert.False(oneSword.Result!.Clamped);
        Assert.Equal(5, new[] { five, big, none, sword, oneSword }.Select(h => h.Result!.Guid).Distinct().Count());
    }

}
