using System.Globalization;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// What an errand's own steps put in her pack at their peak (<see cref="ErrandRoomRule.GainOf"/>), from the quest file and what she holds now.
/// </summary>
/// <param name="Items">The most items the errand's steps hold at once: a hunt-until's count less what she holds of it, a pick-up's and a buy's count; a combine takes two and gives one (its count off), a give takes its count.</param>
/// <param name="Slots">The main-pack slots those items take at their peak: one an item, or the stacks a held copy's stack size gives.</param>
/// <param name="Burden">Their burden at the peak, from a copy she holds; an item of which she holds no copy adds nothing and is named in <see cref="Unknown"/>.</param>
/// <param name="Unknown">The items whose burden is unknown (no copy held: the quest file names no burden), joined, or null.</param>
/// <param name="Fights">True when a step fights (hunt-until, kill): its fight loots sell loot as well, so it needs a slot and the load line.</param>
/// <param name="What">The gaining steps in words, or empty.</param>
public sealed record ErrandGain(int Items, int Slots, int Burden, string? Unknown, bool Fights, string What)
{
    /// <summary>True when a step gains items or fights (loot): only such an errand has its room checked.</summary>
    public bool Gains => Items > 0 || Fights;

    /// <summary>The main-pack slots the errand needs free: its peak, and one for a fight's loot when it gains nothing else.</summary>
    public int SlotsNeeded => Math.Max(Slots, Fights ? 1 : 0);
}

/// <summary>One room check's answer (<see cref="ErrandRoomRule.Decide"/>): the sale first, or the errand straight; <see cref="Why"/> names every line that holds, or the room she has.</summary>
public sealed record ErrandRoomVerdict(bool SaleFirst, string Why);

/// <summary>
/// M6 14 sell first (the owner's ruling, 2026-11-17: she must sell before she goes on this quest; live ladder58 12:23 UTC: the Atlan wand's start quest left home at burden 21,674 of 21,750 with 0 free main-pack slots, and the cave hunt left
/// loot and wanted motes for lack of room). Before an errand from home whose steps gain items (hunt-until, pick-up, buy, a kill's loot), her live pack is read against the errand's own steps, and the logistics station runs first
/// (<c>trigger=errand-room</c>: the vendors' sale by the existing sell rules, the scroll buys, the supplies) when any of these holds:
/// <list type="bullet">
/// <item>her burden is at or over the load line;</item>
/// <item>her burden with the errand's expected gain would cross it (the gain's burden from a copy she holds; unknown, the slots judge it);</item>
/// <item>her free main-pack slots are under what the errand's steps hold at their peak (combines and gives reduce it; a fight needs at least one);</item>
/// <item>a carried stock line is under its trip line (the counters' own verdict).</item>
/// </list>
/// Pure: the autopilot reads the live state and logs the check before and after (<c>AutopilotScenario.ErrandRoom.cs</c>).
/// </summary>
public static class ErrandRoomRule
{
    /// <summary>The logistics trip's trigger for the sale before an errand (<c>trigger=errand-room</c>).</summary>
    public const string Trigger = "errand-room";

