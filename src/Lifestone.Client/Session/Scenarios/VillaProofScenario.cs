using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World.Data;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The villa track's live proof (M6 villa, step 4): on <c>Lifestone Throwaway</c> (account test-tools, the one character that may be levelled
/// and granted freely; never Qwinnie), buy apartment 4051 through the guardian window, pay its rent, recall home from Holtburg, and move an
/// item into the apartment's storage chest and back out. Every step is checked before and after and logs its numbers.
/// </summary>
/// <remarks>
/// <para><b>What is granted, and how</b> (M6 D10's "granting for testing continues", the test-tools character only; the mod's verbs, the
/// gear scenario's grant path, M5 D146): experience to level 20 (the apartment guardian's MinLevel, ACE <c>slumlordapartment</c> 15608 PropertyInt
/// 86), pyreals to 115,000 (the price 100,000 plus the rent 10,000 plus margin), one Writ of Refuge (11710) and one Handy Healing Kit (628), the
/// item stored; and two placements (<c>place loc</c>): onto the guardian's own spot (where ACE's house recall lands a player,
/// <c>HandleActionTeleToHouse</c>) and, after the recall proof, one metre from the storage chest (the headless body does not follow ACE's walk-to for a Use: live 2026-11-16 17:07, the Use from 4.4 m ended with ACE's UseDone after 15 s and no contents; walking to the chest is the storage trip's job). The purchase, the rent, the recall and the
/// chest moves are a player's own actions through the guardian window (<see cref="GuardianWindowModel"/>), <c>/house recall</c> and the chest.</para>
/// <para><b>The apartment</b> (read-only SELECTs, 2026-11-16): house 0x77200005 (houseapartment4051) in landblock 0x7200, guardian 0x77200007 at cell
/// 0x7200018C (90, -137.984, 0), storage chest 0x77200001 at cell 0x72000189 (93.72, -126.26, 0). Buy 100,000 pyreals + 1 Writ of Refuge; rent
/// 10,000 pyreals per 90 days. A re-run on a character that already owns it skips the purchase and says so.</para>
/// </remarks>
public sealed class VillaProofScenario : IScenario
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "villa-proof";

    /// <summary>The only character the proof runs on.</summary>
    public const string CharacterName = "Lifestone Throwaway";

    /// <summary>The apartment's guardian.</summary>
    public const uint Guardian = 0x77200007;

    /// <summary>The apartment's storage chest.</summary>
    public const uint Chest = 0x77200001;

    /// <summary>The apartment's landblock.</summary>
    public const uint ApartmentLandblock = 0x7200;

    private const string LogCategory = "villa";
    private const uint PyrealWcid = 273;
    private const uint WritWcid = 11710;
    private const uint KitWcid = 628;
    private const int TargetLevel = 20;
    private const int PyrealsWanted = 115_000;
    private const int PyrealStack = 25_000;
    private const double StepSeconds = 45;

    private static readonly TeleportDestination GuardianSpot = Spot("villa proof: apartment 4051's guardian", 0x7200018C, new Vector3(90f, -137.984f, 0f));
    private static readonly TeleportDestination ChestRoom = Spot("villa proof: apartment 4051's storage chest", 0x72000189, new Vector3(93.02f, -126.96f, 0f));

    private readonly ulong _xpForTarget;
    private readonly ILogSink _log;
    private readonly TeleportDestination _holtburg;
    private readonly List<string> _record = [];
    private string _stage = "starting";
    private double _stageStart = double.NaN;
    private ActionHandle? _pending;
    private ActionHandle? _tool;
    private uint _kit;
    private bool _alreadyOwned;

    /// <summary>A proof run.</summary>
    /// <param name="characterLevelXp">The dat's cumulative experience by level (<c>XpTableData.CharacterLevelXpList</c>).</param>
    /// <param name="log">The log.</param>
    public VillaProofScenario(IReadOnlyList<ulong> characterLevelXp, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(characterLevelXp);
        ArgumentNullException.ThrowIfNull(log);
        _xpForTarget = characterLevelXp[TargetLevel];
        _log = log;
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(static p => p.DisplayName == "Holtburg"));
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage => _stage;

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    public long Unmatched => 0;

    /// <summary>The proof's record: one line per checked step.</summary>
    public IReadOnlyList<string> Record => _record;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (double.IsNaN(_stageStart))
        {
            _stageStart = now;
        }

        if (now - _stageStart > StepSeconds * (_stage is "recall" or "away" ? 2 : 1))
        {
            return Fail("the stage " + _stage + " did not finish within its limit" + (_pending?.Outcome is { } o ? " (last outcome " + o.Kind + ": " + o.Detail + ")" : string.Empty));
        }

        var state = session.State;
        if (state.Self.Travel != SelfTravel.Present)
        {
            return ScenarioStatus.Running;
        }

        return _stage switch
        {
            "starting" => Starting(session),
            "level" => Level(session),
            "grants" => Grants(session),
            "query" => Query(session),
            "to-guardian" => Arrive(session, GuardianSpot, ApartmentLandblock, "open-guardian"),
            "open-guardian" => OpenGuardian(session),
            "buy" => Buy(session),
            "owned" => Owned(session),
            "rent" => Rent(session),
            "away" => Arrive(session, _holtburg, _holtburg.Location.Position.CellId >> 16, "recall"),
            "recall" => Recall(session),
            "to-chest" => Arrive(session, ChestRoom, ApartmentLandblock, "open-chest"),
            "open-chest" => OpenChest(session),
            "put" => Put(session),
            "take" => Take(session),
            "close" => Close(session),
            _ => Fail("unknown stage " + _stage),
        };
    }

    private ScenarioStatus Starting(IGameSessionView session)
    {
        if (!string.Equals(session.State.Self.Name, CharacterName, StringComparison.Ordinal))
        {
            return Fail("the proof runs on " + CharacterName + " only, not " + (session.State.Self.Name ?? "nobody"));
        }

        Note("in the world as " + CharacterName + " level " + session.State.Self.Qualities.Level.ToString(CultureInfo.InvariantCulture));
        return Go("level");
    }

    private ScenarioStatus Level(IGameSessionView session)
    {
        var level = session.State.Self.Qualities.Level;
        if (level >= TargetLevel)
        {
            Note("level " + level.ToString(CultureInfo.InvariantCulture) + " (the apartment asks " + TargetLevel.ToString(CultureInfo.InvariantCulture) + ")");
            _tool = null;
            return Go("grants");
        }

        if (_tool is null)
        {
            var total = session.State.Self.Qualities.Int64s.TryGetValue(PlayerQualities.TotalExperienceProperty, out var t) ? t : 0;
            var add = (long)_xpForTarget - total;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"grant (M6 D10, test-tools only): {add:N0} experience to reach level {TargetLevel} (total {total:N0} of {_xpForTarget:N0})"));
            _tool = session.Tools.AddXp(Math.Max(1, add), ActionSource.Host);
        }

        return _tool.Outcome is { Kind: not ActionOutcomeKind.Confirmed } bad ? Fail("the experience grant failed: " + bad.Kind + " " + bad.Detail) : ScenarioStatus.Running;
    }

    private ScenarioStatus Grants(IGameSessionView session)
    {
        if (_tool is { IsCompleted: false })
        {
            return ScenarioStatus.Running;
        }

        if (_tool?.Outcome is { Kind: not ActionOutcomeKind.Confirmed } bad)
        {
            return Fail("an item grant failed: " + bad.Kind + " " + bad.Detail);
        }

        var held = GearInventory.CountAll(session.State, out var unsettled);
        if (unsettled > 0)
        {
            return ScenarioStatus.Running;
        }

        var pyreals = held.GetValueOrDefault(PyrealWcid);
        (uint Wcid, int Count)? want = pyreals < PyrealsWanted ? (PyrealWcid, Math.Min(PyrealStack, PyrealsWanted - pyreals))
            : held.GetValueOrDefault(WritWcid) < 1 ? (WritWcid, 1)
            : held.GetValueOrDefault(KitWcid) < 1 ? (KitWcid, 1)
            : null;
        if (want is { } w)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"grant (M6 D10, test-tools only): weenie {w.Wcid} x{w.Count} (held {held.GetValueOrDefault(w.Wcid)})"));
            _tool = session.Tools.SpawnItem(w.Wcid, w.Count, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        _kit = FirstHeld(session.State, KitWcid);
        Note(string.Create(CultureInfo.InvariantCulture, $"held: {pyreals:N0} pyreals, {held.GetValueOrDefault(WritWcid)} writ, kit 0x{_kit:X8}"));
        _tool = null;
        _pending = session.Actions.QueryHouse(ActionSource.Host);
        return Go("query");
    }

    private ScenarioStatus Query(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        if (done.Kind != ActionOutcomeKind.Confirmed)
        {
            return Fail("the house query got no answer: " + done.Kind + " " + done.Detail);
        }

        _alreadyOwned = session.State.HouseStatus == HouseOwnership.Owned;
        Note("house before: " + (_alreadyOwned ? "owned (an earlier run): the purchase is skipped" : "none"));
        _pending = session.Tools.Place(GuardianSpot, ActionSource.Host);
        return Go("to-guardian");
    }

    private ScenarioStatus Arrive(IGameSessionView session, TeleportDestination where, uint landblock, string next)
    {
        if (_pending is { Outcome: { Kind: not ActionOutcomeKind.Confirmed } bad })
        {
            return Fail("placing at " + where.Name + ": " + bad.Kind + " " + bad.Detail);
        }

        // Arrived means the place was confirmed AND the body stands within 5 m (horizontally) of the spot in its landblock: a landblock alone is
        // not enough when the player is already in it (live 2026-11-16 17:05: the chest was opened from the guardian before the place landed).
        var spot = where.Location.Position;
        if (_pending is not { Outcome.Kind: ActionOutcomeKind.Confirmed }
            || session.State.Self.Position is not { } at
            || (at.CellId >> 16) != landblock
            || Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(spot.Local.X, spot.Local.Y)) > 5)
        {
            return ScenarioStatus.Running;
        }

        Note(string.Create(CultureInfo.InvariantCulture, $"at {where.Name}: cell 0x{at.CellId:X8} ({at.Origin.X:0.0}, {at.Origin.Y:0.0}, {at.Origin.Z:0.0})"));
        _pending = next switch
        {
            "open-guardian" => session.Actions.Use(Guardian, ActionSource.Host),
            "recall" => session.Actions.RecallHouse(ActionSource.Host),
            "open-chest" => session.Actions.OpenContainer(Chest, ActionSource.Host),
            _ => null,
        };
        return Go(next);
    }

    private ScenarioStatus OpenGuardian(IGameSessionView session)
    {
        if (session.State.Guardian is not { } open || open.Guardian != Guardian)
        {
            return _pending is { Outcome: { Kind: not ActionOutcomeKind.Confirmed } bad } ? Fail("the guardian's Use: " + bad.Kind + " " + bad.Detail) : ScenarioStatus.Running;
        }

        var window = WindowOf(session);
        Note("the window: " + (open.IsOwned ? "owned by " + open.OwnerName : "for sale") + ", " + open.Type + ", buy " + Lines(window, GuardianPane.Buy) + "; rent " + Lines(window, GuardianPane.Rent));
        var pyreals = HouseGuardian.Line(open.Buy, PyrealWcid)?.Required;
        var writs = HouseGuardian.Line(open.Buy, WritWcid)?.Required;
        if (pyreals != 100_000 || writs != 1)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the window's price is {pyreals} pyreals and {writs} writs, not ACE's 100,000 and 1 (slumlordapartment 15608)"));
        }

        if (_alreadyOwned)
        {
            return open.OwnerId == session.State.Self.Guid ? Go("rent") : Fail("this character owns a house, but not apartment 4051: " + open.OwnerName);
        }

        if (open.IsOwned)
        {
            return Fail("apartment 4051 is owned by " + open.OwnerName + "; pick another");
        }

        if (!window.FillFromPacks(GuardianPane.Buy))
        {
            return Fail("filling the purchase pane: " + window.ErrorLine);
        }

        Note("purchase pane: " + string.Join(", ", window.Placed(GuardianPane.Buy).Select(static g => "0x" + g.ToString("X8", CultureInfo.InvariantCulture))) + " covers " + Lines(window, GuardianPane.Buy));
        _pending = window.PressBuy(ActionSource.Automation);
        return _pending is null ? Fail("Buy refused by the window: " + window.ErrorLine) : Go("buy");
    }

    private ScenarioStatus Buy(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        if (done.Kind != ActionOutcomeKind.Confirmed)
        {
            return Fail("the purchase: " + done.Kind + " " + done.Detail);
        }

        Note("bought: " + done.Detail);
        return Go("owned");
    }

    private ScenarioStatus Owned(IGameSessionView session)
    {
        // ACE sends HouseData and a fresh profile three seconds after the purchase (Player_House.SetHouseOwner).
        if (session.State.HouseStatus != HouseOwnership.Owned || session.State.Guardian is not { IsOwned: true } open)
        {
            return ScenarioStatus.Running;
        }

        Note(string.Create(CultureInfo.InvariantCulture, $"owned: the house data says {session.State.House!.Type}, rent due {DateTimeOffset.FromUnixTimeSeconds(session.State.House.RentDue):u}; the guardian names {open.OwnerName}"));
        return Go("rent");
    }

    private ScenarioStatus Rent(IGameSessionView session)
    {
        if (session.State.Guardian is not { } open)
        {
            return Fail("the guardian window closed before the rent");
        }

        if (_pending is { Kind: ActionKind.PayRent } paying)
        {
            if (paying.Outcome is not { } paid)
            {
                return ScenarioStatus.Running;
            }

            if (paid.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail("the rent: " + paid.Kind + " " + paid.Detail);
            }

            if (!open.IsRentPaid)
            {
                return ScenarioStatus.Running; // the refreshed profile follows "Maintenance paid."
            }

            Note("rent: " + paid.Detail + "; the window now reads " + Lines(WindowOf(session), GuardianPane.Rent));
            _pending = session.Tools.Place(_holtburg, ActionSource.Host);
            return Go("away");
        }

        if (open.IsRentPaid)
        {
            Note("rent: already paid this period (" + Lines(WindowOf(session), GuardianPane.Rent) + "): no payment to prove in this run");
            _pending = session.Tools.Place(_holtburg, ActionSource.Host);
            return Go("away");
        }

        var window = WindowOf(session);
        if (!window.FillFromPacks(GuardianPane.Rent))
        {
            return Fail("filling the rent pane: " + window.ErrorLine);
        }

        Note("rent pane: " + Lines(window, GuardianPane.Rent));
        _pending = window.PressPay(ActionSource.Automation);
        return _pending is null ? Fail("Pay refused by the window: " + window.ErrorLine) : ScenarioStatus.Running;
    }

    private ScenarioStatus Recall(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        if (done.Kind != ActionOutcomeKind.Confirmed)
        {
            return Fail("/house recall: " + done.Kind + " " + done.Detail);
        }

        if (session.State.Self.Position is not { } at || (at.CellId >> 16) != ApartmentLandblock)
        {
            return ScenarioStatus.Running;
        }

        var d = Vector3.Distance(at.Origin, new Vector3(90f, -137.984f, 0f));
        Note(string.Create(CultureInfo.InvariantCulture, $"/house recall: {done.Detail}; landed in cell 0x{at.CellId:X8}, {d:0.0} m from the guardian"));
        if (d > 5)
        {
            return Fail("the recall landed more than 5 m from the guardian");
        }

        _pending = session.Tools.Place(ChestRoom, ActionSource.Host);
        return Go("to-chest");
    }

    private ScenarioStatus OpenChest(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        if (done.Kind != ActionOutcomeKind.Confirmed || session.State.Containers.ViewedGuid != Chest)
        {
            return Fail("opening the storage chest: " + done.Kind + " " + done.Detail);
        }

        Note("the chest is open: " + ChestCount(session).ToString(CultureInfo.InvariantCulture) + " items inside");
        if (_kit == 0 || !session.State.Self.Inventory.Contains(_kit))
        {
            return Fail("the healing kit to store is not in the pack");
        }

        _pending = session.Actions.MoveToPack(_kit, Chest, 0, ActionSource.Automation);
        return Go("put");
    }

    private ScenarioStatus Put(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        var inChest = session.State.Containers.TryGet(Chest, out var view) && view.Entries.Any(e => e.Guid == _kit);
        if (done.Kind != ActionOutcomeKind.Confirmed || !inChest || session.State.Self.Inventory.Contains(_kit))
        {
            return Fail($"putting the kit in the chest: {done.Kind} {done.Detail}; in the chest {inChest}, in the pack {session.State.Self.Inventory.Contains(_kit)}");
        }

        Note("put in: the kit 0x" + _kit.ToString("X8", CultureInfo.InvariantCulture) + " is in the chest (" + ChestCount(session).ToString(CultureInfo.InvariantCulture) + " items) and not in the pack");
        _pending = session.Actions.PickUp(_kit, 0, 0, ActionSource.Automation);
        return Go("take");
    }

    private ScenarioStatus Take(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        var inChest = session.State.Containers.TryGet(Chest, out var view) && view.Entries.Any(e => e.Guid == _kit);
        if (done.Kind != ActionOutcomeKind.Confirmed || inChest || !session.State.Self.Inventory.Contains(_kit))
        {
            return Fail($"taking the kit back: {done.Kind} {done.Detail}; in the chest {inChest}, in the pack {session.State.Self.Inventory.Contains(_kit)}");
        }

        Note("taken out: the kit is back in the pack, the chest holds " + ChestCount(session).ToString(CultureInfo.InvariantCulture));
        _pending = session.Actions.CloseContainer(Chest, ActionSource.Automation);
        return Go("close");
    }

    private ScenarioStatus Close(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } done })
        {
            return ScenarioStatus.Running;
        }

        Note("chest closed (" + done.Kind + ")");
        _log.Info(LogCategory, "verdict villa=proved bought=" + (_alreadyOwned ? "earlier" : "yes") + " steps=" + _record.Count.ToString(CultureInfo.InvariantCulture));
        return ScenarioStatus.Succeeded;
    }

    private static GuardianWindowModel WindowOf(IGameSessionView session) =>
        session is GameSession game ? game.GuardianWindow : throw new InvalidOperationException("the villa proof needs the game session's guardian window");

    private static string Lines(GuardianWindowModel window, GuardianPane pane) =>
        string.Join(" + ", window.Lines(pane).Select(static l => string.Create(CultureInfo.InvariantCulture, $"{l.Line.Required:N0} {l.Line.Name} (paid {l.Line.Paid:N0}, placed {l.Placed:N0})")));

    private static int ChestCount(IGameSessionView session) => session.State.Containers.TryGet(Chest, out var view) ? view.Entries.Count : -1;

    private static uint FirstHeld(GameState state, uint wcid)
    {
        var guids = new List<uint>();
        GearInventory.Collect(state, guids);
        return guids.FirstOrDefault(g => state.Objects.TryGet(g, out var o) && o.Weenie.WeenieClassId == wcid && !state.Self.Inventory.TryGetEquipped(g, out _));
    }

    private static TeleportDestination Spot(string name, uint cell, Vector3 local) =>
        new(name, [], new WorldLocation(name, new CellPosition(cell, local), HasHeight: true, HeadingDegrees: 0), "villa proof (read-only SELECTs, 2026-11-16)");

    private ScenarioStatus Go(string stage)
    {
        _stage = stage;
        _stageStart = double.NaN;
        return ScenarioStatus.Running;
    }

    private void Note(string line)
    {
        _record.Add(line);
        _log.Info(LogCategory, "[" + _stage + "] " + line);
    }

    private ScenarioStatus Fail(string why)
    {
        Failure = why;
        _log.Error(LogCategory, "villa proof failed at " + _stage + ": " + why);
        return ScenarioStatus.Failed;
    }
}
