using System.Globalization;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 sprint (the owner's decision of 2026-11-18: the vitae burnt on a known-good ground first, the corpse after): after a death on the ladder she does not go straight back to the ground that killed her. The death handler re-arms (a pack caster, else the kit's bought one),
/// re-wears, replaces and re-buffs as before; then, while she carries vitae, the corpse is owed instead of fetched (<c>corpse_recovered=deferred trip_abort=vitae-first</c>), the rung she died on and the ground that holds the corpse are held
/// (<see cref="HeldWhy"/>, read by <see cref="RungOwner"/> as <c>vitae-held:&lt;rung&gt;</c>), and the ladder hunts the rung below, a ground she has already climbed through. The hold is released when the vitae is gone (the vitae
/// enchantment's multiplier reads 1, <see cref="Contracts.EffectRules.VitaeValue"/>) or when the corpse's rot is near (<see cref="RotMarginSeconds"/> before ACE's rot time); the ladder then takes her rung again, and the run on the
/// corpse's ground enters the death station again with the death's tally (<see cref="TakeFor"/>), which fetches the corpse by its usual rules. One object a process, carried on <see cref="ApproachHolds.Vitae"/> across the
/// ladder's restarts; a new process forgets the corpse (<c>TODO(m6-vitae-burn-restart)</c>, logged at the run's start when one was owed is not knowable: the record is in memory only).
/// </summary>
public sealed class VitaeBurn
{
    /// <summary>
    /// How long before ACE's rot time the burn gives way to the corpse trip, in seconds. Lifestone's own: two of the death state's watchdog (900 s, <see cref="AutopilotWatchdogs"/>): one for the move back to the rung, one for the
    /// trip itself.
    /// </summary>
    public const double RotMarginSeconds = 1800;

    private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);

    /// <summary>The corpse owed, or null.</summary>
    public VitaeOwed? Owed { get; private set; }

    /// <summary>True while the holds stand (an owed corpse whose burn is not over).</summary>
    public bool Burning => Owed is { Released: false };

    /// <summary>Why <paramref name="key"/> (a rung's key or a ground's key) is held for the burn, or null.</summary>
    /// <param name="key">The key.</param>
    public string? HeldWhy(string key) => _held.TryGetValue(key, out var why) ? why : null;

    /// <summary>The corpse's rot time after a death at <paramref name="level"/> (<see cref="DeathScenario.PlayerCorpseRotSeconds"/>).</summary>
    /// <param name="level">Her level at the death.</param>
    public static double RotSeconds(int level) => DeathScenario.PlayerCorpseRotSeconds(level);

    /// <summary>The session clock at which the burn gives way to the corpse: the rot time less <see cref="RotMarginSeconds"/>.</summary>
    /// <param name="diedAt">The session clock of the corpse's appearance.</param>
    /// <param name="level">Her level at the death.</param>
    public static double FetchBy(double diedAt, int level) => diedAt + RotSeconds(level) - RotMarginSeconds;

    /// <summary>Owes the corpse and holds its rung and ground. A corpse owed already is replaced (the newer death's; the older is named lost by the caller), and the holds already standing stay: a death during the burn sends her lower still.</summary>
    /// <param name="owed">The corpse.</param>
    public void Defer(VitaeOwed owed)
    {
        ArgumentNullException.ThrowIfNull(owed);
        Owed = owed;
        var why = string.Create(CultureInfo.InvariantCulture, $"the vitae burn after the death on {owed.GroundKey} (rung {owed.RungKey})");
        _held.TryAdd(owed.RungKey, why);
        _held.TryAdd(owed.GroundKey, why);
    }

    /// <summary>Why the burn is over, or null while it goes on: the vitae is gone, or the rot is near.</summary>
    /// <param name="vitae">The vitae multiplier (1 with none).</param>
    /// <param name="now">The session clock.</param>
    public string? DueWhy(float vitae, double now)
    {
        if (Owed is not { Released: false } owed)
        {
            return null;
        }

        if (vitae >= 1f)
        {
            return "the vitae is gone";
        }

        return now >= owed.FetchBy
            ? string.Create(CultureInfo.InvariantCulture, $"the corpse rots at {owed.RotAt:0} s on the session clock (ACE's max(3600, level x 300) at level {owed.LevelAtDeath}) and the burn gives way {RotMarginSeconds:0} s before it; the vitae is {Contracts.EffectRules.VitaePercentage(vitae)}% still")
            : null;
    }

    /// <summary>Ends the burn: the holds are released and the corpse stays owed until a run on its ground takes it (<see cref="TakeFor"/>).</summary>
    public void Release()
    {
        _held.Clear();
        if (Owed is { } owed)
        {
            owed.Released = true;
        }
    }

    /// <summary>The owed corpse's tally when the burn is over and <paramref name="holds"/> says the corpse's cell is on the run's ground; the record is then cleared. Null otherwise.</summary>
    /// <param name="holds">True when the run's ground holds the cell.</param>
    public DeathTally? TakeFor(Func<uint, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);
        if (Owed is not { Released: true } owed || !holds(owed.CorpseCell))
        {
            return null;
        }

        Owed = null;
        return owed.Tally;
    }

    /// <summary>Forgets the corpse (rotted, or given up by name) and any hold.</summary>
    public void Forget()
    {
        _held.Clear();
        Owed = null;
    }
}

/// <summary>An owed corpse (<see cref="VitaeBurn"/>).</summary>
/// <param name="Tally">The death's tally: the corpse's guid, place and time (no longer listening).</param>
/// <param name="CorpseCell">The corpse's cell.</param>
/// <param name="GroundKey">The ground that holds the corpse (or the run's ground, for a corpse on its approach).</param>
/// <param name="RungKey">The rung she died on.</param>
/// <param name="LevelAtDeath">Her level at the death (the rot's).</param>
/// <param name="DiedAt">The session clock of the corpse's appearance.</param>
public sealed record VitaeOwed(DeathTally Tally, uint CorpseCell, string GroundKey, string RungKey, int LevelAtDeath, double DiedAt)
{
    /// <summary>ACE's rot time on the session clock.</summary>
    public double RotAt => DiedAt + VitaeBurn.RotSeconds(LevelAtDeath);

    /// <summary>When the burn gives way to the corpse (<see cref="VitaeBurn.FetchBy"/>).</summary>
    public double FetchBy => VitaeBurn.FetchBy(DiedAt, LevelAtDeath);

    /// <summary>True once the burn is over and the corpse waits for a run on its ground.</summary>
    public bool Released { get; set; }
}
