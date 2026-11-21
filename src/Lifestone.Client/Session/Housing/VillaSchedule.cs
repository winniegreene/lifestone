using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>The villa's step a town trip's boundary may run (<see cref="VillaSchedule.Decide"/>), in the order the schedule weighs them.</summary>
public enum VillaStep
{
    /// <summary>Nothing is due.</summary>
    None,

    /// <summary>The rent (<see cref="RentStation"/>).</summary>
    Rent,

    /// <summary>The purchase (<see cref="VillaPurchaseStation"/>).</summary>
    Purchase,

    /// <summary>The Crude Lockpick (<see cref="LockpickBuyStation"/>).</summary>
    LockpickBuy,

    /// <summary>Stones to writs at Holtburg's Agent (<see cref="WritHandInStation"/>).</summary>
    WritHandIn,

    /// <summary>One minor stone from its cave (the quest station on the cave's quest file).</summary>
    WritFetch,

    /// <summary>The kept finds into the villa's chests (<see cref="StorageTripStation"/>).</summary>
    Storage,
}

/// <summary>
/// What the villa schedule reads at a town trip's boundary (M6 villa, the climb's wiring): the clock, the account's opening, the house the server described, what the packs hold, the stones' timers, and what this run and this boundary
/// have done already. Every game number in it is read live by the caller (the state, the timers, the quest files); the margins are Lifestone's own.
/// </summary>
/// <param name="Now">The time (UTC).</param>
/// <param name="OpensAt">The first instant the account may buy a villa: <see cref="VillaFacts.ClimberAccountOpensVillas"/> (at once: the server mod turns ACE's 15-day rule off), or <see cref="VillaFacts.ClimberAccountOpensVillasRetail"/> under the
/// server's <c>LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace</c> (the client's <c>--autopilot-villa-account-age=ace</c>).</param>
/// <param name="Block">The block to buy in.</param>
/// <param name="Status">What the client knows of the account's house.</param>
/// <param name="RentPaid">True when every rent line of the owned house is paid this period.</param>
/// <param name="RentDue">The owned house's period end (Unix seconds), 0 with no house.</param>
/// <param name="PeriodSeconds">The owned house's rent period (<see cref="OwnedHouse.PeriodSeconds"/>), 0 with no house.</param>
/// <param name="RentOwed">The owned house's rent lines still owed: weenie, name, count left (the money line in pyreals).</param>
/// <param name="RentWritsPerPeriod">The writs one period's rent takes (the owned house's writ line, else the block's guardian's <see cref="VillaFacts.RentWrits"/>).</param>
/// <param name="Level">The character's level.</param>
/// <param name="Held">What the packs hold toward a price.</param>
/// <param name="Coins">The coin (CoinValue), or -1 unread.</param>
/// <param name="Stones">The minor stones held (any of the four).</param>
/// <param name="HalfPending">True when the last hand-in left a first stone at the Agent.</param>
/// <param name="Timers">The character's quest timers.</param>
/// <param name="Caves">The stone caves, in the order to try (failed ones left out by the caller).</param>
/// <param name="StoreItems">The kept finds the storage trip would be asked to put away.</param>
/// <param name="ReviewFree">The review pack's free slots last published, or -1.</param>
/// <param name="StorageWalkReady">True when the walk to the house portal is established (<see cref="StorageTripStation.HousePortalWalkEstablished"/>).</param>
/// <param name="Failed">The steps that failed this run (not called again until the next run).</param>
/// <param name="Done">The steps this boundary has run already (each once a boundary, but the fetch: see <paramref name="BoundaryFetches"/>).</param>
/// <param name="BoundaryFetches">The stone fetches this boundary has started: while the pace is <c>open</c> or <c>behind</c>, one a walkable cave a boundary (the caves' timers are the limit); else one.</param>
public sealed record VillaInput(
    DateTimeOffset Now,
    DateTimeOffset OpensAt,
    VillaBlock Block,
    HouseOwnership Status,
    bool RentPaid,
    long RentDue,
    long PeriodSeconds,
    IReadOnlyList<(uint Wcid, string Name, long Left)> RentOwed,
    int RentWritsPerPeriod,
    int Level,
    PriceHeld Held,
    long Coins,
    int Stones,
    bool HalfPending,
    QuestTimers Timers,
    IReadOnlyList<StoneCave> Caves,
    int StoreItems,
    long ReviewFree,
    bool StorageWalkReady,
    IReadOnlySet<VillaStep> Failed,
    IReadOnlySet<VillaStep> Done,
    int BoundaryFetches = 0)
{
    /// <summary>
    /// M6 16 E5a2: the faces of the trade notes the lockpick step counts with <see cref="Coins"/>: every note held (<see cref="Quartermaster.PurseView.NotesCounterCanChange"/>), or 0 when the Merchant's mask is known to
    /// take no note (<see cref="Quartermaster.NoteMasks"/>). The step's buy is the vendor counter's, which changes a note when the coin is short.
    /// </summary>
    public long LockpickNotes { get; init; }

    /// <summary>M6 16 E5a2: what the lockpick step says of the notes it counted or left out (<see cref="Quartermaster.PurseView.CounterSays"/>), or empty.</summary>
    public string LockpickNotesSay { get; init; } = string.Empty;
}

