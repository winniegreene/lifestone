using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F8 (D89): the fight's opener (FightScenario.Opener.cs) over the scripted ACE: a Tusker Slave, whose ground-profile entry names Fire Vulnerability Other, gets the opener once and then bolts; a Tusker Guard, which names none,
/// gets bolts only; a resisted or fizzled opener is pressed again up to the bound (<c>--fight-opener-tries</c>); a target under half its health skips it; the level follows the plan cap and the pack's scarabs. Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint SecondTuskerGuid = 0x80001003;

    private static readonly IReadOnlyList<uint> OpenerIds = [.. ArenaProfile.ExtraNamed("Fire Vulnerability Other")!.Spells.Select(s => s.Id)];

    [Fact]
    public void A_Tusker_Slave_gets_the_opener_once_and_then_bolts_and_the_line_and_the_verdict_say_so()
    {
        var r = new Rig(openers: true);
        var slave = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(slave.Dead);
        Assert.Contains(r.CastSpells[0], OpenerIds);
        Assert.Equal(2, r.CastSpells.Count(id => !OpenerIds.Contains(id))); // then the two bolts that kill it
        Assert.All(r.CastSpells.Skip(1), id => Assert.DoesNotContain(id, OpenerIds));
        Assert.Equal(3, r.CastTargets.Count);
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t));
        Assert.Equal((1, 1, 0, 0), (scenario.OpenersSent, scenario.OpenersLanded, scenario.OpenersResisted, scenario.OpenersSkipped));
        Assert.Equal(1, LinesWith(r, "opener: Fire Vulnerability"));
        Assert.Equal(1, LinesWith(r, " on Tusker Slave 0x80001001: landed"));
        Assert.Contains(" openers=sent:1,landed:1,resisted:0,skipped:0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Tusker_Guard_gets_bolts_only()
    {
        var r = new Rig(openers: true);
        var guard = r.Add(TuskerGuid, TuskerGuard, FloorCell, 30, "Tusker Guard");
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(guard.Dead);
        Assert.All(r.CastSpells, id => Assert.DoesNotContain(id, OpenerIds));
        Assert.Equal(2, r.CastSpells.Count);
        Assert.Equal((0, 0, 0, 0), (scenario.OpenersSent, scenario.OpenersLanded, scenario.OpenersResisted, scenario.OpenersSkipped));
        Assert.Equal(0, LinesWith(r, "opener: "));
        Assert.Contains(" openers=sent:0,landed:0,resisted:0,skipped:0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_resisted_opener_is_pressed_again_up_to_the_bound_then_the_bolts_go_on()
    {
        var r = new Rig(openers: true);
        var slave = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Script(Shot.Resist, Shot.Resist, Shot.Resist); // the default of three casts, all resisted
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(slave.Dead);
        Assert.All(r.CastSpells.Take(3), id => Assert.Contains(id, OpenerIds));
        Assert.All(r.CastSpells.Skip(3), id => Assert.DoesNotContain(id, OpenerIds));
        Assert.Equal(5, r.CastSpells.Count);
        Assert.Equal((3, 0, 1, 0), (scenario.OpenersSent, scenario.OpenersLanded, scenario.OpenersResisted, scenario.OpenersSkipped));
        Assert.Equal(1, LinesWith(r, " on Tusker Slave 0x80001001: resisted after 3"));
    }

    [Fact]
    public void A_resist_then_a_landing_is_two_casts_and_the_bound_follows_the_option()
    {
        var r = new Rig(openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Script(Shot.Resist);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal((2, 1, 0), (scenario.OpenersSent, scenario.OpenersLanded, scenario.OpenersResisted));

        var two = new Rig(fight: new FightOptions { OpenerTries = 2 }, openers: true);
        two.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        two.Script(Shot.Resist, Shot.Resist, Shot.Resist);
        var bounded = two.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, two.Run(bounded));
        Assert.Equal(2, two.CastSpells.Count(id => OpenerIds.Contains(id))); // two casts, not three
        Assert.Equal(1, bounded.OpenersResisted);

        var off = new Rig(fight: FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-opener-tries=0"])), openers: true);
        off.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        Assert.Equal(ScenarioStatus.Succeeded, off.Run(off.Scenario(new FakeWalk())));
        Assert.DoesNotContain(off.CastSpells, id => OpenerIds.Contains(id)); // 0 turns openers off

        Assert.Equal(3, FightOptions.FromArguments(CommandLineArgument.ParseAll([])).OpenerTries);
        foreach (var bad in new[] { "-1", "11", "x" })
        {
            var parsed = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-opener-tries=" + bad]));
            Assert.Equal(FightOptions.DefaultOpenerTries, parsed.OpenerTries);
        }
    }

    [Fact]
    public void A_fizzled_opener_is_pressed_again_and_a_target_already_under_half_its_health_skips_it()
    {
        var r = new Rig(openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Script(Shot.Fizzle);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.All(r.CastSpells.Take(2), id => Assert.Contains(id, OpenerIds)); // the fizzle, then the same level again
        Assert.Equal((2, 1), (scenario.OpenersSent, scenario.OpenersLanded));

        var hurt = new Rig(openers: true);
        var slave = hurt.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        hurt.R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, TuskerGuid, 0.4f));
        var skipped = hurt.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, hurt.Run(skipped));
        Assert.True(slave.Dead);
        Assert.DoesNotContain(hurt.CastSpells, id => OpenerIds.Contains(id));
        // M5 7 F10 (D90): the picker decides by the arithmetic before F8's half-health rule is reached: on 160 health the vulnerability (VII here, the rig's cap) saves less than its cast costs, so it is never chosen (nothing sent,
        // nothing skipped), and the log says once why.
        Assert.Equal((0, 0, 0, 0), (skipped.OpenersSent, skipped.OpenersLanded, skipped.OpenersResisted, skipped.OpenersSkipped));
        Assert.Equal(1, LinesWith(hurt, "pick: Inferno's Gift does not win here"));
    }

    [Fact]
    public void Each_new_Slave_gets_its_own_opener_once()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2 }, openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(SecondTuskerGuid, TuskerSlave, FloorCell, 50);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(2, scenario.OpenersLanded); // two Slaves, one opener each
        Assert.Equal(2, r.CastSpells.Count(id => OpenerIds.Contains(id)));
        Assert.Equal(2, r.Log.Lines.Count(l => l.Contains(" on Tusker Slave 0x", StringComparison.Ordinal) && l.Contains(": landed", StringComparison.Ordinal))); // one landing line a Slave (the cast policy's own lines say "fight opener: try")
        Assert.Equal(TuskerGuid, r.CastTargets[0]);
    }

    [Fact]
    public void The_opener_s_level_follows_the_plan_cap_the_spellbook_and_the_pack_s_scarabs_VI_at_80_and_VII_at_126()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var cap80 = FightScenario.PlanCap(den, 80, 7, out _);
        var cap126 = FightScenario.PlanCap(den, 126, 7, out _);
        Assert.Equal((6, 7), (cap80, cap126));

        var r = new Rig(openers: true);
        var book = r.R.H.State.Self.Spellbook;
        var line = ArenaProfile.ExtraNamed("Fire Vulnerability Other")!;

        // A skill that reaches level VII's power (325) and a pack that can pay for it: VII at 126, VI at 80 (the plan's cap).
        Assert.Equal(5, FightScenario.ChooseOpenerLevel(line, r.Table, book, 450, 0.9, cap80, null, out _));
        Assert.Equal(6, FightScenario.ChooseOpenerLevel(line, r.Table, book, 450, 0.9, cap126, null, out _));

        // A pack without the Platinum Scarab (the VII formula's): VII is skipped by name and VI is cast.
        string? Lacks(SpellInfo info) => info.Id == 2170 ? "Platinum Scarab 8897, which the pack does not hold" : null;
        Assert.Equal(5, FightScenario.ChooseOpenerLevel(line, r.Table, book, 450, 0.9, cap126, Lacks, out _));

        // A skill under VII's chance but over VI's: VI; under every level's: none, named.
        Assert.Equal(5, FightScenario.ChooseOpenerLevel(line, r.Table, book, 330, 0.9, cap126, null, out _));
        Assert.Equal(-1, FightScenario.ChooseOpenerLevel(line, r.Table, book, 1, 0.9999, cap126, null, out var why));
        Assert.Contains("does not reach a chance", why, StringComparison.Ordinal);

        // A spellbook that lacks the levels: none, named.
        var thin = new Rig(known: [27u, 81u, 82u, 83u, 84u, 85u], openers: true);
        Assert.Equal(-1, FightScenario.ChooseOpenerLevel(line, thin.Table, thin.R.H.State.Self.Spellbook, 450, 0.9, cap126, null, out var missing));
        Assert.Contains("not in the spellbook", missing, StringComparison.Ordinal);
    }
}
