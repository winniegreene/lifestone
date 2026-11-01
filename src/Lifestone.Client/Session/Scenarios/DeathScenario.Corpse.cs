using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The corpse trip's constants and accessors (M4 2.2 s1, D5; M5 3 d2, D51). The trip itself is the death handler's (<see cref="DeathHandler"/>, steps <c>route</c> to <c>wear</c>): the runner or Primary Portal Recall to the ground, the
/// runner to the corpse (<c>purpose=corpse</c>), the take-all, the items worn and wielded again. M4 2.2's place-based trip (<c>corpse-teleporting</c> to <c>corpse-return-settle</c>, its guard and aborts) is removed.
/// </summary>
public sealed partial class DeathScenario
{
    /// <summary>
    /// ACE's least time a player corpse that holds items lives, in seconds (@95ef426d <c>Corpse.cs</c> 103-118, line 110: <c>RecalculateDecayTime</c>: <c>max(3600, level x 300)</c> seconds with items, 15 s empty; the floor is the safe bound for any level).
    /// A corpse older than this may be gone, so the trip is not made.
    /// </summary>
    public const double PlayerCorpseMinRotSeconds = 3600;

    /// <summary>The limit on the whole trip, in seconds.</summary>
    public const double TripSeconds = 90;

    /// <summary>How long after the arrival the corpse may take to be in the object table, in seconds (ACE re-creates it for the client at the arrival).</summary>
    public const double CorpseFindSeconds = 15;

    /// <summary>M4 P5b: the object range the corpse trip's travel asks for (a run stops within it of the corpse). TODO(m4-corpse-run-range): a first value until the lead's live gate reads the run's real stop.</summary>
    public const double CorpseTravelRangeMetres = 2.0;

    private DeathLegMeasures? _atRespawn;

    /// <summary>What the trip came to (<c>corpse_looted=</c>): skipped until the handler's trip ends; <see cref="CorpseLoot.Yes"/> only when every entry of the opened view came back.</summary>
    public CorpseLoot CorpseLooted =>
        _handler is { } handler ? handler.Corpse switch
        {
            DeathCorpse.Yes => CorpseLoot.Yes,
            DeathCorpse.Partial => CorpseLoot.Partial,
            DeathCorpse.No => CorpseLoot.No,
            _ => CorpseLoot.Skipped,
        }
        : CorpseLoot.Skipped;

    /// <summary>The items the trip took out of the corpse (<c>items_recovered=</c>).</summary>
    public int ItemsRecovered => _handler?.ItemsBack ?? 0;

    /// <summary>The entries the corpse's opened view listed (0 before an open): what <c>items_recovered</c> is measured against.</summary>
    public int CorpseEntries => _handler?.CorpseEntries ?? 0;

    /// <summary>Why the trip aborted (<c>trip_abort=</c>), or <see cref="DeathLegVerdict.TripAbortNone"/>.</summary>
    public string TripAbort => _handler?.TripAbort ?? DeathLegVerdict.TripAbortNone;

    /// <summary>The seconds the trip took from its start (to now while it runs), or null when no trip began (<c>trip_s=</c>).</summary>
    public double? TripElapsedSeconds => _handler?.TripSeconds;

    /// <summary>
    /// True from the moment the corpse's open is confirmed and its contents are listed until its close is sent (the handler's step <c>corpse-loot</c>): the game's leg takes the <c>corpse</c> review frame then and holds the scenario (a held
    /// scenario sends no pick-up), so the container window shows the contents.
    /// </summary>
    public bool CorpseOpen => Stage == HandlerStagePrefix + "corpse-loot";

    /// <summary>True while the trip is under way (the handler's route, recall, runner, trip-heal, corpse and wear steps).</summary>
    public bool TripRunning => _handler is not null && Stage.StartsWith(HandlerStagePrefix, StringComparison.Ordinal) && _handler.CurrentStep is "route" or "recall" or "runner" or "trip-heal" or "corpse-walk" or "corpse-seek" or "corpse-open" or "corpse-loot" or "corpse-close" or "wear";

    internal static TeleportDestination CorpseDestination(CellFrame at)
    {
        var heading = WorldLocations.HeadingFromQuaternion(at.Frame.Orientation.W, at.Frame.Orientation.Z);
        const string Name = "the mage's corpse";
        return new TeleportDestination(Name, [], new WorldLocation(Name, new CellPosition(at.CellId, at.Frame.Origin), true, heading), "the death loop's remembered corpse position (M4 2.2 s1)");
    }

    private DeathLegMeasures TallyMeasures(IGameSessionView session) =>
        DeathLegVerdict.Measure(_tally, session.State.Objects, _suicides, false, 0, double.IsNaN(_deathStart) ? null : DeathSeconds, null, null, BoundLifestone);
}