/// <summary>
/// The writ run's pace (<see cref="VillaSchedule.Pace"/>): the writs wanted by a deadline, the stones still to fetch, and whether a fetch is due now. The stones are spread evenly over the time left before the deadline less the margin, so the
/// hunt keeps the rest of her hours; when the time left is no more than the fastest the walkable caves allow, every boundary fetches (<c>behind</c>).
/// </summary>
/// <param name="Goal">What the writs are for (<c>purchase+rent</c>, <c>rent</c>, <c>next-rent</c>).</param>
/// <param name="Target">The writs wanted.</param>
/// <param name="Deadline">When they are wanted (UTC).</param>
/// <param name="WritsShort">The writs short.</param>
/// <param name="StonesToFetch">The stones still to fetch (two a writ, less the stones held and a half pair at the Agent).</param>
/// <param name="WindowHours">The hours left before the deadline less the margin (negative once past it).</param>
/// <param name="FastestHours">The hours the walkable caves need at their fastest (each one stone a timer).</param>
/// <param name="IntervalHours">The even spacing of the fetches over the window (the window over the stones), or 0 when none is to fetch.</param>
/// <param name="LastFetch">The last stone fetch the timers recorded (any walkable cave), or null.</param>
/// <param name="Due">True when a fetch is due by the pace.</param>
/// <param name="Token">The pace's token: <c>enough</c>, <c>no-cave</c>, <c>open</c> (the purchase is open now: the fastest the caves allow), <c>behind</c>, <c>first</c>, <c>on-pace</c>, <c>ahead</c>.</param>
/// <param name="Why">The pace in words, with every number.</param>
public sealed record WritPace(string Goal, int Target, DateTimeOffset Deadline, int WritsShort, int StonesToFetch, double WindowHours, double FastestHours, double IntervalHours, DateTimeOffset? LastFetch, bool Due, string Token, string Why);

/// <summary>The schedule's answer: the step to run (<see cref="VillaStep.None"/> for none), its token and reason, the cave of a fetch, and every step's own line (the log's).</summary>
/// <param name="Step">The step to run.</param>
/// <param name="Token">The decision's token.</param>
/// <param name="Why">The decision in words.</param>
/// <param name="Cave">The cave of a <see cref="VillaStep.WritFetch"/>, else null.</param>
/// <param name="Pace">The writ pace read.</param>
/// <param name="Lines">One line a step weighed, in order (<c>villa: weigh &lt;step&gt;: &lt;token&gt;: why</c>).</param>
public sealed record VillaCall(VillaStep Step, string Token, string Why, StoneCave? Cave, WritPace Pace, IReadOnlyList<string> Lines)
{
    /// <summary>True when a step runs.</summary>
    public bool Runs => Step != VillaStep.None;
}

