using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Session.Vitals;

/// <summary>A run speed and where it comes from (<see cref="CreaturePace"/>).</summary>
/// <param name="MetresPerSecond">The speed, metres a second.</param>
/// <param name="Why">Its sources in words (the run rate, the motion's run speed and the scale).</param>
public readonly record struct Pace(double MetresPerSecond, string Why);

/// <summary>
/// M6 14 run past (the owner's amendment of M5 D88 on travel legs, D137): how fast a creature runs and how fast she does, from facts and her live state, never a hand-sized number.
/// <list type="bullet">
/// <item><b>A creature</b> runs at its motion table's run speed times its run rate times its scale: ACE's <c>Monster_Navigation.GetMovementSpeed</c> (<c>MotionTable.GetRunSpeed(MotionTableId) x RunRate x ObjScale</c>, 2.5 m/s when the
/// table gives no run, Monster_Navigation.cs:330-341 @95ef426d), the run rate <c>MovementSystem.GetRunRate(0, Run, 1)</c> of its Run skill (<c>GetRunRate</c>, 346-368: no burden for a creature). The motion's run speed is its RunForward
/// cycle in the table's default style (<c>MotionTable.GetMotionData</c>): the cycle's velocity plus its animations' root motion a second (<c>GetAnimDist</c>: the pos frames' summed offset over their count times the first animation's
/// framerate, MotionTable.cs:572-589), read from the user's dats at run time (<see cref="RunAnimSpeed"/>; never recorded). The run rate is the live one when the server sent it (a MoveToObject's run rate, the creature chasing her),
/// else its Run skill's from the profile's facts (<see cref="GroundApproachHostile.RunSkill"/>).</item>
/// <item><b>She</b> runs at <see cref="MotionInterpreter.RunAnimSpeed"/> times the run rate of her current (buffed) Run skill, her burden not counted (<see cref="Her"/>; the client's own <c>InqRunRate</c>).</item>
/// </list>
/// </summary>
public static class CreaturePace
{
    /// <summary>ACE's run speed of a motion table that gives none (<c>Monster_Navigation.GetMovementSpeed</c>: <c>if (moveSpeed == 0) moveSpeed = 2.5f</c>).</summary>
    public const double AceFallbackAnimMetresPerSecond = 2.5;

    /// <summary>The Run skill's SkillTable formula: Quickness over 1 (retail's Run; the dats' SkillTable row the movement's <c>QualitiesWeenie</c> reads).</summary>
    public static readonly Lifestone.Data.CharacterCreation.SkillTableFormula RunFormula = new(0, 1, 0, 1, MageTemplate.Quickness, 0);

    /// <summary>The run rate of a Run skill with no burden (<c>MovementSystem.GetRunRate(0, skill, 1)</c>).</summary>
    /// <param name="runSkill">The Run skill.</param>
    public static double RunRate(int runSkill) => MovementSystem.GetRunRate(0f, Math.Max(0, runSkill), 1f);

    /// <summary>A creature's speed: its motion's run speed (ACE's 2.5 when the table gives none) times its run rate times its scale.</summary>
    /// <param name="animMetresPerSecond">The motion table's run speed at rate 1 (0: none).</param>
    /// <param name="runRate">Its run rate.</param>
    /// <param name="scale">Its ObjScale (1 when none).</param>
    public static double Speed(double animMetresPerSecond, double runRate, double scale) =>
        (animMetresPerSecond > 0 ? animMetresPerSecond : AceFallbackAnimMetresPerSecond) * Math.Max(0, runRate) * (scale > 0 ? scale : 1.0);

    /// <summary>
    /// The run speed at rate 1 of a motion table's RunForward cycle in its default style: the cycle's velocity plus its animations' root motion a second (ACE's <c>GetAnimDist</c>); 0 when the table has no such cycle or neither moves.
    /// </summary>
    /// <param name="table">The motion table.</param>
    /// <param name="animation">The animations by id.</param>
    public static double RunAnimSpeed(MotionTableData table, Func<uint, AnimationData?> animation)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(animation);
        if (!table.Cycles.TryGetValue(MotionTableData.Key(table.DefaultStyle, MotionCommands.RunForward), out var data) || data.Anims.Count == 0)
        {
            return 0;
        }

        var offset = Vector3.Zero;
        var frames = 0;
        foreach (var a in data.Anims)
        {
            if (animation(a.AnimId)?.PosFrames is not { } pos)
            {
                continue;
            }

            foreach (var f in pos)
            {
                offset += f.Origin;
                frames++;
            }
        }

