using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The hero hook (M4 P5b J7): the hero plans' orders pinned, the showpiece events over scripted counters, the burst names and cadence over the frame observer (no Godot), the options, the old legs' frame names unchanged and the melee window triggers.
/// </summary>
public sealed class J7HeroShotsTests
{
    // ------------------------------------------------------------------------------------------------ the hero plans

    [Fact]
    public void The_hero_plans_are_three_journeys_on_their_own_accounts_and_the_gate_still_has_three_plans()
    {
        Assert.Equal(["mage", "melee", "archer"], JourneyPlans.All.Select(p => p.Name));
        Assert.Equal(["hero-archer", "hero-brawler", "hero-mage"], JourneyPlans.Hero.Select(p => p.Name));
        Assert.Equal(["mage-corpses"], JourneyPlans.Tools.Select(p => p.Name)); // M5 7 lead: the one-off clean-up
        Assert.Same(JourneyPlans.MageCorpses, JourneyPlans.ForName("mage-corpses"));
        Assert.Equal(["buff", "den-travel", "corpses", "recall-home"], JourneyPlans.MageCorpses.Stations.Select(s => s.Name));
        Assert.True(JourneyPlans.MageCorpses.Stations[2].Arguments.ContainsKey(JourneyPlans.NoItemWaitArgument));
        Assert.Equal(["test-archer", "test-melee", "test"], JourneyPlans.Hero.Select(p => p.Account));
        Assert.Equal(["Holtburg Archer", "Holtburg Brawler", "BSD Mage"], JourneyPlans.Hero.Select(p => p.Character));
        Assert.Equal([StationWhere.Forest, StationWhere.Belt, StationWhere.Den], JourneyPlans.Hero.Select(p => p.Ground));
        Assert.All(JourneyPlans.Hero, p => Assert.Equal(1, p.TeleportBudget));
        Assert.Same(JourneyPlans.HeroMage, JourneyPlans.ForName("HERO-MAGE"));
        Assert.Null(JourneyPlans.ForName("hero"));
    }

    [Fact]
    public void The_hero_plans_stations_are_pinned_in_order()
    {
        // The M4 hero video (the owner's re-cut): the lower-right bar filled first, the Brawler's store, the mage's den death (its re-buff raises the stats on screen).
        Assert.Equal(["build-verify", "hotbar", "buff", "bind", "hunt", "recall-home"], JourneyPlans.HeroArcher.Stations.Select(s => s.Name));
        Assert.Equal(["build-verify", "hotbar", "buff", "bind", "hunt", "recall-home", "vendor"], JourneyPlans.HeroBrawler.Stations.Select(s => s.Name));
        Assert.Equal(["build-verify", "hotbar", "gear", "buff", "bind", "den-travel", "corpses", "den-fight", "death", "recall-home"], JourneyPlans.HeroMage.Stations.Select(s => s.Name));
        Assert.All(JourneyPlans.All.SelectMany(p => p.Stations), s => Assert.NotEqual(StationKinds.Hotbar, s.Kind)); // the gate's plans keep their stations
        Assert.Equal(JourneyPlans.HeroMage.Stations.Select(s => s.Name), JourneyPlans.HeroMage.Stations.Select(s => s.Kind));
        Assert.All(JourneyPlans.Hero.SelectMany(p => p.Stations), s => Assert.Contains(s.Kind, StationKinds.All));
        Assert.Equal(StationWhere.Forest, JourneyPlans.HeroArcher.Stations.Single(s => s.Name == "hunt").NeedsOverride!.Where);
        Assert.Equal(StationWhere.Belt, JourneyPlans.HeroBrawler.Stations.Single(s => s.Name == "hunt").NeedsOverride!.Where);
    }

