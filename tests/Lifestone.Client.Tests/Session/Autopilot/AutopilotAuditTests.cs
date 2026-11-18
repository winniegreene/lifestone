using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7: the start-up state audit (<see cref="StartupAudit"/>) over the scripted ACE of <see cref="AutopilotRig"/>: it runs at every start and after every reconnect, before the first fight; each check logs its finding and its fix, and one summary
/// line ends it. Proved: a start with everything proven passes through to the buff pass and the fight with no extra station; an unproven bind binds at home; an untied portal of a ground no route reaches stops by the named gap (known at the start, or refused by the den
/// travel's recall); short tapers make a trip first and a short carried line stops by the counters' name; a death still to recover runs the death station first; a reconnect audits again.
/// </summary>
public sealed class AutopilotAuditTests(ITestOutputHelper output)
{
    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void At_home_with_everything_proven_the_audit_passes_and_the_buff_pass_then_the_fight_run(string key)
    {
        var w = new ApWorld(key, output) { Prime = c => Proven(c, key) };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        Assert.Equal(["buff", "den-travel", "den-fight", "recall-home"], Kinds(w)); // no station of the audit's own
        Assert.Equal(1, autopilot.Audits);
        Assert.Equal("autopilot audit: place=home bind=proven portal_tie=proven kit=ok corpse=none buffs=buff packs=0/0 worn_restored=0 alive=alive travel=present hostiles=clear windows=none caster=none stance=left buffs_due=0 spirit=unlearned vitals=ok corpses_in_view=0 why=start", autopilot.LastAudit);
        foreach (var check in new[] { "corpse", "place", "bind", "portal_tie", "kit", "buffs" })
        {
            Assert.Single(w.Lines, l => l.StartsWith($"autopilot audit {check}: ", StringComparison.Ordinal));
        }

        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit buffs: no enchantment is up; vitae 0%", StringComparison.Ordinal) && l.EndsWith("fix=the buff pass before the first fight", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den, null)]
    [InlineData(Den, false)]
    [InlineData(Temple, null)]
    public void At_home_with_the_bind_unproven_or_disproven_the_audit_binds_first(string key, bool? tied)
    {
        var w = new ApWorld(key, output) { Prime = c => c.Facts.LifestoneTied = tied };
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Equal(["bind", "buff", "den-travel", "den-fight", "recall-home"], Kinds(w));
        Assert.True(w.Factory.Bound);
        var finding = tied == false ? "disproven: a Lifestone Recall was refused with 0x049E" : "not proven this session";
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: " + finding, StringComparison.Ordinal) && l.Contains($"fix=bind at the {w.Ground.Home.Town} lifestone", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: proven by the bind station this session; fix=none", StringComparison.Ordinal));
        Assert.StartsWith("autopilot audit: place=home bind=bound-now portal_tie=pending ", autopilot.LastAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void In_the_ground_the_bind_is_left_unknown_for_the_first_recall_to_prove(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.DoesNotContain("bind", Kinds(w));
        Assert.StartsWith("autopilot audit: place=ground bind=unknown ", autopilot.LastAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)] // M6 4 t3b: the den's route has no portal and no run, so the gap stays; the routed Temple goes by the rung change (AutopilotRungTravelTests)
    public void A_portal_known_untied_stops_the_run_by_the_named_gap_before_any_station(string key)
    {
        var w = new ApWorld(key, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(key, PortalTie.Untied); } };
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.Equal(StartupAudit.PortalGapStop, autopilot.StopName);
        Assert.Empty(Kinds(w));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit portal_tie: ACE refused a Primary Portal Recall untied (0x04A3)", StringComparison.Ordinal) && l.Contains("fix=gap: TODO(m5-portal-retie-route)", StringComparison.Ordinal));
        Assert.StartsWith("autopilot audit: place=home bind=proven portal_tie=gap ", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.StartsWith($"autopilot done ok=no cycles=0 stop={StartupAudit.PortalGapStop} ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)] // M6 4 t3b: the den's route has no portal and no run, so the gap stays; the routed Temple goes by the rung change (AutopilotRungTravelTests)
    public void A_den_travel_refused_untied_stops_the_run_by_the_named_gap_not_by_recoveries(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.RefusePortalRecall = true;
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.Equal(StartupAudit.PortalGapStop, autopilot.StopName);
        Assert.Equal(["bind", "buff", "den-travel"], Kinds(w));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit portal_tie: the den travel's Primary Portal Recall was refused untied", StringComparison.Ordinal) && l.Contains("TODO(m5-portal-retie-route)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Short_of_tapers_at_the_start_a_trip_runs_first_then_the_buff_and_the_fight(string key)
    {
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.TripUnder / 2 : l.Keep) { Prime = c => Proven(c, key) };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Equal("logistics", Kinds(w)[0]);
        Assert.Equal(["buff", "den-travel", "den-fight"], Kinds(w).SkipWhile(k => k != "buff").Take(3)); // the Tusker Temple's trip ends at a vendor outside its home landblock: the audit's place check recalls home before the buff
        Assert.Equal(JourneyStations.TripByCounters, w.Factory.Made[0].Arguments[JourneyPlans.TripArgument]);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit kit: short: Prismatic Taper ", StringComparison.Ordinal) && l.EndsWith($"fix=a trip to {w.Ground.Home.Town} first (by its counters)", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Audits); // the audit stays due across its trip and finishes once
        Assert.StartsWith("autopilot audit: place=home bind=proven portal_tie=proven kit=ok corpse=none buffs=buff packs=0/0 worn_restored=0 alive=alive travel=present hostiles=clear windows=", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.Matches(" windows=(none|vendor-closed) caster=none stance=left ", autopilot.LastAudit); // P7n: a trip that ends beside its last vendor leaves the window open: the audit's pass after it closes it
        Assert.EndsWith(" why=start", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.True(Int(autopilot.Lines[0], "bought") > 0, autopilot.Lines[0]);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Under_a_tapers_stop_line_the_trip_restocks_and_a_short_carried_line_stops_by_the_counters_name(string key)
    {
        var ground = GroundCatalog.Get(key);
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.StopUnder - 1 : l.Keep) { Prime = c => Proven(c, key) };
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Assert.Equal("logistics", Kinds(w)[0]); // the trip restocks a line its home sells, under its stop line too

        if (ground.Supplies.Stock.FirstOrDefault(l => l.StopUnder > 0 && ground.IsCarried(l.Wcid)) is not { } carried)
        {
            return; // every line with a stop line is sold at this ground's home
        }

        var c = new ApWorld(key, output, held: l => l.Wcid == carried.Wcid ? l.StopUnder - 1 : l.Keep) { Prime = x => Proven(x, key) };
        var stopped = c.Autopilot(new AutopilotOptions());
        Assert.Equal(ScenarioStatus.Failed, c.Run(stopped));
        Assert.Equal("logistics-stop:stop:" + carried.Name.Replace(' ', '_'), stopped.StopName);
        Assert.Empty(Kinds(c));
        Assert.Contains("kit=gap", stopped.LastAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_death_still_to_recover_runs_the_death_station_first(string key)
    {
        var w = new ApWorld(key, output) { Prime = c => Proven(c, key) };
        var autopilot = w.Autopilot(new AutopilotOptions(), seed: new AutopilotSeed(1, 0, PendingDeath: new DeathTally()));
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal("death", Kinds(w)[0]);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit corpse: the death checkpoint holds a death whose corpse is not recovered", StringComparison.Ordinal) && l.Contains("fix=recover", StringComparison.Ordinal));
        Assert.Contains("corpse=recover", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.Totals.Deaths);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void After_a_reconnect_the_audit_runs_again_and_finds_the_session_proofs(string key)
    {
        var w = new ApWorld(key, output);
        var host = new ReconnectHost(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), new MemoryCheckpointStore(), host);
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        Assert.Equal(1, autopilot.Audits);

        w.Drop();
        w.Run(autopilot, a => a.Audits == 2 && a.State == AutopilotState.Fight);

        Assert.Equal(1, host.Reconnects);
        Assert.Equal(1, Kinds(w).Count(k => k == "bind")); // the bind proven at the start holds over the reconnect
        Assert.Single(w.Lines, l => l.StartsWith("autopilot audit begins (reconnect)", StringComparison.Ordinal));
        Assert.StartsWith("autopilot audit: place=ground bind=proven portal_tie=proven ", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.EndsWith(" why=reconnect", autopilot.LastAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void While_the_worn_set_restore_appraises_held_casters_the_audit_waits_with_one_line_and_runs_its_checks_once_P7c(string key)
    {
        // D139 row h: round 13 (2026-10-30 03:01:31-33 UTC) logged the place, bind, portal and kit lines 94 times each while the restore appraised 10 casters: the audit re-ran from the top every frame of the wait. Three held casters
        // with no appraisal: the scripted ACE never answers an Identify, so each one waits out its time (ActionPolicy.IdentifySeconds) and the restore's wait spans dozens of frames. Now the wait is begun once, logged once, and the
        // frames resume at the restore; each check's line is written once.
        var w = new ApWorld(key, output) { Prime = c => Proven(c, key) };
        w.OwnKit();
        for (var i = 0u; i < 3; i++)
        {
            w.Trip.Own("Wand " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 90_000 + i, Lifestone.Client.Session.Mage.KeepAsideRule.CasterType, 100);
        }

        var autopilot = w.Autopilot(new AutopilotOptions());
        var waitFrames = 0;
        w.Run(autopilot, a => a.Audits >= 1, perFrame: a =>
        {
            AnswerWieldsAndModes(w); // P7n: the caster check wields one of them (none held), as ACE answers a wield
            if (a.Audits == 0 && w.Lines.Any(l => l.StartsWith("gear: restore: appraising ", StringComparison.Ordinal)))
            {
                waitFrames++;
            }
        });

        Assert.Equal(1, autopilot.Audits);
        Assert.Contains(w.Lines, l => l.StartsWith("gear: restore: appraising 2 held caster(s)", StringComparison.Ordinal)); // M6 6 MF: the caster check before the bind wielded one of the three, so two are in the packs
        Assert.True(waitFrames >= 20, $"the restore's appraisals spanned only {waitFrames} frames");
        foreach (var check in new[] { "corpse", "place", "bind", "portal_tie", "kit", "packs", "worn", "buffs" })
        {
            Assert.Single(w.Lines, l => l.StartsWith($"autopilot audit {check}: ", StringComparison.Ordinal));
        }

        Assert.Single(w.Lines, l => l.StartsWith("autopilot audit waits: worn: ", StringComparison.Ordinal));

        Assert.StartsWith("autopilot audit: place=home bind=proven portal_tie=proven kit=ok ", autopilot.LastAudit, StringComparison.Ordinal);
    }

    [Fact]
    public void An_audits_wait_is_begun_once_and_ended_when_its_step_is_in_P7c()
    {
        var audit = new StartupAudit("start", 0);
        Assert.Null(audit.WaitingOn);
        Assert.True(audit.BeginWait(StartupAudit.WaitWorn, 1));
        for (var frame = 0; frame < 50; frame++)
        {
            Assert.False(audit.BeginWait(StartupAudit.WaitWorn, 1 + (frame * 0.016)));
        }

        Assert.Equal(StartupAudit.WaitWorn, audit.WaitingOn);
        Assert.Equal(1, audit.WaitStartedAt);
        Assert.Equal(1, audit.Waits);
        audit.EndWait();
        Assert.Null(audit.WaitingOn);
        Assert.Equal("autopilot audit waits: worn: x", StartupAudit.WaitLine(StartupAudit.WaitWorn, "x"));
    }

    /// <summary>The host's reconnect double (as the resume tests' own): the reset, then the login back into the world after a few steps.</summary>
    private sealed class ReconnectHost(ApWorld w) : IAutopilotSession
    {
        private int _steps;

        public int Reconnects { get; private set; }

        public string? LoginFailure => null;

        public void Reconnect()
        {
            Reconnects++;
            _steps = 0;
            w.LeaveWorld();
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (++_steps < 4)
            {
                return ScenarioStatus.Running;
            }

            w.Trip.Vendor.H.Phase(SessionPhase.Idle, SessionPhase.InWorld);
            w.Relogin();
            return ScenarioStatus.Succeeded;
        }
    }
}
