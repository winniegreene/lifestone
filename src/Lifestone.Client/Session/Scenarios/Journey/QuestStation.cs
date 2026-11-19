using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>quest</c> (M6 item 5, DoD 7, D56): runs one quest file (<see cref="QuestCatalog"/>, spec argument <see cref="JourneyPlans.QuestArgument"/>) step by step over the shared step runners (<see cref="QuestStepLeg"/>), each step check, act, check (M5 D141)
/// with one named recovery and a <c>quest_step:</c> line, the quest's done check (<see cref="Quest.Done"/>) and its completion recorded and saved (<see cref="JourneyContext.CompleteQuest"/>). <see cref="JourneyPlans.FromArgument"/> says where the run
/// starts: <c>home</c> (the default) or <c>ground</c> (the climber's hunting ground: the trophy turn-in on the way home, D58). A kill or a hunt-until step runs the ground fight as a sub-run (<see cref="SubRun"/>, the factory's <c>den-fight</c> by
/// default), with the spec arguments <c>ground=</c>, <c>want=</c> and <c>until=</c> (item 5's contract between the slices). The verdict line is <c>quest: key= from= steps= done= xp=+&lt;n&gt; reward_line=&lt;n&gt; timer= ok= fail=</c>.
/// </summary>
public sealed class QuestStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "quest";

    /// <summary>The fight verdict's field when the spec's <c>until=</c> was met (<c>FightScenario</c>'s line, M6 5 Q4); <c>until=not-met</c> otherwise.</summary>
    public const string UntilMetField = "until=met";

    /// <summary>The fight verdict's field when the server refused the until's item at the pick-up (M6 5 F1, review 5-R4).</summary>
    public const string UntilRefusedField = "until=refused";

    /// <summary>The seconds a sub-run fight is expected to take (its spec's <see cref="StationSpec.ExpectedSeconds"/>; the quest station steps it itself, so the journey's limit is the quest station's own spec).</summary>
    public const double FightExpectedSeconds = 3600;

    /// <summary>A quest station over the quest <paramref name="questKey"/>.</summary>
    /// <param name="questKey">The quest file's key.</param>
    /// <param name="from"><see cref="JourneyPlans.FromHome"/> or <see cref="JourneyPlans.FromGround"/>; anything else is refused by name.</param>
    /// <param name="subRun">The fight's sub-run (a spec and the context to a run); null: none, and a kill or hunt-until step stops by name (<c>TODO(m6-quest-no-fight)</c>).</param>
    /// <param name="steps">The spec's <see cref="JourneyPlans.StepsArgument"/> (<c>&lt;from&gt;-&lt;to&gt;</c>, 0-based, inclusive), or null for all of the file's steps; a malformed or out-of-range value is a named stop at the run's start (M6 5 F2, D62).</param>
    public QuestStation(string questKey, string? from = null, Func<StationSpec, JourneyContext, IStationRun>? subRun = null, string? steps = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(questKey);
        QuestKey = questKey;
        From = from ?? JourneyPlans.FromHome;
        if (From != JourneyPlans.FromHome && From != JourneyPlans.FromGround)
        {
            throw new ArgumentException($"journey: the quest station's {JourneyPlans.FromArgument}={from}: it is {JourneyPlans.FromHome} or {JourneyPlans.FromGround}");
        }

        SubRun = subRun;
        Steps = steps;
    }

    /// <summary>The steps range argument as given (<c>0-1</c>), or null.</summary>
    public string? Steps { get; }

    /// <summary>The quest's key.</summary>
    public string QuestKey { get; }

    /// <summary>Where the run starts (<see cref="JourneyPlans.FromHome"/> or <see cref="JourneyPlans.FromGround"/>).</summary>
    public string From { get; }

    /// <summary>The seam a kill or hunt-until step's fight runs through: the factory's station for the spec, started on the context (a test fakes it).</summary>
    public Func<StationSpec, JourneyContext, IStationRun>? SubRun { get; }

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Here, Peace: true);

    /// <summary>
    /// The factory's quest station for <paramref name="spec"/>: <see cref="JourneyPlans.QuestArgument"/> required (refused by name), <see cref="JourneyPlans.FromArgument"/> optional, the fight's sub-run the factory's own station for the
    /// fight spec (<paramref name="create"/>), started on the step's context.
    /// </summary>
    /// <param name="spec">The quest spec.</param>
    /// <param name="create">The factory's station maker.</param>
    public static QuestStation Of(StationSpec spec, Func<StationSpec, IStation> create)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(create);
        if (!spec.Arguments.TryGetValue(JourneyPlans.QuestArgument, out var key) || string.IsNullOrEmpty(key))
        {
            throw new ArgumentException($"journey: the quest station '{spec.Name}' names no {JourneyPlans.QuestArgument}=<key>");
        }

        return new QuestStation(key, spec.Arguments.TryGetValue(JourneyPlans.FromArgument, out var from) ? from : null, (s, c) => create(s).Start(c), spec.Arguments.TryGetValue(JourneyPlans.StepsArgument, out var steps) ? steps : null);
    }

    /// <summary>
    /// The fight spec of a kill or hunt-until step (item 5's contract, the arguments <see cref="JourneyStations.GroundArgument"/>, <see cref="JourneyStations.WantArgument"/> and <see cref="JourneyStations.UntilArgument"/>): <c>ground=&lt;key&gt;</c>,
    /// <c>want=</c> and <c>until=</c> when given, and <see cref="JourneyStations.OpenEndedArgument"/> (the until is read only at the fight's safe point, so a kill cap must not end the fight first; the journey's limit on the quest station bounds it).
    /// </summary>
    /// <param name="name">The spec's name.</param>
    /// <param name="ground">The ground profile's key.</param>
    /// <param name="want">The wanted items' weenie classes, or none.</param>
    /// <param name="until">The fight's end (<c>item:&lt;wcid&gt;x&lt;n&gt;</c> or <c>chat:&lt;text&gt;</c>), or null.</param>
    /// <param name="carriers">M6 14 want loot: the creatures whose create list can hold a wanted item (<see cref="JourneyStations.CarriersArgument"/>), or none.</param>
    /// <param name="chain">M6 14 combine: the until item's no-roll combine chain (<see cref="QuestCombineChain.Of"/>), or none: <see cref="JourneyStations.CombineArgument"/> when it has a link.</param>
    public static StationSpec FightSpec(string name, string ground, IReadOnlyList<uint>? want, string? until, IReadOnlyList<WantCarrier>? carriers = null, IReadOnlyList<uint>? chain = null)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyStations.GroundArgument] = ground, [JourneyStations.OpenEndedArgument] = "yes" };
        if (want is { Count: > 0 })
        {
            args[JourneyStations.WantArgument] = string.Join(",", want.Select(w => w.ToString(CultureInfo.InvariantCulture)));
        }

        if (until is not null)
        {
            args[JourneyStations.UntilArgument] = until;
        }

        if (carriers is { Count: > 0 })
        {
            args[JourneyStations.CarriersArgument] = WantCarrier.Format(carriers);
        }

        if (chain is { Count: > 1 })
        {
            args[JourneyStations.CombineArgument] = QuestCombineChain.Format(chain);
        }

        return new StationSpec(name, StationKinds.DenFight, null, FightExpectedSeconds, args);
    }

    /// <summary>
    /// M6 14 want loot: the hunt-until start line's grind, as data: each carrier with its create-list shade, and with the file's live kill rate the items an hour if every carrier corpse is looted (kills a minute x 60 x the shade of the
    /// carrier the rate counts) and the hours the <paramref name="short"/> still wanted take at that rate. A step with no carriers names <c>TODO(m6-create-list-general-read)</c>; one with no rate says so.
    /// </summary>
    /// <param name="step">The hunt-until step.</param>
    /// <param name="short">The items still wanted.</param>
    public static string HuntGrind(QuestStep step, int @short)
    {
        ArgumentNullException.ThrowIfNull(step);
        var c = CultureInfo.InvariantCulture;
        if (step.Carriers is not { Count: > 0 } carriers)
        {
            return "grind: TODO(m6-create-list-general-read): the step names no carriers (no create-list fact), so the fight keeps the loot radius for every corpse and no rate is known";
        }

        var list = string.Join(", ", carriers.Select(x => string.Create(c, $"{x.Name} {x.Wcid} gives it at {x.Shade:0.###} a kill (create-list row {x.Row})")));
        if (step.KillRate is not { } rate || carriers.FirstOrDefault(x => x.Wcid == rate.Wcid) is not { } counted)
        {
            return $"grind: carriers {list}; no live kill rate is recorded, so the grind's length is not known yet";
        }

        var perHour = rate.PerMinute * 60 * counted.Shade;
        var hours = perHour > 0 ? @short / perHour : double.PositiveInfinity;
        return string.Create(c, $"grind: carriers {list}; live rate {rate.Kills} {counted.Name} kills in {rate.Minutes:0.#} min ({rate.PerMinute:0.##} a minute; {rate.Source}): {perHour:0.##} {step.Item!.Name} an hour if every corpse is looted, so {@short} more take about {hours:0.#} h ({rate.PerMinute * 60 * hours:0} kills)");
    }

    /// <summary>The <c>until=</c> value of a hunt-until step: <c>item:&lt;wcid&gt;x&lt;count&gt;</c>.</summary>
    /// <param name="wcid">The item's weenie class.</param>
    /// <param name="count">The count.</param>
    public static string UntilItem(uint wcid, int count) => string.Create(CultureInfo.InvariantCulture, $"item:{wcid}x{count}");

    /// <summary>The <c>until=</c> value of a kill step: <c>chat:&lt;text&gt;</c> (the counter's complete text).</summary>
    /// <param name="text">The text.</param>
    public static string UntilChat(string text) => "chat:" + text;

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new QuestRun(context, QuestCatalog.Get(QuestKey), From, SubRun, Steps);
    }
}

/// <summary>
/// M6 item 5 (Q3): the checked buy's judge, pure (the buy-trip's checks, <c>BuyTripStation</c>, as one rule a station calls): the shop open and the vendor's, the row by weenie class, the stock, the wire price, the coin and a free slot.
/// <para>M6 16 E5b: the check before of the one buy (<see cref="Quartermaster.VendorCounter.Buy"/>). The purse is the coin and, when the buy may change notes, the faces of the trade notes held (M6 16 E5a2: in any pack) at a shop
/// whose mask takes a note (<see cref="VendorStock.BuysType"/> of <see cref="VendorStock.PromissoryNoteType"/>; a vendor pays a note at its face, ACE <c>Vendor.cs</c> 591-599 @95ef426d). A partial buy (the town trip's) takes what
/// the shop holds and leaves the purse to the counter, which buys what the coin pays after the notes are changed; a ware that takes a container slot (a side pack) is judged against the container slots free.</para>
/// </summary>
public static class CheckedBuy
{
    /// <summary>The judge's answer: the row and the cost, or a refusal token (<c>vendor-closed</c>, <c>not-sold</c>, <c>stock-short</c>, <c>purse-unread</c>, <c>purse-short</c>, <c>pack-full</c>) with the reason.</summary>
    /// <param name="Entry">The row, or null on a refusal before it.</param>
    /// <param name="Price">The wire price of one, or 0.</param>
    /// <param name="Cost">The wire price of <c>count</c>, or 0.</param>
    /// <param name="Refusal">The refusal token, or null when the buy may be sent.</param>
    /// <param name="Why">The numbers in words.</param>
    public sealed record Verdict(VendorStockEntry? Entry, uint Price, long Cost, string? Refusal, string Why)
    {
        /// <summary>M6 16 E5b: the count the buy may send: the count asked, or (a partial buy) what the shop holds of it; 0 on a refusal before the stock is judged.</summary>
        public int Count { get; init; }
    }

