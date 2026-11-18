using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>One stock line as the trigger counted it when it called a trip (<see cref="TripOrder.Lines"/>).</summary>
/// <param name="Name">The line's name.</param>
/// <param name="Wcid">The ware's weenie class id.</param>
/// <param name="Held">The count held.</param>
/// <param name="TripUnder">The line's trip line.</param>
/// <param name="StopUnder">The line's stop line.</param>
/// <param name="Keep">The count a visit buys the line to (the profile's keep, raised by a component need).</param>
/// <param name="Clear">The count that lifts the line clear of its trip line (<see cref="LogisticsCounters.ClearOf"/>): what the trigger's purse test priced.</param>
/// <param name="Carried">True when no vendor of the ground sells it.</param>
/// <param name="FloorOnly">True when it is floor-only (carried, needed by the war options alone).</param>
/// <param name="Suspended">True when the line is suspended (every focus held, or the drinks ditched).</param>
/// <param name="HeldBack">True when its trip was held back for the purse.</param>
public sealed record TripLine(string Name, uint Wcid, int Held, int TripUnder, int StopUnder, int Keep, int Clear, bool Carried, bool FloorOnly, bool Suspended, bool HeldBack);

/// <summary>
/// M6 16 E5d: the verdict a trip carries: what the trigger decided when it called the trip (the reason, every stock line with its held and wanted counts, the purse it read, the sale it estimated), made once and taken through
/// the logistics station to the town trip. The town trip counts again at home and says where the two differ (<see cref="AtHome"/>); the station says after the visit whether the reason was served (<see cref="After"/>). Made
/// when a trip is called, never per check.
/// </summary>
/// <param name="Ground">The ground's key.</param>
/// <param name="Verdict">The counters' verdict (or the hour's, <see cref="LogisticsVerdict.HourUp"/>); <see cref="LogisticsVerdict.AllWell"/> for a trip no counters call (the buff bot's, a lapsed line's).</param>
/// <param name="Trigger">The reason's token (<see cref="LogisticsVerdict.Trigger"/>, or the caller's name for a trip no counters call).</param>
/// <param name="Reason">The reason in words.</param>
/// <param name="Lines">The stock lines as counted by the trigger's last check.</param>
/// <param name="Coin">The coin read (unread counted as 0).</param>
/// <param name="Notes">The face of the trade notes held.</param>
/// <param name="SaleValue">The Value of the loot the last sale estimate counted, or null when the trigger made none.</param>
/// <param name="SalePayout">The pyreals that estimate expected, or null.</param>
/// <param name="CalledAt">The session clock when the trip was called.</param>
public sealed record TripOrder(string Ground, LogisticsVerdict Verdict, string Trigger, string Reason, IReadOnlyList<TripLine> Lines, long Coin, long Notes, long? SaleValue, long? SalePayout, double CalledAt)
{
    /// <summary>The purse the trigger read: the coin and the notes' face.</summary>
    public long Purse => Coin + Notes;

    /// <summary>
    /// True when the reason is one the counters read again at home with no more than the pack and the purse: a stock line, the burden, the slots, a full pack, a focus, a learn, a sale. False for the schedule's (it counts the
    /// outer loop's buff cycles), the hour's, the buff bot's and a lapsed line's, which no counters at home know.
    /// </summary>
    public bool CountersReadIt => Verdict.Cause is not (LogisticsCause.None or LogisticsCause.Cadence or LogisticsCause.Hour);

    /// <summary>The stock line the reason names, as the trigger counted it, or null.</summary>
    public TripLine? Named => Verdict.Line is { } line ? Lines.FirstOrDefault(l => l.Wcid == line.Wcid) : null;

    /// <summary>What the trigger said, in words with its numbers.</summary>
    public string Said => string.Create(CultureInfo.InvariantCulture, $"{Trigger} ({Reason}{(Named is { } n ? string.Create(CultureInfo.InvariantCulture, $", wanted {n.Keep}, clear of its line at {n.Clear}") : string.Empty)}; {PurseWords(Coin, Notes)}{SaleWords(SaleValue, SalePayout)})");

    private static string PurseWords(long coin, long notes) => string.Create(CultureInfo.InvariantCulture, $"purse {coin + notes}: coin {coin}, notes {notes}");

