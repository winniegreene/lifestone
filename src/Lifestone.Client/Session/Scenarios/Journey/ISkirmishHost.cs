using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 Q7 (docs/automation-engine.md section 2, "one combat entry": the skirmish): what only the caller of a <see cref="Skirmish"/> knows. The skirmish casts, picks its bolt, waits on it, reaches, strikes with Life Magic and dances under
/// fire by itself; the host says which creatures it fights and leaves, when the run past is judged again, when a retreat is due, where the lines go, and lends its hands (the cast policy, the modes, its waits). The travel leg is the
/// first host (<c>TravelLegSkirmishHost.cs</c>).
/// </summary>
internal interface ISkirmishHost
{
    // ------------------------------------------------------------------ where it runs and logs

    /// <summary>The session the host plays in.</summary>
    IGameSessionView Session { get; }

    /// <summary>The journey's context (the spell table, the dats' readers).</summary>
    JourneyContext Context { get; }

    /// <summary>The host's attack watch (the hits for the dance under fire and the line's tally), or null when none runs.</summary>
    AttackWatch? Watch { get; }

    /// <summary>The bestiary's read of a creature kind for the host (named once a kind it does not hold).</summary>
    LegKindRead Kinds { get; }

    /// <summary>
    /// M6 16 Q8: the account's name as the server stores it, for the bolt pick's pack judgement on the account's formula when the pack holds no War Magic focus (<see cref="Mage.LevelFloor.HeldForAccount"/>), or null: a pack without the
    /// focus is then not judged before the cast (the travel leg's and the quest steps' answer, as before). Hashed by the pick, never logged.
    /// </summary>
    string? AccountName => null;

    /// <summary>
    /// M6 16 Q8: true when a bolt's damage or drain line of 0 points counts as a bolt that came to nothing (toward Life Magic, then the leaving), as the quest steps' kill counted it (live ladder64: a Gout singed for 0 points 50 times);
    /// false (the travel leg's, as before): any damage, drain or resist line resolves the bolt.
    /// </summary>
    bool ZeroPointsComeToNothing => false;

    /// <summary>Writes one line to the host's log.</summary>
    /// <param name="text">The line.</param>
    void Log(string text);

    // ------------------------------------------------------------------ the host's hands

    /// <summary>Waits until <paramref name="done"/> holds or <paramref name="max"/> seconds passed (the host's clock, held seconds excluded).</summary>
    /// <param name="done">The end test.</param>
    /// <param name="max">The bound in seconds.</param>
    IEnumerable<ActionHandle?> Until(Func<bool> done, double max);

    /// <summary>A cast through the host's cast policy, <paramref name="result"/> holding the handle and the chat mark before it.</summary>
    /// <param name="spell">The spell.</param>
    /// <param name="target">The target, 0 for none.</param>
    /// <param name="result">The handle and marks.</param>
    IEnumerable<ActionHandle?> Cast(uint spell, uint target, TravelLeg.CastResult result);

    /// <summary>The first refusal code in the chat since <paramref name="mark"/>, or 0.</summary>
    /// <param name="mark">The chat mark.</param>
    uint RefusalSince(int mark);

    /// <summary>To the magic mode.</summary>
    IEnumerable<ActionHandle?> MagicNow();

    /// <summary>To peace.</summary>
    IEnumerable<ActionHandle?> PeaceNow();

    /// <summary>Fails the host by name (throws).</summary>
    /// <param name="name">The failure's name.</param>
    /// <param name="detail">What was seen.</param>
    void Fail(string name, string detail);

    /// <summary>Where the client draws her, or null when unread.</summary>
    CellFrame? WhereSheStands();

    // ------------------------------------------------------------------ which creatures it fights and leaves

    /// <summary>The nearest attacker to fight now (the ones the host leaves skipped), false when none.</summary>
    /// <param name="guid">Its guid.</param>
    /// <param name="name">Its name.</param>
    /// <param name="metres">Its distance.</param>
    bool NextAttacker(out uint guid, out string name, out double metres);

    /// <summary>The skirmish leaves <paramref name="guid"/>: for good (<paramref name="forRange"/> false) or until it attacks her again from within <paramref name="reach"/>.</summary>
    /// <param name="guid">The creature.</param>
    /// <param name="name">Its name.</param>
    /// <param name="forRange">True when left for standing beyond her reach.</param>
    /// <param name="why">Why, in words.</param>
    /// <param name="reach">Her reach it is fought again from.</param>
    void Leave(uint guid, string name, bool forRange, string why, double reach);

    /// <summary>The words the target line carries when the target changes (the host publishes its health where it does).</summary>
    /// <param name="guid">The target.</param>
    /// <param name="name">Its name.</param>
    string NewTarget(uint guid, string name);

    /// <summary>The last attack heard on her, in words.</summary>
    string LastAttackWord();

    /// <summary>Why the skirmish ends, in words: what is still on her and why none is fought now.</summary>
    /// <param name="onTheRun">True when the run past named the set at its start.</param>
    string EndWhy(bool onTheRun);

    // ------------------------------------------------------------------ the run past

    /// <summary>True when the run past named the set to fight (the skirmish then asks <see cref="RunPastAgain"/> when the set is spent).</summary>
    bool OnTheRun { get; }

    /// <summary>The run past judged again where she stands: the words of the first she must fight, or null when none.</summary>
    string? RunPastAgain();

    // ------------------------------------------------------------------ when a retreat is due

    /// <summary>True when a retreat is due.</summary>
    bool RetreatIsDue { get; }

    /// <summary>Sets the retreat due.</summary>
    /// <param name="why">Its reason's token.</param>
    /// <param name="detail">What was seen.</param>
    void RetreatDue(string why, string detail);

    /// <summary>The retreat line in words when her health is under it, else null.</summary>
    /// <param name="health">Her health in words.</param>
    string? HealthUnderLine(out string health);

    /// <summary>A cast, a kill or the dance's mana the host counts (the crossing's measure).</summary>
    /// <param name="what">Which.</param>
    /// <param name="netted">The mana a dance cast netted.</param>
    void Counted(SkirmishTally what, double netted = 0);

    /// <summary>The host's measure gives the way on up, in words, or null.</summary>
    string? GivesUp();
}

/// <summary>What a <see cref="Skirmish"/> hands its host to count (<see cref="ISkirmishHost.Counted"/>).</summary>
internal enum SkirmishTally
{
    /// <summary>A cast at an attacker.</summary>
    Cast,

    /// <summary>An attacker killed.</summary>
    Kill,

    /// <summary>A cast of the dance under fire, with the mana it netted.</summary>
    DanceCast,
}
