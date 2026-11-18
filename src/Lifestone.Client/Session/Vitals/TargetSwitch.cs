using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// One creature in the target-switch rule (<see cref="TargetSwitch"/>): its health left, her damage a second on it (her bolt's landed expectation on its kind over the bolt's seconds) and its damage a second on her (the measured or
/// derived per-kind rate, the losing race's own read, shared among the ones of its kind on her).
/// </summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Name">Its name.</param>
/// <param name="Health">Its health left.</param>
/// <param name="HerDamagePerSecond">Her damage a second on it (0: her bolts do nothing to it, or nothing is read).</param>
/// <param name="ThreatPerSecond">Its damage a second on her.</param>
/// <param name="ThreatWhy">How the threat was read, for the log.</param>
public readonly record struct SwitchSide(uint Guid, string Name, double Health, double HerDamagePerSecond, double ThreatPerSecond, string ThreatWhy)
{
    /// <summary>The seconds her bolts need to kill it: its health over her damage a second; infinity when her bolts do nothing to it.</summary>
    public double KillSeconds => HerDamagePerSecond > 0 ? Math.Max(0, Health) / HerDamagePerSecond : double.PositiveInfinity;
}

/// <summary>The rule's answer: keep the current target or switch to the attacker, the health she is expected to lose under each order, and the line for the log.</summary>
/// <param name="Keep">True: she finishes the current target first.</param>
/// <param name="TakenKeeping">The health she loses killing the current target first, then the attacker.</param>
/// <param name="TakenSwitching">The health she loses killing the attacker first, then the current target.</param>
/// <param name="Line">The decision with its numbers.</param>
public readonly record struct SwitchAnswer(bool Keep, double TakenKeeping, double TakenSwitching, string Line);

/// <summary>
/// M6 14 tactics (live ladder56 2026-11-17 08:33:02.349 UTC, the Crater Caves mote hunt: Qwinnie's Whirling Blade VI had just left Cursed Wisp 0x800050F6 at 46 of 225 when a second wisp, 0x800051C0, harmed her for 18; the
/// self-defence rule took the attacker at full health and left the hurt one to bolt her; she recalled at 85 of 219 seven seconds later). A general rule for the order she kills two creatures in, both hurting her while either
/// lives: killing the current target first costs her its kill seconds at both threats and then the attacker's kill seconds at its own; switching costs the reverse. She takes the order that costs less health:
/// keep while <c>threat(current) / kill(current) &gt;= threat(attacker) / kill(attacker)</c> (the exchange argument of scheduling to the least weighted completion time; a tie keeps, no turn is spent). So a nearly dead target of
/// the same kind is finished first, and a caster bolting her for 40 is taken over a slow melee golem at full health. Unknowns never hide behind the rule: a current target her bolts do not hurt is left for the attacker (today's
/// self-defence), and an attacker her bolts do not hurt does not take her off a target she can kill. Engine-free, allocation only for the log line.
/// </summary>
public static class TargetSwitch
{
    /// <summary>Decides between the current target and a new attacker.</summary>
    /// <param name="current">The current target, alive, in range with a clear line.</param>
    /// <param name="attacker">The creature that attacked her.</param>
    public static SwitchAnswer Decide(in SwitchSide current, in SwitchSide attacker)
    {
        var (tc, ta) = (current.KillSeconds, attacker.KillSeconds);
        var (dc, da) = (Math.Max(0, current.ThreatPerSecond), Math.Max(0, attacker.ThreatPerSecond));
        var keeping = (tc * (dc + da)) + (ta * da);
        var switching = (ta * (da + dc)) + (tc * dc);
        bool keep;
        string why;
        if (double.IsPositiveInfinity(tc))
        {
            (keep, why) = (false, "her bolts do nothing to the current target: the attacker is taken (self-defence)");
        }
        else if (double.IsPositiveInfinity(ta))
        {
            (keep, why) = (true, "her bolts do nothing to the attacker: the current target, which she can kill, is finished");
        }
        else
        {
            keep = keeping <= switching; // dc*ta >= da*tc
            why = keep ? "finishing the current target costs her no more health" : "the attacker is the bigger threat for the seconds it takes to kill";
        }

        var losses = double.IsFinite(keeping) && double.IsFinite(switching)
            ? string.Create(CultureInfo.InvariantCulture, $"she loses {Fmt(keeping)} finishing 0x{current.Guid:X8} first against {Fmt(switching)} taking 0x{attacker.Guid:X8} first")
            : "the losses cannot be compared";
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"target switch: {(keep ? "keep" : "switch to")} {(keep ? Words(in current) : Words(in attacker))} over {(keep ? Words(in attacker) : Words(in current))}: {losses}; {why}");
        return new SwitchAnswer(keep, keeping, switching, line);
    }

    private static string Words(in SwitchSide s) => string.Create(
        CultureInfo.InvariantCulture,
        $"'{s.Name}' 0x{s.Guid:X8} ({s.Health:0} left, her {s.HerDamagePerSecond:0.#}/s on it: dies in {Fmt(s.KillSeconds)}{(double.IsFinite(s.KillSeconds) ? " s" : string.Empty)}; {s.ThreatPerSecond:0.#}/s on her: {s.ThreatWhy})");

    private static string Fmt(double v) => double.IsPositiveInfinity(v) ? "never" : double.IsNaN(v) ? "unread" : v.ToString("0.#", CultureInfo.InvariantCulture);
}
