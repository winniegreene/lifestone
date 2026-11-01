using Lifestone.Client.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What the stations of lane J3a share (M4 P5b J3a): a <see cref="ScriptLeg"/> built from the journey's <see cref="JourneyContext"/> that walks only through <see cref="JourneyContext.Travel"/> (lanes.md contract 5:
/// a station never calls <c>Tools.Place</c>), settles a place's arrival as the scenarios do, and reads the pack and the object table. The leg starts in the world where the journey put it: no login, no relog, no log off.
/// </summary>
public abstract class StationLeg : ScriptLeg
{
    /// <summary>The seconds a station gives an NPC to come into the object table after the arrival.</summary>
    protected const double NpcSeconds = 10;

    private const double ArrivalSeconds = 30;

    /// <summary>Creates the leg.</summary>
    /// <param name="name">The leg's name (the log's <c>[live name]</c>).</param>
    /// <param name="context">The journey's context.</param>
    protected StationLeg(string name, JourneyContext context)
        : base(name, text => (context ?? throw new ArgumentNullException(nameof(context))).Log.Info("station", text))
    {
        Context = context;
        if (context.Runners is null && context.Dats is { } dats)
        {
            // A host with no runner (the loopback tests, a headless leg over places) has nobody else walking the client's body after a server move: the leg's own follower does, as the loopback LiveLeg's did.
            Follow = new HuntFollow(dats, context.Log);
        }
    }

    /// <summary>The journey's context.</summary>
    protected JourneyContext Context { get; }

    /// <summary>The reason the last <see cref="TravelTo"/> failed when it was optional, else null (reset at each call).</summary>
    protected string? LastTravelFailure { get; private set; }

    /// <summary>
    /// Travels to <paramref name="request"/> through the journey's travel seam and, for a place, waits out the end of the teleport as <see cref="ScriptLeg.PlaceAndSettle"/> does (the teleport seen, Present, the post-teleport LoginComplete
    /// confirmed, a 3 s settle); for a run, a recall or nothing the arrival is authoritative (a 1 s settle). A failed travel is a failed check and ends the leg Failed with the travel's reason.
    /// </summary>
    /// <remarks>
    /// With <paramref name="optional"/> a failed travel is not a failed check and does not throw: it logs a WARN line naming the destination and the travel's reason, sets <see cref="LastTravelFailure"/> (null again at every call) and ends,
    /// so the caller can skip the stop (M5 7: the restock walk to a vendor the physics refused must not fail the whole trip).
    /// </remarks>
    protected IEnumerable<ActionHandle?> TravelTo(TravelRequest request, string what, bool optional = false)
    {
        LastTravelFailure = null;
        var mark = S.ActionLog.Total;
        using var leg = Context.Travel.GoTo(S, request, S.Clock.Now);
        Log($"travel to {what}: {request.Purpose}{(request.ObjectGuid == 0 ? string.Empty : $" to object {request.ObjectGuid:X8} within {request.Range:0.0} m")}");
        var status = leg.Step(S, S.Clock.Now);
        while (status == TravelStatus.Running)
        {
            yield return null;
            status = leg.Step(S, S.Clock.Now);
        }

        if (status == TravelStatus.Failed && optional)
        {
            LastTravelFailure = leg.Failure ?? "failed";
            Context.Log.Warn("station", $"[live {Name}] travel to {what}: failed, the stop is skipped: {LastTravelFailure}");
            yield break;
        }

        if (status == TravelStatus.Failed)
        {
            Check(false, $"travel to {what}: {leg.Failure ?? "failed"}");
            throw new InvalidOperationException(leg.Failure ?? $"travel to {what} failed");
        }

        Log($"travel to {what}: arrived by {leg.Means}{(string.IsNullOrEmpty(leg.Fields) ? string.Empty : " (" + leg.Fields + ")")}");
        if (leg.Means == TravelMeans.Place)
        {
            foreach (var h in Until(() => S.State.Self.Travel is SelfTravel.TeleportArrived or SelfTravel.Arriving or SelfTravel.Materialising, ArrivalSeconds))
            {
                yield return h;
            }

            foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(mark), ArrivalSeconds))
            {
                yield return h;
            }