/// <summary>
/// The villa's schedule, pure (M6 villa, the climb's wiring; the owner's ruling, 2026-11-15: packs first, and then ideally a villa): at a town trip's boundary, the one villa step that runs next, by rule and never by hand. Weighed in order, each at most
/// once a boundary and never again in a run after it failed:
/// <list type="number">
/// <item>the rent (<see cref="RentRule"/>, within <see cref="RentMargin"/> of the period's end; a late rent at once);</item>
/// <item>the purchase (<see cref="VillaPurchaseRule"/>: from the account's opening, no house owned, the price in the packs);</item>
/// <item>the Crude Lockpick (<see cref="LockpickRule"/>), while no house is owned and none is held: once, before the purchase;</item>
/// <item>the hand-in at Holtburg's Agent: two stones or more, or one with a half pair at the Agent (<see cref="WritRule.PlanHandIn"/>);</item>
/// <item>one stone fetch when the pace says so (<see cref="Pace"/>) and a walkable cave is due by its timer (<see cref="WritRule.NextFetch"/>);</item>
/// <item>the storage trip with a house owned and kept finds held (<c>TODO(villa-house-portal-walk)</c> holds it while the walk is not established).</item>
/// </list>
/// Nothing runs while the house status is unread: the caller asks the server first (HouseQuery) and the next boundary reads the answer.
/// </summary>
public static class VillaSchedule
{
    /// <summary>The margin the writs are wanted by before their deadline (Lifestone's own: a day, so a failed fetch or a lost run still leaves time to fetch again before the purchase opens or the rent falls due).</summary>
    public static readonly TimeSpan WritMargin = TimeSpan.FromDays(1);

    /// <summary>How long before the period's end the rent is paid (Lifestone's own: three days, three days of town trips to pay it in).</summary>
    public static readonly TimeSpan RentMargin = TimeSpan.FromDays(3);

    /// <summary>The weigh order.</summary>
    public static IReadOnlyList<VillaStep> Order { get; } = [VillaStep.Rent, VillaStep.Purchase, VillaStep.LockpickBuy, VillaStep.WritHandIn, VillaStep.WritFetch, VillaStep.Storage];

    /// <summary>The step's token in lines and stop names (its station's name; the fetch's <c>writ-fetch</c>).</summary>
    /// <param name="step">The step.</param>
    public static string Name(VillaStep step) => step switch
    {
        VillaStep.Rent => RentStation.StationName,
        VillaStep.Purchase => VillaPurchaseStation.StationName,
        VillaStep.LockpickBuy => LockpickBuyStation.StationName,
        VillaStep.WritHandIn => WritHandInStation.StationName,
        VillaStep.WritFetch => "writ-fetch",
        VillaStep.Storage => StorageTripStation.StationName,
        _ => "none",
    };

    /// <summary>
    /// What the writs are for and by when: with no house, the price's writs and the first period's rent (<see cref="VillaFacts.PriceWrits"/> + the rent's) by the account's opening, or now once it has passed (the owner, 2026-11-16: a house
    /// needs no account age, so the purchase is open at once and the writs are wanted as soon as the caves allow); with a house whose rent is owed, the rent's writs still owed by the period's end; with the rent paid, one period's
    /// writs by the next period's end.
    /// </summary>
    /// <param name="input">The inputs.</param>
    public static (string Goal, int Target, DateTimeOffset Deadline) WritGoal(VillaInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Status != HouseOwnership.Owned)
        {
            return ("purchase+rent", input.Block.Price.Writs + input.RentWritsPerPeriod, input.OpensAt > input.Now ? input.OpensAt : input.Now);
        }

