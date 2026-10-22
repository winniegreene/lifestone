using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A stand-in session that stamps every answer with the session-clock time it arrived at (ticks of 10 ms), for the cast sequence's order and timing (M3 2.1 s2a). <see cref="Begin"/> starts a recording;
/// <see cref="Timeline"/> then holds the stand-in's own answer tags (<c>StandInWorld.AnswerLog</c>: 02BB the words, F74C a motion, F755 a script, 02C2, 028A, 02E9 a vital, F7E0 a line, 01C7 UseDone) with their times.
/// </summary>
internal sealed class CastRig : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private int _seen;

    public CastRig(StandInVisit visit, IEnumerable<SpellInfo>? spells = null, Lifestone.Data.Dat.DatSet? dats = null)
    {
        Log = new RecordingLogSink();
        Session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit);
            },
            _time,
            Log);
        if (spells is not null)
        {
            var byId = spells.ToDictionary(s => s.Id);
            World.SpellLookup = id => byId.GetValueOrDefault(id);
            Session.ActionApi.SpellLookup = World.SpellLookup;
        }

        if (dats is not null)
        {
            Session.UseSpellsFrom(dats);
            Session.SpellTableLoaded.Wait();
        }

        Session.Events.Subscribe((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf)
            {
                Motions.Add((Session.Clock.Now, e.Motion));
            }
        });
        Session.Events.Subscribe((in ObjectScriptPlayed e) => Scripts.Add((Session.Clock.Now, e)));
        Run(2.0);
    }

    public RecordingLogSink Log { get; }

    public GameSession Session { get; }

    public StandInWorld World => Session.Reducer.LocalResponder!.World!;

    public PlayerQualities Q => Session.State.Self.Qualities;

    /// <summary>The stand-in's answers since <see cref="Begin"/>, with the time each arrived.</summary>
    public List<(double At, string Tag)> Timeline { get; } = [];

    /// <summary>The player's own motions since <see cref="Begin"/>, with their times.</summary>
    public List<(double At, Lifestone.Net.Types.ServerMotionState Motion)> Motions { get; } = [];

    /// <summary>The scripts played on any object since <see cref="Begin"/>, with their times.</summary>
    public List<(double At, ObjectScriptPlayed Script)> Scripts { get; } = [];

    public void Run(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += 0.01)
        {
            _time.Advance(TimeSpan.FromSeconds(0.01));
            Session.Tick(Session.Clock.Now);
            for (; _seen < World.AnswerLog.Count; _seen++)
            {
                Timeline.Add((Session.Clock.Now, World.AnswerLog[_seen]));
            }
        }
    }

    public void Begin()
    {
        _seen = World.AnswerLog.Count;
        Timeline.Clear();
        Motions.Clear();
        Scripts.Clear();
    }

    public void Learn(params uint[] ids)
    {
        var learn = Session.Tools.LearnSpells(ids, ActionSource.Host);
        Run(1.5);
        Assert.True(learn.IsCompleted, learn.Summary);
    }

    public void Mode(CombatMode mode)
    {
        if (mode == CombatMode.Magic)
        {
            WieldedSetups.Hold(Session, WieldedSetups.Wand); // the combat-mode guard (M4 3.1 g) wants a caster in the hand for the magic mode
        }

        var change = Session.Actions.SetCombatMode(mode, ActionSource.Host);
        Run(1.5);
        Assert.True(change.IsCompleted, change.Summary);
    }

    public void Dispose() => Session.Dispose();

    /// <summary>The time of the first answer tagged <paramref name="tag"/>.</summary>
    public double First(string tag) => Timeline.First(e => e.Tag == tag).At;

    /// <summary>The chat lines the session holds, oldest first.</summary>
    public IReadOnlyList<ChatLine> Chat => [.. Enumerable.Range(0, Session.State.Chat.Count).Select(i => Session.State.Chat[i])];

    public string[] Tags => [.. Timeline.Select(e => e.Tag)];
}

