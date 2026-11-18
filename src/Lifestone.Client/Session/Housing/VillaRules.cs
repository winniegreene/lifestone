using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Housing;

/// <summary>What the packs hold toward a villa's price or rent: pyreal stacks, the trade notes' face value, writs and lockpicks (the read the steps take before they leave and again at the guardian).</summary>
/// <param name="Pyreals">The pyreals in held stacks (weenie 273).</param>
/// <param name="NoteValue">The face value of the held trade notes (ItemType PromissoryNote): ACE's <c>HousePayment.GetConsumeItems</c> pays the money line with them too.</param>
/// <param name="Writs">The Writs of Refuge held.</param>
/// <param name="Lockpicks">The Crude Lockpicks held.</param>
public readonly record struct PriceHeld(long Pyreals, long NoteValue, int Writs, int Lockpicks)
{
    /// <summary>The money the packs bring to a money line.</summary>
    public long Money => Pyreals + NoteValue;

    /// <summary>One line: <c>pyreals=… notes=… writs=… lockpicks=…</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"pyreals={Pyreals} notes={NoteValue} writs={Writs} lockpicks={Lockpicks}");
}

/// <summary>The answer of a villa rule: may the step run (<see cref="May"/>), a token (the verdict's <c>reason=</c>, kebab case), the reason in words with every number, and what is missing (one entry a line short).</summary>
/// <param name="May">True when the step may run.</param>
/// <param name="Token">The reason as a token.</param>
/// <param name="Why">The reason in words.</param>
/// <param name="Missing">What is missing, one entry a line (empty when nothing is).</param>
public sealed record VillaDecision(bool May, string Token, string Why, IReadOnlyList<string> Missing)
{
    /// <summary>A yes.</summary>
    internal static VillaDecision Yes(string token, string why) => new(true, token, why, []);

    /// <summary>A no.</summary>
    internal static VillaDecision No(string token, string why, IReadOnlyList<string>? missing = null) => new(false, token, why, missing ?? []);
}

/// <summary>
/// The writ run's rules, pure (M6 villa): how many stones the writs still wanted need, which stones a hand-in gives, and which stone fetch is due next. ACE's give flow (<see cref="WritFacts.FirstStoneLine"/>,
/// <see cref="WritFacts.WritLine"/>): the first stone of a pair stamps <c>MinorStoneWrit</c>, the second gives the writ and erases it, so a pair is any two stones and a half pair left on the server (a cut run) makes the next stone a writ.
/// </summary>
public static class WritRule
{
    /// <summary>What the writs still wanted need: the writs short and the stones to fetch (two a writ, less the stones held and a half pair the server holds).</summary>
    /// <param name="writsHeld">The writs held.</param>
    /// <param name="stonesHeld">The minor stones held (any of the four).</param>
    /// <param name="writsWanted">The writs wanted (the purchase's 5, a rent's 2, or a caller's sum).</param>
    /// <param name="halfPending">True when the server holds a first stone (the last hand-in ended on <see cref="WritFacts.FirstStoneLine"/>).</param>
    public static (int WritsShort, int StonesToFetch, string Why) Need(int writsHeld, int stonesHeld, int writsWanted, bool halfPending = false)
    {
        var writsShort = Math.Max(0, writsWanted - writsHeld);
        var stonesToFetch = Math.Max(0, (writsShort * WritFacts.StonesPerWrit) - stonesHeld - (halfPending ? 1 : 0));
        return (writsShort, stonesToFetch, string.Create(CultureInfo.InvariantCulture, $"writs {writsHeld} held of {writsWanted} wanted ({writsShort} short); stones {stonesHeld} held{(halfPending ? " and a half pair at the Agent" : string.Empty)}; {stonesToFetch} to fetch"));
    }

