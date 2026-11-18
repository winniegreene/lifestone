using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (the owner's ruling, 2026-11-16: drop the loot casters too): she carries only the casters her gear rules need (<see cref="CasterKeep"/>: the wielded, the best for each element the fight's line uses, the floor's spare, the mana
/// rule's charged spare, a useful great cantrip, the spend's target, an unread one); every other caster is sell loot. The stones the kit keeps stay; the ones beyond its target are sell loot (<see cref="ItemManaKeeper.Kept"/>).
/// </summary>
public sealed class CasterKeepTests
{
    private const uint Fire = 0x10, Acid = 0x20;

    private static HeldCasterFacts Held(uint guid, string name, bool wielded = false, bool read = true, bool usable = true, bool great = false, int mana = 0, int maxMana = 0) => new(guid, name, wielded, read, usable, great, mana, maxMana);

    /// <summary>
    /// Her live state (ladder44, 2026-11-16 21:29:05 UTC, level 79, the Tusker Quarters): she wields the Piercing Sceptre 0x800003C0 (2.77) and holds 16 more usable casters, each with its caster score on the plan's element as the pick
    /// logged it (the plan names none at level 79, so the scores are the element-free part: mana conversion and defences); the fight's line in hand was the fire line, and no line change was logged. With every score the log gives, the
    /// fire line's best is the wielded sceptre, the floor's spare the Piercing Baton (1.58), and the other 15 are sell loot (their mana is not in the log: no charged spare is read here; the live rule reads it from each appraisal).
    /// </summary>
    [Fact]
    public void Of_her_17_casters_the_wielded_and_the_floors_spare_stay_and_15_are_sell_loot()
    {
        var field = new (uint Guid, string Name, double Score)[]
        {
            (0x800003C0, "Piercing Sceptre", 2.77), (0x8000B715, "Blunt Baton", 1.4), (0x80005ACF, "Electric Sceptre", 1.08), (0x80001282, "Slashing Baton", 1.26), (0x80005AD5, "Electric Sceptre", 1.26), (0x80004D14, "Acid Baton", 0.97),
            (0x800074D8, "Blunt Sceptre", 1.44), (0x8000BA34, "Piercing Baton", 1.58), (0x8000296F, "Electric Baton", 1.4), (0x800094E6, "Fire Baton", 0.72), (0x800020C5, "Staff", 1.24), (0x800001AA, "Training Wand", 0.3),
            (0x80006056, "Piercing Sceptre", 1.08), (0x8000C8D0, "Staff", 0.99), (0x800006B9, "Blunt Sceptre", 1.42), (0x800082F9, "Wand", 1.12), (0x80005794, "Electric Baton", 1.2),
        };
        var casters = field.Select(f => Held(f.Guid, f.Name, wielded: f.Guid == 0x800003C0)).ToList();

        var verdicts = CasterKeep.Choose(casters, [("fire", Fire)], Fire, (_, g) => field.First(f => f.Guid == g).Score, new Dictionary<uint, string>());

        var kept = verdicts.Where(v => v.KeptFor is not null).ToList();
        Assert.Equal([0x800003C0u, 0x8000BA34u], kept.Select(v => v.Guid));
        Assert.Equal("the caster she wields", kept[0].KeptFor);
        Assert.Equal("the gear floor's spare (two casters always; score 1.58 on the element 0x10)", kept[1].KeptFor);
        Assert.Equal(15, verdicts.Count(v => v.KeptFor is null));
        Assert.Contains("'Fire Baton' 0x800094E6: sell loot (not the wielded, no useful great cantrip, not the best for any of the 1 element(s) the fight's line uses (fire), not the floor's spare nor the charged spare)", verdicts.Select(v => v.Line));
        Assert.StartsWith("casters: 17 held, 2 kept, 15 sell loot (the owner, 2026-11-16: only the casters her gear rules need): ", CasterKeep.Line(verdicts), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_rule_keeps_its_caster_by_name_and_every_other_is_sell_loot()
    {
        HeldCasterFacts[] casters =
        [
            Held(1, "Piercing Sceptre", wielded: true),
            Held(2, "Fire Baton"),
            Held(3, "Acid Baton"),
            Held(4, "Staff", mana: 500, maxMana: 1000),
            Held(5, "Wand", mana: 100, maxMana: 1000),
            Held(6, "Orb", read: false),
            Held(7, "Slashing Baton", great: true),
            Held(8, "Fire Sceptre", usable: false),
            Held(9, "Blunt Baton"),
            Held(10, "Electric Baton", usable: false),
        ];
        double Score(uint element, uint guid) => (element, guid) switch
        {
            (Fire, 2) => 3.0, // a fire bonus
            (Acid, 3) => 2.5, // an acid bonus
            (_, 1) => 2.0,
            _ => 1.0,
        };

        var verdicts = CasterKeep.Choose(casters, [("fire", Fire), ("acid", Acid)], Fire, Score, new Dictionary<uint, string> { [8] = "as wielded 4 against 'Piercing Sceptre' 2: gain +2 of her hour, over the swap margin 0.1" });
        var why = verdicts.ToDictionary(v => v.Guid, v => v.KeptFor);

        Assert.Equal("the caster she wields", why[1]);
        Assert.Equal("the best caster for the fire line the fight uses (score 3)", why[2]);
        Assert.Equal("the best caster for the acid line the fight uses (score 2.5)", why[3]);
        Assert.Equal("the mana rule's charged spare (500/1000 mana, the most of the spares)", why[4]);
        Assert.Equal("not appraised: never sold blind", why[6]);
        Assert.Equal("a useful great cantrip", why[7]);
        Assert.StartsWith("the experience spend works toward its wield requirement (", why[8], StringComparison.Ordinal);
        Assert.Null(why[5]); // less mana than the Staff: not the charged spare
        Assert.Null(why[9]); // three usable casters are kept already: the floor wants no spare
        Assert.Null(why[10]); // short of a wield requirement the spend does not work toward
        Assert.Contains("short of a wield requirement the spend does not work toward", verdicts.Single(v => v.Guid == 10).Line, StringComparison.Ordinal);
    }

    /// <summary>ladder42's round held seven loot stones (2 Mana Stone, 3 Moderate, 2 Greater); the kit keeps its target of four, the charged first (the most mana first), then the empty by efficiency.</summary>
    [Fact]
    public void The_kit_keeps_its_target_of_stones_the_charged_first_and_the_rest_are_beyond_it()
    {
        HeldStone[] stones =
        [
            new(0x800055D9, "Mana Stone", null, 0.5), new(0x80004804, "Mana Stone", null, 0.5),
            new(0x800057C4, "Moderate Mana Stone", 2000, 0.6), new(0x80005BE4, "Moderate Mana Stone", null, 0.6), new(0x80005E95, "Moderate Mana Stone", 1500, 0.6),
            new(0x800056A7, "Greater Mana Stone", null, 0.75), new(0x80005CE9, "Greater Mana Stone", null, 0.75),
        ];

        var kept = ItemManaKeeper.Kept(stones, _ => false);

        Assert.Equal(ItemManaKeeper.StoneTarget, kept.Count);
        Assert.Equal(new HashSet<uint> { 0x800057C4, 0x80005E95, 0x800056A7, 0x80005CE9 }, kept); // the two charged, then the two empty Greater by efficiency
    }

    [Fact]
    public void In_the_pack_a_caster_her_rules_do_not_need_and_a_stone_beyond_the_kits_target_are_sell_loot()
    {
        const uint Needed = 0x8C0D0101, Spare = 0x8C0D0102, KitStone = 0x8C0D0103, Extra = 0x8C0D0104;
        var ground = GroundCatalog.Get("tusker-quarters");
        var r = new WorldActionRig();
        var items = new (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name)[]
        {
            (Needed, 9301, 0x8000, 6705, 50, "Slashing Baton"),
            (Spare, 9302, 0x8000, 4009, 50, "Staff"),
            (KitStone, 9303, 0x80000, 5000, 50, "Greater Mana Stone"),
            (Extra, 9304, 0x80000, 2500, 50, "Moderate Mana Stone"),
        };
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, i.Value, i.Burden, i.Guid, i.Name, i.Wcid);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))] });
        var state = r.H.State;
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Needed), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Spare, 1.05), 0); // a +5% war bonus: the keep-aside's war rule no longer keeps it
        var census = new Dictionary<string, int>(StringComparer.Ordinal);

        var noSpells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>();
        var loot = CantripRoom.SellLoot(state, CantripRoomTests.Book(state, ground, new KeepAsideRule(KeepAsideRule.ClimberDefault), noSpells, casters: g => g == Needed ? "the best caster for the fire line the fight uses (score 3)" : null, stones: g => g == KitStone), census);

        Assert.Equal([(Spare, "gear-loot"), (Extra, "mana-stones-shed")], loot.Select(l => (l.Guid, l.Rule)));
        Assert.Equal(1, census["pack rule gear-caster-kept (keep)"]); // M6 16 E5c2: a keep is named by the rule book's row
        Assert.Equal(1, census["pack rule mana-stones (keep)"]);

        // M6 16 E5c2: a read that names no caster or stone rule asks the session's gear rules (it kept every caster and stone before): a caster is sell loot exactly when those rules do not need it, and a stone she has not
        // appraised is kept
        var keeps = CantripRoom.GearKeeps(state, ground, noSpells, Lifestone.Client.Session.Quartermaster.Wardrobe.For(state).KeepCastChance);
        var sessions = CantripRoom.SellLoot(state, CantripRoomTests.Book(state, ground, null, noSpells));
        Assert.Equal(new[] { Needed, Spare }.Where(g => keeps.Casters(g) is null), sessions.Where(l => l.Guid is Needed or Spare).Select(l => l.Guid));
        Assert.DoesNotContain(sessions, l => l.Guid is KitStone or Extra);
    }
}
