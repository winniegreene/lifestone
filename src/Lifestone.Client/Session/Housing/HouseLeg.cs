using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// What the villa's steps share (M6 villa): a <see cref="StationLeg"/> with check, act, check helpers in the player's own ways (M5 D141; the owner's player rules, 2026-11-11): the walk to a placed thing through the journey's
/// travel seam, the guardian opened by a Use (the window a player sees, <see cref="GuardianWindowModel"/>), <c>/house recall</c>, the house query, Lifestone Recall home, and the packs' count of a price. A failure is a named token
/// (<c>&lt;step&gt;:&lt;what&gt;</c>), logged and kept as the verdict's <c>reason=</c>.
/// </summary>
public abstract class HouseLeg : StationLeg
{
    /// <summary>The seconds an after-check waits for the server's answer to show in the read model (a HouseData follows a purchase by 3 s: ACE <c>Player_House.SetHouseOwner</c>).</summary>
    public const double AfterSeconds = 10;

    /// <summary>The seconds the arrival of a teleport (a recall, a portal) has to settle before the check after reads the body's place.</summary>
    public const double TeleportSeconds = 30;

    /// <summary>The seconds before the one named retry of a house recall the server called busy or moved (<c>recovery=recall-again</c>).</summary>
    public const double RecallRetrySeconds = 3;

    private readonly List<uint> _scratch = [];

    /// <summary>Creates the leg.</summary>
    /// <param name="name">The station's name.</param>
    /// <param name="context">The journey's context.</param>
    protected HouseLeg(string name, JourneyContext context)
        : base(name, context)
    {
    }

    /// <summary>The verdict's <c>reason=</c> token (<c>none</c> until set).</summary>
    protected string Reason { get; set; } = "none";

    /// <summary>The step's token prefix (<c>writ</c>, <c>lockpick</c>, <c>villa</c>, <c>storage</c>, <c>rent</c>).</summary>
    protected abstract string Prefix { get; }

    /// <inheritdoc />
    public override string VerdictFields() => Fields() + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <summary>The station's own fields (its one line), with <c>ok=</c> and <c>reason=</c>.</summary>
    protected abstract string Fields();

    /// <summary>Fails the step by name: <c>&lt;prefix&gt;:&lt;token&gt;: why</c>; the first failure stands.</summary>
    /// <param name="token">The token after the prefix.</param>
    /// <param name="why">The reason with its numbers.</param>
    protected void Fail(string token, string why)
    {
        Reason = token;
        var text = Prefix + ":" + token + ": " + why;
        Check(false, text);
        Failure ??= text;
    }

    /// <summary>Ends the step well without acting (a rule said not now): the reason is logged and kept.</summary>
    /// <param name="token">The rule's token.</param>
    /// <param name="why">The rule's reason.</param>
    protected void NotNow(string token, string why)
    {
        Reason = "not-due:" + token;
        Log($"{Prefix}: no step: {why}");
    }

    /// <summary>How many of <paramref name="wcid"/> the player holds (stacks summed, side packs counted).</summary>
    protected int Held(uint wcid) => GearInventory.CountOf(S.State, _scratch, wcid);

    /// <summary>What the packs hold toward a price: pyreal stacks, trade notes' face value, writs and lockpicks.</summary>
    protected PriceHeld ReadPrice() => VillaReads.Price(S.State, _scratch);