        var due = DateTimeOffset.FromUnixTimeSeconds(input.RentDue);
        if (!input.RentPaid)
        {
            var owed = input.RentOwed.Where(r => r.Wcid == VillaFacts.WritWcid).Sum(r => r.Left);
            return ("rent", (int)Math.Min(int.MaxValue, owed), due);
        }

        return ("next-rent", input.RentWritsPerPeriod, due.AddSeconds(input.PeriodSeconds));
    }

    /// <summary>
    /// The writ run's pace (see <see cref="WritPace"/>): the stones to fetch for <see cref="WritGoal"/>; the window, the hours before the deadline less <see cref="WritMargin"/>; the fastest, the caves' own timers (each walkable cave one
    /// stone a timer, so <c>ceil(stones / caves) x timer</c>); the spacing, the window over the stones. Due: <c>open</c> when no house is owned and the purchase is open now (the default since the owner's 2026-11-16 decision: the writs
    /// as soon as the caves' timers allow, a fetch from each due cave at every town trip); <c>behind</c> when the window is no more than the fastest; <c>first</c> when no fetch is recorded; <c>on-pace</c> when the spacing has passed since
    /// the last fetch; else <c>ahead</c> (not due).
    /// </summary>
    /// <param name="input">The inputs.</param>
    public static WritPace Pace(VillaInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.InvariantCulture;
        var (goal, target, deadline) = WritGoal(input);
        var (writsShort, stones, needWhy) = WritRule.Need(input.Held.Writs, input.Stones, target, input.HalfPending);
        var open = input.Status != HouseOwnership.Owned && input.OpensAt <= input.Now;
        var window = open ? 0 : (deadline - WritMargin - input.Now).TotalHours;
        var walkable = input.Caves.Where(cv => cv.RouteTodo is null).ToList();
        var timer = walkable.Select(cv => QuestCatalog.Get(cv.QuestKey).TimerSeconds ?? 0).DefaultIfEmpty(0).Max() / 3600.0;
        var fastest = walkable.Count == 0 ? double.PositiveInfinity : Math.Ceiling(stones / (double)walkable.Count) * timer;
        var last = walkable.Select(cv => input.Timers.Get(cv.QuestKey)?.LastCompleted).Where(t => t is not null).Max();
        var head = open
            ? $"{goal}: {needWhy}; the purchase is open now (no account age to wait for): wanted as soon as the caves allow"
            : string.Create(c, $"{goal}: {needWhy}; by {deadline:yyyy-MM-dd HH:mm}Z less the margin {WritMargin.TotalHours:0} h: {window:0.0} h left");
        if (stones == 0)
        {
            return new(goal, target, deadline, writsShort, 0, window, 0, 0, last, false, "enough", head + ": no stone to fetch");
        }

        if (walkable.Count == 0)
        {
            return new(goal, target, deadline, writsShort, stones, window, fastest, 0, last, false, "no-cave", head + ": no walkable cave (each carries its TODO)");
        }

        var interval = Math.Max(0, window) / stones;
        var caves = string.Create(c, $"{walkable.Count} walkable caves, one stone each a {timer:0.#} h timer: {fastest:0.#} h at the fastest");
        if (open)
        {
            return new(goal, target, deadline, writsShort, stones, window, fastest, 0, last, true, "open", head + $"; {caves}: a fetch from each due cave at every town trip");
        }

        if (window <= fastest)
        {
            return new(goal, target, deadline, writsShort, stones, window, fastest, interval, last, true, "behind", head + $"; {caves}: behind, a fetch at every boundary");
        }

        if (last is not { } at)
        {
            return new(goal, target, deadline, writsShort, stones, window, fastest, interval, null, true, "first", head + string.Create(c, $"; {caves}; one fetch every {interval:0.0} h; no fetch recorded: due"));
        }

        var since = (input.Now - at).TotalHours;
        return since >= interval
            ? new(goal, target, deadline, writsShort, stones, window, fastest, interval, at, true, "on-pace", head + string.Create(c, $"; {caves}; one fetch every {interval:0.0} h; the last {since:0.0} h ago: due"))
            : new(goal, target, deadline, writsShort, stones, window, fastest, interval, at, false, "ahead", head + string.Create(c, $"; {caves}; one fetch every {interval:0.0} h; the last {since:0.0} h ago: the next in {interval - since:0.0} h"));
    }

    /// <summary>The step that runs next (see the class), with one line for every step weighed.</summary>
    /// <param name="input">The inputs.</param>
    public static VillaCall Decide(VillaInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var pace = Pace(input);
        var lines = new List<string>();
        if (input.Status == HouseOwnership.Unknown)
        {
            const string unread = "the house status is unread: no villa step until the server answers the HouseQuery";
            lines.Add("villa: weigh all: status-unread: " + unread);
            return new(VillaStep.None, "status-unread", unread, null, pace, lines);
        }

        foreach (var step in Order)
        {
            var name = Name(step);
            if (input.Failed.Contains(step))
            {
                lines.Add($"villa: weigh {name}: failed-this-run: it failed this run and is not called again until the next run");
                continue;
            }

            if (input.Done.Contains(step) && !(step == VillaStep.WritFetch && pace.Token is "open" or "behind" && input.BoundaryFetches < input.Caves.Count(cv => cv.RouteTodo is null)))
            {
                lines.Add($"villa: weigh {name}: done-this-boundary: it ran at this boundary already");
                continue;
            }

            var (runs, token, why, cave) = Weigh(step, input, pace);
            lines.Add($"villa: weigh {name}: {token}: {why}");
            if (runs)
            {
                return new(step, token, why, cave, pace, lines);
            }
        }

        return new(VillaStep.None, "nothing-due", "no villa step is due at this boundary", null, pace, lines);
    }

    /// <summary>The storage trip's items: the held, unworn finds that are great-cantrip pieces or kept aside, in pack order.</summary>
    /// <param name="held">The held items: guid, worn, great-cantrip piece, kept aside.</param>
    public static IReadOnlyList<uint> StoreList(IEnumerable<(uint Guid, bool Worn, bool GreatPiece, bool KeptAside)> held) =>
        [.. (held ?? throw new ArgumentNullException(nameof(held))).Where(h => !h.Worn && (h.GreatPiece || h.KeptAside)).Select(h => h.Guid).Distinct()];

    /// <summary>The cycle line's villa field: <c>off</c>, or <c>on,&lt;phase&gt;,writs:h/t,stones:n,half:yes|no,pyreals:held/price,per_h:+n|n/a</c> (one token, no spaces).</summary>
    /// <param name="on">The switch and the job: false prints <c>off</c>.</param>
    /// <param name="input">The inputs, or null before the first read (<c>on,unread</c>).</param>
    /// <param name="perHour">The money's trend an hour over the run (pyreals and notes), or null before an hour's span is read.</param>
    public static string Marker(bool on, VillaInput? input, double? perHour)
    {
        if (!on)
        {
            return "off";
        }

        if (input is null)
        {
            return "on,unread";
        }

        var c = CultureInfo.InvariantCulture;
        var (_, target, _) = WritGoal(input);
        var phase = input.Status switch
        {
            HouseOwnership.Unknown => "unread",
            HouseOwnership.Owned => "owned",
            _ when input.Now < input.OpensAt || VillaPurchaseRule.Missing(input.Held, input.Block.Price.Pyreals, input.Block.Price.Writs, input.Block.Price.Lockpicks).Count > 0 => "gather",
            _ => "buy",
        };
        var price = input.Status == HouseOwnership.Owned ? input.RentOwed.Where(r => r.Wcid == VillaFacts.PyrealWcid).Sum(r => r.Left) : input.Block.Price.Pyreals;
        return string.Create(c, $"on,{phase},writs:{input.Held.Writs}/{target},stones:{input.Stones},half:{(input.HalfPending ? "yes" : "no")},pyreals:{input.Held.Money}/{price},per_h:{(perHour is { } p ? (p >= 0 ? "+" : string.Empty) + p.ToString("0", c) : "n/a")}");
    }

    private static (bool Runs, string Token, string Why, StoneCave? Cave) Weigh(VillaStep step, VillaInput input, WritPace pace)
    {
        var c = CultureInfo.InvariantCulture;
        switch (step)
        {
            case VillaStep.Rent:
            {
                var rent = RentRule.Decide(input.Status, input.RentPaid, input.RentDue, input.Now.ToUnixTimeSeconds(), RentMargin, input.RentOwed, input.Held);
                return (rent.May, rent.Token, rent.Why, null);
            }

            case VillaStep.Purchase:
            {
                if (input.Status == HouseOwnership.Owned)
                {
                    return (false, "owns-house", "the account owns a house", null);
                }

                var buy = VillaPurchaseRule.Decide(input.Now, input.OpensAt, input.Block, input.Status, input.Level, input.Held);
                return (buy.May, buy.Token, buy.Why, null);
            }

            case VillaStep.LockpickBuy:
            {
                if (input.Status == HouseOwnership.Owned)
                {
                    return (false, "owns-house", "the account owns a house: the purchase's lockpick is spent", null);
                }

                var pick = LockpickRule.Decide(input.Held.Lockpicks, input.Coins, LockpickFacts.CatalogPrice, input.LockpickNotes, input.LockpickNotesSay); // M6 16 E5a2: judged on what the Merchant's counter can pay with, not the coin alone
                return (pick.May, pick.Token, pick.Why, null);
            }

            case VillaStep.WritHandIn:
            {
                var pairs = input.Stones >= WritFacts.StonesPerWrit || (input.Stones >= 1 && input.HalfPending);
                return pairs
                    ? (true, "hand-in", string.Create(c, $"{input.Stones} stones held{(input.HalfPending ? " and a half pair at the Agent" : string.Empty)}: to writs at Holtburg's Agent"), null)
                    : (false, input.Stones == 0 ? "no-stones" : "one-stone-kept", string.Create(c, $"{input.Stones} stones held, no half pair at the Agent"), null);
            }

            case VillaStep.WritFetch:
            {
                if (!pace.Due)
                {
                    return (false, pace.Token, pace.Why, null);
                }

                var cave = WritRule.NextFetch(input.Caves, input.Timers, input.Now, out var why);
                return cave is null ? (false, "no-cave-due", pace.Why + "; " + why, null) : (true, pace.Token, pace.Why + "; " + why, cave);
            }

            case VillaStep.Storage:
            {
                if (input.Status != HouseOwnership.Owned)
                {
                    return (false, "no-house", "the account owns no house yet", null);
                }

                if (input.StoreItems == 0)
                {
                    return (false, "nothing-to-store", "no great-cantrip piece or kept-aside find is held off the body", null);
                }

                var full = input.ReviewFree == 0 ? "the review pack is full" : input.ReviewFree < 0 ? "the review pack's room is unread" : string.Create(c, $"the review pack has {input.ReviewFree} free");
                if (!input.StorageWalkReady)
                {
                    return (false, "walk-missing", string.Create(c, $"{input.StoreItems} finds to store ({full}), but TODO(villa-house-portal-walk): no plan reaches the house portal's room, so the trip would stop at storage:house-portal-walk"), null);
                }

                return (true, input.ReviewFree == 0 ? "review-full" : "town-trip", string.Create(c, $"{input.StoreItems} finds to store at the town trip ({full})"), null);
            }

            default:
                return (false, "none", "no such step", null);
        }
    }
}

