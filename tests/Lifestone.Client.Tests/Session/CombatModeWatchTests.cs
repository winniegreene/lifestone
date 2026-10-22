using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="CombatModeWatch"/> (M4 3.1 g): the moments the combat mode and the wielded weapon disagree are counted once per change, whoever caused them (our send, ACE's revert, a key), a consistent run counts zero, and the
/// field reads <c>mode_mismatch=&lt;n&gt;</c>. Everything goes through the real reducer, so the events are the ones the session publishes.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class CombatModeWatchTests
{
    private const uint Player = ReducerHarness.PlayerGuid;

    [Fact]
    public void A_melee_mode_with_a_wand_is_counted_once_and_named_with_the_clock()
    {
        var r = new Rig(WieldedSetups.Wand);
        Assert.Equal(0, r.Watch.Violations);
        Assert.Null(r.Watch.FirstViolation);
        Assert.Equal("mode_mismatch=0", r.Watch.Field);

        r.H.Time.Advance(TimeSpan.FromSeconds(12.3));
        r.Mode(CombatModes.Melee);

        Assert.Equal(1, r.Watch.Violations);
        Assert.Equal("mode_mismatch=1", r.Watch.Field);
        Assert.NotNull(r.Watch.FirstViolation);
        Assert.Contains("melee", r.Watch.FirstViolation, StringComparison.Ordinal);
        Assert.Contains("Wand of Flame", r.Watch.FirstViolation, StringComparison.Ordinal);
        Assert.StartsWith("t=12.3s: ", r.Watch.FirstViolation, StringComparison.Ordinal);

        // The same disagreement seen again is not a new one: the same mode again, a whole description that says the same.
        r.Mode(CombatModes.Melee);
        r.H.Reducer.OnPlayerDescription(
            r.H.Context,
            new GameEventHeader(Player, 9, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg
            {
                Equipped = [new EquippedDesc(WieldedSetups.Wand.Guid, (uint)EquipMask.Held, 0)],
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(CombatModes.Property, CombatModes.Melee),
            });
        Assert.Equal(1, r.Watch.Violations);
    }

    [Fact]
    public void ACE_s_revert_to_peace_ends_the_disagreement_and_the_next_one_counts_again()
    {
        var r = new Rig(WieldedSetups.Orb);
        r.Mode(CombatModes.Melee); // the client drew melee; ACE's revert follows
        r.Mode(CombatModes.NonCombat);
        Assert.Equal(1, r.Watch.Violations);

        r.Mode(CombatModes.Melee); // a key press again
        Assert.Equal(2, r.Watch.Violations);
        Assert.Equal("mode_mismatch=2", r.Watch.Field);
        Assert.Contains("Crystal Orb", r.Watch.FirstViolation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_magic_mode_without_a_caster_and_a_missile_mode_without_a_launcher_are_counted()
    {
        var r = new Rig(WieldedSetups.Mace);
        r.Mode(CombatModes.Magic);
        Assert.Equal(1, r.Watch.Violations);
        Assert.Contains("magic", r.Watch.FirstViolation, StringComparison.Ordinal);
        Assert.Contains("Mace", r.Watch.FirstViolation, StringComparison.Ordinal);

        r.Mode(CombatModes.Missile); // from magic to missile, still the mace: another disagreement
        Assert.Equal(2, r.Watch.Violations);

        var bare = new Rig();
        bare.Mode(CombatModes.Missile);
        Assert.Equal(1, bare.Watch.Violations);
        Assert.Contains("nothing", bare.Watch.FirstViolation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wield_that_makes_the_mode_wrong_is_counted_and_unwielding_ends_it()
    {
        var r = new Rig();
        r.Mode(CombatModes.Melee); // fists: fine
        Assert.Equal(0, r.Watch.Violations);

        r.Wield(WieldedSetups.Wand); // a wand into the hand while in melee mode: the weapon changed, not the mode
        Assert.Equal(1, r.Watch.Violations);
        Assert.Contains("Wand of Flame", r.Watch.FirstViolation, StringComparison.Ordinal);

        r.Unwield(WieldedSetups.Wand);
        Assert.Equal(1, r.Watch.Violations);
        r.Wield(WieldedSetups.Wand); // and back: a new disagreement
        Assert.Equal(2, r.Watch.Violations);
    }

    [Fact]
    public void A_consistent_run_counts_zero()
    {
        var r = new Rig();
        r.Mode(CombatModes.Melee); // fists
        r.Mode(CombatModes.NonCombat);
        r.Wield(WieldedSetups.Mace);
        r.Mode(CombatModes.Melee);
        r.Mode(CombatModes.NonCombat);
        r.Unwield(WieldedSetups.Mace);
        r.Wield(WieldedSetups.Wand);
        r.Mode(CombatModes.Magic);
        r.Mode(CombatModes.NonCombat);
        r.Unwield(WieldedSetups.Wand);
        r.Wield(WieldedSetups.Bow);
        r.Wield(WieldedSetups.Arrows);
        r.Mode(CombatModes.Missile);
        r.Mode(CombatModes.NonCombat);

        Assert.Equal(0, r.Watch.Violations);
        Assert.Null(r.Watch.FirstViolation);
        Assert.Equal("mode_mismatch=0", r.Watch.Field);
        Assert.Equal(0, r.Watch.Unchecked);
    }

    [Fact]
    public void A_disagreement_already_there_at_the_start_counts_once_and_a_weapon_the_table_lacks_is_not_one()
    {
        var r = new Rig(false, WieldedSetups.Wand);
        r.Mode(CombatModes.Melee); // the watch is not running yet: the disagreement is there before it
        Assert.Equal(0, r.Watch.Violations);
        r.Watch.Start(new View(r.H));
        Assert.Equal(1, r.Watch.Violations);
        r.Watch.Start(new View(r.H)); // started again on the same state: the same disagreement is not counted twice
        Assert.Equal(1, r.Watch.Violations);

        var unknown = new Rig(WieldedSetups.UnknownMace);
        unknown.Mode(CombatModes.Melee); // the mace's kind is unknown: nothing to judge, and nothing guessed
        Assert.Equal(0, unknown.Watch.Violations);
        Assert.Equal(1, unknown.Watch.Unchecked);
        WieldedSetups.Create(unknown.H, WieldedSetups.UnknownMace with { Present = true }); // its CreateObject arrives: melee with a mace fits
        Assert.Equal(0, unknown.Watch.Violations);
    }

    [Fact]
    public void A_stopped_watch_counts_nothing_and_Reset_zeroes_the_counts()
    {
        var r = new Rig(WieldedSetups.Wand);
        r.Mode(CombatModes.Melee);
        Assert.Equal(1, r.Watch.Violations);
        Assert.True(r.Watch.Running);

        r.Watch.Dispose();
        Assert.False(r.Watch.Running);
        r.Mode(CombatModes.NonCombat);
        r.Mode(CombatModes.Melee);
        Assert.Equal(1, r.Watch.Violations); // not subscribed: the counts stay readable and do not move

        r.Watch.Reset(); // not running: nothing to judge
        Assert.Equal(0, r.Watch.Violations);
        Assert.Null(r.Watch.FirstViolation);
        Assert.Equal("mode_mismatch=0", r.Watch.Field);
    }

    [Fact]
    public void Judging_a_state_allocates_nothing()
    {
        var r = new Rig(WieldedSetups.Wand);
        r.Mode(CombatModes.Melee);
        Assert.Equal(1, r.Watch.Violations);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                r.Watch.Judge(); // the same disagreement again: no text, no count
            }
        });

        r.Mode(CombatModes.Magic);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                r.Watch.Judge(); // a consistent state
            }
        });

        Assert.Equal(1, r.Watch.Violations);
    }

    /// <summary>A world with the pieces wielded and a watch started on it; the sequences of the server's messages count per rig.</summary>
    private sealed class Rig
    {
        private uint _event;
        private byte _private;

        public Rig(params Piece[] wielded)
            : this(true, wielded)
        {
        }

        public Rig(bool start, params Piece[] wielded)
        {
            H = WieldedSetups.World(wielded);
            Watch = new CombatModeWatch();
            if (start)
            {
                Watch.Start(new View(H));
            }
        }

        public ReducerHarness H { get; }

        public CombatModeWatch Watch { get; }

        public void Send(byte[] message) =>
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(H.Context, message, H.Reducer).Outcome);

        public void Mode(int mode) => Send(AceWireWriters.PrivateUpdatePropertyInt(++_private, CombatModes.Property, mode));

        public void Wield(Piece piece)
        {
            WieldedSetups.Create(H, piece);
            Send(AceM3Writers.WieldObject(Player, ++_event, piece.Guid, (int)piece.Slot));
        }

        public void Unwield(Piece piece) => Send(AceM3Writers.PutObjInContainer(Player, ++_event, piece.Guid, Player, 0, 0));
    }

    private sealed class View(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => null!;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog { get; } = new();

        public LinkStats Link => default;
    }
}