            Log($"travel to {what}: the teleport ended (travel {S.State.Self.Travel})");
            foreach (var h in Pause(3))
            {
                yield return h;
            }
        }
        else
        {
            foreach (var h in Pause(1))
            {
                yield return h;
            }
        }
    }

    /// <summary>The object whose name contains <paramref name="part"/> nearest the player within <paramref name="metres"/>, or 0 (the NPCs' names carry a title: "Shopkeeper Renald the Elder").</summary>
    protected uint NearestContaining(string part, double metres)
    {
        if (S.State.Self.Position is not { } here)
        {
            return 0;
        }

        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Guid == S.State.Self.Guid || o.Position is not { } p || p.CellId >> 16 != here.CellId >> 16 || !o.Name.Contains(part, StringComparison.Ordinal))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, here.Origin);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o.Guid;
            }
        }

        return bestDistance <= metres ? best : 0;
    }

    /// <summary>The object of exactly that name nearest the point <paramref name="at"/> (landblock coordinates) within <paramref name="metres"/> of it, wherever the player stands, or 0: the journey asks for an object goal before it walks.</summary>
    protected uint NearestTo(string objectName, uint cell, Vector3 at, double metres)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Guid == S.State.Self.Guid || o.Position is not { } p || p.CellId >> 16 != cell >> 16 || !string.Equals(o.Name, objectName, StringComparison.Ordinal))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, at);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o.Guid;
            }
        }

        return bestDistance <= metres ? best : 0;
    }

    /// <summary>The guids of the main pack's items of that weenie class (ContainerType 0: no side pack, no focus), in the pack's order.</summary>
    protected List<uint> InPack(uint wcid)
    {
        var list = new List<uint>();
        foreach (var entry in S.State.Self.Inventory.Pack)
        {
            if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid)
            {
                list.Add(entry.Guid);
            }
        }

        return list;
    }

    /// <summary>The first Handy Healing Kit in the main pack that has uses left, or 0.</summary>
    protected uint PickKit() => InPack(StationItems.KitWcid).FirstOrDefault(k => S.State.Objects.TryGet(k, out var o) && (o.Weenie.Structure is null or > 0));

    /// <summary>The coins the character holds (property 20, CoinValue), or -1 when unknown.</summary>
    protected long Coins() => S.State.Self.Qualities.TryGetInt(StationItems.CoinProperty, out var value) ? value : -1;

    /// <summary>"yes" or "no".</summary>
    protected static string Yes(bool value) => value ? "yes" : "no";
}

/// <summary>The weenies and properties the stations of lane J3a use.</summary>
internal static class StationItems
{
    /// <summary>Handy Healing Kit.</summary>
    internal const uint KitWcid = 628;

    /// <summary>Potion of Healing.</summary>
    internal const uint PotionWcid = 377;

    /// <summary>The CoinValue property.</summary>
    internal const uint CoinProperty = 20;
}

/// <summary>
/// The chests station (M4 P5b J3a; ports <c>ContainerLiveTests</c> <c>A_chest_opens_and_closes_and_a_locked_chest_refuses</c>, :68): Chest 143 at cell 0xA9B4016A is opened and closed, and the locked Aluvian Pathwarden Chest at 0xA9B40022 is refused,
/// all through <see cref="IActionApi.OpenContainer"/> and <see cref="IActionApi.CloseContainer"/>. The player walks there from wherever it stands in Holtburg through <see cref="JourneyContext.Travel"/>.
/// <para><b>The travel purpose.</b> <see cref="TravelPurpose.Vendor"/> and <see cref="TravelPurpose.Bind"/> name a shop and the lifestone; a chest is neither, so the requests use <see cref="TravelPurpose.Cell"/> (a point in a known cell) with an
/// object goal (the chest's guid when the object table holds it, else the point alone) and a short range: the use radius of a chest is a live unknown (<c>TODO(chest-use-range)</c>: 1.5 m is the guess, the lead reads it from the first live run).</para>
/// </summary>
public sealed class ChestsStation : IStation
{
    /// <inheritdoc />
    public string Name => "chests";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new ChestsRun(context);
}

