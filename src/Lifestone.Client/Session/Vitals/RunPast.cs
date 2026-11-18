using System.Globalization;
using System.Numerics;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What the run-past rule says of one attacker (<see cref="RunPast.Judge"/>).</summary>
public enum RunPastCall
{
    /// <summary>She runs on: it is left behind, passed, or still measured.</summary>
    RunOn,

    /// <summary>She stops and fights it back.</summary>
    Fight,
}

/// <summary>One attacker as the run-past rule reads it on a travel leg (<see cref="RunPast.Judge"/>).</summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Name">Its name.</param>
/// <param name="Metres">Its distance now, centre to centre (the body the client draws when it draws one).</param>
/// <param name="Pace">Its run speed, or null when not known.</param>
/// <param name="PaceWhy">Why its pace is not known (empty when it is).</param>
/// <param name="Ranged">True when it hits her from beyond <see cref="RunPast.MeleeReachMetres"/> (heard so, or its facts give a throw).</param>
/// <param name="RangeMetres">Its range: the farthest it was heard hitting her from, or its throw's (ranged only).</param>
/// <param name="HitsSinceSeen">The hits heard from its name since the rule first saw it attacking her.</param>
/// <param name="DamagePerSecond">The damage a second its name has dealt her since then (ranged only).</param>
/// <param name="SecondsInRange">The seconds she stays in its range at her speed on her way (ranged only; infinity when it keeps pace).</param>
public readonly record struct RunPastAttacker(uint Guid, string Name, double Metres, Pace? Pace, string PaceWhy, bool Ranged, double RangeMetres, int HitsSinceSeen, double DamagePerSecond, double SecondsInRange);

/// <summary>Her side of the run-past rule (<see cref="RunPast.Judge"/>).</summary>
/// <param name="Speed">Her run speed, metres a second.</param>
/// <param name="SpeedWhy">Its sources.</param>
/// <param name="Blocked">True when she has moved under <see cref="RunPast.BlockedGainMetres"/> in the last <see cref="RunPast.BlockedSeconds"/> of the walk.</param>
/// <param name="AtGoal">True at the leg's goal (its door).</param>
/// <param name="HealthAboveLine">Her health over the derived retreat line, points.</param>
/// <param name="LineWhy">The line in words.</param>
/// <param name="Standing">
/// M6 14 caught on the run (the owner's ruling, 2026-11-15: she must not keep heading back on the run while she is being attacked): true while she stands to fight (a fight-back judging again when the set it was stopped for is spent): a melee attacker on her
/// then is fought before she runs on, whatever its pace (<c>on-her</c>).
/// </param>
public readonly record struct RunPastHer(double Speed, string SpeedWhy, bool Blocked, bool AtGoal, double HealthAboveLine, string LineWhy, bool Standing = false);

/// <summary>The rule's answer for one attacker: the call, its reason token and the line in words.</summary>
/// <param name="Call">Run on or fight.</param>
/// <param name="Reason"><c>outrun</c>, <c>measuring</c>, <c>passing</c> (run on); <c>caught</c>, <c>on-her</c>, <c>keeps-pace</c>, <c>blocked</c>, <c>ranged-line</c>, <c>at-goal</c> (fight).</param>
/// <param name="Why">The answer in words with its numbers.</param>
public readonly record struct RunPastVerdict(RunPastCall Call, string Reason, string Why);