    /// <summary>
    /// The hand-in's plan: the stones to give, one at a time, in order (a stack gives one a time and is named once per stone), so that every give but a lone last one makes a writ. A lone stone with no half pending is kept
    /// for the next run (<c>one-stone-kept</c>); none held is <c>no-stones</c>.
    /// </summary>
    /// <param name="stones">The minor stones held: guid and stack.</param>
    /// <param name="halfPending">True when the server holds a first stone.</param>
    public static (IReadOnlyList<uint> Gives, int Writs, VillaDecision Decision) PlanHandIn(IReadOnlyList<(uint Guid, int Stack)> stones, bool halfPending)
    {
        ArgumentNullException.ThrowIfNull(stones);
        var units = stones.SelectMany(s => Enumerable.Repeat(s.Guid, Math.Max(0, s.Stack))).ToList();
        var total = units.Count + (halfPending ? 1 : 0);
        var writs = total / WritFacts.StonesPerWrit;
        var gives = (writs * WritFacts.StonesPerWrit) - (halfPending ? 1 : 0);
        var c = CultureInfo.InvariantCulture;
        if (units.Count == 0)
        {
            return ([], 0, VillaDecision.No("no-stones", "no minor stone is held"));
        }

        if (gives <= 0)
        {
            return ([], 0, VillaDecision.No("one-stone-kept", "one minor stone is held and no half pair is at the Agent: it is kept for the next run (a lone stone would only stamp MinorStoneWrit)"));
        }

        var kept = units.Count - gives;
        return (units.Take(gives).ToList(), writs, VillaDecision.Yes("hand-in", string.Create(c, $"{units.Count} stones held{(halfPending ? " and a half pair at the Agent" : string.Empty)}: give {gives} for {writs} writ{(writs == 1 ? string.Empty : "s")}{(kept > 0 ? $", keep {kept}" : string.Empty)}")));
    }

    /// <summary>
    /// The first of <paramref name="caves"/> whose fetch is due by the timers (in the given order: <see cref="WritFacts.Caves"/>, the nearest run first), or null with when each comes due. A cave whose route carries a
    /// <see cref="StoneCave.RouteTodo"/> is passed by, named on the line (a loud stop, never a walk the dats did not establish).
    /// </summary>
    /// <param name="caves">The caves in the order to try.</param>
    /// <param name="timers">The character's quest timers.</param>
    /// <param name="now">The time now (UTC).</param>
    /// <param name="why">The chosen cave, or every cave's next due time.</param>
    public static StoneCave? NextFetch(IReadOnlyList<StoneCave> caves, QuestTimers timers, DateTimeOffset now, out string why)
    {
        ArgumentNullException.ThrowIfNull(caves);
        ArgumentNullException.ThrowIfNull(timers);
        var waiting = new List<string>();
        foreach (var cave in caves)
        {
            if (cave.RouteTodo is { } todo)
            {
                waiting.Add($"{cave.QuestKey} passed by ({todo.Split(':')[0]})");
                continue;
            }

            var quest = QuestCatalog.Get(cave.QuestKey);
            if (timers.IsDue(quest, now))
            {
                var carries = quest.Todos.Select(t => t.Split(':')[0]).Distinct(StringComparer.Ordinal).ToList();
                why = $"{cave.QuestKey} is due ({cave.StoneName}, its pick-up timer {cave.QuestRow} passed)" + (carries.Count == 0 ? string.Empty : "; its file carries " + string.Join(", ", carries))
                    + (waiting.Count == 0 ? string.Empty : "; waiting: " + string.Join(", ", waiting));
                return cave;
            }

            waiting.Add(string.Create(CultureInfo.InvariantCulture, $"{cave.QuestKey} due {timers.NextDue(quest):yyyy-MM-ddTHH:mm:ssZ}"));
        }

        why = "no stone fetch is due: " + string.Join(", ", waiting);
        return null;
    }
}