/// <summary>The chests station's run (see <see cref="ChestsStation"/>).</summary>
public sealed class ChestsRun : StationLeg
{
    /// <summary>Chest 143's cell.</summary>
    public const uint HoltburgChestCell = 0xA9B4016A;

    /// <summary>The Aluvian Pathwarden Chest's cell.</summary>
    public const uint PathwardenCell = 0xA9B40022;

    /// <summary>The use range asked of the travel for the open chest, in metres: <c>TODO(chest-use-range)</c> (a guess until the first live journey).</summary>
    public const double ChestRange = 1.5;

    /// <summary>
    /// The range asked for the locked chest. F3-3: 3.0 m was too far. The live answer was a UseDone 0 after 15.1 s with no F750 0x94 and no text: ACE's Use walks the player to the chest first, and that walk gave up
    /// at its 15 s timeout (ACE Player_Move.cs:16 @95ef426d <c>defaultMoveToTimeout</c>, :119-123 @95ef426d callback(false)) before <c>Chest.CheckUseRequirements</c> (Chest.cs:105-111, the lock refusal and its sound) ever ran. The same range as Chest 143's.
    /// </summary>
    public const double LockedRange = ChestRange;

    private static readonly Vector3 ChestAt = new(75.82f, 38.39f, 94.01f);
    private static readonly Vector3 PathwardenAt = new(99, 31, 94);

    internal ChestsRun(JourneyContext context)
        : base("containers-chest", context)
    {
    }

    /// <summary>True when Chest 143 was opened (Confirmed, the view names it).</summary>
    public bool Opened { get; private set; }

    /// <summary>True when it was closed (Confirmed, the view is gone).</summary>
    public bool Closed { get; private set; }

    /// <summary>
    /// True when a chest use ended as ACE's walk-to-the-chest timeout does: a ServerError UseDone with no view, no lock sound and no text ("no reason given"), so ACE never reached its lock rule (F3-3).
    /// </summary>
    internal static bool WalkTimedOut(ActionOutcome? outcome) =>
        outcome is { Kind: ActionOutcomeKind.ServerError, Detail: { } d } && d.Contains("no reason given", StringComparison.Ordinal) && !d.Contains("locked", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>refused</c> when the locked chest answered a ServerError whose detail says locked; <c>unreached</c> when ACE's walk to it timed out (no lock answer at all; fails); <c>not_refused</c>; <c>none</c> before.</summary>
    public string Locked { get; private set; } = "none";

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"chest_opened={Opened.ToString().ToLowerInvariant()} closed={Closed.ToString().ToLowerInvariant()} locked={Locked} problems={Problems.Count}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Stage = "chest";
        var chestGuid = NearestTo("Chest", HoltburgChestCell, ChestAt, 10);
        foreach (var h in TravelTo(new TravelRequest(Destination("chest 0xA9B4016A", HoltburgChestCell, ChestAt), TravelPurpose.Cell, chestGuid, ChestRange), "chest 143"))
        {
            yield return h;
        }

        var chest = Nearest("Chest", 8);
        if (Check(chest != 0, "no object named 'Chest' within 8 m of the arrival"))
        {
            var open = S.Actions.OpenContainer(chest, ActionSource.Host);
            yield return open;
            Log("OpenContainer: " + Describe(open));
            Opened = Check(open.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "OpenContainer: " + Describe(open))
                && Check(S.State.Containers.Viewed is { } v && v.Guid == chest, "OpenContainer: the viewed container is not the chest");

            var close = S.Actions.CloseContainer(chest, ActionSource.Host);
            yield return close;
            Log("CloseContainer: " + Describe(close));
            foreach (var h in Until(() => S.State.Containers.Viewed is null, 2))
            {
                yield return h;
            }

            Closed = Check(close.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "CloseContainer: " + Describe(close))
                && Check(S.State.Containers.Viewed is null, "CloseContainer: the chest view is still open");
        }

        Stage = "locked-chest";
        var lockedGuid = NearestTo("Aluvian Pathwarden Chest", PathwardenCell, PathwardenAt, 10);
        foreach (var h in TravelTo(new TravelRequest(Destination("pathwarden 0xA9B40022", PathwardenCell, PathwardenAt), TravelPurpose.Cell, lockedGuid, LockedRange), "the Aluvian Pathwarden Chest"))
        {
            yield return h;
        }

        var locked = Nearest("Aluvian Pathwarden Chest", 15);
        if (Check(locked != 0, "no 'Aluvian Pathwarden Chest' within 15 m of the arrival"))
        {
            var use = S.Actions.OpenContainer(locked, ActionSource.Host);
            yield return use;
            Log($"OpenContainer of the locked chest {locked:X8}: " + Describe(use) + " (the F750 0x94 sound and the UseDone expected, no text)");
            var refused = use.Outcome is { Kind: ActionOutcomeKind.ServerError, Detail: { } detail } && detail.Contains("locked", StringComparison.OrdinalIgnoreCase);
            Locked = refused ? "refused" : WalkTimedOut(use.Outcome) ? "unreached" : "not_refused";
            Check(refused, "the locked chest: wanted a ServerError whose detail says locked, got " + Describe(use) + (Locked == "unreached" ? " (the answer carries no lock sound and no text: ACE's walk to the chest timed out, Player_Move.cs:119 @95ef426d, so the lock rule, Chest.cs:105 @95ef426d, was never asked; still a failure)" : string.Empty));
            Check(S.State.Containers.Viewed is null, "the locked chest left a container view open");
        }
    }
}