/// <summary>
/// M6 14 run past (D137, the owner's amendment of M5 D88 on travel legs, 2026-11-14: a run to a dungeon door is no fight): on a travel leg an attacker does not stop her while she can outpace it. Pure; the travel leg reads it each frame
/// (<c>TravelLegRunPast.cs</c>) and the crossing's cost prices a run with it (<see cref="ApproachCost"/>). Inside a hunting ground M5 D88 stands.
/// <list type="bullet">
/// <item><b>Left behind</b> (<c>outrun</c>): a melee attacker whose run speed (<see cref="CreaturePace"/>: its facts and the dats) is under her speed less <see cref="PaceMarginShare"/>. Named once with both speeds.</item>
/// <item><b>Caught</b> (<c>caught</c>; M6 14 caught on the run, the owner's ruling, 2026-11-15: asked whether she was fighting or running; live ladder25 04:08:00 UTC: a Tusker Guard of 13.8 m/s, Armored Tuskers of 16.0 and a Silver Tusker of 17.0
/// stood 2 to 4 m from her 7.9 m/s run and were "measured" while they bit her): a melee attacker whose known pace keeps pace with hers and that stands within <see cref="MeleeReachMetres"/> has caught her: she cannot outrun it, so she
/// fights it at once, both speeds in the words.</item>
/// <item><b>Keeps pace</b> (<c>keeps-pace</c>): a melee attacker that hits her again after the rule first saw it (<see cref="KeepsHittingHits"/>) while its pace is as fast as hers, not known, or (its facts notwithstanding) it still hits a
/// running body: she stops and fights it. Until it hits again she runs on (<c>measuring</c>): only a pace not known, or a fast one not yet within melee reach, is measured.</item>
/// <item><b>On her</b> (<c>on-her</c>; M6 14 caught on the run): while she stands to fight (<see cref="RunPastHer.Standing"/>), a melee attacker within <see cref="MeleeReachMetres"/> is fought before she runs on, whatever its pace: she sheathes
/// and runs only from what is not on her.</item>
/// <item><b>Blocked</b> (<c>blocked</c>): she has moved under <see cref="BlockedGainMetres"/> in <see cref="BlockedSeconds"/> of the walk (the walker found no way round) with it within <see cref="MeleeReachMetres"/>: she fights it.</item>
/// <item><b>Ranged</b> (<c>passing</c> or <c>ranged-line</c>): an attacker that hits her from beyond <see cref="MeleeReachMetres"/>: its measured damage a second over the seconds she stays in its range is the cost of passing; she stops only
/// when that would bring her health to the derived retreat line before she is out of range.</item>
/// <item><b>At the goal</b> (<c>at-goal</c>): an attacker still on her at the leg's door is fought there before the portal (or the safety lines recall her).</item>
/// </list>
/// </summary>
public static class RunPast
{
    /// <summary>
    /// The share of her run speed a creature must stay under to be left behind (Lifestone's own: a creature chases her straight while her route turns round rocks and doors; live 2026-11-14 her walks on the island ran 5.8 to 6.3 m/s over
    /// legs with turns against her run's 7.7, so a creature within a tenth of her run is taken to keep pace).
    /// </summary>
    public const double PaceMarginShare = 0.1;

    /// <summary>The distance, centre to centre, within which an attacker heard hitting her is in melee (Lifestone's own: live fight-backs found the tuskers that hit her at 1.5 to 4.1 m).</summary>
    public const double MeleeReachMetres = 5;

    /// <summary>The hits heard from an attacker after the rule first saw it that make it one that keeps hitting (one more hit: a creature she outran swings at nothing).</summary>
    public const int KeepsHittingHits = 1;

    /// <summary>The seconds of a walk in which she must move <see cref="BlockedGainMetres"/> or be blocked (Lifestone's own: the walker's own creature replans take under a second).</summary>
    public const double BlockedSeconds = 3;

    /// <summary>The metres she must move in <see cref="BlockedSeconds"/> of a walk to be running.</summary>
    public const double BlockedGainMetres = 1;

    /// <summary>The seconds of the door: the last steps to the portal and its Use's answer (Lifestone's own: live, a teleport began 0.06 to 1 s after the seek ended). An attacker that closes to melee in them is on her at the door.</summary>
    public const double DoorSeconds = 3;

    /// <summary>True when a creature of <paramref name="speed"/> keeps pace with her at <paramref name="herSpeed"/> (at or over her speed less <see cref="PaceMarginShare"/>).</summary>
    /// <param name="speed">The creature's speed.</param>
    /// <param name="herSpeed">Her speed.</param>
    public static bool KeepsPace(double speed, double herSpeed) => speed >= herSpeed * (1 - PaceMarginShare);