    /// <summary>The body's place in words.</summary>
    protected string Where() => S.State.Self.Position is { } p ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{p.CellId:X8} ({p.Origin.X:0.#}, {p.Origin.Y:0.#}, {p.Origin.Z:0.#})") : "an unknown place";

    /// <summary>True when the body stands in <paramref name="landblock"/>.</summary>
    protected bool InLandblock(uint landblock) => S.State.Self.Position is { } p && p.CellId >> 16 == landblock;

    /// <summary>The live distance from the body to <paramref name="guid"/> (landblock-global metres), or null when either is unplaced.</summary>
    protected double? DistanceTo(uint guid) =>
        S.State.Objects.TryGet(guid, out var o) && o.Position is { } there && S.State.Self.Position is { } me
            ? Vector2.Distance(ResupplyPlanner.WorldMetres(me.CellId, me.Origin), ResupplyPlanner.WorldMetres(there.CellId, there.Origin))
            : null;

    /// <summary>
    /// Walks to <paramref name="thing"/> through the travel seam (an object goal with <paramref name="range"/> when the object table holds it, else its point) and checks after that the live distance is within
    /// <paramref name="reach"/>; a second walk when it stands beyond. Returns through <paramref name="found"/> (true when in reach) and leaves <see cref="StationLeg.LastTravelFailure"/> set when the walk failed.
    /// </summary>
    protected IEnumerable<ActionHandle?> Meet(PlacedThing thing, TravelPurpose purpose, double range, double reach, Action<bool> found)
    {
        for (var walk = 0; walk < 2; walk++)
        {
            var request = S.State.Objects.TryGet(thing.Guid, out var live) && live.Position is { } at
                ? new TravelRequest(Destination(thing.Name, at.CellId, at.Origin), purpose, thing.Guid, range)
                : new TravelRequest(Destination(thing.Name, thing.Cell, thing.Point), purpose, Range: range);
            Log(string.Create(CultureInfo.InvariantCulture, $"walking: purpose={purpose.ToString().ToLowerInvariant()} ({Prefix}: to {thing.Name} 0x{thing.Guid:X8} in 0x{thing.Cell:X8}, walk {walk + 1})"));
            foreach (var h in TravelTo(request, thing.Name, optional: true)) { yield return h; }

            if (LastTravelFailure is not null)
            {
                found(false);
                yield break;
            }

            foreach (var h in Until(() => DistanceTo(thing.Guid) is not null, NpcSeconds)) { yield return h; }

            if (DistanceTo(thing.Guid) is { } metres && metres <= reach)
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"{Prefix}: check after the walk: {thing.Name} 0x{thing.Guid:X8} is {metres:0.0} m away, within {reach:0.0} m"));
                found(true);
                yield break;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"{Prefix}: {thing.Name} 0x{thing.Guid:X8} is {(DistanceTo(thing.Guid) is { } d ? d.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "not seen")} after walk {walk + 1}, beyond {reach:0.0} m"));
        }

        found(false);
    }

    /// <summary>
    /// Opens <paramref name="guardian"/>'s window the way a player does: in use reach, a Use (ACE's <c>SlumLord.ActOnUse</c> answers HouseProfile 0x021D); after, <see cref="GameState.Guardian"/> names it. Fails
    /// <c>guardian-unreached</c> or <c>guardian-closed</c>.
    /// </summary>
    protected IEnumerable<ActionHandle?> OpenGuardian(PlacedThing guardian)
    {
        if (S.State.Guardian?.Guardian == guardian.Guid)
        {
            Log($"{Prefix}: the guardian 0x{guardian.Guid:X8} is already open");
            yield break;
        }

        var reached = false;
        foreach (var h in Meet(guardian, TravelPurpose.Cell, PathwardenRun.UseRange, QuestStepLeg.UseReachMetres, ok => reached = ok)) { yield return h; }

        if (!reached)
        {
            Fail("guardian-unreached", $"the guardian 0x{guardian.Guid:X8} was not met in reach: {LastTravelFailure ?? "not seen in reach"} (the body at {Where()})");
            yield break;
        }

        var use = S.Actions.Use(guardian.Guid, ActionSource.Automation);
        yield return use;
        foreach (var h in Until(() => S.State.Guardian?.Guardian == guardian.Guid, NpcSeconds)) { yield return h; }

        if (S.State.Guardian is not { } open || open.Guardian != guardian.Guid)
        {
            Fail("guardian-closed", $"the Use of the guardian 0x{guardian.Guid:X8} opened no window within {NpcSeconds:0} s: {Describe(use)}");
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{Prefix}: check after the Use: the guardian 0x{open.Guardian:X8} is open: {open.Type}, {(open.IsOwned ? "owned by " + open.OwnerName : "unowned")}, min level {open.MinLevel}, buy [{Lines(open.Buy)}], rent [{Lines(open.Rent)}]{(open.IsRentPaid ? ", rent paid" : string.Empty)}"));
    }

    /// <summary>The window model the drawn window shares when the session has one, else one over the session's state and actions.</summary>
    protected GuardianWindowModel Window() => S is GameSession session ? session.GuardianWindow : (_window ??= new GuardianWindowModel(S.State, S.Actions));

    private GuardianWindowModel? _window;

    /// <summary>Asks the server for the house status (HouseQuery) and waits for its answer: after, <see cref="GameState.HouseStatus"/> is not Unknown. Fails <c>status-unread</c>.</summary>
    protected IEnumerable<ActionHandle?> QueryHouse()
    {
        var before = S.State.HouseInfoRevision;
        var query = S.Actions.QueryHouse(ActionSource.Automation);
        yield return query;
        foreach (var h in Until(() => S.State.HouseInfoRevision > before, AfterSeconds)) { yield return h; }

        if (S.State.HouseStatus == HouseOwnership.Unknown)
        {
            Fail("status-unread", $"the house query got no answer: {Describe(query)}");
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{Prefix}: house status {S.State.HouseStatus}{(S.State.House is { } house ? $": a {house.Type} at 0x{house.Position.CellId:X8}, rent due {DateTimeOffset.FromUnixTimeSeconds(house.RentDue):yyyy-MM-dd HH:mm}Z, {(house.IsRentPaid ? "paid" : "owed")}" : string.Empty)}"));
    }

    /// <summary>
    /// <c>/house recall</c>, check, act, check: before, an owned house; the act, RecallHouse; after, the body present in <paramref name="landblock"/> (the guardian's). One named retry when the server answers busy or moved
    /// (<c>recovery=recall-again</c>); any other refusal fails <c>house-recall</c>.
    /// </summary>
    protected IEnumerable<ActionHandle?> RecallToHouse(uint landblock)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var mark = S.ActionLog.Total;
            Log($"{Prefix}: /house recall (attempt {attempt + 1}) from {Where()}");
            var recall = S.Actions.RecallHouse(ActionSource.Automation);
            yield return recall;
            if (recall.Outcome is { Kind: ActionOutcomeKind.Confirmed })
            {
                foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(mark) && InLandblock(landblock), TeleportSeconds)) { yield return h; }

                if (InLandblock(landblock))
                {
                    Log($"{Prefix}: check after the house recall: at {Where()}");
                    foreach (var h in Pause(1)) { yield return h; }

                    yield break;
                }

                Fail("house-recall", string.Create(CultureInfo.InvariantCulture, $"after the recall the body stands at {Where()}, not in the house's landblock 0x{landblock:X4}"));
                yield break;
            }

            var detail = recall.Outcome?.Detail ?? "no answer";
            if (attempt == 0 && (detail.Contains("YoureTooBusy", StringComparison.Ordinal) || detail.Contains("YouHaveMovedTooFar", StringComparison.Ordinal)))
            {
                Log($"{Prefix}: recovery=recall-again: the server said {detail}; one more recall after {RecallRetrySeconds:0} s");
                foreach (var h in Pause(RecallRetrySeconds)) { yield return h; }

                continue;
            }

            Fail("house-recall", $"the house recall was refused: {Describe(recall)}");
            yield break;
        }
    }

    /// <summary>
    /// Lifestone Recall home (the journey's <see cref="TravelPurpose.Home"/>, the ground's home lifestone) and the check after: present in the home's landblock. One named retry after a failed recall
    /// (<c>recovery=recall-home-again</c>); a second failure fails <c>way-back</c> (the body stays where it is, named, for the caller's recovery).
    /// </summary>
    protected IEnumerable<ActionHandle?> GoHome()
    {
        var home = Context.Dungeon.Home;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Log($"walking: purpose=home ({Prefix}: Lifestone Recall to {home.Town}, attempt {attempt + 1})");
            foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", home.LifestoneCell, home.LifestonePoint), TravelPurpose.Home), "home", optional: true)) { yield return h; }

            if (LastTravelFailure is null && InLandblock(home.LifestoneCell >> 16))
            {
                Log($"{Prefix}: check after the way back: at {Where()}");
                yield break;
            }

            if (attempt == 0)
            {
                Log($"{Prefix}: recovery=recall-home-again: the way back ended at {Where()}: {LastTravelFailure ?? "not in the home's landblock"}");
                foreach (var h in Pause(RecallRetrySeconds)) { yield return h; }
            }
        }

        if (Failure is null)
        {
            Fail("way-back", string.Create(CultureInfo.InvariantCulture, $"the way back to {home.Town} failed twice: {LastTravelFailure ?? "not in the home's landblock 0x" + (home.LifestoneCell >> 16).ToString("X4", CultureInfo.InvariantCulture)} (the body at {Where()})"));
        }
        else
        {
            Check(false, $"{Prefix}:way-back: the way back to {home.Town} failed twice (the body at {Where()})");
        }
    }

    /// <summary>A price list in words: <c>2,000,000 Pyreals (0 paid), 5 Writs of Refuge (0 paid)</c>.</summary>
    protected static string Lines(IEnumerable<Lifestone.Net.Messages.HousePayment> lines) =>
        string.Join(", ", lines.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Required:N0} {(l.Required == 1 ? l.Name : l.PluralName)} ({l.Paid:N0} paid)")));

    /// <summary>The pane's lines in words: what each asks, holds and the placed stacks bring.</summary>
    protected static string PaneText(IEnumerable<PaneLine> lines) =>
        string.Join("; ", lines.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Line.Name}: left {l.Line.Remaining:N0}, placed {l.Placed:N0}, missing {l.Missing:N0}")));
}
