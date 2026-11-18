using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Autopilot;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 walk drains (the owner, 2026-11-15: the runs drain as the grounds do; the target's health and kind are known data; live ladder23 02:51:43 UTC: the walk's heal pick said "Heal Self VI h/s=9.407 vs Heal Self h/s=9.407 (no drain
/// offered) ... no target of known health in reach" with Reedshark Ravagers on her): the travel vitals guard offers Drain Health Other on the walk's fight-back target, its health known from its facts (<see cref="KnownTargetHealth"/>:
/// the world database's 375 for a Reedshark Ravager, less what she dealt it). On that shape the pick is a drain, not Heal Self; on a kind that lets no drain through (an Armored Tusker, wcid 11540, resistance 0) it stays Heal Self.
/// </summary>
public sealed class WalkDrainTests
{
    private const uint Guid = 0x80006001;

    private static (FightManaTests.Rig Rig, AutopilotVitalsGuard Guard, HostileWatch Watch) Shape(uint wcid, string name, Action<FightManaTests.Rig>? before = null)
    {
        var rig = new FightManaTests.Rig(mana: 300, stamina: 200, knowsDrain: true);
        rig.Caster.ManaRegenPerSecond = 0;
        AutopilotF30Tests.Settle(rig);
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 40 / 100);
        var here = rig.R.H.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(Guid, ReducerHarness.Stamps(), new AcPosition(here.CellId, here.Origin + new Vector3(2.5f, 0, 0), Quaternion.Identity), name: name);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = HostileReach.CreatureItemType, ObjectDescriptionFlags = HostileReach.AttackableFlag } }, isUpdate: false);
        rig.R.Tick();
        before?.Invoke(rig); // M6 16 Q5: what happened to it before it became the fight-back's target
        var max2 = CreatureFacts.All.MaxHealthOf(wcid, out var source)!.Value;
        var known = new KnownTargetHealth(Guid, wcid, name, max2, Lifestone.Client.Session.Grounds.CreatureTable.Find(wcid)!.MagicDefence, source, rig.R.H.State.Chat.Total);
        var watch = new HostileWatch(rig.View, rig.Fight.GroundProfile, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, rig.Fight.GroundProfile, new AutopilotOptions(), 0.9, watch, rig.Log) { WalkTarget = () => known };
        return (rig, guard, watch);
    }

    private static string FirstPick(FightManaTests.Rig rig, AutopilotVitalsGuard guard)
    {
        var until = rig.Now + 20;
        while (rig.Now < until)
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "travel");
            if (rig.Log.Lines.FirstOrDefault(l => l.Contains("guard: travel: heal pick", StringComparison.Ordinal) || l.Contains("guard: travel: Heal Self", StringComparison.Ordinal) || (l.Contains("guard: travel: ", StringComparison.Ordinal) && l.Contains(" at health ", StringComparison.Ordinal) && l.Contains("(line ", StringComparison.Ordinal))) is { } line)
            {
                return line;
            }
        }

        return "no pick: " + string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("guard", StringComparison.Ordinal)).Take(8));
    }

    [Fact]
    public void On_the_Ravager_walk_shape_the_guard_offers_a_drain_on_the_fight_back_target_and_picks_the_faster_heal()
    {
        var (rig, guard, watch) = Shape(22746, "Reedshark Ravager");
        using (watch)
        {
            var pick = FirstPick(rig, guard);

            // The drain is offered on the walk's fight-back target (its health the world database's 375). M6 14 tactics: the pick is by the health a landed cast gives her a second (HealPick.ChooseNow), no longer charging Heal
            // Self VI its 70 mana's ceiling at 0.09 s a point: here Heal Self VI's 26.6 a second beats the best drain's 18.1, where the old score had the drain over it.
            Assert.Contains("best drain Drain Health Other II on 0x80006001 h/s=18.", pick, StringComparison.Ordinal);
            Assert.Contains("heal pick: Heal Self VI h/s=26.", pick, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_kind_that_lets_no_drain_through_still_picks_Heal_Self()
    {
        var (rig, guard, watch) = Shape(11540, "Armored Tusker");
        using (watch)
        {
            var pick = FirstPick(rig, guard);
            Assert.DoesNotContain("Drain Health Other", pick, StringComparison.Ordinal);
            Assert.Contains("no target of known health in reach", pick, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// M6 16 Q5 (<c>TODO(engine-health-flip)</c>): the guard reads its drain target's health from the session's one picture. A Ravager she burnt for 370 of its 375 before it became the fight-back's target (an earlier fight or
    /// leg: the tracking published with the target is made new and never heard the line) stays hurt: before, the guard read it at its facts' 375 and its best drain at 18 a second; now it reads the 5 left, and the drain
    /// a near-dead creature can give is far less.
    /// </summary>
    [Fact]
    public void The_guards_drain_pick_reads_a_target_hurt_before_it_was_published_from_the_one_health_picture()
    {
        var (rig, guard, watch) = Shape(22746, "Reedshark Ravager", before: r =>
        {
            _ = Lifestone.Client.Session.Threat.ThreatBoard.For(r.View); // the session's picture hears from an earlier fight
            r.R.H.ServerMessage("You scorch Reedshark Ravager for 370 points with Flame Bolt VI.", (int)FightTally.MagicChatType);
        });
        using (watch)
        {
            var pick = FirstPick(rig, guard);
            var read = Lifestone.Client.Session.Threat.ThreatBoard.For(rig.View).Health.Read(Guid, rig.Now);
            Assert.Equal(370.0, read.Dealt); // the picture holds the earlier 370
            Assert.Contains(" on 0x80006001 h/s=0.", pick, StringComparison.Ordinal); // now: the 5 left (plus what it mended) give her under 1 a second (Drain Health Other I at 0.45 when written)
            Assert.DoesNotContain("best drain Drain Health Other II on 0x80006001 h/s=18.", pick, StringComparison.Ordinal); // before: the facts' 375, Drain Health Other II at 18.1 a second
            Assert.Contains("heal pick: Heal Self VI h/s=26.", pick, StringComparison.Ordinal);
            Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("health-shadow: ", StringComparison.Ordinal)); // the guard's lanes are gone
        }
    }
}
