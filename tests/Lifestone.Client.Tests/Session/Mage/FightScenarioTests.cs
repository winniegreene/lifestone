using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.Magic;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The fight scenario's loop rules (M3 3.1 lane fa, <see cref="FightScenario"/>) over a scripted ACE on the real read model, reducer and Action API: a target in range dies after two hits and the scenario ends with
/// <c>kills=1</c>; a cast with no projectile is a miss and three in a row move to the next waypoint (a scripted <see cref="IFightWalk"/>); an out-of-range answer walks at once; a Virindi (23), a drudge (1610) and
/// a tusker in the chest corner (cell 0x010502D6) are never targeted; a fizzle drops one level once; the seconds limit and the player's death end the run loudly; the stages are logged once each; and the loop allocates
/// nothing while it waits. Nothing here needs the dats or a server.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class FightScenarioTests
{
    private const uint Den = 0x01050375;
    private const uint HoltburgCell = 0xA9B40019;
    private const uint TuskerSlave = 1628;
    private const uint DeadChestCorner = 0x010502D6;
    private const float PlayerX = 100;
    private const float PlayerY = 20;

    /// <summary>The session view over the scripted rig.</summary>
    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>A walk that arrives after <see cref="TicksPerLeg"/> ticks, or fails on demand.</summary>
    private sealed class FakeWalk : IFightWalk
    {
        private int _ticks;
        private bool _walking;

        public int TicksPerLeg { get; set; } = 4;

        public int Prepared { get; private set; }

        public int Legs { get; private set; }

        public bool FailNext { get; set; }

        /// <summary>When set, <see cref="Tick"/> returns Failed with this reason on every call (the walker left the floor, 3.1-R4), whatever the scenario is doing.</summary>
        public string? LeaveFloor { get; set; }

        public string? Failure { get; private set; }

        /// <summary>When set, <see cref="HasClearLine"/> is false for a target in this cell until a leg has arrived (the wall between the arrival and that plaza).</summary>
        public uint BlockedCell { get; set; }

        public int LineQueries { get; private set; }

        public bool HasClearLine(in CellFrame from, in CellFrame to)
        {
            LineQueries++;
            return BlockedCell == 0 || to.CellId != BlockedCell;
        }

        public void Prepare(IGameSessionView session, double now) => Prepared++;

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "fake leg " + (Legs + 1);
            if (FailNext)
            {
                Failure = "the fake walk is cut";
                return false;
            }

            Legs++;
            _walking = true;
            _ticks = 0;
            return true;
        }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (LeaveFloor is not null)
            {
                Failure = LeaveFloor;
                return FightWalkStatus.Failed;
            }

            if (!_walking)
            {
                return FightWalkStatus.Idle;
            }

            if (++_ticks < TicksPerLeg)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            BlockedCell = 0;
            return FightWalkStatus.Arrived;
        }

        public void Abandon() => _walking = false;

        public void Dispose()
        {
        }
    }

    private enum Shot
    {
        /// <summary>The cast hits (a damage line) or kills when it is the creature's last hit.</summary>
        Normal,

        /// <summary>UseDone 0 with no projectile.</summary>
        NoProjectile,

        /// <summary>The projectile exists but no damage line follows.</summary>
        Dud,

        /// <summary>The projectile exists, stops (zero velocity) 0.2 s later short of the target and no damage line follows: a wall.</summary>
        Wall,

        /// <summary>A tusker that ran at the mage: the bolt stops 0.2 s later where the target now stands, and "X resists your spell" arrives, with no damage line (4.2 R2).</summary>
        Resist,

        /// <summary>The target ran at the mage and the bolt stops beside it with no resist line and no damage line: still not a wall (4.2 R2, the distance rule alone).</summary>
        RanIn,

        /// <summary>WeenieError 0x0402 then UseDone.</summary>
        Fizzle,

        /// <summary>UseDone 0x0550.</summary>
        OutOfRange,

        /// <summary>The creature dies (its object is deleted) with no 0x01AD.</summary>
        SilentKill,
    }

    private sealed class Creature(uint guid, uint wcid, string name, int hitsToKill)
    {
        public uint Guid { get; } = guid;

        public uint Wcid { get; } = wcid;

        public string Name { get; } = name;

        public int HitsToKill { get; } = hitsToKill;

        public int Hits { get; set; }

        public bool Dead { get; set; }
    }

    /// <summary>The scripted ACE and the session over it.</summary>
    private sealed class Rig
    {
        private readonly Dictionary<uint, Creature> _creatures = [];
        private readonly Queue<Shot> _script = new();
        private readonly Queue<(uint Spell, uint Target, double Due)> _pending = new();
        private readonly byte[] _sequence = new byte[16];
        private int _seen;
        private uint _nextProjectile = 0x80009000;
        private double _lastNow = double.NaN;
        private double _manaAcc;
        private readonly Queue<(uint Guid, double Due)> _late = new();
        private readonly Queue<(uint Guid, double Due)> _stops = new();
        private readonly Queue<(uint Spell, uint Target, Shot Shot, double Due)> _lateHits = new();
        private ushort _teleports = 2;

        public Rig(uint mana = 400, uint warRanks = 330, IReadOnlyCollection<uint>? known = null, FightOptions? fight = null, BuffOptions? buff = null, float range = 30f)
        {
            R = new WorldActionRig();
            Mana = mana;
            Table = BuildTable(range);
            R.Spells([.. Table.Values]);
            Fight = fight ?? new FightOptions();
            Buff = buff ?? new BuffOptions();
            View = new View(R);
            Describe(warRanks, known ?? Table.Keys.ToArray());
            R.CombatModeIs(CombatModes.NonCombat);
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 2, 2, 1, cell: Den, x: PlayerX));
        }

        public WorldActionRig R { get; }

        public IGameSessionView View { get; }

        public Dictionary<uint, SpellInfo> Table { get; }

        public FightOptions Fight { get; }

        public BuffOptions Buff { get; }

        public uint Mana { get; set; }

        /// <summary>When above zero, a killed creature stays in the world this many seconds after the 0x01AD (ACE's death animation): then the Dead motion and the deletion arrive together.</summary>
        public double DeathDelay { get; set; }

        /// <summary>When above zero, the projectile's hit (the damage line, or the kill) comes this many seconds after its CreateObject: the flight (ACE sends the UseDone at the launch, the hit when the bolt lands).</summary>
        public double HitDelay { get; set; }

        /// <summary>When set, the 0x01AD text is this instead of "You have killed &lt;name&gt;!" (a kill that does not name the target).</summary>
        public string? KillText { get; set; }

        /// <summary>When set, a "place loc" tool request (the den's arrival through its Places row) is answered with a teleport to this cell (the den's arrival is 0x01050375). A "place poi" (the way out of the den, Holtburg) is always answered with <see cref="HoltburgCell"/>.</summary>
        public uint? ArrivalCell { get; set; }

        /// <summary>The place tool requests, in order.</summary>
        public List<string> Places { get; } = [];

        public double ManaRegenPerSecond { get; set; } = 5;

        public double CastSeconds { get; set; } = 0.5;

        public uint ManaPerCast { get; set; } = 20;

        public List<int> ModesSent { get; } = [];

        public List<uint> CastTargets { get; } = [];

        public List<uint> CastSpells { get; } = [];

        public List<uint> HealthQueries { get; } = [];

        public double Now => R.H.R.Clock.Now;

        public RecordingLogSink Log => R.H.Log;

        public GearKit Kit { get; } = GearKit.Of([GearItemOf(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand), .. GearKit.Armour.Select(a => GearItemOf(a.Wcid, a.Name, GearKind.Armour, a))]);

        public void Script(params Shot[] shots)
        {
            foreach (var shot in shots)
            {
                _script.Enqueue(shot);
            }
        }

        public Creature Add(uint guid, uint wcid, uint cell, float y, string name = "Tusker Slave", int hitsToKill = 2, float x = PlayerX)
        {
            var creature = new Creature(guid, wcid, name, hitsToKill);
            _creatures[guid] = creature;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(cell, x, y, 30), name: name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = wcid } }, isUpdate: false);
            return creature;
        }

        public FightScenario Scenario(IFightWalk? walk, FightStages stages = FightStages.Mode) =>
            new(Kit, Table, Buff, Fight, walk, R.H.Log, endSession: false, stages: stages);

        /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, then the scenario steps.</summary>
        public ScenarioStatus Frame(FightScenario scenario, double dt = 0.25)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            return scenario.Step(View, Now);
        }

        public ScenarioStatus Run(FightScenario scenario, double maxSeconds = 600, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Now < end)
            {
                status = Frame(scenario, dt);
            }

            return status;
        }

        public void Pump()
        {
            var now = Now;
            Regenerate(now);
            var sends = R.H.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                switch (send.Kind)
                {
                    case FakeSendKind.ChangeCombatMode:
                        ModesSent.Add((int)send.Amount);
                        R.CombatModeIs((int)send.Amount);
                        break;
                    case FakeSendKind.CastTargetedSpell:
                        CastTargets.Add(send.Guid);
                        CastSpells.Add((uint)send.Index);
                        _pending.Enqueue(((uint)send.Index, send.Guid, now + CastSeconds));
                        break;
                    case FakeSendKind.Talk when send.Text is { } text && text.Contains("place", StringComparison.Ordinal):
                        Places.Add(text);
                        if (text.Contains("poi", StringComparison.Ordinal))
                        {
                            Arrive(text, HoltburgCell);
                        }
                        else if (ArrivalCell is { } arrival)
                        {
                            Arrive(text, arrival);
                        }

                        break;
                    case FakeSendKind.QueryHealth:
                        HealthQueries.Add(send.Guid);
                        var c = _creatures[send.Guid];
                        R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, send.Guid, Math.Max(0f, 1f - ((float)c.Hits / c.HitsToKill))));
                        break;
                }
            }

            while (_pending.Count > 0 && _pending.Peek().Due <= now)
            {
                var cast = _pending.Dequeue();
                Answer(cast.Spell, cast.Target);
            }

            while (_stops.Count > 0 && _stops.Peek().Due <= now)
            {
                R.H.R.Reducer.OnVectorUpdate(R.H.R.Context, new VectorUpdateMsg(_stops.Dequeue().Guid, Vector3.Zero, Vector3.Zero, 1, 100));
            }

            while (_lateHits.Count > 0 && _lateHits.Peek().Due <= now)
            {
                var hit = _lateHits.Dequeue();
                Land(hit.Spell, _creatures[hit.Target], hit.Shot);
            }

            while (_late.Count > 0 && _late.Peek().Due <= now)
            {
                var gone = _late.Dequeue().Guid;
                R.Raw(AceWireWriters.UpdateMotion(gone, 1, 2, 2, false, new AceMovementData { Forward = 0x0011 })); // MotionCommand.Dead
                Assert.Equal((ushort?)0x0011, R.H.State.Objects.TryGet(gone, out var body) ? body.Motion?.Forward : null);
                R.Raw(AceWireWriters.ObjectDelete(gone, 1));
            }
        }

        /// <summary>The server answers the place tool: the reply, then the teleport and the position in <paramref name="cell"/>.</summary>
        private void Arrive(string request, uint cell)
        {
            var id = request[(request.LastIndexOf('#') + 1)..];
            var data = new PlaceData("loc", null, ToolsWire.FormatCell(cell), PlayerX, PlayerY, 0f, 1f, 0f, 0f, 0f);
            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.PlaceLoc, data), data.Describe()));
            _teleports++;
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(_teleports));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, _teleports, _teleports, 1, cell: cell, x: PlayerX));
            R.Raw(AceWireWriters.SetState(WorldActionRig.Player, (uint)PhysicsState.Gravity, 1, (ushort)(40 + _teleports))); // a state without Hidden ends the arrival
        }

        public void Die() => R.Server((p, s) => AceM3Writers.VictimNotification(p, s, "You have been killed by Tusker Slave."));

        private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
            new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

        private static Dictionary<uint, SpellInfo> BuildTable(float range)
        {
            var table = new Dictionary<uint, SpellInfo>();
            foreach (var line in ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt))
            {
                foreach (var spell in line.Spells)
                {
                    table[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Projectile, SpellFlags.Projectile) with
                    {
                        Name = spell.Name,
                        School = MagicSchool.WarMagic,
                        Power = spell.Power,
                        BaseMana = 20 + (uint)spell.Position,
                        BaseRangeConstant = range,
                        BaseRangeMod = 0f,
                    };
                }
            }

            return table;
        }

        private void Describe(uint warRanks, IReadOnlyCollection<uint> known)
        {
            var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
            foreach (var id in new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
            {
                attributes.Add(new AttributeDesc(id, (uint)(MageTemplate.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0), MageTemplate.CreationValue(id), 0));
            }

            var skills = ImmutableArray.CreateBuilder<SkillDesc>();
            foreach (var id in new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.LifeMagic, MageTemplate.WarMagic, MageTemplate.ManaConversion })
            {
                var t = MageTemplate.TargetOf(MageStatKind.Skill, id)!;
                var ranks = id == MageTemplate.WarMagic ? warRanks : (uint)t.Ranks;
                skills.Add(new SkillDesc(id, (ushort)ranks, 1, t.Specialized ? 3u : 2u, 0, t.Specialized ? 10u : 0u, 0, 0));
            }

            var vitals = ImmutableArray.CreateBuilder<VitalDesc>();
            vitals.Add(new VitalDesc(MageTemplate.MaxHealth, 100, 0, 0, 200));
            vitals.Add(new VitalDesc(MageTemplate.MaxStamina, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)!.Ranks, 0, 0, 200));
            vitals.Add(new VitalDesc(MageTemplate.MaxMana, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)!.Ranks, 0, 0, Mana));
            R.H.Describe(new PlayerDescriptionMsg
            {
                Attributes = attributes.ToImmutable(),
                Skills = skills.ToImmutable(),
                Vitals = vitals.ToImmutable(),
                Spells = known.ToImmutableDictionary(id => id, _ => 2f),
                Equipped = [new EquippedDesc(WieldedSetups.Wand.Guid, (uint)EquipMask.Held, 0)], // M4 3.1 g: the mage holds its wand, as the combat-mode guard needs for the magic mode
            });
            WieldedSetups.Create(R.H.R, WieldedSetups.Wand);
        }

        private void Regenerate(double now)
        {
            if (!double.IsNaN(_lastNow) && now > _lastNow)
            {
                _manaAcc += (now - _lastNow) * ManaRegenPerSecond;
                if (_manaAcc >= 1 && R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max))
                {
                    var whole = (uint)_manaAcc;
                    _manaAcc -= whole;
                    if (Mana < max)
                    {
                        Mana = Math.Min(max, Mana + whole);
                        SendMana();
                    }
                }
            }

            _lastNow = now;
        }

        private void SendMana()
        {
            var key = MageTemplate.MaxMana + 1; // the current's secondary attribute: 6 mana (0x02E9)
            _sequence[key] = (byte)(_sequence[key] + 1);
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_sequence[key], key, Mana));
        }

        private void Answer(uint spellId, uint target)
        {
            var shot = _script.Count > 0 ? _script.Dequeue() : Shot.Normal;
            switch (shot)
            {
                case Shot.OutOfRange:
                    R.UseDone(0x0550);
                    return;
                case Shot.Fizzle:
                    Mana = Mana >= 5 ? Mana - 5 : 0;
                    SendMana();
                    R.WeenieError(0x0402);
                    R.UseDone();
                    return;
                case Shot.NoProjectile:
                    R.UseDone();
                    return;
            }

            Mana = Mana >= ManaPerCast ? Mana - ManaPerCast : 0;
            SendMana();
            var boltId = Projectile();
            var creature = _creatures[target];
            if (shot is Shot.Resist or Shot.RanIn)
            {
                // The bolt flew 0.2 s at 20 m/s (4 m) and met the target, which had run to 6 m from the bolt's origin.
                var at = ReducerHarness.Position(R.H.R.State.Objects.TryGet(target, out var t) && t.Position is { } p ? p.CellId : Den, PlayerX, PlayerY + 6, 30);
                R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(target, PositionFlags.Grounded, at, null, null, 1, 50, 1, 1));
                _stops.Enqueue((boltId, Now + 0.2));
                if (shot == Shot.Resist)
                {
                    R.Raw(AceWireWriters.ServerMessage($"{creature.Name} resists your spell", 7));
                }

                R.UseDone();
                return;
            }

            if (shot == Shot.Wall)
            {
                _stops.Enqueue((boltId, Now + 0.2));
                R.UseDone();
                return;
            }

            if (shot == Shot.Dud)
            {
                R.UseDone();
                return;
            }

            if (HitDelay > 0)
            {
                _lateHits.Enqueue((spellId, target, shot, Now + HitDelay)); // the bolt is in flight: the UseDone ends the cast now, the hit lands later
            }
            else
            {
                Land(spellId, creature, shot);
            }

            R.UseDone();
        }

        /// <summary>The bolt lands: the creature's hit, and the kill (0x01AD, the deletion) or the damage line.</summary>
        private void Land(uint spellId, Creature creature, Shot shot)
        {
            var target = creature.Guid;
            creature.Hits++;
            if (shot == Shot.SilentKill || creature.Hits >= creature.HitsToKill)
            {
                creature.Dead = true;
                if (shot != Shot.SilentKill)
                {
                    R.Server((p, s) => AceM3Writers.KillerNotification(p, s, KillText ?? "You have killed " + creature.Name + "!"));
                }

                if (DeathDelay > 0 && shot != Shot.SilentKill)
                {
                    _late.Enqueue((target, Now + DeathDelay));
                }
                else
                {
                    R.Raw(AceWireWriters.ObjectDelete(target, 1));
                }
            }
            else
            {
                R.Raw(AceWireWriters.ServerMessage($"You scorch {creature.Name} for 40 points with {Table[spellId].Name}.", 7));
            }
        }

        private uint Projectile()
        {
            var id = _nextProjectile++;
            R.H.R.Reducer.OnCreateObject(
                R.H.R.Context,
                ReducerHarness.Create(id, ReducerHarness.Stamps(), ReducerHarness.Position(Den, PlayerX, PlayerY, 30), state: PhysicsState.Missile | PhysicsState.Gravity, velocity: new Vector3(0, 20, 0)),
                isUpdate: false);
            return id;
        }
    }

    private static int FlameBoltIndexFor(Rig r)
    {
        var self = r.R.H.State.Self;
        return FightScenario.ChooseLevel(self.Qualities, self.Spellbook, r.Buff.CastChance, out _, out _);
    }

    [Fact]
    public void A_tusker_in_range_dies_after_two_hits_and_the_scenario_ends_with_one_kill()
    {
        var r = new Rig();
        var tusker = r.Add(0x80001001, TuskerSlave, 0x01050303, 40); // 20 m from the player, in range 30
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.True(tusker.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(1, scenario.Tally.TargetKills); // the notice names "Tusker Slave"
        Assert.Equal(2, scenario.Tally.Casts);
        Assert.Equal(1, scenario.Tally.DamageLinesDealt); // the killing blow has no damage line (SpellProjectile.cs 857-861)
        Assert.Equal(40, scenario.Tally.DamageDealt);
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, scenario.Walks);
        Assert.Equal(2, scenario.Projectiles);
        Assert.Equal([0x80001001u, 0x80001001u], r.CastTargets);
        Assert.Equal([0x80001001u], r.HealthQueries); // after the hit that did not kill
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], r.ModesSent);
        Assert.Equal(CombatModes.NonCombat, r.R.H.State.Self.Qualities.CombatMode);

        var index = FlameBoltIndexFor(r);
        Assert.True(index >= 0);
        Assert.Equal(ArenaProfile.LineNamed("Flame Bolt")!.Spells[index].Id, scenario.SpellId);
        Assert.All(r.CastSpells, id => Assert.Equal(scenario.SpellId, id));
        Assert.Contains("kills=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("spells_cast=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(30f, scenario.RangeMetres);
    }

    [Fact]
    public void An_arena_here_run_skips_the_place_and_buff_stages_and_the_floor_rule_and_takes_its_level_from_the_arena_skill()
    {
        // M3 3.1 fd: the offline stand-in's arena. A tusker in the chest corner (off the floor, excluded from the den's rule) is a target; the stages the host did not ask for do not run even with FightStages.All.
        var options = new FightOptions { ArenaHere = true, ArenaSkill = 250 };
        var r = new Rig(warRanks: 0, fight: options);
        var tusker = r.Add(0x80001001, TuskerSlave, DeadChestCorner, 40);
        var scenario = r.Scenario(null, FightStages.All);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.True(tusker.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Walks);
        var stages = r.Log.Lines.Where(l => l.Contains("stage ", StringComparison.Ordinal) && l.Contains("[fight]", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("stage ", StringComparison.Ordinal) + 6)..].Trim()).ToArray();
        Assert.Equal(["buffs", "place", "mode-magic", "fight", "mode-peace"], stages); // no leave-den: the host put the player here and keeps it here
        Assert.Empty(r.Places);
        Assert.Null(scenario.Gear);
        var index = FightScenario.ChooseLevel(options.ArenaSkill, null, r.Buff.CastChance, out _); // the arena-here run reads no spellbook
        Assert.True(index >= 0);
        Assert.Equal(index, FightScenario.ChooseLevel(options.ArenaSkill, r.R.H.State.Self.Spellbook, r.Buff.CastChance, out _)); // the rig knows every level
        Assert.Equal(ArenaProfile.LineNamed("Flame Bolt")!.Spells[index].Id, scenario.SpellId);
        Assert.Equal(30f, scenario.RangeMetres);
    }

    [Fact]
    public void The_level_from_a_known_skill_needs_the_spellbook_and_a_castable_chance()
    {
        var r = new Rig();
        var book = r.R.H.State.Self.Spellbook;

        Assert.Equal(-1, FightScenario.ChooseLevel(0, book, r.Buff.CastChance, out var why0));
        Assert.Contains("chance", why0, StringComparison.Ordinal);
        var low = FightScenario.ChooseLevel(100, book, r.Buff.CastChance, out _);
        var high = FightScenario.ChooseLevel(500, book, r.Buff.CastChance, out _);
        Assert.True(high >= low);
        Assert.Equal(FightScenario.Levels - 1, high);
    }

    [Fact]
    public void Each_stage_is_logged_once_in_order()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        var stages = r.Log.Lines.Where(l => l.Contains("stage ", StringComparison.Ordinal) && l.Contains("[fight]", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("stage ", StringComparison.Ordinal) + 6)..].Trim()).ToArray();
        Assert.Equal(["buffs", "place", "mode-magic", "fight", "mode-peace", "leave-den"], stages);
    }

    [Fact]
    public void A_finished_fight_leaves_the_den_before_it_ends()
    {
        // The next run (login-death after login-bsd in the smoke, GearLiveTests after FightLiveTests in the loopback order) must not log BSD Mage in among the tuskers: after mode-peace the run places the player at Holtburg,
        // settles, and only then ends (done here; the log off in the headless run).
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        var place = Assert.Single(r.Places);
        Assert.Contains("poi", place, StringComparison.Ordinal);
        Assert.Contains("Holtburg", place, StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, r.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Contains(r.Log.Lines, l => l.Contains("leaving the den before the log off", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("left the den: the player is present in cell 0xA9B40019", StringComparison.Ordinal));
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], r.ModesSent);
        var kinds = r.R.H.Net.Kinds();
        Assert.True(kinds.LastIndexOf(FakeSendKind.ChangeCombatMode) < kinds.LastIndexOf(FakeSendKind.Talk), "the leave goes out after the return to non-combat");
    }

    [Fact]
    public void A_failed_fight_leaves_the_den_and_only_then_ends_failed()
    {
        // endSession: false is the game's leg (FightLegRunner), which logs off as soon as the scenario returns Failed; a Failed returned in the den left BSD Mage on the arrival floor for the next login. The failure is set at
        // once; the run stays Running until the leave is over, and sends no log off of its own.
        var r = new Rig(fight: new FightOptions { Seconds = 20 });
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // nothing to fight: the seconds limit fails the run in the den
        var scenario = r.Scenario(new FakeWalk());

        var status = ScenarioStatus.Running;
        var failedAt = double.NaN;
        for (var i = 0; i < 2000 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario);
            if (double.IsNaN(failedAt) && scenario.Failure is not null)
            {
                failedAt = r.Now;
                Assert.Equal(ScenarioStatus.Running, status); // not Failed in the den: the leave runs first
            }
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("20 s passed with kills=0 of 1", scenario.Failure, StringComparison.Ordinal);
        Assert.InRange(r.Now - failedAt, DenExit.SettleSeconds, DenExit.SettleSeconds + 1); // the rig lands the player at once; then the settle
        var place = Assert.Single(r.Places);
        Assert.Contains("poi", place, StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, r.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.LogOff)); // the host logs off, not the scenario
    }

    [Fact]
    public void A_cast_with_no_projectile_is_a_miss_and_three_in_a_row_move_to_the_next_waypoint()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(3, scenario.Tally.Misses);
        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, scenario.Walks);
        Assert.Equal(5, scenario.Tally.Casts); // three misses, then the two hits of the kill after the walk
        Assert.Equal(1, scenario.Tally.Kills);
        var lines = r.Log.Lines.Where(l => l.Contains("[fight]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, lines.Count(l => l.Contains("miss: the cast was not confirmed", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.Contains("walking: 3 misses in a row; fake leg 1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("the leg is done", StringComparison.Ordinal));
    }

    [Fact]
    public void A_blocked_target_is_not_cast_at_the_loop_walks_at_once_with_no_miss()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 0, 1), (scenario.Tally.BlockedPicks, scenario.Tally.Misses, walk.Legs));
        Assert.Equal(2, scenario.Tally.Casts); // the two hits of the kill, after the leg: none before it
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: no tusker has a clear line", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("blocked: no clear line to 0x80001001", StringComparison.Ordinal));
    }

    [Fact]
    public void The_next_nearest_tusker_with_a_clear_line_is_cast_at_and_the_blocked_nearer_one_is_skipped()
    {
        var r = new Rig();
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 30);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050310, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, walk.Legs);
        Assert.All(r.CastTargets, t => Assert.Equal(far.Guid, t));
        Assert.False(near.Dead);
        Assert.True(scenario.Tally.BlockedPicks >= 1);
    }

    [Fact]
    public void Fight_ignore_line_restores_the_nearest_pick()
    {
        var r = new Rig(fight: new FightOptions { IgnoreLine = true });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((0, 0, 0), (scenario.Tally.BlockedPicks, walk.LineQueries, walk.Legs));
    }

    [Fact]
    public void A_bolt_that_stops_short_of_the_target_with_no_damage_line_is_a_wall_hit_and_walks_on_the_first_one()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.Wall);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 1, 1), (scenario.Tally.WallHits, scenario.Tally.Misses, walk.Legs));
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: a bolt hit a wall", StringComparison.Ordinal));
    }

    [Fact]
    public void A_resisted_bolt_at_a_target_that_ran_at_the_mage_is_a_miss_not_a_wall_hit() => BoltBesideTarget(Shot.Resist, 1);

    [Fact]
    public void A_bolt_that_stops_beside_a_target_that_ran_at_the_mage_with_no_resist_line_is_a_miss_not_a_wall_hit() => BoltBesideTarget(Shot.RanIn, 0);

    private static void BoltBesideTarget(Shot shot, int resists)
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(shot, Shot.Normal, Shot.Normal);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((0, 1, 0, resists), (scenario.Tally.WallHits, scenario.Tally.Misses, walk.Legs, scenario.Tally.Resists));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("a bolt hit a wall", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20.0, 20.0, 0.2, true)] // the first-cast wall: 16 m short
    [InlineData(6.0, 20.0, 0.2, false)] // the target ran in: 2 m short, inside the radius and the margin
    [InlineData(20.0, 20.0, 1.0, false)] // flew all the way
    [InlineData(20.0, 20.0, 0.75, true)] // 5 m short: a wall, however the share is read
    public void The_wall_rule_is_the_stop_distance_from_the_target(double toTarget, double speed, double seconds, bool wall) =>
        Assert.Equal(wall, FightScenario.StoppedShortOfTarget(toTarget, speed, seconds));

    [Fact]
    public void The_fight_ignore_line_option_parses()
    {
        Assert.False(new FightOptions().IgnoreLine);
        Assert.True(FightOptions.FromArguments([new CommandLineArgument("--fight-ignore-line", "--fight-ignore-line", string.Empty, false)]).IgnoreLine);
    }

    [Fact]
    public void Two_misses_and_a_hit_do_not_walk()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.NoProjectile, Shot.NoProjectile);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((2, 0, 0), (scenario.Tally.Misses, walk.Legs, scenario.Walks));
    }

    [Fact]
    public void A_confirmed_projectile_with_no_damage_line_in_three_seconds_is_a_miss()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.Dud);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Misses);
        Assert.Equal(3, scenario.Tally.Casts);
        Assert.Contains(r.Log.Lines, l => l.Contains("no damage line or kill came within 3 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_target_sixty_metres_away_whose_damage_line_arrives_at_four_seconds_is_a_hit_of_the_first_cast_not_a_miss_and_a_hit_of_the_second()
    {
        // The rig's bolt flies at 20 m/s (its CreateObject velocity): 60 m is 3 s of flight, so the hit wait is 5 s and the line at 4 s is cast 1's (package 2, finding 6: at the old fixed 3 s it was a miss, then cast 2's hit).
        var r = new Rig(range: 75f) { HitDelay = 4 };
        r.Add(0x80001001, TuskerSlave, 0x01050303, PlayerY + 60);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(2, scenario.Tally.Casts);
        Assert.Equal(1, scenario.Tally.DamageLinesDealt);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(2, r.Log.Lines.Count(l => l.Contains("the target 0x80001001 60.0 m from it: hit wait 5 s", StringComparison.Ordinal)));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("no damage line or kill came within", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20.0, 20f, 3.0)] // a close target: the floor
    [InlineData(60.0, 20f, 5.0)] // the rig's bolt at 60 m: 3 s of flight plus the margin
    [InlineData(75.0, 15f, 7.0)] // the cast range's cap at a live Flame Bolt's speed
    [InlineData(75.0, 0.5f, 10.0)] // not a speed to divide by: the cap
    [InlineData(1000.0, 15f, 10.0)] // past the cap
    public void The_hit_wait_is_the_flight_time_plus_two_seconds_between_three_and_ten(double distance, float speed, double wait)
    {
        Assert.Equal(wait, FightScenario.HitWaitFor(distance, speed), 6);
        Assert.Equal((3.0, 2.0, 10.0, 1f), (FightScenario.HitWaitSeconds, FightScenario.HitWaitMarginSeconds, FightScenario.HitWaitMaxSeconds, FightScenario.MinProjectileSpeed));
    }

    [Fact]
    public void An_out_of_range_answer_walks_at_once()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.OutOfRange);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Misses);
        Assert.Equal(1, walk.Legs);
        Assert.Contains(r.Log.Lines, l => l.Contains("MissileOutOfRange (0x0550)", StringComparison.Ordinal));
    }

    [Fact]
    public void No_target_in_range_walks_and_a_creature_beyond_the_range_is_not_cast_at()
    {
        var r = new Rig();
        var far = r.Add(0x80001001, TuskerSlave, 0x01050303, 80); // 60 m, range 30
        var walk = new FakeWalk { TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 40 && walk.Legs == 0; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        Assert.Equal(1, walk.Legs);
        Assert.Empty(r.CastTargets);
        Assert.False(far.Dead);

        // After the leg the tusker has come within range (the test moves it): the loop casts again.
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(far.Guid, 1, 2, 1, 1, cell: 0x01050303, x: PlayerX));
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void A_virindi_a_drudge_and_a_tusker_in_the_chest_corner_are_never_targeted()
    {
        var r = new Rig();
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // 5 m, on the floor but not a tusker
        r.Add(0x80001002, 1610, 0x01050303, 26, "Drudge Ravener");
        r.Add(0x80001003, 237, 0x01050303, 27, "Virindi Master");
        r.Add(0x80001004, TuskerSlave, DeadChestCorner, 28); // a tusker off the floor (the chest corner is excluded)
        r.Add(0x80001005, 1629, 0x01050154, 29, "Tusker Guard"); // the north wing's cell
        var good = r.Add(0x80001006, 1629, 0x010502E1, 45, "Tusker Guard"); // 25 m, the only legal target
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.NotEmpty(r.CastTargets);
        Assert.All(r.CastTargets, t => Assert.Equal(good.Guid, t));
        Assert.True(good.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void The_seconds_limit_ends_the_fight_loudly_with_the_counts()
    {
        var r = new Rig(fight: new FightOptions { Seconds = 20 });
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // nothing to fight: the loop keeps walking
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.NotNull(scenario.Failure);
        Assert.Contains("20 s passed with kills=0 of 1", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("walks=", scenario.Failure, StringComparison.Ordinal);
        Assert.True(walk.Legs >= 2, "the loop walked leg after leg: " + walk.Legs);
        Assert.Empty(r.CastTargets);
        Assert.Contains(r.Log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal) && l.Contains("20 s passed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_players_death_ends_the_fight_loudly()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 5);
        var scenario = r.Scenario(new FakeWalk());
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 80 && scenario.Tally.Casts == 0; i++)
        {
            status = r.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        r.Die();
        status = r.Run(scenario, 10);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("the player died", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal) && l.Contains("the player died", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fizzle_drops_one_level_once()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 3);
        r.Script(Shot.Fizzle, Shot.Fizzle);
        var scenario = r.Scenario(new FakeWalk());
        var first = FlameBoltIndexFor(r);
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(spells[first].Id, r.CastSpells[0]);
        Assert.Equal(spells[first - 1].Id, r.CastSpells[1]); // one level down after the fizzle
        Assert.All(r.CastSpells.Skip(1), id => Assert.Equal(spells[first - 1].Id, id)); // and no further drop for the second fizzle
        Assert.Equal(2, scenario.Tally.Misses);
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("one level down", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_fizzle_drops_to_the_next_lower_level_the_spellbook_knows_and_the_range_follows_it()
    {
        // A book that knows the chosen level and the one two below it, not the one between (package 2, finding 16): the drop skips the unknown level and takes the lower level's own range.
        var all = new Rig();
        var first = FlameBoltIndexFor(all);
        Assert.True(first >= 2, "the default rig must choose level III or higher for this test");
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;
        var r = new Rig(known: [.. all.Table.Keys.Where(id => id != spells[first - 1].Id)]);
        r.Table[spells[first - 2].Id] = r.Table[spells[first - 2].Id] with { BaseRangeConstant = 20f }; // the lower level reaches less far
        r.Add(0x80001001, TuskerSlave, 0x01050303, PlayerY + 15, hitsToKill: 2);
        r.Script(Shot.Fizzle);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(spells[first].Id, r.CastSpells[0]);
        Assert.Equal(spells[first - 2].Id, r.CastSpells[1]); // not the unknown level between
        Assert.All(r.CastSpells.Skip(1), id => Assert.Equal(spells[first - 2].Id, id));
        Assert.Equal(20f, scenario.RangeMetres);
        Assert.Contains(r.Log.Lines, l => l.Contains("one level down, to " + spells[first - 2].Name + " (" + spells[first - 2].Id.ToString(CultureInfo.InvariantCulture) + "), the next lower level the spellbook knows; range 20 m", StringComparison.Ordinal));
        Assert.Contains(" level=" + new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[first - 2] + " range_m=20 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_is_the_highest_known_one_whose_chance_reaches_the_threshold()
    {
        // The highest level a skill reaches at 0.9 by the buff cycle's code, then only the levels the spellbook holds.
        var all = new Rig();
        var allIndex = FlameBoltIndexFor(all);
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;
        Assert.InRange(allIndex, 0, FightScenario.Levels - 1);
        Assert.True(GearCasts.TryCurrentSkill(all.R.H.State.Self.Qualities, MageTemplate.WarMagic, out var skill, out _));
        Assert.True(GearCasts.Chance(skill, spells[allIndex].Power) >= 0.9);
        if (allIndex < FightScenario.Levels - 1)
        {
            Assert.True(GearCasts.Chance(skill, spells[allIndex + 1].Power) < 0.9);
        }

        var onlyLow = new Rig(known: [spells[0].Id, spells[1].Id]);
        Assert.Equal(1, FlameBoltIndexFor(onlyLow));

        var none = new Rig(known: []);
        Assert.Equal(-1, FlameBoltIndexFor(none));
        none.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = none.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Failed, none.Run(scenario));
        Assert.Contains("no Flame Bolt level to cast", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(none.CastTargets);
    }

    [Fact]
    public void The_range_is_aces_with_the_skills_init_plus_ranks_and_capped()
    {
        var info = WorldActionRig.Spell(1, SpellType.Projectile, SpellFlags.Projectile) with { BaseRangeConstant = 5f, BaseRangeMod = 0.02f };
        Assert.Equal(13f, FightScenario.CastRange(info, 400), 3);
        var wide = info with { BaseRangeConstant = 50f, BaseRangeMod = 0.1f };
        Assert.Equal(FightScenario.MaxRange, FightScenario.CastRange(wide, 400));
        Assert.Equal(75f, FightScenario.MaxRange);
    }

    [Fact]
    public void Two_kills_are_wanted_and_two_tuskers_are_cast_at_nearest_first()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2 });
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(near.Dead && far.Dead);
        Assert.Equal(2, scenario.Tally.Kills);
        Assert.Equal([near.Guid, near.Guid, far.Guid, far.Guid], r.CastTargets);
    }

    [Fact]
    public void A_first_hit_that_kills_leaves_damage_dealt_at_zero_so_the_fight_goes_on_to_the_next_tusker_for_a_damage_line()
    {
        // 3.1-R1: ACE writes no damage line for the killing blow, the judge wants damage_dealt > 0, so one kill is not the end while no line has been seen.
        var r = new Rig(fight: new FightOptions { Kills = 1 });
        var first = r.Add(0x80001001, TuskerSlave, 0x01050303, 35, hitsToKill: 1);
        var second = r.Add(0x80001002, TuskerSlave, 0x01050303, 45, hitsToKill: 2);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(first.Dead);
        Assert.Equal(1, scenario.Tally.Kills); // the wanted kill was made by the first cast, with no damage line: the loop went on, to the second tusker's first hit, and stopped there
        Assert.True(scenario.Tally.DamageDealt > 0);
        Assert.Equal(40, scenario.Tally.DamageDealt);
        Assert.Equal([first.Guid, second.Guid], r.CastTargets);
        Assert.Contains(r.Log.Lines, l => l.Contains("damage_dealt=0", StringComparison.Ordinal) && l.Contains("fighting on until a damage line", StringComparison.Ordinal));
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("fighting on until a damage line", StringComparison.Ordinal))); // logged once
    }

    [Fact]
    public void A_one_shot_kill_with_no_other_tusker_fails_at_the_seconds_limit_naming_the_missing_damage()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Seconds = 40 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35, hitsToKill: 1);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.DamageDealt);
        Assert.Contains("kills=1 of 1 and damage_dealt=0", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dying_tusker_is_not_picked_again_while_its_dead_motion_and_deletion_are_still_coming()
    {
        // 3.1-R2: ACE sends the 0x01AD, then the Dead motion a moment later, and keeps the body for its death animation. Two seconds of that, and the loop must go to the other tusker at once.
        var r = new Rig(fight: new FightOptions { Kills = 2 }) { DeathDelay = 2.0 };
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(near.Dead && far.Dead);
        Assert.Equal(2, scenario.Tally.Kills);
        Assert.Equal([near.Guid, near.Guid, far.Guid, far.Guid], r.CastTargets); // no cast at the dying near tusker after its kill
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, scenario.Walks);
    }

    [Fact]
    public void A_creature_whose_motion_is_dead_or_that_the_caller_excluded_is_never_nearest()
    {
        var r = new Rig();
        var table = r.R.H.State.Objects;
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var self = r.R.H.State.Self.Position!.Value;

        Assert.True(table.NearestCreature(ArenaProfile.TargetWcids, in self, 30f, null, [0x80001001u], out var guid));
        Assert.Equal(0x80001002u, guid);

        r.R.Raw(AceWireWriters.UpdateMotion(0x80001001, 1, 2, 2, false, new AceMovementData { Forward = 0x0011 }));
        Assert.True(table.NearestCreature(ArenaProfile.TargetWcids, in self, 30f, null, out guid));
        Assert.Equal(0x80001002u, guid);
    }

    [Fact]
    public void A_kill_that_does_not_name_the_target_counts_in_kills_but_not_in_target_kills()
    {
        // 3.1-R7: report-only; the judged kills stays the total.
        var r = new Rig { KillText = "You have killed Somebody Else!" };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.TargetKills);
        Assert.False(scenario.Tally.LastKillNamedTarget);
        Assert.Contains(" kills=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" target_kills=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_walker_that_left_the_floor_fails_the_fight_at_once_in_any_phase_with_the_walks_reason()
    {
        // 3.1-R4: the walk reports Failed from its floor check every frame; the scenario reads it in the Cast and Hit phases too, not only while a leg is walking.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        while (scenario.Stage != "fight")
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        walk.LeaveFloor = "the walker left the den's floor: it is in cell 0x01050341";
        var status = r.Frame(scenario);
        Assert.Contains("the walker left the den's floor", scenario.Failure, StringComparison.Ordinal); // the failure is set in that frame
        Assert.Equal(ScenarioStatus.Running, status); // and the run leaves the den before it ends Failed
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 30));
        Assert.Single(r.Places);
        Assert.Equal(0, scenario.Walks);
    }

    [Fact]
    public void A_settle_off_the_floor_is_a_loud_failure_and_a_settle_on_it_goes_on()
    {
        // 3.1-R4 / D12: the arrival cell comes from the server's position. 0x01050341 is the exit portal's room.
        var off = new Rig { ArrivalCell = 0x01050341 };
        off.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var offScenario = off.Scenario(new FakeWalk(), FightStages.Place | FightStages.Mode);
        var offStatus = off.Run(offScenario, 60);
        Assert.True(offStatus == ScenarioStatus.Failed, string.Join(" | ", off.Log.Lines));
        Assert.Contains("settle: the player settled in cell 0x01050341", offScenario.Failure, StringComparison.Ordinal);

        var on = new Rig { ArrivalCell = 0x01050375 };
        on.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var onScenario = on.Scenario(new FakeWalk(), FightStages.Place | FightStages.Mode);
        Assert.Equal(ScenarioStatus.Succeeded, on.Run(onScenario, 120));
        Assert.Contains(on.Log.Lines, l => l.Contains("settled in cell 0x01050375; floor cell yes", StringComparison.Ordinal));
    }

    [Fact]
    public void A_target_that_dies_without_a_kill_notice_is_left_after_two_seconds_and_not_counted()
    {
        var r = new Rig(fight: new FightOptions { Seconds = 40 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.SilentKill);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.Equal(0, scenario.Tally.Kills);
        Assert.Contains(r.Log.Lines, l => l.Contains("no kill notice came within 2 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_walk_that_cannot_be_planned_fails_the_fight_loudly_and_a_missing_walker_is_a_named_todo()
    {
        var r = new Rig();
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var scenario = r.Scenario(new FakeWalk { FailNext = true });
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));
        Assert.Contains("no walk could be planned", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the fake walk is cut", scenario.Failure, StringComparison.Ordinal);

        var none = new Rig();
        none.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var withoutWalker = none.Scenario(null);
        Assert.Equal(ScenarioStatus.Failed, none.Run(withoutWalker));
        Assert.Contains("TODO(m3-fight-no-walker)", withoutWalker.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cast_that_needs_more_mana_waits_for_regeneration_and_logs_it_once()
    {
        var r = new Rig(mana: 5) { ManaRegenPerSecond = 2 };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Contains(r.Log.Lines, l => l.Contains("waiting for mana", StringComparison.Ordinal));
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void The_loop_allocates_nothing_while_it_waits_for_mana_for_a_hit_or_for_a_leg()
    {
        // Waiting for mana.
        var mana = new Rig(mana: 1) { ManaRegenPerSecond = 0 };
        mana.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var waiting = mana.Scenario(new FakeWalk());
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, mana.Frame(waiting));
        }

        Assert.Empty(mana.CastTargets);
        Assert.Equal(0, Allocated(mana, waiting));

        // Waiting for a hit that never comes (a projectile with no damage line): the three-second window.
        var hit = new Rig { CastSeconds = 0.1 };
        hit.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        hit.Script(Shot.Dud);
        var hitting = hit.Scenario(new FakeWalk());
        for (var i = 0; i < 60 && hit.CastTargets.Count == 0; i++)
        {
            hit.Frame(hitting);
        }

        hit.Frame(hitting, 0.25);
        hit.Frame(hitting, 0.25);
        Assert.Equal(0, Allocated(hit, hitting, 0.01, 100)); // one second of the 3 s window

        // Walking a leg.
        var walk = new Rig();
        walk.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var legs = new FakeWalk { TicksPerLeg = int.MaxValue };
        var walking = walk.Scenario(legs);
        for (var i = 0; i < 40 && legs.Legs == 0; i++)
        {
            walk.Frame(walking);
        }

        Assert.Equal(1, legs.Legs);
        Assert.Equal(0, Allocated(walk, walking));
    }

    private static long Allocated(Rig rig, FightScenario scenario, double dt = 0.01, int frames = 300)
    {
        scenario.Step(rig.View, rig.Now);
        long allocated = 0;
        for (var i = 0; i < frames; i++)
        {
            rig.R.H.R.Time.Advance(TimeSpan.FromSeconds(dt));
            var before = GC.GetAllocatedBytesForCurrentThread();
            scenario.Step(rig.View, rig.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }

    [Fact]
    public void The_target_rule_the_loop_filters_by_keeps_the_three_named_creatures_out()
    {
        // The rule the filter applies is BlackSpawnDen's: pinned here at the three cells the brief names, so a change to the set cannot silently let them in.
        Assert.False(BlackSpawnDen.IsTarget(23, 0x01050303));
        Assert.False(BlackSpawnDen.IsTarget(1610, 0x01050303));
        Assert.False(BlackSpawnDen.IsTarget(TuskerSlave, DeadChestCorner));
        Assert.True(BlackSpawnDen.IsTarget(TuskerSlave, 0x01050303));
        Assert.True(BlackSpawnDen.IsTarget(1629, 0x010502E1));
    }

    [Fact]
    public void The_fight_options_parse_and_the_defaults_are_one_kill_and_five_minutes()
    {
        Assert.Equal((1, 300.0), (new FightOptions().Kills, new FightOptions().Seconds));
        var parsed = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-kills=4", "--fight-seconds=90"]));
        Assert.Empty(parsed.Errors);
        Assert.Equal((4, 90.0), (parsed.Kills, parsed.Seconds));
        var bad = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-kills=0", "--fight-seconds=abc"]));
        Assert.Equal(2, bad.Errors.Count);
        Assert.Equal((1, 300.0), (bad.Kills, bad.Seconds));
    }

    private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
        new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);
}

/// <summary>
/// The den's floor from the dats (M3 3.1 lane fa): <see cref="BlackSpawnDen.FloorLowWords"/> is the arrival's connected floor (cells at z -1 to 0.5 a standing human walks to from the arrival through the doorways
/// <see cref="DenPlanner.Passable"/> accepts, less the exit portal's room and the north wing), never a cell below z -1 nor an excluded one; every waypoint of the walk's tour is on it and reachable from the
/// arrival; and why <see cref="DenPlanner"/> exists (the stock planner reaches one spawn cell of twelve). Skipped without the dats.
/// </summary>
public sealed class BlackSpawnDenFloorDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private LandblockCellSet All() => LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(1, 5));

    [RequiresDatsFact]
    public void The_floor_set_is_the_arrivals_connected_floor_read_from_the_cell_graph()
    {
        var cells = All();
        var planner = new DenPlanner(cells);
        var seen = new HashSet<uint> { Den() };
        var queue = new Queue<uint>([Den()]);
        while (queue.Count > 0)
        {
            var cell = cells.GetCell(queue.Dequeue())!;
            foreach (var portal in cell.Portals)
            {
                var other = cells.GetCell(portal.OtherCellId);
                if (portal.LeadsOutside || other is null || seen.Contains(other.Id) || BlackSpawnDen.IsExcluded(other.Id & 0xFFFF)
                    || other.Frame.Origin.Z < -1f || other.Frame.Origin.Z > 0.5f || !planner.Passable(cell, portal))
                {
                    continue;
                }

                seen.Add(other.Id);
                queue.Enqueue(other.Id);
            }
        }

        var expected = seen.Select(c => (ushort)(c & 0xFFFF)).Order().ToArray();
        var pinned = BlackSpawnDen.FloorLowWords.Order().ToArray();
        output.WriteLine($"the floor: {pinned.Length} cells; recomputed {expected.Length}");
        Assert.Equal(expected, pinned);
        Assert.Equal(158, pinned.Length);
    }

    [RequiresDatsFact]
    public void No_floor_cell_is_below_z_minus_1_or_excluded_and_the_exit_portals_room_is_out()
    {
        var cells = All();
        foreach (var low in BlackSpawnDen.FloorLowWords)
        {
            var cell = cells.GetCell(0x01050000u | low);
            Assert.NotNull(cell);
            Assert.InRange(cell.Frame.Origin.Z, -1f, 0.5f);
            Assert.False(BlackSpawnDen.IsExcluded(low), $"0x{low:X4}");
            Assert.True(BlackSpawnDen.IsFloorCell(0x01050000u | low), $"0x{low:X4}");
        }

        foreach (var out_ in new uint[] { 0x0341, 0x0342, 0x0343, 0x035C })
        {
            Assert.DoesNotContain((ushort)out_, BlackSpawnDen.FloorLowWords);
            Assert.False(BlackSpawnDen.IsFloorCell(0x01050000u | out_));
        }
    }

    [RequiresDatsFact]
    public void Every_tour_waypoint_is_a_floor_cell_and_reachable_from_the_arrival_over_the_floor()
    {
        var floor = DenPlanner.FloorOf(All());
        Assert.Equal(BlackSpawnDen.FloorLowWords.Count, floor.Count);
        var planner = new DenPlanner(floor);
        Assert.Equal(12, DenWalk.Tour.Count);
        Assert.Equal(DenWalk.Tour.Order(), BlackSpawnDen.FloorLowWords.Skip(1).Take(12).Order()); // the tour is the twelve spawn cells
        foreach (var low in DenWalk.Tour)
        {
            var cellId = 0x01050000u | low;
            var route = planner.Plan("test", BlackSpawnDen.Arrival, cellId, DenWalk.StopWithinMetres);
            Assert.NotNull(route);
            Assert.True(route.Goals.Count >= 1);
            Assert.Equal(cellId, route.Goals[^1].Target.CellId);
            var path = planner.FindPath(BlackSpawnDen.Arrival.CellId, cellId)!;
            Assert.All(path, c => Assert.True(BlackSpawnDen.IsFloorCell(c), $"0x{c:X8} on the way to 0x{cellId:X8}"));
            output.WriteLine($"0x{low:X4}: {path.Count - 1} doorways, {route.Goals.Count} goals");
        }
    }

    [RequiresDatsFact]
    public void The_stock_planner_reaches_one_spawn_cell_of_twelve_because_the_dens_doorways_lean()
    {
        var floor = DenPlanner.FloorOf(All());
        var stock = new CellRoutePlanner(floor);
        var reached = DenWalk.Tour.Count(low => stock.FindPath(BlackSpawnDen.Arrival.CellId, 0x01050000u | low) is not null);
        output.WriteLine($"CellRoutePlanner reaches {reached} of 12 spawn cells; DenPlanner reaches 12 (the doorways' normals have a vertical part of 0.196, the stock rule wants under 0.1)");
        Assert.Equal(1, reached); // 0x0383 only: TODO(m3-fight-planner-lean)
    }

    [RequiresDatsFact]
    public void Flame_Bolt_levels_one_to_seven_are_the_dats_ids_in_the_war_school()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var line = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!;
        uint[] ids = [27, 81, 82, 83, 84, 85, 2128];
        Assert.Equal(ids, line.Spells.Take(FightScenario.Levels).Select(s => s.Id));
        string[] numerals = ["I", "II", "III", "IV", "V", "VI"];
        for (var i = 0; i < ids.Length; i++)
        {
            var spell = spells[ids[i]];
            Assert.Equal(MagicSchool.WarMagic, spell.School);
            Assert.Equal(SpellType.Projectile, spell.MetaSpellType);
            Assert.Equal(line.Spells[i].Power, spell.Power);
            Assert.Equal(i < 6 ? "Flame Bolt " + numerals[i] : "Ilservian's Flame", spell.Name);
            output.WriteLine($"{spell.Name} ({spell.Id}): power {spell.Power}, base mana {spell.BaseMana}, range {spell.BaseRangeConstant} + {spell.BaseRangeMod} * skill");
        }
    }

    private static uint Den() => ArenaProfile.ArenaCell;
}