/// <summary>
/// M3 2.1 s2a: the stand-in's cast plays ACE's sequence (<c>StandInWorld.Cast.cs</c>): the words, an UpdateMotion per wind-up and the cast gesture, the effect and the scripts at the plan's EffectTime, Ready, then UseDone a second later; a fizzle at the same
/// moment as the effect; a self Boost and a self Transfer answered with the vital updates and the line. The plans here are made by hand with distinct durations, so a timing that is not the plan's cannot pass for it; the dats' own plans are
/// <see cref="StandInCastPlannerTests"/>'.
/// </summary>
public sealed class StandInCastSequenceTests
{
    private const uint Buff = 10;
    private const uint FastBuff = 11;
    private const uint Fizzler = 12;
    private const uint Revitalize = 1177; // Revitalize Self I: 15 to 35 stamina
    private const uint StaminaToMana = 1676; // Stamina to Mana Self I
    private const uint MeditativeTrance = 2345; // Stamina to Mana Self VII (a BuffStatMods row the stand-in once lacked)

    private static readonly PlayScript CasterScript = (PlayScript)0x1A;
    private static readonly PlayScript TargetScript = (PlayScript)0x1B;

    private static SpellInfo Self(uint id, SpellType meta, string name, SpellFlags flags = SpellFlags.None, uint power = 1) =>
        WorldActionRig.Spell(id, meta, SpellFlags.SelfTargeted | SpellFlags.Beneficial | flags) with
        {
            Name = name,
            Category = 40 + id,
            Power = power,
            Duration = 1800.0,
            CasterEffect = CasterScript,
            TargetEffect = TargetScript,
        };

    private static readonly SpellInfo ThreeScarabs = Self(Buff, SpellType.Enchantment, "Three Scarab Self");
    private static readonly SpellInfo Fast = Self(FastBuff, SpellType.Enchantment, "Fast Self", SpellFlags.FastCast);
    private static readonly SpellInfo Level8 = Self(Fizzler, SpellType.Enchantment, "Strength Self VIII", power: 400);
    private static readonly SpellInfo Boost = Self(Revitalize, SpellType.Boost, "Revitalize Self I");
    private static readonly SpellInfo Transfer = Self(StaminaToMana, SpellType.Transfer, "Stamina to Mana Self I");
    private static readonly SpellInfo Trance = Self(MeditativeTrance, SpellType.Transfer, "Meditative Trance");

    // Wind-up and gesture motions: real Magic-stance commands (the ids do not matter here; the order and the times do).
    private static readonly uint[] Windups = [0x10000103, 0x10000104, 0x10000105];

    private static CastPlan Plan(SpellInfo spell, double[] windupSeconds, double gestureSeconds)
    {
        var steps = new List<CastStep>();
        var t = 0.0;
        for (var i = 0; i < windupSeconds.Length; i++)
        {
            steps.Add(new CastStep(CastStepKind.Windup, t, MotionCommands.Magic, Windups[i], CastGesturePlanner.CastSpeed, windupSeconds[i], (uint)(i + 1)));
            t += windupSeconds[i];
        }

        steps.Add(new CastStep(CastStepKind.CastGesture, t, MotionCommands.Magic, MotionCommands.MagicHeal, CastGesturePlanner.CastSpeed, gestureSeconds, 80));
        t += gestureSeconds;
        steps.Add(new CastStep(CastStepKind.SpellEffects, t, MotionCommands.Magic, 0, 0f, 0.0, 0));
        steps.Add(new CastStep(CastStepKind.ReturnToReady, t, MotionCommands.Magic, MotionCommands.Ready, 1f, CastGesturePlanner.RecoilSeconds, 0));
        return new CastPlan
        {
            Spell = spell,
            BaseFormula = [],
            PlayerFormula = [],
            AccountName = StandInCastPlanner.AccountName,
            Words = "Zojak Quaguz",
            Steps = steps,
            EffectTime = t,
            EffectIntensity = 0.4f,
            EndTime = t + CastGesturePlanner.RecoilSeconds,
        };
    }

    private static readonly Dictionary<uint, CastPlan> Plans = new()
    {
        [Buff] = Plan(ThreeScarabs, [0.5, 0.75, 0.4], 1.1),
        [FastBuff] = Plan(Fast, [], 0.9),
        [Fizzler] = Plan(Level8, [0.6], 1.3),
        [Revitalize] = Plan(Boost, [0.45], 1.0),
        [StaminaToMana] = Plan(Transfer, [0.3, 0.3], 0.8),
        [MeditativeTrance] = Plan(Trance, [0.3, 0.3], 0.8),
    };