/// <summary>The villa's reads of the live state (M6 villa): the price held, the stones held, the rent lines owed (shared by the steps and the schedule, so both read the packs one way).</summary>
public static class VillaReads
{
    /// <summary>
    /// What the packs hold toward a price: pyreal stacks, trade notes' face value, writs and lockpicks (worn items left out). M6 16 E5a1: this is not the quartermaster's purse read (<see cref="Quartermaster.PurseView"/>) and
    /// was left as it is, for it counts differently and the two cannot be one without changing an answer: the pyreals are the coin stacks' sizes (not the coin property), and a note is any item of the promissory type at
    /// its stack's own value (not one of ACE's fourteen trade notes, wcids 2621-2627, 7374-7377 and 20628-20630 at its face).
    /// <para>M6 16 E5a2: a stack's Value is the whole stack's, on the wire as in ACE: a stackable's Value is its unit value times its size (<c>Stackable.SetEphemeralValues</c>, <c>Stackable.cs</c> 48-62;
    /// <c>WorldObject.SetStackSize</c>, <c>WorldObject_Properties.cs</c> 3130-3140 @95ef426d), the description's weenie header carries that Value (<c>WorldObject_Networking.cs</c> 99-100) and the stack-size message carries
    /// it again (<c>GameMessageSetStackSize.cs</c> 15-16). So a stack with no stack-size message since login counts at its description's Value as it is; it was multiplied by the stack's size, which counted three 250,000
    /// notes as 2,250,000.</para>
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A scratch list for the guids.</param>
    public static PriceHeld Price(GameState state, List<uint> scratch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scratch);
        GearInventory.Collect(state, scratch);
        long pyreals = 0, notes = 0;
        int writs = 0, picks = 0;
        foreach (var guid in scratch)
        {
            if (!state.Objects.TryGet(guid, out var o) || state.Self.Inventory.TryGetEquipped(guid, out _))
            {
                continue;
            }

            var stack = GearInventory.StackOf(state, o);
            switch (o.Weenie.WeenieClassId)
            {
                case VillaFacts.PyrealWcid:
                    pyreals += stack;
                    continue;
                case VillaFacts.WritWcid:
                    writs += stack;
                    continue;
                case VillaFacts.LockpickWcid:
                    picks += stack;
                    continue;
            }

            if ((o.ItemType & VendorStock.PromissoryNoteType) != 0)
            {
                notes += state.Self.Inventory.TryGetStack(guid, out _, out var value) ? value : o.Weenie.Value ?? 0; // M6 16 E5a2: the description's Value is the stack's already (see above); it is not multiplied by the size
            }
        }

