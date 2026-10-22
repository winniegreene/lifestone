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
/// The hunt loop (M4 1.2 s4a, <see cref="HuntScenario"/>) against the offline stand-in (s3's <c>StandInWorld.Attack</c>, the Tusker Slave's model renamed to a belt creature): one melee kill and the return, one missile
/// kill with the weapon swap in capture.md C2's order and the restore, the exclusion of a target the server refuses, the next cell when a cell holds no listed creature, the logged fallback to an unlisted creature, the
/// health steps (potions then the regeneration wait) and the retreat under 30%. HOST: the stand-in, not the scripted ACE of 1.1. The stand-in already answers capture C1 and C3's orders (s3), the lifestone's Use, the
/// item moves, the wield refusal and the teleports, and the scenario takes an already entered session as the gear scenario does (the stand-in names its first character "Holtburg Hunter" here); the scripted ACE has none
/// of the attack answers, and adding them would copy s3's model. What neither host proves (the character choice from a real list, ACE's own refusals and the 02EB text) is the loopback tests' (the lead's).
/// </summary>
public sealed class HuntScenarioTests(DatSetFixture datsFixture) : IClassFixture<DatSetFixture>
{
    private const uint LifestoneGuid = 0x80000C01;
    private const uint FirstCreature = 0x80000D01;
    private const string Belt = "Field Ursuin";
    private const uint BeltWcid = 7990; // Field Ursuin, level 8 (HuntGround.LowBTable)
    private const uint TumerokWcid = 233; // Tumerok Warrior, level 30: listed, filtered by the band 8-20

    private static readonly Regex Verdict = new(
        @"^fighter=(ok|wrong) kills=\d+ swings=\d+ shots=\d+ hits=\d+ misses=\d+ damage_dealt=\d+ damage_taken=\d+ hits_taken=\d+ evades=\d+ xp_gained=-?\d+ returned=(yes|no)$",
        RegexOptions.CultureInvariant);

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();
        private int _beltArrivals;
        private int _seenTeleports;

        public Rig(string character = FighterTemplate.CharacterName, ImmutableArray<StandInVitalUpdate> vitals = default, bool heals = false, bool healItems = false, StandInPlayerStats? stats = null, bool vendors = false)
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

        public void PlaceCreature(uint guid, string name, double metres, uint health = 40, uint wcid = BeltWcid)
        {
            // health 0: an object the client reads as a creature and the stand-in does not (no health to hold), so the server refuses an attack on it (Player_Melee.cs 105-134).
            var creature = health == 0
                ? new StandInObject(guid, name, default, StandInTusker.Setup, 0, DescriptionFlags: StandInTusker.AttackableFlag, WeenieClassId: wcid)
                : StandInTusker.Make(guid, default, maxHealth: health) with { Name = name, WeenieClassId = wcid };
            Assert.NotNull(Server.PlaceNear(creature, new Vector3(0f, (float)metres, 0f)));
            Run(0.3);
        }

        /// <summary>One tick (0.1 s) of the session, then <paramref name="afterTick"/>, then one step of the scenario: lets a test act between what the stand-in sent and what the scenario sees.</summary>
        public ScenarioStatus RunHooked(HuntScenario scenario, Action afterTick)
        {
            World.PlayerHitRate = 1.0;
            Tick();
            afterTick();
            return scenario.Step(Session, Session.Clock.Now);
        }

