using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 16, slice E5e4: the loot step's re-wield of a worn piece, on the scripted ACE of the other loot tests with a wield and a take-off answered or refused as ACE answers them. The loot step is put into its
/// Wearing stage by reflection (as <see cref="A_hit_while_a_wield_is_in_flight_in_the_wearing_stage_waits_for_its_outcome_logs_it_and_then_the_fight_goes_first"/> reaches it); everything after is the step's own:
/// the check before (<c>RelightCandidate</c>), the swap in hand, the check after, the recovery. What these prove: a piece whose spells are only surpassed is left on, said once, with nothing sent; a piece put on
/// before her lore rose comes off, goes on and reads lit; and a wield ACE refuses can never leave her undressed quietly: the recovery's wield, an error line that names the piece when that fails too, and a
/// put-back at every pass after until the piece is read worn.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint RelightArmorSelfV = 1311, RelightArmorSelfVI = 1312, RelightMinorFocus = 2581;
    private const uint RelightChest = 0x200, RelightChestCoverage = 0x400;

    /// <summary>ACE's side of a wield and a take-off, for the pieces the fight rig's own script does not answer.</summary>
    private sealed class RelightCloset(Rig r)
    {
        private readonly VendorRig _items = new(r.R, 1000);
        private int _seen = r.R.H.Net.Sends.Count;
        private byte _sequence = 150;
        private uint _event = 0x70000;

        /// <summary>The pieces whose wield ACE refuses.</summary>
        public HashSet<uint> RefuseWield { get; } = [];

        /// <summary>What ACE casts on her when a piece is wielded (its activation met): the entries of her registry with the piece as caster.</summary>
        public Dictionary<uint, AceEnchantment[]> Lights { get; } = [];

        public bool Worn(uint guid) => r.R.H.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;

        public int Wields(uint guid) => r.R.H.Net.Of(FakeSendKind.GetAndWieldItem).Count(s => s.Guid == guid);

        public int TakeOffs(uint guid) => r.R.H.Net.Of(FakeSendKind.PutItemInContainer).Count(s => s.Guid == guid);

        /// <summary>A worn piece of armour with mana, an ItemDifficulty and its spells, appraised (illustrative, not world data).</summary>
        public uint WornPiece(string name, uint guid, uint location, uint coverage, int difficulty, params uint[] spells)
        {
            _items.Arrive(guid, name, 2472, 0x2, 200, validLocations: location, priority: coverage);
            _items.Put(guid);
            r.R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)location));
            r.R.Tick();
            r.R.H.R.Reducer.OnIdentifyResponse(r.R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, 0x00C9), new IdentifyResponseMsg(
                guid,
                IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook,
                true,
                new Dictionary<uint, int> { [5] = 300, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = difficulty }.ToImmutableDictionary(),
                ImmutableDictionary<uint, long>.Empty,
                ImmutableDictionary<uint, bool>.Empty,
                ImmutableDictionary<uint, double>.Empty,
                ImmutableDictionary<uint, string>.Empty,
                ImmutableDictionary<uint, uint>.Empty,
                [.. spells],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
            Assert.True(Worn(guid));
            return guid;
        }

        /// <summary>An enchantment of her registry: the spell on a layer with the table's category and power, cast by <paramref name="caster"/>.</summary>
        public AceEnchantment Entry(uint spell, ushort layer, uint caster) =>
            new((ushort)spell, layer, (ushort)r.Table[spell].Category, 0, r.Table[spell].Power, 0, caster == WorldActionRig.Player ? 3600 : -1, caster, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0);

        public void Cast(AceEnchantment entry) => r.R.Server((p, e) => AceM3Writers.MagicUpdateEnchantment(p, e, entry));

        /// <summary>Her Arcane Lore trained with <paramref name="ranks"/> ranks (the fight rig's mage holds none).</summary>
        public void Lore(ushort ranks) => r.R.Raw(AceWireWriters.PrivateUpdateSkill(++_sequence, new AceSkill(MageTemplate.ArcaneLore, ranks, 2, 0, 0)));

        /// <summary>Answers the wields and take-offs sent since the last call, as ACE does, and lets the Action API read the answers.</summary>
        public void Pump()
        {
            var sends = r.R.H.Net.Sends;
            for (; _seen < sends.Count; _seen++)
            {
                var s = sends[_seen];
                switch (s.Kind)
                {
                    case FakeSendKind.GetAndWieldItem when RefuseWield.Contains(s.Guid):
                        _items.ItemRefusal(s.Guid);
                        break;
                    case FakeSendKind.GetAndWieldItem:
                        r.R.Raw(AceM3Writers.PublicUpdateInstanceId(++_sequence, s.Guid, 3, 0));
                        r.R.Raw(AceM3Writers.PublicUpdatePropertyInt(_sequence, s.Guid, 10, 0));
                        r.R.Server((p, q) => AceM3Writers.WieldObject(p, q, s.Guid, (int)s.Amount));
                        foreach (var entry in Lights.GetValueOrDefault(s.Guid) ?? [])
                        {
                            Cast(entry);
                        }

                        break;
                    case FakeSendKind.PutItemInContainer:
                        if (Worn(s.Guid))
                        {
                            r.R.Raw(AceM3Writers.PublicUpdateInstanceId(++_sequence, s.Guid, 3, 0));
                            r.R.Raw(AceM3Writers.PublicUpdatePropertyInt(_sequence, s.Guid, 10, 0));
                        }

                        _items.Put(s.Guid, uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                }
            }

            r.R.Tick();
        }
    }

    private static SpellInfo RelightRow(uint id, string name, MagicSchool school, uint category, uint power) =>
        WorldActionRig.Spell(id, SpellType.Enchantment) with { Name = name, School = school, Category = category, Power = power };

    /// <summary>The rig of the loot tests with the three spells the pieces cast in its table (illustrative rows: the schools are the spells', the categories and powers stand-ins that keep the order).</summary>
    private static Rig RelightRig()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 20)) { Corpses = true };
        r.Table[RelightArmorSelfV] = RelightRow(RelightArmorSelfV, "Armor Self V", MagicSchool.LifeMagic, 901, 250);
        r.Table[RelightArmorSelfVI] = RelightRow(RelightArmorSelfVI, "Armor Self VI", MagicSchool.LifeMagic, 901, 300);
        r.Table[RelightMinorFocus] = RelightRow(RelightMinorFocus, "Minor Focus", MagicSchool.CreatureEnchantment, 902, 1);
        StockCorpse(r);
        return r;
    }

    /// <summary>Runs the fight to the first corpse's loot and returns the loot step.</summary>
    private static object ToTheLoot(Rig r, FightScenario scenario, RelightCloset closet)
    {
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        var loot = LootOf(scenario);
        for (var i = 0; i < 400; i++)
        {
            FrameWith(r, scenario, closet.Pump);
            if (a.Dead && (bool)loot.GetType().GetProperty("Active")!.GetValue(loot)!)
            {
                return loot;
            }
        }

        throw new InvalidOperationException("the loot step never ran");
    }

    /// <summary>One pass of the loot step's Wearing stage with no find to judge (a corpse's end): the stage is set, then the frames run with ACE answering.</summary>
    private static void WearingPass(Rig r, FightScenario scenario, object loot, RelightCloset closet, int frames = 60)
    {
        var stage = loot.GetType().GetNestedType("Stage", BindingFlags.NonPublic)!;
        LootField(loot, "_stage").SetValue(loot, Enum.Parse(stage, "Wearing"));
        LootField(loot, "_pending").SetValue(loot, null);
        LootField(loot, "_corpse").SetValue(loot, CorpseA);
        LootField(loot, "_stageStart").SetValue(loot, r.Now);
        LootField(loot, "_corpseStart").SetValue(loot, r.Now);
        for (var i = 0; i < frames; i++)
        {
            FrameWith(r, scenario, closet.Pump);
        }
    }

    private static List<string> GearLines(Rig r) => [.. r.Log.Lines.Where(l => l.Contains("loot: gear:", StringComparison.Ordinal))];

    [Fact]
    public void M6_16_E5e4_a_worn_piece_whose_spells_are_only_surpassed_by_her_own_is_left_on_said_once_and_nothing_is_sent()
    {
        const uint Breastplate = 0x80007B01;
        var r = RelightRig();
        var closet = new RelightCloset(r);
        var scenario = r.Scenario(new FakeWalk());
        var loot = ToTheLoot(r, scenario, closet);
        closet.Lore(200);
        closet.WornPiece("Breastplate", Breastplate, RelightChest, RelightChestCoverage, 100, RelightArmorSelfV);
        closet.Cast(closet.Entry(RelightArmorSelfVI, 1, WorldActionRig.Player)); // her own Armor Self VI stands; the breastplate was never lit (no entry cast by it)

        WearingPass(r, scenario, loot, closet);
        WearingPass(r, scenario, loot, closet);

        Assert.True(LinesWith(r, "loot: gear: re-wield: 'Breastplate' 0x80007B01 left on: its spells are surpassed by her own Armor Self VI over its Armor Self V") == 1, string.Join(" | ", GearLines(r)));
        Assert.Equal(0, closet.TakeOffs(Breastplate));
        Assert.Equal(0, closet.Wields(Breastplate));
        Assert.True(closet.Worn(Breastplate));
        Assert.Equal(0, LinesWith(r, "gear-spells-not-lit"));
    }

    [Fact]
    public void M6_16_E5e4_a_worn_piece_put_on_before_her_lore_rose_goes_off_and_on_once_and_reads_lit()
    {
        const uint Bracers = 0x80007B02;
        var r = RelightRig();
        var closet = new RelightCloset(r);
        var scenario = r.Scenario(new FakeWalk());
        var loot = ToTheLoot(r, scenario, closet);
        closet.WornPiece("Bracers", Bracers, RelightChest, RelightChestCoverage, 150, RelightArmorSelfV, RelightMinorFocus);
        closet.Cast(closet.Entry(RelightArmorSelfVI, 1, WorldActionRig.Player));
        closet.Lights[Bracers] = [closet.Entry(RelightArmorSelfV, 2, Bracers), closet.Entry(RelightMinorFocus, 1, Bracers)];

        // Before her lore rose (the rig's mage has no rank of Arcane Lore): ACE would refuse the activation again, so nothing comes off, and the spend is asked for the lore that lights it, as before this slice.
        var asked = new List<string>();
        typeof(FightScenario).GetField("<LoreWanted>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scenario, (Action<string>)asked.Add);
        WearingPass(r, scenario, loot, closet);
        Assert.Equal(0, closet.TakeOffs(Bracers));
        Assert.True(asked.Count == 1 && asked[0].StartsWith("the worn 'Bracers' 0x80007B02 has its spells off (activation: Arcane Lore ", StringComparison.Ordinal) && asked[0].EndsWith(" point(s) of Arcane Lore light it", StringComparison.Ordinal), string.Join(" | ", asked));
        Assert.Empty(GearLines(r));

        // Her lore meets it now: the Minor Focus would show (her Armor Self VI stays over its Armor Self V), so it comes off, goes on, and ACE casts its spells.
        closet.Lore(200);
        WearingPass(r, scenario, loot, closet);
        WearingPass(r, scenario, loot, closet);

        var lines = string.Join(" | ", GearLines(r));
        Assert.True(LinesWith(r, "loot: gear: re-wield: 'Bracers' 0x80007B02 is not lit on her (no enchantment in her registry is cast by it): ACE would activate it now and Minor Focus would show on her (Armor Self VI over its Armor Self V)") == 1, lines);
        Assert.True(LinesWith(r, "loot: gear: re-wield: 'Bracers' 0x80007B02 off to 0x") == 1 && LinesWith(r, "check: off, in her pack") == 1, lines);
        Assert.True(LinesWith(r, "loot: gear: re-wield: 'Bracers' 0x80007B02 on at 0x200: Confirmed") == 1 && LinesWith(r, "check: worn") == 1, lines);
        Assert.True(LinesWith(r, "loot: gear: re-wield: check after: 'Bracers' 0x80007B02 worn, its spells active on her (surpassed by her own Armor Self VI over its Armor Self V); none taken off for good") == 1, lines);
        Assert.Equal((1, 1), (closet.TakeOffs(Bracers), closet.Wields(Bracers))); // once: the second pass reads it lit and sends nothing
        Assert.True(closet.Worn(Bracers));
        Assert.True(WearScore.LitOn(r.R.H.State.Self.Qualities, Bracers));
        Assert.DoesNotContain(r.Log.Entries, e => e.Severity >= LogSeverity.Warning && e.Line.Contains("loot: gear:", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_16_E5e4_a_re_wield_whose_wield_ACE_refuses_is_recovered_named_in_an_error_when_that_fails_too_and_put_back_at_every_pass_until_worn()
    {
        const uint Greaves = 0x80007B03;
        var r = RelightRig();
        var closet = new RelightCloset(r);
        var scenario = r.Scenario(new FakeWalk());
        var loot = ToTheLoot(r, scenario, closet);
        closet.Lore(200);
        closet.WornPiece("Greaves", Greaves, RelightChest, RelightChestCoverage, 100, RelightMinorFocus);
        closet.Lights[Greaves] = [closet.Entry(RelightMinorFocus, 1, Greaves)];
        closet.RefuseWield.Add(Greaves);
        int Errors() => r.Log.Entries.Count(e => e.Severity == LogSeverity.Error && e.Line.Contains("'Greaves' 0x80007B03 is NOT worn (named: gear-left-off)", StringComparison.Ordinal));

        // The re-wield: off, the wield refused, the recovery's wield refused too.
        WearingPass(r, scenario, loot, closet);
        var lines = string.Join(" | ", GearLines(r));
        Assert.True(LinesWith(r, "loot: gear: re-wield: 'Greaves' 0x80007B03 on at 0x200: ") == 1 && LinesWith(r, "check: not worn (refused)") == 1, lines);
        Assert.True(LinesWith(r, "loot: gear: re-wield: recovery: 'Greaves' 0x80007B03 back on at 0x200: ") == 1 && LinesWith(r, "check: NOT worn: the parts it covered are bare") == 1, lines);
        Assert.True(Errors() == 1, lines);
        Assert.Contains(r.Log.Lines, l => l.Contains("the recovery's wield did not land", StringComparison.Ordinal) && l.Contains("what it covered at 0x200 is bare; recovery: it is put on again at every corpse until it is read worn", StringComparison.Ordinal));
        Assert.True(LinesWith(r, "loot: gear: re-wield with 'Greaves' 0x80007B03 did not land: 0 piece(s) put back, 1 NOT worn (named: gear-left-off)") == 1, lines);
        Assert.False(closet.Worn(Greaves));
        Assert.Equal((1, 2), (closet.TakeOffs(Greaves), closet.Wields(Greaves)));

        // The next pass: the put-back, refused again: said again, as loudly.
        WearingPass(r, scenario, loot, closet);
        lines = string.Join(" | ", GearLines(r));
        Assert.True(LinesWith(r, "loot: gear: put back: check before: 'Greaves' 0x80007B03 is in her packs and not worn (an earlier swap left it off); on again at 0x200") == 1, lines);
        Assert.True(Errors() == 2, lines);
        Assert.Contains(r.Log.Lines, l => l.Contains("loot: gear: put back: 'Greaves' 0x80007B03 is NOT worn (named: gear-left-off): the wield did not land again", StringComparison.Ordinal));
        Assert.Equal((1, 3), (closet.TakeOffs(Greaves), closet.Wields(Greaves))); // nothing comes off again: one more wield

        // ACE takes the wield now: the piece is on, its spells are read, and nothing more is sent after.
        closet.RefuseWield.Clear();
        WearingPass(r, scenario, loot, closet);
        WearingPass(r, scenario, loot, closet);
        lines = string.Join(" | ", GearLines(r));
        Assert.True(closet.Worn(Greaves), lines);
        Assert.True(LinesWith(r, "loot: gear: put back: 'Greaves' 0x80007B03 on at 0x200: Confirmed") == 1, lines);
        Assert.True(LinesWith(r, "loot: gear: put back: check after: 'Greaves' 0x80007B03 worn, its spells active on her; none taken off for good") == 1, lines);
        Assert.Equal((1, 4), (closet.TakeOffs(Greaves), closet.Wields(Greaves)));
        Assert.Equal(2, Errors());
    }
}