    private static string SaleWords(long? value, long? payout) => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"; sale estimate {v} Value, about {payout ?? 0} pyreals") : string.Empty;

    /// <summary>
    /// The check at home (or where <paramref name="where"/> says): <c>trip: the verdict said &lt;x&gt;, at home &lt;y&gt;: &lt;each difference&gt;</c>, or <c>trip: the verdict stands at home: &lt;x&gt;</c> when the reason, every
    /// stock count, the purse and the sale estimate are what the trigger read. Nothing is recomputed in silence: the caller acts on what it counted and this line says how that differs from what the trip was called for.
    /// </summary>
    /// <param name="counters">The session's counters, after the check that gave <paramref name="now"/>.</param>
    /// <param name="now">The verdict of the check just made.</param>
    /// <param name="state">The session's read model.</param>
    /// <param name="where">Where the check was made, for the line.</param>
    public string AtHome(LogisticsCounters counters, LogisticsVerdict now, GameState state, string where = "at home")
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(state);
        var differs = new List<string>();
        var trigger = now.Trigger;
        if (CountersReadIt && !string.Equals(trigger, Trigger, StringComparison.Ordinal))
        {
            differs.Add($"the reason {Trigger} is now {trigger}");
        }

        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (Lines.FirstOrDefault(l => l.Wcid == line.Wcid) is { } then && then.Held != counters.HeldOf(i))
            {
                differs.Add(string.Create(CultureInfo.InvariantCulture, $"{line.Name} {then.Held} held is now {counters.HeldOf(i)}"));
            }
        }

        var (coin, notes) = ((long)PurseView.CoinUnreadAsZero(state), PurseView.NotesHeld(state));
        if (coin + notes != Purse)
        {
            differs.Add(string.Create(CultureInfo.InvariantCulture, $"the purse {Purse} is now {coin + notes}"));
        }

        var sale = counters.SaleEstimate;
        if (SaleValue is { } said && sale is { } found && found.Value != said)
        {
            differs.Add(string.Create(CultureInfo.InvariantCulture, $"the sale estimate {said} Value is now {found.Value}"));
        }

        if (differs.Count == 0)
        {
            return $"trip: the verdict stands {where}: {Said}";
        }

        return string.Create(CultureInfo.InvariantCulture, $"trip: the verdict said {Said}, {where} {trigger} ({now.Reason}; {PurseWords(coin, notes)}{SaleWords(sale?.Value, sale?.Payout)}): {string.Join("; ", differs)}");
    }

    /// <summary>
    /// True when the visit served the reason the trip was called for: the named stock line is at or over its trip line, or the counters no longer give that reason. Null for a reason no counters read at home
    /// (<see cref="CountersReadIt"/>).
    /// </summary>
    /// <param name="counters">The session's counters, after the check that gave <paramref name="after"/>.</param>
    /// <param name="after">The verdict of the check after the visit.</param>
    public bool? Served(LogisticsCounters counters, LogisticsVerdict after)
    {
        ArgumentNullException.ThrowIfNull(counters);
        if (!CountersReadIt)
        {
            return null;
        }

        if (Verdict.Line is { } named)
        {
            for (var i = 0; i < counters.LineCount; i++)
            {
                if (counters.LineAt(i).Wcid == named.Wcid)
                {
                    return counters.HeldOf(i) >= counters.LineAt(i).TripUnder;
                }
            }
        }

        return !string.Equals(after.Trigger, Trigger, StringComparison.Ordinal);
    }

    /// <summary>The check after the visit, in words: whether the reason the trip was called for was served (<see cref="Served"/>), with the named line's counts; a reason left standing is a warning.</summary>
    /// <param name="counters">The session's counters, after the check that gave <paramref name="after"/>.</param>
    /// <param name="after">The verdict of the check after the visit.</param>
    public string After(LogisticsCounters counters, LogisticsVerdict after)
    {
        if (Served(counters, after) is not { } served)
        {
            return $"trip: check after: the verdict's reason ({Trigger}) is not one the counters read again at home (the schedule's, the hour's, the buff bot's or a lapsed line's): nothing to compare";
        }

        var line = string.Empty;
        if (Named is { } then)
        {
            for (var i = 0; i < counters.LineCount; i++)
            {
                if (counters.LineAt(i).Wcid == then.Wcid)
                {
                    line = string.Create(CultureInfo.InvariantCulture, $" ({then.Name} {then.Held} held when called, {counters.HeldOf(i)} now, its trip line {counters.LineAt(i).TripUnder})");
                }
            }
        }

        return served
            ? $"trip: check after: the verdict's reason is served: {Trigger}{line}; the counters now say {after.Kind}: {after.Reason}"
            : $"WARNING: trip: check after: the verdict's reason still stands after the visit: {Trigger}{line}; the counters say {after.Kind}: {after.Reason}";
    }
}

