using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 S2b (D125, the owner's addition): each worn item's own mana, tracked from what the server sends.
/// </summary>
/// <remarks>
/// <para><b>What ACE sends.</b> The player's heartbeat (every 5 s: <c>HeartbeatInterval</c> defaults to 5, WorldObject_Tick.cs:44-56; <c>Player.Heartbeat</c> calls <c>ManaConsumersTick</c>, Player_Tick.cs:110-116) burns each worn item whose spells
/// are on (<c>IsAffecting</c>) by <c>-ManaRate x 5</c>, accumulated until a whole point (Player_Tick.cs:624-660). Setting <c>ItemCurMana</c> only sets the property (WorldObject_Properties.cs:2163-2167): no message goes to the client as the mana falls.
/// The tick sends exactly two lines: "Your X is low on Mana." once when under 120 s are left (<c>CheckLowMana</c>, :662-679) and "Your X is out of Mana." with the ItemManaDepleted sound at 0 (<c>HandleManaDepleted</c>, :681-699),
/// both <c>GameMessageSystemChat</c> of ChatMessageType.Magic (7); two seconds later the item's spells are removed (<c>RemoveItemSpell</c>, Creature_Magic.cs:140-166). The client reads the mana itself only from an appraisal
/// (ItemCurMana and ItemMaxMana are assessment properties, PropertyInt.cs:154-157; <c>AppraiseInfo.BuildProperties</c>) or as a fraction from a QueryItemMana (GameAction 0x0263, answered by 0x0264 once, never pushed:
/// Player.cs:440-455, WorldObject.cs:621-637), for which the client has no verb (<c>TODO(m5-query-item-mana)</c>).</para>
/// <para><b>So the watch</b> re-appraises each worn magic item every <see cref="ReadEverySeconds"/> (an Identify, a read: ActionSource.Host), keeps the last read with its time and predicts the mana between reads at the item's rate
/// (<see cref="Predicted"/>); a read logs the read value beside the prediction. The chat's low and out lines are logged as alarms and set the prediction to 0 for "out". The retail figure was the same: the examine window shows the
/// appraisal's number, read when it opened (<c>Appraisal_ShowMagicInfo</c>, 11.6096 0x004B34B0).</para>
/// <para><b>M6 item 14, the field refill</b> (closes <c>m5-stone-field-refill</c>). Every <see cref="FieldCheckSeconds"/> the watch asks <see cref="WornManaRefill.Due"/> which worn item would be empty before she is next home
/// (<see cref="WornManaMemory.SecondsToNextVisit"/>); when one would and she holds a charged stone or charge, it is used (check: the prediction and the reason; act: <see cref="IActionApi.UseManaStone"/>, a plain Use; check: the item read
/// again and the gain logged). A stone goes on the player (ACE's even ration over the worn items), never on one worn item: an empty stone used on an item drains and destroys it (ManaStone.cs:84-113), and in the field the stone's
/// appraisal may be stale; only a Mana Charge, which is always full, goes on the due item itself. An item already out is left to the home visit, which also wears it again (its spells come back only then). A worn item that holds mana
/// and burns none between two reads has its spells off: it is marked (<see cref="CasterSpellsDropped"/>) for the home visit to wear again.</para>
/// </remarks>
/// <param name="log">Where the lines go.</param>
/// <param name="category">The log category.</param>
public sealed partial class WornManaWatch(ILogSink log, string category)
{
    /// <summary>Seconds between two reads of one worn item: at the Compass's ManaRate of -1/120 (Major War Magic Aptitude, base mana 10: <c>CalculateManaRate</c>, LootGenerationFactory_Magic.cs:105-112) five minutes burn 2.5 points. Lifestone's own.</summary>
    public const double ReadEverySeconds = 300;

    /// <summary>Major War Magic Aptitude's base mana (spell 2534, the dat's SpellTable; pinned by <c>ItemManaDatTests</c>): round 12's Compass.</summary>
    public const int CompassBaseMana = 10;

    /// <summary>The Compass's seconds per point burned: <c>ceil(1200 / base mana)</c> (LootGenerationFactory_Magic.cs:105-112).</summary>
    public const double CompassSecondsPerPoint = 120;

    /// <summary>The seconds ACE warns at (<c>CheckLowMana</c>'s <c>lowManaWarningSeconds</c>, Player_Tick.cs:664).</summary>
    public const double LowWarningSeconds = 120;

    /// <summary>M6 item 14: seconds between two checks of the field refill. Lifestone's own.</summary>
    public const double FieldCheckSeconds = 30;

    /// <summary>M6 item 14: seconds before a refused field use is tried again. Lifestone's own.</summary>
    public const double FieldRetrySeconds = 60;

    /// <summary>M6 item 14: the tries one stone gets in the field before it is left to the home visit. Lifestone's own.</summary>
    public const int FieldTriesAStone = 3;

    /// <summary>M6 item 14: the points an item's rate must have burned between two equal reads before its spells are said to be off.</summary>
    public const double SilentBurnPoints = 3;

    private readonly Dictionary<uint, Reading> _reads = [];
    private readonly HashSet<uint> _reread = [];
    private readonly HashSet<uint> _silent = [];
    private readonly Dictionary<uint, int> _fieldBefore = [];
    private readonly Dictionary<uint, int> _fieldTries = [];
    private readonly Dictionary<uint, int?> _fieldSpent = [];
    private readonly HashSet<uint> _fieldSaid = [];
    private IdentifyHandle? _pending;
    private uint _pendingGuid;
    private ActionHandle? _fieldUse;
    private ManaUse _fieldPick;
    private double _fieldNextAt;
    private int _chargedHeld;

    /// <summary>One read of a worn item.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Mana">ItemCurMana at the read.</param>
    /// <param name="MaxMana">ItemMaxMana.</param>
    /// <param name="Rate">ManaRate (negative, a second).</param>
    /// <param name="At">The session clock of the read.</param>
    /// <param name="Out">True once the server said it is out of mana (since the read).</param>
    public readonly record struct Reading(string Name, int Mana, int MaxMana, double Rate, double At, bool Out);

    /// <summary>The reads so far, by guid.</summary>
    public IReadOnlyDictionary<uint, Reading> Reads => _reads;

    /// <summary>The reads made.</summary>
    public int ReadCount { get; private set; }

    /// <summary>The low and out lines seen.</summary>
    public int Alarms { get; private set; }

    /// <summary>M6 item 14: the stones and charges the field refill used.</summary>
    public int FieldRefills { get; private set; }

    /// <summary>M6 item 14: where the home visits' times are kept (<see cref="WornManaMemory.Shared"/> unless a test gives its own).</summary>
    public WornManaMemory Memory { get; init; } = WornManaMemory.Shared;

    /// <summary>The mana <paramref name="guid"/> holds now by the last read and its rate (0 once the server said it is out), or null when it was never read or has no mana.</summary>
    public int? Predicted(uint guid, double now) =>
        _reads.TryGetValue(guid, out var r) && r.MaxMana > 0 ? r.Out ? 0 : Math.Max(0, (int)Math.Floor(r.Mana + (r.Rate * Math.Max(0, now - r.At)))) : null;

    /// <summary>Takes an appraisal of a worn item and logs it beside the prediction it replaces.</summary>
    /// <param name="guid">The item.</param>
    /// <param name="name">Its name.</param>
    /// <param name="appraisal">The appraisal.</param>
    /// <param name="now">The session clock.</param>
    public void Read(uint guid, string name, Appraisal appraisal, double now)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        if (appraisal.ItemMaxMana is not > 0 || appraisal.ItemMana is not { } mana)
        {
            _reads[guid] = new Reading(name, 0, 0, 0, now, false);
            return;
        }

        var before = Predicted(guid, now);
        var known = _reads.TryGetValue(guid, out var last);
        var since = known ? now - last.At : double.NaN;
        var rate = appraisal.Float(5) ?? 0.0;
        var max = appraisal.ItemMaxMana.Value;
        _reads[guid] = new Reading(name, mana, max, rate, now, false);
        ReadCount++;
        if (_fieldBefore.Remove(guid, out var was))
        {
            // M6 item 14, the check after a field use: the item read again from the server, the gain named
            if (mana > was)
            {
                log.Info(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: check after: '{name}' 0x{guid:X8} {was} -> {mana} of {max} (+{mana - was})"));
            }
            else
            {
                log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: check after: '{name}' 0x{guid:X8} {was} -> {mana} of {max}: NOTHING GAINED (named: field-refill-no-gain); the home visit refills it"));
            }
        }
        else if (known && last.MaxMana > 0 && !last.Out && rate < 0 && mana > 0 && mana == last.Mana && -rate * since >= SilentBurnPoints)
        {
            if (_silent.Add(guid))
            {
                Dropped.Mark(name, [guid]);
                log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: '{name}' 0x{guid:X8} holds {mana}/{max} and burned none in {since:0} s (its rate would have burned {-rate * since:0}): its spells are off (ACE removes them at 0 and a refill does not bring them back, Player_Inventory.cs:313-315): the next home visit wears it again"));
            }
        }
        else if (known && mana < last.Mana)
        {
            _silent.Remove(guid); // it burns: its spells are on
        }

        if (known && mana > last.Mana)
        {
            _fieldSaid.Remove(guid); // refilled since: a new shortage is said again
        }

        var left = rate < 0 ? string.Create(CultureInfo.InvariantCulture, $"burns {-rate:0.####}/s while its spells are on: {mana / -rate / 3600:0.0} h left") : "no ManaRate on the appraisal";
        var prediction = before is { } p ? string.Create(CultureInfo.InvariantCulture, $"; predicted {p} from the read {since:0} s ago") : "; the first read";
        log.Info(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: read '{name}' 0x{guid:X8} {mana}/{max} ({100.0 * mana / max:0.#}%), {left}{prediction}"));
    }

    /// <summary>Reads a chat line: ACE's low and out lines for a worn item (Player_Tick.cs:662-699) are logged as alarms; "out" sets the prediction to 0.</summary>
    /// <param name="text">The line's text.</param>
    /// <param name="now">The session clock.</param>
    /// <returns>True when the line was one of the two.</returns>
    public bool OnChat(string? text, double now)
    {
        if (text is null || ManaLine().Match(text) is not { Success: true } m)
        {
            return false;
        }

        var name = m.Groups["name"].Value;
        var isOut = m.Groups["what"].Value == "out of";
        Alarms++;
        var guids = new List<uint>();
        foreach (var (guid, r) in _reads.ToArray())
        {
            if (string.Equals(r.Name, name, StringComparison.Ordinal) && isOut)
            {
                _reads[guid] = r with { Out = true };
                guids.Add(guid);
            }
            else if (string.Equals(r.Name, name, StringComparison.Ordinal) && r.Rate < 0 && r.MaxMana > 0)
            {
                _reread.Add(guid); // M6 item 14: "low" is ACE's own figure, under 120 s left (Player_Tick.cs:662-679): the item is read again at once, so the field refill rules on the server's number
            }
        }

        if (isOut)
        {
            Dropped.Mark(name, guids); // M6 2 b: the caster's "spells dropped" flag (CasterManaRule's SpellsDropped), cleared at its next wield
        }

        if (!isOut)
        {
            _fieldNextAt = now; // the field refill looks at once
        }

        log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: ALARM: the server says \"{text}\" at {now:0.0} s ({(isOut ? "its spells drop in 2 s (Player_Tick.cs:681-699)" : "under 120 s left (Player_Tick.cs:662-679)")}); the refill: {RefillPlan(isOut, now)}"));
        return true;
    }

    /// <summary>M6 item 14: what the refill does about an alarm, for its line: the field's use of a charged stone she holds, else the home visit's refill (and, for an item that ran out, its wearing again).</summary>
    private string RefillPlan(bool isOut, double now)
    {
        var home = string.Create(CultureInfo.InvariantCulture, $"the next home visit (expected in about {Memory.SecondsToNextVisit(now) / 60:0} min)");
        if (isOut)
        {
            return home + " refills it from a charged stone, a capture from loot or bought charges, and wears it again, which alone brings its spells back (Player_Inventory.cs:313-315)";
        }

        return _chargedHeld > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{_chargedHeld} charged stone(s) held: the field refill uses one now")
            : "no charged stone is held, so the field has nothing to use: " + home + " refills it from a capture from loot or bought charges";
    }

    /// <summary>
    /// One frame: takes a finished read, then asks for the next due one (a worn item never read, or read <see cref="ReadEverySeconds"/> ago and holding mana). One Identify in flight at a time.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    public void Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        FieldRefill(session, now);
        if (_pending is not null)
        {
            if (_pending.Outcome is null)
            {
                return;
            }

            if (session.State.Appraisals.TryGet(_pendingGuid, out var a) && session.State.Objects.TryGet(_pendingGuid, out var o) && !a.IsNotFound)
            {
                Read(_pendingGuid, o.Name, a, now);
            }

            _pending = null;
        }

        foreach (var e in session.State.Self.Inventory.Equipped)
        {
            var due = !_reads.TryGetValue(e.Guid, out var r) || _reread.Contains(e.Guid) || (r.MaxMana > 0 && now - r.At >= ReadEverySeconds);
            if (due)
            {
                _reread.Remove(e.Guid);
                _pendingGuid = e.Guid;
                _pending = session.Actions.Identify(e.Guid, ActionSource.Host);
                return;
            }
        }
    }

    /// <summary>
    /// M6 item 14: the field refill (see the class remarks): the outcome of a use in flight, else, every <see cref="FieldCheckSeconds"/>, the worn items that would be empty before she is next home and the charged stone used for them.
    /// </summary>
    private void FieldRefill(IGameSessionView session, double now)
    {
        if (_fieldUse is { } sent)
        {
            if (sent.Outcome is not { } outcome)
            {
                return;
            }

            _fieldUse = null;
            var stone = _fieldPick.Stone;
            if (outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                FieldRefills++;
                _fieldSpent[stone.Guid] = stone.Mana; // its appraisal is stale now: not used again until a new read shows another figure
                foreach (var guid in _fieldBefore.Keys)
                {
                    _reread.Add(guid);
                }

                log.Info(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: the stone '{stone.Name}' 0x{stone.Guid:X8}: Confirmed{(outcome.Detail is null ? string.Empty : " (" + outcome.Detail + ")")}; the worn items are read again for the gain"));
                if (session.State.Self.Inventory.Contains(stone.Guid))
                {
                    session.Actions.Identify(stone.Guid, ActionSource.Host); // the stone's own mana, read again (a stone that outlived its destroy roll is empty)
                }
            }
            else
            {
                _fieldBefore.Clear();
                var tries = _fieldTries[stone.Guid] = _fieldTries.GetValueOrDefault(stone.Guid) + 1;
                _fieldNextAt = now + FieldRetrySeconds;
                log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: the stone '{stone.Name}' 0x{stone.Guid:X8} was not used: {outcome.Kind}{(outcome.Code == 0 ? string.Empty : $" (code 0x{outcome.Code:X})")}{(outcome.Detail is null ? string.Empty : ": " + outcome.Detail)} (try {tries} of {FieldTriesAStone}: {(tries >= FieldTriesAStone ? "this stone is left to the home visit" : $"again in {FieldRetrySeconds:0} s")})"));
            }

            return;
        }

        if (now < _fieldNextAt)
        {
            return;
        }

        _fieldNextAt = now + FieldCheckSeconds;
        var state = session.State;
        if (state.Self.Guid == 0 || !state.Self.Inventory.IsKnown)
        {
            return;
        }

        var stones = WornManaRefill.StonesOf(state).Where(s => s.Charged && _fieldTries.GetValueOrDefault(s.Guid) < FieldTriesAStone && !(_fieldSpent.TryGetValue(s.Guid, out var spent) && spent == s.Mana)).ToList();
        _chargedHeld = stones.Count;
        if (Memory.HomeStepActive(now))
        {
            return; // the home step owns the stones while it works
        }

        var worn = new List<WornMana>();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (_reads.TryGetValue(e.Guid, out var r) && r.MaxMana > 0 && Predicted(e.Guid, now) is { } mana)
            {
                worn.Add(new WornMana(e.Guid, r.Name, mana, r.MaxMana, r.Rate));
            }
        }

        var next = Memory.SecondsToNextVisit(now);
        var due = WornManaRefill.Due(worn, next, 0).Where(d => !d.Out && !_silent.Contains(d.Item.Guid)).ToList(); // an item that is out, or whose spells are off, is the home visit's: mana alone does not bring its spells back
        if (due.Count == 0)
        {
            return;
        }

        if (stones.Count == 0)
        {
            foreach (var d in due)
            {
                if (_fieldSaid.Add(d.Item.Guid))
                {
                    log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: {d.Why} (the field's rule: empty before she is next home), and no charged stone is held: nothing to use in the field; the next home visit refills it (named: field-refill-no-stone)"));
                }
            }

            return;
        }

        // A stone goes on the player; only a Mana Charge (always full: its one use destroys it) may go on the due item itself. An empty stone used on a worn item would drain and destroy it (ManaStone.cs:84-113).
        var pick = WornManaRefill.PickUse(due, worn, stones, next, 0);
        if (pick.Target != 0 && !CasterManaRule.ChargeMana.ContainsKey(state.Objects.TryGet(pick.Stone.Guid, out var o) ? o.Weenie.WeenieClassId : 0))
        {
            var gains = WornManaRefill.PourOnPlayer(pick.Stone.Mana ?? 0, worn, out var left);
            pick = new ManaUse(pick.Stone, 0, "the player", gains, left, string.Create(CultureInfo.InvariantCulture, $"'{pick.Stone.Name}' 0x{pick.Stone.Guid:X8} ({pick.Stone.Mana}) on the player (in the field a stone never goes on one worn item: an empty stone would destroy it); ACE's even ration gives '{due[0].Item.Name}' {gains.GetValueOrDefault(due[0].Item.Guid)}"));
        }

        _fieldBefore.Clear();
        foreach (var guid in pick.Predicted.Keys)
        {
            _fieldBefore[guid] = worn.First(w => w.Guid == guid).Mana;
        }

        _fieldPick = pick;
        log.Info(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: field refill: check before: {due[0].Why} (the field's rule: empty before she is next home, in about {next / 60:0} min); use {pick.Why}"));
        _fieldUse = session.Actions.UseManaStone(pick.Stone.Guid, pick.Target == 0 ? state.Self.Guid : pick.Target, ActionSource.Automation);
    }

    /// <summary>M6 2 b: where the "out of Mana" lines mark an item's spells dropped (<see cref="CasterSpellsDropped.Shared"/> unless a test gives its own).</summary>
    public CasterSpellsDropped Dropped { get; init; } = CasterSpellsDropped.Shared;

    [GeneratedRegex(@"^Your (?<name>.+) is (?<what>low on|out of) Mana\.$", RegexOptions.CultureInvariant)]
    private static partial Regex ManaLine();
}

/// <summary>
/// M6 2 b (the mage half of <c>m4-gear-mana-reactivate</c>, slice c's <see cref="CasterManaInput.SpellsDropped"/>): the items whose spells ACE removed when they ran dry. Set from <see cref="WornManaWatch.OnChat"/>'s "Your X is out of
/// Mana." line (ACE's <c>HandleManaDepleted</c>, Player_Tick.cs:681-699 @95ef426d: the spells go at 0 and only a wield brings them back, Player_Inventory.cs:313-315); cleared at the item's next wield (<see cref="Clear"/>, called by
/// the resupply's wields). The chat names the item, not its guid: the guids the watch had read under that name are marked, and the name itself, so a caster never read is matched by name (two casters of one name are both marked
/// until one is wielded: the safe side, a rewield too many). Shared by the run's scenarios as <see cref="PackMemory.Shared"/> is: the watch lives in the autopilot, the wield in the resupply.
/// </summary>
public sealed class CasterSpellsDropped
{
    private readonly HashSet<uint> _guids = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>The run's one memory.</summary>
    public static CasterSpellsDropped Shared { get; } = new();

    /// <summary>Marks the item <paramref name="name"/> (and the <paramref name="guids"/> read under it) as out of mana, its spells dropped.</summary>
    /// <param name="name">The name in the chat line.</param>
    /// <param name="guids">The guids read under that name.</param>
    public void Mark(string name, IEnumerable<uint> guids)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(guids);
        lock (_guids)
        {
            _names.Add(name);
            foreach (var g in guids)
            {
                _guids.Add(g);
            }
        }
    }

    /// <summary>True when <paramref name="guid"/> (or an item of <paramref name="name"/>) was said out of mana and has not been wielded since.</summary>
    /// <param name="guid">The item.</param>
    /// <param name="name">Its name.</param>
    public bool IsDropped(uint guid, string name)
    {
        lock (_guids)
        {
            return _guids.Contains(guid) || (name is not null && _names.Contains(name));
        }
    }

    /// <summary>The item was wielded: its spells come back with its mana (Player_Inventory.cs:313-315), so the flag clears for it and its name.</summary>
    /// <param name="guid">The item wielded.</param>
    /// <param name="name">Its name.</param>
    public void Clear(uint guid, string name)
    {
        lock (_guids)
        {
            _guids.Remove(guid);
            if (name is not null)
            {
                _names.Remove(name);
            }
        }
    }

    /// <summary>Forgets every mark (a test's fresh start).</summary>
    public void Reset()
    {
        lock (_guids)
        {
            _guids.Clear();
            _names.Clear();
        }
    }
}