        return new PriceHeld(pyreals, notes, writs, picks);
    }

    /// <summary>The minor stones held (any of the four), stacks summed.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A scratch list for the guids.</param>
    public static int Stones(GameState state, List<uint> scratch) => WritFacts.StoneWcids.Sum(w => GearInventory.CountOf(state, scratch, w));

    /// <summary>The owned house's rent lines still owed: weenie, name, count left.</summary>
    /// <param name="house">The owned house, or null.</param>
    public static IReadOnlyList<(uint Wcid, string Name, long Left)> Owed(OwnedHouse? house) =>
        house?.Rent.Where(r => r.Remaining > 0).Select(r => (r.WeenieClassId, r.Required == 1 ? r.Name : r.PluralName, (long)r.Remaining)).ToList() ?? [];

    /// <summary>The writs one period's rent takes: the owned house's writ line, else the block guardian's (<see cref="VillaFacts.RentWrits"/>).</summary>
    /// <param name="house">The owned house, or null.</param>
    public static int RentWrits(OwnedHouse? house) => house?.Rent.FirstOrDefault(r => r.WeenieClassId == VillaFacts.WritWcid) is { WeenieClassId: VillaFacts.WritWcid } line ? line.Required : VillaFacts.RentWrits;
}

/// <summary>The villa steps as a station kind (<see cref="StationKinds.Villa"/>; M6 villa): the spec's <see cref="StepArgument"/> names the step, its other arguments the step's own.</summary>
public static class VillaStations
{
    /// <summary>The step's argument: a <see cref="VillaSchedule.Name"/> of a station step.</summary>
    public const string StepArgument = "step";

