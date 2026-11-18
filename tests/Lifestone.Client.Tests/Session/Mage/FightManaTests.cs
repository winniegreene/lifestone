using System.Globalization;
using System.Numerics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 1 F1 (DoD 3): the fight's mana through <see cref="ManaOrder"/> over the scripted caster (<see cref="ScriptedCaster"/>: the BSD mage's description, ACE's Mana Conversion cost from the model's seeded rolls, a dearer roll refused
/// 0x0401 with nothing charged, the transfer, the Mana Potion) with a tusker on the den's floor answered by the projectile hook: the bolt from its try floor with the refusals counted; under the floor the dance at the 0.25 s
/// spacing; low stamina then a potion; no potion then a rest in peace then the dance; the pre-emptive transfer; <c>--cast-mana-tries 0</c> as the ceiling wait; the waits and the kill rates on the fake clock; the clean
/// <c>--fight-minutes</c> end; <c>--fight-kills 0</c>; <c>--fight-max-level</c>; the level's re-choice after a buff change; the killed list bounded; the options. Nothing here needs the dats or a server.
/// </summary>
public sealed class FightManaTests
{
    internal const uint Den = 0x01050375;
    private const uint FloorCell = 0x01050303;
    private const uint TuskerSlave = 1628;
    internal const float PlayerX = 100;
    internal const float PlayerY = 20;

    /// <summary>
    /// A Mana Conversion seed (<see cref="ScriptedCaster"/>'s rolls) whose first three tries are refused 0x0401 and whose fourth lands, both for the bolt at 40 mana and for the dance at 2: the default seed lands both on the first
    /// try, which would leave the refusals and the 0.25 s spacing unchecked (M5 1 fix A, 1-R5). If the model's rolls change and these counts move, find another seed with <c>FightManaTests</c>' probe (loop the seeds, count the refusals) and say so.
    /// </summary>
    private const int RefusingSeed = 314;

    private const int RefusedBeforeLanding = 3;

    private static readonly uint[] BoltIds = [.. ArenaProfile.LineNamed("Flame Bolt")!.Spells.Select(s => s.Id)];

    internal sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>M6 16 E2b: a view over <paramref name="rig"/> of a session that draws bodies: a creature stands where <paramref name="live"/> says.</summary>
    internal sealed class DrawnView(WorldActionRig rig, LivePositions live) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;

