using System.Globalization;
using System.Numerics;
using System.Text;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// <see cref="MotionInterpreter"/> against ACE's MotionInterp (<see cref="AceMotionInterpOracle"/>): 500 seeded random
/// scripts give the same part-array calls, states, pending motions and speeds, and each documented difference between
/// ACE and retail (docs/avatars.md §5.4) is shown on its own.
/// </summary>
public sealed class MotionInterpreterOracleTests
{
    private const uint Wave = 0x13000087;
    private const uint Crouch = 0x41000012;
    private const uint Sitting = 0x41000013;
    private const uint Sleeping = 0x41000014;
    private const uint Fallen = 0x40000008;
    private const uint Purple01 = 0x1000012B;
    private const uint Sanctuary = 0x10000057;
    private const uint AiTelegraphCast = 0x1000019B;

    private static readonly uint[] Styles = [MotionCommands.NonCombat, MotionCommands.HandCombat, MotionCommands.Magic];
    private static readonly uint[] ServerForwards = [MotionCommands.Ready, MotionCommands.WalkForward, MotionCommands.RunForward, Sitting, MotionCommands.Falling, MotionCommands.Dead];
    private static readonly float[] ServerSpeeds = [1f, 0.5f, 1.75f, -0.65f];
    private static readonly uint[] Actions = [Wave, MotionCommands.MagicPowerUp01, Purple01];
    private static readonly uint[] RawCommands =
    [
        MotionCommands.WalkForward, MotionCommands.WalkBackwards, MotionCommands.RunForward, MotionCommands.SideStepRight,
        MotionCommands.SideStepLeft, MotionCommands.TurnRight, MotionCommands.TurnLeft, MotionCommands.Ready, Sitting, Crouch,
        MotionCommands.NonCombat, MotionCommands.HandCombat, MotionCommands.Magic, Wave, MotionCommands.MagicPowerUp01, Purple01,
    ];

    private static readonly float?[] RunRates = [null, 1f, 1.5f, 2.5f, 3f];

    public static TheoryData<int> Batches => new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    [Theory]
    [MemberData(nameof(Batches))]
    public void Random_server_driven_creatures_agree_with_ACE(int batch)
    {
        for (var script = 0; script < 25; script++)
        {
            RunRemoteScript(seed: (batch * 1000) + script);
        }
    }

    [Theory]
    [MemberData(nameof(Batches))]
    public void Random_player_input_agrees_with_ACE(int batch)
    {
        for (var script = 0; script < 25; script++)
        {
            RunPlayerScript(seed: 50_000 + (batch * 1000) + script);
        }
    }

    [Fact]
    public void ACE_reads_weenie_slot_5_as_IsCreature_and_plays_the_raw_state_of_any_autonomous_creature()
    {
        // A remote creature whose last movement was autonomous (a player character echoed by the server).
        var pair = new Pair(FakeWeenie.RemoteCreature(), autonomous: true);
        var state = MotionRig.State(forward: MotionCommands.WalkForward);

        pair.Ours.MoveToInterpretedState(state);
        pair.Ace.move_to_interpreted_state(ToAce(state));

        Assert.Contains(pair.Parts.Calls, c => c.Motion == MotionCommands.WalkForward); // retail: the server's state
        Assert.DoesNotContain(pair.Body.Calls, c => c.Motion == MotionCommands.WalkForward); // ACE: the raw state (Ready)
    }

    [Fact]
    public void ACE_plays_the_players_autonomous_actions_and_skips_a_non_creatures_server_actions()
    {
        var player = new Pair(FakeWeenie.Player(), autonomous: true);
        var state = MotionRig.State();
        state.AddAction(Wave, 1f, 1, true);
        player.Ours.MoveToInterpretedState(state);
        player.Ace.move_to_interpreted_state(ToAce(state));
        Assert.DoesNotContain(player.Parts.Calls, c => c.Motion == Wave);
        Assert.Contains(player.Body.Calls, c => c.Motion == Wave);

        var door = new Pair(new FakeWeenie { IsCreature = false }, autonomous: false);
        var server = MotionRig.State();
        server.AddAction(Wave, 1f, 1, false);
        door.Ours.MoveToInterpretedState(server);
        door.Ace.move_to_interpreted_state(ToAce(server));
        Assert.Contains(door.Parts.Calls, c => c.Motion == Wave);
        Assert.DoesNotContain(door.Body.Calls, c => c.Motion == Wave);
    }