/// <summary>The Crude Lockpick buy's rule, pure (M6 villa): one is wanted for the purchase; refused when one is held, and when the coin does not cover the price nor, with it, the trade notes the Merchant's counter can change (M6 16 E5a2).</summary>
public static class LockpickRule
{
    /// <summary>The decision.</summary>
    /// <param name="held">The lockpicks held.</param>
    /// <param name="coins">The coin held (CoinValue; -1 unread).</param>
    /// <param name="price">The price of one (the catalog's or the wire's).</param>
    /// <param name="notes">M6 16 E5a2: the faces of the trade notes the Merchant's counter can change (0: none held, none counted, or a caller that judges the coin alone): a short coin the notes cover is due, for the buy is the vendor counter's, which changes a note first.</param>
    /// <param name="notesSay">M6 16 E5a2: what the caller says of the notes it counted or left out (<see cref="Quartermaster.PurseView.CounterSays"/>), added to the reason when the coin is short; null or empty: nothing.</param>
    public static VillaDecision Decide(int held, long coins, int price, long notes = 0, string? notesSay = null)
    {
        var c = CultureInfo.InvariantCulture;
        if (held >= VillaFacts.PriceLockpicks)
        {
            return VillaDecision.No("held", string.Create(c, $"{held} Crude Lockpick held: the purchase takes {VillaFacts.PriceLockpicks}"));
        }

        if (coins < 0)
        {
            return VillaDecision.No("purse-unread", "the coin (CoinValue) is not read");
        }

        if (coins < price)
        {
            var say = string.IsNullOrEmpty(notesSay) ? string.Empty : "; " + notesSay;
            notes = Math.Max(0, notes);
            if (coins + notes >= price)
            {
                return VillaDecision.Yes("due", string.Create(c, $"no Crude Lockpick held; one at {price}: the coin {coins} is short by {price - coins} and the trade notes held ({notes}) cover it, so a note is changed at the Merchant's counter{say}"));
            }

            var rest = notes > 0 ? string.Create(c, $" (the trade notes held, {notes}, do not cover the rest)") : string.Empty;
            return VillaDecision.No("purse-short", string.Create(c, $"the coin {coins} does not cover one Crude Lockpick at {price}{rest}{say}"), [string.Create(c, $"pyreals {price - coins - notes} short")]);
        }

        return VillaDecision.Yes("due", string.Create(c, $"no Crude Lockpick held; one at {price} within the coin {coins}"));
    }
}

/// <summary>
/// The villa purchase's rule, pure (M6 villa): refused before the account may buy (<see cref="VillaFacts.ClimberAccountOpensVillas"/>: at once, as the server mod turns ACE's 15-day rule off; <see cref="VillaFacts.ClimberAccountOpensVillasRetail"/> under the retail flag), while the house status is unread (the step queries first), when a house
/// is owned (ACE: one per account), below the villa's level, and when the price is not all in the packs (pyreals and notes, writs, the lockpick), each short line named.
/// </summary>
public static class VillaPurchaseRule
{
    /// <summary>The price's lines short of <paramref name="held"/>, one entry each, or empty.</summary>
    /// <param name="held">What the packs hold.</param>
    /// <param name="pyreals">The money line.</param>
    /// <param name="writs">The writs.</param>
    /// <param name="lockpicks">The lockpicks.</param>
    public static IReadOnlyList<string> Missing(PriceHeld held, long pyreals, int writs, int lockpicks)
    {
        var c = CultureInfo.InvariantCulture;
        var missing = new List<string>();
        if (held.Money < pyreals)
        {
            missing.Add(string.Create(c, $"pyreals {pyreals - held.Money:N0} short (coins {held.Pyreals:N0} and notes {held.NoteValue:N0} of {pyreals:N0})"));
        }

        if (held.Writs < writs)
        {
            missing.Add(string.Create(c, $"Writ of Refuge {writs - held.Writs} short ({held.Writs} of {writs})"));
        }

        if (held.Lockpicks < lockpicks)
        {
            missing.Add(string.Create(c, $"Crude Lockpick {lockpicks - held.Lockpicks} short ({held.Lockpicks} of {lockpicks})"));
        }

        return missing;
    }

