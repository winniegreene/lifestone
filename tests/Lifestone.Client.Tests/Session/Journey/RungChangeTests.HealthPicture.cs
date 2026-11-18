using System.Numerics;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Threat;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 Q5 (docs/automation-engine.md section 6, <c>TODO(engine-health-flip)</c>; E2d had it in shadow): the travel leg's fight-back publishes its target's health from the session's one picture. A creature she hurt
/// before it became the target stays hurt in the target line; and a bolt's line is the skirmish's own target's, though a nearer creature of the same name stands by it.
/// </summary>
public sealed partial class RungChangeTests
{
    [Fact]
    public void The_fight_backs_published_target_reads_the_one_health_picture_with_what_she_dealt_it_before()
    {
        var max = Bestiary.Find(11540)!.Vitals.MaxHealth!.Value;
        var (t, context) = RunPastRig(anim: 3.0, seconds: 20);
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            var here = t.R.H.State.Self.Position!.Value;
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
                t.R.Raw(AceWireWriters.ServerMessage("You scorch Armored Tusker for 40 points with Flame Bolt.", (int)FightTally.MagicChatType)); // before the fight-back takes it for its target
                return;
            }

            if (t.R.H.State.Objects.TryGet(Runner, out _))
            {
                t.MoveObject(Runner, here.CellId, here.Origin + new Vector3(2, 0, 0)); // it runs beside her
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead")); // the leg's own fight-back, as before
        var target = Assert.Single(t.Lines("fight-back: target 'Armored Tusker' 0x80001960 "));
        // before: "its health as data: 'Armored Tusker' 0x80001960 health <max> of <max> (... less the 0 she dealt it in 0 line(s) ...)": the tracking made new never heard the 40
        var health = System.Text.RegularExpressions.Regex.Match(target, $@"its health as data: 'Armored Tusker' 0x80001960 health (\d+) of {max} \(the one picture: its maximum {max}, less the 40 she dealt it since, plus [0-9.]+ regenerated at ");
        Assert.True(health.Success, target);
        Assert.InRange(int.Parse(health.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), max - 40, max - 39); // the 40, less the second it mended at its own rate
        Assert.Empty(t.Lines(HealthShadow.Token)); // the travel leg's lanes are gone
    }

    [Fact]
    public void A_bolts_line_is_the_skirmishs_own_target_though_a_nearer_creature_of_its_name_stands_by()
    {
        const uint Near = 0x80001945;
        var (t, ground) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        var picture = ThreatBoard.For(t.Session).Health;
        (double Target, double Near)? dealt = null;
        t.CastAt = (spell, target) =>
        {
            if (target != 0x80001944)
            {
                t.Kill(target);
                return;
            }

            t.Bystander(Near, "Tusker Slave", 1f); // one of the same name walks up beside her, nearer than the target
            t.R.Raw(AceWireWriters.ServerMessage("You scorch Tusker Slave for 30 points with Flame Bolt.", (int)FightTally.MagicChatType));
            dealt = (picture.Read(target, t.Now).Dealt, picture.Read(Near, t.Now).Dealt);
            t.Kill(target);
        };
        t.RunStation(new RungChangeStation(), t.Context(ground));

        Assert.True(dealt is not null, string.Join(" | ", t.R.H.Log.Lines.TakeLast(10)));
        Assert.Equal((30.0, 0.0), dealt!.Value); // before: the nearest of the name took it (0, 30)
        Assert.True(picture.AimedLines >= 1);
    }
}