    [Theory]
    [InlineData(MotionCommands.Falling, false, true)] // ACE blocks Falling; retail blocks Fallen
    [InlineData(Fallen, true, false)]
    [InlineData(0x10000128u, false, true)] // TripleThrustLow: ACE keeps the 2013 range
    [InlineData(Purple01, true, true)] // MagicPowerUp01Purple: in both ranges
    [InlineData(0x10000132u, true, false)] // MagicPowerUp08Purple: 11.6096's range only
    [InlineData(0x10000134u, true, false)]
    [InlineData(Sanctuary, true, false)] // new in 11.6096
    [InlineData(AiTelegraphCast, true, false)]
    [InlineData(Sitting, true, true)]
    [InlineData(MotionCommands.MagicPowerUp01, true, true)]
    public void ACE_has_the_2013_jump_gating_set_with_Falling_for_Fallen(uint substate, bool retailBlocks, bool aceBlocks)
    {
        Assert.Equal(retailBlocks, MotionInterpreter.MotionAllowsJump(substate) != WeenieError.None);
        Assert.Equal(aceBlocks, AceMotionInterpOracle.AceMotionAllowsJump(substate) != WeenieError.None);
    }

    [Fact]
    public void ACE_ignores_the_run_key_given_to_DoMotion()
    {
        var pair = new Pair(FakeWeenie.Player(runRate: 2f), autonomous: true);
        var ours = new MovementParameters { HoldKeyToApply = HoldKey.Run };
        var ace = new AceMotionInterpOracle.Params { HoldKeyToApply = HoldKey.Run };

        pair.Ours.DoMotion(MotionCommands.WalkForward, ours);
        pair.Ace.DoMotion(MotionCommands.WalkForward, ace);

        Assert.Equal(HoldKey.Run, pair.Ours.Raw.CurrentHoldKey);
        Assert.Equal(HoldKey.None, pair.Ace.RawState.CurrentHoldKey);
    }

    [Fact]
    public void ACE_drops_the_jump_ban_context_and_autonomous_bit_of_DoMotion_parameters()
    {
        var pair = new Pair(FakeWeenie.Player(), autonomous: true);
        pair.Ours.DoMotion(MotionCommands.WalkForward, new MovementParameters { ContextId = 9 }.WithFlag(MovementParameterFlags.DisableJumpDuringLink, true));
        pair.Ace.DoMotion(MotionCommands.WalkForward, new AceMotionInterpOracle.Params { ContextID = 9, DisableJumpDuringLink = true });

        Assert.Equal(new PendingMotion(9, MotionCommands.WalkForward, WeenieError.YouCantJumpFromThisPosition), pair.Ours.PendingMotions.Last());
        Assert.Equal(new AceMotionInterpOracle.MotionNode(0, MotionCommands.WalkForward, WeenieError.None), pair.Ace.PendingMotions.Last!.Value);

        // An action keeps its stamp and autonomous bit in retail's interpreted state; ACE's copy loses both.
        pair.Ours.DoMotion(Wave, new MovementParameters { ActionStamp = 5 }.WithFlag(MovementParameterFlags.Autonomous, true));
        pair.Ace.DoMotion(Wave, new AceMotionInterpOracle.Params { ActionStamp = 5, Autonomous = true });
        Assert.Equal(new MotionActionNode(Wave, 1f, 5, true), Assert.Single(pair.Ours.Interpreted.Actions));
        Assert.Equal(new AceMotionInterpOracle.ActionNode(Wave, 1f, 0, false), Assert.Single(pair.Ace.InterpretedState.Actions));
    }