    /// <summary>The hand-in's argument: <c>yes</c> when a half pair waits at the Agent.</summary>
    public const string HalfPendingArgument = "half-pending";

    /// <summary>The purchase's argument: the account's opening, round-trip UTC (<c>o</c>); absent, <see cref="VillaFacts.ClimberAccountOpensVillas"/>.</summary>
    public const string OpensAtArgument = "opens-at";

    /// <summary>The rent's argument: the margin in hours.</summary>
    public const string MarginHoursArgument = "margin-h";

    /// <summary>The storage trip's argument: the items, hex guids separated by commas.</summary>
    public const string ItemsArgument = "items";

    /// <summary>The seconds a villa station is expected to take (the purchase's walk of about 2.5 km from Holtburg each way is the longest; the quest station's errand's own figure).</summary>
    public const double ExpectedSeconds = 900;

    /// <summary>The spec of <paramref name="step"/> (not the fetch: that is the quest station's).</summary>
    /// <param name="step">The step.</param>
    /// <param name="halfPending">The hand-in's half pair.</param>
    /// <param name="items">The storage trip's items.</param>
    /// <param name="opensAt">The purchase's opening (null: <see cref="VillaFacts.ClimberAccountOpensVillas"/>).</param>
    public static StationSpec Spec(VillaStep step, bool halfPending = false, IReadOnlyList<uint>? items = null, DateTimeOffset? opensAt = null)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { [StepArgument] = VillaSchedule.Name(step) };
        switch (step)
        {
            case VillaStep.WritHandIn when halfPending:
                args[HalfPendingArgument] = "yes";
                break;
            case VillaStep.Purchase when opensAt is { } at:
                args[OpensAtArgument] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                break;
            case VillaStep.Rent:
                args[MarginHoursArgument] = VillaSchedule.RentMargin.TotalHours.ToString("0.###", CultureInfo.InvariantCulture);
                break;
            case VillaStep.Storage:
                args[ItemsArgument] = string.Join(',', (items ?? []).Select(g => g.ToString("X8", CultureInfo.InvariantCulture)));
                break;
            case VillaStep.WritFetch or VillaStep.None:
                throw new ArgumentException($"the villa step {VillaSchedule.Name(step)} has no villa station", nameof(step));
        }