/// <summary>
/// The shortcuts station (M4 P5b J3a; ports <c>ShortcutLiveTests</c> :42 without the relogs): a Handy Healing Kit goes to slot 0 and a Potion of Healing to slot 1 (<see cref="IActionApi.AddShortcut"/>), the potion is drunk by slot
/// (<see cref="IActionApi.UseShortcut"/>: a Confirmed Drank, or Wasted at full health) and both rows are removed (<see cref="IActionApi.RemoveShortcut"/>). A kit or potion an earlier station used up is granted
/// (<see cref="ScriptLeg.GrantIfMissing"/>, counted). The proof that the bar survives a relog stays the loopback fact's (three sessions on <c>test-archer</c>, before the journey).
/// </summary>
public sealed class ShortcutsStation : IStation
{
    /// <inheritdoc />
    public string Name => "shortcuts";

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new ShortcutsRun(context);
}

/// <summary>The shortcuts station's run (see <see cref="ShortcutsStation"/>).</summary>
public sealed class ShortcutsRun : StationLeg
{
    /// <summary>The slot the kit goes to.</summary>
    public const int KitSlot = 0;

    /// <summary>The slot the potion goes to.</summary>
    public const int PotionSlot = 1;

    private uint _kit;
    private uint _potion;

    internal ShortcutsRun(JourneyContext context)
        : base("shortcuts", context)
    {
    }

    /// <summary>The shortcuts added.</summary>
    public int Added { get; private set; }

    /// <summary>The slots of the two that the model lists with their item present (not missing) after the adds.</summary>
    public int Listed { get; private set; } = -1;

    /// <summary>The drink by slot: outcome/result/kind.</summary>
    public string Used { get; private set; } = "none";

    /// <summary>True when the drink by slot was a Confirmed Drank or Wasted.</summary>
    public bool UsedConfirmed { get; private set; }

    /// <summary>The slots empty after the removals.</summary>
    public int Removed { get; private set; } = -1;

    /// <summary>True when the bar holds nothing at the end.</summary>
    public bool BarEmpty { get; private set; }

    private ShortcutBar Bar => S.State.Self.Shortcuts;

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"added={Added} listed={Listed} used={Used} removed={Removed} bar_empty={Yes(BarEmpty)} problems={Problems.Count} granted={Granted}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Stage = "clean";
        for (var slot = 0; slot < ShortcutBar.SlotCount; slot++)
        {
            if (Bar[slot].IsEmpty)
            {
                continue;
            }

            Log($"slot {slot} held {Bar[slot].ObjectId:X8} from an earlier run: removing it");
            var clean = S.Actions.RemoveShortcut(slot, ActionSource.Host);
            yield return clean;
            Check(clean.Outcome is { Kind: ActionOutcomeKind.Sent }, $"clean-up of slot {slot}: {Describe(clean)}");
        }