    [Fact]
    public void Both_refuse_a_jump_when_fully_constrained_or_in_the_air()
    {
        var pair = new Pair(FakeWeenie.Player(), autonomous: true);
        pair.Host.IsFullyConstrained = pair.Body.FullyConstrained = true;
        Assert.Equal(WeenieError.GeneralMovementFailure, pair.Ours.JumpIsAllowed(1f, out _));
        Assert.Equal(WeenieError.GeneralMovementFailure, pair.Ace.jump_is_allowed(1f));

        pair.Host.IsFullyConstrained = pair.Body.FullyConstrained = false;
        pair.Host.OnWalkable = pair.Body.OnWalkable = false;
        Assert.Equal(WeenieError.YouCantJumpWhileInTheAir, pair.Ours.JumpIsAllowed(1f, out _));
        Assert.Equal(WeenieError.YouCantJumpWhileInTheAir, pair.Ace.jump_is_allowed(1f));
    }

    [Fact]
    public void ACE_checks_the_oldest_pending_jump_error_only_with_two_pending()
    {
        var pair = new Pair(FakeWeenie.Player(), autonomous: true);
        pair.Ours.HandleExitWorld();
        pair.Ace.PendingMotions.Clear();
        pair.Ours.DoInterpretedMotion(MotionCommands.Ready, new MovementParameters().WithFlag(MovementParameterFlags.DisableJumpDuringLink, true));
        pair.Ace.DoInterpretedMotion(MotionCommands.Ready, new AceMotionInterpOracle.Params { DisableJumpDuringLink = true });

        Assert.Equal(WeenieError.YouCantJumpFromThisPosition, pair.Ours.JumpIsAllowed(0.5f, out _));
        Assert.Equal(WeenieError.None, pair.Ace.jump_is_allowed(0.5f));
    }

    [Fact]
    public void ACE_caps_the_state_velocity_of_an_object_without_a_weenie_at_its_own_run_rate()
    {
        var pair = new Pair(FakeWeenie.RemoteCreature(), autonomous: false);
        pair.Host.Weenie = null;
        pair.Body.HasWeenie = false;
        pair.Ours.MyRunRate = pair.Ace.MyRunRate = 2f;
        pair.Ours.Interpreted.ForwardCommand = pair.Ace.InterpretedState.ForwardCommand = MotionCommands.RunForward;
        pair.Ours.Interpreted.ForwardSpeed = pair.Ace.InterpretedState.ForwardSpeed = 2f;

        Assert.Equal(4f, pair.Ours.GetStateVelocity().Y, 4); // retail: rate 1 without a weenie
        Assert.Equal(8f, pair.Ace.get_state_velocity().Y, 4);
    }

    [Theory]
    [InlineData(MotionCommands.Falling, WeenieError.None, 0x48)] // ACE refuses a charge while Falling, retail while Fallen
    [InlineData(Sleeping, WeenieError.YouCantJumpFromThisPosition, 0)] // ACE stops short of Sleeping
    public void ACE_charges_a_jump_from_a_different_set(uint forward, WeenieError retail, int ace)
    {
        var pair = new Pair(FakeWeenie.Player(), autonomous: true);
        pair.Ours.Interpreted.ForwardCommand = pair.Ace.InterpretedState.ForwardCommand = forward;
        Assert.Equal(retail, pair.Ours.ChargeJump());
        Assert.Equal(ace, pair.Ace.charge_jump());
    }

    [Fact]
    public void The_movement_system_agrees_with_ACE_except_above_a_run_skill_of_800()
    {
        foreach (var load in new[] { 0f, 0.5f, 1f, 1.5f, 2f, 2.5f })
        {
            foreach (var skill in new[] { 0, 10, 150, 300, 799, 800 })
            {
                Assert.Equal(AceMotionInterpOracle.GetRunRate(load, skill, 1f), MovementSystem.GetRunRate(load, skill, 1f), 5);
                Assert.Equal(AceMotionInterpOracle.GetRunRate(load, skill, 1.2f), MovementSystem.GetRunRate(load, skill, 1.2f), 5);
            }

            foreach (var power in new[] { -1f, 0f, 0.25f, 0.5f, 1f, 2f })
            {
                Assert.Equal(AceMotionInterpOracle.GetJumpHeight(load, 200, power, 1f), MovementSystem.GetJumpHeight(load, 200, power, 1f), 5);
                Assert.Equal(AceMotionInterpOracle.JumpStaminaCost(power, load, false), MovementSystem.JumpStaminaCost(power, load, false));
                Assert.Equal(AceMotionInterpOracle.JumpStaminaCost(power, load, true), MovementSystem.JumpStaminaCost(power, load, true));
            }
        }

        // The decompile tests the Run skill for equality with 800; ACE caps from 800 up.
        Assert.Equal(4.5, AceMotionInterpOracle.GetRunRate(0f, 801, 1f), 5);
        Assert.Equal(((801.0 / 1001.0 * 11.0) + 4.0) / 4.0, MovementSystem.GetRunRate(0f, 801, 1f), 9);
    }

