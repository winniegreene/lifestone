using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.State;

/// <summary>
/// M4 1.2 s1: the <see cref="AttackSequence"/> over the live order of cache/m4/1-2/capture.md (C1 melee, C3 missile): ACE walks the player by itself, the swing is the player's own F74C with an attack
/// forward command (the MoveToObject F74C 4 ms after the send is not one), 0x01A7 {0} ends one swing, 0x01B8 starts the next repeat, 0x01A7 {0x0036} ends the sequence.
/// </summary>
public sealed class AttackSequenceReducerTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Reedshark = 0x80000419;
    private const uint Arrows = 0x80000422;
    private const ushort SlashMed = 0x5C;
    private const ushort Reload = 0x16;
    private const uint AmmoSlot = 0x00800000;
    private const uint Over = 0x0036;

    private sealed class Rig
    {
        private uint _event;
        private byte _private;
        private byte _stack;
        private ushort _movement = 1;

        public Rig(int mode = CombatModes.Melee)
        {
            Harness = new ReducerHarness(track: false);
            Harness.EnterWorld();
            Harness.Bus.Subscribe((in AttackChanged e) => Changes.Add(e));
            _ = Harness.Reducer.Attack; // the session reads it (GameSession.Attack) before the first swing
            Mode(mode);
        }

        public ReducerHarness Harness { get; }

        public List<AttackChanged> Changes { get; } = [];

        public AttackSequence Seq => Harness.Reducer.Attack;

        public void Mode(int mode) => Send(AceWireWriters.PrivateUpdatePropertyInt(++_private, CombatModes.Property, mode));

        public void Send(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Harness.Context, message, Harness.Reducer).Outcome);

        public WorldObject Creature(uint guid, string name, float x = 14f, uint flags = 0x10)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, x), name: name);
            Harness.Reducer.OnCreateObject(Harness.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = flags } }, isUpdate: false);
            Assert.True(Harness.State.Objects.TryGet(guid, out var o));
            return o;
        }

        /// <summary>The arrows, created in the pack and wielded at the ammo slot (C2: 02DA, 02CE, 0023).</summary>
        public void WieldArrows(ushort stack)
        {
            var basis = ReducerHarness.Create(Arrows, ReducerHarness.Stamps(), null, name: "Arrow");
            Harness.Reducer.OnCreateObject(Harness.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = 300, StackSize = stack, Header = WeenieHeaderFlags.StackSize } }, isUpdate: false);
            Send(AceM3Writers.WieldObject(Player, ++_event, Arrows, (int)AmmoSlot));
        }

        public void Stack(uint size) => Send(AceM3Writers.SetStackSize(++_stack, Arrows, size, 0));

        /// <summary>The player's own F74C: an interpreted motion with <paramref name="forward"/>, sticking to <paramref name="sticky"/> when given.</summary>
        public void OwnMotion(ushort forward, uint sticky = 0) => Motion(Player, new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = 0x3C,
            Forward = forward,
            MotionFlags = sticky == 0 ? MotionFlags.None : MotionFlags.StickToObject,
            StickyGuid = sticky == 0 ? null : sticky,
        });

        /// <summary>The MoveToObject F74C ACE sends 4 ms after the 0x0008 (capture.md C1), or the aim turn of a missile send.</summary>
        public void MoveToMotion(MovementType type) => Motion(Player, new ServerMotionState { MovementType = type, Stance = 0x3C });

        public void Motion(uint guid, ServerMotionState motion) => Harness.Reducer.OnUpdateMotion(Harness.Context, new UpdateMotionMsg(guid, 1, ++_movement, 1, false, motion));

        public void Done(uint error) => Harness.Reducer.OnAttackDone(Harness.Context, Header(GameEventTypes.AttackDone), new AttackDoneEvent(error));

        public void Commence() => Harness.Reducer.OnCommenceAttack(Harness.Context, Header(GameEventTypes.CombatCommenceAttack), default);

        public void Hit(string defender) => Send(AceM3Writers.AttackerNotification(Player, ++_event, defender, 1, 0.1, 12, false, 0));

        public void Evade(string defender) => Send(AceM3Writers.EvasionAttackerNotification(Player, ++_event, defender));

        public void Kill() => Send(AceM3Writers.KillerNotification(Player, ++_event, "You have killed Adult Reedshark!"));

        private GameEventHeader Header(uint type) => new(Player, ++_event, type);
    }

    [Fact]
    public void Idle_until_something_starts_it()
    {
        var r = new Rig();

        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);
        Assert.Equal(0, r.Seq.Swings);
        Assert.Equal(0u, r.Seq.Target);
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void One_melee_send_gives_five_swings_and_the_kill_ends_it_with_0x0036()
    {
        var r = new Rig();
        r.Creature(Reedshark, "Adult Reedshark");

        r.MoveToMotion(MovementType.MoveToObject); // 4 ms after the send: not a swing
        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);

        for (var swing = 1; swing <= 5; swing++)
        {
            if (swing > 1)
            {
                r.Commence(); // 01B8 before every repeat; the first swing has none
                Assert.Equal(AttackSequenceState.Started, r.Seq.State);
                Assert.Equal(swing - 1, r.Seq.Swings);
            }

            r.OwnMotion(SlashMed, Reedshark);
            Assert.Equal(AttackSequenceState.Started, r.Seq.State);
            Assert.Equal(swing, r.Seq.Swings);
            Assert.Equal(Reedshark, r.Seq.Target);
            if (swing < 5)
            {
                r.Hit("Adult Reedshark"); // 01C0 + 01B1 about 600 ms after the swing
                r.Done(0);
                Assert.Equal(AttackSequenceState.SwingOver, r.Seq.State);
                Assert.Equal(0u, r.Seq.LastWeenieError);
            }
        }

        r.Kill(); // 01AD, no 01B1
        r.Done(Over);

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(5, r.Seq.Swings);
        Assert.Equal(0, r.Seq.Shots);
        Assert.Equal(Over, r.Seq.LastWeenieError);
        Assert.Equal(Reedshark, r.Seq.Target);
        Assert.Equal("Adult Reedshark", r.Seq.TargetName);
        Assert.Equal(1, r.Seq.Number);
        Assert.All(r.Changes, c => Assert.Equal(1, c.Number));
        Assert.Equal(AttackSequenceState.Done, r.Changes[^1].State);
        Assert.Contains(r.Changes, c => c.State == AttackSequenceState.SwingOver && c.Swings == 2);
    }

    [Fact]
    public void A_refusal_is_a_0x0036_with_no_start()
    {
        var r = new Rig();

        r.Done(Over);

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(Over, r.Seq.LastWeenieError);
        Assert.Equal(0, r.Seq.Swings);
        Assert.Equal(0, r.Seq.Number);
        Assert.Single(r.Changes);
    }

    [Fact]
    public void A_cancel_lets_the_swing_finish_and_then_ends_with_0x0036()
    {
        var r = new Rig();
        r.Creature(Reedshark, "Adult Reedshark");
        r.OwnMotion(SlashMed, Reedshark);
        Assert.True(r.Seq.Active); // 01B7 sent here

        r.Done(Over);

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(1, r.Seq.Swings);
        Assert.False(r.Seq.Active);
    }

    [Fact]
    public void A_new_sequence_resets_the_counts_the_target_and_the_error()
    {
        var r = new Rig();
        r.Creature(Reedshark, "Adult Reedshark");
        r.OwnMotion(SlashMed, Reedshark);
        r.OwnMotion(SlashMed, Reedshark);
        r.Done(Over);

        r.OwnMotion(SlashMed, 0x80000500);

        Assert.Equal(AttackSequenceState.Started, r.Seq.State);
        Assert.Equal(1, r.Seq.Swings);
        Assert.Equal(0x80000500u, r.Seq.Target);
        Assert.Equal(0u, r.Seq.LastWeenieError);
        Assert.Equal(2, r.Seq.Number);
    }

    [Fact]
    public void A_missile_send_gives_five_launches_with_the_hit_before_the_01A7_and_one_evade()
    {
        var r = new Rig(CombatModes.Missile);
        r.Creature(0x8000041B, "Adult Reedshark");
        r.WieldArrows(100);
        r.MoveToMotion(MovementType.TurnToObject); // the aim turns: not a launch
        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);

        uint stack = 100;
        for (var shot = 1; shot <= 5; shot++)
        {
            if (shot > 1)
            {
                r.Commence();
            }

            r.Stack(--stack); // the launch tick: 02CE, 0197, then the arrow's create and launch
            Assert.Equal(AttackSequenceState.Started, r.Seq.State);
            Assert.Equal(shot, r.Seq.Shots);
            if (shot == 4)
            {
                r.Evade("Adult Reedshark");
            }
            else if (shot < 5)
            {
                r.Hit("Adult Reedshark"); // at 4 m the hit comes before the 01A7
            }

            if (shot < 5)
            {
                r.OwnMotion(Reload);
                r.Done(0);
                Assert.Equal(AttackSequenceState.SwingOver, r.Seq.State);
            }
        }

        r.Kill();
        r.Done(Over);

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(5, r.Seq.Shots);
        Assert.Equal(0, r.Seq.Swings);
        Assert.Equal(Over, r.Seq.LastWeenieError);
        Assert.Equal(0x8000041Bu, r.Seq.Target); // no sticky guid on a launch: the nearest object of the defender's name
        Assert.Equal("Adult Reedshark", r.Seq.TargetName);
        Assert.Equal(95u, HeldAmmo.Of(r.Harness.State)!.Value.Stack);
    }

    [Fact]
    public void The_final_01A7_after_a_teleport_leaves_the_state_done_not_stuck()
    {
        var r = new Rig(CombatModes.Missile);
        r.Creature(0x8000041B, "Adult Reedshark");
        r.WieldArrows(50);
        r.Stack(49);
        r.Done(0);
        r.Commence();
        r.Stack(48);

        r.Harness.Reducer.OnPlayerTeleport(r.Harness.Context, new PlayerTeleportMsg(2)); // place poi, sent after the kill
        Assert.True(r.Seq.Active);
        r.Done(Over); // about a second after the teleport

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(2, r.Seq.Shots);
        Assert.Equal(Over, r.Seq.LastWeenieError);
    }

    [Fact]
    public void Only_the_players_attack_motions_start_a_sequence()
    {
        var r = new Rig();
        r.Creature(Reedshark, "Adult Reedshark");

        r.OwnMotion(Reload); // a substate, not a swing
        r.OwnMotion(0x0005); // WalkForward
        r.Motion(Reedshark, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = SlashMed }); // the shark's bite
        r.Done(0); // a swing over with nothing started

        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);
        // The motions changed nothing; only the 0x01A7 is published (every 0x01A7 is, changed or not: TODO(m4-attack-done-event) closed), and it shows the sequence still Idle.
        var notice = Assert.Single(r.Changes);
        Assert.Equal(AttackSequenceState.Idle, notice.State);
        Assert.Equal(0, notice.Number);
        Assert.True(AttackMotions.IsMeleeSwing(0x5C));
        Assert.True(AttackMotions.IsMeleeSwing(0x18F));
        Assert.False(AttackMotions.IsMeleeSwing(0x61));
        Assert.True(AttackMotions.IsMissileLaunch(0x61));
        Assert.False(AttackMotions.IsMeleeSwing(Reload));
    }

    [Fact]
    public void A_hit_outside_a_sequence_is_not_the_targets_and_stack_falls_count_only_for_wielded_ammo_in_missile_mode()
    {
        var r = new Rig(CombatModes.Melee);
        r.Creature(Reedshark, "Adult Reedshark");
        r.WieldArrows(100);

        r.Hit("Adult Reedshark"); // a spell's damage line, say
        r.Stack(99); // melee mode: not a launch
        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);
        Assert.Equal(0u, r.Seq.Target);

        r.Mode(CombatModes.Missile);
        r.Stack(99); // not a fall
        Assert.Equal(AttackSequenceState.Idle, r.Seq.State);
        r.Stack(98);
        Assert.Equal(1, r.Seq.Shots);
    }
}