/// <summary>
/// M6 item 16, slice E5d: <b>the verdict a trip carries</b> (<see cref="Call(HuntingGround, LogisticsVerdict, double)"/>, <see cref="Take"/>, <see cref="InHand"/>). The reason a trip was called for was worked out up to three times: by the
/// trigger, by the logistics station at the fight's end and by the town trip at home, each from the pack as it stood then. The trigger's verdict is now made once (<see cref="TripOrder"/>) and travels with the trip: the
/// logistics station takes it, says where its own count at the fight's end differs and goes on it (a stop line no trip restocks still stops; a pack it cannot count is still no trip), the town trip counts again at home
/// and says where the two differ, and the station says after the visit whether the reason was served.
/// </summary>
public sealed partial class TripDesk
{
    private TripOrder? _called;

    /// <summary>The trip the trigger called that no logistics station has taken yet, or null.</summary>
    public TripOrder? Called => _called;

    /// <summary>The trip in hand: taken by the logistics station that is running (<see cref="Take"/>), read by its town trip; null outside a trip, and for a trip no trigger called (a forced one, a plan's).</summary>
    public TripOrder? InHand { get; private set; }

    /// <summary>
    /// The trigger calls a trip on the counters' verdict (or the hour's): what it decided is made into the trip's order, once, from the session's counters as its check just left them. Allocates: called when a trip is
    /// called, never per check.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="verdict">The verdict the trip is called on.</param>
    /// <param name="now">The session clock.</param>
    public TripOrder Call(HuntingGround ground, LogisticsVerdict verdict, double now) => Call(ground, verdict, verdict.Trigger, verdict.Reason, now);

    /// <summary>The trigger calls a trip no counters call (the buff bot's lines about to lapse, a line down in the field): the order carries the caller's name and words with the counts and the purse as they stand.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="trigger">The caller's token.</param>
    /// <param name="reason">Why, in words.</param>
    /// <param name="now">The session clock.</param>
    public TripOrder Call(HuntingGround ground, string trigger, string reason, double now) => Call(ground, LogisticsVerdict.AllWell, trigger, reason, now);

    private TripOrder Call(HuntingGround ground, LogisticsVerdict verdict, string trigger, string reason, double now)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        ArgumentNullException.ThrowIfNull(reason);
        var counters = On(ground);
        var lines = new TripLine[counters.LineCount];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = counters.LineAt(i);
            lines[i] = new TripLine(line.Name, line.Wcid, counters.HeldOf(i), line.TripUnder, line.StopUnder, line.Keep, LogisticsCounters.ClearOf(line), counters.IsCarried(i), counters.IsFloorOnly(i), counters.IsSuspended(i), counters.IsHeldBack(i));
        }

        var sale = counters.SaleEstimate;
        _called = new TripOrder(ground.Key, verdict, trigger, reason, lines, PurseView.CoinUnreadAsZero(_state), PurseView.NotesHeld(_state), sale?.Value, sale?.Payout, now);
        return _called;
    }

    /// <summary>A trip that was called and not taken is no longer due: the fight went on, or she died. Allocation-free.</summary>
    public void Void() => _called = null;

    /// <summary>
    /// The logistics station takes the trip the trigger called for <paramref name="ground"/>: it is in hand until <see cref="Close"/>. Null when the trigger called none (a forced trip, a plan's): the station's own count is
    /// then the only verdict, as before.
    /// </summary>
    /// <param name="ground">The station's ground.</param>
    public TripOrder? Take(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        InHand = _called is { } called && string.Equals(called.Ground, ground.Key, StringComparison.Ordinal) ? called : null;
        _called = null;
        return InHand;
    }

    /// <summary>The logistics station ended: no trip is in hand.</summary>
    public void Close() => InHand = null;
}