    /// <summary>The decision.</summary>
    /// <param name="now">The time now (UTC).</param>
    /// <param name="opensAt">The first instant the account may buy anything but an apartment (<see cref="VillaFacts.ClimberAccountOpensVillas"/> for the climber's).</param>
    /// <param name="block">The block to buy in: its kind, price and level.</param>
    /// <param name="status">What the client knows of the account's house.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="held">What the packs hold.</param>
    public static VillaDecision Decide(DateTimeOffset now, DateTimeOffset opensAt, VillaBlock block, HouseOwnership status, int level, PriceHeld held)
    {
        ArgumentNullException.ThrowIfNull(block);
        var c = CultureInfo.InvariantCulture;
        if (block.Type != Lifestone.Net.Messages.HouseType.Apartment && now < opensAt)
        {
            return VillaDecision.No("too-early", string.Create(c, $"the account may buy a {block.Type.ToString().ToLowerInvariant()} from {opensAt:yyyy-MM-dd HH:mm:ss}Z (ACE's house_15day_account, restored by the server's LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace: 15 days after the account was made), {(opensAt - now).TotalHours:0.0} h from now"));
        }

        switch (status)
        {
            case HouseOwnership.Unknown:
                return VillaDecision.No("status-unread", "the house status is unread: ask the server first (HouseQuery)");
            case HouseOwnership.Owned:
                return VillaDecision.No("owns-house", "the account already owns a house (ACE: one per account)");
        }

        if (level < block.MinLevel)
        {
            return VillaDecision.No("below-level", string.Create(c, $"level {level} is under the block's {block.MinLevel}"));
        }

        var missing = Missing(held, block.Price.Pyreals, block.Price.Writs, block.Price.Lockpicks);
        if (missing.Count > 0)
        {
            return VillaDecision.No("price-short", "the price is not all in the packs: " + string.Join("; ", missing), missing);
        }

        return VillaDecision.Yes("may", string.Create(c, $"the account may buy, owns no house, level {level}; the packs hold the price ({held})"));
    }
}

/// <summary>One item the storage trip is asked to put away.</summary>
/// <param name="Guid">The item.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsPack">True for a container (a pack takes the chest's pack slot).</param>
/// <param name="Attuned">PropertyInt 114 above 0 (from the appraisal), or null when unread.</param>
/// <param name="Held">True when the player holds it in a pack.</param>
/// <param name="Worn">True when it is wielded or worn.</param>
public readonly record struct KeepItem(uint Guid, string Name, bool IsPack, bool? Attuned, bool Held, bool Worn);

/// <summary>
/// The storage trip's rule, pure (M6 villa): which of the caller's items go into a chest that already holds some. ACE refuses an attuned item into a container the player does not own (<c>Player_Inventory.cs:2326</c>), a
/// worn item must come off first (the caller's choice: the trip only moves pack items), and a villa chest holds 26 items and one pack (<see cref="VillaFacts.ChestItems"/>, <see cref="VillaFacts.ChestPacks"/>).
/// </summary>
public static class StorageRule
{
    /// <summary>The moves into one chest, in the caller's order, and each refused item with its token (<c>attuned</c>, <c>attuned-unread</c>, <c>not-held</c>, <c>worn</c>, <c>chest-full</c>, <c>pack-slot-full</c>).</summary>
    /// <param name="items">The items, in order.</param>
    /// <param name="chestItems">The items the chest already holds.</param>
    /// <param name="chestPacks">The packs the chest already holds.</param>
    /// <param name="itemSlots">The chest's item slots.</param>
    /// <param name="packSlots">The chest's pack slots.</param>
    public static (IReadOnlyList<KeepItem> Moves, IReadOnlyList<(KeepItem Item, string Token)> Refused) Plan(IReadOnlyList<KeepItem> items, int chestItems, int chestPacks, int itemSlots = VillaFacts.ChestItems, int packSlots = VillaFacts.ChestPacks)
    {
        ArgumentNullException.ThrowIfNull(items);
        var moves = new List<KeepItem>();
        var refused = new List<(KeepItem, string)>();
        var freeItems = Math.Max(0, itemSlots - chestItems);
        var freePacks = Math.Max(0, packSlots - chestPacks);
        foreach (var item in items)
        {
            string? token = item switch
            {
                { Held: false } => "not-held",
                { Worn: true } => "worn",
                { Attuned: null } => "attuned-unread",
                { Attuned: true } => "attuned",
                { IsPack: true } when freePacks == 0 => "pack-slot-full",
                { IsPack: false } when freeItems == 0 => "chest-full",
                _ => null,
            };
            if (token is not null)
            {
                refused.Add((item, token));
                continue;
            }

            if (item.IsPack)
            {
                freePacks--;
            }
            else
            {
                freeItems--;
            }

            moves.Add(item);
        }

        return (moves, refused);
    }
}

