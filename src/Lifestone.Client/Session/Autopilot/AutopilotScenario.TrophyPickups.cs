using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): the moment each tusk is first seen in the pack, so its quest's 20 h run from the pickup the server stamps, not from the turn-in (<see cref="TrophyPickups"/>).
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>How often the pack is read for the tusks (Lifestone's own): a second's error on a 20 h timer, never early.</summary>
    private const double TrophyPickupWatchSeconds = 1;

    private double _trophyPickupAt = double.NegativeInfinity;
    private QuestThing[]? _pickupItems;
    private readonly List<uint> _pickupScratch = [];

    /// <summary>Each second, in any state of the level up job: each pickup-timed trophy held for the first time is noted with the session's clock, one line each; one no longer held is forgotten.</summary>
    private void TrophyPickupWatch(IGameSessionView session)
    {
        if (_options.Job != AutopilotJob.LevelUp || _clock - _trophyPickupAt < TrophyPickupWatchSeconds)
        {
            return;
        }

        _trophyPickupAt = _clock;
        _pickupItems ??= [.. (TrophyQuests ?? QuestCatalog.All).Select(TrophyPickups.PickupItem).OfType<QuestThing>().DistinctBy(t => t.Wcid)];
        if (_pickupItems.Length == 0)
        {
            return;
        }

        var pickups = TrophyPickups.For(session.State);
        var now = session.Clock.Time.GetUtcNow();
        foreach (var item in _pickupItems)
        {
            if (pickups.Watch(item.Wcid!.Value, GearInventory.CountOf(session.State, _pickupScratch, item.Wcid.Value), now))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"trophy: {item} first seen in the pack at {now:yyyy-MM-ddTHH:mm:ssZ}: its quest's timer runs from here, the server's stamp at the pickup (timer_from pickup, m6-audit-tusk-pickup)"));
            }
        }
    }
}