    /// <summary>The rule for one attacker (see the class).</summary>
    /// <param name="her">Her side.</param>
    /// <param name="a">The attacker.</param>
    public static RunPastVerdict Judge(in RunPastHer her, in RunPastAttacker a)
    {
        var c = CultureInfo.InvariantCulture;
        var who = string.Create(c, $"'{a.Name}' 0x{a.Guid:X8} at {a.Metres:0.0} m");
        var speeds = a.Pace is { } p
            ? string.Create(c, $"its {p.MetresPerSecond:0.0} m/s ({p.Why}) against her {her.Speed:0.0} m/s ({her.SpeedWhy})")
            : string.Create(c, $"its pace is not known ({a.PaceWhy}); her {her.Speed:0.0} m/s ({her.SpeedWhy})");
        var keepsPace = a.Pace is not { } known || KeepsPace(known.MetresPerSecond, her.Speed);
        var keepsHitting = a.HitsSinceSeen >= KeepsHittingHits;
        if (her.AtGoal)
        {
            return new(RunPastCall.Fight, "at-goal", $"{who} is still on her at the leg's goal: she fights it at the door before the portal ({speeds})");
        }

        if (her.Blocked && a.Metres <= MeleeReachMetres)
        {
            return new(RunPastCall.Fight, "blocked", string.Create(c, $"{who} stands in melee and she has moved under {BlockedGainMetres:0} m in {BlockedSeconds:0} s: the way is blocked, so she fights it ({speeds})"));
        }

        if (a.Ranged)
        {
            var seconds = keepsPace && a.Pace is not null ? double.PositiveInfinity : a.SecondsInRange;
            var expected = a.DamagePerSecond * seconds;
            var numbers = string.Create(c, $"{a.DamagePerSecond:0.##} damage a second (measured) over {(double.IsPositiveInfinity(seconds) ? "all the run (it keeps pace)" : seconds.ToString("0.0", c) + " s")} left in its {a.RangeMetres:0} m range: about {(double.IsPositiveInfinity(expected) ? "all her health" : expected.ToString("0", c))}, against {her.HealthAboveLine:0} over the retreat line ({her.LineWhy})");
            return expected >= her.HealthAboveLine && expected > 0
                ? new(RunPastCall.Fight, "ranged-line", $"{who} hits her from range and would bring her to the retreat line before she is out of its range: she fights it; {numbers}")
                : new(RunPastCall.RunOn, "passing", $"{who} hits her from range: she runs past it; {numbers}");
        }

        if (!keepsPace)
        {
            return keepsHitting
                ? new(RunPastCall.Fight, "keeps-pace", $"{who} keeps hitting a running body though its pace is under hers: it keeps pace, so she fights it ({speeds})")
                : OnHer(in her, in a, who, speeds) ?? new(RunPastCall.RunOn, "outrun", $"{who} is left behind: {speeds}");
        }

        if (a.Pace is not null && a.Metres <= MeleeReachMetres)
        {
            return new(RunPastCall.Fight, "caught", string.Create(c, $"{who} keeps pace with her and stands within the {MeleeReachMetres:0} m of melee: it has caught her and she cannot outrun it, so she fights it at once ({speeds})"));
        }

        if (!keepsHitting && OnHer(in her, in a, who, speeds) is { } standing)
        {
            return standing; // a pace not known, in reach, while she stands to fight
        }

        return keepsHitting
            ? new(RunPastCall.Fight, "keeps-pace", $"{who} keeps pace and keeps hitting: she fights it ({speeds})")
            : new(RunPastCall.RunOn, "measuring", a.Pace is null
                ? $"{who} has a pace not known: she runs on and fights it if it hits again ({speeds})"
                : string.Create(c, $"{who} is as fast as her but not yet within the {MeleeReachMetres:0} m of melee: she runs on and fights it when it closes or hits again ({speeds})"));
    }

