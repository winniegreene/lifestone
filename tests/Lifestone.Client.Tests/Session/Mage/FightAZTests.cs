using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AZ (live, 2026-11-06 23:25 to 23:27 UTC, level 19: a refresh in a lull spent her mana from 200 to 3, the lookahead started another pass, she stood in its mana wait in peace mode and died to tuskers without a bolt). A refresh in a fight keeps
/// the data mana reserve and starts no new line below it, and a pass ends at once, by name, when she is attacked or a hostile is in reach: she is back in the magic mode and the fight goes on. Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed class FightAZTests(ITestOutputHelper output)
{
    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("buff refresh", StringComparison.Ordinal) || l.Contains("mana wait", StringComparison.Ordinal) || l.Contains("combat mode", StringComparison.Ordinal) || l.Contains("abort", StringComparison.Ordinal)).Take(60))
        {
            output.WriteLine(line);
        }
    }

    private static FightManaTests.Rig Rig(uint mana, double regen, Func<ProfileLine, double> left)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: 0, knowsDrain: true, knowsRevitalize: false, knowsDance: false) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = regen;
        r.Caster.HealthPerCast = 20;
        r.AddTusker(60); // out of the 20 m reach: a lull
        FightF24Tests.Prime(r, left);
        return r;
    }

    private static FightScenario Armed(FightManaTests.Rig r)
    {
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        return scenario;
    }

    private static List<string> BuffCasts(FightManaTests.Rig r) => [.. r.Caster.Answered.Select(a => ArenaProfile.Lines.FirstOrDefault(l => l.IsBuff && l.Spells.Any(s => s.Id == a.Spell))?.Name).OfType<string>()];

    [Fact]
    public void M6_7_finish_the_fights_pack_peak_counts_the_living_within_the_awake_reach()
    {
        var r = Rig(mana: 200, regen: 0, l => 3600);
        var at = r.R.H.State.Self.Position!.Value;
        void Awake(uint guid, float east, uint flags = HostileReach.AttackableFlag)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + east, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Slave");
            r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = flags, ItemType = HostileReach.CreatureItemType } }, isUpdate: false);
        }

        Awake(0x80000F01, 10);
        Awake(0x80000F02, 20);
        Awake(0x80000F03, 30); // outside the 24 m reach
        Awake(0x80000F04, 5, flags: 0); // not attackable: an NPC
        var scenario = r.Scenario();
        r.Run(scenario, 4, 0.1);

        Assert.Equal(2, HostileReach.CountWithin(r.View, FightScenario.PackPeakReachMetres));
        Assert.Equal(2, scenario.PackPeak);
    }

    [Fact]
    public void Under_the_mana_reserve_a_lull_starts_no_refresh_line()
    {
        var n = 0;
        var r = Rig(mana: 30, regen: 0, l => 40 + (n++ * 7)); // spread 7 s apart (no same-age stagger): some are inside the 60 s lookahead margin, none under the 30 s hard floor
        var scenario = Armed(r);
        r.Run(scenario, 4, 0.1);
        Dump(r);
        Assert.Empty(BuffCasts(r));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: lookahead waits: mana 30 of ", StringComparison.Ordinal) && l.Contains("under the reserve 60 (refresh_mana_reserve)", StringComparison.Ordinal));
        Assert.Single(r.Log.Lines, l => l.Contains("buff refresh: lookahead waits:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refresh_in_a_lull_runs_out_of_mana_she_is_attacked_and_she_fights_in_the_magic_mode()
    {
        var r = Rig(mana: 70, regen: 0, l => 50); // a same-age batch: the stagger puts its first lines under the hard floor, the pass has the mana for a few casts and then waits
        var scenario = Armed(r);
        r.Run(scenario, 120, 0.1, () => r.Log.Lines.Any(l => l.Contains("mana wait:", StringComparison.Ordinal)));
        Dump(r);
        Assert.Contains(r.Log.Lines, l => l.Contains("mana wait:", StringComparison.Ordinal)); // the pass is standing in its wait
        r.Run(scenario, 8, 0.1, () => r.R.H.State.Self.Qualities.CombatMode == CombatModes.NonCombat); // and has gone to peace

        var bolts = r.BoltAnswers.Count();
        r.Caster.ManaRegenPerSecond = 40; // the mana comes back as it does in the game, once she is on her feet
        r.AddTusker(12); // a tusker arrives in reach and hits
        Hit(r);
        r.Run(scenario, 40, 0.1, () => r.BoltAnswers.Count() > bolts);
        Dump(r);

        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: the pass for ", StringComparison.Ordinal) && l.Contains(" is ended: the fight's order comes first: ", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff: pass: aborted: ", StringComparison.Ordinal));
        Assert.Equal(CombatModes.Magic, r.R.H.State.Self.Qualities.CombatMode);
        Assert.True(r.BoltAnswers.Count() > bolts, "she cast a bolt at the tusker after the pass ended");
        Assert.Equal(1, scenario.RefreshAborts);
    }

    /// <summary>M6 7 BD (live, 2026-11-07 03:34:19 to 03:34:49 UTC: thirteen lines of a leave refresh each started and ended on a hit, 30 s with no bolt): a leave pass that is fought ends the leave: no next line starts, and the stop goes ahead.</summary>
    [Fact]
    public void A_leave_refresh_ended_by_an_attack_starts_no_next_line_and_the_stop_goes_ahead()
    {
        var r = Rig(mana: 70, regen: 0, l => 50);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => scenario.Stage == "fight"); // a stop before the first pick ends the fight at once (F30)
        scenario.RequestLeaveRefresh(300, 20, "logistics-trip: the test");
        ((IStoppableFight)scenario).RequestStop(FightStopReason.LogisticsTrip);
        r.Run(scenario, 120, 0.1, () => r.Log.Lines.Any(l => l.Contains("mana wait:", StringComparison.Ordinal)));
        Assert.Contains(r.Log.Lines, l => l.Contains("mana wait:", StringComparison.Ordinal)); // the leave's pass stands in its wait
        r.AddTusker(12);
        Hit(r);
        r.Run(scenario, 40, 0.1, () => !scenario.LeaveRefreshAsked);
        r.Run(scenario, 40, 0.1);
        Dump(r);

        Assert.False(scenario.LeaveRefreshAsked);
        Assert.Equal(1, scenario.RefreshAborts);
        Assert.Single(r.Log.Lines, l => l.Contains("buff refresh: leave: dropped: the fight's order comes first: ", StringComparison.Ordinal));
        Assert.Single(r.Log.Lines, l => l.Contains("buff refresh: the pass for ", StringComparison.Ordinal) && l.Contains(" is ended: ", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 (live ladder37, 2026-11-16 09:19:01 to 09:19:57 UTC): the fight ended an Impenetrability pass (56 s left) before it cast, the pass then closed with nothing cast, and the 300 s futile wait replaced the abort's 30 s retry;
    /// the next hard pass left the line out and it lapsed, its expiry blaming "the pass cast nothing". A pass the fight ends before it casts keeps the abort's retry and reason.
    /// </summary>
    [Fact]
    public void A_pass_the_fight_ends_before_it_casts_is_tried_again_after_the_aborts_retry_not_the_futile_wait()
    {
        var r = Rig(mana: 70, regen: 0, l => 50);
        var scenario = Armed(r);
        r.Run(scenario, 120, 0.1, () => r.Log.Lines.Any(l => l.Contains("mana wait:", StringComparison.Ordinal))); // a hard pass stands in its mana wait
        r.Caster.ManaRegenPerSecond = 40;
        r.AddTusker(12);
        Hit(r);
        r.Run(scenario, 10, 0.1, () => r.Log.Lines.Any(l => l.Contains(" is ended: the fight's order comes first", StringComparison.Ordinal)));
        var ended = r.Log.Lines.Single(l => l.Contains("buff refresh: the pass for ", StringComparison.Ordinal) && l.Contains(" is ended: ", StringComparison.Ordinal));
        var from = ended.IndexOf("the pass for ", StringComparison.Ordinal) + "the pass for ".Length;
        var name = ended[from..ended.IndexOf(" is ended: ", StringComparison.Ordinal)];
        var at = r.Log.Lines.ToList().IndexOf(ended);
        r.Run(scenario, 45, 0.1);
        Dump(r);

        Assert.Contains(r.Log.Lines, l => l.Contains($"buff refresh: done ({name}: 0 casts", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains($"buff refresh: {name}: the pass was ended by the fight before it cast; the line is tried again ", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines.Skip(at + 1), l => l.Contains($"buff refresh: hard: {name} ", StringComparison.Ordinal)); // tried again after 30 s, not 300 s
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains(name, StringComparison.Ordinal) && l.Contains("EXPIRED", StringComparison.Ordinal) && l.Contains("the pass cast nothing", StringComparison.Ordinal));
    }

    private static void Hit(FightManaTests.Rig r) =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 5, 4, false, 0));
}
