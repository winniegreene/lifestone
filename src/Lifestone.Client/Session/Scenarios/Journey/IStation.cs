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