    /// <summary>
    /// The errand's gain over the steps <paramref name="steps"/> runs (<c>&lt;from&gt;-&lt;to&gt;</c>, 0-based, inclusive; null or malformed: every step, as the station's own read would stop a malformed one by name).
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="steps">The station's range, or null.</param>
    /// <param name="held">How many of a weenie class she holds now.</param>
    /// <param name="copy">A held copy's burden each and stack size (null: none held).</param>
    public static ErrandGain GainOf(Quest quest, string? steps, Func<uint, int> held, Func<uint, (int BurdenEach, int MaxStack)?> copy)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(copy);
        var (first, last) = Range(steps, quest.Steps.Count);
        var running = new Dictionary<uint, int>(); // wcid (0: unnamed) -> items gained (or, negative, taken) by the errand's steps so far
        var peakItems = 0;
        var peakSlots = 0;
        var peakBurden = 0;
        var fights = false;
        var unknown = new List<string>();
        var what = new List<string>();
        var combined = new HashSet<uint>(); // M6 14 combine3: the sources of the no-roll links a hunt combines as it goes
        for (var i = first; i <= last; i++)
        {
            var step = quest.Steps[i];
            var wcid = step.Item?.Wcid ?? 0u;
            var transient = 0; // M6 14 combine3: the objects held on the way over the step's end state (a looted item before its combine)
            switch (step.Kind)
            {
                case QuestStepKind.HuntUntil:
                    fights = true;
                    if (step.Item?.Wcid is { } hunted)
                    {
                        var want = step.Count ?? 1;
                        var chain = QuestCombineChain.Of(quest, hunted);
                        var have = QuestCombineChain.HuntHeld(quest, step, held); // M6 14 combine3: the equivalents along the quest's no-roll chain
                        if (chain.Count > 1)
                        {
                            // M6 14 combine3: the hunt combines as it goes, so its room is the chain's end state, not the loose count: each item of the chain changes by its end count less what she holds now, and the most objects held on the
                            // way (a looted item before its pair is combined) is the step's peak
                            var (end, peakObjects) = QuestCombineChain.Simulate(chain, held, want);
                            var now = 0;
                            for (var j = 0; j < chain.Count; j++)
                            {
                                Add(running, chain[j], end[j] - held(chain[j]));
                                now += held(chain[j]);
                                if (j < chain.Count - 1)
                                {
                                    combined.Add(chain[j]);
                                }
                            }

                            transient = Math.Max(0, peakObjects - end.Sum());
                            what.Add(string.Create(CultureInfo.InvariantCulture, $"hunt-until {want} {step.Item} ({have} held as equivalents; combined as it goes: {now} object(s) now, {end.Sum()} at its end, at most {peakObjects} on the way)"));
                        }
                        else
                        {
                            Add(running, hunted, Math.Max(0, want - have));
                            what.Add(string.Create(CultureInfo.InvariantCulture, $"hunt-until {want} {step.Item} ({have} held)"));
                        }
                    }
                    else
                    {
                        what.Add("hunt-until (no item named): its fight's loot");
                    }

                    break;
                case QuestStepKind.Kill:
                    fights = true;
                    what.Add("kill: its fight's loot");
                    break;
                case QuestStepKind.PickUp:
                case QuestStepKind.Buy:
                    Add(running, wcid, step.Count ?? 1);
                    what.Add(string.Create(CultureInfo.InvariantCulture, $"{QuestStepKinds.Word(step.Kind)} {step.Count ?? 1} {step.Item?.ToString() ?? "item"}"));
                    break;
                case QuestStepKind.Combine when QuestCombineChain.IsNoRollLink(step) && combined.Contains(wcid):
                    break; // M6 14 combine3: the hunt made its end state already: the step finds its result held (done already, QuestStation)
                case QuestStepKind.Combine:
                    Add(running, wcid, -2 * (step.Count ?? 1)); // two in, one out: each combine is one item fewer
                    Add(running, step.Expect.Item?.Wcid ?? wcid, step.Count ?? 1);
                    break;
                case QuestStepKind.Give:
                    Add(running, wcid, -(step.Count ?? 1));
                    break;
                default:
                    break;
            }

            // the pack relative to now: a combine or a give may take items she held before the errand (signed per weenie), so the totals are net
            var items = 0;
            var slots = 0;
            var burden = 0;
            var unread = new List<string>();
            foreach (var (w, n) in running)
            {
                items += n;
                var known = w == 0 ? null : copy(w);
                var stack = known is { MaxStack: > 1 } stacked ? stacked.MaxStack : 1;
                slots += Math.Sign(n) * ((Math.Abs(n) + stack - 1) / stack);
                burden += known is { } k ? n * k.BurdenEach : 0;
                if (n > 0 && known is null)
                {
                    unread.Add(w == 0 ? "an item the file names no weenie of" : Named(quest, w) ?? string.Create(CultureInfo.InvariantCulture, $"weenie {w}"));
                }
            }

            if (items + transient > peakItems)
            {
                peakItems = items + transient;
                unknown = unread; // the items at the peak whose burden no held copy gives
            }

            peakSlots = Math.Max(peakSlots, slots + transient);
            peakBurden = Math.Max(peakBurden, burden);
        }

