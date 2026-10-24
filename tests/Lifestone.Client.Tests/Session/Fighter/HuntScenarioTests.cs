using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Data.Dat;
using Lifestone.Data.World;
using Lifestone.Data.Tests;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The hunt loop (M4 1.2 s4a, <see cref="HuntScenario"/>) over the offline stand-in. M4 4.1 t2 (D23, D26) retired the stand-in's creature, so the rows that fought, killed and looted one (the melee and missile hunts, the corpse
/// loot, the recovery, the retreat, the approach, the vendor trips after a hunt) were deleted with it: the hunt is proved live (the hunt leg, <c>HuntLiveTests</c>) and by <c>HuntLegVerdictTests</c>, <c>HuntTallyTests</c>-style rules tests.
/// What stays needs no creature: the options and the restore of the template's end state with nothing near. The scenario's own logic against a scripted ACE (fights, corpses, heals, shops) is the partial files' rows over <see cref="HuntScriptRig"/>.
/// </summary>
public sealed partial class HuntScenarioTests(DatSetFixture datsFixture) : IClassFixture<DatSetFixture>
{
    private const uint LifestoneGuid = 0x80000C01;
    private const uint FirstCreature = 0x80000D01;
    private const string Belt = "Field Ursuin";
    private const uint BeltWcid = 7990; // Field Ursuin, level 8 (HuntGround.LowBTable)
    private const uint TumerokWcid = 233; // Tumerok Warrior, level 30: listed, filtered by the band 8-20

    private static readonly Regex Verdict = new(
        @"^fighter=(ok|wrong) kills=\d+ swings=\d+ shots=\d+ hits=\d+ misses=\d+ blocked=\d+ seeks=\d+ damage_dealt=\d+ damage_taken=\d+ hits_taken=\d+ evades=\d+ xp_gained=-?\d+ returned=(yes|no)$",
        RegexOptions.CultureInvariant);

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();
        private int _beltArrivals;
        private int _seenTeleports;

