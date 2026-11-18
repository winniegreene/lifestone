namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>Where a station must stand before its first step (M4 P5b, <c>cache/m4-p5-design.md</c> 3.4).</summary>
public enum StationWhere
{
    /// <summary>Wherever the previous station left the player.</summary>
    Here,

    /// <summary>Holtburg (landblock 0xA9B4): the lifestone, the shops, the chests.</summary>
    Holtburg,

    /// <summary>Inside the Black Spawn Den (landblock 0x0105, the den portal's destination).</summary>
    Den,

    /// <summary>The Low B belt camp (the Brawler's ground).</summary>
    Belt,

    /// <summary>The forest (the Archer's ground).</summary>
    Forest,
}

/// <summary>
/// What a station needs of the character before its first step (M4 P5b 3.4). The station adapter (<c>ScenarioStation</c>, J1) asserts it
/// against the session's state on the first step: a miss is <c>why=precondition:&lt;x&gt;</c>, never a silent place.
/// The journey restores what it can before the station (the vitals keeper, the travel seam) and skips the station with
/// <c>skipped=precondition:&lt;x&gt;</c> when it cannot.
/// </summary>
/// <param name="Where">Where the player must be.</param>
/// <param name="Peace">True when the station must start in peace (non-combat) mode.</param>
/// <param name="MinHealthPercent">The least health, in percent of the maximum.</param>
/// <param name="MinStaminaPercent">The least stamina, in percent.</param>
/// <param name="MinManaPercent">The least mana, in percent.</param>
/// <param name="Kits">The least number of healing kits held.</param>
/// <param name="Potions">The least number of health potions held.</param>
/// <param name="Buffed">True when the station needs the character's buff profile active.</param>
public sealed record StationNeeds(
    StationWhere Where = StationWhere.Here,
    bool Peace = false,
    double MinHealthPercent = 0,
    double MinStaminaPercent = 0,
    double MinManaPercent = 0,
    int Kits = 0,
    int Potions = 0,
    bool Buffed = false)
{
    /// <summary>No needs: anywhere, any state.</summary>
    public static StationNeeds None { get; } = new();
}

/// <summary>
/// M6 7 BD: a station that can say it stands in a wait: nothing in flight, nothing cast, only time passing (a mana wait, a rest in place for a cast). The autopilot's one rule (<c>AutopilotScenario.KeeperStands</c>) ends such a wait at once on a
/// ground when a hostile is on her, fights back, and runs the state's station again from its start.
/// </summary>
public interface IStationWait
{
    /// <summary>What it waits for (one clause, logged), or null while it does not wait.</summary>
    string? Waiting { get; }
}

/// <summary>M6 8 K: a station in a step the survival layer must not cut: a portal's Use has been sent and the server is walking her to it, so a cast or a sit would cancel the hop. The layer stands down while it says so.</summary>
public interface IStationCommitted
{
    /// <summary>What it is committed to (one clause, logged), or null.</summary>
    string? Committed { get; }
}

/// <summary>M6 8 K: a station whose own time limits do not count a survival hold: the layer says how long it held the station when it lets it go, so a wait that was under way when the hold began has as long left as it had.</summary>
public interface IStationHeld
{
    /// <summary>The station was not stepped for <paramref name="seconds"/>.</summary>
    /// <param name="seconds">The seconds held.</param>
    void NoteHeld(double seconds);
}

/// <summary>
/// M6 14 entry recharge: a station that stands still while it works (the rung change's recharge at an entry's front and its top-up before a hostile drop: the bars rising by a press, a drink or a rest, a fight-back answered): the session time of
/// its last sign of work, which the autopilot's watchdog reads as the state's progress, as it reads a moving body. A recharge that stops filling still trips it; the recharge's own bounds end it first.
/// </summary>
public interface IStationWorking
{
    /// <summary>The session time of the last sign of work, or null while none.</summary>
    double? WorkedAt { get; }
}

/// <summary>
/// One running station: an embedded scenario (no login, no place of its own, no leave: <c>endSession:false</c>) that
/// the journey steps until it ends, then reads its verdict.
/// </summary>
public interface IStationRun : IScenario
{
    /// <summary>The station's verdict fields after it ended, space-separated <c>key=value</c> with today's leg names (e.g. <c>kills=3 looted=2</c>).</summary>
    string VerdictFields();

    /// <summary>True when the station passed by its leg's verdict rules (read after it ended).</summary>
    bool Ok { get; }
}

/// <summary>
/// A station of a journey (M4 P5b 3.4): a named piece of a test that runs embedded in the one session of a character.
/// Stations are data in a <c>JourneyPlan</c>; the journey creates each station's run when it reaches it.
/// </summary>
public interface IStation
{
    /// <summary>The station's name, the verdict line's <c>station=</c> (lower-case, hyphens: <c>harm-heal</c>).</summary>
    string Name { get; }

    /// <summary>What the station needs before its first step.</summary>
    StationNeeds Needs { get; }

    /// <summary>Creates the station's run (embedded: no login, no place, no leave).</summary>
    /// <param name="context">The journey's context (log, keeper, travel, facts).</param>
    IStationRun Start(JourneyContext context);
}
