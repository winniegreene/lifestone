using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 6 R1G: the quest step's kill picks its war spell by <see cref="WarBoltPick.ChooseCastable"/> (the pack's components, the bolts refused on the leg left out) and enters the magic mode before each cast, as the run leg and the
/// harm-heal station do (R1F). Before, <see cref="QuestStepLeg"/> chose blind and a 0x0400 failed the step by name.
/// </summary>
public sealed class QuestKillBoltTests
{
    private const uint HomeCell = 0x860201AD; // the rig player's cell, at (10, 20)
    private const uint Creature = 0x80001600;
    private const uint CreatureWcid = 4110;
    private static readonly uint BoltI = VitalsMeans.RevitalizeSelf[0];
    private static readonly uint BoltIII = VitalsMeans.RevitalizeSelf[1];

    private sealed class Probe(JourneyContext context) : QuestStepLeg("kill-probe", context)
    {
        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in KillOne(CreatureWcid, "Blood Shreth", HomeCell, new System.Numerics.Vector3(10, 20, 0), new Victim())) { yield return h; }
        }
    }

    private static C2VitalsRig PackThatPays(params uint[] paid)
    {
        var bolts = new Dictionary<uint, SpellInfo>
        {
            [BoltI] = WorldActionRig.Spell(BoltI, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Flame Bolt I", School = DatReaderWriter.Enums.MagicSchool.WarMagic, Power = 1, BaseMana = 10, Formula = [1, 0, 0, 0, 0, 0, 0, 0], BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant },
            [BoltIII] = WorldActionRig.Spell(BoltIII, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Flame Bolt III", School = DatReaderWriter.Enums.MagicSchool.WarMagic, Power = 100, BaseMana = 15, Formula = [3, 0, 0, 0, 0, 0, 0, 0], BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant }, // M6 16 Q8: a range, as the dat's row has (the skirmish casts within it)
        };
        var pack = new Dictionary<uint, int> { [LevelFloor.FocusOf(DatReaderWriter.Enums.MagicSchool.WarMagic)] = 1 };
        foreach (var id in paid)
        {
            foreach (var (component, n) in GearKit.FociFormulaCounts(bolts[id].Formula))
            {
                Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie));
                pack[weenie.Wcid] = Math.Max(pack.GetValueOrDefault(weenie.Wcid), n);
            }
        }

        var vitals = new C2VitalsRig(new C2Character { Caster = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = [.. VitalsMeans.HealSelf, BoltI, BoltIII], Pack = [.. pack.Select(p => (p.Key, p.Value))] });
        foreach (var (id, info) in bolts)
        {
            vitals.Table[id] = info;
        }

        vitals.R.Spells([.. vitals.Table.Values]);
        var basis = ReducerHarness.Create(Creature, ReducerHarness.Stamps(), ReducerHarness.Position(HomeCell, 14, 20), name: "Blood Shreth", itemType: 0x10);
        vitals.R.H.R.Reducer.OnCreateObject(vitals.R.H.R.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = CreatureWcid } }, isUpdate: false);
        return vitals;
    }

    [Fact]
    public void M6_6_R1G_the_kill_casts_the_bolt_the_pack_pays_for_with_the_magic_mode_checked_first_and_the_creature_dies()
    {
        var vitals = PackThatPays(BoltIII);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        var run = new Probe(rig.Context("Winnie"));
        var dead = false;
        var status = rig.Run(run, 200, pump: () =>
        {
            vitals.Pump();
            if (!dead && vitals.Casts.Count > 0)
            {
                vitals.R.H.R.Reducer.OnDeleteObject(vitals.R.H.R.Context, new DeleteObjectMsg(Creature, 1));
                dead = true;
            }
        });

        var lines = vitals.R.H.Log.Lines;
        Assert.True(dead && status == ScenarioStatus.Succeeded, run.Failure + " | " + string.Join(" | ", lines.TakeLast(8)));
        Assert.DoesNotContain(vitals.Casts, c => c.Spell == BoltI); // the bolt the pack is short of is never cast
        Assert.Contains(vitals.Casts, c => c.Spell == BoltIII);
        Assert.Contains(lines, l => l.Contains("the war spell is Flame Bolt III", StringComparison.Ordinal) && l.Contains("skipped: Flame Bolt I needs", StringComparison.Ordinal));
        Assert.True(vitals.Used.IndexOf("mode:8") is var mode && mode >= 0 && mode < vitals.Used.IndexOf("cast:" + BoltIII), string.Join(" > ", vitals.Used));
    }

    [Fact]
    public void M6_6_R1G_a_pack_that_pays_for_no_bolt_stops_the_kill_named_no_components_and_casts_nothing()
    {
        var vitals = PackThatPays();
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        var run = new Probe(rig.Context("Winnie"));
        var status = rig.Run(run, 200, pump: vitals.Pump);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("no components", run.Failure, StringComparison.Ordinal);
        Assert.Empty(vitals.Casts);
        Assert.Contains(vitals.R.H.Log.Lines, l => l.Contains("kill: no-components: ", StringComparison.Ordinal) && l.Contains("Flame Bolt III needs", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 6 FXD2 (6-D7): a 0x0400 that reaches the kill only as chat (the UseDone had already ended the cast) still drops the bolt for the step, as the run leg reads it (the chat's refusal code since the cast): the next cast is the other bolt,
    /// never the refused one again.
    /// </summary>
    [Fact]
    public void M6_6_FXD2_a_component_refusal_that_arrives_only_as_chat_drops_the_bolt_for_the_step()
    {
        var vitals = PackThatPays(BoltI, BoltIII);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        var run = new Probe(rig.Context("Winnie"));
        var dead = false;
        var told = false;
        var status = rig.Run(run, 200, pump: () =>
        {
            var before = vitals.Casts.Count;
            vitals.Pump();
            if (!told && vitals.Casts.Count > before)
            {
                told = true;
                vitals.R.WeenieError(0x0400); // after the UseDone: the chat carries it, the cast's handle has ended without it
            }
            else if (told && !dead && vitals.Casts.Count > 1)
            {
                vitals.R.H.R.Reducer.OnDeleteObject(vitals.R.H.R.Context, new DeleteObjectMsg(Creature, 1));
                dead = true;
            }
        });

        var lines = vitals.R.H.Log.Lines;
        Assert.True(dead && status == ScenarioStatus.Succeeded, run.Failure + " | " + string.Join(" | ", lines.TakeLast(8)));
        Assert.Equal(2, vitals.Casts.Count);
        Assert.NotEqual(vitals.Casts[0].Spell, vitals.Casts[1].Spell); // never the refused bolt again
        Assert.Contains(lines, l => l.Contains("refused for a missing component", StringComparison.Ordinal));
    }
}