    private static void RunRemoteScript(int seed)
    {
        var rng = new Random(seed);
        var weenie = FakeWeenie.RemoteCreature();
        weenie.RunRate = RunRates[rng.Next(RunRates.Length)];
        var pair = new Pair(weenie, autonomous: false);
        if (rng.Next(4) == 0)
        {
            pair.Refuse(Sitting);
        }

        var stamp = rng.Next(0x8000);
        pair.Compare(seed, "default state");
        for (var step = 0; step < 40; step++)
        {
            var roll = rng.Next(100);
            string what;
            if (roll < 40)
            {
                var state = RandomServerState(rng, ref stamp, allowAutonomous: true);
                what = "server state " + Describe(state);
                pair.Ours.MoveToInterpretedState(state);
                pair.Ace.move_to_interpreted_state(ToAce(state));
            }
            else if (roll < 55)
            {
                what = pair.RandomGround(rng);
            }
            else if (roll < 60)
            {
                what = pair.ToggleGravity();
            }
            else if (roll < 65)
            {
                what = pair.ToggleCell();
            }
            else if (roll < 75)
            {
                what = pair.CompleteMotions(rng.Next(1, 4));
            }
            else if (roll < 80)
            {
                what = "stop completely";
                pair.Ours.StopCompletely();
                pair.Ace.StopCompletely();
            }
            else if (roll < 87)
            {
                var motion = ServerForwards[rng.Next(ServerForwards.Length)];
                var speed = ServerSpeeds[rng.Next(ServerSpeeds.Length)];
                what = $"interpreted 0x{motion:X8} @{speed}";
                pair.Ours.DoInterpretedMotion(motion, new MovementParameters { Speed = speed });
                pair.Ace.DoInterpretedMotion(motion, new AceMotionInterpOracle.Params { Speed = speed });
            }
            else if (roll < 92)
            {
                var motion = rng.Next(2) == 0 ? MotionCommands.SideStepRight : MotionCommands.WalkForward;
                what = $"stop interpreted 0x{motion:X8}";
                pair.Ours.StopInterpretedMotion(motion, new MovementParameters());
                pair.Ace.StopInterpretedMotion(motion, new AceMotionInterpOracle.Params());
            }
            else if (roll < 96)
            {
                var longJump = rng.Next(2) == 0;
                what = $"standing long jump {longJump}";
                pair.Ours.StandingLongJump = pair.Ace.StandingLongJump = longJump;
            }
            else
            {
                var rate = (float)(rng.Next(1, 30) / 10.0);
                what = $"my run rate {rate}";
                pair.Ours.MyRunRate = pair.Ace.MyRunRate = rate;
            }

            pair.Compare(seed, what);
        }
    }