    private static StandInVisit Visit(StandInPlayerStats? stats = null, bool planner = true) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = stats ?? new StandInPlayerStats(),
        CastPlanner = planner ? info => Plans.GetValueOrDefault(info.Id) : null,
    };

    private static CastRig Ready(StandInVisit visit, params uint[] known)
    {
        var rig = new CastRig(visit, [ThreeScarabs, Fast, Level8, Boost, Transfer, Trance]);
        rig.Learn(known);
        rig.Mode(CombatMode.Magic);
        rig.Begin();
        return rig;
    }

    private static void AssertTimes(CastRig rig, CastPlan plan, double[] motionTimes, double effectTag)
    {
        // The words at the cast's start, each motion at its plan time after it, within the 10 ms tick.
        var start = rig.First("02BB");
        for (var i = 0; i < motionTimes.Length; i++)
        {
            Assert.InRange(motionTimes[i] - start, plan.Steps[i].StartTime - 0.011, plan.Steps[i].StartTime + 0.021);
        }

        Assert.InRange(effectTag - start, plan.EffectTime - 0.011, plan.EffectTime + 0.021);
    }

    [Fact]
    public void A_self_buff_with_three_scarabs_plays_the_words_three_windups_the_gesture_the_effect_ready_then_use_done_in_the_plans_times()
    {
        using var rig = Ready(Visit(), Buff);
        var plan = Plans[Buff];
        var cast = rig.Session.Actions.Cast(Buff, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(["02BB", "F74C", "F74C", "F74C", "F74C", "02C2", "F755", "F755", "F74C", "01C7"], rig.Tags);

        // The words: the planner's chant, spoken by the player, chat type Spellcasting.
        var words = rig.Chat.Last(l => l.Kind == ChatKind.Speech && l.ChatType == StandInWorld.SpellcastingChat);
        Assert.Equal((plan.Words, rig.Session.State.Self.Guid), (words.Text, words.SenderGuid));

        // The motions: stance Magic, the plan's commands in order, speed 2.0 for the wind-ups and the gesture, then Ready at 1.0 (no speed on the wire).
        Assert.Equal(5, rig.Motions.Count);
        var expected = plan.Steps.Where(s => s.Kind != CastStepKind.SpellEffects).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            var motion = rig.Motions[i].Motion;
            Assert.Equal((MotionCommandTable.ToWire(MotionCommands.Magic), MotionCommandTable.ToWire(expected[i].Motion)), (motion.Stance, motion.Forward!.Value));
            Assert.Equal(expected[i].Kind == CastStepKind.ReturnToReady ? (float?)null : CastGesturePlanner.CastSpeed, motion.ForwardSpeed);
        }

        // Times: the plan's, which is the sum of the windups' and the gesture's durations; Ready at the effect's time, UseDone a recoil later.
        var tag = (string name) => rig.Timeline.First(e => e.Tag == name).At;
        AssertTimes(rig, plan, [.. rig.Motions.Take(4).Select(m => m.At)], tag("02C2"));
        Assert.Equal(0.5 + 0.75, rig.Motions[2].At - rig.Motions[0].At, 0.03);
        Assert.Equal(plan.EffectTime, rig.Motions[4].At - rig.First("02BB"), 0.03);
        Assert.Equal(CastGesturePlanner.RecoilSeconds, rig.First("01C7") - rig.Motions[4].At, 0.03);

        // The scripts: the caster's then the target's, both on the player (a self spell), at the plan's intensity, at the effect's moment (after the enchantment, as ACE's DoSpellEffects follows the switch).
        Assert.Equal([(uint)CasterScript, (uint)TargetScript], rig.Scripts.Select(s => s.Script.ScriptType));
        Assert.All(rig.Scripts, s => Assert.Equal((rig.Session.State.Self.Guid, plan.EffectIntensity), (s.Script.Guid, s.Script.Speed)));
        Assert.Equal(tag("02C2"), rig.Scripts[0].At, 0.011);
    }

    [Fact]
    public void A_fast_cast_spell_has_no_windup_only_the_gesture()
    {
        using var rig = Ready(Visit(), FastBuff);
        var plan = Plans[FastBuff];
        var cast = rig.Session.Actions.Cast(FastBuff, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(["02BB", "F74C", "02C2", "F755", "F755", "F74C", "01C7"], rig.Tags);
        Assert.Equal(MotionCommandTable.ToWire(MotionCommands.MagicHeal), rig.Motions[0].Motion.Forward);
        Assert.Equal(plan.EffectTime, rig.First("02C2") - rig.First("02BB"), 0.03);
        Assert.Equal(plan.EndTime, rig.First("01C7") - rig.First("02BB"), 0.03);
    }

    [Fact]
    public void A_fizzle_comes_after_the_words_and_the_gestures_as_a_fizzle_script_and_the_0402_error_then_ready_and_use_done()
    {
        using var rig = Ready(Visit(), Fizzler);
        var plan = Plans[Fizzler];
        var fizzles = rig.World.Fizzles;
        var cast = rig.Session.Actions.Cast(Fizzler, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        Assert.Equal((ActionOutcomeKind.ServerError, StandInWorld.FizzleError), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
        Assert.Equal(["02BB", "F74C", "F74C", "F755", "028A", "F74C", "01C7"], rig.Tags);
        Assert.Equal(fizzles + 1, rig.World.Fizzles);
        var script = Assert.Single(rig.Scripts);
        Assert.Equal(((uint)PlayScript.Fizzle, StandInWorld.FizzleIntensity), (script.Script.ScriptType, script.Script.Speed));
        Assert.Equal(plan.EffectTime, script.At - rig.First("02BB"), 0.03);
        Assert.Equal(script.At, rig.First("028A"), 0.011);
        Assert.Equal(plan.EndTime, rig.First("01C7") - rig.First("02BB"), 0.03);
        Assert.DoesNotContain(rig.Q.Enchantments, e => e.SpellId == Fizzler);
    }

    [Fact]
    public void A_self_boost_raises_stamina_by_the_seeded_roll_with_ACEs_line_and_no_enchantment()
    {
        using var rig = Ready(Visit(new StandInPlayerStats { Stamina = 20 }), Revitalize);
        Assert.True(rig.Q.TryGetVitalLevels(3, out var before, out var max));
        var plan = Plans[Revitalize];
        var cast = rig.Session.Actions.Cast(Revitalize, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        // The roll is the stand-in's seeded generator's first: between the base and the base plus the variance, inclusive (15 to 35).
        var roll = new Random(StandInWorld.VitalSeed).Next(15, 36);
        Assert.True(rig.Q.TryGetVitalLevels(3, out var after, out _));
        Assert.Equal((20u, 20u + (uint)roll), (before, after));
        Assert.True(after <= max);
        Assert.Equal(["02BB", "F74C", "F74C", "02E9", "F7E0", "F755", "F755", "F74C", "01C7"], rig.Tags);
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind); // no enchantment and no projectile: only UseDone ends it
        var line = rig.Chat.Last(l => l.Text.StartsWith("You cast", StringComparison.Ordinal));
        Assert.Equal($"You cast Revitalize Self I and restore {roll} points of your stamina.", line.Text);
        Assert.Equal(StandInWorld.MagicChat, (int)line.ChatType);
        Assert.Empty(rig.Q.Enchantments.Where(e => e.SpellId == Revitalize));
    }

    [Fact]
    public void A_self_transfer_moves_stamina_to_mana_by_ACEs_rule_capped_scaled_and_clamped_to_what_mana_misses()
    {
        using var rig = Ready(Visit(), StaminaToMana); // stamina 120 of 120, mana 75 of 100
        var plan = Plans[StaminaToMana];
        var cast = rig.Session.Actions.Cast(StaminaToMana, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        // Stamina to Mana Self I: proportion 0.5 of 120 = 60, capped at 50; loss 0.1 gives 45; mana misses 25, so the destination gets 25 and the source's share scales to round(50 * 25/45) = 28.
        Assert.True(rig.Q.TryGetVitalLevels(3, out var stamina, out _));
        Assert.True(rig.Q.TryGetVitalLevels(5, out var mana, out _));
        Assert.Equal((92u, 100u), (stamina, mana));
        Assert.Equal(["02BB", "F74C", "F74C", "F74C", "02E9", "02E9", "F7E0", "F755", "F755", "F74C", "01C7"], rig.Tags);
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind);
        Assert.Contains(rig.Chat, l => l.Text == "You cast Stamina to Mana Self I on yourself and lose 28 points of stamina and also gain 25 points of mana");
    }

    [Fact]
    public void Meditative_Trance_2345_transfers_stamina_to_mana_from_the_cycles_own_BuffStatMods_row()
    {
        // M3 2.1 fix A (2.1-R3): the stand-in's Stamina to Mana rows ARE BuffStatMods.StaminaToMana, so the VII (loss -0.75, no cap) it once lacked changes the vitals as the cycle predicts.
        using var rig = Ready(Visit(), MeditativeTrance); // stamina 120 of 120, mana 75 of 100
        var plan = Plans[MeditativeTrance];
        var cast = rig.Session.Actions.Cast(MeditativeTrance, ActionSource.Host);
        rig.Run(plan.EndTime + 1.5);

        var row = Lifestone.Client.Session.Mage.BuffStatMods.TransferOf(MeditativeTrance)!.Value;
        Lifestone.Client.Session.Mage.BuffStatMods.Transfer(row, 120, 25, out var drained, out var given);
        Assert.Equal((14u, 25u), (drained, given)); // 60 drained, 105 given, scaled to the 25 mana misses: round(60 * 25/105) = 14

        Assert.True(rig.Q.TryGetVitalLevels(3, out var stamina, out _));
        Assert.True(rig.Q.TryGetVitalLevels(5, out var mana, out _));
        Assert.Equal((120u - drained, 75u + given), (stamina, mana));
        Assert.Contains(rig.Chat, l => l.Text == "You cast Meditative Trance on yourself and lose 14 points of stamina and also gain 25 points of mana");
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("m3-standin-vital-spell-table", StringComparison.Ordinal));
    }

    [Fact]
    public void A_self_transfer_with_nothing_to_gain_changes_no_vital_and_sends_no_update()
    {
        using var rig = Ready(Visit(new StandInPlayerStats { Mana = 100 }), StaminaToMana); // mana is full: the destination's room is 0
        var cast = rig.Session.Actions.Cast(StaminaToMana, ActionSource.Host);
        rig.Run(Plans[StaminaToMana].EndTime + 1.5);

        Assert.True(rig.Q.TryGetVitalLevels(3, out var stamina, out _));
        Assert.Equal(120u, stamina);
        Assert.DoesNotContain("02E9", rig.Tags);
        Assert.Contains(rig.Chat, l => l.Text == "You cast Stamina to Mana Self I on yourself and lose 0 points of stamina and also gain 0 points of mana");
        Assert.True(cast.IsCompleted, cast.Summary);
    }

    [Fact]
    public void Without_a_planner_a_cast_is_what_it_was_the_effect_and_use_done_a_second_later_logged_once_as_a_named_todo()
    {
        using var rig = Ready(Visit(planner: false), Buff);
        var cast = rig.Session.Actions.Cast(Buff, ActionSource.Host);
        rig.Run(3.0);
        var second = rig.Session.Actions.Cast(Buff, ActionSource.Host);
        rig.Run(3.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);
        Assert.Equal(["02C2", "01C7", "02C2", "01C7"], rig.Tags);
        Assert.Empty(rig.Motions);
        Assert.Equal(StandInWorld.CastRecoilSeconds, rig.Timeline[1].At - rig.Timeline[0].At, 0.03);
        Assert.Single(rig.Log.Lines, l => l.Contains("TODO(m3-standin-cast-plan)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("m3-standin-cast-gestures", StringComparison.Ordinal));
    }

    [Fact]
    public void The_placeholders_that_remain_are_still_logged_once_each_by_name()
    {
        using var rig = Ready(Visit(), Buff, Fizzler);
        rig.Session.Actions.Cast(Buff, ActionSource.Host);
        rig.Run(Plans[Buff].EndTime + 1.5);
        rig.Session.Actions.Cast(Fizzler, ActionSource.Host);
        rig.Run(Plans[Fizzler].EndTime + 1.5);

        foreach (var todo in new[] { "m3-standin-cast-mana", "m3-standin-cast-components", "m3-standin-magic-skill" })
        {
            Assert.Single(rig.Log.Lines, l => l.Contains("TODO(" + todo + ")", StringComparison.Ordinal));
        }
    }
}
