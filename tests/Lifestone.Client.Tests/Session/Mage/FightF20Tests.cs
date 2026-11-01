using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F20 ("Never drain a target by a stale health"): the live health model per creature, the freshness and yield gates, and the picker over them. The rehearsal's defect: seven Drain Health Other VI in a row on a Slave whose
/// 43 % was never re-read while ACE answered "You gain" 51, 4, 3, 2, 1, 1, 0. Engine-free; nothing here needs a server.
/// </summary>
public sealed class FightF20Tests
{
    private const uint Slave = 0x80000DE7;

    private static FightSnapshot SlaveSnapshot(double fraction, string? source, double age, string? off)
    {
        var s = new FightSnapshot
        {
            Health = 60,
            HealthMax = 300,
            Stamina = 300,
            StaminaMax = 300,
            Mana = 300,
            ManaMax = 300,
            WarSkill = 361,
            LifeSkill = 351,
            CreatureSkill = 351,
            ManaConversion = 258,
            MeleeDefense = 100,
            DebuffLevel = 6,
            DrainVI = true,
            TargetGuid = Slave,
            TargetName = "Tusker Slave",
            TargetMaxHealth = 400,
            TargetMagicDefense = 200,
            Distance = 1.6,
            LineClear = true,
            BoltName = "Flame Bolt VI",
            BoltLevel = 6,
            BoltMeanDamage = 146.5,
            BoltCastSeconds = 2.3 + 0.4 + (10 / 15.0),
            BoltBaseMana = 30,
            BoltPower = 250,
            DebuffCastSeconds = 2.7,
        };
        (s.TargetHealthFraction, s.TargetHealthSource, s.TargetHealthAge, s.DrainOffWhy) = (fraction, source, age, off);
        return s;
    }

    private static FightPicker Picker(Recording log)
    {
        var p = new FightPicker(log, "fight") { LogEveryDecision = true };
        p.Rates.NoteIncoming(0, 0);
        p.Rates.NoteIncoming(1000, 25_000);
        p.Rates.SeedHeal(0.15);
        return p;
    }

    private static HealthSource PushedSource(TargetHealthModel m, float fraction, double clock)
    {
        m.Updated(Slave, fraction, clock);
        m.TryGet(Slave, 400, out _, out var src);
        return src;
    }

    [Fact]
    public void The_model_follows_pushes_then_what_our_casts_take_and_never_reads_unknown_as_full()
    {
        var m = new TargetHealthModel();
        Assert.False(m.TryGet(Slave, 400, out _, out var none));
        Assert.Equal(HealthSource.Unknown, none);
        Assert.False(m.Fresh(Slave, 1));

        m.Asked(Slave, 1.0);
        Assert.True(m.Waiting(Slave, 1.5));
        m.Updated(Slave, 0.43f, 1.2); // the answer
        Assert.True(m.TryGet(Slave, 400, out var f, out var src));
        Assert.Equal((0.43f, HealthSource.Queried), (f, src));
        Assert.True(m.Fresh(Slave, 2.0));
        Assert.Equal(HealthSource.Pushed, PushedSource(m, 0.40f, 1.5)); // a later push, with no ask pending

        m.Took(Slave, 68, 400, 3.0, since: 2.5); // a drain's source change: 160 less 68
        m.TryGet(Slave, 400, out f, out src);
        Assert.Equal(HealthSource.Modelled, src);
        Assert.Equal(0.40f - (68f / 400), f, 3);
        Assert.True(m.Fresh(Slave, 4.0)); // updated by our own last cast
        Assert.False(m.Fresh(Slave, 6.0)); // a modelled value two seconds on: ask again

        m.Updated(Slave, 0.20f, 6.1);
        m.Took(Slave, 68, 400, 6.2, since: 6.0); // the push after the cast began already holds it: never taken twice
        m.TryGet(Slave, 400, out f, out _);
        Assert.Equal(0.20f, f);
        Assert.True(m.Fresh(Slave, 9.0)); // the selected target's pushed value stays right between its pushes (ACE pushes every change)
        Assert.False(m.Fresh(Slave, 20.0));

        var other = new TargetHealthModel();
        other.Took(0x80000001, 100, 400, 1.0); // never asked, but hurt by us: estimated from the profile's health
        Assert.True(other.TryGet(0x80000001, 400, out f, out src));
        Assert.Equal((0.75f, HealthSource.Modelled), (f, src));
    }

