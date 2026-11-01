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

    private readonly Dictionary<uint, Reading> _reads = [];
    private IdentifyHandle? _pending;
    private uint _pendingGuid;

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
        var since = _reads.TryGetValue(guid, out var last) ? now - last.At : double.NaN;
        var rate = appraisal.Float(5) ?? 0.0;
        var max = appraisal.ItemMaxMana.Value;
        _reads[guid] = new Reading(name, mana, max, rate, now, false);
        ReadCount++;
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
        foreach (var (guid, r) in _reads.ToArray())
        {
            if (string.Equals(r.Name, name, StringComparison.Ordinal) && isOut)
            {
                _reads[guid] = r with { Out = true };
            }
        }

        log.Warn(category, string.Create(CultureInfo.InvariantCulture, $"gear mana: ALARM: the server says \"{text}\" at {now:0.0} s ({(isOut ? "its spells drop in 2 s (Player_Tick.cs:681-699)" : "under 120 s left (Player_Tick.cs:662-679)")}); the refill is the home visit's (TODO(m5-stone-field-refill))"));
        return true;
    }

    /// <summary>
    /// One frame: takes a finished read, then asks for the next due one (a worn item never read, or read <see cref="ReadEverySeconds"/> ago and holding mana). One Identify in flight at a time.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock.</param>
    public void Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
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
            var due = !_reads.TryGetValue(e.Guid, out var r) || (r.MaxMana > 0 && now - r.At >= ReadEverySeconds);
            if (due)
            {
                _pendingGuid = e.Guid;
                _pending = session.Actions.Identify(e.Guid, ActionSource.Host);
                return;
            }
        }
    }

    [GeneratedRegex(@"^Your (?<name>.+) is (?<what>low on|out of) Mana\.$", RegexOptions.CultureInvariant)]
    private static partial Regex ManaLine();
}
