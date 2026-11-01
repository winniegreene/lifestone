using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 F30, "resume where it stands; a trip goes home by recall; bind after a re-create" (soak rounds 6 and 7): the resumed fight on the ground makes no walk to the arrival; a trip due at the fight's start ends the fight at once (the recall home follows);
/// the build's last step binds at the lifestone; the vitals guard waits for the login and treats YoureTooBusy as a wait; WeenieError 0x3C has its ACE name.
/// </summary>
public sealed class AutopilotF30Tests(ITestOutputHelper output)
{
    private static FightScenario Scenario(FightManaTests.Rig rig, FightOptions fight) =>
        new(rig.Kit, rig.Caster.Table, rig.Buff, fight, null, rig.Log, endSession: false, stages: FightStages.Place | FightStages.Mode);

    private void Dump(FightManaTests.Rig r)
    {
        foreach (var line in r.Log.Lines.Where(l => l.Contains("stage", StringComparison.Ordinal) || l.Contains("resumed", StringComparison.Ordinal) || l.Contains("stop", StringComparison.Ordinal) || l.Contains("guard", StringComparison.Ordinal)).Take(60))
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void Item1_the_autopilots_fight_on_the_ground_is_the_resume_spec_and_its_options_leave_out_the_place()
    {
        Assert.False(JourneyStations.DenFightOptions(AutopilotScenario.FightSpec, new FightOptions(), []).ResumeOnGround);
        var resume = JourneyStations.DenFightOptions(AutopilotScenario.FightResumeSpec, new FightOptions(), []);
        Assert.True(resume.ResumeOnGround);
        Assert.Equal(FightOptions.UnlimitedKills, resume.Kills); // still the open-ended fight
        Assert.True(resume.HoldStance);
    }

    [Fact]
    public void Item1_a_fight_resumed_on_the_ground_fights_from_where_it_stands_with_no_place_stage_and_no_walk_to_the_arrival()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        var scenario = Scenario(rig, rig.Fight with { ResumeOnGround = true });
        for (var i = 0; i < 40 && scenario.Stage != "fight"; i++)
        {
            rig.Frame(scenario, 0.25);
        }

        Dump(rig);
        Assert.Equal("fight", scenario.Stage);
        Assert.Contains(rig.Log.Lines, l => l.Contains("fight: resumed on the ground: no walk to the arrival", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("stage teleporting", StringComparison.Ordinal));
        Assert.Equal(0, rig.R.H.Net.Sends.Count(s => s.Kind == FakeSendKind.MoveToState));
    }

    [Fact]
    public void Item2_a_stop_asked_before_the_first_pick_ends_the_fight_at_once_with_no_place_no_walk_and_no_cast()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        var scenario = Scenario(rig, rig.Fight with { ResumeOnGround = true });
        scenario.RequestStop(FightStopReason.LogisticsTrip);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 40 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario, 0.25);
        }

        Dump(rig);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(rig.Log.Lines, l => l.Contains("asked to stop before its first pick (logistics-trip): no walk, no cast", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("stage teleporting", StringComparison.Ordinal));
        Assert.Empty(rig.Caster.Answered);
        Assert.Equal(FightStopReason.LogisticsTrip, scenario.StopReason);
    }

    [Fact]
    public void Item2_the_trips_way_home_is_always_the_recall_on_a_ground_with_a_home_recall()
    {
        // JourneyTravel.Start: TravelPurpose.Home is a RecallLeg (Lifestone Recall through RecallHome.ForCharacter) unless the mage is already in the home landblock; the logistics station asks for Home with no walking fallback.
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lifestone.Client", "Session", "Scenarios", "Journey", "JourneyTravel.cs"));
        var home = source[source.IndexOf("case TravelPurpose.Home:", StringComparison.Ordinal)..];
        home = home[..home.IndexOf("case TravelPurpose.Ground:", StringComparison.Ordinal)];
        Assert.Contains("new RecallLeg(", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Run", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Item3_the_build_binds_last_for_a_fresh_session_ending_build_with_a_kit_plan_only()
    {
        Assert.False(MageBuildScenario.BindIntent(null, endSession: true, "recreated"));
        Assert.False(MageBuildScenario.BindIntent(null, endSession: false, null));
    }

    private sealed class BindOnly(LifestoneBindLeg leg) : IStation
    {
        public string Name => "lifestone-bind";

        public StationNeeds Needs { get; } = StationNeeds.None;

        public IStationRun Start(JourneyContext context) => leg;
    }

    [Fact]
    public void Item3_the_bind_leg_places_at_the_lifestone_uses_it_then_ties_it_in_the_stand_ins_world()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        var leg = new LifestoneBindLeg(ctx.Dungeon, ctx.Log);
        var (run, status) = t.RunStation(new BindOnly(leg), ctx);
        foreach (var e in t.Events)
        {
            output.WriteLine(e);
        }

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + string.Join("; ", leg.Problems));
        Assert.True(leg.Bound);
        Assert.True(leg.Linked);
        var use = t.Events.IndexOf("use:" + J3bRig.LifestoneGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        var tie = t.Events.FindIndex(e => e.StartsWith("cast:2644@", StringComparison.Ordinal));
        Assert.True(use >= 0 && tie > use, "the Use of the lifestone comes first, the tie after it: " + string.Join(", ", t.Events));
        Assert.Single(t.PlacedCells); // the one place to the lifestone point; the build's own, not the journey seam's
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
    }

    /// <summary>The login over for the rig's player: LoginComplete sent, then the server's unhide (the player is present), as a settled login is.</summary>
    internal static void Settle(FightManaTests.Rig rig)
    {
        rig.R.H.R.Reducer.ApplyLocal(new Lifestone.Client.Session.Recording.RecordingNotice(Lifestone.Client.Session.State.SessionNotices.LoginCompleteSent, 0, null));
        rig.R.H.Unhide(5000);
        rig.R.Tick();
    }

    private static void Announce(FightManaTests.Rig rig) =>
        rig.R.H.R.Reducer.OnPlayerTeleport(rig.R.H.R.Context, new PlayerTeleportMsg(30000));

    [Fact]
    public void Item4_the_guard_casts_nothing_while_the_login_is_settling_and_says_so_once()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        rig.Caster.HealthPerCast = 20;
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        Announce(rig);
        rig.R.Tick();
        Assert.NotEqual(SelfTravel.Present, rig.R.H.State.Self.Travel);
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        for (var i = 0; i < 40; i++)
        {
            rig.R.Tick(0.1);
            Assert.False(guard.Step(rig.View, rig.Now, "known-state"));
        }

        Assert.Equal(0, guard.Casts);
        Assert.Empty(rig.Caster.Answered);
        Assert.Single(rig.Log.Lines, l => l.Contains("the login is still settling", StringComparison.Ordinal));
    }

    [Fact]
    public void Item4_the_guard_takes_YoureTooBusy_as_a_wait_and_tries_again_after_a_second()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        AutopilotF30Tests.Settle(rig);
        rig.Caster.HealthPerCast = 20;
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        var busy = 0;
        rig.Caster.CastHook = spell =>
        {
            if (VitalsMeans.HealSelf.Contains(spell) && busy < 6)
            {
                busy++;
                rig.R.UseDone(0x001D);
                return true; // ACE: YoureTooBusy, nothing else
            }

            return false;
        };
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        var end = rig.Now + 60;
        while (rig.Now < end && !rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)))
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "known-state");
        }

        Dump(rig);
        Assert.True(busy >= 4, "the busy answers were met: " + busy);
        Assert.True(guard.Busy >= 1, "the guard counted a busy end");
        Assert.Contains(rig.Log.Lines, l => l.Contains("WeenieError 0x001D (YoureTooBusy)", StringComparison.Ordinal) && l.Contains("a wait, not a failure", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("did not land (busy", StringComparison.Ordinal));
        Assert.Contains(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell)); // and the heal went through afterwards
    }

    [Fact]
    public void Item4_WeenieError_0x3C_has_its_ACE_name()
    {
        Assert.Equal("ITeleported", WeenieErrorText.NameOf(0x3C));
        Assert.False(WeenieErrorText.TryFormat(0x3C, null, out _, out var text)); // ACE gives it no message text: the number and the name show
        Assert.Equal("WeenieError 0x003C (ITeleported)", text);
        Assert.False(WeenieErrorText.TryFormat(0x7777, null, out _, out var plain));
        Assert.Equal("WeenieError 0x7777", plain);
    }
}
