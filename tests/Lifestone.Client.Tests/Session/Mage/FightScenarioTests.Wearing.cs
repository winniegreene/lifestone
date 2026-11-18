using System.Reflection;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 6 FXD3 (6-D9): a hit while the loot's Wearing stage has a wield in flight. The smallest rig that reaches the stage: the loot step is put into Wearing by reflection (a real jewellery wear needs a keep-aside cantrip item, a
/// spell table, an appraisal with spells and a free slot, none of which the rig builds), with a real wield handle the scripted ACE never answers. What is proved is the stage's attacker arm: the in-flight wield is read first
/// (up to the loot step's action seconds, logged once), its late or missing outcome is logged, and only then does the fight go first.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint RingGuid = 0x80007A01;

    private static object LootOf(FightScenario scenario) =>
        typeof(FightScenario).GetField("_loot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario)!;

    private static FieldInfo LootField(object loot, string name) => loot.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void A_hit_while_a_wield_is_in_flight_in_the_wearing_stage_waits_for_its_outcome_logs_it_and_then_the_fight_goes_first()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 20)) { Corpses = true };
        StockCorpse(r);
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(VirindiGuid, Virindi, Den, 40, "Virindi", hitsToKill: 100_000);
        var scenario = r.Scenario(new FakeWalk());
        var loot = LootOf(scenario);
        var forced = false;

        for (var i = 0; i < 400 && !forced; i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (a.Dead && !forced && (bool)loot.GetType().GetProperty("Active")!.GetValue(loot)!)
                {
                    forced = true;
                    var stage = loot.GetType().GetNestedType("Stage", BindingFlags.NonPublic)!;
                    var ring = Fighter.LootRulesTests.Item(0x8, 100, 5, RingGuid, "Gold Ring", 2500).Created; // a ring in the pack, wearable on a free finger
                    r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, ring with { Weenie = ring.Weenie with { ValidLocations = (uint)EquipMask.FingerWearRight } }, isUpdate: false);
                    r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, RingGuid, WorldActionRig.Player, 0, 0));
                    r.R.Tick(0.01);
                    var wield = r.View.Actions.Wield(RingGuid, EquipMask.FingerWearRight, ActionSource.Automation); // the rig never answers a wield: it stays in flight
                    LootField(loot, "_stage").SetValue(loot, Enum.Parse(stage, "Wearing"));
                    LootField(loot, "_pending").SetValue(loot, wield);
                    LootField(loot, "_corpse").SetValue(loot, CorpseA);
                    LootField(loot, "_stageStart").SetValue(loot, r.Now);
                    LootField(loot, "_corpseStart").SetValue(loot, r.Now);
                    MeleeHit(r);
                }
            });
        }

        Assert.True(forced);
        r.Frame(scenario);
        Assert.True(LinesWith(r, "while a wield (0x") == 1, string.Join(" | ", r.Log.Lines.TakeLast(15)));
        Assert.Equal(0, LinesWith(r, "the fight goes first") - LinesWith(r, "its outcome is read first")); // no "fight goes first" warning yet: the wield is still read
        Assert.Equal(0, LinesWith(r, "were judged for the self-equip"));

        for (var i = 0; i < 60; i++) // past the action seconds: the wait ends, the outcome is logged, the fight goes first
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "while a wield (0x")); // logged once
        Assert.Equal(1, LinesWith(r, "were judged for the self-equip"));
        Assert.Equal(1, LinesWith(r, "loot: gear: the wield ended TimedOut")); // the 5 s action timeout answers it; read before the fight went first
        Assert.Equal(1, LinesWith(r, "(read before the fight went first)"));
        Assert.Contains(VirindiGuid, r.CastTargets);
    }

    /// <summary>
    /// M6 16 E5e2: the loot's replace in hand sends its wield through the wear service (<c>Wardrobe.Send</c>). The state the step needed of its own: a piece that left her packs between the check before the swap and
    /// its wield (sold, dropped, moved by another step). Before, the step sent the wield again on every frame its handle was empty; the wear service's check before sends nothing for a piece she does not hold, so the
    /// step names it once and ends the swap by its recovery. The loot step is put into Wearing with a swap in hand by reflection, as the test above reaches the stage.
    /// </summary>
    [Fact]
    public void M6_16_E5e2_a_replace_in_hand_whose_piece_is_no_longer_held_sends_no_wield_names_it_once_and_ends_by_its_recovery()
    {
        const uint Gone = 0x80007A02;
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 20)) { Corpses = true };
        StockCorpse(r);
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        var loot = LootOf(scenario);
        var forced = false;

        for (var i = 0; i < 400 && !forced; i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (a.Dead && !forced && (bool)loot.GetType().GetProperty("Active")!.GetValue(loot)!)
                {
                    forced = true;
                    var stage = loot.GetType().GetNestedType("Stage", BindingFlags.NonPublic)!;
                    var swap = loot.GetType().GetNestedType("GearSwap", BindingFlags.NonPublic)!;
                    var choice = new Lifestone.Client.Session.Mage.SelfEquipChoice(Lifestone.Client.Session.Mage.SelfEquipKind.Replace, Gone, "Gone Cap", (uint)EquipMask.HeadWear, 0, "a test's replace");
                    LootField(loot, "_stage").SetValue(loot, Enum.Parse(stage, "Wearing"));
                    LootField(loot, "_pending").SetValue(loot, null);
                    LootField(loot, "_corpse").SetValue(loot, CorpseA);
                    LootField(loot, "_stageStart").SetValue(loot, r.Now);
                    LootField(loot, "_corpseStart").SetValue(loot, r.Now);
                    LootField(loot, "_swap").SetValue(loot, Activator.CreateInstance(swap, choice, new List<Lifestone.Client.Session.Mage.WornPiece>(), WorldActionRig.Player, "the main pack", false, r.Now));
                }
            });
        }

        Assert.True(forced);
        for (var i = 0; i < 10; i++)
        {
            r.Frame(scenario);
        }

        Assert.DoesNotContain(r.R.H.Net.Of(FakeSendKind.GetAndWieldItem), s => s.Guid == Gone); // nothing sent for a piece she does not hold
        Assert.True(LinesWith(r, "loot: gear: replace: check before failed: 'Gone Cap' 0x80007A02 is not held: nothing sent") == 1, string.Join(" | ", r.Log.Lines.TakeLast(15)));
        Assert.Equal(1, LinesWith(r, "loot: gear: replace with 'Gone Cap' 0x80007A02 did not land: 0 piece(s) put back"));
    }
}