    [Fact]
    public void The_hero_mage_fights_with_Cassius_Ring_of_Fire_and_its_build_teaches_it_M4_P5b_J7()
    {
        var fight = JourneyPlans.HeroMage.Stations.Single(s => s.Name == "den-fight");
        Assert.Equal("1785", fight.Arguments[JourneyPlans.SpellArgument]);
        Assert.Equal("3", fight.Arguments[JourneyPlans.KillsArgument]);
        Assert.Equal(1785u, ArenaProfile.RingOfFireId);
        Assert.Contains(1785u, ArenaProfile.SpellsToLearn);
        Assert.Contains(1785u, ArenaProfile.FightExtras);
        Assert.Contains(1785u, GearKit.KitFightSpells);
        var line = ArenaProfile.ExtraNamed("Cassius' Ring of Fire")!;
        Assert.Equal((1785u, 200u), (line.Spells.Single().Id, line.Spells.Single().Power));
        Assert.DoesNotContain(line, ArenaProfile.Lines); // no buff line, no war loop's choice
        Assert.Contains("spell=1785", new FightOptions { SpellId = 1785 }.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("spell=", new FightOptions().ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the options

    [Fact]
    public void Journey_shots_parse_with_their_burst_and_are_refused_outside_the_journey_check()
    {
        var off = PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--play-screenshot=cache/x/hero.png"]);
        Assert.Empty(off.Errors);
        Assert.Equal("hero-mage", off.Journey);
        Assert.Equal(JourneyShots.Off, off.JourneyShots);

        var on = PlayOptions.Parse(["--login-check=journey", "--journey=hero-archer", "--account=test-archer", "--journey-shots"]);
        Assert.Empty(on.Errors);
        Assert.True(on.JourneyShots.Enabled);
        Assert.Equal((JourneyShots.DefaultBurstFrames, 0.2), (on.JourneyShots.BurstFrames, on.JourneyShots.StepSeconds));

        var custom = PlayOptions.Parse(["--login-check=journey", "--journey=hero-brawler", "--account=test-melee", "--journey-shots", "--journey-shot-frames=4", "--journey-shot-step=0.5"]);
        Assert.Empty(custom.Errors);
        Assert.Equal((4, 0.5), (custom.JourneyShots.BurstFrames, custom.JourneyShots.StepSeconds));

        Assert.NotEmpty(PlayOptions.Parse(["--login-check=hunt", "--journey-shots"]).Errors);
        Assert.NotEmpty(PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--journey-shot-frames=4"]).Errors);
        Assert.NotEmpty(PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--journey-shots", "--journey-shot-frames=0"]).Errors);
    }

    [Fact]
    public void Journey_film_parses_on_the_journey_check_alone_and_saves_no_frame_M4_hero_video()
    {
        var film = PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--journey-film"]);
        Assert.Empty(film.Errors);
        Assert.True(film.JourneyFilm);
        Assert.Equal(JourneyShots.Off, film.JourneyShots);
        Assert.Null(film.BuffShotPath);
        Assert.False(PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage"]).JourneyFilm);

        // A saved frame stalls the filmed run (about 0.5 s at 1080p), so the film takes none.
        Assert.Contains("--journey-shots", PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--journey-film", "--journey-shots"]).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("--play-screenshot", PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--journey-film", "--play-screenshot=cache/x/hero.png"]).Errors.Single(), StringComparison.Ordinal);
        Assert.NotEmpty(PlayOptions.Parse(["--login-check=hunt", "--journey-film"]).Errors);
        Assert.True(OptionCatalog.Registry.IsKnown("--journey-film"));
        Assert.True(OptionCatalog.Registry.AppliesTo("--journey-film", OptionScenes.Main));
    }

    [Fact]
    public void The_hero_journeys_keep_the_account_rule_and_the_options_are_registered()
    {
        Assert.Contains("hero-mage", PlayOptions.Parse(["--login-check=journey", "--journey=hero-mage", "--account=test-melee"]).Errors.Single(), StringComparison.Ordinal);
        Assert.Empty(PlayOptions.Parse(["--login-check=journey", "--journey=hero-brawler", "--account=test-melee"]).Errors);
        foreach (var option in new[] { "--journey-shots", "--journey-shot-frames", "--journey-shot-step" })
        {
            Assert.True(OptionCatalog.Registry.IsKnown(option), option);
            Assert.True(OptionCatalog.Registry.AppliesTo(option, OptionScenes.Main), option);
        }
    }

    // ------------------------------------------------------------------------------------------------ the showpiece events

    [Fact]
    public void The_watch_raises_the_showpiece_events_from_scripted_counters()
    {
        var watch = new ShowpieceWatch();
        var events = new List<string>();

        void Feed(ShowpieceInput input) => events.AddRange(watch.Observe(in input).Select(e => input.Station + ":" + e));

        Feed(new("hunt", 0, 0, 0, 0, false, false));
        Feed(new("hunt", 0, 1, 0, 0, false, false)); // the Archer looses an arrow
        Feed(new("hunt", 0, 1, 0, 0, false, false)); // no new one
        Feed(new("hunt", 1, 1, 0, 0, false, false)); // the Brawler swings
        Feed(new("den-travel", 0, 0, 0, 0, false, false)); // a new station: its counters are the bases
        Feed(new("den-travel", 0, 0, 0, 0, true, false)); // the cast window opens
        Feed(new("den-travel", 0, 0, 0, 0, true, true)); // the recall lands in the den
        Feed(new("den-fight", 0, 0, 2, 1785, false, true));
        Feed(new("den-fight", 0, 0, 3, 1785, false, true)); // a cast of the ring
        Feed(new("den-fight", 0, 0, 4, 85, false, true)); // a Flame Bolt: no showpiece
        Feed(new(null, 0, 0, 0, 0, false, false));

        Assert.Equal(["hunt:frost-arrow", "hunt:mace-hit", "den-travel:portal-recall", "den-travel:portal-arrival", "den-fight:ring-of-fire"], events);
        Assert.Equal(["frost-arrow", "mace-hit", "portal-recall", "portal-arrival", "ring-of-fire"], ShowpieceEvents.All);
    }

    [Fact]
    public void The_watch_raises_a_run_in_any_station_once_per_counted_run_and_not_between_stations()
    {
        var watch = new ShowpieceWatch();
        var events = new List<string>();

        void Feed(ShowpieceInput input) => events.AddRange(watch.Observe(in input).Select(e => input.Station + ":" + e));

        Feed(new("bind", 0, 0, 0, 0, false, false, Runs: 2)); // a new station: the count so far is the base
        Feed(new("bind", 0, 0, 0, 0, false, false, Runs: 3)); // the walk to the lifestone has gone its first metres
        Feed(new("bind", 0, 0, 0, 0, false, false, Runs: 3));
        Feed(new("hunt", 0, 0, 0, 0, false, false, Runs: 3));
        Feed(new("hunt", 0, 0, 0, 0, false, false, Runs: 4)); // a seek
        Feed(new(null, 0, 0, 0, 0, false, false, Runs: 5));
        Feed(new(null, 0, 0, 0, 0, false, false, Runs: 6)); // between stations: no burst

        Assert.Equal(["bind:run", "hunt:run"], events);
        Assert.DoesNotContain(ShowpieceEvents.Run, ShowpieceEvents.All); // not a hero showpiece
    }

    // ------------------------------------------------------------------------------------------------ the bursts

    [Fact]
    public void A_burst_is_named_by_station_event_and_number_and_saved_one_frame_a_step_over_the_hosts_clock()
    {
        var observer = new JourneyFrameObserver("cache/x/hero.png", new JourneyShots(true, BurstFrames: 3, StepSeconds: 0.5, EventGapSeconds: 4), NullLogSink.Instance);
        observer.StationStarting("hunt", 0);
        observer.StationEvent("hunt", ShowpieceEvents.FrostArrow, 10);
        Assert.Equal(0, observer.Pending);
        var names = new List<string>();
        var times = new List<double>();
        for (var step = 0; step <= 20; step++)
        {
            var t = 10.0 + (step * 0.1);
            observer.Tick(t);
            while (observer.TryDequeue(out var request))
            {
                names.Add(request.Name);
                times.Add(Math.Round(t, 1));
                Assert.Equal(JourneyFrameKind.Event, request.Kind);
                Assert.Equal($"cache/x/hero-{request.Name}.png", request.Path);
            }
        }

        Assert.Equal(["hunt-frost-arrow-01", "hunt-frost-arrow-02", "hunt-frost-arrow-03"], names);
        Assert.Equal([10.0, 10.5, 11.0], times);
        Assert.Equal(0, observer.BurstsOpen);
        Assert.Equal(names, observer.Requested);
    }

    [Fact]
    public void A_burst_of_one_keeps_the_plain_name_and_an_event_inside_its_gap_or_over_the_cap_is_not_taken()
    {
        var single = new JourneyFrameObserver("cache/x/hero.png", new JourneyShots(true, BurstFrames: 1), NullLogSink.Instance);
        single.StationStarting("den-fight", 0);
        single.StationEvent("den-fight", ShowpieceEvents.RingOfFire, 1);
        Assert.Equal(["den-fight-ring-of-fire"], single.Requested);

        var gapped = new JourneyFrameObserver("cache/x/hero.png", new JourneyShots(true, MaxPerStation: 2, BurstFrames: 2, StepSeconds: 0.1, EventGapSeconds: 5), NullLogSink.Instance);
        gapped.StationStarting("hunt", 0);
        var shotAt = new[] { 1.0, 3.0, 7.0, 13.0 }; // the second is inside the gap, the fourth is over the cap of two bursts
        for (var step = 0; step < 600; step++)
        {
            var t = step * 0.05;
            gapped.Tick(t);
            foreach (var at in shotAt.Where(a => Math.Abs(a - t) < 0.025))
            {
                gapped.StationEvent("hunt", ShowpieceEvents.FrostArrow, at);
            }
        }

        Assert.Equal(["hunt-frost-arrow-01", "hunt-frost-arrow-02", "hunt-frost-arrow-01", "hunt-frost-arrow-02"], gapped.Requested);

        var noFrames = new JourneyFrameObserver(null, new JourneyShots(true, BurstFrames: 4), NullLogSink.Instance);
        noFrames.StationStarting("hunt", 0);
        noFrames.StationEvent("hunt", ShowpieceEvents.FrostArrow, 1);
        noFrames.Tick(2);
        Assert.Empty(noFrames.Requested);
    }

    // ------------------------------------------------------------------------------------------------ the old legs' frame names

    [Fact]
    public void The_old_legs_frame_names_are_unchanged_and_the_station_slots_name_them()
    {
        Assert.Equal(["c1-combat", "c1-powerbar", "meter", "corpse", "heal", "potion", "vendor"], HuntUiShotPlan.Names);
        Assert.Equal(["bolt", "hit", "kill", "viewer", "first"], FightShotPlan.Names);
        Assert.Equal(["hit", "dead", "corpse", "lifestone", "corpse-open"], DeathShotPlan.Names);
        Assert.Equal("cache/x/shot-c1-combat.png", HuntUiShotPlan.ShotPath("cache/x/shot.png", 0).Replace(Path.DirectorySeparatorChar, (char)47));
        Assert.Equal("cache/x/shot-kill.png", FightShotPlan.ShotPath("cache/x/shot.png", 2).Replace(Path.DirectorySeparatorChar, '/'));
        Assert.Equal("cache/x/shot-corpse.png", DeathShotPlan.ShotPath("cache/x/shot.png", 2).Replace(Path.DirectorySeparatorChar, '/'));
        Assert.Equal("cache/x/shot-hunt-c1-combat.png", JourneyFrameNames.PathFor("cache/x/shot.png", JourneyFrameNames.InStation("hunt", "c1-combat")));
        Assert.Equal(HuntUiShotPlan.Names, JourneyFrameNames.SlotsOf("hunt"));
        Assert.Equal(["bolt", "hit", "kill"], JourneyFrameNames.SlotsOf("den-fight"));
        Assert.Equal(DeathShotPlan.Names, JourneyFrameNames.SlotsOf("death"));
        Assert.Equal(["mid-cast"], JourneyFrameNames.SlotsOf("buff"));
        Assert.Equal("buff-mid-cast", JourneyFrameNames.InStation("buff", JourneyFrameNames.BuffMidCast));
    }

    [Fact]
    public void The_melee_triggers_are_the_hunt_legs_three_conditions()
    {
        const float none = float.NaN;
        Assert.Equal(HuntMeleeSlots.None, HuntMeleeShotTriggers.Evaluate(false, true, 1, true, 0.6f, 1f, 0.5f, 0.9f));
        Assert.Equal(HuntMeleeSlots.Combat | HuntMeleeSlots.PowerBar, HuntMeleeShotTriggers.Evaluate(true, true, 1, true, 0.6f, 1f, null, none));
        Assert.Equal(HuntMeleeSlots.Combat, HuntMeleeShotTriggers.Evaluate(true, true, 1, true, 0.1f, 1f, null, none)); // not yet mid-charge
        Assert.Equal(HuntMeleeSlots.None, HuntMeleeShotTriggers.Evaluate(true, true, 1, false, 0.6f, 1f, null, none)); // not in Melee mode
        Assert.Equal(HuntMeleeSlots.Meter, HuntMeleeShotTriggers.Evaluate(true, false, 1, true, none, 1f, 0.5f, 0.9f)); // lowered since the selection
        Assert.Equal(HuntMeleeSlots.None, HuntMeleeShotTriggers.Evaluate(true, false, 1, true, none, 1f, 0.9f, 0.9f));
    }
}