/// <summary>
/// The rent's rule, pure (M6 villa): due when the account owns a house, its rent is not all paid this period, and the period ends within the caller's margin; a rent already late is due at once (ACE evicts at the
/// period's end, <c>HouseManager.HandleEviction</c>). The packs must hold what is left of each rent line (named when short).
/// </summary>
public static class RentRule
{
    /// <summary>The decision.</summary>
    /// <param name="status">What the client knows of the account's house.</param>
    /// <param name="rentPaid">True when every rent line is paid this period (<see cref="OwnedHouse.IsRentPaid"/>).</param>
    /// <param name="rentDue">The period's end (Unix seconds, <see cref="OwnedHouse.RentDue"/>).</param>
    /// <param name="nowUnix">The time now (Unix seconds).</param>
    /// <param name="margin">How long before the end the rent is paid.</param>
    /// <param name="owed">The rent lines still owed: name, count left; the money line's count in pyreals.</param>
    /// <param name="held">What the packs hold.</param>
    public static VillaDecision Decide(HouseOwnership status, bool rentPaid, long rentDue, long nowUnix, TimeSpan margin, IReadOnlyList<(uint Wcid, string Name, long Left)> owed, PriceHeld held)
    {
        ArgumentNullException.ThrowIfNull(owed);
        var c = CultureInfo.InvariantCulture;
        switch (status)
        {
            case HouseOwnership.Unknown:
                return VillaDecision.No("status-unread", "the house status is unread: ask the server first (HouseQuery)");
            case HouseOwnership.None:
                return VillaDecision.No("no-house", "the account owns no house");
        }

        var left = rentDue - nowUnix;
        if (rentPaid)
        {
            return VillaDecision.No("paid", string.Create(c, $"the rent is paid this period; the next period begins in {left / 3600.0:0.0} h"));
        }

        if (left > margin.TotalSeconds)
        {
            return VillaDecision.No("not-yet", string.Create(c, $"the rent is owed by {DateTimeOffset.FromUnixTimeSeconds(rentDue):yyyy-MM-dd HH:mm}Z, {left / 3600.0:0.0} h from now, beyond the margin {margin.TotalHours:0.#} h"));
        }

        var missing = new List<string>();
        foreach (var (wcid, name, count) in owed)
        {
            var have = wcid == VillaFacts.PyrealWcid ? held.Money : wcid == VillaFacts.WritWcid ? held.Writs : wcid == VillaFacts.LockpickWcid ? held.Lockpicks : 0;
            if (have < count)
            {
                missing.Add(string.Create(c, $"{name} {count - have:N0} short ({have:N0} of {count:N0})"));
            }
        }

        if (missing.Count > 0)
        {
            return VillaDecision.No("rent-short", "the rent is not all in the packs: " + string.Join("; ", missing), missing);
        }

        return VillaDecision.Yes(left < 0 ? "late" : "due", string.Create(c, $"the rent is owed by {DateTimeOffset.FromUnixTimeSeconds(rentDue):yyyy-MM-dd HH:mm}Z ({left / 3600.0:0.0} h), within the margin {margin.TotalHours:0.#} h; the packs hold it ({held})"));
    }
}