        public Rig(string character = FighterTemplate.HunterName, ImmutableArray<StandInVitalUpdate> vitals = default, bool heals = false, bool healItems = false, StandInPlayerStats? stats = null, bool vendors = false)
        {
            Log = new RecordingLogSink();
            var visit = new StandInVisit
            {
                EnterAfter = 0.2,
                UnhideAfter = 1.0,
                Heals = heals, // M4 3.1 s3: the stand-in's heal (off: a potion's Use is the old bare UseDone and a kit's UseWithTarget throws)
                HealItems = healItems, // the announced Handy Healing Kit (20 uses) and 5 Potions of Healing; implies Heals
                Vendors = vendors, // M4 3.2 s4a: Thelnoth and Renald at their real cells, the purse of 10,000 pyreals, a Handy Healing Kit and Leather Bracers (StandInWorld.Vendor.cs)
                Stats = stats ?? new StandInPlayerStats { Level = 40 },
                VitalUpdates = vitals.IsDefault ? [] : vitals,
                Items = new Dictionary<uint, StandInItemKind>
                {
                    [FighterKit.MaceWcid] = new("Mace", null),
                    [FighterKit.ShortbowWcid] = new("Shortbow", null),
                    [FighterKit.ArrowWcid] = new("Arrow", 250),
                    [FighterKit.PotionWcid] = new("Potion of Healing", 100),
                },
            };
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h, new StandInCharacter(character));
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                Log);
            Server = StandInSessions.Server(Session.Reducer);
            Server.UnhideAfterTeleport = true; // the offline session refuses LoginComplete, so the stand-in ends an arrival itself (StandInServer.UnhideAfterTeleport)
            Run(2.0);
        }

        public RecordingLogSink Log { get; }

        public GameSession Session { get; }

        public StandInServer Server { get; }

        public StandInWorld World => Session.Reducer.LocalResponder!.World!;

        /// <summary>Called on each arrival outside Holtburg's landblock with the arrival's number (1 for the first cell), to place what that cell holds.</summary>
        public Action<Rig, int>? OnBelt { get; set; }

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds - 1e-9; t += 0.1)
            {
                Tick();
            }
        }

        private void Tick()
        {
            _time.Advance(TimeSpan.FromSeconds(0.1));
            Session.Tick(Session.Clock.Now);
            if (Session.State.Self.Position is { } at && (at.CellId >> 16) != 0xA9B4 && Session.State.Self.Travel == SelfTravel.Present && World.Teleports > _seenTeleports)
            {
                _seenTeleports = World.Teleports;
                OnBelt?.Invoke(this, ++_beltArrivals);
            }
        }

        public uint Spawn(uint wcid, int? stack = null)
        {
            var spawn = Session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
            Run(1.5);
            Assert.True(spawn.IsCompleted, spawn.Summary);
            return spawn.Result!.Guid;
        }

        public void Wield(uint item, EquipMask mask)
        {
            var wield = Session.Actions.Wield(item, mask, ActionSource.Host);
            Run(1.5);
            Assert.True(wield.IsCompleted, wield.Summary);
        }

        /// <summary>The build's state: the Mace wielded, the bow, the arrows and some potions in the pack, a lifestone near the arrival.</summary>
        public void Kit(int potions = 0)
        {
            Wield(Spawn(FighterKit.MaceWcid), EquipMask.MeleeWeapon);
            Spawn(FighterKit.ShortbowWcid);
            Spawn(FighterKit.ArrowWcid, 20);
            for (var i = 0; i < potions; i++)
            {
                Spawn(FighterKit.PotionWcid, 1);
            }

            Assert.NotNull(Server.PlaceNear(StandInLifestone.Make(LifestoneGuid), new Vector3(1f, 0f, 0f)));
            Run(0.5);
        }

        public void PlaceCreature(uint guid, string name, double metres, uint health = 40, uint wcid = BeltWcid) =>
            throw new NotSupportedException("the stand-in holds no creature (M4 4.1 t2, D23, D26): a hunt over a creature is proved live (the hunt leg) and by the scripted tests");

        /// <summary>One tick (0.1 s) of the session, then <paramref name="afterTick"/>, then one step of the scenario: lets a test act between what the stand-in sent and what the scenario sees.</summary>
        public ScenarioStatus RunHooked(HuntScenario scenario, Action afterTick)
        {

            Tick();
            afterTick();
            return scenario.Step(Session, Session.Clock.Now);
        }

        public ScenarioStatus Run(HuntScenario scenario, double limitSeconds)
        {

            var status = ScenarioStatus.Running;
            for (var t = 0.0; t < limitSeconds && status == ScenarioStatus.Running; t += 0.1)
            {
                Tick();
                status = scenario.Step(Session, Session.Clock.Now);
            }

            return status;
        }

        public bool Held(uint wcid, out bool equipped)
        {
            equipped = false;
            foreach (var o in Session.State.Objects.All)
            {
                if (o.Weenie.WeenieClassId == wcid && Session.State.Self.Inventory.Find(o.Guid).Held)
                {
                    equipped = Session.State.Self.Inventory.Find(o.Guid).Kind == InventoryPlaceKind.Equipped;
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<uint> Guids(uint wcid) =>
            [.. Session.State.Objects.All.Where(o => o.Weenie.WeenieClassId == wcid && Session.State.Self.Inventory.Find(o.Guid).Held).Select(o => o.Guid)];

        public bool Equipped(uint guid) => Session.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;

        /// <summary>The inventory actions the scenario sent, in order (the stand-in's record of each game action: its type and fields).</summary>
        public IReadOnlyList<(uint Type, uint First, uint Second, uint Third)> Sends => Session.Reducer.LocalResponder!.ActionSends;

        /// <summary>The position in <see cref="Sends"/> of the last send of <paramref name="type"/> for <paramref name="guid"/> (-1 when none).</summary>
        public int LastSend(uint type, uint guid)
        {
            for (var i = Sends.Count - 1; i >= 0; i--)
            {
                if (Sends[i].Type == type && Sends[i].First == guid)
                {
                    return i;
                }
            }

            return -1;
        }

        public void MoveToPack(uint item)
        {
            var move = Session.Actions.MoveToPack(item, source: ActionSource.Host);
            Run(1.5);
            Assert.True(move.IsCompleted, move.Summary);
        }

        public bool Logged(string text) => Log.Lines.Any(l => l.Contains(text, StringComparison.Ordinal));

        public void Dispose() => Session.Dispose();
    }

    /// <summary>The scenario over the rig. The rows before M4 3.2 run without the shops (<c>sell</c> off, <c>buyKits</c> 0): the stand-in has no vendors unless the rig asks, and those rows prove the hunt, not the sale.</summary>
    private static HuntScenario Make(Rig rig, HuntWeapon weapon = HuntWeapon.Melee, int kills = 1, double seconds = 300, bool openCorpse = true, LootOptions? loot = null, RecoveryOptions? recovery = null, bool sell = false, int buyKits = 0, bool appraise = true) =>
        new(new HuntOptions { Weapon = weapon, Kills = kills, Seconds = seconds, OpenCorpse = openCorpse, Loot = loot ?? LootOptions.Default, Recovery = recovery ?? RecoveryOptions.Default, Sell = sell, BuyKits = buyKits, AppraiseLoot = appraise }, rig.Log, _ => 40f, endSession: false);

    /// <summary>Steps the hunt, 0.1 s of the session clock at a time, until <paramref name="reached"/> holds; true when it did within <paramref name="limitSeconds"/>.</summary>
    private static bool RunUntil(Rig rig, HuntScenario scenario, Func<bool> reached, double limitSeconds = 600)
    {
        for (var t = 0.0; t < limitSeconds && !reached(); t += 0.1)
        {
            if (rig.Run(scenario, 0.1) != ScenarioStatus.Running)
            {
                break;
            }
        }

        return reached();
    }

    private static bool Holds(Rig rig, uint wcid) => rig.Held(wcid, out _);

    [Fact]
    public void The_loot_options_parse_with_the_hunts_and_loot_is_on_by_default()
    {
        var o = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=2", "--loot=off", "--loot-min-value=5"]));

        Assert.Empty(o.Errors);
        Assert.False(o.Loot.Enabled);
        Assert.Equal(5, o.Loot.MinValue);
        Assert.True(HuntOptions.FromArguments(CommandLineArgument.ParseAll([])).Loot.Enabled);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--loot-max-burden=0"])).Errors);
    }

    private static bool RunToFirstKill(Rig rig, HuntScenario scenario, double limitSeconds = 600)
    {
        for (var t = 0.0; t < limitSeconds && scenario.Tally.Kills < 1; t += 0.1)
        {
            if (rig.Run(scenario, 0.1) != ScenarioStatus.Running)
            {
                break;
            }
        }

        return scenario.Tally.Kills >= 1;
    }

    [Fact]
    public void Nothing_at_all_near_fails_loudly_after_the_cells_and_still_returns()
    {
        using var rig = new Rig();
        rig.Kit();
        var scenario = Make(rig);

        var status = rig.Run(scenario, 1200);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("no creature of the table in the band 8-20", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(HuntScenario.MaxCells, scenario.CellsVisited);
        Assert.True(scenario.Returned);
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- M4 3.1 s3: the recovery rule's driver on the stand-in's heal

    /// <summary>The Holtburg Hunter's numbers (Endurance 240 and 68 health ranks: 188 health, full), so the recovery's chance is the capture's.</summary>
    private static StandInPlayerStats Hunter188() => new() { Level = 40, Endurance = 240, HealthRanks = 68, Health = 188, Stamina = 240 };

    private static ImmutableArray<StandInVitalUpdate> HealthAt(uint health) => [new StandInVitalUpdate(0.5, 2, health)];

    /// <summary>A generator that answers the same draw for ever: 0.9999 loses every roll the stand-in makes (the chance never reaches it).</summary>
    private sealed class Fixed(double draw) : Random
    {
        public override double NextDouble() => draw;
    }

    private static int LineAt(Rig rig, string text, int from = 0)
    {
        var lines = rig.Log.Lines;
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The last lines of the log, for a failure message (the stand-in runs are long).</summary>
    private static string Tail(Rig rig, int count = 30) => Environment.NewLine + string.Join(Environment.NewLine, rig.Log.Lines.TakeLast(count));

    private static int LineCount(Rig rig, string text) => rig.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void The_end_state_is_restored_to_the_templates_whenever_it_differs_even_when_the_hunt_never_swapped()
    {
        using var rig = new Rig();
        rig.Kit();
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var bow = rig.Guids(FighterKit.ShortbowWcid).Single();
        var toPack = rig.Session.Actions.MoveToPack(mace, source: ActionSource.Host);
        rig.Run(1.5);
        Assert.True(toPack.IsCompleted, toPack.Summary);
        rig.Wield(bow, EquipMask.MissileWeapon);
        var scenario = Make(rig); // melee: no swap, nothing near

        var status = rig.Run(scenario, 1200);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace is wielded again");
        Assert.False(rig.Equipped(bow), "the Shortbow is in the pack");
    }

    [Fact]
    public void A_half_done_swap_with_two_Shortbows_is_repaired_by_the_restore_taking_off_the_wielded_one_by_its_guid_before_the_Mace_3_1_fix2()
    {
        // The Mace in the pack, the second Shortbow wielded and the first (listed before it) in the pack: the hunt (melee, nothing near) ends in its wind-down, which reads the end state.
        using var rig = new Rig();
        rig.Kit();
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var packBow = rig.Guids(FighterKit.ShortbowWcid).Single();
        var wieldedBow = rig.Spawn(FighterKit.ShortbowWcid);
        rig.MoveToPack(mace);
        rig.Wield(wieldedBow, EquipMask.MissileWeapon);
        Assert.True(rig.Equipped(wieldedBow) && !rig.Equipped(mace));
        var before = rig.Sends.Count;
        var scenario = Make(rig); // melee: no swap, nothing near

        var status = rig.Run(scenario, 1200);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace is wielded again");
        Assert.False(rig.Equipped(wieldedBow) || rig.Equipped(packBow), "no Shortbow is wielded");
        Assert.DoesNotContain(rig.Sends.Skip(before), x => x.First == packBow);
        var bowBack = rig.LastSend(GameActionTypes.PutItemInContainer, wieldedBow);
        var maceWield = rig.LastSend(GameActionTypes.GetAndWieldItem, mace);
        Assert.True(bowBack >= before && maceWield > bowBack, $"the wielded Shortbow to the pack ({bowBack}) before the Mace is wielded ({maceWield})");
        Assert.True(rig.Logged($"the Shortbow to the pack 0x{wieldedBow:X8}: confirmed"), "the step is logged by guid");
        Assert.True(rig.Logged("restore: the Mace is wielded, no launcher and no ammunition are wielded"));
    }

    [Fact]
    public void Another_character_is_never_acted_for_and_the_verdict_says_wrong()
    {
        using var rig = new Rig(character: "Stand-in Alpha");
        rig.Kit();
        var before = rig.Log.Lines.Count;
        var scenario = Make(rig);

        var status = rig.Run(scenario, 60);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("is not Holtburg Hunter", scenario.Failure, StringComparison.Ordinal);
        Assert.StartsWith("fighter=wrong ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.Attacks);
        Assert.False(scenario.Bound);
        Assert.Equal(0, rig.World.Teleports);
        Assert.DoesNotContain(rig.Log.Lines.Skip(before), l => l.Contains("bind:", StringComparison.Ordinal));
    }

    // The shop rows (M4 3.2 s4a: the sale, the buy and the appraisal at the loop's end) run over the scripted vendors of the rig: HuntScenarioTests.Shops.cs.

    [Fact]
    public void M4_3_2_s4a_the_hunt_options_carry_sell_buy_kits_and_appraise_loot()
    {
        var d = HuntOptions.FromArguments(CommandLineArgument.ParseAll([]));
        Assert.Equal((true, 2, true), (d.Sell, d.BuyKits, d.AppraiseLoot));
        var o = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--sell=off", "--buy-kits=5", "--appraise-loot=off"]));
        Assert.Empty(o.Errors);
        Assert.Equal((false, 5, false), (o.Sell, o.BuyKits, o.AppraiseLoot));
        Assert.Contains("sell=off buy_kits=5 appraise_loot=off", o.ToString(), StringComparison.Ordinal);
    }
}
