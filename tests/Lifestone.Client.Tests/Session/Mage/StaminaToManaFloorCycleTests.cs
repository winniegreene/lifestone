using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 4 G1, the buff cycle's mana loop through the level floor on both dungeon profiles' kits (the SpellTable's own formulas, the pack the ground's kit builds). Live, after the scripted death the rebuff ran its mana loop at mana 1 of 415:
/// the dance's level is the highest a press can land at the mana held (<see cref="ManaOrder.DanceLevelOf"/>), which at mana 1 is one of the low levels, here V (spell 1680), whose Gold Scarab no stock line carries; ACE refused the
/// cast 0x0400 and the cycle failed. The floor passes over every level the pack cannot pay for.
/// </summary>
public sealed class StaminaToManaFloorCycleTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    public static TheoryData<string> Dungeons => new() { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" };

    private static readonly uint[] Levels = [.. VitalsMeans.StaminaToManaSelf];

    private uint _guid = 0x80000E00;

    private void Give(WorldActionRig r, uint wcid, string name, int count)
    {
        var guid = _guid++;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        r.H.R.Reducer.OnCreateObject(
            r.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = 0x1000, StackSize = (ushort)count } },
            isUpdate: false);
        r.Server((player, sequence) => AceM3Writers.PutObjInContainer(player, sequence, guid, WorldActionRig.Player, 0, 0));
    }

    /// <summary>The levels of Stamina to Mana Self the kit's components pay for on the foci formula.</summary>
    private static List<uint> PayableLevels(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> real)
    {
        var held = kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus).GroupBy(i => i.Wcid).ToDictionary(g => g.Key, g => g.Sum(i => i.Stack));
        return [.. Levels.Where(id => !LevelFloor.ShortOf(held, real[id], out _))];
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void M5_4_G1_the_buff_cycles_mana_loop_never_presses_a_Stamina_to_Mana_level_the_grounds_kit_cannot_pay_for(string ground)
    {
        var real = SpellTableData.Load(fixture.Dats).Spells;
        var grounds = GearKit.Build(real, "gear-test-account", ground: GroundCatalog.Get(ground));
        Assert.Equal(ground == "black-spawn-den-aluvian", PayableLevels(grounds, real).Contains(1680u)); // M5 7 F25 (D93): the den carries the Gold Scarab now, so its kit pays for level V; Fort Tethana's does not
        var kit = GearKit.Of([.. grounds.Items.Where(i => i.Wcid is not (687 or 688))]); // the floor is proved on a pack without the Silver and Gold Scarabs (as the den's kit was before F25)
        var payable = PayableLevels(kit, real);
        Assert.DoesNotContain(1680u, payable); // level V needs a Gold Scarab, which this pack does not carry
        Assert.NotEmpty(payable);

        var r = new WorldActionRig();
        var caster = new ScriptedCaster(r, 1, 275); // the live pass: mana 1 of 415
        foreach (var id in caster.Table.Keys.ToList())
        {
            if (real.TryGetValue(id, out var spell))
            {
                caster.Table[id] = caster.Table[id] with { Formula = spell.Formula, FormulaVersion = spell.FormulaVersion };
            }
        }

        foreach (var item in kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus))
        {
            Give(r, item.Wcid, item.Name, item.Stack);
        }

        // The cause, pinned: the highest level a press can land at mana 1 is a low one the kit does not carry, so without the floor the dance presses it.
        var mc = caster.ManaConversionSkill;
        var levels = Levels.Select(id => new DanceLevel(id, GearCasts.ManaCeiling(real[id], 0), real[id].Power, BuffStatMods.TransferOf(id)!.Value)).ToArray();
        var unfloored = ManaOrder.DanceLevelOf(levels, 1, mc);
        Assert.True(unfloored >= 0 && !payable.Contains(levels[unfloored].SpellId), $"without the floor the dance at mana 1 presses {(unfloored >= 0 ? levels[unfloored].SpellId : 0)}, payable {string.Join(',', payable)}");

        var cycle = new BuffCycle(r.Api, r.H.State, caster.Table, new BuffOptions(), r.H.Log);
        Assert.True(cycle.Start(r.H.R.Clock.Now));
        var end = r.H.R.Clock.Now + 4000;
        var status = BuffStatus.Running;
        while (status == BuffStatus.Running && r.H.R.Clock.Now < end)
        {
            r.Tick(0.25);
            caster.Pump();
            r.Tick();
            status = cycle.Step(r.H.R.Clock.Now);
        }

        var transfers = caster.Answered.Select(a => a.Spell).Where(Levels.Contains).ToList();
        Assert.NotEqual(BuffStatus.Failed, status);
        Assert.All(transfers, id => Assert.Contains(id, payable)); // no level the kit lacks the scarab for was pressed (the old cycle pressed 1680)
        Assert.Contains(r.H.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.StartsWith("Stamina to Mana Self: level V needs Gold Scarab 687, which the pack does not hold; skipped", StringComparison.Ordinal));
        Assert.DoesNotContain(r.H.Log.Entries, e => e.Severity >= Lifestone.Client.Diagnostics.LogSeverity.Error);
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void M5_4_G1_the_keeper_casts_only_levels_of_Heal_Self_Revitalize_Self_and_Stamina_to_Mana_Self_the_grounds_kit_can_pay_for(string ground)
    {
        var real = SpellTableData.Load(fixture.Dats).Spells;
        var kit = GearKit.Build(real, "gear-test-account", ground: GroundCatalog.Get(ground));
        var held = kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus).GroupBy(i => i.Wcid).ToDictionary(g => g.Key, g => g.Sum(i => i.Stack));
        uint[] all = [.. VitalsMeans.HealSelf, .. VitalsMeans.RevitalizeSelf, .. Levels];

        var rig = new C2VitalsRig(new C2Character
        {
            HealingClass = 1,
            LifeMagicRanks = 400,
            Spells = all,
            Health = 60,
            Stamina = 40,
            Mana = 25,
            Pack = [.. kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus).Select(i => (i.Wcid, i.Stack))],
        });
        foreach (var id in all)
        {
            rig.Table[id] = rig.Table[id] with { Formula = real[id].Formula, FormulaVersion = real[id].FormulaVersion };
        }

        Assert.True(all.Any(id => LevelFloor.ShortOf(held, real[id], out _)), "the kit lacks some level (V's Gold Scarab), or the test proves nothing");
        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        rig.RunToEnd();

        var casts = rig.Casts.Select(c => c.Spell).Where(all.Contains).ToList();
        Assert.NotEmpty(casts);
        Assert.All(casts, id => Assert.False(LevelFloor.ShortOf(held, real[id], out var need), $"spell {id} ({real[id].Name}) needs {need}"));
        Assert.DoesNotContain(rig.R.H.Log.Entries, e => e.Severity >= Lifestone.Client.Diagnostics.LogSeverity.Error);
    }
}