    private static void RunPlayerScript(int seed)
    {
        var rng = new Random(seed);
        var weenie = FakeWeenie.Player(RunRates[rng.Next(RunRates.Length)]);
        var pair = new Pair(weenie, autonomous: true);
        if (rng.Next(4) == 0)
        {
            pair.Refuse(Crouch);
        }

        var stamp = rng.Next(0x8000);
        pair.Compare(seed, "default state");
        for (var step = 0; step < 40; step++)
        {
            var roll = rng.Next(100);
            string what;
            if (roll < 30)
            {
                var motion = RawCommands[rng.Next(RawCommands.Length)];
                var (ours, ace, text) = RandomParams(rng);
                what = $"DoMotion 0x{motion:X8} {text}";
                Assert.Equal(pair.Ace.DoMotion(motion, ace), pair.Ours.DoMotion(motion, ours));
            }
            else if (roll < 45)
            {
                var motion = RawCommands[rng.Next(RawCommands.Length)];
                var (ours, ace, text) = RandomParams(rng);
                what = $"StopMotion 0x{motion:X8} {text}";
                Assert.Equal(pair.Ace.StopMotion(motion, ace), pair.Ours.StopMotion(motion, ours));
            }
            else if (roll < 55)
            {
                var run = rng.Next(2) == 0;
                what = $"hold run {run}";
                pair.Ours.SetHoldRun(run, cancelMoveTo: false);
                pair.Ace.set_hold_run(run ? 1 : 0, false);
            }
            else if (roll < 65)
            {
                what = pair.RandomGround(rng);
            }
            else if (roll < 75)
            {
                what = pair.CompleteMotions(rng.Next(1, 4));
            }
            else if (roll < 80)
            {
                what = "stop completely";
                pair.Ours.StopCompletely();
                pair.Ace.StopCompletely();
            }
            else if (roll < 90)
            {
                var state = RandomServerState(rng, ref stamp, allowAutonomous: false);
                what = "server echo " + Describe(state);
                pair.Ours.MoveToInterpretedState(state);
                pair.Ace.move_to_interpreted_state(ToAce(state));
            }
            else if (roll < 95)
            {
                var rate = RunRates[rng.Next(RunRates.Length)];
                what = $"run rate {rate}";
                weenie.RunRate = pair.Body.RunRate = rate;
            }
            else
            {
                what = rng.Next(2) == 0 ? pair.ToggleGravity() : pair.ToggleCell();
            }

            pair.Compare(seed, what);
        }
    }

    private static (MovementParameters Ours, AceMotionInterpOracle.Params Ace, string Text) RandomParams(Random rng)
    {
        float[] speeds = [1f, 0.5f, 2f];
        var speed = speeds[rng.Next(speeds.Length)];
        var holdKey = (HoldKey)rng.Next(3);

        // ACE's SetHoldKey ignores the run key (shown separately), so a run key is given without SetHoldKey.
        var setHoldKey = holdKey != HoldKey.Run && rng.Next(4) != 0;
        var ours = new MovementParameters { Speed = speed, HoldKeyToApply = holdKey }.WithFlag(MovementParameterFlags.SetHoldKey, setHoldKey);
        var ace = new AceMotionInterpOracle.Params { Speed = speed, HoldKeyToApply = holdKey, SetHoldKey = setHoldKey };
        return (ours, ace, $"@{speed} hold {holdKey} set {setHoldKey}");
    }

    private static InterpretedMotionState RandomServerState(Random rng, ref int stamp, bool allowAutonomous)
    {
        var state = MotionRig.State(
            forward: ServerForwards[rng.Next(ServerForwards.Length)],
            forwardSpeed: ServerSpeeds[rng.Next(ServerSpeeds.Length)],
            sidestep: rng.Next(3) == 0 ? MotionCommands.SideStepRight : 0,
            sidestepSpeed: rng.Next(2) == 0 ? 1f : -1.248f,
            turn: rng.Next(3) == 0 ? MotionCommands.TurnRight : 0,
            turnSpeed: rng.Next(2) == 0 ? 1f : -1.5f,
            style: Styles[rng.Next(Styles.Length)]);
        for (var i = rng.Next(3); i > 0; i--)
        {
            stamp = rng.Next(10) == 0 ? stamp - rng.Next(1, 5) : stamp + rng.Next(1, 4);
            state.AddAction(Actions[rng.Next(Actions.Length)], rng.Next(2) == 0 ? 1f : 1.5f, (ushort)(stamp & 0x7FFF), allowAutonomous && rng.Next(2) == 0);
        }

        return state;
    }

