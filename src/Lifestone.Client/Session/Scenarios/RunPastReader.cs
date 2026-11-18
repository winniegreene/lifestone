using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 run past (D137): the attackers of one travel leg as the run-past rule reads them (<see cref="RunPast.Judge"/>), shared by the travel legs' walks (<c>TravelLegRunPast.cs</c>) and the corpse leg of a death on a travel leg
/// (<c>DeathHandler</c>, route=leg). Each attacker is tracked from the frame the reader first sees it: the attacks its name lands after that (an <see cref="AttackWatch"/>'s tally), the farthest it attacked her from (a thrower or a
/// caster attacks from beyond <see cref="RunPast.MeleeReachMetres"/>), its damage a second, its pace (<see cref="CreaturePaces.Live"/>), and where its body stands as the client draws it (<see cref="Contracts.LivePositions"/>). Each
/// verdict is logged once a change (<see cref="Note"/>).
/// </summary>
public sealed class RunPastReader
{
    private readonly Dictionary<uint, Track> _tracks = [];

    /// <summary>The attackers left behind or passed (report-only).</summary>
    public int RanPast { get; private set; }

    /// <summary>One attacker for the rule.</summary>
    /// <param name="session">The session (its objects, its live positions, its clock).</param>
    /// <param name="watch">The attacks heard, by name.</param>
    /// <param name="p">The attacker present.</param>
    /// <param name="her">Her planar global position.</param>
    /// <param name="goal">The walk's goal (her heading for the ranged rule), or null.</param>
    /// <param name="herSpeed">Her run speed.</param>
    /// <param name="paces">The creatures' paces.</param>
    /// <param name="times">M6 14 attack timing: the creatures' attack timings (its first attack's seconds pad the measure); null: the named fallback.</param>
    public RunPastAttacker Read(IGameSessionView session, AttackWatch watch, PresentAttacker p, Vector2 her, Vector2? goal, double herSpeed, CreaturePaces paces, CreatureAttackTimes? times = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(paces);
        var now = session.Clock.Now;
        var (attacks, damage) = watch.AttacksOf(p.Name);
        if (!_tracks.TryGetValue(p.Guid, out var track))
        {
            track = new Track { FirstSeen = now, AttacksAtFirst = attacks, DamageAtFirst = damage, AttacksSeen = attacks };
            _tracks[p.Guid] = track;
        }

        var metres = p.Metres;
        var where = her;
        Pace? pace = null;
        var paceWhy = "it is not in the object table";
        if (session.State.Objects.TryGet(p.Guid, out var o))
        {
            if (session.Live.At(o) is { } body)
            {
                var g = body.ToGlobal();
                where = new Vector2((float)g.X, (float)g.Y);
                metres = Vector2.Distance(where, her);
            }

            pace = paces.Live(o, CreatureFacts.All, out paceWhy);
        }

        if (attacks > track.AttacksSeen || !track.Placed)
        {
            track.MaxHitMetres = Math.Max(track.MaxHitMetres, metres); // where it stood when its last attack reached her (the first: where the reader first saw it)
            track.AttacksSeen = attacks;
            track.Placed = true;
        }

        var ranged = track.MaxHitMetres > RunPast.MeleeReachMetres;
        var range = ranged ? Math.Max(track.MaxHitMetres, CreatureFacts.All.Find(p.Wcid)?.Ranged?.RangeMetres ?? 0) : track.MaxHitMetres;
        var seconds = ranged ? RunPast.SecondsInRange(her, goal is { } g2 ? g2 - her : Vector2.Zero, herSpeed, where, range) : 0;

        // its damage a second: what its name dealt since the reader first saw it, and the attack that made it an attacker (its name's mean before), over the seconds since, one attack before (M6 14 attack timing: its own mean
        // seconds between attacks, ACE's attack loop over the dats; the named 2.75 s without them)
        var first = track.AttacksAtFirst > 0 ? track.DamageAtFirst / track.AttacksAtFirst : 0;
        var perSecond = (damage - track.DamageAtFirst + first) / (now - track.FirstSeen + (times ?? CreatureAttackTimes.None).Of(p.Wcid, p.Name).Seconds);
        return new RunPastAttacker(p.Guid, p.Name, metres, pace, paceWhy, ranged, range, attacks - track.AttacksAtFirst, perSecond, seconds);
    }

    /// <summary>The verdict's line when it changed for the attacker (<c>run past: &lt;reason&gt;: &lt;why&gt;</c>), else null; counts the attackers left behind or passed.</summary>
    /// <param name="guid">The attacker.</param>
    /// <param name="v">Its verdict.</param>
    public string? Note(uint guid, in RunPastVerdict v)
    {
        if (!_tracks.TryGetValue(guid, out var track) || string.Equals(track.Logged, v.Reason, StringComparison.Ordinal))
        {
            return null;
        }

        if (v.Reason is "outrun" or "passing" && track.Logged is null or "measuring")
        {
            RanPast++;
        }

        track.Logged = v.Reason;
        return $"run past: {v.Reason}: {v.Why} (M6 14 run past, D137)";
    }

    private sealed class Track
    {
        public double FirstSeen { get; init; }

        public int AttacksAtFirst { get; init; }

        public double DamageAtFirst { get; init; }

        public int AttacksSeen { get; set; }

        public double MaxHitMetres { get; set; }

        public bool Placed { get; set; }

        public string? Logged { get; set; }
    }
}