        public ScenarioStatus Run(HuntScenario scenario, double limitSeconds)
        {
            World.PlayerHitRate = 1.0;
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

    [Fact]
    public void A_melee_hunt_binds_goes_to_the_belt_kills_the_nearest_listed_creature_and_comes_back()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Null(scenario.Failure);
        var fields = scenario.VerdictFields();
        Assert.Matches(Verdict, fields);
        Assert.StartsWith("fighter=ok kills=1 ", fields, StringComparison.Ordinal);
        Assert.True(scenario.Tally.Swings >= 1, fields);
        Assert.True(rig.Session.Attack.Swings >= 1, fields);
        Assert.True(scenario.Tally.Hits >= 1 || scenario.Tally.Kills == 1, fields);
        Assert.Equal(0, scenario.Tally.Shots);
        Assert.EndsWith(" returned=yes", fields, StringComparison.Ordinal);
        Assert.True(scenario.Bound);
        Assert.Equal(1, scenario.CellsVisited);
        Assert.Equal(1, scenario.Attacks);
        Assert.Equal(0xA9B4u, rig.Session.State.Self.Position!.Value.CellId >> 16);
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var maceWielded) && maceWielded, "the Mace stays wielded (the fighter-build scenario reads wielded=331)");
        Assert.True(rig.Logged("[hunt] verdict fighter=ok"), "the verdict line");
    }

    [Fact]
    public void The_hunt_opens_lists_and_closes_its_kills_corpse_and_with_loot_off_takes_nothing_2_1()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, loot: new LootOptions { Enabled = false });

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.CorpsesOpened);
        Assert.Equal(2, scenario.CorpseItems); // the stand-in corpse's Bowl and Smock
        Assert.Equal(0, scenario.CorpsesMissed);
        Assert.Equal(0, scenario.CorpsesRefused);
        Assert.True(rig.Logged("corpse: ") && rig.Logged("holds 2 item(s)"), "the contents are logged");
        Assert.True(rig.Logged("'Bowl' wcid " + StandInWorld.BowlWcid) && rig.Logged("'Smock' wcid " + StandInWorld.SmockWcid), "each entry's name and wcid");
        Assert.True(rig.Logged("corpse: closed"), "the corpse is closed");
        Assert.Equal(0u, rig.World.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
        Assert.DoesNotContain(rig.Session.State.Objects.All, o => o.Weenie.WeenieClassId == StandInWorld.BowlWcid && rig.Session.State.Self.Inventory.Find(o.Guid).Held);
        Assert.Equal(0, scenario.Looted);
        Assert.Equal(0, scenario.LootSkipped);
        Assert.False(rig.Logged("loot: "), "--loot off is 2.1's hunt: no loot line at all");
        Assert.False(rig.Logged("looting"));
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

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
    public void The_hunt_loots_its_kills_corpse_between_the_open_and_the_close_under_the_default_rule()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        var sawLooting = false;
        var windowWhileLooting = true;

        // step through the whole run, watching the loot phase: the window (the open sent to the close settled) holds around it
        for (var t = 0.0; t < 600; t += 0.1)
        {
            var status = rig.Run(scenario, 0.1);
            sawLooting |= scenario.Looting;
            windowWhileLooting &= !scenario.Looting || scenario.CorpseWindow;
            if (status != ScenarioStatus.Running)
            {
                Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
                break;
            }
        }

        Assert.True(sawLooting, "the loot phase ran");
        Assert.True(windowWhileLooting);
        Assert.False(scenario.CorpseWindow, "the window is closed at the end");
        Assert.Equal(1, scenario.CorpsesOpened);
        Assert.Equal(2, scenario.CorpseItems);
        Assert.Equal(2, scenario.Looted);
        Assert.Equal(0, scenario.LootSkipped);
        Assert.Equal(2, scenario.Tally.Looted);
        Assert.True(Holds(rig, StandInWorld.BowlWcid) && Holds(rig, StandInWorld.SmockWcid), "the Bowl and the Smock are in the pack");
        Assert.Equal(0u, rig.World.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);

        // the order of the log: listed, taken one by one, the loot done, then the close
        int At(string text) => rig.Log.Lines.ToList().FindIndex(l => l.Contains(text, StringComparison.Ordinal));
        Assert.True(At("holds 2 item(s)") >= 0);
        Assert.True(At("holds 2 item(s)") < At("loot: taking 'Bowl'"));
        Assert.True(At("loot: taking 'Bowl'") < At("loot: took 'Bowl'"));
        Assert.True(At("loot: took 'Bowl'") < At("loot: taking 'Smock'"), "one at a time: the Smock goes after the Bowl is confirmed");
        Assert.True(At("loot: taking 'Smock'") < At("loot: done looted=2"));
        Assert.True(At("loot: done looted=2") < At("corpse: closed"), "the close waits for the loot");
        Assert.Contains("looted=2 loot_skipped=0", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the verdict fields did not change shape
    }

    [Fact]
    public void The_rule_leaves_what_it_does_not_take_and_the_tally_counts_the_skips_by_the_rule()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        // the stand-in's Bowl is Misc 0x80 value 10, the Smock Clothing 0x4 value 25 (placeholders): a type mask of Clothing takes the Smock and leaves the Bowl by type
        var scenario = Make(rig, loot: new LootOptions { TypeMask = 0x4 });

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Looted);
        Assert.Equal(1, scenario.LootSkipped);
        Assert.True(Holds(rig, StandInWorld.SmockWcid) && !Holds(rig, StandInWorld.BowlWcid));
        Assert.True(rig.Logged("loot: skipped 'Bowl'") && rig.Logged("(type,"), "the skip names its reason");

        // a least value of 20 leaves the Bowl (10) by value and takes the Smock (25)
        using var valued = new Rig();
        valued.Kit();
        valued.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var second = Make(valued, loot: new LootOptions { MinValue = 20 });
        Assert.True(valued.Run(second, 600) == ScenarioStatus.Succeeded, second.Failure);
        Assert.Equal(1, second.Looted);
        Assert.Equal(1, second.LootSkipped);
        Assert.True(valued.Logged("(value,"));
    }

    [Fact]
    public void The_retreat_rule_abandons_the_loot_in_progress_and_closes_the_corpse()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        Assert.True(RunUntil(rig, scenario, () => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);

        rig.World.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health while the first pick-up is in flight
        var status = rig.Run(scenario, 0.5);

        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), "the abandon is logged within half a second of the drop");
        Assert.False(scenario.Looting);
        Assert.False(scenario.CorpseWindow);
        Assert.True(rig.Logged("loot: done") && rig.Logged("stopped_by=abandoned"), "the loop says it was abandoned");
        status = status == ScenarioStatus.Running ? rig.Run(scenario, 120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status); // never held for the loot's other item
        Assert.True(scenario.Looted < 2, "the second item was never sent");
        Assert.Equal(0u, rig.World.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
    }

    [Fact]
    public void M4_2_2_fix_R3_a_pick_up_in_flight_at_the_retreat_is_counted_when_it_settles_so_looted_does_not_read_low()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        Assert.True(RunUntil(rig, scenario, () => rig.Logged("loot: taking 'Bowl'")), "the first pick-up was sent: " + scenario.Failure);
        Assert.Equal(0, scenario.Looted);

        rig.World.SetPlayerHealthLater(0.05, 4); // the retreat rule fires while the first pick-up is in flight
        var status = rig.Run(scenario, 0.2);
        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), "the abandon is logged");
        Assert.True(rig.Logged("is in flight at the abandon"), "the loop kept the pick-up in flight");
        Assert.Equal(0, scenario.Looted); // not settled yet: the count so far

        // ACE's 0022 for the Bowl lands after the abandon (the stand-in itself would refuse the pick-up of a corpse it has just seen closed; live, the death trip's abort left the pick-up in flight and the item came back).
        var bowl = rig.Session.State.Objects.All.First(static o => o.Weenie.WeenieClassId == StandInWorld.BowlWcid).Guid;
        var self = rig.Session.State.Self.Guid;
        rig.Session.Reducer.OnInventoryPutObjInContainer(new Lifestone.Net.Dispatch.InboundContext(0, Lifestone.Net.Types.MessageQueue.SmartBox, 0), new Lifestone.Net.Types.GameEventHeader(self, 901, 0x0022), new Lifestone.Net.Messages.InventoryPutObjInContainerEvent(bowl, self, 0, 0));
        status = status == ScenarioStatus.Running ? rig.Run(scenario, 120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status);

        Assert.Equal(1, scenario.Looted); // the old count stopped at 0: the handle was dropped at the abandon
        Assert.Equal(1, scenario.Tally.Looted);
        Assert.True(rig.Logged("the pick-up in flight at the abandon is counted: looted=1 (1 after the abandon"), "the hunt says it counted it");
        Assert.Contains("looted=1 ", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_2_2_fix_R9_loot_on_with_the_viewed_container_not_the_corpse_logs_one_warning_naming_the_viewed_guid()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        var injected = false;
        const uint Other = 0x80000EEE;
        var ctx = new Lifestone.Net.Dispatch.InboundContext(0, Lifestone.Net.Types.MessageQueue.SmartBox, 0);
        rig.Session.Reducer.OnCreateObject(ctx, Lifestone.Client.Tests.Session.ReducerHarness.Create(Other, Lifestone.Client.Tests.Session.ReducerHarness.Stamps(), null, name: "Chest"), isUpdate: false); // a known object, so a 0196 for it opens it as the viewed ground container
        for (var t = 0.0; t < 600 && !injected; t += 0.1)
        {
            // the open is Confirmed once the corpse's entries have resolved (OpenContainerTracker.Poll); a view of another container takes the viewed slot right then, before the scenario's step reads the confirmed open
            var status = rig.RunHooked(scenario, () =>
            {
                if (!injected && scenario.CorpseWindow && !scenario.CorpseOpen && rig.Session.State.Containers.Viewed is { } viewed && viewed.Guid != Other && viewed.IsResolved && rig.Session.State.Objects.TryGet(viewed.Guid, out var seen) && seen.Name.StartsWith("Corpse of", StringComparison.Ordinal))
                {
                    injected = true;
                    rig.Session.Reducer.OnViewContents(ctx, new Lifestone.Net.Types.GameEventHeader(rig.Session.State.Self.Guid, 900, 0x0196), new Lifestone.Net.Messages.ViewContentsEvent(Other, []));
                }
            });
            Assert.Equal(ScenarioStatus.Running, status);
        }

        Assert.True(injected, "a view of another container was put in place of the corpse's");
        rig.Run(scenario, 5);
        var warnings = rig.Log.Lines.Where(l => l.Contains("--loot is on but the viewed container is", StringComparison.Ordinal)).ToList();
        Assert.True(warnings.Count == 1, "the warning count was " + warnings.Count + " (injected " + injected + ")" + Environment.NewLine + string.Join(Environment.NewLine, rig.Log.Lines.Where(static l => l.Contains("corpse", StringComparison.Ordinal) || l.Contains("loot", StringComparison.Ordinal))));
        Assert.Contains("0x" + Other.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), warnings[0], StringComparison.Ordinal);
        Assert.Contains("the loot did not start", warnings[0], StringComparison.Ordinal);
        Assert.False(rig.Logged("corpse: looting"), "no loop started");
        Assert.Equal(0, scenario.Looted);
    }

    [Fact]
    public void M4_2_2_fix_R7_the_corpse_looting_line_names_the_shop_mask_note_only_for_the_shop_mask()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        Assert.True(rig.Run(scenario, 600) == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Contains(rig.Log.Lines, static l => l.Contains("corpse: looting ", StringComparison.Ordinal) && l.Contains("m4-loot-rules-shop-mask closed", StringComparison.Ordinal));

        using var all = new Rig();
        all.Kit();
        all.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var custom = Make(all, loot: new LootOptions { TypeMask = 0x4 });
        Assert.True(all.Run(custom, 600) == ScenarioStatus.Succeeded, custom.Failure);
        Assert.Contains(all.Log.Lines, static l => l.Contains("corpse: looting ", StringComparison.Ordinal));
        Assert.DoesNotContain(all.Log.Lines, static l => l.Contains("m4-loot-rules-shop-mask", StringComparison.Ordinal));
    }

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

    [Fact]
    public void With_the_corpse_option_off_the_hunt_leaves_the_corpse_alone()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, openCorpse: false);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpseItems);
        Assert.False(rig.Logged("corpse: "));
        Assert.Equal(0u, rig.World.OpenContainerGuid);
    }

    /// <summary>Runs the hunt until its first kill is counted (or the limit), one step at a time; true when it was.</summary>
    [Fact]
    public void M4_2_2_fix2_R15_GuardWhileHeld_abandons_the_corpse_sequence_when_the_health_falls_while_the_scenario_is_held_and_does_nothing_otherwise()
    {
        // The leg holds the scenario for the corpse review frame and does not step it: the guard alone must see the retreat rule.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        Assert.False(scenario.GuardWhileHeld(rig.Session), "no corpse sequence yet: nothing to abandon");
        Assert.True(RunUntil(rig, scenario, () => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);

        Assert.False(scenario.GuardWhileHeld(rig.Session), "health is fine: the sequence goes on");
        Assert.True(scenario.CorpseWindow);

        rig.World.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health while the scenario is held (rig.Run(seconds) ticks the session only: no scenario step)
        rig.Run(0.5);
        Assert.True(scenario.CorpseWindow, "held, so not stepped: the sequence has not noticed");
        Assert.True(scenario.GuardWhileHeld(rig.Session), "the guard sees the retreat rule and abandons the sequence");
        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
        Assert.False(scenario.Looting);
        Assert.False(scenario.CorpseWindow);
        Assert.False(scenario.GuardWhileHeld(rig.Session), "abandoned: nothing more to guard");
        rig.Run(2.0);
        Assert.Equal(0u, rig.World.OpenContainerGuid); // the open view was closed (fire-and-forget)
        Assert.Null(rig.Session.State.Containers.Viewed);
        var status = rig.Run(scenario, 120);
        Assert.NotEqual(ScenarioStatus.Running, status); // stepped again, the hunt ends on the retreat rule
    }

    [Fact]
    public void M4_2_2_fix2_R15_GuardWhileHeld_abandons_the_corpse_sequence_when_the_hunts_seconds_pass_while_the_scenario_is_held()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, seconds: 300);
        Assert.True(RunUntil(rig, scenario, () => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);
        Assert.False(scenario.GuardWhileHeld(rig.Session));

        rig.Run(301); // the clock passes the hunt's 300 s with the scenario held
        Assert.True(scenario.GuardWhileHeld(rig.Session), "the hunt's seconds are checked while held");
        Assert.False(scenario.CorpseWindow);
        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
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
    public void Health_under_the_retreat_share_right_after_the_kill_abandons_the_corpse_within_a_step_and_it_is_not_opened_2_1_R5()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        Assert.True(RunToFirstKill(rig, scenario), scenario.Failure);

        rig.World.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health a tick after the kill, before the corpse comes (2 s)
        var status = rig.Run(scenario, 0.5);

        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), "the abandon is logged within half a second of the drop");
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0u, rig.World.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
        status = status == ScenarioStatus.Running ? rig.Run(scenario, 120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status); // never held for the corpse's 5 s + 20 s + 5 s
        Assert.Equal(0, scenario.CorpsesOpened);
    }

    [Fact]
    public void An_open_abandoned_in_flight_is_closed_when_it_lands_Confirmed_2_1_R10()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        rig.World.CorpseOpenDelaySeconds = 2.0; // the stand-in's corpse is found about 0.6 s after the kill; its 0196 comes 2 s after the open was sent
        var scenario = Make(rig);
        Assert.True(RunToFirstKill(rig, scenario), scenario.Failure);

        rig.World.SetPlayerHealthLater(1.2, 4); // the drop lands while the open is in flight (sent at about 0.6 s, answered at about 2.6 s)
        rig.Run(scenario, 8);

        Assert.True(rig.Logged("the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
        Assert.True(rig.Logged("the abandoned open of"), "the open landed Confirmed after the abandon and the scenario noticed");
        Assert.Equal(0u, rig.World.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
    }

    [Fact]
    public void A_corpse_that_never_appears_is_counted_as_missed_within_5_seconds_and_the_hunt_succeeds_2_1_R5()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        rig.World.WithholdCreatureCorpses = true;
        var scenario = Make(rig);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.CorpsesMissed);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpsesRefused);
        Assert.True(rig.Logged("counted, not a failure"));
    }

    [Fact]
    public void An_open_answered_by_a_UseDone_with_no_0196_is_counted_as_refused_and_the_hunt_succeeds_2_1_R5()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        rig.World.AnswerOpenWithUseDoneOnly = true;
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.CorpsesRefused);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpsesMissed);
        Assert.True(rig.Logged("was not confirmed"));
    }

    [Fact]
    public void A_missile_hunt_swaps_the_weapon_in_the_captures_order_shoots_and_restores_the_mace()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, HuntWeapon.Missile);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.True(scenario.Returned);
        Assert.True(scenario.Restored);
        var lines = rig.Log.Lines.ToList();
        int At(string text) => lines.FindIndex(l => l.Contains(text, StringComparison.Ordinal));
        var order = new[] { At("the Mace to the pack"), At("wield the Shortbow"), At("wield the Arrows"), At("mode: Missile"), At("MissileAttack 1"), lines.FindIndex(l => l.Contains("to the pack", StringComparison.Ordinal) && l.Contains("the Arrows", StringComparison.Ordinal)), At("the Shortbow to the pack"), At("wield the Mace again") };
        Assert.All(order, i => Assert.True(i >= 0, string.Join(" | ", order)));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace is wielded again");
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack");
        Assert.True(rig.Held(FighterKit.ArrowWcid, out var arrows) && !arrows, "the Arrows are in the pack");
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_target_the_server_refuses_is_excluded_and_the_next_one_is_fought()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) =>
        {
            r.PlaceCreature(FirstCreature, Belt, 8, health: 0); // the nearest: a creature to the client, none to the stand-in (a refusal)
            r.PlaceCreature(FirstCreature + 1, Belt, 12);
        };
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(2, scenario.Attacks);
        Assert.True(rig.Logged("was refused"), "the refusal is logged by name");
        Assert.True(rig.Logged("excluding it and picking the next"));
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_with_no_listed_creature_sends_the_hunt_to_the_next_cell()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) =>
        {
            if (n == 2)
            {
                r.PlaceCreature(FirstCreature, Belt, 12);
            }
        };
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(2, scenario.CellsVisited);
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void A_nearer_listed_level_30_creature_is_skipped_for_a_farther_one_in_the_band()
    {
        // Review 1.2-R1: live round 1 fought the Tumerok Warrior (level 30) for the retreat. It is listed (wcid 233) and the band 8-20 filters it; the Field Ursuin (level 8) 20 m away is the target.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) =>
        {
            r.PlaceCreature(FirstCreature, "Tumerok Warrior", 6, health: 1_000_000, wcid: TumerokWcid);
            r.PlaceCreature(FirstCreature + 1, Belt, 20);
        };
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(rig.Logged("acquire: target 'Field Ursuin' wcid 7990 level 8"), "the target's wcid and level are logged");
        Assert.False(rig.Logged("acquire: target 'Tumerok Warrior'"), "the level-30 creature is never the target");
    }

    [Fact]
    public void A_creature_that_is_not_in_the_table_is_never_picked_not_even_as_a_fallback()
    {
        // Review 1.2-R1: the unlisted fallback (any creature within 40 m on the last cell) is gone. The stand-in's Tusker is not a belt creature.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, StandInTusker.Name, 3, wcid: StandInTusker.Wcid);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 1200);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("an unlisted creature is never a target", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(HuntScenario.MaxCells, scenario.CellsVisited);
        Assert.Equal(0, scenario.Attacks);
        Assert.False(rig.Logged("fallback"));
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void A_sequence_that_never_ends_is_cancelled_after_the_bounded_wait_and_its_target_excluded()
    {
        // Review 1.2-R4: a sequence left Active (a lost final 01A7) stalled the hunt silently until its 300 s. Here the sequence is made Active by hand just before the send: no 01A7 will ever come.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        var scenario = Make(rig);
        var forced = false;
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.1)
        {
            if (!forced && scenario.Stage == "fight-send")
            {
                forced = true;
                rig.Session.Reducer.Attack.OnCommence();
                Assert.True(rig.Session.Attack.Active);
            }

            status = rig.Run(scenario, 0.1);
        }

        Assert.True(forced, "the hunt reached the first send");
        Assert.Equal(1, scenario.Stalls);
        Assert.Equal(0, scenario.Attacks);
        Assert.True(rig.Logged("the final 01A7 never came"));
        Assert.True(rig.Logged("sending CancelAttack and excluding"));
        Assert.Equal(ScenarioStatus.Failed, status); // the only creature is excluded: the run ends loudly, not at the 300 s
        Assert.True(scenario.Returned);
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
    public void Under_the_trigger_before_the_first_fight_a_potion_is_drunk_and_the_hunt_goes_on_3_1_s3()
    {
        // 30 of 65 health (46%), no kit, three potions: one potion restores 25 (55 of 65, 85%: over heal-below), and a second would be drunk for 10 missing, so the rule stops.
        using var rig = new Rig(heals: true, vitals: HealthAt(30));
        rig.Kit(potions: 3);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.HealTally.Potions);
        Assert.Equal(0, scenario.HealTally.KitUses);
        Assert.Equal(25, scenario.HealTally.Points);
        Assert.Equal(1, scenario.Heals);
        Assert.Equal(1, scenario.PotionsUsed);
        Assert.True(rig.Logged("heal: potion 0x"), "the decision is logged");
        Assert.True(rig.Logged("heal: the potion restored 25 Health"), "the outcome with the amount is logged");
        Assert.True(LineAt(rig, "heal: the potion restored") < LineAt(rig, "ground: cell"), "the recovery came before the first teleport to the belt");
        Assert.Contains(" potions=1 heal_failed_rolls=0 heal_points=25 heal_wasted=0 ", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the scenario's verdict fields did not change shape
    }

    [Fact]
    public void With_nothing_to_heal_with_the_hunt_waits_300_seconds_for_regeneration_then_fails_loudly_before_any_fight_3_1_s3()
    {
        // 46% health, no kit and no potion: the rule waits (the stand-in does not regenerate), then the 300 s run out: a loud failure, no fight, the run returns (m4-hunt-recovery's rule).
        using var rig = new Rig(heals: true, vitals: HealthAt(30));
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);
        var started = double.NaN;

        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.1)
        {
            status = rig.Run(scenario, 0.1);
            if (double.IsNaN(started) && rig.Logged("regenerating for up to 300 s"))
            {
                started = rig.Session.Clock.Now;
            }
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(double.IsNaN(started), "the wait began");
        Assert.InRange(rig.Session.Clock.Now - started, 300.0, 330.0); // the wait ran its 300 s (the return to Holtburg follows within seconds)
        Assert.StartsWith("recovery:", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("still under heal-below 75% after 300 s of regeneration", scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m4-hunt-recovery)", scenario.Failure, StringComparison.Ordinal); // the row is closed by this item
        Assert.Equal(0, scenario.Attacks);
        Assert.False(rig.Logged("ground: cell"), "no teleport to the belt");
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void Under_the_retreat_share_with_nothing_to_heal_with_the_recovery_fails_at_once_without_a_wait_3_1_s3()
    {
        using var rig = new Rig(heals: true, vitals: HealthAt(10)); // 15% of 65
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 120);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("under the retreat share 30%, so no regeneration wait", scenario.Failure, StringComparison.Ordinal);
        Assert.False(rig.Logged("regenerating for up to"));
        Assert.Equal(0, scenario.Attacks);
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void A_kit_with_a_low_chance_waits_behind_potions_until_the_health_lifts_it_over_the_chance_option_3_1_s3()
    {
        // The capture's case, in peace before the first fight: 43 of 188 health, the chance 0.31 (0.16 in a combat mode): potions (25 each, no roll) until the kit's chance reaches 0.7 (68 of 188 is 0.67, 93 is 0.90), then the kit heals on to 90%.
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: HealthAt(43));
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        var firstPotion = LineAt(rig, "heal: potion 0x");
        var firstKit = LineAt(rig, "heal: kit 0x");
        Assert.True(firstPotion >= 0 && firstKit > firstPotion, $"potion at {firstPotion}, kit at {firstKit}");
        Assert.Contains("the kit's chance 0.31 is under 0.7", rig.Log.Lines[firstPotion], StringComparison.Ordinal);
        Assert.Contains("(145 missing)", rig.Log.Lines[firstPotion], StringComparison.Ordinal);
        Assert.True(scenario.HealTally.Potions >= 1 && scenario.HealTally.KitUses >= 1, scenario.DetailFields());
        Assert.True(scenario.Heals >= 2);
        Assert.Contains("estimated chance 0.", rig.Log.Lines[firstKit], StringComparison.Ordinal);
        Assert.True(rig.Logged("the recovery is over after"), "the recovery ended on its own rule (heal-until)");
        Assert.Equal(1, LineCount(rig, "TODO(m4-heal-skill-read)")); // the stand-in's description lists no Healing: the capture's 190 stands in, loudly, once
        Assert.True(scenario.Tally.Kills >= 1);
    }

    [Fact]
    public void With_the_potions_spent_a_kit_under_the_wanted_chance_is_used_as_the_last_resort_and_the_regeneration_wait_never_starts_3_1_fix_R1()
    {
        // The five potions are drunk at a full vital (spent for nothing, capture C3), then the health falls to 60 of 188 (32%): in peace 128 missing, the chance about 0.56, under the wanted 0.7 and over the floor 0.3. Before the fix the hunt waited
        // 300 s for regeneration and failed with the kit unused; now the kit is the last resort, and says so.
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: [new StandInVitalUpdate(30, 2, 60)]);
        rig.World.HealRandom = new Fixed(0.5); // wins a roll at any chance over 0.5 and is no critical heal
        rig.Kit();
        for (var i = 0; i < 5; i++)
        {
            var drink = rig.Session.Actions.Drink(StandInWorld.HealPotionGuid, ActionSource.Host);
            rig.Run(2.0);
            Assert.True(drink.IsCompleted, drink.Summary);
        }

        rig.Run(30); // past the vital update
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        var first = LineAt(rig, "heal: kit 0x");
        Assert.True(first >= 0, "the kit was used" + Tail(rig));
        Assert.Contains("(128 missing)", rig.Log.Lines[first], StringComparison.Ordinal);
        Assert.Contains("under the 0.7 wanted, but no potion can be drunk and the kit is the last resort before the regeneration wait (at least 0.3 needed, --heal-floor-chance)", rig.Log.Lines[first], StringComparison.Ordinal);
        Assert.False(rig.Logged("regenerating for up to"), "no regeneration wait began");
        Assert.Equal(0, scenario.HealTally.Potions);
        Assert.True(scenario.HealTally.KitUses >= 1);
        Assert.True(LineAt(rig, "heal: kit 0x") < LineAt(rig, "ground: cell"), "the recovery came before the first teleport to the belt");
    }

    [Fact]
    public void Two_lost_rolls_in_a_row_switch_the_recovery_to_a_potion_3_1_s3()
    {
        // 100 of 188 health, every roll lost (the draw is never under the chance): the kit twice, then a potion (the health moves, the count clears), the kit twice, a potion, and so on up to 90%.
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: HealthAt(100));
        rig.World.HealRandom = new Fixed(0.9999);
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(6, scenario.HealTally.KitUses);
        Assert.Equal(6, scenario.HealTally.FailedRolls);
        Assert.Equal(3, scenario.HealTally.Potions); // 100 -> 125 -> 150 -> 175 of 188 (93%)
        Assert.Equal(75, scenario.HealTally.Points);
        Assert.Equal(3, scenario.Heals);
        var firstPotion = LineAt(rig, "heal: potion 0x");
        Assert.True(firstPotion > LineAt(rig, "heal: kit 0x"));
        Assert.Contains("2 rolls lost in a row", rig.Log.Lines[firstPotion], StringComparison.Ordinal);
        Assert.Equal(2, rig.Log.Lines.Take(firstPotion).Count(l => l.Contains("heal: the kit's roll was lost", StringComparison.Ordinal)));
        Assert.Contains("heal_failed_rolls=6 heal_points=75", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Between_two_kills_a_health_share_under_the_trigger_is_healed_before_the_next_creature_is_picked_3_1_s3()
    {
        using var rig = new Rig(healItems: true, stats: Hunter188());
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature + (uint)(n - 1), Belt, 12); // the harness's one creature per arrival, as the next-cell test places it: the second kill is in the next cell
        var scenario = Make(rig, kills: 2);
        Assert.True(RunToFirstKill(rig, scenario), scenario.Failure);

        rig.World.SetPlayerHealthLater(0.05, 80); // 43% right after the first kill: the next creature must wait for the recovery
        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig, 60));
        Assert.Equal(2, scenario.Tally.Kills);
        var recovering = LineAt(rig, "after kill 1 is under heal-below 75%: recovering before the next fight");
        Assert.True(recovering >= 0, "the between-kills recovery started");
        var firstTarget = LineAt(rig, "acquire: target");
        var secondTarget = LineAt(rig, "acquire: target", firstTarget + 1);
        var firstHeal = LineAt(rig, "heal: ", recovering);
        Assert.True(firstTarget < recovering && recovering < firstHeal && firstHeal < secondTarget, $"first target {firstTarget}, recovery {recovering}, first heal {firstHeal}, second target {secondTarget}");
        Assert.True(scenario.Heals >= 1);
        Assert.True(LineAt(rig, "the recovery is over after", recovering) < secondTarget, "the recovery ended before the second creature was acquired");
        Assert.True(scenario.Tally.Looted >= 1);
        Assert.Equal(1, LineCount(rig, "TODO(m4-heal-skill-read)"));
    }

    [Fact]
    public void A_health_above_the_trigger_between_kills_sends_no_heal_3_1_s3()
    {
        using var rig = new Rig(healItems: true, stats: Hunter188());
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature + (uint)(n - 1), Belt, 12); // the harness's one creature per arrival, as the next-cell test places it: the second kill is in the next cell
        var scenario = Make(rig, kills: 2);
        Assert.True(RunToFirstKill(rig, scenario), scenario.Failure);

        rig.World.SetPlayerHealthLater(0.05, 150); // 80%: over heal-below 75%
        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.False(rig.Logged("recovering before the next fight"));
    }

    [Fact]
    public void A_kit_refused_for_an_untrained_healing_is_set_aside_and_the_potions_carry_the_recovery_3_1_s3()
    {
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: HealthAt(100));
        rig.World.HealingTrained = false; // ACE: UseDone 0x04FC at once (capture C4)
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(rig.Logged("heal: the kit is set aside for the run (Healing is untrained, ACE's 0x04FC)"));
        Assert.Equal(0, scenario.HealTally.KitUses); // a refused use is not a use
        Assert.True(scenario.HealTally.Potions >= 1);
        Assert.Equal(1, LineCount(rig, "heal: kit 0x")); // the one try; after it the potions alone
    }

    [Fact]
    public void With_heal_off_the_hunt_runs_no_recovery_at_all_and_fights_at_the_low_health_3_1_s3()
    {
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: HealthAt(80));
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, recovery: new RecoveryOptions { Enabled = false });

        var status = rig.Run(scenario, 900);

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.False(rig.Logged("heal: kit 0x") || rig.Logged("heal: potion 0x"));
        Assert.True(rig.Logged("recovery: heal=off"));
        Assert.True(scenario.Attacks >= 1, "it went and fought at 43% health");
    }

    [Fact]
    public void The_heal_below_option_moves_the_trigger_3_1_s3()
    {
        // 60% health: under the default 75% (a heal), over --heal-below 50 (none).
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: HealthAt(113));
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, recovery: new RecoveryOptions { BelowPercent = 50, UntilPercent = 70 });

        var status = rig.Run(scenario, 900);

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.True(scenario.Attacks >= 1);
        Assert.True(rig.Logged("heal_below=50% heal_until=70%"));
    }

    [Fact]
    public void The_retreat_still_ends_a_fight_at_30_percent_with_a_kit_in_the_pack_because_nothing_heals_during_a_fight_3_1_s3()
    {
        using var rig = new Rig(healItems: true, stats: Hunter188(), vitals: [new StandInVitalUpdate(45, 2, 20)]);
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("retreat", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions); // the kit and five potions were in the pack and none was used
        Assert.True(rig.Logged("TODO(m4-hunt-infight-heal)"), "the unbuilt in-fight heal is named in the log");
        Assert.True(scenario.Returned);
        Assert.False(rig.Session.Attack.Active);
    }

    [Fact]
    public void A_target_thirty_metres_away_is_walked_to_in_fifteen_metre_approaches_retried_after_YouChargedTooFar()
    {
        // Live round 2: ACE cancels a melee approach once the player is 15 m from where it began (028A 0x003D then 01A7 0x0036); the band's targets are 15-60 m away.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(scenario.ChargeRetries >= 1, "the first approach is cancelled at 15 m");
        Assert.True(rig.Logged("YouChargedTooFar (0x003D)"), "the WeenieError is logged by name");
        Assert.True(rig.Logged("retry 1 of"));
        Assert.False(rig.Logged("no sequence began within"), "the hunt reacts to the 01A7 at once, not after 15 s");
    }

    /// <summary>Takes the client's reported positions to the stand-in (the offline session sends no movement), as ACE takes the client's autonomous positions.</summary>
    private sealed class ClientToStandIn(Rig rig) : Lifestone.Client.Session.Contracts.IMovementSink
    {
        public int Positions { get; private set; }

        public void MoveToState(Lifestone.Data.Motion.RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) => Report(position);

        public void AutonomousPosition(in CellFrame position, bool contact) => Report(position);

        public void Jump(float extent, Vector3 velocity, in CellFrame position) => Report(position);

        private void Report(in CellFrame position)
        {
            Positions++;
            rig.World.ClientPosition(position);
        }
    }

    [RequiresDatsFact]
    public void A_target_thirty_metres_away_is_walked_to_by_the_clients_own_body_when_the_server_sends_only_the_MoveToObject()
    {
        // Live round 3: ACE's approach is its MoveToObject F74C and no F748 for the player; the client's physics carries it out and reports its positions. Headless, the hunt's own body (HuntFollow) does.
        var dats = datsFixture.Dats;
        using var rig = new Rig();
        rig.Kit();
        rig.World.ApproachSendsPositions = false;
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);
        var sink = new ClientToStandIn(rig);
        using var follow = new HuntFollow(dats, rig.Log, _ => sink);
        var scenario = new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Melee, Kills = 1, Seconds = 600 }, rig.Log, cell => HuntGround.TerrainHeight(dats, cell), endSession: false, follow: follow);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(sink.Positions > 10, "the client's body reported its positions: " + sink.Positions);
        Assert.True(scenario.ChargeRetries >= 1, "the first approach is cancelled at 15 m and retried from where the client's body stands");
        Assert.True(rig.Logged("the server moves the player: the hunt's own body follows"), "the body followed the MoveToObject");
        Assert.True(rig.Logged("the client is 3"), "the client's distance is logged at each send");
        Assert.False(rig.Logged("did not close in"), "no target excluded for a walk that did not close");
    }

    [Fact]
    public void Without_the_clients_own_body_a_MoveToObject_with_no_F748_moves_nothing_and_the_far_target_is_excluded()
    {
        // The live round 3 failure, kept as the control for the test above: the hunt measured the closure from a position that never moved.
        using var rig = new Rig();
        rig.Kit();
        rig.World.ApproachSendsPositions = false;
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);
        var scenario = Make(rig);

        rig.Run(scenario, 300);

        Assert.True(rig.Logged("did not close in") || rig.Logged("no sequence began within"), "no body walked the MoveTo");
        Assert.Equal(0, scenario.Tally.Kills);
    }

    [Fact]
    public void Two_Arrow_stacks_are_merged_into_the_largest_which_is_wielded_by_its_guid_and_put_back_in_the_pack()
    {
        // Live round 3: the fighter-build top-up had added a second Arrow stack, and the wield of the first one found was not seen in the read model.
        using var rig = new Rig();
        rig.Kit(); // 20 arrows
        var second = rig.Spawn(FighterKit.ArrowWcid, 30);
        var before = rig.Guids(FighterKit.ArrowWcid);
        Assert.Equal(2, before.Count);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, HuntWeapon.Missile);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(rig.Logged("merge the Arrows"), "the stacks were merged");
        var after = rig.Guids(FighterKit.ArrowWcid);
        Assert.Single(after);
        Assert.Equal(second, after[0]); // the larger (30) stack is the one that remains
        Assert.True(rig.Logged($"wield the Arrows 0x{second:X8}"), "the wield names the stack by its guid");
        Assert.False(rig.Equipped(second), "the stack is back in the pack");
        Assert.True(scenario.Restored);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace is wielded again");
    }

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
    public void A_second_Shortbow_in_the_pack_does_not_stall_the_swap_and_the_restore_ends_with_the_Mace_and_no_launcher_3_1_fix2()
    {
        // The live round 4 defect: the loot adds a Shortbow, the pack copy is listed before the equipped one, and the lookup that returned the first of the class read "not wielded" for ever.
        using var rig = new Rig();
        rig.Kit();
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var first = rig.Guids(FighterKit.ShortbowWcid).Single();
        var second = rig.Spawn(FighterKit.ShortbowWcid);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, HuntWeapon.Missile);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.False(rig.Logged("still waiting"), "the swap's wait ended");
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace is wielded again");
        Assert.False(rig.Equipped(first) || rig.Equipped(second), "no Shortbow is wielded");
        Assert.Equal(2, rig.Guids(FighterKit.ShortbowWcid).Count);
        Assert.DoesNotContain(rig.Guids(FighterKit.ArrowWcid), rig.Equipped);
        // The Shortbow the swap wielded went back by its own guid, and before the Mace; the other copy was never touched.
        var wielded = rig.Sends.Single(x => x.Type == GameActionTypes.GetAndWieldItem && x.Second == (uint)EquipMask.MissileWeapon).First;
        Assert.Contains(wielded, new[] { first, second });
        var other = wielded == first ? second : first;
        Assert.DoesNotContain(rig.Sends, x => x.First == other);
        var bowBack = rig.LastSend(GameActionTypes.PutItemInContainer, wielded);
        var maceBack = rig.LastSend(GameActionTypes.GetAndWieldItem, mace);
        Assert.True(bowBack >= 0 && maceBack > bowBack, $"the Shortbow to the pack ({bowBack}) before the Mace is wielded ({maceBack})");
        Assert.True(rig.Logged($"wield the Shortbow 0x{wielded:X8}: confirmed") && rig.Logged($"the Shortbow to the pack 0x{wielded:X8}: confirmed"), "each step is logged by guid");
    }

    [Fact]
    public void A_looted_second_Mace_in_the_pack_is_left_there_the_wielded_one_is_swapped_and_restored_3_1_fix2()
    {
        using var rig = new Rig();
        rig.Kit();
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var looted = rig.Spawn(FighterKit.MaceWcid); // in the pack, listed before the wielded Mace
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, HuntWeapon.Missile);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace the hunt put aside is the one wielded again");
        Assert.False(rig.Equipped(looted), "the looted Mace stays in the pack");
        Assert.DoesNotContain(rig.Sends, x => x.First == looted);
        Assert.Equal(2, rig.Guids(FighterKit.MaceWcid).Count);
        Assert.True(rig.Logged($"the Mace to the pack (ACE refuses the bow while it is wielded) 0x{mace:X8}"), "the step names the wielded Mace");
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack");
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
    public void A_missile_hunt_that_starts_from_a_half_done_swap_goes_on_from_the_wielded_Shortbow_and_restores_the_Mace_3_1_fix2()
    {
        using var rig = new Rig();
        rig.Kit();
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var bow = rig.Guids(FighterKit.ShortbowWcid).Single();
        rig.MoveToPack(mace);
        rig.Wield(bow, EquipMask.MissileWeapon);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, HuntWeapon.Missile);

        var status = rig.Run(scenario, 600);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.False(rig.Logged("the Mace to the pack (ACE"), "the Mace was already in the pack: the swap does not move it again");
        Assert.False(rig.Logged("wield the Shortbow"), "the Shortbow was already wielded: it is not wielded again");
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace) && !rig.Equipped(bow));
    }

    [Fact]
    public void A_target_near_the_edge_of_the_band_range_takes_several_retries()
    {
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 58);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 1200);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.ChargeRetries >= 3, scenario.ChargeRetries.ToString());
    }

    [Fact]
    public void A_health_fall_under_thirty_percent_mid_fight_cancels_the_attack_and_returns_with_the_run_failed()
    {
        using var rig = new Rig(vitals: [new StandInVitalUpdate(45, 2, 4)]);
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        var scenario = Make(rig);

        var status = rig.Run(scenario, 900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("retreat", scenario.Failure, StringComparison.Ordinal);
        Assert.True(scenario.Returned);
        Assert.Equal(0, scenario.Tally.Kills);
        Assert.False(rig.Session.Attack.Active);
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
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

    [Fact]
    public void A_forest_ground_is_accepted_the_log_names_it_and_a_belt_creature_is_never_its_target_3_1()
    {
        // M4 3.1 s0: the forest row is in use (--hunt-ground=forest). The stand-in's creature is a belt creature (Field Ursuin, wcid 7990, level 8), not in the forest table, so the run ends at the "nothing near" path
        // of every cell: the forest target rule is the belt's (nearest creature whose wcid is in the ground's table and whose table level is in its band), and the hunt attacks nothing.
        using var rig = new Rig();
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var options = new HuntOptions { Ground = HuntGroundChoice.Forest };
        var scenario = new HuntScenario(options, rig.Log, _ => 40f, options.GroundRow, endSession: false);

        var status = rig.Run(scenario, 1200);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.True(rig.Logged("ground Low North Forest Mix:"), "the log names the ground");
        Assert.True(rig.Logged($"{HuntGround.LowNorthForestMix.UsableCells.Count()} usable cell(s) of {HuntGround.LowNorthForestMix.Cells.Count}"));
        Assert.Contains("no creature of the table in the band 30-40", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Attacks);
        Assert.Equal(HuntScenario.MaxCells, scenario.CellsVisited);
        Assert.True(scenario.Returned);
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_that_is_not_in_use_is_refused_and_the_options_parse()
    {
        using var rig = new Rig();
        var unused = HuntGround.LowNorthForestMix with { InUse = false };
        var refused = Assert.Throws<ArgumentException>(() => new HuntScenario(new HuntOptions(), rig.Log, _ => 40f, unused));
        Assert.Contains("TODO(m4-hunt-ground-unused)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Low North Forest Mix", refused.Message, StringComparison.Ordinal);
        Assert.NotNull(new HuntScenario(new HuntOptions(), rig.Log, _ => 40f, HuntGround.LowNorthForestMix)); // the forest row itself is in use now
        var o = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=3", "--hunt-seconds=90", "--hunt-weapon=missile"]));
        Assert.Empty(o.Errors);
        Assert.Equal((3, 90.0, HuntWeapon.Missile), (o.Kills, o.Seconds, o.Weapon));
        Assert.True(o.OpenCorpse, "--hunt-open-corpse is on by default");
        Assert.False(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-open-corpse=off"])).OpenCorpse);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-open-corpse=maybe"])).Errors);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=0"])).Errors);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=spear"])).Errors);
        Assert.Matches(Verdict, new HuntScenario(new HuntOptions(), rig.Log, _ => 40f).VerdictFields());
    }

    // ------------------------------------------------------------------------------------------------ M4 3.2 s4a: the sale, the buy and the appraisal at the loop's end (over s3's stand-in vendors)

    private const uint BracersWcid = 36;
    private const uint HandyKitWcid = FighterKit.HealingKitWcid;
    private const uint PyrealWcid = 273;

    private static int FindLine(Rig rig, string text) => rig.Log.Lines.ToList().FindIndex(l => l.Contains(text, StringComparison.Ordinal));

    private static int SendsOf(Rig rig, uint type) => rig.Sends.Count(s => s.Type == type);

    private static Rig ShopRig()
    {
        var rig = new Rig(vendors: true);
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        return rig;
    }

    [Fact]
    public void M4_3_2_s4a_the_loop_sells_what_Renalds_mask_takes_after_the_return_keeps_its_own_kit_and_records_the_sale()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: true);
        var coinsBefore = rig.World.Coins;

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Null(scenario.Failure);
        Assert.Equal(3, scenario.Sold); // the Leather Bracers the player holds and the corpse's Bowl and Smock
        Assert.Equal("ok", scenario.SellOutcome);
        Assert.Equal(rig.World.Coins - coinsBefore, scenario.Pyreals);
        Assert.Equal(990 + 9 + 22, scenario.Pyreals); // floor(0.9 x Value + 0.1) per stack: 1,100, 10 and 25
        Assert.False(Holds(rig, BracersWcid) || Holds(rig, StandInWorld.BowlWcid) || Holds(rig, StandInWorld.SmockWcid), "what Renald takes is gone from the pack");
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace stays wielded");
        Assert.True(Holds(rig, FighterKit.ShortbowWcid) && Holds(rig, FighterKit.ArrowWcid) && Holds(rig, HandyKitWcid), "the template's weapons, the arrows and the kit are never sold");
        Assert.True(Holds(rig, PyrealWcid));
        Assert.Equal(1, SendsOf(rig, 0x0060)); // one batch: the capture shows one sale is one UseDone either way
        Assert.True(scenario.Returned);
        Assert.False(scenario.VendorWindow);
        Assert.Equal(0u, scenario.SaleVendor);
        Assert.Equal(0xA9B4u, rig.Session.State.Self.Position!.Value.CellId >> 16);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the verdict fields did not change shape
        Assert.Contains("sold=3 bought=0 pyreals=1021 appraised=2 sell=ok buy=off buy_pyreals=0", scenario.DetailFields(), StringComparison.Ordinal);

        // the order: home first, then the shop, the open, the sale, the way home again
        var home = FindLine(rig, "wind-down: back in Holtburg");
        var go = FindLine(rig, "vendors: going to Shopkeeper Renald the Elder");
        var open = FindLine(rig, "vendors: Renald the Elder is open");
        var done = FindLine(rig, "sell: done sold=3 of 3 pyreals=1021");
        var back = FindLine(rig, "vendors: done; back to Holtburg");
        Assert.True(home >= 0 && home < go && go < open && open < done && done < back, $"{home} {go} {open} {done} {back}" + Tail(rig));
        Assert.True(FindLine(rig, "sell: rule types 0x480467A7 (the vendor's 0062)") >= 0, "the mask is the 0062's, not the shard's");
        Assert.Equal(1, LineCount(rig, "sell: rule "));
        Assert.True(FindLine(rig, "vendors: going to Shopkeeper Renald the Elder (shop) +1.5 m") >= 0, "3.2-R14: the hunter is placed 1.5 m beside the vendor, not on his spot");
    }

    [Fact]
    public void M4_3_2_s4a_a_missile_hunt_sells_its_loot_but_never_the_bow_or_the_arrows_the_restore_put_in_the_pack()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, HuntWeapon.Missile, sell: true);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.True(scenario.Restored);
        Assert.Equal(3, scenario.Sold);
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack and stays there");
        Assert.True(Holds(rig, FighterKit.ArrowWcid));
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace);
    }

    [Fact]
    public void M4_3_2_s4a_sell_off_keeps_the_loot_and_visits_no_one()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: false);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Equal(0, scenario.Sold);
        Assert.Equal("off", scenario.SellOutcome);
        Assert.True(Holds(rig, StandInWorld.BowlWcid) && Holds(rig, StandInWorld.SmockWcid) && Holds(rig, BracersWcid), "nothing was sold");
        Assert.Equal(0, SendsOf(rig, 0x0060));
        Assert.Equal(0, LineCount(rig, "vendors: "));
        Assert.Contains(" sold=0 bought=0 pyreals=0 appraised=2 sell=off buy=off", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_3_2_s4a_with_fewer_kits_than_buy_kits_the_loop_buys_the_missing_ones_at_Thelnoth_at_the_predicted_price()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: false, buyKits: 3); // the player holds one Handy Healing Kit (the stand-in's): two are missing
        var coinsBefore = rig.World.Coins;

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Equal(2, scenario.Bought);
        Assert.Equal("ok", scenario.BuyOutcome);
        Assert.Equal(3, rig.Guids(HandyKitWcid).Count);
        Assert.Equal(coinsBefore - (2 * 14), rig.World.Coins); // ceil(1.35 x 10 - 0.1) = 14 a kit, each its own stack
        Assert.Contains("bought=2 pyreals=0", scenario.DetailFields(), StringComparison.Ordinal); // the sale's delta only; the price is buy_pyreals
        Assert.Contains("buy=ok buy_pyreals=28", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(1, SendsOf(rig, 0x005F));
        Assert.Equal(0, SendsOf(rig, 0x0060));
        Assert.True(FindLine(rig, "vendors: going to Thelnoth Cort the Healer") > FindLine(rig, "wind-down: back in Holtburg"));
        Assert.True(FindLine(rig, "buy: done bought=2 of 2 cost=28") >= 0, Tail(rig));
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void M4_3_2_s4a_the_sale_comes_first_and_the_purchase_follows_in_the_same_trip_each_vendor_has_its_own_window()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: true, buyKits: 3);
        var sampler = new VendorFrameSampler();
        var windows = 0;
        var previous = false;
        var sawSaleVendor = false;
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.1)
        {
            status = rig.Run(scenario, 0.1);
            sampler.Track(scenario.VendorWindow);
            sawSaleVendor |= scenario.SaleVendor != 0 && scenario.VendorWindow;
            windows += scenario.VendorWindow && !previous ? 1 : 0;
            previous = scenario.VendorWindow;
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Equal(3, scenario.Sold);
        Assert.Equal(2, scenario.Bought);
        Assert.Equal(2, windows);
        Assert.Equal(2, sampler.Windows);
        Assert.True(sawSaleVendor, "SaleVendor names Renald while his window is open");
        Assert.False(scenario.VendorWindow);
        Assert.True(FindLine(rig, "sell: done") < FindLine(rig, "vendors: going to Thelnoth Cort the Healer"), "Renald first, Thelnoth after the sale's coins");
        Assert.Equal(1, LineCount(rig, "vendors: done; back to Holtburg"));
    }

    [Fact]
    public void M4_3_2_s4a_buy_kits_0_is_off_and_enough_kits_held_means_no_trip_to_Thelnoth()
    {
        using var off = ShopRig();
        var zero = Make(off, sell: false, buyKits: 0);
        Assert.True(off.Run(zero, 900) == ScenarioStatus.Succeeded, zero.Failure);
        Assert.Equal(0, zero.Bought);
        Assert.Equal("off", zero.BuyOutcome);
        Assert.Equal(0, SendsOf(off, 0x005F));
        Assert.Equal(0, LineCount(off, "vendors: "));

        using var enough = ShopRig();
        var held = Make(enough, sell: false, buyKits: 1); // the one the stand-in announces is enough
        Assert.True(enough.Run(held, 900) == ScenarioStatus.Succeeded, held.Failure);
        Assert.Equal(0, held.Bought);
        Assert.Equal("enough", held.BuyOutcome);
        Assert.Equal(0, SendsOf(enough, 0x005F));
        Assert.Equal(0, LineCount(enough, "vendors: "));
        Assert.True(FindLine(enough, "buy: 1 Handy Healing Kit(s) held, --buy-kits 1; no trip to Thelnoth") >= 0);
    }

    [Fact]
    public void M4_3_2_s4a_appraise_loot_identifies_each_looted_item_once_and_off_identifies_nothing()
    {
        using var on = ShopRig();
        var scenario = Make(on);
        Assert.True(on.Run(scenario, 900) == ScenarioStatus.Succeeded, scenario.Failure + Tail(on));
        Assert.Equal(2, scenario.Looted);
        Assert.Equal(2, scenario.Appraised); // the Bowl and the Smock, not the pack's older items
        Assert.Equal(2, SendsOf(on, 0x00C8));
        Assert.Equal(2, LineCount(on, "appraise: identifying the looted "));
        Assert.Contains("appraised=2", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(2, scenario.Tally.Appraised);

        using var off = ShopRig();
        var quiet = Make(off, appraise: false);
        Assert.True(off.Run(quiet, 900) == ScenarioStatus.Succeeded, quiet.Failure);
        Assert.Equal(2, quiet.Looted);
        Assert.Equal(0, quiet.Appraised);
        Assert.Equal(0, SendsOf(off, 0x00C8));
        Assert.Contains("appraised=0", quiet.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_3_2_s4a_a_shop_that_is_not_in_view_is_reported_and_the_hunt_still_returns_and_succeeds()
    {
        using var rig = new Rig(); // no vendors in this stand-in
        rig.Kit();
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        var scenario = Make(rig, sell: true, buyKits: 2);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Null(scenario.Failure);
        Assert.True(scenario.Returned);
        Assert.Equal(0, scenario.Sold);
        Assert.StartsWith("failed:", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.Contains("is not in view", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.StartsWith("failed:", scenario.BuyOutcome, StringComparison.Ordinal);
        Assert.Contains("sell=failed:", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(" ", scenario.DetailFields().Split("sell=")[1].Split(' ')[0], StringComparison.Ordinal);
        Assert.True(FindLine(rig, "a failed shop visit never fails the hunt") >= 0);
        Assert.True(FindLine(rig, "vendors: done; back to Holtburg") > 0, "the way home and the log off are still reached");
        Assert.Equal(0, SendsOf(rig, 0x0060));
    }

    [Fact]
    public void M4_3_2_s4a_a_sale_the_server_refuses_is_reported_as_failed_and_the_loop_goes_on_to_its_log_off()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: true);
        var busied = false;
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.1)
        {
            // as soon as Renald's stock is in, a Use of Thelnoth goes straight to the wire (not through the action lanes): the stand-in is busy when the sale arrives, and answers 00A0 then UseDone 0x001D
            status = rig.RunHooked(scenario, () =>
            {
                if (!busied && scenario.Stage == "vendor-open" && rig.Session.State.Vendor is { } stock && stock.Vendor == scenario.SaleVendor)
                {
                    busied = true;
                    rig.Session.Reducer.LocalResponder!.Session.SendUse(StandInWorld.ThelnothGuid);
                }
            });
            if (status != ScenarioStatus.Running)
            {
                break;
            }
        }

        Assert.True(busied, "the hook ran");
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Null(scenario.Failure);
        Assert.Equal(0, scenario.Sold);
        Assert.StartsWith("failed:", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.True(scenario.Returned);
        Assert.True(Holds(rig, StandInWorld.BowlWcid), "nothing was sold");
        Assert.True(FindLine(rig, "sell: nothing was sold (failed:") >= 0, Tail(rig));
        Assert.True(FindLine(rig, "vendors: done; back to Holtburg") > 0);
    }

    [Fact]
    public void M4_3_2_s4a_a_purchase_the_server_refuses_for_want_of_pyreals_is_reported_and_the_hunt_succeeds()
    {
        using var rig = ShopRig();
        rig.World.SetCoins(0); // the server's purse is empty; the client's model still shows 10,000 (SetCoins sends nothing)
        var scenario = Make(rig, sell: false, buyKits: 3);

        var status = rig.Run(scenario, 900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Null(scenario.Failure);
        Assert.Equal(0, scenario.Bought);
        Assert.StartsWith("failed:", scenario.BuyOutcome, StringComparison.Ordinal);
        Assert.Single(rig.Guids(HandyKitWcid));
        Assert.True(scenario.Returned);
        Assert.True(FindLine(rig, "buy: failed:") >= 0, Tail(rig));
    }

    [Fact]
    public void M4_3_2_s4a_nothing_the_shop_takes_means_no_trip_and_a_failed_hunt_sells_nothing()
    {
        using var rig = ShopRig();
        var scenario = Make(rig, sell: true, loot: new LootOptions { Enabled = false });
        // with loot off the pack holds only the stand-in's Leather Bracers to sell: one item
        Assert.True(rig.Run(scenario, 900) == ScenarioStatus.Succeeded, scenario.Failure + Tail(rig));
        Assert.Equal(1, scenario.Sold);

        using var failed = ShopRig();
        failed.OnBelt = null;
        var hunt = Make(failed, sell: true);
        Assert.True(failed.Run(hunt, 900) == ScenarioStatus.Failed); // no creature at all near: the hunt fails loudly, still returns
        Assert.True(hunt.Returned);
        Assert.Equal(0, hunt.Sold);
        Assert.StartsWith("skipped:", hunt.SellOutcome, StringComparison.Ordinal);
        Assert.Equal(0, SendsOf(failed, 0x0060));
    }

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