    /// <summary>The checks before a buy of <paramref name="count"/> of <paramref name="wcid"/> from <paramref name="vendor"/>.</summary>
    /// <param name="stock">The open shop (<see cref="GameState.Vendor"/>), or null.</param>
    /// <param name="vendor">The vendor's guid.</param>
    /// <param name="wcid">The item's weenie class.</param>
    /// <param name="count">How many.</param>
    /// <param name="coins">The coin held (-1 unread).</param>
    /// <param name="freeSlots">The main pack's free slots.</param>
    /// <param name="notesWorth">M6 16 E5b: the faces of the trade notes the buy may change (0: none held, or the buy changes none); counted only at a shop whose mask takes a note.</param>
    /// <param name="partial">M6 16 E5b: true for a partial buy: fewer in the shop than asked buys what it holds (none is still <c>stock-short</c>), and the purse is not judged here.</param>
    /// <param name="containerRoom">M6 16 E5b: the container slots free when the ware takes one (a side pack), else null: the main pack's <paramref name="freeSlots"/> are judged.</param>
    public static Verdict Judge(VendorStock? stock, uint vendor, uint wcid, int count, long coins, int freeSlots, long notesWorth = 0, bool partial = false, int? containerRoom = null)
    {
        var c = CultureInfo.InvariantCulture;
        if (stock is null || stock.Vendor != vendor)
        {
            return new(null, 0, 0, "vendor-closed", string.Create(c, $"the shop of 0x{vendor:X8} is not open ({(stock is null ? "no shop" : $"the open shop is 0x{stock.Vendor:X8}'s")})"));
        }

        var entry = stock.Entries.FirstOrDefault(e => e.WeenieClassId == wcid);
        if (entry is null)
        {
            return new(null, 0, 0, "not-sold", string.Create(c, $"the weenie {wcid} is not in the shop ({stock.Entries.Length} rows)"));
        }

        if (!entry.IsUnlimited && (entry.Stack < count || (partial && entry.Stack <= 0)))
        {
            if (!partial || entry.Stack <= 0)
            {
                return new(entry, 0, 0, "stock-short", string.Create(c, $"the shop holds {entry.Stack} {entry.Name}, fewer than {count}"));
            }

            count = entry.Stack;
        }

        var price = stock.PriceToBuy(entry, 1);
        var cost = (long)stock.PriceToBuy(entry, (uint)count);
        var takesNotes = stock.BuysType(VendorStock.PromissoryNoteType);
        var notes = takesNotes ? Math.Max(0, notesWorth) : 0;
        if (!partial)
        {
            if (coins < 0)
            {
                return new(entry, price, cost, "purse-unread", "the coin (CoinValue) is not read") { Count = count };
            }

            if (coins + notes < cost)
            {
                var why = notes > 0
                    ? string.Create(c, $"the coin {coins} and the trade notes held ({notes}) do not cover {count} {entry.Name} at {price} each ({cost})")
                    : string.Create(c, $"the coin {coins} does not cover {count} {entry.Name} at {price} each ({cost})") + (notesWorth > 0 && !takesNotes ? string.Create(c, $"; the shop's mask takes no trade note (the {notesWorth} held in notes are not changed here)") : string.Empty);
                return new(entry, price, cost, "purse-short", why) { Count = count };
            }
        }

        if (containerRoom is { } room)
        {
            if (room < count)
            {
                return new(entry, price, cost, "pack-full", string.Create(c, $"{room} container slots are free for the {count} {entry.Name}")) { Count = count };
            }
        }
        else if (freeSlots <= 0)
        {
            return new(entry, price, cost, "pack-full", "the main pack has no free slot") { Count = count };
        }

        var purse = partial ? string.Create(c, $", the coin {coins}")
            : coins >= cost ? string.Create(c, $" within the coin {coins}")
            : string.Create(c, $": the coin {coins} is short by {cost - coins} and the trade notes held ({notes}) cover it, so a note is changed at the counter");
        return new(entry, price, cost, null, string.Create(c, $"{entry.Name} listed at {price}, {count} cost {cost}{purse}; {freeSlots} free slots")) { Count = count };
    }
}

/// <summary>The quest station's run (see <see cref="QuestStation"/>).</summary>
public sealed partial class QuestRun : QuestStepLeg, IStationSubFight
{
    /// <summary>The seconds a reward line has to show after a talk or a give (ACE sends it in the emote chain, after the Tell).</summary>
    public const double RewardSeconds = 2;

    /// <summary>The seconds of silence in the chat that end the wait after the NPC's busy refusal (<c>recovery=give-after-busy</c>; ACE's <c>AiRefuseItemDuringEmote</c>, @95ef426d <c>WorldObjects/Player_Inventory.cs:3405-3409</c>). The refusal lasts as long as the NPC's speech, whose lines came about a second apart in M6 item 8's long round: Lubziklan's ran past a flat 2 s wait.</summary>
    public const double BusyQuietSeconds = 2.5;

    /// <summary>The most seconds one wait after a busy refusal lasts, however long the chat keeps talking.</summary>
    public const double BusyMaxSeconds = 20;

    /// <summary>The gives a step makes at most while the NPC answers busy; the last refusal stops the step by name (<c>give:refused:busy</c>).</summary>
    public const int BusyGives = 3;

    /// <summary>The verdict line's prefix.</summary>
    public const string LinePrefix = "quest:";

    private readonly Quest _quest;
    private readonly string _from;
    private readonly Func<StationSpec, JourneyContext, IStationRun>? _subRun;
    private long _xpStart;
    private long _rewardXp;
    private int _rewardLines;
    private int _stepsDone;
    private int _lastTalk = -1;
    private string _timer = "none";
    private string? _failName;
    private bool _doneReply;
    private bool _doneItem;
    private long _lastStepMark;
    private bool _lined;
    private string _read = string.Empty;
    private bool _turnedInAtArm; // M6 14 guard task: the arming talk was the turn-in (the counter was complete before it)

    /// <inheritdoc />
    /// <remarks>M6 8 G: the climber walks a quest's roads at low level (478 m to the Redoubt, 503 m across Shoushi), so every walk of the station answers an attacker.</remarks>
    protected override bool WalksFightBack => true;
    private bool _rangeRan;
    private readonly string? _stepsArgument;
    private int _first;
    private int _last;

    /// <summary>True when the run's range ends at the file's last step (the done check and the completion run only then).</summary>
    private bool RangeIsWhole => _last == _quest.Steps.Count - 1;

    /// <summary>True when a range was given (the verdict names it).</summary>
    private bool Ranged => _stepsArgument is not null;

    /// <summary>The step range's text, <c>from-to</c> (M6 5 F2).</summary>
    public string RangeText => string.Create(CultureInfo.InvariantCulture, $"{_first}-{_last}");

    internal QuestRun(JourneyContext context, Quest quest, string from, Func<StationSpec, JourneyContext, IStationRun>? subRun, string? steps = null)
        : base("quest-" + quest.Key, context)
    {
        _stepsArgument = steps;
        _first = 0;
        _last = quest.Steps.Count - 1;
        _quest = quest;
        _from = from;
        _subRun = subRun;
    }

    /// <summary>True when the quest's done check held (the reply, the item, the stamp it names) and the completion was recorded.</summary>
    public bool QuestDone { get; private set; }

    /// <summary>The steps played to their check.</summary>
    public int StepsDone => _stepsDone;

    /// <summary>The experience the reward lines named over the run (<see cref="QuestReads.Reward"/>), summed.</summary>
    public long RewardExperience => _rewardXp;

    /// <summary>The failure's name (<c>kill:not-complete:3/10</c>), or null.</summary>
    public string? FailName => _failName;

    /// <summary>The give's check-after line (M6 5 F1, review 5-R1; the live tests assert it): <c>give: Aphus Lassel Portal Gem arrived: held 0 -> 1</c>.</summary>
    /// <param name="name">The gift's name.</param>
    /// <param name="before">The count held before the give.</param>
    /// <param name="after">The count held after.</param>
    public static string GiftArrivedLine(string name, int before, int after) => string.Create(CultureInfo.InvariantCulture, $"give: {name} arrived: held {before} -> {after}");

    /// <summary>The contract's "arrives" (item 5): a gift shows when its count rose over the act, never when it is merely held (review 5-R6).</summary>
    protected override bool GiftArrived(uint wcid, int before) => CountHeld(wcid) > before;

    /// <inheritdoc />
    public override string VerdictFields() => Fields() + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    private long XpGained => S is null ? 0 : S.State.Self.Qualities.TotalExperience - _xpStart;

    private string Fields() => string.Create(
        CultureInfo.InvariantCulture,
        $"key={_quest.Key} from={_from} steps={_stepsDone}/{_quest.Steps.Count} done={DoneWord} xp=+{XpGained} reward_line={_rewardXp} timer={_timer} ok={Yes(Failure is null && (QuestDone || Partial))} fail={_failName ?? (Failure is null ? "none" : "failed")}{(Ranged ? " steps=" + RangeText : string.Empty)} {ShotFields}");

    /// <summary>True when a partial range (one that stops before the file's last step) ran to its end without a failure.</summary>
    private bool Partial => Ranged && !RangeIsWhole && _rangeRan && Failure is null;

    private string DoneWord => Partial ? "partial" : Yes(QuestDone);

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Watch();
        try
        {
            foreach (var h in Guarded(Start()))
            {
                yield return h;
            }

            for (var i = _first; i <= _last && Failure is null && !_turnedInAtArm; i++)
            {
                var step = _quest.Steps[i];
                var word = QuestStepKinds.Word(step.Kind);
                Stage = string.Create(CultureInfo.InvariantCulture, $"{i}:{word}");
                var started = S.Clock.Now;
                var mark = S.State.Chat.Total;
                _lastStepMark = mark;
                var doneItemBefore = _quest.Done.Item?.Wcid is { } di ? CountHeld(di) : 0;
                _read = string.Empty;
                foreach (var h in Guarded(DefendIfAttacked())) { yield return h; } // M5 D88: an attacker is fought before the next step

                if (step.Todo is { } todo)
                {
                    Log($"step {i} ({word}: {step.What}) carries {todo}");
                }

                if (Failure is null)
                {
                    foreach (var h in Guarded(StepOf(step, i, mark))) { yield return h; }
                }

                var ok = Failure is null;
                if (ok)
                {
                    _stepsDone++;
                    if (step.Kind is QuestStepKind.Talk)
                    {
                        _lastTalk = i;
                    }
                }

                _doneReply |= _quest.Done.Reply is { } reply && QuestReads.Replied(S.State.Chat, mark, reply);
                _doneItem |= _quest.Done.Item?.Wcid is { } item && CountHeld(item) > doneItemBefore;
                Log(string.Create(CultureInfo.InvariantCulture, $"quest_step: n={i} kind={word} what={Word(step.What)} ok={Yes(ok)} ms={(S.Clock.Now - started) * 1000:0} {(_read.Length == 0 ? "read=none" : _read)}{(ok ? string.Empty : " fail=" + (_failName ?? "failed"))}"));
            }

            if (Failure is null && (RangeIsWhole || _turnedInAtArm))
            {
                Stage = "done-check";
                foreach (var h in Guarded(DoneCheck())) { yield return h; }
            }
            else if (Failure is not null && RangeIsWhole)
            {
                DoneBeforeTheFailure();
            }
            else if (Failure is null)
            {
                _rangeRan = true;
                Stage = "partial";
                Log($"quest: the steps {RangeText} of {_quest.Steps.Count} ran; the done check and the completion wait for the range that ends at the last step (done=partial)");
            }
        }
        finally
        {
            Emit();
        }
    }

    // ---------------------------------------------------------------------------------------------- the run

    private IEnumerable<ActionHandle?> Start()
    {
        Stage = "start";
        _xpStart = S.State.Self.Qualities.TotalExperience;
        var here = S.State.Self.Position;
        var ground = here is { } at ? BodyGround(at.CellId) : null;
        var now = S.Clock.Time.GetUtcNow();
        Log(string.Create(CultureInfo.InvariantCulture, $"quest start: {_quest} from={_from}; level {S.State.Self.Qualities.Level}, experience {_xpStart}; cell 0x{here?.CellId ?? 0:X8} ({(ground is null ? "no ground" : "on the ground " + ground.Key)}); {_quest.Steps.Count} steps: {string.Join(", ", _quest.Steps.Select(s => QuestStepKinds.Word(s.Kind)))}"));
        Log(Context.QuestTimersInUse.Line(_quest, now));
        if (_stepsArgument is not null)
        {
            ParseRange();
        }
        else if (_quest.Steps.Any(st => st.Kind is QuestStepKind.Combine or QuestStepKind.Apply))
        {
            Resume(); // M6 14 the Atlan wand: a crafted chain starts after what she holds of it
        }

        if (!Context.QuestTimersInUse.IsDue(_quest, now))
        {
            // M6 5 F1 (review 5-R7): the client's own timer says not yet; named, before any walk (the server still gates the quest itself).
            Stop("start:not-due", $"the quest's timer says it is not due ({Context.QuestTimersInUse.Line(_quest, now)})");
        }

        if (_from == JourneyPlans.FromGround && ground is null)
        {
            Stop("start:not-on-ground", $"from={_from} but the body stands in cell 0x{here?.CellId ?? 0:X8}, in no ground of the catalog");
        }

        // A range that starts past the arming talk: the arm is the earlier journey's (D62), so the kill step reads the last talk before the range.
        _lastTalk = _quest.Steps.Take(_first).Select((s, i) => (s, i)).LastOrDefault(t => t.s.Kind == QuestStepKind.Talk) is { s: not null } armed ? armed.i : -1;

        // The kill step's arm (QuestManager.HandleKillTask counts only for a player who holds the task, @95ef426d Managers/QuestManager.cs:605): a talk before it in the file's order.
        for (var i = 0; i < _quest.Steps.Count; i++)
        {
            if (_quest.Steps[i].Kind == QuestStepKind.Kill && !_quest.Steps.Take(i).Any(s => s.Kind == QuestStepKind.Talk))
            {
                Stop("kill:unarmed", $"the kill step {i} has no talk before it in the file (the talk is the task's arm)");
            }
        }

        yield break;
    }

    /// <summary>Reads <c>&lt;from&gt;-&lt;to&gt;</c> (0-based, inclusive, both within the file's steps); anything else is a named stop at the start.</summary>
    private void ParseRange()
    {
        var parts = _stepsArgument!.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to))
        {
            Stop("steps:malformed", $"{JourneyPlans.StepsArgument}={_stepsArgument} is not <from>-<to> (two whole numbers)");
        }
        else if (from > to || to >= _quest.Steps.Count)
        {
            Stop("steps:out-of-range", $"{JourneyPlans.StepsArgument}={_stepsArgument}: the file has steps 0-{_quest.Steps.Count - 1} and the range must run forward within them");
        }
        else
        {
            _first = from;
            _last = to;
            Log($"quest: the range {RangeText} of the file's steps 0-{_quest.Steps.Count - 1}");
        }
    }

    /// <summary>
    /// M6 14 the Atlan wand: a crafted chain (a file with a combine or an apply) cut after its hunt or its combines starts after the latest step whose product she holds with every input the later steps need (<see cref="QuestResume.From"/>), one
    /// line; a chain whose done item she holds already took it (the item decides the done check).
    /// </summary>
    private void Resume()
    {
        var point = QuestResume.From(_quest, CountHeld);
        if (point.Step == 0)
        {
            return;
        }

        _first = point.Step;
        if (_quest.Done.Item?.Wcid is { } doneItem && CountHeld(doneItem) > 0 && _quest.Steps.Take(point.Step).Any(st => QuestResume.ProductOf(st)?.Wcid == doneItem))
        {
            _doneItem = true; // the step that makes the done item is behind the start, and the item is held
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"quest: resume: the run starts at step {point.Step} of 0-{_quest.Steps.Count - 1} ({QuestStepKinds.Word(_quest.Steps[point.Step].Kind)}: {_quest.Steps[point.Step].What}): {point.Why}"));
    }

    private IEnumerable<ActionHandle?> StepOf(QuestStep step, int index, long mark) => step.Kind switch
    {
        QuestStepKind.Go => Go(step),
        QuestStepKind.Talk => TalkStep(step, index, mark),
        QuestStepKind.Give => GiveStep(step),
        QuestStepKind.Use => UseStep(step, mark),
        QuestStepKind.PickUp => PickUpStep(step),
        QuestStepKind.Wear => WearStep(step),
        QuestStepKind.Buy => BuyStep(step),
        QuestStepKind.Recall => RecallStep(step),
        QuestStepKind.Confirm => ConfirmStep(step),
        QuestStepKind.Kill => KillStep(step, mark),
        QuestStepKind.HuntUntil => HuntStep(step),
        QuestStepKind.Combine or QuestStepKind.Apply => RecipeStep(step),
        _ => StopSteps($"step:{QuestStepKinds.Word(step.Kind)}", $"TODO(m6-quest-step-{QuestStepKinds.Word(step.Kind)}): the quest station has no act for this kind"),
    };

    /// <summary>
    /// M6 8 (live 2026-11-11 08:48 UTC, m6-quest-done-before-fail): a step failed after the quest's own done evidence was seen (the reward's reply, the reward's item): the stolen goods paid +66,725 at the
    /// give, then the walk home failed at Yaraq's portal, the station ended failed with nothing recorded, and the next run of the ladder (the rung move) called the whole errand again. The completion is the
    /// server's, so it is recorded and saved as <see cref="DoneCheck"/> does; the station still ends failed by its own step, and <see cref="QuestDone"/> stays false (the run did not end at its last step).
    /// The stamp is not asked here (a failed station sends nothing more): the reply and the item decide.
    /// </summary>
    private void DoneBeforeTheFailure()
    {
        var done = _quest.Done;
        var replyOk = done.Reply is null || _doneReply;
        var itemOk = done.Item is null || _doneItem;
        if (!replyOk || !itemOk || (done.Reply is null && done.Item is null))
        {
            return;
        }

        var how = _doneReply ? QuestTimers.HowReply : QuestTimers.HowItem;
        var entry = Context.CompleteQuest(_quest, S.Clock.Time.GetUtcNow(), how);
        _timer = Context.QuestStore is null ? "memory" : "saved";
        Log(string.Create(CultureInfo.InvariantCulture, $"done: check after the failure ({_failName ?? "failed"}): the quest's done evidence was seen before it (reply={(done.Reply is null ? "-" : Yes(_doneReply))} item={(done.Item is null ? "-" : Yes(_doneItem))}): recorded by {how}: completions {entry.Completions}, next due {(entry.NextDue is { } n ? n.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "never (once)")}; timers {_timer}; the station still ends failed (m6-quest-done-before-fail)"));
    }

    private IEnumerable<ActionHandle?> DoneCheck()
    {
        var done = _quest.Done;
        if (done.Reply is { } doneReply && !_doneReply)
        {
            // The done line may follow the last step's reply by a moment (San Ming's "Congratulations!" is the emote chain's last Tell, at a delay of 1 s: facts.md section 3).
            foreach (var h in Until(() => QuestReads.Replied(S.State.Chat, _lastStepMark, doneReply), AfterSeconds)) { yield return h; }

            _doneReply = QuestReads.Replied(S.State.Chat, _lastStepMark, doneReply);
        }

        var stampHeld = false;
        var stampRead = false;
        if (done.Stamp is { } stamp)
        {
            var has = S.Tools.HasQuest(stamp, ActionSource.Host);
            yield return has;
            stampRead = has.Outcome is { Kind: ActionOutcomeKind.Confirmed } && has.Result is not null;
            stampHeld = has.Result is { Held: true };
            Log($"done: the quest stamp {stamp} asked: {(stampRead ? "held=" + Yes(stampHeld) : "unread (" + Describe(has) + "): the reply and the item decide")}");
        }

        var replyOk = done.Reply is null || _doneReply;
        var itemOk = done.Item is null || _doneItem;
        var stampOk = done.Stamp is null || stampHeld || !stampRead;
        QuestDone = replyOk && itemOk && stampOk && (done.Reply is not null || done.Item is not null || stampHeld);
        Log($"done: {done}: reply={(done.Reply is null ? "-" : Yes(_doneReply))} item={(done.Item is null ? "-" : Yes(_doneItem))} stamp={(done.Stamp is null ? "-" : stampRead ? Yes(stampHeld) : "unread")}: done={Yes(QuestDone)}");
        if (!QuestDone)
        {
            Stop("done:not-seen", $"the quest's done check did not hold ({done})");
        }

        var how = stampHeld ? QuestTimers.HowServerStamp : _doneReply ? QuestTimers.HowReply : QuestTimers.HowItem;
        var pickups = TrophyPickups.For(S.State);
        var at = pickups.StartOf(_quest, S.Clock.Time.GetUtcNow(), out var timerFrom); // M6 audit: a tusk's timer runs from its pickup, which the server stamps (timer_from pickup)
        var entry = Context.CompleteQuest(_quest, at, how); // M6 5 Q2: record, save through the host's store, log the line
        if (TrophyPickups.PickupItem(_quest) is { Wcid: { } given })
        {
            pickups.Forget(given); // given away: the next one is a new pickup
        }

        _timer = Context.QuestStore is null ? "memory" : "saved";
        Log(string.Create(CultureInfo.InvariantCulture, $"done: recorded by {how}: completions {entry.Completions}, next due {(entry.NextDue is { } n ? n.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "never (once)")}; the timer from {timerFrom}; timers {_timer}"));
    }

    // ---------------------------------------------------------------------------------------------- the steps

    /// <summary>
    /// A go: check (where the body stands), act, check (the place's landblock, then the target in view). In the place's travel place: the walk for the step's purpose (<see cref="QuestStepLeg.WalkWithRecovery"/>). From a ground: with
    /// <c>from=ground</c> and the ground's exit leading to the place's travel place, <see cref="RungRoute.Decide"/>'s first home leg (the exit hop) only; else the rung change home (<see cref="RungChangeStation"/>). From a town to another: the
    /// Town Network trip (<see cref="TownNetworkTrip"/>, each hop a <see cref="PortalHopLeg"/>) as the buy-trip's hops.
    /// </summary>
    private IEnumerable<ActionHandle?> Go(QuestStep step)
    {
        if (step.Place is not { } place)
        {
            uint guid = 0;
            foreach (var h in Approach(step.Target!.Wcid!.Value, step.Target.Name, g => guid = g)) { yield return h; }

            _read = string.Create(CultureInfo.InvariantCulture, $"via=walk at=0x{S.State.Self.Position?.CellId ?? 0:X8} target=0x{guid:X8}");
            yield break;
        }

        if (step.Target?.Wcid is { } sought && NextPicksUp(step, sought) && CountHeld(sought) > 0)
        {
            // M6 14 the Atlan wand: the stone held already (the stone's own errand, an earlier run): the walk to it is not made; the pick-up step says held
            Log($"go: {step.What}: {step.Target.Name} ({sought}) is held already and the next step picks it up: no walk");
            _read = "via=none held=yes";
            yield break;
        }

        LastPlace = place;
        var via = new List<string>();
        var foughtBefore = WalkFightBacks;
        var here = Here();
        var goal = place.Cell >> 16;
        Log(string.Create(CultureInfo.InvariantCulture, $"go: {step.What}: from cell 0x{here.CellId:X8} ({PlaceKey(here.CellId >> 16) ?? "no place"}) to {place} ({PlaceKey(goal) ?? "no place"})"));
        if (!SamePlace(here.CellId >> 16, goal) && BodyGround(here.CellId) is { } ground)
        {
            var exit = _from == JourneyPlans.FromGround ? ExitHopTo(ground, goal) : null;
            if (exit is not null)
            {
                via.Add("exit-hop");
                foreach (var h in Hop(exit, $"the quest {_quest.Key}: {ground.Key}'s exit toward {place.Name} (from=ground, D58)", "go:exit-hop")) { yield return h; }
            }
            else
            {
                via.Add("rung-change-home");
                Log($"walking: purpose=quest (the quest {_quest.Key}: home from {ground.Key} by the rung change, then to {place.Name})");
                var run = new RungChangeStation(RungDirection.Home).Start(Context.WithDungeon(ground));
                foreach (var h in SubRunOf(run)) { yield return h; }

                Log($"go: the way home from {ground.Key}: {run.VerdictFields()}");
                Require(Verify(run.Ok, $"go: the rung change home from {ground.Key} failed: {run.VerdictFields()}"), "go: rung-change home failed");
            }
        }

        here = Here();
        if (!SamePlace(here.CellId >> 16, goal))
        {
            via.Add(TownNetworkTrip.TravelWord);
            foreach (var h in TownTrip(step, place)) { yield return h; }
        }

        here = Here();
        Require(Verify(SamePlace(here.CellId >> 16, goal), $"go: after the travel the body stands in cell 0x{here.CellId:X8}, not in {place}'s place"), "go: off-place");
        var target = step.Target?.Wcid;
        if (place.Point is { } point)
        {
            var cell = Cell(place.Cell);
            var guid = target is { } w ? WorldObjectNear(w, cell, point, SearchMetres) : 0;
            if (guid != 0 && Distance(guid) <= UseRange + 1)
            {
                Log($"go: {step.What}: {step.Target!.Name} (0x{guid:X8}) is in reach already ({Distance(guid):0.0} m)");
            }
            else
            {
                via.Add("walk");
                Log($"walking: purpose=quest ({step.What}: the quest {_quest.Key}'s step to {place.Name})");
                Func<double?>? person = target is { } wcid ? () => NearestOfWcid(wcid) is var seen and not 0 ? Distance(seen) : null : null;
                foreach (var h in WalkWithRecovery(new TravelRequest(Destination(place.Name, cell, point), TravelPurpose.Cell, guid, guid == 0 ? 0 : UseRange, Way: WayOf(step)), step.What, person)) { yield return h; }
            }
        }

        if (target is { } t)
        {
            foreach (var h in Until(() => NearestOfWcid(t) != 0, NpcSeconds)) { yield return h; }

            Require(Verify(NearestOfWcid(t) != 0, $"go: {step.Target!.Name} ({t}) is not in the object table after the walk to {place}"), "go: target not in view");
        }

        var fought = WalkFightBacks - foughtBefore;
        _read = string.Create(CultureInfo.InvariantCulture, $"via={(via.Count == 0 ? "none" : string.Join("+", via))} at=0x{S.State.Self.Position?.CellId ?? 0:X8}{(fought > 0 ? " fought_back=" + fought : string.Empty)}");
    }

    /// <summary>M6 14: the step after <paramref name="step"/> is a pick-up of <paramref name="wcid"/>.</summary>
    private bool NextPicksUp(QuestStep step, uint wcid) =>
        step.Index + 1 < _quest.Steps.Count && _quest.Steps[step.Index + 1] is { Kind: QuestStepKind.PickUp, Item.Wcid: { } item } && item == wcid;

    /// <summary>M6 8 B2c: a go step's authored way as the frames the walk follows (the points of the file, in their cells), or null.</summary>
    private IReadOnlyList<CellFrame>? WayOf(QuestStep step) => step.Way?.Select(p => new CellFrame(Cell(p.Cell), new AcFrame(p.Point!.Value, Quaternion.Identity))).ToList();

    private IEnumerable<ActionHandle?> TalkStep(QuestStep step, int index, long mark)
    {
        var arms = _quest.Steps.Skip(index + 1).TakeWhile(s => s.Kind != QuestStepKind.Talk).Any(s => s.Kind == QuestStepKind.Kill);
        Func<long, bool>? turnedIn = arms && _quest.Done.Reply is { } doneReply ? m => QuestReads.Replied(S.State.Chat, m, doneReply) : null; // M6 14 guard task: an arming talk whose counter was already complete is the turn-in
        foreach (var h in Talk(step, turnedIn)) { yield return h; }

        foreach (var h in Reward(mark)) { yield return h; }

        if (turnedIn is not null && turnedIn(mark) && Failure is null)
        {
            // M6 14 guard task: the server's counter was complete before this talk (a new process reads it here first): the giver took the turn-in at once; the rest of the file is done by it, and the done check runs now
            _turnedInAtArm = true;
            Log($"talk: {step.Target!.Name} answered the arming talk with the quest's done reply ('{_quest.Done.Reply}'): the counter was complete, so this talk was the turn-in; the steps after it are done by it, and the done check follows");
        }
        else if (arms)
        {
            Log($"talk: {step.Target!.Name}'s talk is the kill task's arm (the next kill step counts from here; an unarmed kill counts nothing, @95ef426d Managers/QuestManager.cs:605)");
        }

        _read = string.Create(CultureInfo.InvariantCulture, $"replied=yes reward_line={QuestReads.Reward(S.State.Chat, mark)?.Experience ?? 0}{(arms ? " armed=yes" : string.Empty)}");
    }

    /// <summary>Waits until no chat line has arrived for <paramref name="quiet"/> seconds, or <paramref name="max"/> seconds passed.</summary>
    private IEnumerable<ActionHandle?> ChatQuiet(double quiet, double max)
    {
        var total = S.State.Chat.Total;
        var since = S.Clock.Now;
        bool Quiet()
        {
            if (S.State.Chat.Total != total)
            {
                total = S.State.Chat.Total;
                since = S.Clock.Now;
            }

            return S.Clock.Now - since >= quiet;
        }

        foreach (var h in Until(Quiet, max)) { yield return h; }
    }

    /// <summary>
    /// A give, check, act, check: before, the item held and the NPC in reach; act, one Give; after, the server's give line (<see cref="QuestReads.Give"/>), the item gone, the reply, the stamp, the reward line and the experience property, and
    /// the expected item arriving. The NPC's busy refusal (0x04CE, <c>AiRefuseItemDuringEmote</c>) waits for the chat to go quiet (<see cref="ChatQuiet"/>) and gives again, <see cref="BusyGives"/> gives at most (<c>recovery=give-after-busy</c>); any other refusal stops by name (<c>give:refused:&lt;outcome&gt;</c>).
    /// </summary>
    private IEnumerable<ActionHandle?> GiveStep(QuestStep step)
    {
        var npcWcid = step.Target!.Wcid!.Value;
        var item = step.Item!.Wcid!.Value;
        var itemGuid = InPack(item).FirstOrDefault();
        Require(Verify(itemGuid != 0, $"give: the pack holds no {step.Item.Name} ({item})"), "give: no item");
        uint npc = 0;
        foreach (var h in Approach(npcWcid, step.Target.Name, g => npc = g)) { yield return h; }

        var npcName = NameOf(npc);
        var xpBefore = S.State.Self.Qualities.TotalExperience;
        var giftBefore = step.Expect.Item?.Wcid is { } gw ? CountHeld(gw) : 0;
        var heldBefore = CountHeld(item);
        long mark = 0;
        QuestGive? line = null;
        for (var attempt = 0; attempt < BusyGives; attempt++)
        {
            mark = S.State.Chat.Total;
            var give = S.Actions.Give(npc, itemGuid, 1, ActionSource.Host);
            yield return give;
            Log($"give: {step.Item.Name} (0x{itemGuid:X8}) to {npcName} (0x{npc:X8}): {Describe(give)}");
            var m = mark;
            foreach (var h in Until(() => QuestReads.Give(S.State.Chat, m, npcName) is not null || !InPack(item).Contains(itemGuid) || (step.Expect.Reply is not null && Replied(step.Expect, m)), AfterSeconds)) { yield return h; }

            line = QuestReads.Give(S.State.Chat, mark, npcName);
            if (line is { Outcome: QuestGiveOutcome.Busy } && attempt < BusyGives - 1)
            {
                Recoveries++;
                Log(string.Create(CultureInfo.InvariantCulture, $"recovery=give-after-busy: {npcName} refused {step.Item.Name} while its emote runs (0x{QuestReads.BusyCode:X4}); waiting for {BusyQuietSeconds:0.#} s of quiet in the chat, {BusyMaxSeconds:0} s at most, then give {attempt + 2} of {BusyGives}"));
                foreach (var h in ChatQuiet(BusyQuietSeconds, BusyMaxSeconds)) { yield return h; }

                continue;
            }

            break;
        }

        var taken = !InPack(item).Contains(itemGuid) || CountHeld(item) < heldBefore;
        if (line is { Accepted: false } refusal)
        {
            Stop("give:refused:" + KebabCase(refusal.Outcome.ToString()), $"{npcName} refused {step.Item.Name}: {refusal.Outcome} \"{refusal.Text}\"{Said(mark)}");
        }

        if (!taken && line is null && !Replied(step.Expect, mark))
        {
            Stop("give:no-answer", $"{npcName} took nothing and the server said nothing {AfterSeconds:0} s after the Give{Said(mark)}");
        }

        if (step.Expect.Reply is not null && !Replied(step.Expect, mark))
        {
            foreach (var h in Until(() => Replied(step.Expect, mark), AfterSeconds)) { yield return h; }
        }

        var replied = Replied(step.Expect, mark);
        Log($"give: taken={Yes(taken)} give_line={(line is null ? "none" : line.Outcome.ToString().ToLowerInvariant())} replied={Yes(replied)}{Said(mark)}");
        Require(Verify(taken, $"give: {step.Item.Name} is still in the pack after the Give", itemGuid, mark), "give: not taken");
        Require(Verify(replied, $"give: {npcName} took {step.Item.Name} but did not reply '{step.Expect.Reply}'", 0, mark), "give: no reply");
        foreach (var h in StampCheck(step)) { yield return h; }

        foreach (var h in Reward(mark)) { yield return h; }

        if (step.Expect.Item?.Wcid is { } gift)
        {
            foreach (var h in Until(() => CountHeld(gift) > giftBefore, AfterSeconds)) { yield return h; }

            Require(Verify(CountHeld(gift) > giftBefore, $"give: {npcName} gave no {step.Expect.Item.Name} ({gift}) after {step.Item.Name} (held {giftBefore} before, {CountHeld(gift)} now)", 0, mark), "give: no gift");
            Log(GiftArrivedLine(step.Expect.Item.Name, giftBefore, CountHeld(gift)));
        }

        var reward = QuestReads.Reward(S.State.Chat, mark);
        var xp = S.State.Self.Qualities.TotalExperience - xpBefore;
        Log(string.Create(CultureInfo.InvariantCulture, $"give: experience {xpBefore} -> {S.State.Self.Qualities.TotalExperience} (+{xp}); the reward line {(reward is null ? "none" : $"{reward.Experience} ({reward.Lines} line(s))")}"));
        _read = string.Create(CultureInfo.InvariantCulture, $"taken=yes give_line={(line is null ? "none" : "given")} replied={Yes(replied)} reward_line={reward?.Experience ?? 0} xp=+{xp}");
    }

    private IEnumerable<ActionHandle?> UseStep(QuestStep step, long stepMark)
    {
        var wcid = step.Target!.Wcid!.Value;
        uint target = 0;
        foreach (var h in Approach(wcid, step.Target.Name, g => target = g)) { yield return h; }

        var keyGuid = step.Item?.Wcid is { } key ? InPack(key).FirstOrDefault() : 0;
        Require(Verify(step.Item is null || keyGuid != 0, $"use: the pack holds no {step.Item?.Name}"), "use: no item");
        var giftBefore = step.Expect.Item?.Wcid is { } gw ? CountHeld(gw) : 0;
        var landblock = S.State.Self.Position?.CellId >> 16 ?? 0;
        bool Seen(long m) =>
            (step.Expect.Reply is not null && Replied(step.Expect, m))
            || (step.Expect.Item?.Wcid is { } g && CountHeld(g) > giftBefore)
            || (step.Place is { } p && p.Cell >> 16 != landblock && S.State.Self.Position is { } now && now.CellId >> 16 == p.Cell >> 16 && S.State.Self.Travel == SelfTravel.Present);
        var named = step.Expect.Reply is not null || step.Expect.Item is not null || step.Place is not null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=use-again: nothing showed {AfterSeconds:0} s after the use of {step.Target.Name}; walking up again and using once more");
                foreach (var h in Approach(wcid, step.Target.Name, g => target = g)) { yield return h; }
            }

            var mark = S.State.Chat.Total;
            var use = keyGuid != 0 ? S.Actions.UseWithTarget(keyGuid, target, ActionSource.Host) : S.Actions.Use(target, ActionSource.Host);
            yield return use;
            Log($"use: {step.Target.Name} (0x{target:X8}){(keyGuid == 0 ? string.Empty : $" with {step.Item!.Name} (0x{keyGuid:X8})")}: {Describe(use)}");
            if (!named)
            {
                _read = "used=yes";
                yield break;
            }

            foreach (var h in Until(() => Seen(mark), step.Place is not null ? PortalSeconds : AfterSeconds)) { yield return h; }

            if (Seen(mark))
            {
                foreach (var h in StampCheck(step)) { yield return h; }

                _read = string.Create(CultureInfo.InvariantCulture, $"used=yes at=0x{S.State.Self.Position?.CellId ?? 0:X8}");
                yield break;
            }

            Warn(Stage, $"use: nothing showed after the use of {step.Target.Name} (attempt {attempt + 1}; expect {step.Expect}{(step.Place is null ? string.Empty : ", the arrival in " + step.Place)})", target, mark);
        }

        Stop("use:nothing-seen", $"nothing showed after the use of {step.Target.Name} and the recovery{Said(stepMark)}");
    }

    private IEnumerable<ActionHandle?> PickUpStep(QuestStep step)
    {
        if (step.Target is null)
        {
            foreach (var h in PickUpFloor(step)) { yield return h; }
        }
        else if (step.Target.Wcid is { } holder && NearestOfWcid(holder) is var guid && guid != 0 && IsCreature(guid))
        {
            foreach (var h in KillAndLoot(step)) { yield return h; }
        }
        else
        {
            foreach (var h in PickUpFromContainer(step)) { yield return h; }
        }

        var item = step.Item!.Wcid!.Value;
        _read = string.Create(CultureInfo.InvariantCulture, $"held={CountHeld(item)} worn={Yes(WornWcid(item))}");
    }

    /// <summary>
    /// A pick-up from a container in reach (M6 item 8), check, act, check: before, held already is done at once, else the chest is walked to; act, one open (a locked chest stops <c>pick-up:locked</c>: ACE sends only the sound 0x94, <see cref="ActionReason.Locked"/>, @95ef426d
    /// <c>WorldObjects/Chest.cs:105-112</c>; an empty one <c>pick-up:empty</c>), the wanted weenie class among its contents, one PickUp; after, the held count risen, and the chest closed on every way out.
    /// </summary>
    private IEnumerable<ActionHandle?> PickUpFromContainer(QuestStep step)
    {
        var item = step.Item!.Wcid!.Value;
        if (InPack(item).Count > 0 || WornWcid(item))
        {
            Log($"pick-up: {step.Item.Name} ({item}) is held already: the chest is left shut");
            yield break;
        }

        if (step.Target!.Wcid is not { } holder)
        {
            Stop("pick-up:container-unknown", $"the container of {step.Item} has no weenie class ({step.Todo ?? "no todo"})");
            yield break;
        }

        uint chest = 0;
        foreach (var h in Approach(holder, step.Target.Name, g => chest = g)) { yield return h; }

        if (IsCreature(chest))
        {
            foreach (var h in KillAndLoot(step)) { yield return h; } // it came into view only now: the second arm's shape

            yield break;
        }

        var mark = S.State.Chat.Total;
        var open = S.Actions.OpenContainer(chest, ActionSource.Host);
        yield return open;
        Log($"pick-up: open {step.Target.Name} (0x{chest:X8}) at {Distance(chest):0.0} m: {Describe(open)}");
        if (open.Outcome is { Reason: ActionReason.Locked })
        {
            Stop("pick-up:locked", $"{step.Target.Name} (0x{chest:X8}) is locked (its sound 0x94): TODO(m6-quest-chest-key): the station does not guess a key; a use step with the key comes before this pick-up{Said(mark)}");
        }

        if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
        {
            Stop("pick-up:not-opened", $"{step.Target.Name} (0x{chest:X8}) did not open: {Describe(open)}{Said(mark)}");
        }

        foreach (var h in Until(() => S.State.Containers.ViewedGuid == chest, AfterSeconds)) { yield return h; }

        if (S.State.Containers.ViewedGuid != chest)
        {
            Stop("pick-up:not-open", $"{step.Target.Name} (0x{chest:X8}) is not the open container {AfterSeconds:0} s after the open (open: 0x{S.State.Containers.ViewedGuid:X8}){Said(mark)}");
        }

        var empty = S.State.Containers.Viewed!.Entries.Count == 0;
        var wanted = empty ? 0 : InContainer(item, chest);
        Log($"pick-up: {step.Target.Name} holds {S.State.Containers.Viewed!.Entries.Count} entries ({Contents(chest)}); {step.Item.Name} ({item}): {(wanted == 0 ? "none" : $"0x{wanted:X8}")}");
        var heldBefore = CountHeld(item);
        ActionHandle? take = null;
        if (wanted != 0)
        {
            take = S.Actions.PickUp(wanted, 0, 0, ActionSource.Host);
            yield return take;
            Log($"pick-up: {step.Item.Name} (0x{wanted:X8}) from {step.Target.Name}: {Describe(take)}");
            foreach (var h in Until(() => CountHeld(item) > heldBefore, AfterSeconds)) { yield return h; }
        }

        var held = CountHeld(item);
        var close = S.Actions.CloseContainer(chest, ActionSource.Host);
        yield return close;
        foreach (var h in Until(() => S.State.Containers.ViewedGuid != chest, AfterSeconds)) { yield return h; }

        Log($"pick-up: close {step.Target.Name} (0x{chest:X8}): {Describe(close)}; open now: {Yes(S.State.Containers.ViewedGuid == chest)}");
        Verify(S.State.Containers.ViewedGuid != chest, $"pick-up: {step.Target.Name} is still open after the close ({Describe(close)})", chest, mark);
        if (empty)
        {
            Stop("pick-up:empty", $"{step.Target.Name} (0x{chest:X8}) holds nothing{Said(mark)}");
        }

        if (wanted == 0)
        {
            Stop("pick-up:not-in-container", $"{step.Target.Name} (0x{chest:X8}) holds no {step.Item}");
        }

        if (held <= heldBefore)
        {
            PickUpRefusedCheck(step, mark);
            Stop("pick-up:not-taken", $"{step.Item.Name} is not in the pack after the PickUp (held {heldBefore} -> {held}; {Describe(take!)}){Said(mark)}");
        }

        Log($"pick-up: {step.Item.Name} arrived: held {heldBefore} -> {held}");
    }

    /// <summary>
    /// M6 8 B2a (D118): an item's own 20-hour pick-up quest said no: the server's line "You have solved this quest too recently!" (or "too many times!", <see cref="FightScenario.QuestSolvedTooText"/>; ACE @95ef426d <c>Player_Inventory.cs:1249-1262</c>, <c>QuestManager.cs:558-578</c>) is the named stop <c>pick-up:too-recent</c>; a second try would only be refused again.
    /// </summary>
    /// <inheritdoc />
    protected override void PickUpRefusedCheck(QuestStep step, long mark)
    {
        if (QuestReads.Replied(S.State.Chat, mark, FightScenario.QuestSolvedTooText))
        {
            Stop("pick-up:too-recent", $"the server refused {step.Item!.Name} on its pick-up quest's timer (min_Delta 72000 s in the world's quest row){Said(mark)}");
        }
    }

    private IEnumerable<ActionHandle?> WearStep(QuestStep step)
    {
        var item = step.Item!.Wcid!.Value;
        foreach (var h in Wear(step.Item)) { yield return h; }

        if (!WornWcid(item))
        {
            Stop("wear:not-worn", $"{step.Item} is not worn after the wear");
        }

        _read = "worn=yes";
    }

    /// <summary>
    /// A buy, check, act, check: the vendor met, the shop opened, then the vendor counter's buy (M6 16 E5b: <see cref="VendorCounter.Buy"/>: <see cref="CheckedBuy.Judge"/> before, a trade note she holds changed at this
    /// counter when the coin is short and the shop takes notes (M6 16 E5a2: a side pack's too), one Buy), after the count risen by the step's count and the coin down; a refusal is by name, never retried.
    /// </summary>
    private IEnumerable<ActionHandle?> BuyStep(QuestStep step)
    {
        var vendorWcid = step.Target!.Wcid!.Value;
        var item = step.Item!.Wcid!.Value;
        var count = step.Count!.Value;
        uint vendor = 0;
        foreach (var h in Approach(vendorWcid, step.Target.Name, g => vendor = g)) { yield return h; }

        var open = S.Actions.OpenVendor(vendor, ActionSource.Automation);
        yield return open;
        foreach (var h in Until(() => S.State.Vendor is { } v && v.Vendor == vendor, AfterSeconds)) { yield return h; }

        var before = CountHeld(item);
        var counter = new VendorBuyResult();
        var host = new VendorCounterHost("buy", Log, Until, () => PackView.MainPackFree(S.State)) { CheckBefore = judge => Log($"buy: check before: {step.Target.Name} (0x{vendor:X8}) open {Describe(open)}: {judge.Why}") };
        foreach (var h in VendorCounter.Buy(S, new VendorBuyRequest(vendor, item, count) { VendorName = step.Target.Name, WareName = step.Item.Name, ArrivalSeconds = AfterSeconds }, counter, host)) { yield return h; }

        if (counter.Refusal == VendorCounter.Refused)
        {
            Stop("buy:refused", $"{step.Target.Name} refused {count} {step.Item.Name}: {counter.Outcome}; never retried");
        }
        else if (counter.Refusal is { } refused && refused != VendorCounter.AfterCheck)
        {
            Stop("buy:" + refused, counter.Why);
        }

        var coins = counter.CoinBefore; // the coin at the send, after any note was changed
        var after = CountHeld(item);
        var paid = coins - Coins();
        if (after < before + count || paid <= 0)
        {
            Stop("buy:after-check", string.Create(CultureInfo.InvariantCulture, $"after the buy the pack holds {after} {step.Item.Name} (was {before}, want +{count}) and the coin is {Coins()} (was {coins})"));
        }

        if (counter.NotesChanged > 0)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"buy: paid from the purse: {counter.NotesChanged} trade note(s) changed to coin at {step.Target.Name} before the buy (the coin was short of {counter.Cost})"));
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"buy: check after: {after} {step.Item.Name} (+{after - before}), paid {paid}"));
        _read = string.Create(CultureInfo.InvariantCulture, $"bought={after - before} paid={paid}");
    }

    private IEnumerable<ActionHandle?> RecallStep(QuestStep step)
    {
        var spell = step.Spell!.Value;
        var place = step.Place!;
        if (!S.State.Self.Spellbook.Knows(spell))
        {
            Stop("recall:not-known", $"the spellbook lacks the spell {spell} ({step.What})");
        }

        var leg = new QuestRecallLeg(Context, spell, place.Cell >> 16, step.What);
        foreach (var h in SubRunOf(leg)) { yield return h; }

        var at = S.State.Self.Position?.CellId ?? 0;
        if (leg.Failure is not null || at >> 16 != place.Cell >> 16)
        {
            Stop("recall:not-arrived", string.Create(CultureInfo.InvariantCulture, $"the recall {spell} to {place}: {leg.Failure ?? "the body is in cell 0x" + at.ToString("X8", CultureInfo.InvariantCulture)}"));
        }

        _read = string.Create(CultureInfo.InvariantCulture, $"arrived=0x{at:X8}");
    }

    /// <summary>
    /// A confirm (the confirm half of <c>m5-6-headless-confirm-reply</c>): check (the step's question open, waited <see cref="QuestStepLeg.AfterSeconds"/>), act (<see cref="QuestConfirmRule.Decide"/> over the open questions, each answer sent with
    /// <see cref="IActionApi.RespondToConfirmation"/>, one <c>confirm:</c> line each), check (the step's question closed).
    /// </summary>
    private IEnumerable<ActionHandle?> ConfirmStep(QuestStep step)
    {
        bool Asked() => S.State.PendingConfirmations.Any(q => q.Text.Contains(step.Expect.Reply!, StringComparison.Ordinal));
        foreach (var h in Until(Asked, AfterSeconds)) { yield return h; }

        if (!Asked())
        {
            Stop("confirm:no-question", $"no open question holds \"{step.Expect.Reply}\" {AfterSeconds:0} s into the step ({S.State.PendingConfirmations.Count} open)");
        }

        var answered = "none";
        foreach (var d in QuestConfirmRule.Decide([.. S.State.PendingConfirmations], step))
        {
            Log(d.Line);
            if (!d.Answers)
            {
                continue;
            }

            var reply = S.Actions.RespondToConfirmation(d.Question.Type, d.Question.Context, d.Accepted, ActionSource.Automation);
            yield return reply;
            Log($"confirm: answered type {d.Question.Type} {(d.Accepted ? "yes" : "no")}: {Describe(reply)}");
            if (d.Question.Text.Contains(step.Expect.Reply!, StringComparison.Ordinal))
            {
                answered = d.Accepted ? "yes" : "no";
            }
        }

        foreach (var h in Until(() => !Asked(), AfterSeconds)) { yield return h; }

        if (Asked() || answered == "none")
        {
            Stop("confirm:still-open", $"the question \"{step.Expect.Reply}\" is still open after the answer (answered {answered})");
        }

        _read = "answered=" + answered;
    }

    /// <summary>
    /// A kill (the kill task's shape, D54): check before (the talk before it armed the task, the body on the step's ground, else the rung change out), act (the ground fight as a sub-run, <c>until=chat:</c> the counter's complete text), check
    /// after (the counter lines since the step's mark, <see cref="QuestReads.KillCounters"/>: the last n and the complete line); a fight that ends without it is <c>kill:not-complete:&lt;n&gt;/&lt;count&gt;</c>.
    /// </summary>
    private IEnumerable<ActionHandle?> KillStep(QuestStep step, long mark)
    {
        var count = step.Count!.Value;
        Require(Verify(_lastTalk >= 0, $"kill: {step.Target} has no talk run before it (the arm)"), "kill: unarmed");
        Log($"kill: {count} {step.Target!.Name} on {step.Ground}; the task armed by the talk at step {_lastTalk}");
        var ground = GroundCatalog.Get(step.Ground!);
        foreach (var h in ToGround(ground, "kill")) { yield return h; }

        var spec = QuestStation.FightSpec($"quest-{_quest.Key}-kill-{step.Index}", ground.Key, null, QuestStation.UntilChat(step.Expect.Reply ?? QuestReads.TaskComplete));
        foreach (var h in Fight(spec, ground)) { yield return h; }

        var lost = S.State.Chat.Total - mark > S.State.Chat.Count;
        var counters = QuestReads.KillCounters(S.State.Chat, mark).Where(c => c.Plural.StartsWith(step.Target.Name, StringComparison.Ordinal)).ToList();
        var last = counters.Count == 0 ? null : counters[^1];
        var complete = counters.Any(c => c.Complete) || (step.Expect.Reply is { } text && Replied(step.Expect, mark) && text == QuestReads.TaskComplete);
        if (UntilMet != complete)
        {
            Warn(Stage, $"kill: the fight's until={(UntilMet ? "met" : "not-met")} and the counter lines' complete={Yes(complete)} disagree (the counter lines decide)");
        }
        Log(string.Create(CultureInfo.InvariantCulture, $"kill: counter lines {counters.Count}{(lost ? " (older lines past the chat's keep)" : string.Empty)}; the last: {(last is null ? "none" : $"\"{last.Text}\" (n={last.Killed}, complete={Yes(last.Complete)})")}; complete={Yes(complete)}"));
        foreach (var h in PeaceMode()) { yield return h; }

        if (!complete && NoCastable is { } nothing)
        {
            Stop("kill:" + FightScenario.NoCastableName, $"{last?.Killed ?? 0} of {count} killed: {nothing}"); // M6 7 K: the kit's or the trip's, by name
        }

        if (!complete)
        {
            Stop(string.Create(CultureInfo.InvariantCulture, $"kill:not-complete:{last?.Killed ?? 0}/{count}"), $"the fight ended without \"{step.Expect.Reply}\" ({counters.Count} counter lines)");
        }

        if (last is { } l && l.Killed < count)
        {
            Warn(Stage, $"kill: the complete line counts {l.Killed}, the file says {count}", 0, -1);
        }

        _read = string.Create(CultureInfo.InvariantCulture, $"counter={last?.Killed ?? count}/{count} complete=yes lines={counters.Count} until={(UntilMet ? "met" : "not-met")}");
    }

    /// <summary>
    /// A hunt-until (D58): check before (the count already held: done at once, logged; the autopilot's errand runs the same file), act (the ground fight as a sub-run, <c>want=</c> the item, <c>until=item:</c> the count), check after (the
    /// count held reached, and it arrived: it rose over the step).
    /// </summary>
    private IEnumerable<ActionHandle?> HuntStep(QuestStep step)
    {
        var item = step.Item!.Wcid!.Value;
        var count = step.Count!.Value;
        var chain = QuestCombineChain.Of(_quest, item); // M6 14 combine: the item's no-roll chain from the file's combine steps; its count is the item's equivalents
        int Held() => (int)QuestCombineChain.Units(chain, CountHeld);
        string Along() => chain.Count < 2 ? string.Empty : " (" + QuestCombineChain.Describe(chain, CountHeld) + ": the no-roll combine chain counted in " + step.Item!.Name + " equivalents)";
        var held = Held();
        if (held >= count)
        {
            Log($"hunt-until: {held} of {count} {step.Item.Name} ({item}) held already{Along()}: done at once, no hunt");
            _read = string.Create(CultureInfo.InvariantCulture, $"held={held}/{count} hunted=no");
            yield break;
        }

        var ground = GroundCatalog.Get(step.Ground!);
        Log($"hunt-until: {held} of {count} {step.Item.Name} ({item}) held{Along()}; hunting {ground.Key}{(step.Target is null ? string.Empty : " (the carrier " + step.Target + ")")}; {QuestStation.HuntGrind(step, count - held)}");
        foreach (var h in ToGround(ground, "hunt-until")) { yield return h; }

        var carriers = step.Carriers?.Select(c => new WantCarrier(c.Wcid, item, c.Shade, step.Item.Name)).ToList(); // M6 14 want loot: their corpses are walked to whatever the distance
        var spec = QuestStation.FightSpec($"quest-{_quest.Key}-hunt-{step.Index}", ground.Key, [item], QuestStation.UntilItem(item, count), carriers, chain);
        foreach (var h in Fight(spec, ground)) { yield return h; }

        var after = Held();
        if (chain.Count > 1)
        {
            Log($"hunt-until: {after} of {count} {step.Item.Name} ({item}) held after the fight{Along()}");
        }

        foreach (var h in PeaceMode()) { yield return h; }

        if (UntilRefused && after < count)
        {
            Stop(string.Create(CultureInfo.InvariantCulture, $"hunt-until:refused:{item}"), $"the server refused {step.Item.Name} ({item}) at the pick-up (the fight ended with until=refused); nothing is recorded: the quest is not due");
        }

        if ((after < count || after <= held) && NoCastable is { } nothing)
        {
            Stop("hunt-until:" + FightScenario.NoCastableName, $"{after} of {count} held: {nothing}"); // M6 7 K
        }

        if (after < count || after <= held)
        {
            Stop(string.Create(CultureInfo.InvariantCulture, $"hunt-until:short:{after}/{count}"), $"the fight ended with {after} {step.Item.Name} held (was {held})");
        }

        if (!UntilMet)
        {
            Warn(Stage, $"hunt-until: {after} of {count} held, but the fight's verdict did not say until=met");
        }

        _read = string.Create(CultureInfo.InvariantCulture, $"held={after}/{count} hunted=yes arrived={after - held} until={(UntilMet ? "met" : "not-met")}");
    }

    // ---------------------------------------------------------------------------------------------- the recipes

    /// <summary>
    /// A <c>combine</c> or an <c>apply</c> (M6 item 8), one runner over one reader (<see cref="RecipeOutcomes"/>): the file's count of uses, each check, act, check (<see cref="RecipeUse"/>). Two of the same weenie class are two different objects, so a step of one weenie class needs twice its count before the first use (ACE refuses an item on itself; nothing is spent on
    /// a chain that cannot finish). A failed roll stops and says what was lost, never a silent continue.
    /// </summary>
    private IEnumerable<ActionHandle?> RecipeStep(QuestStep step)
    {
        var word = QuestStepKinds.Word(step.Kind);
        if (step.Item!.Wcid is not { } sourceWcid || step.Target!.Wcid is not { } targetWcid || step.Expect.Item!.Wcid is not { } resultWcid)
        {
            Stop($"{word}:unresolved", $"the step's weenie classes are not established ({step.Todo ?? "no todo"})");
            yield break;
        }

        var times = step.Count ?? 1;
        if (QuestCombineChain.IsNoRollLink(step) && NoRollUsesLeft(step, sourceWcid, resultWcid, times) is var left && left < times)
        {
            // M6 14 combine: the hunt combined as it went; the state this step reaches is partly or wholly held already (its result counted with everything above it on the chain, in the result's units)
            if (left <= 0)
            {
                _read = string.Create(CultureInfo.InvariantCulture, $"outcome=done-already uses=0/{times}");
                yield break;
            }

            times = left;
        }

        var tally = new RecipeTally(times);
        var expect = new RecipeExpectation(resultWcid, step.Expect.Reply!, step.FailItem?.Wcid, step.FailReply);
        var objects = HeldItems().Count(i => i.Wcid == sourceWcid);
        if (sourceWcid == targetWcid && objects < times * 2)
        {
            Stop($"{word}:short", string.Create(CultureInfo.InvariantCulture, $"{times} use(s) of {step.Item.Name} ({sourceWcid}) on itself need {times * 2} different objects; {objects} held"));
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{word}: {step.What}: {times} use(s) of {step.Item} on {step.Target}; result {step.Expect.Item}; fail result {(object?)step.FailItem ?? "none"}"));
        if (step.RecipeSkill is not null)
        {
            foreach (var h in RecipeSkillReady(step, word)) { yield return h; } // M6 14 (D136): the roll's skill trained and raised before the first use
        }

        for (var n = 1; n <= times; n++)
        {
            foreach (var h in DefendIfAttacked()) { yield return h; } // M5 D88: an attacker is fought before the next use

            foreach (var h in RecipeUse(step, word, sourceWcid, targetWcid, expect, n, tally)) { yield return h; }
        }

        _read = tally.Read(RecipeOutcomes.Success);
    }

    /// <summary>
    /// M6 14 combine: the uses a no-roll link step still needs: its count less what is held of its result and everything above it on the quest's chain from the result, in the result's units (<see cref="QuestCombineChain.Units"/>). The hunt combined
    /// as it went, so a step whose result is held at its count passes as done, logged; one partly done runs the uses left. 0 or less: done.
    /// </summary>
    private int NoRollUsesLeft(QuestStep step, uint sourceWcid, uint resultWcid, int times)
    {
        var up = QuestCombineChain.Of(_quest, resultWcid);
        var units = QuestCombineChain.Units(up, CountHeld);
        var left = (int)Math.Max(0, times - units);
        var word = QuestStepKinds.Word(step.Kind);
        if (left < times)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"{word}: {step.What}: check before: {QuestCombineChain.Describe(up, CountHeld)} is {units} {step.Expect.Item!.Name} or their worth, of the {times} the step makes; {(left == 0 ? "done already (the hunt combined as it went): no use" : $"{left} use(s) left of {CountHeld(sourceWcid)} {step.Item!.Name} held")}"));
        }

        return left;
    }

    /// <summary>
    /// One use, check, act, check, each one line. Before: peace mode (<c>allow_combat_mode_crafting</c> is false here: @95ef426d <c>Managers/RecipeManager.cs:51-57</c>), not busy, both items held and two objects where the classes match. Act: one <see cref="IActionApi.UseRecipe"/>. After: the reader's verdict and the counts.
    /// <para>The crafting chance dialog (<c>RecipeManager.cs:94</c>, <c>:322-352</c>): a recipe with a difficulty asks first when the character has the option on. The option is off on a new character (<c>CharacterOptions1.Default</c> lacks <c>UseCraftSuccessDialog</c> 0x80000000, @95ef426d <c>ACE.Entity/Enum/CharacterOptions1.cs:45-47</c>, set by
    /// <c>Factories/PlayerFactory.cs:656</c>) and the automation never sets it, but the options panel can, and the character keeps it, so the question is answered Yes by <see cref="IActionApi.RespondToConfirmation"/> and its percent logged. ACE's UseDone 0 then comes before the recipe (:351) and none after it (:137-143), so the read waits on the items.</para>
    /// </summary>
    private IEnumerable<ActionHandle?> RecipeUse(QuestStep step, string word, uint sourceWcid, uint targetWcid, RecipeExpectation expect, int n, RecipeTally tally)
    {
        foreach (var h in PeaceMode()) { yield return h; }

        var mode = S.State.Self.Qualities.CombatMode;
        if (mode != CombatModes.NonCombat)
        {
            _read = tally.Read(RecipeOutcomes.CombatMode);
            Stop($"{word}:{RecipeOutcomes.CombatMode}", $"the combat mode is {mode} after asking for peace; ACE refuses a craft out of peace mode");
        }

        foreach (var h in Until(() => S.Actions.BusyReason is null, AfterSeconds)) { yield return h; }

        if (S.Actions.BusyReason is { } busy)
        {
            _read = tally.Read(RecipeOutcomes.Busy);
            Stop($"{word}:{RecipeOutcomes.Busy}", $"still busy {AfterSeconds:0} s before use {n}: {busy}");
        }

        var before = HeldItems();
        var source = before.FirstOrDefault(i => i.Wcid == sourceWcid);
        var target = before.FirstOrDefault(i => i.Wcid == targetWcid && i.Guid != source?.Guid);
        if (source is null || target is null)
        {
            _read = tally.Read("not-held");
            Stop($"{word}:not-held", string.Create(CultureInfo.InvariantCulture, $"use {n}: the pack holds {before.Count(i => i.Wcid == sourceWcid)} {step.Item!.Name} and {before.Count(i => i.Wcid == targetWcid)} {step.Target!.Name}: not two objects to use"));
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{word}: check before: use {n}/{tally.Times} {source!.Name} 0x{source.Guid:X8} on {target!.Name} 0x{target.Guid:X8}; held source {Held(before, sourceWcid)} target {Held(before, targetWcid)} result {Held(before, expect.ResultWcid)}; peace=yes busy=no"));
        var mark = S.State.Chat.Total;
        var use = S.Actions.UseRecipe(source.Guid, target.Guid, ActionSource.Host);
        yield return use;
        Log($"{word}: act: use {n}/{tally.Times} UseWithTarget 0x{source.Guid:X8} on 0x{target.Guid:X8}: {Describe(use)}");
        uint? Code() => use.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.ServerError } o ? o.Code : null;
        RecipeOutcome Read(bool ended) => RecipeOutcomes.Read(expect, new RecipeEvidence(before, HeldItems(), LinesSince(mark), Code(), ended));
        var question = S.State.PendingConfirmations.FirstOrDefault(q => q.Type == RecipeOutcomes.CraftDialogType);
        if (question is not null)
        {
            var percent = RecipeOutcomes.ChancePercent(question.Text);
            var yes = S.Actions.RespondToConfirmation(question.Type, question.Context, true, ActionSource.Host);
            yield return yes;
            Log(string.Create(CultureInfo.InvariantCulture, $"{word}: the crafting dialog: percent={(percent is { } p ? p.ToString("0.##", CultureInfo.InvariantCulture) : "unread")} text=\"{question.Text}\" decision=yes: {Describe(yes)}"));
            foreach (var h in Until(() => Read(ended: false).Settled, AfterSeconds)) { yield return h; }
        }

        var outcome = Read(ended: true);
        var after = HeldItems();
        tally.Add(outcome);
        _read = tally.Read(outcome.Name);
        Log(string.Create(CultureInfo.InvariantCulture, $"{word}: check after: use {n}/{tally.Times} outcome={outcome.Name} consumed=[{outcome.ConsumedText}] created=[{outcome.CreatedText}] line={Yes(outcome.Line is not null)}; held source {Held(before, sourceWcid)} -> {Held(after, sourceWcid)} target {Held(before, targetWcid)} -> {Held(after, targetWcid)} result {Held(before, expect.ResultWcid)} -> {Held(after, expect.ResultWcid)}; {outcome.Why}{Said(mark)}"));
        if (outcome.Verdict == RecipeVerdict.FailedRoll)
        {
            Stop($"{word}:{RecipeOutcomes.FailedRoll}", $"use {n}/{tally.Times} lost its roll: {outcome.Why}; lost {outcome.ConsumedText}; arrived {outcome.CreatedText}");
        }

        if (outcome.Verdict != RecipeVerdict.Success)
        {
            Stop($"{word}:{outcome.Name}", $"use {n}/{tally.Times}: {outcome.Why}; consumed {outcome.ConsumedText}");
        }
    }

    /// <summary>Every held object the recipe can touch: the main pack, the side packs' contents and what is worn (the side packs themselves are not items here).</summary>
    private List<RecipeHeldItem> HeldItems()
    {
        var inventory = S.State.Self.Inventory;
        var list = new List<RecipeHeldItem>();
        void Add(uint guid)
        {
            if (S.State.Objects.TryGet(guid, out var o))
            {
                list.Add(new RecipeHeldItem(guid, o.Weenie.WeenieClassId, o.Name ?? "?", Math.Max(1, StackOf(guid))));
            }
        }

        foreach (var e in inventory.Pack)
        {
            Add(e.Guid);
        }

        foreach (var pack in inventory.SidePacks)
        {
            foreach (var e in inventory.ItemsIn(pack.Guid))
            {
                Add(e.Guid);
            }
        }

        foreach (var e in inventory.Equipped)
        {
            Add(e.Guid);
        }

        return list;
    }

    private static int Held(List<RecipeHeldItem> items, uint wcid) => items.Where(i => i.Wcid == wcid).Sum(i => i.Stack);

    private List<ChatLine> LinesSince(long mark)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        var lines = new List<ChatLine>(fresh);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            lines.Add(chat[i]);
        }

        return lines;
    }

    /// <summary>What a recipe step has done over its uses, for its <c>read</c> field.</summary>
    private sealed class RecipeTally(int times)
    {
        public int Times => times;

        private int _uses;
        private int _consumed;
        private int _created;

        public void Add(RecipeOutcome outcome)
        {
            _uses += outcome.Verdict == RecipeVerdict.Success ? 1 : 0;
            _consumed += outcome.Consumed.Sum(c => c.Amount);
            _created += outcome.Created.Sum(c => c.Amount);
        }

        public string Read(string outcome) => string.Create(CultureInfo.InvariantCulture, $"outcome={outcome} uses={_uses}/{times} consumed={_consumed} created={_created}");
    }

    // ---------------------------------------------------------------------------------------------- the sub-runs

    /// <summary>The body on <paramref name="ground"/>: nothing when it stands there; else the rung change out to it (item 4's station, D50) as a sub-run, checked after.</summary>
    private IEnumerable<ActionHandle?> ToGround(HuntingGround ground, string kind)
    {
        if (ground.InLandblocks(Here().CellId))
        {
            Log($"{kind}: the body stands on {ground.Key} already");
            yield break;
        }

        Log($"walking: purpose=quest ({kind}: the quest {_quest.Key}'s ground {ground.Key} by the rung change out)");
        var run = new RungChangeStation(RungDirection.Out).Start(Context.WithDungeon(ground));
        foreach (var h in SubRunOf(run)) { yield return h; }

        Log($"{kind}: the way to {ground.Key}: {run.VerdictFields()}");
        if (!run.Ok || !ground.InLandblocks(Here().CellId))
        {
            Stop($"{kind}:no-ground", $"the rung change out to {ground.Key} ended at cell 0x{Here().CellId:X8}: {run.VerdictFields()}");
        }
    }

    /// <summary>The ground fight as a sub-run through the station's seam, its verdict logged; <see cref="UntilMet"/> reads its <c>until=</c> field. While it runs it is shown (<see cref="SubFight"/>, M6 14 errand lapse).</summary>
    private IEnumerable<ActionHandle?> Fight(StationSpec spec, HuntingGround ground)
    {
        if (_subRun is null)
        {
            Stop("fight:no-factory", "TODO(m6-quest-no-fight): the station was made without the fight's sub-run (a host with no factory)");
        }

        Log($"fight: {spec.Name} kind={spec.Kind} {string.Join(" ", spec.Arguments.Select(a => a.Key + "=" + a.Value))}");
        var run = _subRun!(spec, Context.WithDungeon(ground));
        _subFightRun = run;
        try
        {
            foreach (var h in SubRunOf(run)) { yield return h; }
        }
        finally
        {
            _subFightRun = null;
        }

        var fields = run.VerdictFields();
        UntilMet = (" " + fields + " ").Contains(" " + QuestStation.UntilMetField + " ", StringComparison.Ordinal);
        UntilRefused = (" " + fields + " ").Contains(" " + QuestStation.UntilRefusedField + " ", StringComparison.Ordinal);
        NoCastable = run.Failure is { } failure && failure.StartsWith(FightScenario.NoCastableFailure, StringComparison.Ordinal) ? failure : null;
        Log($"fight: {spec.Name} ended ok={Yes(run.Ok)} until_met={Yes(UntilMet)}: {fields}");
    }

    private IStationRun? _subFightRun;

    /// <inheritdoc />
    /// <remarks>M6 14 errand lapse: the hunt-until's and the kill's fight while it runs (the factory's den fight wraps a <see cref="FightScenario"/>).</remarks>
    public IStoppableFight? SubFight => _subFightRun is { } run ? (run as IStationInner)?.Scenario as IStoppableFight ?? run as IStoppableFight : null;

    /// <summary>True when the last sub-run fight's verdict said <c>until=met</c>.</summary>
    private bool UntilMet { get; set; }

    /// <summary>True when the last sub-run fight's verdict said <c>until=refused</c> (the server refused the item: M6 5 F1, review 5-R4).</summary>
    private bool UntilRefused { get; set; }

    /// <summary>M6 7 K: the last sub-run fight's failure when it ended with no war spell it could cast (<see cref="FightScenario.NoCastableFailure"/>), else null: the step ends by that name.</summary>
    private string? NoCastable { get; set; }

    /// <summary>Steps a sub-run until it ends (its own check, act, check lines), then disposes it.</summary>
    private IEnumerable<ActionHandle?> SubRunOf(IStationRun run)
    {
        try
        {
            while (run.Step(S, S.Clock.Now) == ScenarioStatus.Running)
            {
                yield return null;
            }
        }
        finally
        {
            (run as IDisposable)?.Dispose();
        }
    }

    /// <summary>The town trip by the Town Network to the step's place (<see cref="TownNetworkTrip.Choose"/> over one candidate, the step's NPC at its place), each hop a <see cref="PortalHopLeg"/>.</summary>
    private IEnumerable<ActionHandle?> TownTrip(QuestStep step, QuestPlace place)
    {
        var goal = place.Cell >> 16;
        var town = Context.Routes.PlaceOf(RungRoute.Hex(goal))?.Name ?? place.Name;
        var candidate = new TownTripCandidate(town, step.Target?.Name ?? place.Name, step.Target?.Wcid ?? 0, new ScrivenerPlace(town, 0, place.Cell, place.Cell, place.Point ?? Vector3.Zero));
        var here = Here();
        var input = new TownTripInput(
            PlaceKey(here.CellId >> 16) ?? PlaceKey(Context.Dungeon.Home.LifestoneCell >> 16),
            S.State.Self.Qualities.Level,
            RecallReadiness.Of(S.State, Context.Facts, Context.Spells).Castable,
            Context.Routes.StartLifestone.Cell >> 16 == Context.Dungeon.Home.LifestoneCell >> 16);
        var choice = TownNetworkTrip.Choose([candidate], input, Context.Routes, LeverDoors.Default);
        Log($"go: by the Town Network to {town}: {choice.Why}");
        if (choice.Chosen is not { } trip)
        {
            Stop("go:town-network", $"{choice.Stop}: {town} is not reached by the Town Network ({choice.Why})");
            yield break;
        }

        if (trip.Hops.Count > 0 && trip.Hops[0].FromLandblocks.Count > 0 && !trip.Hops[0].FromLandblocks.Contains((ushort)(here.CellId >> 16)))
        {
            var home = Context.Dungeon.Home;
            Log($"walking: purpose=town-trip (the quest {_quest.Key}: to {home.Town}'s lifestone point, in the place of {trip.Hops[0].Name}, the first portal to {town})");
            foreach (var h in TravelFightingBack(new TravelRequest(Destination("home-lifestone-point", home.LifestoneCell, home.LifestonePoint), TravelPurpose.Bind), "the trip's start", optional: true)) { yield return h; }

            if (LastTravelFailure is not null || !trip.Hops[0].FromLandblocks.Contains((ushort)(Here().CellId >> 16)))
            {
                Stop("go:trip-start", $"the walk to the trip's start ended at cell 0x{Here().CellId:X8}, not in {trip.Hops[0].Name}'s place: {LastTravelFailure ?? "the check after"}");
            }
        }

        Log($"go: by the Town Network to {town}: hops {trip.HopKeys}, the drop 0x{trip.DropCell:X8}");
        for (var n = 0; n < trip.Hops.Count; n++)
        {
            foreach (var h in Hop(trip.Hops[n], $"the quest {_quest.Key} to {town}, hop {n + 1} of {trip.Hops.Count}", "go:hop")) { yield return h; }
        }

        if (!trip.Place.Landblocks.Contains((ushort)(Here().CellId >> 16)))
        {
            Stop("go:off-town", $"after the hops the body stands in cell 0x{Here().CellId:X8}, not in {town}'s place ({trip.Place.Key})");
        }
    }

    /// <summary>One portal hop (<see cref="PortalHopLeg"/>) as a sub-run, its <c>quest_hop:</c> line; a failure stops by name (<c>&lt;prefix&gt;:&lt;the hop's name&gt;</c>).</summary>
    private IEnumerable<ActionHandle?> Hop(RungPortal portal, string why, string prefix)
    {
        var leg = new PortalHopLeg(Context, portal, why);
        while (leg.Step(S, S.Clock.Now) == ScenarioStatus.Running)
        {
            yield return null;
        }

        var seen = leg.Outcome;
        Log(string.Create(CultureInfo.InvariantCulture, $"quest_hop: edge={portal.Key} to={RungRoute.Hex(Here().CellId >> 16)} walk_m={seen.WalkMetres:0} taken={Yes(seen.Taken)} recoveries={seen.Recoveries} ok={Yes(leg.Failure is null)}"));
        if (leg.Failure is not null)
        {
            Stop(prefix + ":" + (leg.FailureName ?? portal.Key), $"the hop {portal.Key} failed: {leg.Failure}");
        }
    }

    /// <summary>The exit hop of <paramref name="ground"/> when <see cref="RungRoute.Decide"/>'s home plan from it starts with the exit portal and that portal leads into <paramref name="goal"/>'s travel place (the island's Refuge, D58); else null.</summary>
    private RungPortal? ExitHopTo(HuntingGround ground, uint goal)
    {
        var here = Here();
        var landblock = here.CellId >> 16;
        var input = new RungRouteInput(
            ground,
            RungDirection.Home,
            RungWhere.AtGround,
            PlaceKey(landblock) ?? RungRoute.Hex(landblock),
            S.State.Self.Qualities.Level,
            RecallReadiness.Of(S.State, Context.Facts, Context.Spells).Castable,
            KnowsPortalRecall: false,
            KnowsPortalTie: false,
            PortalTie.Unknown,
            HoldsGem: false);
        var plan = RungRoute.Decide(input, Context.Routes);
        var first = plan.Legs.Count == 0 ? null : plan.Legs[0];
        var leads = first is { Kind: RungLegKind.Hop, Portal: { } p } && first.Label.StartsWith("exit:", StringComparison.Ordinal) && SamePlace(p.DestLandblock, goal);
        Log($"go: the home plan from {ground.Key}: route={plan.RouteKey} legs=[{string.Join(",", plan.Legs.Select(l => l.KindName + ":" + l.Label))}]; {(leads ? "its first leg, the exit, leads to the place: the exit hop only" : "its first leg does not lead to the place: the rung change home")}");
        return leads ? first!.Portal : null;
    }

    // ---------------------------------------------------------------------------------------------- the reads

    private IEnumerable<ActionHandle?> Reward(long mark)
    {
        foreach (var h in Until(() => QuestReads.Reward(S.State.Chat, mark) is not null, RewardSeconds)) { yield return h; }

        if (QuestReads.Reward(S.State.Chat, mark) is { } reward)
        {
            _rewardXp += reward.Experience;
            _rewardLines += reward.Lines;
            Log(string.Create(CultureInfo.InvariantCulture, $"reward: \"You've earned {reward.Experience:N0} experience.\" ({reward.Lines} line(s)); the property {S.State.Self.Qualities.TotalExperience} (+{XpGained} this run); reward lines {_rewardLines} this run"));
        }
    }

    private CellFrame Here() => S.State.Self.Position ?? throw new InvalidOperationException("the body's position is unknown");

    private HuntingGround? BodyGround(uint cell) => Context.Grounds.FirstOrDefault(g => g.InLandblocks(cell));

    private string? PlaceKey(uint landblock) => TownNetworkTrip.PlaceKeyOf(landblock, Context.Routes);

    private bool SamePlace(uint landblock, uint goal) => landblock == goal || (PlaceKey(landblock) is { } a && a == PlaceKey(goal));

    /// <summary>Stops the run by name: the failed check is logged, the name kept for <c>fail=</c>, and the step ends (the guard turns the throw into the failure).</summary>
    private void Stop(string name, string why)
    {
        _failName ??= name;
        throw new InvalidOperationException(name + ": " + why);
    }

    private IEnumerable<ActionHandle?> StopSteps(string name, string why)
    {
        Stop(name, why);
        yield break;
    }

    /// <summary>Runs <paramref name="inner"/>, turning a throw (a <c>Require</c>, a <see cref="Stop"/>) into the run's failure by name, so the step's line and the verdict are written.</summary>
    private IEnumerable<ActionHandle?> Guarded(IEnumerable<ActionHandle?> inner)
    {
        using var e = inner.GetEnumerator();
        for (;;)
        {
            var more = false;
            try
            {
                more = e.MoveNext();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                _failName ??= Kebab(ex.Message.Split(';')[0]);
                Failure ??= Stage + ": " + ex.Message;
                Check(false, "the step failed: " + Failure);
                Warn(Stage, ex.Message);
            }

            if (!more)
            {
                yield break;
            }

            yield return e.Current;
        }
    }

    private void Emit()
    {
        if (_lined || S is null)
        {
            return;
        }

        _lined = true;
        Log(LinePrefix + " " + Fields());
    }

    private static string Kebab(string text)
    {
        var chars = text.Trim().Replace(": ", ":", StringComparison.Ordinal).Select(c => char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c));
        var s = new string(chars.ToArray());
        return s.Length > 80 ? s[..80] : s;
    }

    /// <summary><c>NotWanted</c> as <c>not-wanted</c>.</summary>
    private static string KebabCase(string name) => string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string Word(string text) => text.Replace(' ', '_');
}

/// <summary>A recall step's leg (M6 item 5): the magic mode, the cast (<see cref="TravelLeg.Cast"/>: the travel cast rules, fizzles retried), the arrival in the landblock (<see cref="TravelLeg.ArriveIn"/>), peace.</summary>
internal sealed class QuestRecallLeg(JourneyContext context, uint spell, uint landblock, string what) : TravelLeg("quest-recall", context)
{
    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Body()
    {
        Stage = "recall";
        foreach (var h in MagicNow())
        {
            yield return h;
        }

        var cast = new CastResult();
        foreach (var h in Cast(spell, 0, cast))
        {
            yield return h;
        }

        Log($"recall: cast {spell} for {what}: {Describe(cast.Handle!)}");
        foreach (var h in ArriveIn(landblock, cast.TravelMark, "recall " + spell.ToString(CultureInfo.InvariantCulture)))
        {
            yield return h;
        }

        foreach (var h in PeaceNow())
        {
            yield return h;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"recall: arrived in cell 0x{S.State.Self.Position?.CellId ?? 0:X8}"));
    }
}