        var root = frames == 0 ? Vector3.Zero : offset / frames * Math.Abs(data.Anims[0].Framerate);
        return (data.Velocity + root).Length();
    }

    /// <summary>Her run speed now: <see cref="MotionInterpreter.RunAnimSpeed"/> times the run rate of her current Run skill, or null with the reason when the skill is not read.</summary>
    /// <param name="q">Her qualities.</param>
    /// <param name="why">The speed's sources, or why there is none.</param>
    public static double? Her(PlayerQualities q, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (!SkillRules.TryCurrent(q, MageTemplate.Run, RunFormula, SkillRules.TrainedClass, out var run))
        {
            why = "her Run skill is not read";
            return null;
        }

        var rate = RunRate((int)run);
        var speed = rate * MotionInterpreter.RunAnimSpeed;
        why = string.Create(CultureInfo.InvariantCulture, $"her Run {run}: rate {rate:0.###} x {MotionInterpreter.RunAnimSpeed:0} m/s");
        return speed;
    }

    /// <summary>The speed of an approach hostile from its facts (<see cref="GroundApproachHostile.RunSkill"/>, its motion table and scale), its motion's run speed by <paramref name="animOf"/>; null when it carries no pace facts.</summary>
    /// <param name="hostile">The hostile.</param>
    /// <param name="animOf">The motion table's run speed at rate 1 (0: none, ACE's 2.5 then), or null when not readable (the dats are not at hand).</param>
    public static Pace? Of(GroundApproachHostile hostile, Func<uint, double?> animOf)
    {
        ArgumentNullException.ThrowIfNull(hostile);
        ArgumentNullException.ThrowIfNull(animOf);
        if (hostile.RunSkill is not { } run || hostile.MotionTable is not { } table || animOf(table) is not { } anim)
        {
            return null;
        }

        var rate = RunRate(run);
        var speed = Speed(anim, rate, hostile.Scale);
        return new Pace(speed, string.Create(CultureInfo.InvariantCulture, $"Run {run}: rate {rate:0.###} x its motion 0x{table:X8}'s run {(anim > 0 ? anim : AceFallbackAnimMetresPerSecond):0.##} m/s{(anim > 0 ? string.Empty : " (ACE's 2.5: the table gives none)")} x scale {hostile.Scale:0.##}"));
    }
}

/// <summary>
/// M6 14 run past: the creatures' paces as the run leg reads them, live where the client knows them: the creature's motion table and scale from its physics description, its run rate from the server's MoveToObject (a creature chasing her),
/// each else from the facts of its weenie (<see cref="CreatureFacts"/>: an approach hostile's <c>run_skill</c>, <c>motion_table</c> and <c>scale</c>); the motion's run speed from the dats (cached by table). Unknown is null, said so.
/// </summary>
/// <param name="animOf">The motion table's run speed at rate 1, or null when it cannot be read (no dats at hand).</param>
public sealed class CreaturePaces(Func<uint, double?> animOf)
{
    private readonly ConcurrentDictionary<uint, double?> _anim = new();

    /// <summary>The paces read from <paramref name="source"/>'s dats.</summary>
    /// <param name="source">The dats' animation source.</param>
    public static CreaturePaces FromDats(DatAnimationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CreaturePaces(table => source.GetMotionTable(table) is { } data ? CreaturePace.RunAnimSpeed(data, source.GetAnimation) : null);
    }

    /// <summary>No dats at hand: no motion's run speed is known, so no creature's pace is (the leg measures it).</summary>
    public static CreaturePaces None { get; } = new(_ => null);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Lifestone.Data.Dat.DatSet, CreaturePaces> ByDats = [];

    /// <summary>The paces of <paramref name="dats"/>, one cache a dat set; <see cref="None"/> without dats.</summary>
    /// <param name="dats">The user's dats, or null.</param>
    public static CreaturePaces For(Lifestone.Data.Dat.DatSet? dats) => dats is null ? None : ByDats.GetValue(dats, d => FromDats(new DatAnimationSource(d)));

    /// <summary>The motion table's run speed at rate 1, cached; null when not readable.</summary>
    /// <param name="table">The motion table id.</param>
    public double? AnimOf(uint table) => _anim.GetOrAdd(table, animOf);

    /// <summary>The pace of <paramref name="o"/> now, or null with the reason (no motion table known, no run rate known, no dats).</summary>
    /// <param name="o">The creature.</param>
    /// <param name="facts">The creatures' facts.</param>
    /// <param name="why">Why there is none.</param>
    public Pace? Live(WorldObject o, CreatureFacts facts, out string why)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(facts);
        var c = CultureInfo.InvariantCulture;
        var fact = facts.Find(o.Weenie.WeenieClassId);
        var table = o.Physics.MotionTableId is { } live and > 0 ? live : fact?.MotionTable ?? 0u;
        if (table == 0)
        {
            why = "no motion table is known for it (none in its description, no facts)";
            return null;
        }

        if (AnimOf(table) is not { } anim)
        {
            why = string.Create(c, $"its motion 0x{table:X8}'s run is not read (no dats at hand)");
            return null;
        }

        double rate;
        string rateWhy;
        if (o.Motion?.MoveTo is { RunRate: > 0 } moveTo)
        {
            (rate, rateWhy) = (moveTo.RunRate, string.Create(c, $"its MoveTo's run rate {moveTo.RunRate:0.###}"));
        }
        else if (fact?.RunSkill is { } run)
        {
            (rate, rateWhy) = (CreaturePace.RunRate(run), string.Create(c, $"Run {run}: rate {CreaturePace.RunRate(run):0.###}"));
        }
        else
        {
            why = "no run rate is known for it (no MoveTo seen, no Run skill in its facts)";
            return null;
        }

        var scale = o.Physics.Scale is { } s and > 0 ? s : fact?.Scale ?? 1.0;
        why = string.Empty;
        return new Pace(CreaturePace.Speed(anim, rate, scale), string.Create(c, $"{rateWhy} x its motion 0x{table:X8}'s run {(anim > 0 ? anim : CreaturePace.AceFallbackAnimMetresPerSecond):0.##} m/s x scale {scale:0.##}"));
    }
}
