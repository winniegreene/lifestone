using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Contracts;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.Tests.Session.Threat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M6 16 Eperf: what the one engine's shadows and readers cost a frame, measured (docs/automation-engine.md section 7). Each row is one call on a state like the climber's in a dungeon: a room of thirty creatures, five of
/// them heard attacking her, drawn bodies, a table of about 250 objects; a main pack of a hundred items with casters, stones, appraised armour, scrolls and trade goods. A row is the median of <see cref="Batches"/> batches,
/// with the fastest and the slowest batch beside it (the spread says how noisy the machine was), and the bytes one call allocates on this thread.
/// <para>Timing tier: the lead (or a slice that changes one of these paths) runs it alone,
/// <c>dotnet test tests/Lifestone.Client.Tests --no-build --filter "FullyQualifiedName~EngineFrameCost" --logger "console;verbosity=detailed"</c>, and reads the table from the test's output. It asserts nothing about the
/// times (a machine under load would fail it for no fault of the code); it asserts that every measured call did its work.</para>
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
[Trait("Tier", "Timing")]
public sealed class EngineFrameCostTests(ITestOutputHelper output, DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const int Batches = 15;
    private const string Guard = "Tusker Guard";
    private const string Stranger = "Shadow Lieutenant";
    private const uint Armor = 0x2, Jewelry = 0x8, Misc = 0x80, Gem = 0x800, Scroll = 0x2000, CasterType = 0x8000, Stone = 0x80000;

    private readonly List<string> _rows = [];

    // ------------------------------------------------------------------------------------------------ the measure

    /// <summary>One row: microseconds a call (median, fastest batch, slowest batch) and bytes a call (median).</summary>
    private readonly record struct Cost(double Micros, double Fastest, double Slowest, double Bytes);

    /// <summary>
    /// Times <paramref name="call"/>: <see cref="Batches"/> batches of <paramref name="perBatch"/> calls after one batch to warm up. With <paramref name="before"/>, each call is timed alone and <paramref name="before"/> runs
    /// untimed ahead of it (a state change that defeats a cache).
    /// </summary>
    private static Cost Measure(Action call, int perBatch, Action? before = null)
    {
        for (var i = 0; i < perBatch; i++)
        {
            before?.Invoke();
            call();
        }

        var micros = new double[Batches];
        var bytes = new double[Batches];
        for (var b = 0; b < Batches; b++)
        {
            long ticks = 0, allocated = 0;
            if (before is null)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                for (var i = 0; i < perBatch; i++)
                {
                    call();
                }

                ticks = Stopwatch.GetTimestamp() - t0;
                allocated = GC.GetAllocatedBytesForCurrentThread() - a0;
            }
            else
            {
                for (var i = 0; i < perBatch; i++)
                {
                    before();
                    var a0 = GC.GetAllocatedBytesForCurrentThread();
                    var t0 = Stopwatch.GetTimestamp();
                    call();
                    ticks += Stopwatch.GetTimestamp() - t0;
                    allocated += GC.GetAllocatedBytesForCurrentThread() - a0;
                }
            }

            micros[b] = ticks * 1_000_000.0 / Stopwatch.Frequency / perBatch;
            bytes[b] = allocated / (double)perBatch;
        }

        Array.Sort(micros);
        Array.Sort(bytes);
        return new Cost(micros[Batches / 2], micros[0], micros[^1], bytes[Batches / 2]);
    }

    /// <summary>Adds a row: the suspect, what one call cost, how often it runs a second (<paramref name="perSecond"/>, with <paramref name="when"/> saying where that number comes from), and so its share of a second.</summary>
    private Cost Row(string suspect, Cost c, double perSecond, string when)
    {
        var ci = CultureInfo.InvariantCulture;
        _rows.Add(string.Create(ci, $"| {suspect} | {c.Micros:0.00} ({c.Fastest:0.00} to {c.Slowest:0.00}) | {c.Bytes:0} | {perSecond:0.##} ({when}) | {c.Micros * perSecond / 1000.0:0.000} | {c.Bytes * perSecond / 1024.0:0.00} |"));
        return c;
    }

    private void Print(string title)
    {
        output.WriteLine(title);
        output.WriteLine("| Suspect | us a call: median (fastest to slowest batch) | Bytes a call | Calls a second | ms a second | KiB a second |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var row in _rows)
        {
            output.WriteLine(row);
        }
    }

    /// <summary>The frames a second the per-frame rows are scaled by: the engine's default frame cap with the display on. The live run's own rate is the soak line's <c>frames=</c> (this slice adds it).</summary>
    private const double Fps = 60;

    // ------------------------------------------------------------------------------------------------ the room

    /// <summary>The room: thirty creatures (five guards on her, ten more targets farther off, fifteen strangers), a hundred held items and a hundred things on the floor, every creature with a drawn body.</summary>
    private sealed class Room
    {
        public ThreatRig T { get; } = new();

        public ScriptedBodies Bodies { get; } = new();

        public IGameSessionView View { get; }

        public List<uint> OnHer { get; } = [];

        public Room()
        {
            var t = T;
            var here = t.View.State.Self.Position!.Value;
            for (var i = 0; i < 5; i++)
            {
                var g = t.Creature(Guard, t.Listed, 2 + i);
                OnHer.Add(g);
                Bodies.StandEast(g, here, 1.5f + (i * 0.5f)); // the body the client draws stands nearer than the server's last report (a sticky follow)
            }

            for (var i = 0; i < 10; i++)
            {
                Bodies.StandEast(t.Creature(Guard, t.Listed, 30 + (i * 6)), here, 30 + (i * 6));
            }

            for (var i = 0; i < 15; i++)
            {
                Bodies.StandEast(t.Creature(Stranger, ThreatRig.Unlisted, 12 + (i * 5)), here, 12 + (i * 5));
            }

            var h = t.Rig.R.H.R;
            for (var i = 0u; i < 100; i++)
            {
                var held = LootRulesTests.Item(Misc, 100, 50, 0x8E7C0000 + i, "Held Thing", 97_500 + i);
                h.Reducer.OnCreateObject(h.Context, held.Created with { Weenie = held.Created.Weenie with { Header = held.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
                h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(0x8E7D0000 + i, ReducerHarness.Stamps(), ReducerHarness.Position(here.CellId, here.Origin.X + 3 + i, here.Origin.Y + 2, here.Origin.Z), name: "Corpse of Tusker Guard"), isUpdate: false);
            }

            View = new FightManaTests.DrawnView(t.Rig.R, Bodies.Over(h.Clock));
        }

        public int Objects => View.State.Objects.Count;
    }

    [Fact]
    public void The_threat_boards_asks_and_its_shadows()
    {
        var room = new Room();
        var t = room.T;
        var view = room.View;
        var board = ThreatBoard.For(view);
        Assert.True(room.Objects >= 230, $"the room holds {room.Objects} objects");

        // ---- quiet: nobody attacked her inside any window; the nearest target stands outside the outer loop's reach, so the watch walks the table and answers no
        var quietRoom = new Room();
        foreach (var g in quietRoom.OnHer)
        {
            quietRoom.T.Gone(g);
        }

        var quietView = quietRoom.View;
        using var plainQuiet = new HostileWatch(quietView, quietRoom.T.Ground, ThreatRig.OuterReach);
        var clock = quietRoom.T.Now;
        var asked = 0;
        Row("outer loop's watch, quiet room (M6 16 Q2: the board's ask)", Measure(() => asked += plainQuiet.InReach(clock += 0.016, out _) ? 1 : 0, 2000), Fps, "a frame");
        Assert.Equal(0, asked);

        // ---- under attack: the five guards hit her and a stranger's swing was evaded, all inside every window
        using var plain = new HostileWatch(view, t.Ground, ThreatRig.OuterReach);
        t.Hit(Guard, 31);
        t.Evade(Stranger);
        t.Wait(0.5);
        var now = t.Now;
        var step = 0.0;
        Row("outer loop's watch, under attack (its answer's words allocate; M6 16 Q2: the board's ask)", Measure(() => asked += plain.InReach(now + (step += 1e-7), out _) ? 1 : 0, 2000), Fps, "a frame");
        Assert.True(asked > 0);

        // ---- the board's own asks, one of each shape the lanes send
        Func<uint, bool> none = static _ => false;
        var here = view.State.Self.Position;
        var guard = new ThreatQuery(HostileReach.DefaultMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers) { Skip = none };
        var defence = new ThreatQuery(double.PositiveInfinity, 30, ThreatHeard.Attacks, ThreatStanding.Attackers) { Skip = none };
        var awake = ThreatQuery.AwakeNear(24);
        var targets = new ThreatQuery(20, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = t.Targets, Here = here, Skip = none };
        var alone = ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets);
        var sum = 0.0;
        Assert.Equal(7, board.Ask(view, in guard, now).Count); // the guards' name was heard hitting her: the five on her and the two drawn at 30 and 36 m stand within 40 m
        var asks = new (string Name, ThreatQuery Query, double PerSecond, string When)[]
        {
            ("board ask: attackers of a name heard, within 40 m (the guard's and the retreat's lanes)", guard, Fps, "a frame while she is hurt"),
            ("board ask: attackers heard, any distance (lane fight-defence)", defence, Fps, "a frame of the fight stage"),
            ("board ask: any creature awake near (lanes fight-awake, keeper-awake)", awake, 2, "at a refresh's or a rest's check"),
            ("board ask: a target of the ground near (lanes fight-refresh without a hit, fight-pull-rest)", targets, 2, "at a refresh's or a rest's check"),
            ("board ask: an attack answers alone (the outer loop's watch under attack: no body read)", alone, Fps, "a frame"),
        };
        foreach (var ask in asks)
        {
            var query = ask.Query;
            Row(ask.Name, Measure(() => sum += board.Ask(view, in query, now).Count, 2000), ask.PerSecond, ask.When);
        }

        // ---- a detector's shadow: the ask, the compare and the lane's count, agreeing
        var shadow = new ThreatDetectorShadow(view, "measured", static _ => { }, NullLogSink.Instance, "fight", hearsSince: true);
        var count = board.Ask(view, in guard, now).Count;
        Row("a detector's shadow, agreeing: the ask, the compare, the lane's count (one of the twelve lanes)", Measure(() => shadow.Answered(now + (step += 1e-7), in guard, true, count: count), 2000), Fps, "a frame, a lane");
        Assert.True(shadow.Lane.Agree > 0 && shadow.Lane.Differ == 0, $"agree {shadow.Lane.Agree} differ {shadow.Lane.Differ}");

        // ---- thirteen lanes in one frame, as a frame of the fight under attack asks them: the outer loop's, the guard's, the retreat's, the fight's defence, and the rest at their owners' asks
        var lanes = new ThreatDetectorShadow[12];
        for (var i = 0; i < lanes.Length; i++)
        {
            lanes[i] = new ThreatDetectorShadow(view, "lane-" + i.ToString(CultureInfo.InvariantCulture), static _ => { }, NullLogSink.Instance, "fight", hearsSince: true);
        }

        var queries = new[] { guard, guard, defence, awake, targets, awake, guard, targets, guard, guard, guard, guard };
        var found = new bool[queries.Length];
        var counts = new int[queries.Length];
        for (var i = 0; i < queries.Length; i++)
        {
            var answer = board.Ask(view, in queries[i], now);
            (found[i], counts[i]) = (answer.Any, answer.Count);
        }

        Row("all thirteen lanes asked in one frame (the worst frame: every owner asks)", Measure(
            () =>
            {
                var at = now + (step += 1e-7);
                asked += plain.InReach(at, out _) ? 1 : 0;
                for (var i = 0; i < lanes.Length; i++)
                {
                    lanes[i].Answered(at, in queries[i], found[i], count: counts[i]);
                }
            },
            1000), Fps, "a frame, if every owner asked every frame");
        Assert.All(lanes, l => Assert.True(l.Lane.Agree > 0 && l.Lane.Differ == 0, $"{l.Lane.Detector}: agree {l.Lane.Agree} differ {l.Lane.Differ}"));

        // ---- the feed: what the board adds to one notice of the bus
        var bare = new Room();
        var hit = new CombatEvent(CombatEventKind.DefenseHit, Guard, 1, 7, 0.05, false, 4, false, 0, "Tusker Guard hits you for 7 points of slashing damage!");
        var bareBus = bare.T.Rig.R.H.R.Bus;
        var bus = t.Rig.R.H.R.Bus;
        var without = Measure(() => bareBus.Publish(in hit), 2000);
        var with = Measure(() => bus.Publish(in hit), 2000);
        Row("a defence notice on the bus, no board (the rig's own handlers)", without, 2.5, "five attackers, one swing each 2 s");
        Row("the same with the board and a watch hearing it", with, 2.5, "five attackers, one swing each 2 s");
        var chat = new ChatLine(0, ChatKind.System, FightTally.MagicChatType, null, 0, "Tusker Guard scorches you for 44 points with Flame Bolt VI.");
        var talk = new ChatLine(0, ChatKind.System, FightTally.MagicChatType, null, 0, "You scorch Tusker Guard for 212 points with Flame Bolt VII.");
        var read = 0;
        Row("the board's read of a creature's spell line", Measure(() => read += ThreatLines.TryRead(in chat, out _) ? 1 : 0, 2000), 0.5, "a creature's spell at her");
        Row("the board's read of one of her own damage lines (not for it)", Measure(() => read += ThreatLines.TryRead(in talk, out _) ? 1 : 0, 2000), 1, "a bolt of hers a second");
        Assert.True(read > 0 && sum > 0);

        // ---- where a creature stands: one read of the drawn body
        var live = view.Live;
        Assert.True(view.State.Objects.TryGet(room.OnHer[0], out var body));
        var metres = 0.0;
        Row("one live position read (a drawn body answers)", Measure(() => metres += live.At(body!)!.Value.Origin.X, 5000), 30 * 4 * Fps, "30 creatures, four walks a frame");
        Assert.True(metres > 0);

        Print($"The threat board and its shadows: a room of {room.Objects} objects, 30 creatures, 5 on her, bodies drawn");
    }

    // ------------------------------------------------------------------------------------------------ the pack

    /// <summary>The spell ids of the three cantrips the pack's pieces carry, by their names in the SpellTable.</summary>
    private static (uint Focus, uint Willpower, uint Armor) Cantrips(IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        uint Id(string name) => spells.First(kv => string.Equals(kv.Value.Name, name, StringComparison.Ordinal)).Key;
        return (Id("Major Focus"), Id("Major Willpower"), Id("Minor Armor"));
    }

    /// <summary>
    /// A level 80 mage: the attributes, War and Life Magic specialised, and in her book every spell of the four schools up to level VII's power (the SpellTable's: she buys every castable spell), 600 at most.
    /// </summary>
    private static PlayerDescriptionMsg Mage(IReadOnlyDictionary<uint, SpellInfo> spells) => new()
    {
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 80),
        Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Focus, 190, 100, 0), new AttributeDesc(MageTemplate.Self, 190, 100, 0)],
        Skills = [new SkillDesc(MageTemplate.WarMagic, 200, 1, 3u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.LifeMagic, 200, 1, 3u, 0, 0u, 0, 0)],
        Spells = spells.Where(kv => kv.Value.Power is > 0 and <= 300 && !kv.Value.Name.StartsWith("Major ", StringComparison.Ordinal) && !kv.Value.Name.StartsWith("Minor ", StringComparison.Ordinal))
            .OrderBy(kv => kv.Key).Take(600).ToImmutableDictionary(kv => kv.Key, _ => 2f),
    };

    /// <summary>
    /// The pack: a hundred items in the main pack. Eight casters (one wielded, all appraised, two with a war bonus), eight mana stones (six appraised), forty pieces of armour and jewellery (thirty appraised, ten of them
    /// with cantrips), fourteen scrolls, twenty gems and ten plain things.
    /// </summary>
    private static (VendorRig Rig, List<uint> Items) Pack(IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        var (focus, willpower, armor) = Cantrips(spells);
        var rig = new VendorRig(1000, Mage(spells));
        var state = rig.State;
        var items = new List<uint>();
        uint Add(string name, uint wcid, uint type, uint value, uint? valid = null)
        {
            var guid = rig.NextGuid();
            rig.Arrive(guid, name, wcid, type, value, validLocations: valid);
            rig.Put(guid);
            items.Add(guid);
            return guid;
        }

        for (var i = 0u; i < 8; i++)
        {
            var g = Add(i % 2 == 0 ? "Fire Baton" : "Piercing Sceptre", 98_000 + i, CasterType, 4000 + (i * 300), SelfEquipJudge.Held);
            state.Appraisals.Add(KeepAsideRuleTests.Answer(g, i < 2 ? 1.05 + (i * 0.03) : null, i == 3 ? [focus] : []), 0);
            if (i == 0)
            {
                rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, g, (int)SelfEquipJudge.Held));
            }
        }

        for (var i = 0u; i < 8; i++)
        {
            var g = Add(i % 2 == 0 ? "Greater Mana Stone" : "Moderate Mana Stone", 98_100 + (i % 2), Stone, 2500 + (i * 100));
            if (i < 6)
            {
                state.Appraisals.Add(KeepAsideRuleTests.Answer(g), 0);
            }
        }

        for (var i = 0u; i < 40; i++)
        {
            var g = Add(i % 4 == 0 ? "Gold Ring" : "Diforsa Hauberk", 98_200 + i, i % 4 == 0 ? Jewelry : Armor, 900 + (i * 150));
            if (i < 30)
            {
                state.Appraisals.Add(KeepAsideRuleTests.Answer(g, null, i < 10 ? [i % 2 == 0 ? focus : armor, willpower] : []), 0);
            }
        }

        for (var i = 0u; i < 14; i++)
        {
            Add("Scroll of Flame Bolt VI", 98_300 + i, Scroll, 2000);
        }

        for (var i = 0u; i < 20; i++)
        {
            Add("Black Garnet", 98_400 + (i % 5), Gem, 373 + i);
        }

        for (var i = 0u; i < 10; i++)
        {
            Add("Gifted Healing Kit", 98_500 + (i % 2), Misc, 120);
        }

        rig.Tick();
        Assert.Equal(100, items.Count);
        Assert.True(state.Self.Inventory.Pack.Count >= 99, $"the main pack holds {state.Self.Inventory.Pack.Count}");
        return (rig, items);
    }

    /// <summary>
    /// M6 16 E5c2 (the merge with Eperf): Eperf measured here the make-room rule's own list, the sale shadow asked at once beside it and the shadow asked on the frames after (<c>SaleShadow.Later</c>). E5c2 moved the rule
    /// onto the rule book's verdict and removed its list and the shadow, so none of those three can be measured any more: their numbers are the ones docs/automation-engine.md section 7 records. What stands is measured:
    /// the gear keeps, the rule's sell loot by the verdict (it decides the corpse plan, so it is asked on the plan's frame and cannot be put off as the report-only shadow was), and the trip trigger's estimate by the verdict.
    /// </summary>
    [RequiresDatsFact]
    public void The_make_room_rule_and_the_gear_keeps_at_a_corpse()
    {
        IReadOnlyDictionary<uint, SpellInfo> Spells = SpellTableData.Load(fixture.Dats).Spells; // the real table: the keeps and the scroll rule read every row of it
        var (rig, items) = Pack(Spells);
        var state = rig.State;
        Assert.True(state.Self.Spellbook.Count >= 300, $"her book holds {state.Self.Spellbook.Count} spells");
        var ground = GroundCatalog.Get("tusker-quarters");
        var character = MageTemplate.ClimberName;
        var keepAside = KeepAsideRule.For(ground, character);
        var chance = Wardrobe.For(state).KeepCastChance;

        // A corpse a minute a kill: the live run's rate is the cycle line's kills over its seconds; one plan a corpse that holds anything she does not hold. Two a minute is the measure's stated rate.
        const double Plans = 2.0 / 60;
        const string PlanWhen = "a corpse plan: 2 a minute";

        var keeps = CantripRoom.GearKeeps(state, ground, Spells, chance);
        Assert.Equal(8, keeps.Verdicts.Count);
        var kept = 0;
        var gearKeeps = Row("the gear keeps (CantripRoom.GearKeeps: the fight's own, before item 16)", Measure(() => kept += CantripRoom.GearKeeps(state, ground, Spells, chance).Verdicts.Count, 20), Plans, PlanWhen);

        // the fight's context: the session's, with the fight's own keep-aside rule and the gear keeps it worked out named over it (CantripRoom.FieldBook does the same from the fight's loot rules)
        SaleContext Book((Func<uint, string?> Casters, Func<uint, bool> Stones, List<CasterKeepVerdict> Verdicts) k)
        {
            var needed = new HashSet<uint>(k.Verdicts.Where(v => v.KeptFor is not null).Select(v => v.Guid));
            return SaleContext.For(state, ground, character, Spells) with { KeepAside = keepAside, CasterNeed = k.Casters, StoneKept = k.Stones, FloorCaster = needed.Contains };
        }

        var loot = CantripRoom.SellLoot(state, Book(keeps));
        Assert.True(loot.Count > 20, $"sell loot {loot.Count}");
        var n = 0;
        var sellLoot = Row("the make-room rule's sell loot over the pack by the rule book's verdict (E5c2: the context, a verdict an item, the field's keeps)", Measure(() => n += CantripRoom.SellLoot(state, Book(keeps)).Count, 20), Plans, PlanWhen);
        var plan = Row("a whole corpse plan's room read now: the gear keeps, then the sell loot by the verdict", Measure(
            () =>
            {
                var k = CantripRoom.GearKeeps(state, ground, Spells, chance);
                n += CantripRoom.SellLoot(state, Book(k)).Count;
            },
            20), Plans, PlanWhen);

        // the parts of the verdict's share
        var need = 0;
        Row("the sale context, its gear keeps worked out (SaleContext.For and one caster asked)", Measure(() => need += SaleContext.For(state, ground, character, Spells).CasterNeed(items[0]) is null ? 0 : 1, 20), Plans, PlanWhen);
        var book = SaleContext.For(state, ground, character, Spells);
        var inventory = state.Self.Inventory;
        var verdicts = 0;
        var pass = Measure(
            () =>
            {
                foreach (var entry in inventory.Pack)
                {
                    if (state.Objects.TryGet(entry.Guid, out var o))
                    {
                        verdicts += SaleVerdict.Of(entry.Guid, o, false, book).Action == PackAction.Sell ? 1 : 0;
                    }
                }
            },
            20);
        Row("one verdict of the rule book (SaleVerdict.Of, the pack's mean)", new Cost(pass.Micros / 100, pass.Fastest / 100, pass.Slowest / 100, pass.Bytes / 100), Plans * 100, "100 items a corpse plan");

        // the trip trigger's estimate, worked out afresh (the coin changed) and read from its cache: asked only with a want the coin cannot pay, and worked out once a pack change
        var burrow = GroundCatalog.Get("tusker-burrow");
        var counters = new LogisticsCounters(burrow);
        var coin = 1000;
        long value = 0;
        var afresh = Row("the trip trigger's sale estimate worked out afresh by the verdict (E5c2)", Measure(() => value += counters.CarriedSaleValue(state), 20, () => rig.Coins(++coin)), 0.2, "a pack change, with a want the coin cannot pay");
        var cached = Row("the same read from its cache: nothing changed", Measure(() => value += counters.CarriedSaleValue(state), 2000), 1, "a check a second, with a want the coin cannot pay");
        Assert.Equal(0, cached.Bytes);
        Assert.True(kept > 0 && n > 0 && need > 0 && verdicts > 0 && value > 0, $"kept {kept} sell loot {n} needed {need} sold {verdicts} value {value}");

        Print(string.Create(CultureInfo.InvariantCulture, $"The make-room rule and the gear keeps: a main pack of 100 items (8 casters, 8 stones, 40 armour and jewellery, 14 scrolls, 20 gems, 10 kits), the Tusker Quarters, the SpellTable's {Spells.Count} spells, a book of {state.Self.Spellbook.Count}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"the frame that plans a corpse: {plan.Micros:0} us and {plan.Bytes:0} bytes (gear keeps {gearKeeps.Micros:0} us, sell loot by the verdict {sellLoot.Micros:0} us and {sellLoot.Bytes:0} bytes); the trip trigger's estimate {afresh.Micros:0} us and {afresh.Bytes:0} bytes afresh, {cached.Micros:0.000} us cached"));
    }

    // ------------------------------------------------------------------------------------------------ the walker's feed and the bestiary

    [Fact]
    public void The_walkers_creature_feed_the_awareness_shadow_and_the_bestiarys_reads()
    {
        var room = new Room();
        var state = room.View.State;
        var feed = new HostileCreatureFeed(state.Objects, state.Self.Guid, NullLogSink.Instance, static _ => 0.6f);
        var from = NavMap.FromCellFrame(state.Self.Position!.Value).Global;
        var into = new List<NavCreature>(64);
        feed.Fill(from, 60, into);
        Assert.True(into.Count >= 20, $"the feed holds {into.Count}");

        var filled = 0;
        Row("the walker's creature feed, one fill (30 creatures; the awareness shadow's reads inside)", Measure(() => { feed.Fill(from, 60, into); filled += into.Count; }, 1000), 2, "every 0.5 s of a walk, and at a plan");

        var walk = state.Objects.WalkAwareness;
        var wcid = room.T.Listed;
        var sum = 0f;
        Row("the awareness shadow's share of one creature of a fill (the own range, the kept range, the sample)", Measure(
            () =>
            {
                var pinned = Bestiary.AwarenessMetres(wcid);
                var own = Bestiary.OwnAwarenessMetres(wcid);
                sum += walk.Kept(wcid, Guard, pinned, own, null);
                walk.Seen(0x80007000, wcid, Guard, 12, NavCreatures.AggroRadius(pinned, 0.6f), NavCreatures.AggroRadius(own, 0.6f));
            },
            5000), 2 * 30, "30 creatures a fill");

        var found = 0;
        Row("one bestiary read (Bestiary.Find)", Measure(() => found += Bestiary.Find(wcid) is null ? 0 : 1, 5000), Fps, "a frame, if a reader asked every frame");
        Row("one awareness read (Bestiary.AwarenessMetres)", Measure(() => sum += Bestiary.AwarenessMetres(wcid), 5000), 2 * 30, "30 creatures a fill");
        Assert.True(filled > 0 && sum > 0);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"the bestiary holds {Bestiary.Count} kinds; the listed kind {wcid} found {found > 0}"));

        Print("The walker's creature feed and the bestiary");
    }

    // ------------------------------------------------------------------------------------------------ a whole step of the fight

    /// <summary>
    /// The top-down number: one whole <see cref="FightScenario.Step"/> of a fight in progress (the fight tests' scripted world: bolts cast and landing, tuskers dying and coming again), five of them heard attacking her,
    /// with a hundred held items and a hundred things on the floor in the table. The fight's four shadows run inside it. Each step is timed alone; the row is the median over the steps of a run, the median of five runs.
    /// The scripted world answers a cast in 0.02 s, so this fight decides and logs far more often than a live one (three steps in four log a line): what one step of a kind costs is the measure, and the eight slowest
    /// steps are named by what they logged, with the microseconds before each line; how often a kind of step comes is the live fight's own.
    /// </summary>
    [Fact]
    public void A_whole_step_of_a_fight_in_progress()
    {
        var medians = new List<double>();
        var p99s = new List<double>();
        var maxes = new List<double>();
        var bytes = new List<double>();
        var quietBytes = new List<double>();
        var logged = new List<double>();
        var slowest = new List<string>();
        var asked = 0L;
        var fingerprint = string.Empty;
        var ci = CultureInfo.InvariantCulture;
        for (var run = 0; run < 5; run++)
        {
            var r = new FightManaTests.Rig(mana: 2000, stamina: 2000) { Fight = new FightOptions { Kills = 100_000 }, Respawn = true, HitsToKill = 3 };
            var h = r.R.H.R;
            for (var i = 0; i < 5; i++)
            {
                r.AddTusker(20 + (i * 3));
            }

            for (var i = 0u; i < 100; i++)
            {
                var held = LootRulesTests.Item(Misc, 100, 50, 0x8E7C0000 + i, "Held Thing", 97_500 + i);
                h.Reducer.OnCreateObject(h.Context, held.Created with { Weenie = held.Created.Weenie with { Header = held.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
                h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(0x8E7D0000 + i, ReducerHarness.Stamps(), ReducerHarness.Position(r.TuskerCell, 60 + i, 80, 30), name: "Corpse of Tusker Slave"), isUpdate: false);
            }

            var sink = new MessageSink();
            using var scenario = new FightScenario(r.Kit, r.Caster.Table, r.Buff, r.Fight, new FightManaTests.Walk(), sink, endSession: false, stages: FightStages.Mode);
            const int Steps = 3600;
            var micros = new double[Steps];
            var allocated = new long[Steps];
            var firstLine = new int[Steps];
            var lines = new int[Steps];
            var began = new long[Steps];
            var ended = new long[Steps];
            for (var i = 0; i < 600; i++)
            {
                r.Frame(scenario, 1.0 / Fps); // the fight's start and its first casts: not measured
            }

            for (var i = 0; i < Steps; i++)
            {
                r.R.Tick(1.0 / Fps);
                r.Caster.Pump();
                r.R.Tick();
                if (i % 120 == 0)
                {
                    h.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, r.TuskerName, 1, 12, 0.05, false, 4, false, 0, "Tusker Slave hits you for 12 points of slashing damage!"));
                }

                firstLine[i] = sink.Messages.Count;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                var status = scenario.Step(r.View, r.Now);
                var t1 = Stopwatch.GetTimestamp();
                (began[i], ended[i]) = (t0, t1);
                micros[i] = (t1 - t0) * 1_000_000.0 / Stopwatch.Frequency;
                allocated[i] = GC.GetAllocatedBytesForCurrentThread() - a0;
                lines[i] = sink.Messages.Count - firstLine[i];
                Assert.Equal(ScenarioStatus.Running, status);
            }

            if (run == 0)
            {
                foreach (var i in Enumerable.Range(0, Steps).OrderByDescending(i => micros[i]).Take(8))
                {
                    var said = lines[i] == 0 ? "no line" : sink.Messages[firstLine[i]];
                    var gaps = new System.Text.StringBuilder(); // where the step's time went: the microseconds before its first line, between its lines and after its last
                    var from = began[i];
                    for (var l = 0; l < lines[i]; l++)
                    {
                        gaps.Append(ci, $"{(sink.At[firstLine[i] + l] - from) * 1_000_000.0 / Stopwatch.Frequency:0} / ");
                        from = sink.At[firstLine[i] + l];
                    }

                    gaps.Append(ci, $"{(ended[i] - from) * 1_000_000.0 / Stopwatch.Frequency:0}");
                    slowest.Add(string.Create(ci, $"| {micros[i]:0} | {allocated[i]} | {lines[i]} | {gaps} | {(said.Length > 110 ? said[..110] : said).Replace('|', '/')} |"));
                }
            }

            var quiet = Enumerable.Range(0, Steps).Where(i => lines[i] == 0).Select(i => (double)allocated[i]).ToArray();
            Array.Sort(quiet);
            quietBytes.Add(quiet.Length == 0 ? 0 : quiet[quiet.Length / 2]);
            logged.Add(lines.Count(l => l > 0) * 100.0 / Steps);
            Array.Sort(micros);
            medians.Add(micros[Steps / 2]);
            p99s.Add(micros[Steps * 99 / 100]);
            maxes.Add(micros[^1]);
            bytes.Add(allocated.Sum() / (double)Steps);
            var lane = ThreatBoard.For(r.View).Lane(FightScenario.DefenceDetector);
            asked += lane.Agree + lane.Differ;
            if (run == 0)
            {
                // what the fight did, as one number: a build that changes no decision logs the same lines in the same order
                var hash = 14695981039346656037UL;
                foreach (var message in sink.Messages)
                {
                    foreach (var c in message)
                    {
                        hash = (hash ^ c) * 1099511628211UL;
                    }
                }

                fingerprint = string.Create(ci, $"{sink.Messages.Count} lines logged, their hash {hash:X16}");
            }

            Assert.True(scenario.KillsPerHour > 0, "the fight killed nothing");
        }

        medians.Sort();
        p99s.Sort();
        maxes.Sort();
        bytes.Sort();
        quietBytes.Sort();
        logged.Sort();
        output.WriteLine("A whole step of the fight (FightScenario.Step), a fight in progress, five runs of 3,600 steps (a minute at 60 frames a second)");
        output.WriteLine("| | median of the runs | fastest run | slowest run |");
        output.WriteLine("|---|---|---|---|");
        output.WriteLine(string.Create(ci, $"| a step's median, us | {medians[2]:0.00} | {medians[0]:0.00} | {medians[^1]:0.00} |"));
        output.WriteLine(string.Create(ci, $"| a step's 99th percentile, us | {p99s[2]:0.0} | {p99s[0]:0.0} | {p99s[^1]:0.0} |"));
        output.WriteLine(string.Create(ci, $"| the slowest step of the minute, us | {maxes[2]:0} | {maxes[0]:0} | {maxes[^1]:0} |"));
        output.WriteLine(string.Create(ci, $"| bytes a step (mean) | {bytes[2]:0} | {bytes[0]:0} | {bytes[^1]:0} |"));
        output.WriteLine(string.Create(ci, $"| bytes a step that logs nothing (median) | {quietBytes[2]:0} | {quietBytes[0]:0} | {quietBytes[^1]:0} |"));
        output.WriteLine(string.Create(ci, $"| steps that log a line, percent | {logged[2]:0.0} | {logged[0]:0.0} | {logged[^1]:0.0} |"));
        output.WriteLine(string.Create(ci, $"so at {Fps:0} frames a second: {medians[2] * Fps / 1000:0.00} ms and {bytes[2] * Fps / 1024:0.0} KiB a second; the fight-defence lane was asked {asked} time(s) over the five runs"));
        // what a decision is made of: the mean rolled cost of a cast (ManaConversionModel.ExpectedCost), asked for every candidate of every decision
        var probe = new FightManaTests.Rig(mana: 2000, stamina: 2000);
        var boltInfo = probe.Caster.Table.Values.Where(v => FightManaTests.Rig.IsBolt(v.Id)).OrderByDescending(v => v.Power).First();
        Lifestone.Client.Session.Vitals.ManaOrder.TryManaConversion(probe.R.H.State.Self.Qualities, out var conversion);
        var cost = 0.0;
        var skill = conversion;
        var fresh = Measure(() => cost += Lifestone.Client.Session.Casting.ManaConversionModel.ExpectedCost(boltInfo.BaseMana, boltInfo.Power, skill), 1, () => skill = skill >= conversion + 400 ? conversion : skill + 1);
        var again = Measure(() => cost += Lifestone.Client.Session.Casting.ManaConversionModel.ExpectedCost(boltInfo.BaseMana, boltInfo.Power, conversion), 200);
        output.WriteLine(string.Create(ci, $"the mean rolled cost of one cast (ManaConversionModel.ExpectedCost: {boltInfo.Name}, base mana {boltInfo.BaseMana}, power {boltInfo.Power}, Mana Conversion {conversion}): {fresh.Micros:0.0} us ({fresh.Fastest:0.0} to {fresh.Slowest:0.0}) and {fresh.Bytes:0} bytes for a skill not asked before; {again.Micros:0.00} us ({again.Fastest:0.00} to {again.Slowest:0.00}) and {again.Bytes:0} bytes asked again with the same numbers"));
        Assert.True(cost > 0);
        output.WriteLine("what the first run's fight did (the same in two builds when no decision changed): " + fingerprint);
        output.WriteLine("The eight slowest steps of the first run:");
        output.WriteLine("| us | bytes | lines logged | us before each line and after the last | the first line |");
        output.WriteLine("|---|---|---|---|---|");
        foreach (var row in slowest)
        {
            output.WriteLine(row);
        }

        Assert.True(asked > 0, "the fight's shadow was never asked");
    }

    /// <summary>A log that keeps each message as it is handed over (no formatting of its own), so a slow step can be named by what it logged.</summary>
    private sealed class MessageSink : ILogSink
    {
        public List<string> Messages { get; } = new(8192);

        public bool IsEnabled(LogSeverity severity) => true;

        public List<long> At { get; } = new(8192);

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
        {
            Messages.Add(message);
            At.Add(Stopwatch.GetTimestamp());
        }
    }
}
