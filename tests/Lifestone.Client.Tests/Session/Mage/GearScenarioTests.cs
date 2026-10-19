using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using DatReaderWriter.Enums;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M3 1.4 s1, the gear scenario (<see cref="GearScenario"/>) against the offline stand-in, which answers a wield in ACE's order (<c>Player_Inventory.cs</c> <c>TryEquipObjectWithNetworking</c> 297-306: 0x02DA Wielder 0, 0x02CE 0, then
/// GameEvent 0x0023): a first run grants the kit, wields the wand and wears the five pieces (<c>gear=wielded worn=5</c>, <c>gear_steps</c> above 0); a second run on the same state sends nothing (<c>gear_steps=0</c>); a piece already worn is
/// skipped; a refused wield (GameEvent 0x00A0) and a refused grant end <c>gear=failed</c> with the reason. The whole real kit (59 lines, needs the dats) is run once to size the timeouts.
/// </summary>
public sealed class GearScenarioTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly Regex Verdict = new(@"^gear=(wielded|failed) worn=\d kit=\d+/\d+ gear_steps=\d+ topup=\d+ cast=(ok|fizzled|failed|n/a)$", RegexOptions.CultureInvariant);

    private static GearItem Armour(GearWeenie w) =>
        new(w.Wcid, w.Name, GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

    private static GearItem Wand() =>
        new(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand.WeenieType, GearKit.Wand.ItemType, GearKit.Wand.ValidLocations, 1, null, GearKit.Wand.Burden);

    private static GearItem Scarab(int stack) =>
        new(691, "Lead Scarab", GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, stack, 100, GearKit.ComponentBurden);

    /// <summary>The wand, the five pieces and 20 Lead Scarabs (7 requests, 6 wields).</summary>
    private static GearKit SmallKit() => GearKit.Of([Wand(), .. GearKit.Armour.Select(Armour), Scarab(20)]);

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        /// <summary>The profile's creature spells as the Rig gave the stand-in and the Action API (by id); a test changes an entry's mana before it hands the table to a scenario.</summary>
        public Dictionary<uint, SpellInfo> Table { get; } = [];

        public Rig(StandInPlayerStats? stats = null, ImmutableArray<StandInVitalUpdate> vitals = default, IReadOnlyDictionary<uint, StandInItemKind>? items = null)
        {
            var visit = stats is null ? new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 } : new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Stats = stats };
            if (!vitals.IsDefault)
            {
                visit = visit with { VitalUpdates = vitals };
            }

            if (items is not null)
            {
                visit = visit with { Items = items };
            }

            Log = new RecordingLogSink();
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                Log);
            // The profile's spells as the SpellTable shapes them (self enchantments), so the stand-in and the cast tracker know each one's kind and power.
            var table = Table;
            foreach (var line in ArenaProfile.Lines.Where(l => l.Role == ProfileRole.CreatureBuff))
            {
                foreach (var spell in line.Spells)
                {
                    table[spell.Id] = WorldActionRig.Spell(spell.Id, DatReaderWriter.Enums.SpellType.Enchantment, SpellFlags.SelfTargeted) with
                    {
                        Name = spell.Name,
                        Category = (uint)(ArenaProfile.Lines.ToList().IndexOf(line) + 1),
                        Power = spell.Power,
                        Duration = spell.DurationSeconds,
                        School = line.School,
                    };
                }
            }

            World.SpellLookup = id => table.GetValueOrDefault(id);
            Session.ActionApi.SpellLookup = World.SpellLookup;
            for (var t = 0.0; t < 2.0; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        /// <summary>The mage knows <paramref name="ids"/> (the mod's learn verb, answered by the stand-in).</summary>
        public void Learn(params uint[] ids)
        {
            var learn = Session.Tools.LearnSpells(ids, ActionSource.Host);
            for (var t = 0.0; t < 3.0 && !learn.IsCompleted; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }

            Assert.True(learn.IsCompleted, learn.Summary);
        }

        /// <summary>Advances the session clock by <paramref name="seconds"/> and ticks the session once (no scenario).</summary>
        public void Tick(double seconds)
        {
            _time.Advance(TimeSpan.FromSeconds(seconds));
            Session.Tick(Session.Clock.Now);
        }

        public GameSession Session { get; }

        public RecordingLogSink Log { get; }

        public StandInResponder Responder => Session.Reducer.LocalResponder!;

        public StandInWorld World => Responder.World!;

        public PlayerInventory Inventory => Session.State.Self.Inventory;

        /// <summary>Steps the scenario until it ends or <paramref name="limit"/> session seconds pass (the kit's requests go out at 2 a second).</summary>
        public ScenarioStatus Run(GearScenario scenario, double limit = 240)
        {
            var status = ScenarioStatus.Running;
            for (var t = 0.0; t < limit && status == ScenarioStatus.Running; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
                status = scenario.Step(Session, Session.Clock.Now);
            }

            return status;
        }

        public void Dispose() => Session.Dispose();
    }

    private static int WornPieces(Rig rig) =>
        GearKit.Armour.Count(a => rig.Inventory.Equipped.Any(e => rig.Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == a.Wcid && e.Location != 0 && (e.Location & ~(uint)a.ValidLocations) == 0));

    [Fact]
    public void A_first_run_grants_the_kit_wields_the_wand_and_wears_the_five_pieces_in_ACEs_wire_order()
    {
        using var rig = new Rig();
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var verdict = scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=n/a", verdict); // 6 requests (the wand and the five pieces) and 6 wields; the scarabs are the top-up
        Assert.True(scenario.GearSteps > 0);
        Assert.Equal(6, scenario.SentWields.Count);
        Assert.StartsWith("wield The Healer's Heart ", scenario.SentWields[0], StringComparison.Ordinal);
        Assert.Equal(5, WornPieces(rig));
        var wand = rig.Inventory.Equipped.Single(e => rig.Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == GearKit.Wand.Wcid);
        Assert.Equal(EquipMask.Held, (EquipMask)wand.Location);
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("verdict gear=wielded worn=5", StringComparison.Ordinal));

        // ACE's order: each wield is answered by 0x02DA Wielder 0 and 0x02CE 0 before GameEvent 0x0023 (Creature_Equipment.cs 354-368).
        var log = rig.World.AnswerLog;
        Assert.Equal(6, log.Count(a => a == "0023"));
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i] == "0023")
            {
                Assert.Equal(["02DA", "02CE"], log.Skip(i - 2).Take(2));
            }
        }

        // The coat goes out with its whole ValidLocations mask (chest, abdomen, upper and lower arm).
        var coat = GearKit.Armour.Single(a => a.Name == "Greater Amuli Shadow Coat");
        Assert.Contains(rig.Responder.ActionSends, s => s.Type == GameActionTypes.GetAndWieldItem && s.Second == (uint)coat.ValidLocations);
    }

    // ---------------------------------------------------------------- M3 4.2 P7 (D43): the swap on a mage already geared

    private const uint OldWandWcid = 2472;

    /// <summary>The kit before P7: the Wand and the five leather pieces (the earlier GearKit rows, the world database's), as a mage built by an earlier run wears them.</summary>
    private static GearItem[] OldKitLines() =>
    [
        new(OldWandWcid, "Wand", GearKind.Wand, GearKit.WeenieTypeCaster, GearKit.ItemTypeCaster, EquipMask.Held, 1, null, 50),
        new(45, "Leather Cap", GearKind.Armour, GearKit.WeenieTypeClothing, GearKit.ItemTypeArmor, EquipMask.HeadWear, 1, null, 100),
        new(47, "Leather Coat", GearKind.Armour, GearKit.WeenieTypeClothing, GearKit.ItemTypeArmor, EquipMask.ChestArmor | EquipMask.AbdomenArmor | EquipMask.UpperArmArmor | EquipMask.LowerArmArmor, 1, null, 810),
        new(81, "Leather Leggings", GearKind.Armour, GearKit.WeenieTypeClothing, GearKit.ItemTypeArmor, EquipMask.UpperLegArmor | EquipMask.LowerLegArmor, 1, null, 960),
        new(56, "Leather Gauntlets", GearKind.Armour, GearKit.WeenieTypeClothing, GearKit.ItemTypeArmor, EquipMask.HandWear, 1, null, 270),
        new(115, "Leather Boots", GearKind.Armour, GearKit.WeenieTypeClothing, GearKit.ItemTypeArmor, EquipMask.LowerLegWear | EquipMask.FootWear, 1, null, 420),
    ];

    /// <summary>The stand-in's own weenies for the earlier kit: the stand-in knows only the current kit's weenies, so the old ones come in through the visit.</summary>
    private static Dictionary<uint, StandInItemKind> OldKitWeenies() => OldKitLines().ToDictionary(i => i.Wcid, i => new StandInItemKind(i.Name, null));

    private static void Pump(Rig rig, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.1)
        {
            rig.Tick(0.1);
        }
    }

    private static bool WornWcid(Rig rig, uint wcid) =>
        rig.Inventory.Equipped.Any(e => e.Location != 0 && rig.Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

    private static bool InPack(Rig rig, uint wcid) =>
        rig.Inventory.Pack.Any(e => rig.Session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

    [Fact]
    public void A_mage_wearing_the_leather_kit_ends_wearing_exactly_the_new_kit_with_the_old_pieces_in_the_pack_and_a_second_run_sends_nothing()
    {
        using var rig = new Rig(items: OldKitWeenies());
        var old = new GearScenario(GearKit.Of(OldKitLines()), rig.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(old));
        Assert.Equal("gear=wielded worn=5 kit=6/6 gear_steps=12 topup=0 cast=n/a", old.VerdictFields());
        Assert.All(OldKitLines(), l => Assert.True(WornWcid(rig, l.Wcid), l.Name));

        var kit = GearKit.Of([Wand(), .. GearKit.Armour.Select(Armour)]);
        var swap = new GearScenario(kit, rig.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(swap));

        // 6 grants, 6 pieces taken off (the old wand and the five leathers), 6 wields.
        Assert.Equal("gear=wielded worn=5 kit=6/6 gear_steps=18 topup=0 cast=n/a", swap.VerdictFields());
        Assert.Equal(6, swap.SentWields.Count(w => w.StartsWith("take off", StringComparison.Ordinal)));
        Assert.Equal(12, swap.SentWields.Count);
        Assert.Equal(5, WornPieces(rig));
        Assert.True(WornWcid(rig, GearKit.Wand.Wcid));
        Assert.All(OldKitLines(), l => Assert.False(WornWcid(rig, l.Wcid), l.Name + " is still worn"));
        Assert.All(OldKitLines(), l => Assert.True(InPack(rig, l.Wcid), l.Name + " is not in the pack"));
        Assert.Equal(6, rig.Inventory.Equipped.Count); // nothing else is worn

        // The take-offs came before the wield that needed the place: the coat's wield follows the leather coat's take-off.
        var coat = GearKit.Armour.Single(a => a.Name == "Greater Amuli Shadow Coat");
        var sends = rig.Responder.ActionSends;
        var coatWield = sends.Select((s, i) => (s, i)).Last(p => p.s.Type == GameActionTypes.GetAndWieldItem && p.s.Second == (uint)coat.ValidLocations).i;
        Assert.Contains(sends.Take(coatWield), s => s.Type == GameActionTypes.PutItemInContainer);

        // A second run sends nothing: every kit line is worn and nothing else covers its places.
        var sent = rig.Responder.ActionSends.Count;
        var again = new GearScenario(kit, rig.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(again));
        Assert.Equal("gear=wielded worn=5 kit=6/6 gear_steps=0 topup=0 cast=n/a", again.VerdictFields());
        Assert.Empty(again.SentWields);
        Assert.Equal(sent, rig.Responder.ActionSends.Count);
    }

    [Fact]
    public void A_worn_piece_that_covers_no_kit_place_is_left_on()
    {
        const uint Cloak = 31_001;
        using var rig = new Rig(items: new Dictionary<uint, StandInItemKind> { [Cloak] = new("Test Cloak", null) });
        var made = rig.Session.Tools.SpawnItem(Cloak, null, ActionSource.Host);
        Pump(rig, 2);
        Assert.True(made.IsCompleted, made.Summary);
        var guid = made.Result!.Guid;
        Pump(rig, 1);
        var wear = rig.Session.Actions.Wield(guid, EquipMask.Cloak, ActionSource.Host);
        Pump(rig, 2);
        Assert.Equal(ActionOutcomeKind.Confirmed, wear.Outcome!.Value.Kind);

        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.DoesNotContain(scenario.SentWields, w => w.StartsWith("take off", StringComparison.Ordinal));
        Assert.True(WornWcid(rig, Cloak));
        Assert.Equal(5, WornPieces(rig));
    }

    [Fact]
    public void A_second_run_on_the_same_state_ends_wielded_and_sends_nothing()
    {
        using var rig = new Rig();
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(new GearScenario(SmallKit(), rig.Log, endSession: false)));
        var sends = rig.Responder.ActionSends.Count;

        var again = new GearScenario(SmallKit(), rig.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(again));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=0 topup=0 cast=n/a", again.VerdictFields());
        Assert.Empty(again.SentWields);
        Assert.Equal(sends, rig.Responder.ActionSends.Count); // no wield went out
        Assert.Equal(5, WornPieces(rig));
    }

    [Fact]
    public void The_wand_and_a_piece_already_worn_are_skipped_and_only_the_rest_is_worn()
    {
        using var rig = new Rig();
        var capOnly = GearKit.Of([Wand(), Armour(GearKit.Armour[0])]);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(new GearScenario(capOnly, rig.Log, endSession: false)));
        Assert.Equal(1, WornPieces(rig)); // the wand and the cap

        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=8 topup=1 cast=n/a", scenario.VerdictFields()); // 4 requests (the wand and the cap are held) and 4 wields, and the scarabs
        Assert.DoesNotContain(scenario.SentWields, w => w.Contains("Olthoi Helm", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_wield_ends_failed_naming_the_piece_and_the_server_error_and_counts_what_was_worn()
    {
        using var rig = new Rig();
        var gauntlets = GearKit.Armour.Single(a => a.Name == "Platemail Gauntlets");
        rig.World.RefusedWieldWcids.Add(gauntlets.Wcid);
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Contains("Platemail Gauntlets", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("ServerError", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("wielding", scenario.Stage);
        var verdict = scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith("gear=failed worn=3 ", verdict, StringComparison.Ordinal); // cap, coat and leggings were worn before the gauntlets
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("Platemail Gauntlets", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("verdict gear=failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_that_fails_in_the_world_logs_off_before_it_reports_the_failure_so_the_next_run_can_enter()
    {
        using var rig = new Rig();
        var gauntlets = GearKit.Armour.Single(a => a.Name == "Platemail Gauntlets");
        rig.World.RefusedWieldWcids.Add(gauntlets.Wcid);
        var scenario = new GearScenario(SmallKit(), rig.Log); // endSession: the headless host's

        Assert.Equal(SessionPhase.InWorld, rig.Session.State.Phase);
        var status = ScenarioStatus.Running;
        var sawFailureWhileInWorld = false;
        for (var t = 0.0; t < 240 && status == ScenarioStatus.Running; t += 0.1)
        {
            rig.Tick(0.1);
            status = scenario.Step(rig.Session, rig.Session.Clock.Now);
            sawFailureWhileInWorld |= status == ScenarioStatus.Failed && rig.Session.State.Phase == SessionPhase.InWorld;
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(sawFailureWhileInWorld, "the failure was reported while the character was still in the world");
        Assert.NotEqual(SessionPhase.InWorld, rig.Session.State.Phase);
        Assert.Contains("Platemail Gauntlets", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("logging off before the run ends as failed", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("the failed run logged off: Confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_grant_fails_the_spawn_with_the_reason_and_wields_nothing()
    {
        using var rig = new Rig();
        var missing = new GearItem(99_999_999, "Not A Weenie", GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, 5, 100, 4);
        var scenario = new GearScenario(GearKit.Of([Wand(), missing]), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.StartsWith("spawning: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("Not A Weenie: UnknownWcid", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(scenario.SentWields);
        Assert.StartsWith("gear=failed worn=0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the first cast (M3 1.4 s2)

    /// <summary>A mage whose Creature Enchantment skill is trained with <paramref name="ranks"/> ranks: with the stand-in's Focus 70 and Self 100 the skill is 43 + ranks.</summary>
    private static StandInPlayerStats Mage(int ranks, uint advancementClass = 2, ImmutableArray<EnchantmentDesc> enchantments = default) =>
        new()
        {
            Skills = [new SkillDesc(31, (ushort)ranks, 1, advancementClass, 0, 0, 0, 0.0)],
            Enchantments = enchantments.IsDefault ? [] : enchantments,
        };

    private static int Sends(Rig rig, uint type) => rig.Responder.ActionSends.Count(s => s.Type == type);

    [Fact]
    public void After_the_gear_the_scenario_enters_the_magic_mode_casts_the_highest_safe_creature_buff_and_returns_to_peace()
    {
        using var rig = new Rig(Mage(300)); // skill 343: level V (power 200, chance 0.99995) passes 0.999, level VI (power 250, chance 0.9985) does not
        const uint strengthV = 1331;
        rig.Learn(strengthV);
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=ok", scenario.VerdictFields()); // the cast is not a gear step
        Assert.Equal(strengthV, scenario.CastSpellId);
        Assert.Contains("Strength Self V", scenario.CastSpell, StringComparison.Ordinal);
        Assert.Contains(rig.Session.State.Self.Qualities.Enchantments, e => e.SpellId == strengthV);
        Assert.Equal(CombatModes.NonCombat, rig.Session.State.Self.Qualities.CombatMode); // back in peace
        Assert.Equal(2, Sends(rig, GameActionTypes.ChangeCombatMode)); // magic, then non-combat
        Assert.Equal(1, Sends(rig, GameActionTypes.CastUntargetedSpell));
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("verdict gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=ok", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fizzle_is_reported_as_cast_fizzled_and_does_not_fail_the_gear()
    {
        using var rig = new Rig(Mage(477)); // skill 520: level VIII (power 400, chance 0.99977) is chosen, and the stand-in's placeholder skill (300) fizzles above power 350
        rig.Learn(4325);
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=fizzled", scenario.VerdictFields());
        Assert.Equal(4325u, scenario.CastSpellId);
        Assert.DoesNotContain(rig.Session.State.Self.Qualities.Enchantments, e => e.SpellId == 4325);
        Assert.Equal(CombatModes.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
        Assert.Equal(1, rig.World.Fizzles);
    }

    [Fact]
    public void A_cast_the_server_refuses_is_reported_as_cast_failed_and_the_mage_still_returns_to_peace()
    {
        using var rig = new Rig(Mage(300)); // the spell is not in the spellbook: ACE's UseDone 0x03FC
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=failed", scenario.VerdictFields());
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("cast Strength Self V", StringComparison.Ordinal) && e.Message.Contains(": failed (ServerError code 0x03FC", StringComparison.Ordinal) && e.Message.Contains("during the cast", StringComparison.Ordinal));
        Assert.Equal(CombatModes.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void Without_a_usable_skill_or_with_the_cast_off_the_field_is_n_a_and_nothing_goes_out()
    {
        using var untrained = new Rig(Mage(300, advancementClass: 1));
        var a = new GearScenario(SmallKit(), untrained.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, untrained.Run(a));
        Assert.EndsWith(" cast=n/a", a.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, Sends(untrained, GameActionTypes.ChangeCombatMode));
        Assert.Contains(untrained.Log.Entries, e => e.Message.Contains("cast skipped (cast=n/a): skill 31 is not trained", StringComparison.Ordinal));

        using var off = new Rig(Mage(300));
        off.Learn(1331);
        var b = new GearScenario(SmallKit(), off.Log, endSession: false, cast: false);
        Assert.Equal(ScenarioStatus.Succeeded, off.Run(b));
        Assert.EndsWith(" cast=n/a", b.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, Sends(off, GameActionTypes.CastUntargetedSpell));
        Assert.Equal(0, Sends(off, GameActionTypes.ChangeCombatMode));
    }

    [Fact]
    public void The_chance_is_ACEs_pre_check_and_the_current_skill_reads_the_buffs_the_read_model_holds()
    {
        // SkillCheck.GetMagicSkillChance (SkillCheck.cs 7-22): the spec's own numbers (about 3%, 33% and 90% against a power of 400), the certain failure 50 under the power, and 0 at skill 0.
        Assert.Equal(0.5, GearCasts.Chance(400, 400), 6);
        Assert.InRange(GearCasts.Chance(350, 400), 0.028, 0.031);
        Assert.InRange(GearCasts.Chance(390, 400), 0.32, 0.34);
        Assert.InRange(GearCasts.Chance(433, 400), 0.89, 0.91);
        Assert.Equal(0.0, GearCasts.Chance(349, 400));
        Assert.Equal(0.0, GearCasts.Chance(0, 0));
        Assert.True(GearCasts.Chance(350, 400) > 0.0); // exactly 50 under is still rolled (Player_Magic.cs 534: skill >= power - 50)

        // Focus 70 and Self 100: round(170 / 4) = 43 (half away from zero), plus the start level 10 and the ranks.
        using var plain = new Rig(new StandInPlayerStats { Skills = [new SkillDesc(31, 100, 1, 2, 0, 10, 0, 0.0)] });
        Assert.True(GearCasts.TryCurrentSkill(plain.Session.State.Self.Qualities, 31, out var baseSkill, out _));
        Assert.Equal(153u, baseSkill);

        // A Focus enchantment (+20: (90 + 100) / 4 = 47.5 -> 48) and a skill enchantment (+25) are both read; so is a worse one of the same category that the strongest one beats.
        const uint beneficialAdditive = EnchantmentRules.BeneficialKind | EnchantmentRules.Additive;
        var focus = new EnchantmentDesc(1421, 1, 40, 0, 200, 0.0, 1800.0, 1, 0f, 0f, 0.0, beneficialAdditive | 0x1000 | EnchantmentRules.AttributeKind, 5, 20f, 0, EnchantmentBucket.Additive);
        var skillPlus = new EnchantmentDesc(557, 1, 50, 0, 200, 0.0, 1800.0, 1, 0f, 0f, 0.0, beneficialAdditive | 0x1000 | 0x10, 31, 25f, 0, EnchantmentBucket.Additive);
        var weaker = skillPlus with { SpellId = 556, Layer = 2, PowerLevel = 100, StatModValue = 10f };
        using var buffed = new Rig(new StandInPlayerStats { Skills = [new SkillDesc(31, 100, 1, 2, 0, 10, 0, 0.0)], Enchantments = [focus, skillPlus, weaker] });
        Assert.True(GearCasts.TryCurrentSkill(buffed.Session.State.Self.Qualities, 31, out var buffedSkill, out _));
        Assert.Equal(48u + 10u + 100u + 25u, buffedSkill);

        Assert.False(GearCasts.TryCurrentSkill(plain.Session.State.Self.Qualities, 33, out _, out var why)); // Life Magic is not in this description
        Assert.Contains("no skill 33", why, StringComparison.Ordinal);
        Assert.False(GearCasts.TryCurrentSkill(plain.Session.State.Self.Qualities, 24, out _, out why)); // Run is not a magic skill
        Assert.Contains("not one of the four magic skills", why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pick_is_the_highest_level_whose_chance_reaches_the_threshold_and_names_why_when_none_does()
    {
        using var rig = new Rig(Mage(300));
        var pick = GearCasts.Pick(rig.Session.State.Self.Qualities, ProfileRole.CreatureBuff, out _);
        Assert.NotNull(pick);
        Assert.Equal((343u, 1331u, 200u), (pick.Skill, pick.Spell.Id, pick.Spell.Power));
        Assert.True(pick.Chance >= GearCasts.MinChance);
        Assert.True(GearCasts.Chance(pick.Skill, 250) < GearCasts.MinChance); // the next level is not safe

        using var tiny = new Rig(Mage(0)); // skill 43: even a power-1 level has a chance of only 0.95
        Assert.Null(GearCasts.Pick(tiny.Session.State.Self.Qualities, ProfileRole.CreatureBuff, out var tooLow));
        Assert.Contains("reaches a chance of", tooLow, StringComparison.Ordinal);
        Assert.Null(GearCasts.Pick(tiny.Session.State.Self.Qualities, ProfileRole.LifeBuff, out var why)); // no Life Magic in this description
        Assert.Contains("no skill 33", why, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ M3 1.4 fix B

    [Fact]
    public void The_cast_on_a_second_run_changes_no_gear_step_and_a_bigger_stack_is_counted_apart_as_topup()
    {
        // Review 1.4-R1 and fix B item 8: run twice with the cast on. The cast does not make a gear step; the offline stand-in burns nothing, so the second run tops up
        // nothing (gear_steps=0 topup=0). A kit that asks for more of a component than the pack holds is a top-up: it is counted in topup=, never in gear_steps=.
        using var rig = new Rig(Mage(300));
        rig.Learn(1331);
        var first = new GearScenario(SmallKit(), rig.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(first));
        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=ok", first.VerdictFields());

        var second = new GearScenario(SmallKit(), rig.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(second));
        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=0 topup=0 cast=ok", second.VerdictFields());
        Assert.Equal(2, Sends(rig, GameActionTypes.CastUntargetedSpell)); // a cast per run: the gear steps stay 0 either way

        var burned = new GearScenario(GearKit.Of([Wand(), .. GearKit.Armour.Select(Armour), Scarab(30)]), rig.Log, endSession: false, cast: false); // ten scarabs short, as after a burn
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(burned));
        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=0 topup=1 cast=n/a", burned.VerdictFields());
        Assert.Empty(burned.SentWields);
    }

    [Fact]
    public void The_gear_help_names_the_verdict_fields_topup_and_cast_and_no_longer_says_a_ready_mage_sends_nothing()
    {
        var help = OptionCatalog.Headless().Options.Single(o => o.Name == "--scenario").Help;
        Assert.Contains("topup=<n>", help, StringComparison.Ordinal);
        Assert.Contains("cast=<ok|fizzled|failed|n/a>", help, StringComparison.Ordinal);
        Assert.Contains("sends no gear step", help, StringComparison.Ordinal);
        Assert.DoesNotContain("a ready mage sends nothing", help, StringComparison.Ordinal);
    }

    private static SpellInfo Costing(Rig rig, uint id, uint baseMana)
    {
        var info = rig.Table[id] with { BaseMana = baseMana };
        rig.Table[id] = info;
        return info;
    }

    [Fact]
    public void The_cast_waits_for_the_mana_it_needs_logs_the_numbers_and_is_sent_once_the_mana_has_regenerated()
    {
        // Strength Self V at up to 90 mana against a mana of 75 (the stand-in's); the server's vital update brings it to 95 twelve seconds after the unhide.
        using var rig = new Rig(Mage(300), [new StandInVitalUpdate(12.0, 6, 95)]);
        rig.Learn(1331);
        Costing(rig, 1331, 90);
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false, spells: rig.Table);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("gear=wielded worn=5 kit=7/7 gear_steps=12 topup=1 cast=ok", scenario.VerdictFields());
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("cast: waiting for mana to regenerate: 75 of ", StringComparison.Ordinal) && e.Message.Contains("up to 90 needed", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.StartsWith("cast: mana 95 reached the 90 needed after ", StringComparison.Ordinal));
        Assert.Equal(1, Sends(rig, GameActionTypes.CastUntargetedSpell));
    }

    [Fact]
    public void A_cast_whose_mana_never_arrives_sends_nothing_and_says_so_with_the_numbers_and_a_cost_above_the_maximum_is_named_at_once()
    {
        using var never = new Rig(Mage(300));
        never.Learn(1331);
        Costing(never, 1331, 90);
        var a = new GearScenario(SmallKit(), never.Log, endSession: false, spells: never.Table);
        Assert.Equal(ScenarioStatus.Succeeded, never.Run(a, limit: 400));
        Assert.StartsWith("gear=wielded worn=5 ", a.VerdictFields(), StringComparison.Ordinal); // report-only: the gear stays judged
        Assert.EndsWith(" cast=failed", a.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, Sends(never, GameActionTypes.CastUntargetedSpell));
        Assert.Equal(0, Sends(never, GameActionTypes.ChangeCombatMode));
        Assert.Contains(never.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.StartsWith("cast not sent (cast=failed): the mana did not reach 90 within 120 s: 75 of ", StringComparison.Ordinal));

        using var huge = new Rig(Mage(300));
        huge.Learn(1331);
        Costing(huge, 1331, 5000);
        var b = new GearScenario(SmallKit(), huge.Log, endSession: false, spells: huge.Table);
        Assert.Equal(ScenarioStatus.Succeeded, huge.Run(b));
        Assert.EndsWith(" cast=failed", b.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(huge.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("costs up to 5000 mana and the maximum is ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_spawn_wait_allocates_nothing_between_its_asks_while_the_inventory_cannot_be_counted()
    {
        // Review 1.4-R5. A tools-lane item whose 0022 has come but not its CreateObject leaves the pack uncountable (the stand-in's CreateObject lags 0.05 s); the session is not ticked, so
        // the gap stays open and the scenario's steps run with the clock frozen: the first ask recounts, the others (within SpawnAskSeconds) must neither recount nor build a string.
        using var rig = new Rig(Mage(300));
        _ = rig.Session.Tools.SpawnItem(774, 3, ActionSource.Host);
        var gap = false;
        for (var i = 0; i < 400 && !gap; i++)
        {
            rig.Tick(0.01);
            gap = rig.Inventory.Pack.Count == 1 && !rig.Session.State.Objects.Contains(rig.Inventory.Pack[0].Guid);
        }

        Assert.True(gap);
        var scenario = new GearScenario(SmallKit(), rig.Log, endSession: false, cast: false);
        for (var i = 0; i < 3 && scenario.Stage != "spawning"; i++)
        {
            Assert.Equal(ScenarioStatus.Running, scenario.Step(rig.Session, rig.Session.Clock.Now));
        }

        Assert.Equal("spawning", scenario.Stage);
        Assert.Equal(ScenarioStatus.Running, scenario.Step(rig.Session, rig.Session.Clock.Now)); // the first ask: it counts, finds the gap and allocates
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            scenario.Step(rig.Session, rig.Session.Clock.Now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal("spawning", scenario.Stage);
        Assert.Equal(0, scenario.GearSteps + scenario.TopUp);
    }

    [Fact]
    public void The_form_of_a_cast_follows_the_spells_target_type_and_the_mana_ceiling_follows_ACEs_cost_rule()
    {
        var self = WorldActionRig.Spell(1, DatReaderWriter.Enums.SpellType.Enchantment, SpellFlags.SelfTargeted) with { School = DatReaderWriter.Enums.MagicSchool.ItemEnchantment, NonComponentTargetType = 0x8000 };
        Assert.Equal(CastForm.Self, GearCasts.FormOf(self)); // an Aura: SelfTargeted wins over a Caster target type (ACE refuses it at any other target)
        var plain = WorldActionRig.Spell(2, DatReaderWriter.Enums.SpellType.Enchantment);
        Assert.Equal(CastForm.Self, GearCasts.FormOf(plain)); // NonComponentTargetType None: untargeted
        Assert.Equal(CastForm.Self, GearCasts.FormOf(plain with { NonComponentTargetType = 0x10 })); // Creature
        foreach (var itemTarget in new uint[] { 0x6, 0x101, 0x8000, 0x8101, 0x1 | 0x2 | 0x4 | 0x8 | 0x20 | 0x40 | 0x80 | 0x100 | 0x200 | 0x800 | 0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x10000 | 0x40000 | 0x80000 | 0x200000 })
        {
            Assert.Equal(CastForm.AtItem, GearCasts.FormOf(plain with { NonComponentTargetType = itemTarget })); // Vestements, Weapon, Caster, WeaponOrCaster, Item
        }

        var cost = WorldActionRig.Spell(3, DatReaderWriter.Enums.SpellType.Enchantment) with { BaseMana = 40, ManaMod = 5, School = DatReaderWriter.Enums.MagicSchool.CreatureEnchantment };
        Assert.Equal(40u, GearCasts.ManaCeiling(cost, 5));
        var armour = cost with { School = DatReaderWriter.Enums.MagicSchool.ItemEnchantment, Category = 160 };
        Assert.Equal(40u + (5u * 5u), GearCasts.ManaCeiling(armour, 5)); // ACE: baseCost += ManaMod x worn clothing, for the armour-value to acid-resistance categories
        Assert.Equal(40u, GearCasts.ManaCeiling(armour with { Category = 190 }, 5));
        Assert.Equal(1u, GearCasts.ManaCeiling(cost with { BaseMana = 0 }, 0));
    }

    [Fact]
    public void The_chance_that_a_cast_burns_nothing_is_the_product_over_the_formula_slots_of_one_minus_loss_times_cdm_times_the_skill_factor()
    {
        // Lead Scarab (component 1, CDM 0.1) and Hyssop (component 7, CDM 1.0): ACE Spell.TryBurnComponents, Entity/Spell.cs 157-189.
        var spell = WorldActionRig.Spell(1, DatReaderWriter.Enums.SpellType.Enchantment) with { ComponentLoss = 0.2f, Power = 200, FormulaVersion = 0, Formula = [1, 7, 0, 0, 0, 0, 0, 0] };
        Assert.Equal((1.0 - (0.2 * 0.1)) * (1.0 - (0.2 * 1.0)), GearCasts.NoBurnProbability(spell, "any-account", 100), 6); // skill below the power: factor 1
        Assert.Equal((1.0 - (0.2 * 0.1 * 0.5)) * (1.0 - (0.2 * 1.0 * 0.5)), GearCasts.NoBurnProbability(spell, "any-account", 400), 6); // skill twice the power: factor 0.5
        Assert.Equal(1.0, GearCasts.NoBurnProbability(spell with { ComponentLoss = 0f }, "any-account", 100), 6);
        Assert.Equal(0.0, GearCasts.NoBurnProbability(spell with { ComponentLoss = 20f }, "any-account", 100), 6); // a rate above 1 is a certain burn
    }

    [Fact]
    public void An_additive_skill_enchantment_is_truncated_to_an_int_before_it_is_summed_as_ACE_does()
    {
        // EnchantmentManager.GetSkillMod_Additives (EnchantmentManager.cs 759-765): skillMod += (int)enchantment.StatModValue.
        const uint beneficialAdditive = EnchantmentRules.BeneficialKind | EnchantmentRules.Additive;
        var a = new EnchantmentDesc(557, 1, 50, 0, 200, 0.0, 1800.0, 1, 0f, 0f, 0.0, beneficialAdditive | 0x1000 | 0x10, 31, 25.9f, 0, EnchantmentBucket.Additive);
        var b = a with { SpellId = 558, SpellCategory = 51, StatModValue = 10.9f };
        using var rig = new Rig(new StandInPlayerStats { Skills = [new SkillDesc(31, 100, 1, 2, 0, 10, 0, 0.0)], Enchantments = [a, b] });
        Assert.True(GearCasts.TryCurrentSkill(rig.Session.State.Self.Qualities, 31, out var skill, out _));
        Assert.Equal(43u + 10u + 100u + 25u + 10u, skill); // 25 + 10, not round(36.8) = 37
    }

    [Fact]
    public void The_fizzle_chooser_takes_the_cheapest_known_spell_above_the_margin_and_skips_a_spell_the_spellbook_does_not_hold()
    {
        using var rig = new Rig(Mage(300)); // Creature Enchantment skill 343: a power above 393 fizzles by margin; the only such creature level is Incantation of Strength Self (400)
        var q = rig.Session.State.Self.Qualities;
        var book = rig.Session.State.Self.Spellbook;
        var none = GearCasts.PickFizzle(q, book, out var report);
        Assert.Null(none);
        Assert.Contains("known", report, StringComparison.Ordinal);

        Assert.True(book.Learn(4325)); // the stand-in answers the learn verb without a GameEvent 0x02C1, so the read model's book is told directly
        var fizzler = GearCasts.PickFizzle(q, book, out _);
        Assert.NotNull(fizzler);
        Assert.Equal(4325u, fizzler.Spell.Id);
        Assert.Equal(DatReaderWriter.Enums.MagicSchool.CreatureEnchantment, fizzler.Line.School);
        Assert.True(fizzler.Spell.Power - (long)fizzler.Skill > GearCasts.FizzleMargin);
    }

    [Fact]
    public void A_failed_cast_is_explained_with_its_code_named_and_the_chat_lines_that_arrived_during_it()
    {
        using var rig = new Rig(Mage(300));
        var state = rig.Session.State;
        var mark = state.Chat.Total;
        var refused = new ActionOutcome(ActionOutcomeKind.ServerError, 0x0401, "not enough mana (WeenieError 0x0401)");
        Assert.Contains("no chat line arrived during the cast", GearCasts.Explain(refused, state, mark, 90), StringComparison.Ordinal);
        Assert.Contains("code 0x0401 (NOT ENOUGH MANA: ", GearCasts.Explain(refused, state, mark, 90), StringComparison.Ordinal);
        Assert.Contains("up to 90 needed)", GearCasts.Explain(refused, state, mark, 90), StringComparison.Ordinal);
        Assert.Contains("code 0x0400 (UseDone: the player lacks a component)", GearCasts.Explain(new ActionOutcome(ActionOutcomeKind.ServerError, 0x0400, null), state, mark, 0), StringComparison.Ordinal);
        Assert.Contains("code 0x0402 (GameEvent 0x028A: the spell fizzled)", GearCasts.Explain(new ActionOutcome(ActionOutcomeKind.ServerError, 0x0402, null), state, mark, 0), StringComparison.Ordinal);
        Assert.Contains("; no chat line arrived", GearCasts.Explain(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server ended the cast without the enchantment"), state, mark, 0), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void Every_wand_line_spell_of_the_real_SpellTable_is_a_self_cast_because_the_auras_target_the_caster()
    {
        // The lead's first live run (2026-10-16): Aura of Hermetic Link Self V (1479) cast at the wielded wand was ended by the server without the enchantment; the end-of-retail wand-line spells are
        // self-targeted Auras that land as the player's own enchantment. Pinned against the dat (the form the CastLiveTests' item school uses), with the data the choice read printed on failure.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in ArenaProfile.Lines.Where(l => l.Role is ProfileRole.ItemBuffWand or ProfileRole.CreatureBuff or ProfileRole.LifeBuff))
        {
            foreach (var spell in line.Spells)
            {
                var info = spells[spell.Id];
                Assert.True(
                    GearCasts.FormOf(info) == CastForm.Self,
                    $"{spell.Name} ({spell.Id}) of {line.Name}: flags {info.Flags}, NonComponentTargetType 0x{info.NonComponentTargetType:X}, meta {info.MetaSpellType}");
            }
        }

        var aura = spells[1479];
        Assert.Contains("Hermetic Link", aura.Name, StringComparison.Ordinal);
        Assert.NotEqual(0u, GearCasts.ManaCeiling(aura, 5));
    }

    [RequiresDatsFact]
    public void The_whole_real_kit_is_spawned_and_worn_within_the_scenarios_limits_and_a_rerun_sends_nothing()
    {
        using var rig = new Rig();
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var first = new GearScenario(spells, "gear-test-account", rig.Log, endSession: false);
        var start = rig.Session.Clock.Now;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(first));

        var seconds = rig.Session.Clock.Now - start;
        Assert.True(seconds < first.SpawnLimit, $"{seconds:0.#} s against a spawn limit of {first.SpawnLimit:0.#} s");
        Assert.Equal("gear=wielded worn=5 kit=59/59 gear_steps=12 topup=53 cast=n/a", first.VerdictFields()); // 6 requests and 6 wields, and 53 component lines
        var second = new GearScenario(spells, "gear-test-account", rig.Log, endSession: false);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(second));
        Assert.Equal("gear=wielded worn=5 kit=59/59 gear_steps=0 topup=0 cast=n/a", second.VerdictFields());
    }
}
