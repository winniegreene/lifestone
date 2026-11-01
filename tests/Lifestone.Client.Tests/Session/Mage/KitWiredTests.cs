using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 3 w1, "the kit wired" (m5-plan section 4.5 row w1; D8, D14, D46, D48): m1 rebuilt the mage's kit from the ground's supplies and left four things outside its files, each proved here. The hosts' share is in
/// <c>J3bBuildStationTests</c> (the build scenario each host makes carries the ground's kit plan, so the granting stage runs). Here: the wand is the kit's wielded line, never weenie 8552 (the keeper, the cast-facts
/// station, the buff leg); the legs build the kit of their <c>--fight-ground</c>; and the buff cycle never picks, or steps down onto, a level whose foci-formula components the pack does not hold.
/// </summary>
public sealed class KitWiredTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint VendorWand = 2472;

    /// <summary><c>GearKit.Wand</c> (the retired loot wand, weenie 8552) as code or doc text; <c>GearKit.WandItem</c> (the kit's wielded line) is the right one.</summary>
    private static readonly System.Text.RegularExpressions.Regex NoWandWeenie = new(@"GearKit\.Wand(?!Item)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string Source(params string[] path) => File.ReadAllText(RepositoryFiles.PathOf(new[] { "src" }.Concat(path).ToArray()));

    private static string Game(string file) => Source("Lifestone.Game", "scripts", "Session", file);

    // ---------------------------------------------------------------------------------------------- the wand (item 2)

    [Fact]
    public void The_keeper_finds_the_vendor_wand_in_the_pack_by_its_item_type_and_reports_cast_needs_swap_M5_3_w1()
    {
        // The retired loot wand (8552) is not the one the mage carries any more: the kit's wielded line is the vendor Wand 2472. A caster held in the pack with a weapon in hand is found by its type (ItemType Caster), whatever its weenie.
        uint[] healSelf = [6, 1157, 2073];
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Caster = false, CasterInPack = true, CasterWcid = VendorWand, Potions = 5, Spells = healSelf, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.DoesNotContain(rig.Used, u => u.StartsWith("cast:", StringComparison.Ordinal) || u.StartsWith("mode:", StringComparison.Ordinal));
        Assert.Equal(VitalsKeeper.NeedsSwap, rig.Keeper.CastOff);
        Assert.EndsWith(" cast=needs-swap", rig.Keeper.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_keeper_with_the_wand_wielded_is_not_told_to_swap_whatever_its_weenie_M5_3_w1()
    {
        uint[] healSelf = [6, 1157, 2073];
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Caster = true, CasterWcid = VendorWand, Potions = 5, Spells = healSelf, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        rig.RunToEnd();

        Assert.NotEqual(VitalsKeeper.NeedsSwap, rig.Keeper.CastOff);
    }

    [Fact]
    public void The_cast_facts_station_finds_the_equipped_wand_by_the_kits_wielded_line_not_weenie_8552_M5_3_w1()
    {
        var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Caster = true, CasterWcid = VendorWand });
        var vendor = GearKit.Of([new GearItem(VendorWand, "Wand", GearKind.Wand, GearKit.WeenieTypeCaster, GearKit.ItemTypeCaster, EquipMask.Held, 1, null, 50)]);
        var loot = GearKit.Of([new GearItem(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.WeenieTypeCaster, GearKit.ItemTypeCaster, EquipMask.Held, 1, null, 50)]);

        Assert.Equal(C2VitalsRig.WandGuid, CastFactsRun.EquippedWand(rig.R.H.State, vendor)); // the kit's wielded line is 2472 and the mage wields 2472
        Assert.Equal(0u, CastFactsRun.EquippedWand(rig.R.H.State, loot)); // a kit that names the retired 8552 finds nothing: the wand is the kit's line, never a constant
    }

    [Fact]
    public void The_buff_leg_the_keeper_and_the_cast_facts_station_name_no_wand_weenie_in_their_source_M5_3_w1()
    {
        // The leg is Godot code: its decisions are read from the source, as the other legs' are (FightLegRunnerTests).
        var buffLeg = Game("BuffLegRunner.cs");
        Assert.DoesNotMatch(NoWandWeenie, buffLeg);
        Assert.Contains("_wandWcid = _loading.Result.Kit.WandItem.Wcid", buffLeg, StringComparison.Ordinal);
        Assert.Contains("WeenieClassId == _wandWcid", buffLeg, StringComparison.Ordinal);

        var keeper = Source("Lifestone.Client", "Session", "Vitals", "VitalsKeeper.cs");
        Assert.DoesNotMatch(NoWandWeenie, keeper);
        Assert.Contains("GearKit.ItemTypeCaster", keeper, StringComparison.Ordinal);

        var heal = Source("Lifestone.Client", "Session", "Scenarios", "Journey", "HealStations.cs");
        Assert.DoesNotMatch(NoWandWeenie, heal);
        Assert.Contains("kit.WandItem.Wcid", heal, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_mages_wand_is_the_grounds_wielded_vendor_line_for_both_dungeons_never_the_retired_loot_wand_M5_3_w1()
    {
        foreach (var key in new[] { FightOptions.DefaultGround, "tusker-temple-fort-tethana" })
        {
            var ground = GroundCatalog.Get(key);
            var kit = GearKit.Build(SpellTableData.Load(fixture.Dats).Spells, "test", ground: ground);
            var wielded = Assert.Single(ground.Supplies.Kit, k => k.Slot == KitSlot.Wielded);

            Assert.Equal(wielded.Wcid, kit.WandItem.Wcid);
            Assert.NotEqual(GearKit.Wand.Wcid, kit.WandItem.Wcid); // 8552 The Healer's Heart is the retired loot wand
        }
    }

    // ---------------------------------------------------------------------------------------------- the legs build the ground's kit (item 3)

    [RequiresDatsFact]
    public void A_leg_on_Tusker_Temple_builds_Tusker_Temples_kit_and_the_den_the_dens_M5_3_w1()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var den = GearKit.Build(spells, "test", ground: GroundCatalog.Get(FightOptions.DefaultGround));
        var tethana = GearKit.Build(spells, "test", ground: GroundCatalog.Get("tusker-temple-fort-tethana"));

        Assert.Contains(den.Items, i => i.Wcid == 5851 && i.Kind == GearKind.Armour); // the den's Faran robe with a hood (M5 7 F13, D87)
        Assert.Contains(den.Items, i => i.Wcid == 56 && i.Kind == GearKind.Armour); // and its leather gauntlets
        Assert.DoesNotContain(den.Items, i => i.Wcid == 6071);
        Assert.DoesNotContain(tethana.Items, i => i.Wcid is 5851 or 56 or 6071);
        Assert.Contains(tethana.Items, i => i.Wcid == 5910 && i.Kind == GearKind.Armour); // Tusker Temple's Dho robe
        Assert.Contains(tethana.Items, i => i.Kind == GearKind.Focus); // both carry the four foci
    }

    [Fact]
    public void The_fight_leg_the_buff_leg_and_the_cast_facts_station_pass_their_fight_ground_to_the_kit_M5_3_w1()
    {
        var fightLeg = Game("FightLegRunner.cs");
        Assert.Contains("GearKit.Build(table.Spells, LoginName(key), ground: ground)", fightLeg, StringComparison.Ordinal);
        Assert.Contains("var ground = options.GroundProfile;", fightLeg, StringComparison.Ordinal); // the leg's --fight-ground

        var buffLeg = Game("BuffLegRunner.cs");
        Assert.Contains("GearKit.Build(table.Spells, LoginName(key), ground: ground)", buffLeg, StringComparison.Ordinal);
        Assert.Contains("var ground = fight.GroundProfile;", buffLeg, StringComparison.Ordinal);
        Assert.Contains("--fight-ground:", buffLeg, StringComparison.Ordinal); // a bad key is the leg's option error, not a silent default

        var heal = Source("Lifestone.Client", "Session", "Scenarios", "Journey", "HealStations.cs");
        Assert.Contains("GearKit.Build(Context.Spells, _account, ground: Context.Dungeon)", heal, StringComparison.Ordinal);

        foreach (var leg in new[] { fightLeg, buffLeg })
        {
            Assert.DoesNotContain("GearKit.Build(table.Spells, LoginName(key));", leg, StringComparison.Ordinal); // no default-ground build left
        }
    }

    // ---------------------------------------------------------------------------------------------- the buff cycle's level floor (item 4)

    private const uint FocusOfEnchantment = 15268;
    private const uint PrismaticTaper = 20631;

    /// <summary>The Creature Enchantment line "Focus Self" with a scarab-only base formula per level (Lead to Pyreal for I to VI, component 110 for VII), so each level has the foci-formula cost ACE gives it.</summary>
    private static void GiveFocusFormulas(Dictionary<uint, SpellInfo> table)
    {
        var line = ArenaProfile.LineNamed("Focus Self")!;
        for (var i = 0; i < line.Spells.Count; i++)
        {
            var id = line.Spells[i].Id;
            table[id] = table[id] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
        }
    }

    private sealed class FloorRig
    {
        private uint _guid = 0x80000F00;

        public FloorRig(params (uint Wcid, string Name, int Count)[] pack)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            GiveFocusFormulas(Caster.Table);
            foreach (var (wcid, name, count) in pack)
            {
                Give(wcid, name, count);
            }

            Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, new BuffOptions(), R.H.Log);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffCycle Cycle { get; }

        private void Give(uint wcid, string name, int count)
        {
            var guid = _guid++;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
            R.H.R.Reducer.OnCreateObject(
                R.H.R.Context,
                basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = 0x1000, StackSize = (ushort)count } },
                isUpdate: false);
            R.Server((player, sequence) => AceM3Writers.PutObjInContainer(player, sequence, guid, WorldActionRig.Player, 0, 0));
        }

        public BuffStatus Run()
        {
            Assert.True(Cycle.Start(R.H.R.Clock.Now));
            var end = R.H.R.Clock.Now + 4000;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && R.H.R.Clock.Now < end)
            {
                R.Tick(0.25);
                Caster.Pump();
                R.Tick();
                status = Cycle.Step(R.H.R.Clock.Now);
            }

            return status;
        }

        public IEnumerable<uint> FocusCasts => Caster.Answered.Select(a => a.Spell).Where(id => ArenaProfile.LineNamed("Focus Self")!.Spells.Any(s => s.Id == id));
    }

    private static string Need(uint componentId, string tail)
    {
        Assert.True(ComponentWeenies.TryGetByComponent(componentId, out var c));
        return $"{c.Name} {c.Wcid}{tail}";
    }

    [Fact]
    public void A_level_whose_foci_components_the_pack_does_not_hold_is_skipped_by_name_once_and_the_line_is_reported_down_M5_3_w1()
    {
        // The pack holds the Foci of Enchantment (so ACE casts Creature Enchantment on the foci formula), the Gold Scarab and the tapers, but no Pyreal Scarab: Focus Self VI (the rule's level from this skill) is not payable, V is.
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new FloorRig((FocusOfEnchantment, "Foci of Enchantment", 1), (687, "Gold Scarab", 10), (PrismaticTaper, "Prismatic Taper", 50));

        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        var skipVi = $"Focus Self: level VI needs {Need(6, ", which the pack does not hold")}; skipped";
        Assert.Equal(1, t.R.H.Log.Lines.Count(l => l.EndsWith(skipVi, StringComparison.Ordinal))); // logged once, though the upgrade pass chooses again (the MISS line that follows carries the reason too, and ends otherwise)
        Assert.Equal([focus.Spells[4].Id], t.FocusCasts.Distinct()); // V is cast, never VI or VII
        Assert.Contains(BuffCycle.ShortNameOf("Focus Self") + ":down", r.Misses);
        Assert.Contains(r.MissDetails, d => d.Contains(skipVi[..^"; skipped".Length], StringComparison.Ordinal) && d.Contains("; cast " + focus.Spells[4].Name + " instead", StringComparison.Ordinal));
        Assert.Contains("levels=", r.Fields, StringComparison.Ordinal);
        Assert.Equal("V", r.Levels.Split(',').Select(l => l.Split(':')).First(p => p[0] == BuffCycle.ShortNameOf("Focus Self"))[1]);
    }

    [Fact]
    public void A_line_with_no_level_the_pack_can_pay_for_casts_nothing_and_is_a_named_miss_M5_3_w1()
    {
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new FloorRig((FocusOfEnchantment, "Foci of Enchantment", 1), (PrismaticTaper, "Prismatic Taper", 50)); // no scarab of any level

        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        Assert.Empty(t.FocusCasts);
        Assert.Contains(BuffCycle.ShortNameOf("Focus Self") + ":no-level", r.Misses);
        Assert.Contains(r.MissDetails, d => d.StartsWith("Focus Self: no level the pack can pay for (level VI needs " + Need(6, ", which the pack does not hold"), StringComparison.Ordinal));
        Assert.Equal("-", r.Levels.Split(',').Select(l => l.Split(':')).First(p => p[0] == BuffCycle.ShortNameOf("Focus Self"))[1]);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("Focus Self: level V needs " + Need(5, ", which the pack does not hold") + "; skipped", StringComparison.Ordinal)); // each skipped level is named too
        Assert.NotEmpty(focus.Spells);
    }

    [Fact]
    public void A_fizzle_never_steps_down_onto_a_level_the_pack_cannot_pay_for_M5_3_w1()
    {
        // D14: VI fizzles on all three tries; the level below is V, which needs the Gold Scarab the pack does not hold, so it is not cast and the miss says so.
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new FloorRig((FocusOfEnchantment, "Foci of Enchantment", 1), (690, "Pyreal Scarab", 10), (PrismaticTaper, "Prismatic Taper", 50));
        t.Caster.FizzleNext(focus.Spells[5].Id, 3);

        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        Assert.Equal([focus.Spells[5].Id, focus.Spells[5].Id, focus.Spells[5].Id], t.FocusCasts); // three tries at VI, never V
        Assert.Contains(BuffCycle.ShortNameOf("Focus Self") + ":fizzled", r.Misses);
        Assert.Contains(r.MissDetails, d => d.Contains("no level below is one the pack can pay for", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_schools_focus_in_the_pack_no_floor_applies_and_the_cycle_is_unchanged_M5_3_w1()
    {
        // A fighter's pack (or the older mage kit) holds no focus: it casts its account's customised formula, which the cycle cannot name, so every level stays castable and nothing is reported down.
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new FloorRig((690, "Pyreal Scarab", 1)); // a scarab, but no Foci of Enchantment

        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        Assert.Empty(r.Misses);
        Assert.Equal(focus.Spells[5].Id, t.FocusCasts.First()); // VI, as the rule gives it
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("which the pack does not hold", StringComparison.Ordinal));
    }
}