        return new ErrandGain(peakItems, peakSlots, peakBurden, unknown.Count == 0 ? null : string.Join(", ", unknown), fights, string.Join("; ", what));
    }

    /// <summary>
    /// The decision: the sale first when a line holds (see the class remarks), each named; else the room she has. A capacity unread (0) judges the slots and the stock alone, named.
    /// </summary>
    /// <param name="gain">The errand's gain (<see cref="GainOf"/>).</param>
    /// <param name="burden">Her burden now.</param>
    /// <param name="capacity">Her burden capacity, or 0 when unread.</param>
    /// <param name="loadLinePercent">The load line, in percent of the capacity (the loot's, of the errand's hunt ground).</param>
    /// <param name="freeSlots">Her free main-pack slots now.</param>
    /// <param name="stockShort">The counters' stock line under its trip line, in words, or null.</param>
    public static ErrandRoomVerdict Decide(ErrandGain gain, int burden, int capacity, int loadLinePercent, int freeSlots, string? stockShort)
    {
        ArgumentNullException.ThrowIfNull(gain);
        var c = CultureInfo.InvariantCulture;
        var held = new List<string>();
        var line = capacity > 0 ? (int)((long)capacity * loadLinePercent / 100) : 0;
        if (capacity > 0 && burden >= line)
        {
            held.Add(string.Create(c, $"burden {burden} of {capacity} is at or over the load line {loadLinePercent}% ({line})"));
        }
        else if (capacity > 0 && gain.Burden > 0 && burden + gain.Burden > line)
        {
            held.Add(string.Create(c, $"burden {burden} with the errand's {gain.Burden} would cross the load line {loadLinePercent}% ({line})"));
        }

        if (freeSlots < gain.SlotsNeeded)
        {
            held.Add(string.Create(c, $"{freeSlots} free main-pack slot(s), under the {gain.SlotsNeeded} the errand needs"));
        }

        if (stockShort is not null)
        {
            held.Add("a stock line is under its trip line: " + stockShort);
        }

        var load = capacity > 0
            ? string.Create(c, $"burden {burden} of {capacity} ({burden * 100.0 / capacity:0.#}%, the load line {loadLinePercent}%)")
            : "burden unread (no capacity yet: the slots and the stock judge it)";
        var needs = string.Create(c, $"the errand gains up to {gain.Items} item(s) in {gain.Slots} slot(s) ({(gain.What.Length == 0 ? "no item step" : gain.What)}), burden {gain.Burden}{(gain.Unknown is null ? string.Empty : " and unknown for " + gain.Unknown + " (no copy held: the slots judge it)")}");
        var reading = string.Create(c, $"{load}, {freeSlots} free main-pack slot(s){(stockShort is null ? ", the stock lines over their trip lines" : string.Empty)}; {needs}");
        return held.Count > 0
            ? new ErrandRoomVerdict(true, $"{reading}: the sale first: {string.Join("; ", held)}")
            : new ErrandRoomVerdict(false, $"{reading}: room for it: the errand goes straight");
    }

    private static string? Named(Quest quest, uint wcid) =>
        quest.Steps.Select(s => s.Item?.Wcid == wcid ? s.Item : s.Expect.Item?.Wcid == wcid ? s.Expect.Item : null).FirstOrDefault(t => t is not null)?.ToString();

    private static void Add(Dictionary<uint, int> running, uint wcid, int n) => running[wcid] = running.GetValueOrDefault(wcid) + n;

    private static (int First, int Last) Range(string? steps, int count)
    {
        var parts = steps?.Split('-');
        return parts is { Length: 2 }
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to)
               && from <= to && to < count
            ? (from, to)
            : (0, count - 1);
    }
}