    /// <summary>
    /// M6 14 caught on the run (live ladder27 06:10:19.6-20.7 UTC: three Stinging Chitticks of 5.6 m/s bit her at 2.4 to 4.2 m as a fight-back's set was spent; judged outrun, she sheathed, the walk began, and the run past stopped it
    /// again within the second): while she stands to fight (<see cref="RunPastHer.Standing"/>), a melee attacker within <see cref="MeleeReachMetres"/> that would otherwise be left to the run (slower than her, or a pace not known) is on
    /// her: she fights it before she runs on (<c>on-her</c>). Null otherwise. A ranged attacker keeps its own rule; a fast one in reach is <c>caught</c> first.
    /// </summary>
    private static RunPastVerdict? OnHer(in RunPastHer her, in RunPastAttacker a, string who, string speeds) =>
        her.Standing && a.Metres <= MeleeReachMetres
            ? new(RunPastCall.Fight, "on-her", $"{who} stands in melee and attacks her while she stands to fight: it is on her, so she fights it before she runs on, whatever its pace ({speeds})")
            : null;

    /// <summary>
    /// The seconds she stays within <paramref name="range"/> of a creature at <paramref name="creature"/> running from <paramref name="her"/> along <paramref name="heading"/> at <paramref name="herSpeed"/>, the creature standing (a thrower
    /// stands to throw; ACE turns a ranged monster in range, <c>Monster_Navigation.StartTurn</c>): the time to the far edge of its range circle; 0 when her line never enters it.
    /// </summary>
    /// <param name="her">Her position.</param>
    /// <param name="heading">Her heading (normalised here; a zero heading leaves her standing: infinity while in range).</param>
    /// <param name="herSpeed">Her speed.</param>
    /// <param name="creature">The creature's position.</param>
    /// <param name="range">Its range.</param>
    public static double SecondsInRange(Vector2 her, Vector2 heading, double herSpeed, Vector2 creature, double range)
    {
        var r0 = her - creature;
        var inside = r0.LengthSquared() <= range * range;
        if (heading.LengthSquared() < 1e-9 || herSpeed <= 0)
        {
            return inside ? double.PositiveInfinity : 0;
        }

        var d = Vector2.Normalize(heading);
        var b = Vector2.Dot(r0, d);
        var disc = (b * b) - (r0.LengthSquared() - (range * range));
        if (disc < 0)
        {
            return 0;
        }

        var exit = -b + Math.Sqrt(disc);
        return exit <= 0 ? 0 : exit / herSpeed;
    }

    /// <summary>
    /// The crossing's mean seconds in a ranged creature's range (the cost's prior, <see cref="ApproachCost"/>): a creature that notices her within <paramref name="awareness"/> of her line (its offset uniform over it) throws from the
    /// moment it notices her until she is out of <paramref name="range"/>: the line's chord from the nearer of the two circles' entries to the range circle's exit, over her speed, averaged over the offsets.
    /// </summary>
    /// <param name="range">Its range.</param>
    /// <param name="awareness">How near it notices her.</param>
    /// <param name="herSpeed">Her speed.</param>
    public static double MeanSecondsInRange(double range, double awareness, double herSpeed)
    {
        if (herSpeed <= 0 || awareness <= 0 || range <= 0)
        {
            return 0;
        }

        const int Steps = 200;
        var sum = 0.0;
        for (var i = 0; i < Steps; i++)
        {
            var y = (i + 0.5) / Steps * awareness;
            if (y >= range)
            {
                continue;
            }

            var noticed = Math.Sqrt((awareness * awareness) - (y * y)); // along her line before it, where it notices her
            var leaves = Math.Sqrt((range * range) - (y * y)); // along her line after it, where she leaves its range
            sum += Math.Min(noticed, leaves) + leaves;
        }

        return sum / Steps / herSpeed;
    }
}