        Stage = "add";
        foreach (var h in GrantIfMissing(PickKit, StationItems.KitWcid, "Handy Healing Kit 628", g => _kit = g)) { yield return h; }

        foreach (var h in GrantIfMissing(() => InPack(StationItems.PotionWcid).FirstOrDefault(), StationItems.PotionWcid, "Potion of Healing 377", g => _potion = g)) { yield return h; }

        Require(_kit != 0, "the character holds no Handy Healing Kit 628 with uses left in its main pack (the grant with the item verb failed)");
        Require(_potion != 0, "the character holds no Potion of Healing 377 in its main pack (the grant with the item verb failed)");
        foreach (var (slot, item, what) in new[] { (KitSlot, _kit, "kit"), (PotionSlot, _potion, "potion") })
        {
            var add = S.Actions.AddShortcut(slot, item, ActionSource.Host);
            yield return add;
            Log($"AddShortcut {what} {item:X8} to slot {slot}: {Describe(add)}");
            if (Check(add.Outcome is { Kind: ActionOutcomeKind.Sent }, $"AddShortcut {what}: {Describe(add)}") && Check(Bar[slot].ObjectId == item, $"the {what} is not on slot {slot} after the add"))
            {
                Added++;
            }
        }

        Listed = 0;
        foreach (var (slot, item, what) in new[] { (KitSlot, _kit, "kit"), (PotionSlot, _potion, "potion") })
        {
            var ok = item != 0 && Bar[slot].ObjectId == item && !Bar.IsMissing(slot);
            Check(ok, $"the bar does not list the {what} {item:X8} on slot {slot} (it holds {Bar[slot].ObjectId:X8}, missing {Bar.IsMissing(slot)})");
            Listed += ok ? 1 : 0;
        }

        Stage = "use";
        foreach (var h in Pause(1)) { yield return h; }

        var use = S.Actions.UseShortcut(PotionSlot, ActionSource.Host);
        yield return use;
        var result = (use as HealHandle)?.Result;
        Used = (use.Outcome?.Kind.ToString() ?? "unsettled") + "/" + (result?.Kind.ToString() ?? "no-result") + "/" + use.Kind;
        UsedConfirmed = use.Outcome is { Kind: ActionOutcomeKind.Confirmed } && result is { Kind: HealResultKind.Drank or HealResultKind.Wasted };
        Log("UseShortcut(1): " + Describe(use) + "; result " + (result?.ToString() ?? "none"));
        Check(use.Kind == ActionKind.Drink, "UseShortcut(1) did not route to Drink: " + use.Kind);
        Check(UsedConfirmed, "UseShortcut(1) was not a Confirmed Drank or Wasted: " + Used);

        Stage = "remove";
        foreach (var h in Pause(1)) { yield return h; }

        foreach (var slot in new[] { KitSlot, PotionSlot })
        {
            if (Bar[slot].IsEmpty)
            {
                Log($"slot {slot} is empty already (an item that leaves the bar is removed by #219)");
                continue;
            }

            var remove = S.Actions.RemoveShortcut(slot, ActionSource.Host);
            yield return remove;
            Log($"RemoveShortcut({slot}): {Describe(remove)}");
            Check(remove.Outcome is { Kind: ActionOutcomeKind.Sent }, $"RemoveShortcut({slot}): {Describe(remove)}");
        }

        Removed = (Bar[KitSlot].IsEmpty ? 1 : 0) + (Bar[PotionSlot].IsEmpty ? 1 : 0);
        BarEmpty = Bar.Count == 0;
        Check(BarEmpty, $"the bar still holds {Bar.Count} shortcut(s) after the removals");
        foreach (var h in Pause(1)) { yield return h; } // the server saves the list with the character
    }
}