        return new StationSpec(VillaSchedule.Name(step), StationKinds.Villa, null, ExpectedSeconds, args);
    }

    /// <summary>The station of a villa spec; an unknown step fails by name.</summary>
    /// <param name="spec">The spec.</param>
    public static IStation Create(StationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var step = spec.Arguments.TryGetValue(StepArgument, out var s) ? s : string.Empty;
        return step switch
        {
            WritHandInStation.StationName => new WritHandInStation(spec.Arguments.TryGetValue(HalfPendingArgument, out var half) && half == "yes"),
            LockpickBuyStation.StationName => new LockpickBuyStation(),
            VillaPurchaseStation.StationName => new VillaPurchaseStation(VillaFacts.Block1401, spec.Arguments.TryGetValue(OpensAtArgument, out var at) ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : VillaFacts.ClimberAccountOpensVillas),
            StorageTripStation.StationName => new StorageTripStation(Items(spec)),
            RentStation.StationName => new RentStation(TimeSpan.FromHours(spec.Arguments.TryGetValue(MarginHoursArgument, out var h) ? double.Parse(h, CultureInfo.InvariantCulture) : VillaSchedule.RentMargin.TotalHours)),
            _ => throw new NotSupportedException($"TODO(villa-station-step): the villa station has no step '{step}' (station '{spec.Name}')"),
        };
    }

    private static List<uint> Items(StationSpec spec) =>
        spec.Arguments.TryGetValue(ItemsArgument, out var items) && items.Length > 0
            ? [.. items.Split(',').Select(g => uint.Parse(g, NumberStyles.HexNumber, CultureInfo.InvariantCulture))]
            : [];
}