    [Fact]
    public void A_drain_that_gave_under_20_or_half_of_the_expected_sets_drains_off_until_a_fresher_health_says_otherwise()
    {
        var m = new TargetHealthModel();
        m.Updated(Slave, 0.43f, 1.0);
        Assert.False(m.NoteYield(Slave, 51, 51, missingBefore: 240)); // as expected
        Assert.False(m.DrainOff(Slave, out _));

        Assert.True(m.NoteYield(Slave, 4, 31, missingBefore: 69));
        Assert.True(m.DrainOff(Slave, out var why));
        Assert.Equal($"drain off on 0x{Slave:X8}: last gave 4", why);

        m.Updated(Slave, 0.18f, 3.0); // a push that confirms the low health does not lift it
        Assert.True(m.DrainOff(Slave, out _));
        m.Updated(Slave, 0.60f, 9.0); // it healed: a fresher health that says otherwise
        Assert.False(m.DrainOff(Slave, out _));

        Assert.False(new TargetHealthModel().NoteYield(Slave, 12, 12, missingBefore: 12)); // the mage was nearly full: 12 is all there was to give
        Assert.True(new TargetHealthModel().NoteYield(Slave, 30, 90, missingBefore: 200)); // over 20 but under half of the expectation
    }

    /// <summary>Runs nine decisions over the model with ACE's rehearsal yields; <paramref name="regenerates"/> pushes the 43 % back after each drain (the model was wrong the other way, as in the rehearsal where the real health fell faster than the stale 43 %).</summary>
    private static (int Drains, List<FightMoveKind> Picks, List<string> Decides) Rehearsal(bool regenerates)
    {
        var log = new Recording();
        var p = Picker(log);
        var m = new TargetHealthModel();
        m.Asked(Slave, 0);
        m.Updated(Slave, 0.43f, 0.1);
        var gave = new double[] { 51, 4, 3, 2, 1, 1, 0 }; // ACE's answers in the rehearsal
        var drains = 0;
        var picks = new List<FightMoveKind>();
        for (var i = 0; i < 9; i++)
        {
            var clock = 1.0 + (3 * i);
            m.TryGet(Slave, 400, out var fraction, out var source);
            var off = m.DrainOff(Slave, out var why) ? why : null;
            var s = SlaveSnapshot(fraction, TargetHealthModel.Name(source), m.AgeOf(Slave, clock), off);
            var pick = p.Decide(in s, clock);
            picks.Add(pick.Kind);
            if (pick.Kind != FightMoveKind.DrainHealth)
            {
                continue;
            }

            var gain = gave[drains++];
            m.Took(Slave, gain / 0.75, 400, clock + 2.7); // the drain's source change: the gain over (1 - loss)
            m.NoteYield(Slave, gain, 0.75 * Math.Min(150, fraction * 400 * 0.4), missingBefore: 240);
            if (regenerates)
            {
                m.Updated(Slave, 0.43f, clock + 2.8);
            }
        }

        return (drains, picks, [.. log.Lines.Where(l => l.StartsWith("decide: ", StringComparison.Ordinal))]);
    }

    [Fact]
    public void Seven_drains_in_a_row_do_not_happen_the_decide_line_follows_each_drain_and_the_picker_bolts_once_the_target_is_low()
    {
        var (drains, picks, decides) = Rehearsal(regenerates: false);

        Assert.Equal(1, drains); // the first drain takes the 43 % to 26 %, where a bolt beats the next one
        Assert.Equal(FightMoveKind.DrainHealth, picks[0]);
        Assert.All(picks.Skip(1), k => Assert.NotEqual(FightMoveKind.DrainHealth, k));
        Assert.True(decides[0].Contains("(health 43 % queried", StringComparison.Ordinal), decides[0]);
        Assert.True(decides[1].Contains("(health 26 % modelled", StringComparison.Ordinal), decides[1]); // the health follows the drain, never the stale 43 %
        Assert.DoesNotContain(decides.Skip(1), l => l.Contains("(health 43 %", StringComparison.Ordinal));
    }

    [Fact]
    public void After_the_first_low_yield_the_picker_picks_a_bolt_whatever_the_model_says()
    {
        var (drains, picks, decides) = Rehearsal(regenerates: true); // the model keeps saying 43 %: only the measured yield can stop the drains

        Assert.Equal(2, drains); // 51, then 4: the second gave under 20 and under half of the 41 expected
        Assert.All(picks.Skip(2), k => Assert.Equal(FightMoveKind.Bolt, k));
        Assert.Contains(decides, l => l.Contains($"drain off on 0x{Slave:X8}: last gave 4", StringComparison.Ordinal));
    }

    [Fact]
    public void A_drain_set_off_or_an_unknown_health_is_never_scored_as_castable()
    {
        var log = new Recording();
        var p = Picker(log);
        var ok = SlaveSnapshot(0.43, "pushed", 0.2, null);
        Assert.Equal(FightMoveKind.DrainHealth, p.Decide(in ok, 1).Kind);

        var off = SlaveSnapshot(0.43, "pushed", 0.2, $"drain off on 0x{Slave:X8}: last gave 4");
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in off, 2).Kind);

        var unknown = SlaveSnapshot(1.0, null, double.PositiveInfinity, null);
        unknown.TargetHealthUnknown = true;
        Assert.Equal(FightMoveKind.Bolt, p.Decide(in unknown, 3).Kind);
        Assert.Contains(log.Lines, l => l.Contains("drain not allowed: the health is unknown", StringComparison.Ordinal));
    }

    private sealed class Recording : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}