        public LivePositions Live => live;
    }

    /// <summary>A walk that arrives after <see cref="TicksPerLeg"/> ticks.</summary>
    internal sealed class Walk : IFightWalk
    {
        private int _ticks;
        private bool _walking;

        public int TicksPerLeg { get; set; } = 4;

        public int Legs { get; private set; }

        public string? Failure => null;

        public Action<int>? OnLeg { get; set; }

        public bool HasClearLine(in CellFrame from, in CellFrame to) => true;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "fake leg " + (++Legs);
            (_walking, _ticks) = (true, 0);
            OnLeg?.Invoke(Legs);
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (!_walking)
            {
                return FightWalkStatus.Idle;
            }

            if (++_ticks < TicksPerLeg)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            return FightWalkStatus.Arrived;
        }

        public void Abandon() => _walking = false;

        public void Dispose()
        {
        }
    }

    /// <summary>The scripted caster, the den's floor and the tuskers: each bolt that is charged makes a projectile, then a damage line, or the kill (0x01AD and the deletion) at the tusker's last hit.</summary>
    internal sealed class Rig
    {
        private readonly Dictionary<uint, (string Name, int HitsToKill, int Hits)> _tuskers = [];
        private uint _nextProjectile = 0x80009000;
        private uint _nextTusker = 0x80001001;

        public Rig(uint mana, uint stamina, uint manaPotions = 0, FightOptions? fight = null, int manaSeed = ScriptedCaster.DefaultManaSeed, uint staminaPotions = 0, bool knowsRevitalize = true, bool knowsDrain = false, bool knowsStaminaDrain = false, uint lifeMagicRanks = 0, bool knowsDance = true, IReadOnlyList<(uint Wcid, uint Count)>? healthDrinks = null, uint manaTinctures = 0, IReadOnlyList<ProfileLine>? lines = null, IReadOnlyList<uint>? alsoKnown = null, WorldActionRig? world = null)
        {
            R = world ?? new WorldActionRig(); // M6 16 E3.1: over another rig's world (the autopilot's), which places the player itself
            Caster = new ScriptedCaster(R, mana, stamina, manaSeed, lines: lines, manaPotions: manaPotions, known: [.. alsoKnown ?? [], .. (knowsRevitalize ? KnownIds() : [.. KnownIds().Except(VitalsMeans.RevitalizeSelf)]).Except(knowsDance ? [] : VitalsMeans.StaminaToManaSelf), .. (knowsDrain ? ScriptedCaster.DrainLine.Spells.Concat(ScriptedCaster.HealLine.Spells).Select(s => s.Id) : []), .. (knowsStaminaDrain ? ScriptedCaster.StaminaDrainLine.Spells.Select(s => s.Id) : [])], staminaPotions: staminaPotions, withDrain: knowsDrain, withStaminaDrain: knowsStaminaDrain, lifeMagicRanks: lifeMagicRanks, healthDrinks: healthDrinks, manaTinctures: manaTinctures);
            foreach (var bolt in BoltIds)
            {
                Caster.Table[bolt] = Caster.Table[bolt] with { BaseRangeConstant = 30f, BaseRangeMod = 0f };
            }

            Caster.ProjectileHook = Land;
            Fight = fight ?? new FightOptions();
            View = new View(R);
            if (world is not null)
            {
                return;
            }

            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 2, 2, 1, cell: Den, x: PlayerX));
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public FightOptions Fight { get; set; }

        public BuffOptions Buff { get; set; } = new();

        public IGameSessionView View { get; }

        public double Now => R.H.R.Clock.Now;

        public RecordingLogSink Log => R.H.Log;

        /// <summary>Hits a tusker takes before it dies.</summary>
        public int HitsToKill { get; set; } = 2;

        /// <summary>M6 16 E3.0: the object description flags of the tuskers added from here on (0, as ever; <c>WorldObjectTable.AttackableFlag</c> for a test whose reader asks the threat board, which counts only what may be attacked).</summary>
        public uint TuskerFlags { get; set; }

        /// <summary>True: a killed tusker is followed at once by a new one 20 m away (an endless floor).</summary>
        public bool Respawn { get; set; }

        public int MaxKilledHeld { get; private set; }

        public GearKit Kit { get; } = GearKit.Of([GearItemOf(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand), .. GearKit.Armour.Select(a => GearItemOf(a.Wcid, a.Name, GearKind.Armour, a))]);

        /// <summary>The bolts the caster answered: (spell, time, refused for mana).</summary>
        public IEnumerable<(uint Spell, double At)> BoltAnswers => Caster.Answered.Where(a => IsBolt(a.Spell) && !a.Fizzled).Select(a => (a.Spell, a.At));

        public IEnumerable<(uint Spell, double At)> RevitalizeAnswers => Caster.Answered.Where(a => VitalsMeans.RevitalizeSelf.Contains(a.Spell)).Select(a => (a.Spell, a.At));

        public IEnumerable<(uint Spell, double At)> DanceAnswers => Caster.Answered.Where(a => VitalsMeans.StaminaToManaSelf.Contains(a.Spell)).Select(a => (a.Spell, a.At));

        public int BoltRefusals => BoltAnswers.Count() - Caster.Charged.Count(c => IsBolt(c.Spell));

        public int DanceRefusals => DanceAnswers.Count() - Caster.Charged.Count(c => VitalsMeans.StaminaToManaSelf.Contains(c.Spell));

        public static bool IsBolt(uint id) => BoltIds.Contains(id);

        /// <summary>The wcid <see cref="AddTusker"/> gives a new tusker (M6 7 AB: a ground of its own).</summary>
        public uint TuskerWcid { get; set; } = TuskerSlave;

        /// <summary>The cell a bolt starts from: the player's.</summary>
        public uint PlayerCell { get; set; } = Den;

        /// <summary>The cell <see cref="AddTusker"/> puts a new tusker in.</summary>
        public uint TuskerCell { get; set; } = FloorCell;

        /// <summary>The name <see cref="AddTusker"/> gives a new tusker (M6 16 E1b: a creature outside the ground's list).</summary>
        public string TuskerName { get; set; } = "Tusker Slave";

        public uint AddTusker(float y = 40)
        {
            var guid = _nextTusker++;
            _tuskers[guid] = (TuskerName, HitsToKill, 0);
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(TuskerCell, PlayerX, y, 30), name: TuskerName);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = TuskerWcid, ObjectDescriptionFlags = TuskerFlags } }, isUpdate: false);
            return guid;
        }

        /// <summary>The server moves a tusker to <paramref name="y"/> (once per tusker: the position stamp is fixed).</summary>
        public void MoveTusker(uint guid, float y) =>
            R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(guid, PositionFlags.Grounded, ReducerHarness.Position(TuskerCell, PlayerX, y, 30), null, null, 1, 50, 1, 1));

        public FightScenario Scenario(IFightWalk? walk = null) => new(Kit, Caster.Table, Buff, Fight, walk ?? new Walk(), Log, endSession: false, stages: FightStages.Mode);

        public ScenarioStatus Frame(FightScenario scenario, double dt)
        {
            R.Tick(dt);
            Caster.Pump();
            R.Tick();
            var status = scenario.Step(View, Now);
            MaxKilledHeld = Math.Max(MaxKilledHeld, scenario.KilledHeld);
            return status;
        }

        public ScenarioStatus Run(FightScenario scenario, double maxSeconds = 600, double dt = 0.25, Func<bool>? until = null)
        {
            var end = Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Now < end && until?.Invoke() != true)
            {
                status = Frame(scenario, dt);
            }

            return status;
        }

        public uint Mana => R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out _) ? m : 0;

        private static uint[] KnownIds() => [.. ArenaProfile.Lines.SelectMany(l => l.Spells).Select(s => s.Id).Distinct()];

        private void Land(uint spellId, uint target)
        {
            var id = _nextProjectile++;
            R.H.R.Reducer.OnCreateObject(
                R.H.R.Context,
                ReducerHarness.Create(id, ReducerHarness.Stamps(), ReducerHarness.Position(PlayerCell, PlayerX, PlayerY, 30), state: PhysicsState.Missile | PhysicsState.Gravity, velocity: new Vector3(0, 20, 0)),
                isUpdate: false);
            var t = _tuskers[target];
            t.Hits++;
            _tuskers[target] = t;
            if (t.Hits >= t.HitsToKill)
            {
                R.Server((p, s) => AceM3Writers.KillerNotification(p, s, "You have killed " + t.Name + "!"));
                R.Raw(AceWireWriters.ObjectDelete(target, 1));
                if (Respawn)
                {
                    AddTusker();
                }
            }
            else
            {
                R.Raw(AceWireWriters.ServerMessage($"You scorch {t.Name} for 40 points with {Caster.Table[spellId].Name}.", 7));
            }
        }

        private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
            new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);
    }

    private static (uint Floor, uint Ceiling) BoltNeed(Rig r, FightScenario scenario)
    {
        var info = r.Caster.Table[scenario.SpellId];
        ManaOrder.TryManaConversion(r.R.H.State.Self.Qualities, out var mc);
        var ceiling = GearCasts.ManaCeiling(info, 0);
        return (ManaOrder.FloorOf(ceiling, info.Power, ceiling, mc, ManaOrderOptions.Default), ceiling);
    }

    [Fact]
    public void The_bolt_is_sent_from_its_try_floor_and_every_refusal_is_counted()
    {
        // M5 1 fix A, 1-R5: the seed refuses the first three tries, so the refusals and the spacing are really there to check (the default seed lands the first); 1-R10: spells_cast counts the cast that went through, not the refused tries.
        var r = new Rig(mana: 40, stamina: 100, manaSeed: RefusingSeed) { Fight = new FightOptions { Kills = 1, StaminaFullPercent = 0 } }; // the refill (D88) off: these two check the bolt's own tries
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.05, () => r.Caster.Charged.Any(c => Rig.IsBolt(c.Spell)));
        var (floor, ceiling) = BoltNeed(r, scenario);

        Assert.InRange(40u, floor, ceiling - 1); // under the ceiling, at or over the floor: the order sends the bolt now (no ceiling wait)
        var tries = r.BoltAnswers.ToList();
        Assert.Equal(RefusedBeforeLanding + 1, tries.Count); // the seed's roll: three refused, the fourth lands
        Assert.Equal(RefusedBeforeLanding, r.BoltRefusals);
        Assert.True(r.BoltRefusals > 0);
        Assert.Equal(0, scenario.Dances);
        for (var i = 1; i < tries.Count; i++)
        {
            Assert.InRange(tries[i].At - tries[i - 1].At, CastRules.DefaultManaTrySpacingSeconds - 1e-9, CastRules.DefaultManaTrySpacingSeconds + 0.15); // the 0.25 s spacing after each refusal, on the fake clock (frames of 0.05 s)
        }

        r.Run(scenario, 60, 0.05, () => scenario.Casts.Attempts > 0);
        Assert.Equal(r.BoltRefusals, scenario.Casts.ManaRefusals); // every 0x0401 of the tries is counted, none guessed
        Assert.Equal(RefusedBeforeLanding + 1, scenario.Casts.Tries);
        Assert.Equal(1, scenario.Casts.ManaLanded); // the fourth try is a mana try (40 is under the ceiling) and the projectile confirms it
        Assert.Equal(r.BoltAnswers.Count() - r.BoltRefusals, scenario.Tally.Casts); // 1-R10: spells_cast= is the casts that went through; the refused tries stay in mana_refusals=
        Assert.Equal(1, scenario.Tally.Casts);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("waiting for mana: the cast needs up to", StringComparison.Ordinal));
        Assert.Contains(" mana_refusals=3 ", scenario.ManaFields() + " ", StringComparison.Ordinal);
    }

    [Fact]
    public void The_mana_wait_opens_at_the_bolts_first_refused_try_and_closes_at_the_try_that_lands_M5_1_fix_A_1_R9()
    {
        // 40 mana is at the try floor, so no bolt is held back (BoltNeedsMana opens no wait): the pause for mana is the refused tries, from the first 0x0401 to the next bolt that lands.
        var r = new Rig(mana: 40, stamina: 100, manaSeed: RefusingSeed) { Fight = new FightOptions { Kills = 1, StaminaFullPercent = 0 } }; // the refill (D88) off: these two check the bolt's own tries
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.05, () => r.BoltAnswers.Any());
        Assert.Equal(0, scenario.ManaWaitSeconds); // the first try is sent and unanswered: nothing is waiting yet
        r.Run(scenario, 30, 0.05, () => r.BoltRefusals >= 1 && scenario.ManaWaitSeconds > 0);
        Assert.True(scenario.ManaWaitSeconds > 0, "the wait opens at the first refused try");
        r.Run(scenario, 30, 0.05, () => scenario.Casts.Attempts > 0);

        var spacing = RefusedBeforeLanding * CastRules.DefaultManaTrySpacingSeconds;
        Assert.InRange(scenario.ManaWaitMaxSeconds, spacing, spacing + 0.6); // the three 0.25 s spacings and the answers between, first refusal to landing
        Assert.Equal(scenario.ManaWaitMaxSeconds, scenario.ManaWaitSeconds, 3); // one wait, closed
        var closed = scenario.ManaWaitSeconds;
        r.Run(scenario, 5, 0.05);
        Assert.Equal(closed, scenario.ManaWaitSeconds, 3);
    }

    [Fact]
    public void Under_the_floor_the_dance_presses_at_the_quarter_second_spacing_until_it_lands_every_press_counted()
    {
        var r = new Rig(mana: 2, stamina: 200, manaSeed: RefusingSeed);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.05, () => scenario.DancesLanded > 0);

        Assert.Equal(1, scenario.DancesLanded);
        Assert.Equal(1, scenario.Dances);
        var presses = r.DanceAnswers.ToList();
        Assert.Equal(presses.Count, scenario.ManaPresses);
        Assert.Equal(RefusedBeforeLanding + 1, presses.Count); // the seed's roll: three refused, the fourth lands (M5 1 fix A, 1-R5)
        Assert.Equal(RefusedBeforeLanding, r.DanceRefusals);
        Assert.True(r.DanceRefusals > 0);
        Assert.Equal(r.DanceRefusals + 1, presses.Count); // k refusals, then the landing
        Assert.Equal(r.DanceRefusals, scenario.Casts.ManaRefusals);
        // The landed press is proved by ACE's transfer line ("You cast ... on yourself and lose N points of stamina and also gain M points of mana", chat type 7): Landed, not Unproven, and counted as landed and proved.
        Assert.Equal((1, 0), (scenario.Casts.Landed, scenario.Casts.Unproven));
        Assert.Equal((1, 1), (scenario.Casts.Proved, scenario.Casts.ManaLanded));
        Assert.Contains(" mana_landed=1 busy=0 resists=0 proved=1 ", scenario.ManaFields() + " ", StringComparison.Ordinal);
        for (var i = 1; i < presses.Count; i++)
        {
            Assert.InRange(presses[i].At - presses[i - 1].At, 0.25 - 1e-9, 0.25 + 0.15); // the spacing after each refusal, on the fake clock (frames of 0.05 s)
        }

        Assert.Contains(r.Log.Lines, l => l.Contains("mana: the dance: ", StringComparison.Ordinal) && l.Contains("ManaDance(40,0.25)", StringComparison.Ordinal));
        Assert.Empty(r.BoltAnswers); // nothing was cast at the tusker under the floor
    }

    [Fact]
    public void Low_stamina_with_a_potion_held_drinks_a_mana_potion()
    {
        var r = new Rig(mana: 2, stamina: 1, manaPotions: 3);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => r.Caster.PotionsDrunk > 0 && scenario.PotionsDrunk > 0);

        Assert.Equal(1, r.Caster.PotionsDrunk);
        Assert.Equal(1, scenario.PotionsDrunk);
        Assert.Equal(0, scenario.Dances);
        Assert.Contains(r.Log.Lines, l => l.Contains("the stamina is too low for a transfer: a Mana Potion", StringComparison.Ordinal));
    }

    [Fact]
    public void No_potion_and_low_stamina_rest_in_peace_for_stamina_then_the_magic_mode_and_the_dance()
    {
        var r = new Rig(mana: 2, stamina: 1);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 5;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 200, 0.1, () => scenario.Dances > 0);

        Assert.Equal(1, scenario.Rests);
        Assert.True(scenario.Dances >= 1);
        var modes = r.Caster.ModesSent;
        var peace = modes.IndexOf((int)CombatMode.NonCombat);
        Assert.True(peace >= 0, "the rest goes to NonCombat");
        Assert.Contains((int)CombatMode.Magic, modes.Skip(peace + 1)); // the magic mode again before the dance
        Assert.Contains(r.Log.Lines, l => l.Contains("mana: a rest in peace for stamina", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("then the dance", StringComparison.Ordinal));
    }

    [Fact]
    public void At_ninety_percent_stamina_with_a_hundred_mana_missing_the_dance_comes_before_the_bolt()
    {
        var r = new Rig(mana: 300, stamina: 260);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.05, () => r.BoltAnswers.Any());

        Assert.True(ManaOrder.PreemptTransfer(260, 275, 300, 426));
        Assert.True(scenario.Dances >= 1);
        var firstDance = r.DanceAnswers.First().At;
        Assert.True(firstDance < r.BoltAnswers.First().At, "the transfer is sent before the bolt");
        Assert.Contains(r.Log.Lines, l => l.Contains("the pre-emptive transfer (D27)", StringComparison.Ordinal));
        Assert.Equal(0, scenario.ManaWaitSeconds); // M5 1 fix A, 1-R9: the pre-emptive dance is not a mana wait (no bolt was held back; the mana was not short)
        Assert.True(scenario.Casts.Proved >= 1 && scenario.Casts.Unproven == 0, "the pre-emptive transfer is proved by the chat line too"); // the same request carries the proof
    }

    /// <summary>A tusker that strikes the mage every <c>every</c> seconds: the health falls by 2 on the fake clock, as the server's vital update tells it. Call <see cref="Tick"/> once a frame (the <c>until</c> of <see cref="Rig.Run"/>).</summary>
    private sealed class Striker(Rig rig, double every)
    {
        private byte _sequence;
        private uint _health = 200;
        private double _next = double.NaN;

        public int Blows { get; private set; }

        /// <summary>One frame's blow, if due; true once the fake clock reaches <paramref name="until"/>.</summary>
        public bool Tick(double until)
        {
            if (double.IsNaN(_next))
            {
                _next = rig.Now + every;
            }

            if (rig.Now >= _next)
            {
                _next += every;
                _health -= 2;
                Blows++;
                rig.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, 2, _health)); // the current health (the secondary attribute 2)
            }

            return rig.Now >= until;
        }
    }

    [Fact]
    public void A_rest_for_stamina_ended_by_a_health_fall_is_set_aside_and_the_fight_waits_in_the_magic_mode_while_a_tusker_hits_M5_1_fix_A_1_R3()
    {
        // No mana, almost no stamina, no potion: the order rests in peace for stamina. A tusker hits every half second. Before the fix every blow ended the rest, the mode flipped to magic and the order chose the rest again at once
        // (one NonCombat round trip per blow, never a bolt, _rests climbing); now the rest is set aside for RestAsideSeconds after a health fall.
        var r = new Rig(mana: 2, stamina: 1);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0.05;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => scenario.Rests > 0);
        Assert.Equal(1, scenario.Rests);
        var restStart = r.Now;
        var strikes = new Striker(r, 0.5);

        r.Run(scenario, 60, 0.1, () => strikes.Tick(restStart + FightScenario.RestAsideSeconds - 5));
        Assert.True(strikes.Blows >= 40, "blows " + strikes.Blows);
        Assert.Equal(1, scenario.Rests); // the blows ended the one rest; none began again
        Assert.Equal(1, r.Caster.ModesSent.Count(m => m == (int)CombatMode.NonCombat));
        Assert.Equal((int)CombatMode.Magic, r.Caster.ModesSent[^1]); // and the fight waits in the magic mode
        Assert.Single(r.Log.Lines, l => l.Contains("the rest for stamina ends early", StringComparison.Ordinal) && l.Contains("no rest for 30 s", StringComparison.Ordinal));
        Assert.Single(r.Log.Lines, l => l.Contains("the rest for stamina is set aside for", StringComparison.Ordinal));

        // Past the aside the order may rest again, and the next blow ends that rest too: two rests in all, not one per blow.
        r.Run(scenario, 60, 0.1, () => strikes.Tick(restStart + FightScenario.RestAsideSeconds + 10));
        Assert.Equal(2, scenario.Rests);
        Assert.Equal(2, r.Caster.ModesSent.Count(m => m == (int)CombatMode.NonCombat));
    }

    [Fact]
    public void During_the_aside_the_fight_casts_what_the_mana_allows_without_leaving_the_magic_mode_M5_1_fix_A_1_R3()
    {
        var r = new Rig(mana: 2, stamina: 1);
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0.05;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => scenario.Rests > 0);
        var strikes = new Striker(r, 0.5);
        r.Run(scenario, 20, 0.1, () => strikes.Tick(r.Now + 3) || r.Log.Lines.Any(l => l.Contains("is set aside for", StringComparison.Ordinal)));
        Assert.Contains(r.Log.Lines, l => l.Contains("the rest for stamina is set aside for", StringComparison.Ordinal));
        Assert.Empty(r.BoltAnswers);

        r.Caster.SetVitals(mana: 400, stamina: 1); // the mana comes back (a potion, a friend): the aside does not stand in the way of a bolt
        r.Run(scenario, 10, 0.1, () => r.BoltAnswers.Any());

        Assert.NotEmpty(r.BoltAnswers);
        Assert.Equal(1, scenario.Rests);
        Assert.Equal(1, r.Caster.ModesSent.Count(m => m == (int)CombatMode.NonCombat));
    }

    [Fact]
    public void A_fight_that_ends_in_the_middle_of_a_dance_keeps_that_dance_s_presses_in_its_counts_M5_1_fix_A_1_R11()
    {
        // Seed 6 refuses seven presses in a row (the eighth lands): the 0.02-minute (1.2 s) stop falls inside the dance.
        var r = new Rig(mana: 2, stamina: 200, manaSeed: 6) { Fight = new FightOptions { Kills = 0, Minutes = 0.02 } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        var status = r.Run(scenario, 30, 0.05);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(r.Log.Lines, l => l.Contains("minutes are up", StringComparison.Ordinal));
        Assert.Equal(1, scenario.Dances);
        Assert.Equal(0, scenario.DancesLanded); // the stop came before the dance landed
        Assert.InRange(scenario.ManaPresses, 3, 7);
        Assert.Equal(scenario.ManaPresses, scenario.Casts.Tries); // the dance's presses are in the line's tally, not lost with the attempt
        Assert.InRange(scenario.Casts.ManaRefusals, 2, scenario.ManaPresses);
        Assert.Contains($" mana_presses={scenario.ManaPresses} dances=1 dances_landed=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_kill_switch_fails_the_fight_at_once_named_and_nothing_more_is_sent_M5_1_fix_A_1_R15()
    {
        // Out of mana and stamina with no potion: the order rests in peace (a NonCombat mode change as Automation). The kill switch turns on during the rest.
        var r = new Rig(mana: 2, stamina: 1) { Fight = new FightOptions { LeaveOnFailure = false } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0.05;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => scenario.Rests > 0);
        Assert.Equal(1, scenario.Rests);
        var before = r.R.H.Net.Sends.Count;
        r.R.Api.KillSwitch = true;

        var status = r.Frame(scenario, 0.1);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("Blocked why=" + VitalsKeeper.KillSwitchWhy, scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("Blocked why=kill-switch", StringComparison.Ordinal));
        for (var i = 0; i < 20; i++)
        {
            r.Frame(scenario, 0.25);
        }

        Assert.Equal(before, r.R.H.Net.Sends.Count); // nothing was sent after the switch: no cast, no mode change, no drink
    }

    [Fact]
    public void Cast_mana_tries_zero_is_the_ceiling_wait_with_no_tries_and_no_dance()
    {
        var r = new Rig(mana: 40, stamina: 260) { Fight = new FightOptions { Mana = ManaOrderOptions.Ceiling } };
        r.Caster.ManaRegenPerSecond = 4;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        uint manaAtFirstBolt = 0;
        r.Run(scenario, 120, 0.25, () =>
        {
            if (manaAtFirstBolt == 0 && r.BoltAnswers.Any())
            {
                manaAtFirstBolt = r.Caster.Charged.First(c => Rig.IsBolt(c.Spell)).Cost + r.Mana;
            }

            return manaAtFirstBolt != 0;
        });
        var (_, ceiling) = BoltNeed(r, scenario);

        Assert.Contains(r.Log.Lines, l => l.Contains("waiting for mana: the cast needs up to " + ceiling.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.True(manaAtFirstBolt >= ceiling, $"the bolt waited for its ceiling {ceiling} (mana {manaAtFirstBolt})");
        Assert.Equal(0, scenario.Dances);
        Assert.Empty(r.DanceAnswers);
        Assert.Equal(0, r.BoltRefusals);
    }

    [Fact]
    public void The_mana_wait_and_both_kill_rates_are_measured_on_the_fake_clock()
    {
        // The ceiling order with a known regeneration: one wait from the first bolt not sent to the bolt sent; then no tusker in range: walks with no target to pick, left out of the burst rate.
        var r = new Rig(mana: 10, stamina: 100) { Fight = new FightOptions { Mana = ManaOrderOptions.Ceiling, Kills = 0, Minutes = 1 } };
        r.Caster.ManaRegenPerSecond = 4;
        r.Caster.StaminaRegenPerSecond = 0;
        r.HitsToKill = 1;
        r.AddTusker();
        var walk = new Walk { TicksPerLeg = 20 };
        var scenario = r.Scenario(walk);
        var status = r.Run(scenario, 120, 0.25);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, scenario.Tally.Kills);
        var (_, ceiling) = BoltNeed(r, scenario);
        Assert.InRange(scenario.ManaWaitMaxSeconds, ((ceiling - 10) / 4.0) - 1.0, ((ceiling - 10) / 4.0) + 0.75); // the regeneration's 4 a second from 10 to the ceiling (it runs from the session's start, a frame or two before the first pick)
        Assert.Equal(scenario.ManaWaitMaxSeconds, scenario.ManaWaitSeconds, 3); // one wait
        Assert.True(walk.Legs >= 2);
        Assert.InRange(scenario.IdleSeconds, 40, 60);
        Assert.Equal(3600.0 / scenario.FightSeconds, scenario.KillsPerHour, 6);
        Assert.Equal(3600.0 / (scenario.FightSeconds - scenario.IdleSeconds), scenario.KillsPerHourBurst, 6);
        Assert.True(scenario.KillsPerHourBurst > scenario.KillsPerHour);
        var fields = scenario.VerdictFields();
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $" mana_wait_max_s={scenario.ManaWaitMaxSeconds:0.#} mana_wait_s={scenario.ManaWaitSeconds:0.#} kills_per_hour={scenario.KillsPerHour:0.#} kills_per_hour_burst={scenario.KillsPerHourBurst:0.#} mana_tries="), fields, StringComparison.Ordinal);
        Assert.EndsWith(" mana_presses=0 dances=0 dances_landed=0 " + scenario.TourFields() + " " + scenario.LootFields(), fields, StringComparison.Ordinal); // g6: the tour's fields follow the mana fields, then M5 3 m2's loot fields
        Assert.StartsWith("level=", scenario.ManaFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fight_minutes_ends_the_fight_cleanly_with_the_kills_it_made_and_the_leg_verdict_holds()
    {
        var r = new Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 5, Minutes = 0.5 } };
        r.AddTusker();
        var scenario = r.Scenario();
        var status = r.Run(scenario, 120, 0.25);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.InRange(scenario.FightSeconds, 30, 31);
        Assert.Contains(r.Log.Lines, l => l.Contains("the fight's 0.5 minutes are up", StringComparison.Ordinal));
        var t = scenario.Tally;
        var verdict = FightLegVerdict.Compose(scenario.Failure, new FightLegMeasures(t.Kills, t.DamageDealt, t.Casts, t.Misses, t.XpGained, t.DamageTaken, t.HitsTaken, t.Evades, null, null, null, null, scenario.FightSeconds, ManaFields: scenario.ManaFields()));
        Assert.True(verdict.Ok, string.Join("; ", verdict.Problems));
        Assert.EndsWith(" dances_landed=0", verdict.Line, StringComparison.Ordinal);
        Assert.Contains(" level=", verdict.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Fight_kills_zero_runs_unlimited_to_the_minutes_and_the_killed_list_stays_small()
    {
        var r = new Rig(mana: 426, stamina: 100) { Fight = new FightOptions { Kills = 0, Minutes = 2 }, Respawn = true, HitsToKill = 1 };
        r.Caster.ManaRegenPerSecond = 50;
        r.AddTusker();
        var scenario = r.Scenario();
        var status = r.Run(scenario, 300, 0.25);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(scenario.Tally.Kills > FightOptions.DefaultKills * 10, $"kills={scenario.Tally.Kills}");
        Assert.InRange(scenario.FightSeconds, 120, 121);
        Assert.InRange(r.MaxKilledHeld, 0, 1); // m5-fight-killed-growth: a guid leaves the list once its object leaves the table
        Assert.Equal(0, scenario.KilledHeld);
    }

    [Fact]
    public void Fight_max_level_six_chooses_vi_from_bsd_mages_skill()
    {
        var r = new Rig(mana: 400, stamina: 100);
        var self = r.R.H.State.Self;
        Assert.True(GearCasts.TryCurrentSkill(self.Qualities, MageTemplate.WarMagic, out var skill, out _));
        Assert.Equal(6, FightScenario.ChooseLevel(skill, self.Spellbook, r.Buff.CastChance, out _)); // the scripted BSD Mage reaches VII
        Assert.Equal(5, FightScenario.ChooseLevel(skill, self.Spellbook, r.Buff.CastChance, out _, maxLevel: 6));

        r.Fight = new FightOptions { MaxLevel = 6 };
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 10, 0.25, () => scenario.Fighting);
        Assert.Equal("VI", scenario.Level);
        Assert.Equal(BoltIds[5], scenario.SpellId);
        Assert.Contains(r.Log.Lines, l => l.Contains("levels up to VI", StringComparison.Ordinal));
    }

    [Fact]
    public void The_level_is_chosen_again_once_after_a_buff_change_lowers_war_magic()
    {
        var r = new Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 3 }, HitsToKill = 1 };
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.25, () => scenario.Tally.Kills >= 1);
        Assert.Equal("VII", scenario.Level);

        // A War Magic debuff (a skill enchantment of -200 on War Magic, ACE's 0x02C2): the next pick chooses the level again from the lower skill.
        var mod = BuffStatMods.BySpell.Values.First(m => m.IsSkill && m.Key == MageTemplate.WarMagic);
        r.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(9999, 1, 900, 0, 300, 0.0, 600, WorldActionRig.Player, 0f, 0f, 0.0, mod.Type, mod.Key, -200f, 0)));
        r.AddTusker();
        r.AddTusker(45);
        r.Run(scenario, 60, 0.25);

        Assert.NotEqual("VII", scenario.Level);
        Assert.Single(r.Log.Lines, l => l.Contains("the level is chosen again: VII to ", StringComparison.Ordinal));
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("the level is chosen again", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_fight_options_parse_the_new_limits_the_level_cap_and_the_mana_order_and_name_their_errors()
    {
        var o = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-kills=0", "--fight-minutes=30", "--fight-max-level=6", "--cast-mana-tries=3", "--cast-mana-dance-presses=20"]));
        Assert.Empty(o.Errors);
        Assert.Equal((0, 30.0, 6, 3, 20), (o.Kills, o.Minutes, o.MaxLevel, o.Mana.Tries, o.Mana.DancePresses));
        Assert.True(o.UnlimitedKillCount);
        Assert.Equal(1800, o.FailSeconds);
        Assert.Contains("kills=unlimited", o.ToString(), StringComparison.Ordinal);
        Assert.Contains("minutes=30 max_level=6 cast_mana_tries=3", o.ToString(), StringComparison.Ordinal);

        var d = new FightOptions();
        Assert.Equal((1, 0.0, 7, ManaOrderOptions.DefaultTries, 300.0), (d.Kills, d.Minutes, d.MaxLevel, d.Mana.Tries, d.FailSeconds));
        Assert.True(FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-kills=5000", "--fight-seconds=60"])).Errors.Count == 0); // the 1,000 cap is lifted

        var bad = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-max-level=8", "--fight-minutes=-1", "--cast-mana-tries=99", "--fight-kills=-2"]));
        Assert.Equal(4, bad.Errors.Count);
        Assert.Contains(bad.Errors, e => e.Contains("--fight-max-level: '8' is not a Flame Bolt level 1 to 7", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.Contains("--fight-minutes: '-1' is not 0 (off) to 1440 minutes", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.StartsWith(ManaOrderOptions.ErrorPrefix + "--cast-mana-tries", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.Contains("is not 0 (no kill limit) or a positive whole number", StringComparison.Ordinal));
        Assert.Equal((1, 7, ManaOrderOptions.DefaultTries), (bad.Kills, bad.MaxLevel, bad.Mana.Tries));

        var alone = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-kills=0"]));
        Assert.Single(alone.Errors, e => e.Contains("needs --fight-minutes above 0", StringComparison.Ordinal));
        Assert.Equal(400, new FightOptions { Seconds = 400, Minutes = 5 }.FailSeconds);
        Assert.Equal(600, new FightOptions { Seconds = 400, Minutes = 10 }.FailSeconds);
    }

    // ---- M5 7 F5 (D88): the owner's order: the transfer, then Revitalize Self to refill the stamina, then back to the fight.

    [Fact]
    public void Mana_empty_with_the_stamina_not_full_gives_the_transfer_then_the_revitalize_then_the_bolt_M5_7_F5()
    {
        var r = new Rig(mana: 2, stamina: 200, manaSeed: RefusingSeed) { Fight = new FightOptions { Kills = 1 } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.05, () => r.BoltAnswers.Any());

        Assert.True(r.DanceAnswers.Any() && r.RevitalizeAnswers.Any() && r.BoltAnswers.Any());
        Assert.True(r.DanceAnswers.First().At < r.RevitalizeAnswers.First().At, "the transfer first");
        Assert.True(r.RevitalizeAnswers.First().At < r.BoltAnswers.First().At, "then the refill, then the bolt");
        Assert.True(r.Caster.Stamina >= ManaOrder.FullStamina(275, 90), "the stamina is back near full before the bolt: " + r.Caster.Stamina);
        Assert.True(scenario.Revitalizes >= 1);
        Assert.Contains(r.Log.Lines, l => l.Contains("mana: revitalize: Revitalize Self", StringComparison.Ordinal) && l.Contains(" at stamina ", StringComparison.Ordinal) && l.Contains(", mana ", StringComparison.Ordinal));
        Assert.Contains($" revitalizes={scenario.Revitalizes} stamina_min=", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("mana: D88 floors:", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("mana: Mana Potion", StringComparison.Ordinal));
    }

    [Fact]
    public void Stamina_low_between_kills_with_enough_mana_revitalizes_first_and_fight_stamina_full_0_turns_it_off_M5_7_F5()
    {
        var r = new Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 1 } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.05, () => r.BoltAnswers.Any());
        Assert.True(r.RevitalizeAnswers.Any(), "a revitalize was cast");
        Assert.True(r.RevitalizeAnswers.First().At < r.BoltAnswers.First().At, "the refill comes before the bolt");
        Assert.Empty(r.DanceAnswers); // the mana was there: no transfer
        Assert.True(r.Caster.Stamina >= ManaOrder.FullStamina(275, 90));

        var off = new Rig(mana: 400, stamina: 100) { Fight = new FightOptions { Kills = 1, StaminaFullPercent = 0 } };
        off.Caster.ManaRegenPerSecond = 0;
        off.Caster.StaminaRegenPerSecond = 0;
        off.AddTusker();
        var offScenario = off.Scenario();
        off.Run(offScenario, 30, 0.05, () => off.BoltAnswers.Any());
        Assert.Empty(off.RevitalizeAnswers);
        Assert.Equal(100u, off.Caster.Stamina);
    }

    [Fact]
    public void No_payable_revitalize_falls_back_to_a_stamina_potion_after_the_transfer_M5_7_F5()
    {
        var r = new Rig(mana: 2, stamina: 200, staminaPotions: 2, knowsRevitalize: false) { Fight = new FightOptions { Kills = 1 } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.05, () => r.BoltAnswers.Any());
        Assert.Empty(r.RevitalizeAnswers); // no level is known: nothing to cast
        Assert.True(r.Caster.StaminaPotionsDrunk >= 1, "a Stamina Potion stood in");
        Assert.Contains(r.Log.Lines, l => l.Contains("no Revitalize Self is payable: a Stamina Potion", StringComparison.Ordinal));
    }

    [Fact]
    public void The_dance_goes_on_pressing_after_a_run_of_refused_presses_while_the_stamina_is_there_M5_7_F5()
    {
        // Two presses a dance and a seed that refuses the first three: item 1's order gave the dance up (set aside 60 s, a rest or a potion); D88 presses again at once.
        var r = new Rig(mana: 2, stamina: 200, manaSeed: RefusingSeed) { Fight = new FightOptions { Kills = 1, Mana = ManaOrderOptions.Default with { DancePresses = 2 } } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.05, () => scenario.DancesLanded > 0);
        Assert.Equal(1, scenario.DancesLanded);
        Assert.True(scenario.Dances >= 2);
        Assert.Equal(0, scenario.Rests);
        Assert.Equal(0, scenario.PotionsDrunk);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("the dance is set aside", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("so the presses go on", StringComparison.Ordinal));
    }

    [Fact]
    public void A_level_80_mage_refills_with_Revitalize_Self_VI_and_a_level_126_mage_with_VII_by_the_scarabs_in_the_pack_M5_7_F5()
    {
        var r = new Rig(mana: 400, stamina: 100);
        var vi = VitalsMeans.RevitalizeSelf[5];
        var vii = VitalsMeans.RevitalizeSelf[6];
        r.Caster.Table[vi] = r.Caster.Table[vi] with { Formula = [6] }; // Pyreal Scarab
        r.Caster.Table[vii] = r.Caster.Table[vii] with { Formula = [112] }; // Platinum Scarab
        var self = r.R.H.State.Self;

        IReadOnlyList<uint> Picked(params uint[] formulaScarabs)
        {
            var held = new Dictionary<uint, int>();
            foreach (var scarab in formulaScarabs)
            {
                foreach (var (component, n) in GearKit.FociFormulaCounts([scarab]))
                {
                    Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie));
                    held[weenie.Wcid] = Math.Max(held.GetValueOrDefault(weenie.Wcid), n);
                }
            }

            var refill = new FightRefill(r.Caster.Table, r.Log, 90);
            refill.Fill(self.Qualities, self.Spellbook, held, 0.5);
            return [.. refill.Levels.ToArray().Select(l => l.SpellId)];
        }

        var at80 = Picked(6); // the Pyreal Scarab only
        var at126 = Picked(6, 112); // the Platinum Scarab too
        Assert.Equal(vi, at80[^1]);
        Assert.DoesNotContain(vii, at80);
        Assert.Equal(vii, at126[^1]);
        Assert.Equal(at80.Count + 1, at126.Count);
        Assert.Contains(r.Log.Lines, l => l.Contains("Revitalize Self: level VII needs Platinum Scarab", StringComparison.Ordinal)); // the skip is named, once
        var level = ManaOrder.RevitalizeLevelOf([.. at126.Select(id => new RevitalizeLevel(id, 60))], 400, 5);
        Assert.Equal(at126.Count - 1, level); // the order takes the highest the mana pays
    }

    [Fact]
    public void Fight_stamina_full_is_registered_parsed_and_bounded_M5_7_F5()
    {
        Assert.Equal(90, new FightOptions().StaminaFullPercent);
        Assert.Equal(85, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-stamina-full=85"])).StaminaFullPercent);
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-stamina-full=0"])).StaminaFullPercent);
        var bad = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-stamina-full=101"]));
        Assert.Single(bad.Errors);
        Assert.Equal(90, bad.StaminaFullPercent);
        Assert.Contains(OptionCatalog.Fight().Options, o => o.Name == "--fight-stamina-full");
    }
}
