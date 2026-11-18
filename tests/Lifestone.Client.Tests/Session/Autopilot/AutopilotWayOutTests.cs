using DatReaderWriter.Enums;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 BE: the way out over the scripted ACE of <see cref="AutopilotRig"/>, the way <see cref="AutopilotRetreatTests"/> proves the retreat: at the line with an attacker on her the ladder's fight stops by the retreat's reason, goes home by the
/// retreat's way (the recall, no keeper's pass first), the keeper at home, the known state, the fight again; it is counted in <c>retreats=</c> and named apart; a second and a third from one ground in one cycle end nothing; and the cases that change
/// nothing: not in the book, no attacker, not the ladder's job. The check before the recall and a recall that fails are named.
/// </summary>
public sealed class AutopilotWayOutTests(ITestOutputHelper output)
{
    private const string Attacker = "Tusker Guard";
    private const string Outskirts = "holtburg-outskirts";
    private const uint HealthCurrent = 2;
    private const uint ManaCurrent = 6;

    private sealed class Hurt(ApWorld w)
    {
        private byte _sequence;

        public void Mana(uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, ManaCurrent, points));

        public void Health(uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, HealthCurrent, points));

        public void Hit() => w.Trip.Vendor.R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, Attacker, 4, 0.1, 1, 4, false, 0));
    }

    /// <summary>The climber on the outskirts at level 5: vitals read (each maximum 100), the recall at 50 mana and Drain Health Other I at 10 in the table.</summary>
    private ApWorld Climber(out Hurt hurt, bool knowsRecall = true, string ground = Outskirts)
    {
        var w = new ApWorld(ground, output, atHome: false)
        {
            Spells = new Dictionary<uint, SpellInfo>
            {
                [RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Lifestone Recall", BaseMana = 50 },
                [FightScenario.EmergencySpellId] = WorldActionRig.Spell(FightScenario.EmergencySpellId, SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Drain Health Other I", BaseMana = 10 },
            },
        };
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
        w.Level(5); // after the description, which replaces the qualities
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells.Where(l => l.SpellId != RecallHome.LifestoneRecallSpell))
        {
            book.Learn(spell.SpellId);
        }

        if (knowsRecall)
        {
            book.Learn(RecallHome.LifestoneRecallSpell);
        }

        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        hurt = new Hurt(w);
        return w;
    }

    /// <summary>The scripted fight, with something that happens as the stop is asked (a cast that was already on its way).</summary>
    private sealed class LastCast(ScriptedFight inner, Action atStop) : IStationRun, IStoppableFight
    {
        public string Name => inner.Name;

        public string Stage => inner.Stage;

        public string? Failure => inner.Failure;

        public double? WhereMaxMetres => inner.WhereMaxMetres;

        public long Unmatched => inner.Unmatched;

        public bool Ok => inner.Ok;

        public int Kills => inner.Kills;

        public FightStopReason? StopReason => inner.StopReason;

        public void RequestStop(FightStopReason reason)
        {
            atStop();
            inner.RequestStop(reason);
        }

        public ScenarioStatus Step(IGameSessionView session, double now) => inner.Step(session, now);

        public string VerdictFields() => inner.VerdictFields();
    }

    private static AutopilotOptions Ladder => new() { Job = AutopilotJob.LevelUp, Minutes = 60 };

    private static bool ForMana(AutopilotTransition a) => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: mana-line at mana ", StringComparison.Ordinal);

    /// <summary>The attacks and the mana while she fights: a hit every 3 s, the mana at <paramref name="mana"/>; at home the bars come back.</summary>
    private static Action<AutopilotScenario> Beat(ApWorld w, Hurt hurt, uint mana, Func<bool>? on = null)
    {
        var next = 0.0;
        return a =>
        {
            if (a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1, StopReason: null } && on?.Invoke() != false && w.Now >= next)
            {
                next = w.Now + 3;
                hurt.Mana(mana);
                hurt.Hit();
            }
            else if (a.State is AutopilotState.Upkeep or AutopilotState.KnownState)
            {
                hurt.Mana(100);
            }
        };
    }

    [Fact]
    public void At_the_line_with_an_attacker_on_her_she_leaves_by_the_retreat_heals_at_home_and_goes_back_out()
    {
        var w = Climber(out var hurt);
        var autopilot = w.Autopilot(Ladder);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var beat = Beat(w, hurt, 60);
        var left = false;
        w.Run(
            autopilot,
            a => a.State == AutopilotState.Fight && left && arrows.Any(t => t.To == AutopilotState.KnownState && t.Why.StartsWith("after the retreat", StringComparison.Ordinal)),
            20000,
            a =>
            {
                left |= arrows.Any(ForMana);
                if (!left)
                {
                    beat(a);
                }
                else
                {
                    hurt.Mana(100);
                }
            });

        var retreat = arrows.First(ForMana);
        Assert.Equal(AutopilotState.Fight, retreat.From);
        Assert.Contains("mana-line at mana 60/100, line=60 (Lifestone Recall 50 + Drain Health Other I 10) (fight)", retreat.Why, StringComparison.Ordinal);
        var at = arrows.IndexOf(retreat);
        Assert.Equal(AutopilotState.Upkeep, arrows[at + 1].To); // the way home first, no keeper's pass
        Assert.StartsWith("retreat: home (", arrows[at + 1].Why, StringComparison.Ordinal);
        Assert.Equal(AutopilotState.KnownState, arrows[at + 2].To);
        Assert.Contains(arrows.Skip(at + 2), a => a.To == AutopilotState.Fight);
        Assert.Contains(RecallHomeStation.StationName, Kinds(w));
        Assert.Contains(w.Lines, l => l.StartsWith("way out: armed: line=60 (Lifestone Recall 50 + Drain Health Other I 10)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("retreat: mana: decide mana=60 line=60 ", StringComparison.Ordinal) && l.EndsWith("-> retreat (mana-line)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("retreat: mana: check before the recall: mana 60 of 50 needed", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("retreat: taken for mana from " + Outskirts, StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("retreat: mana: check after: home (recalled)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (retreat)", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Retreats);
    }

    [Fact]
    public void A_retreat_for_mana_is_counted_and_named_apart_on_the_cycle_line()
    {
        var w = Climber(out var hurt);
        var autopilot = w.Autopilot(Ladder);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var beat = Beat(w, hurt, 55, () => !arrows.Any(ForMana));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && arrows.Any(ForMana) && a.Retreats == 1, 20000, beat);

        autopilot.RequestStop("test");
        w.Run(autopilot);

        var cycle = autopilot.Lines.First(l => l.Contains("retreat-mana:1", StringComparison.Ordinal));
        Assert.Equal(1, Int(cycle, "retreats"));
        Assert.Equal(1, autopilot.Retreats);
    }

    [Fact]
    public void A_second_and_a_third_retreat_for_mana_from_one_ground_in_one_cycle_end_nothing_and_trip_no_guard()
    {
        var w = Climber(out var hurt);
        var autopilot = w.Autopilot(Ladder);
        var beat = Beat(w, hurt, 55);

        var status = w.Run(autopilot, a => a.Retreats >= 3 && a.State == AutopilotState.Fight, 40000, beat);

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(3, autopilot.Retreats);
        Assert.Null(autopilot.StopName);
        Assert.DoesNotContain(w.Lines, l => l.Contains(AutopilotScenario.RetreatAgainPrefix, StringComparison.Ordinal) && !l.Contains("does not count toward", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("autopilot recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_lifestone_recall_in_the_book_she_stands_as_before_and_it_is_said_once()
    {
        var w = Climber(out var hurt, knowsRecall: false);
        var autopilot = w.Autopilot(Ladder);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var beat = Beat(w, hurt, 55);

        w.Run(autopilot, a => a.CurrentRun is IStoppableFight { Kills: >= 5 }, 8000, beat);

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Equal(0, autopilot.Retreats);
        Assert.DoesNotContain(arrows, ForMana);
        Assert.Single(w.Lines, l => l.StartsWith("way out: off: Lifestone Recall: not in the book", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_attacker_the_mana_at_the_line_changes_nothing()
    {
        var w = Climber(out var hurt);
        var autopilot = w.Autopilot(Ladder);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;

        w.Run(
            autopilot,
            a => a.CurrentRun is IStoppableFight { Kills: >= 5 },
            8000,
            a =>
            {
                if (a.State == AutopilotState.Fight)
                {
                    hurt.Mana(55);
                }
            });

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Equal(0, autopilot.Retreats);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("retreat: mana:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_that_is_not_the_ladders_keeps_its_order()
    {
        var w = Climber(out var hurt, ground: Temple);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var beat = Beat(w, hurt, 55);

        w.Run(autopilot, a => a.CurrentRun is IStoppableFight { Kills: >= 5 }, 8000, beat);

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Equal(0, autopilot.Retreats);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("way out: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recall_unpaid_at_the_check_before_it_is_not_cast_and_the_fight_goes_on_by_name()
    {
        var w = Climber(out var hurt);
        var autopilot = w.Autopilot(Ladder);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var beat = Beat(w, hurt, 55, () => !arrows.Any(t => t.Why.StartsWith("retreat: mana:", StringComparison.Ordinal)));
        w.Factory.WrapFight = fight => new LastCast(fight, () =>
        {
            hurt.Mana(10); // a last cast took it, after the stop was asked
            w.Trip.Vendor.R.Tick();
        });

        w.Run(autopilot, _ => arrows.Any(t => t.To == AutopilotState.Fight && t.Why.StartsWith("retreat: mana: the recall is not payable", StringComparison.Ordinal)), 8000, beat);

        Assert.Contains(w.Lines, l => l.StartsWith("retreat: mana: check before the recall: not payable (mana 10 under 50): no recall is cast", StringComparison.Ordinal));
        Assert.Equal(0, autopilot.Retreats);
        Assert.DoesNotContain(RecallHomeStation.StationName, Kinds(w));
    }

    [Fact]
    public void A_recall_that_fails_says_so_and_takes_the_station_failures_recovery()
    {
        var w = Climber(out var hurt);
        w.Factory.FailWith = spec => spec.Name == RecallHomeStation.StationName ? "recall-home: recall:cast: five fizzles" : null;
        var autopilot = w.Autopilot(Ladder);
        var beat = Beat(w, hurt, 55);

        w.Run(autopilot, _ => w.Lines.Any(l => l.StartsWith("autopilot recovery after travel:recall-home", StringComparison.Ordinal)), 12000, beat);

        Assert.Contains(w.Lines, l => l.StartsWith("retreat: mana: the way home did not land (recall-home: recall:cast: five fizzles)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery after travel:recall-home", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Retreats);
    }

    [Fact]
    public void A_recall_refused_for_components_takes_the_way_home_without_it_as_the_run_already_does()
    {
        var w = Climber(out var hurt);
        w.Factory.RefuseRecallComponents = true;
        var autopilot = w.Autopilot(Ladder);
        var beat = Beat(w, hurt, 55, () => autopilot.Retreats == 0);

        w.Run(autopilot, _ => w.Lines.Any(l => l.StartsWith("autopilot: recall-unavailable:components:", StringComparison.Ordinal)), 12000, beat);

        Assert.Contains(w.Lines, l => l.StartsWith("autopilot: recall-unavailable:components:", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Retreats);
    }
}