    private static AceMotionInterpOracle.InterpMotion ToAce(InterpretedMotionState state)
    {
        var ace = new AceMotionInterpOracle.InterpMotion
        {
            CurrentStyle = state.CurrentStyle,
            ForwardCommand = state.ForwardCommand,
            ForwardSpeed = state.ForwardSpeed,
            SideStepCommand = state.SideStepCommand,
            SideStepSpeed = state.SideStepSpeed,
            TurnCommand = state.TurnCommand,
            TurnSpeed = state.TurnSpeed,
        };
        foreach (var a in state.Actions)
        {
            ace.Actions.Add(new AceMotionInterpOracle.ActionNode(a.Action, a.Speed, a.Stamp, a.Autonomous));
        }

        return ace;
    }

    private static string Describe(InterpretedMotionState s) => string.Create(
        CultureInfo.InvariantCulture,
        $"style 0x{s.CurrentStyle:X8} fwd 0x{s.ForwardCommand:X8}@{s.ForwardSpeed} side 0x{s.SideStepCommand:X8}@{s.SideStepSpeed} turn 0x{s.TurnCommand:X8}@{s.TurnSpeed} actions [{string.Join(", ", s.Actions.Select(a => $"0x{a.Action:X8}#{a.Stamp}{(a.Autonomous ? "a" : "")}"))}]");

    /// <summary>The interpreter and the ACE oracle side by side, with the same object.</summary>
    private sealed class Pair
    {
        private readonly List<string> _steps = [];

        public Pair(FakeWeenie weenie, bool autonomous)
        {
            (Ours, Host, Parts) = MotionRig.Create(weenie, enterDefaultState: false, autonomous);

            // The scripts reach negative run rates (a RunForward at a negative speed): compare retail's cap, which ACE
            // shares, not Lifestone's glitch fix (A_negative_run_rate_leaves_the_state_velocity_uncapped… pins that).
            Ours.RetailNegativeRunRateCap = true;
            Body = new AceMotionInterpOracle.Body
            {
                Autonomous = autonomous,
                IsCreature = weenie.IsCreature,
                RunRate = weenie.RunRate,
                JumpVelocity = weenie.JumpVelocity,
            };
            Ace = new AceMotionInterpOracle(Body) { MotionAllowsJump = MotionInterpreter.MotionAllowsJump };
            Body.Interp = Ace;
            Ours.EnterDefaultState();
            Ace.enter_default_state();
        }

        public MotionInterpreter Ours { get; }

        public FakeMotionHost Host { get; }

        public RecordingParts Parts { get; }

        public AceMotionInterpOracle.Body Body { get; }

        public AceMotionInterpOracle Ace { get; }

        public void Refuse(uint motion)
        {
            Parts.Refuse.Add(motion);
            Body.Refuse.Add(motion);
        }

        public string RandomGround(Random rng)
        {
            var contact = rng.Next(3) != 0;
            var walkable = contact && rng.Next(3) != 0;
            Host.HasContact = Body.Contact = contact;
            var was = Host.OnWalkable;
            Host.SetOnWalkable(walkable);
            Body.OnWalkable = walkable;
            if (was && !walkable)
            {
                Ace.LeaveGround();
            }
            else if (!was && walkable)
            {
                Ace.HitGround();
            }

            return $"ground contact {contact} walkable {walkable}";
        }

        public string ToggleGravity()
        {
            Host.HasGravity = Body.Gravity = !Host.HasGravity;
            return $"gravity {Host.HasGravity}";
        }

        public string ToggleCell()
        {
            Host.InCell = Body.InCell = !Host.InCell;
            return $"in cell {Host.InCell}";
        }

        public string CompleteMotions(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Ours.MotionDone(success: true);
                Ace.MotionDone(true);
            }

            return $"complete {count}";
        }

        public void Compare(int seed, string step)
        {
            _steps.Add(step);
            var problems = new List<string>();
            if (Parts.Calls.Count != Body.Calls.Count || Parts.Calls.Where((c, k) => !c.Matches(Body.Calls[k])).Any())
            {
                problems.Add($"calls\n  ours: {string.Join(", ", Parts.Calls)}\n  ace:  {string.Join(", ", Body.Calls)}");
            }

            var i = Ours.Interpreted;
            var a = Ace.InterpretedState;
            if (i.CurrentStyle != a.CurrentStyle || i.ForwardCommand != a.ForwardCommand || i.SideStepCommand != a.SideStepCommand || i.TurnCommand != a.TurnCommand
                || !Close(i.ForwardSpeed, a.ForwardSpeed) || !Close(i.SideStepSpeed, a.SideStepSpeed) || !Close(i.TurnSpeed, a.TurnSpeed)
                || !i.Actions.Select(x => (x.Action, x.Stamp, x.Autonomous)).SequenceEqual(a.Actions.Select(x => (x.Action, (ushort)x.Stamp, x.Autonomous))))
            {
                problems.Add("interpreted state");
            }

            var r = Ours.Raw;
            var ar = Ace.RawState;
            if (r.CurrentHoldKey != ar.CurrentHoldKey || r.CurrentStyle != ar.CurrentStyle || r.ForwardCommand != ar.ForwardCommand
                || r.ForwardHoldKey != ar.ForwardHoldKey || r.SideStepCommand != ar.SideStepCommand || r.SideStepHoldKey != ar.SideStepHoldKey
                || r.TurnCommand != ar.TurnCommand || r.TurnHoldKey != ar.TurnHoldKey
                || !Close(r.ForwardSpeed, ar.ForwardSpeed) || !Close(r.SideStepSpeed, ar.SideStepSpeed) || !Close(r.TurnSpeed, ar.TurnSpeed)
                || r.Actions.Count != ar.Actions.Count)
            {
                problems.Add("raw state");
            }

            if (!Ours.PendingMotions.Select(p => ((int)p.ContextId, p.Motion, p.JumpErrorCode)).SequenceEqual(Ace.PendingMotions.Select(p => (p.ContextID, p.Motion, p.JumpErrorCode))))
            {
                problems.Add($"pending\n  ours: {string.Join(", ", Ours.PendingMotions)}\n  ace:  {string.Join(", ", Ace.PendingMotions)}");
            }

            if (!Close(Ours.MyRunRate, Ace.MyRunRate) || Ours.StandingLongJump != Ace.StandingLongJump || Ours.ServerActionStamp != Ace.ServerActionStamp)
            {
                problems.Add("run rate, standing long jump or server action stamp");
            }

            if (!Close(Ours.GetStateVelocity(), Ace.get_state_velocity()) || !Close(Ours.GetMaxSpeed(), Ace.get_max_speed())
                || !Close(Ours.GetAdjustedMaxSpeed(), Ace.get_adjusted_max_speed()) || !Close(Host.Velocity, Body.Velocity))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"velocities or speeds: state {Ours.GetStateVelocity()} / {Ace.get_state_velocity()}, max {Ours.GetMaxSpeed()} / {Ace.get_max_speed()}, adjusted {Ours.GetAdjustedMaxSpeed()} / {Ace.get_adjusted_max_speed()}, body {Host.Velocity} / {Body.Velocity}; interpreted fwd 0x{i.ForwardCommand:X8}@{i.ForwardSpeed} side 0x{i.SideStepCommand:X8}@{i.SideStepSpeed}, my run rate {Ours.MyRunRate}"));
            }

            if (problems.Count > 0)
            {
                var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"seed {seed}, after:\n  ").AppendJoin("\n  ", _steps).Append("\n\n").AppendJoin("\n", problems);
                Assert.Fail(text.ToString());
            }

            Parts.Calls.Clear();
            Body.Calls.Clear();
        }

        private static bool Close(float x, float y) => MathF.Abs(x - y) <= 1e-5f * MathF.Max(1f, MathF.Abs(x));

        // A non-positive run rate (RunForward at a negative speed) makes both cap a zero velocity to NaN, as retail does.
        private static bool Close(Vector3 x, Vector3 y) =>
            Vector3.Distance(x, y) <= 1e-4f || (float.IsNaN(x.X) && float.IsNaN(y.X) && float.IsNaN(x.Y) && float.IsNaN(y.Y));
    }
}
