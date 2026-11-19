using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 sprint (the owner's rule of 2026-11-19: a scroll that drops is picked up and read when she does not know it): between fights (nobody in reach, no corpse waiting), a scroll in her packs whose spell the book lacks and ACE lets her
/// read now (<c>ResupplyRun.CanRead</c>, ACE's <c>CanReadScroll</c>) is read on the spot, as a player reads a find, not carried to the next trip. The loot keeps
/// such a scroll (<see cref="ScrollKeepRule"/> with the SpellTable). Check before (unknown, readable, not refused in this fight), act (the Use), check after (the book holds the spell; else ACE's answer names the refusal, which is not
/// tried again in this fight; "You're too busy!" is lag: re-sent after a short pause, as the trip's read does). The trip's own read stays the backstop (<c>ResupplyStation.PlanHeldScrolls</c>).
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The wait for ACE's answer to a read (the book's update or its chat line) before it is given up (Lifestone's own).</summary>
    private const double ScrollReadSeconds = 10;

    /// <summary>The pause before a re-send after "You're too busy!" (the last pick-up's or cast's chain still running on the server; owner ruling 2026-11-19: re-send, no long wait; Lifestone's own).</summary>
    private const double ScrollBusyPauseSeconds = ResupplyRun.BusyPauseSeconds;

    private readonly List<uint> _readScratch = [];
    private readonly HashSet<uint> _readRefused = [];
    private ActionHandle? _readUse;
    private uint _readScroll;
    private uint _readSpell;
    private string _readName = string.Empty;
    private double _readSince = double.NaN;
    private double _readRetryAt = double.NaN;
    private long _readMark;
    private int _readBusyResends;
    private double _readNextScan = double.NegativeInfinity;

    /// <summary>How often the packs are scanned for a scroll to read while none is found (Lifestone's own: a find arrives with a corpse's loot, so a second is soon enough).</summary>
    private const double ScrollScanSeconds = 1;

    /// <summary>M6 sprint: the scrolls this fight read on the spot (<c>m6-read-looted-scroll</c>).</summary>
    public int ScrollsRead { get; private set; }

    /// <summary>One step of the read between fights: Running while a read is under way, else null (nothing to read: the tour walks on).</summary>
    private ScenarioStatus? ReadStep(IGameSessionView session, double clock)
    {
        if (_readUse is { } use)
        {
            return ReadPending(session, clock, use);
        }

        if (!double.IsNaN(_readRetryAt))
        {
            if (clock < _readRetryAt)
            {
                return ScenarioStatus.Running;
            }

            _readRetryAt = double.NaN;
            return BeginRead(session, clock, string.Create(CultureInfo.InvariantCulture, $"re-send {_readBusyResends} of {ResupplyRun.BusyResends} after \"You're too busy!\""));
        }

        var state = session.State;
        var book = state.Self.Spellbook;
        if (!_options.Loot || !book.IsKnown || clock < _readNextScan)
        {
            return null;
        }

        _readNextScan = clock + ScrollScanSeconds;
        GearInventory.Collect(state, _readScratch);
        foreach (var guid in _readScratch)
        {
            if (!state.Objects.TryGet(guid, out var item) || (item.ItemType & ResupplyRun.WritableType) == 0 || item.Weenie.SpellId is not { } id || _readRefused.Contains(id) || book.Knows(id) || !_spells.TryGetValue(id, out var info))
            {
                continue;
            }

            if (ResupplyRun.CanRead(state.Self.Qualities, info.School, info.Power, out var why) != LearnReason.None)
            {
                continue; // ACE would refuse it now: the trip reads it once she can, or the loot rules sell it
            }

            (_readScroll, _readSpell, _readName, _readBusyResends) = (guid, id, info.Name, 0);
            return BeginRead(session, clock, string.Create(CultureInfo.InvariantCulture, $"not in her book, and ACE lets her read it ({why})"));
        }

        return null;
    }

    private ScenarioStatus BeginRead(IGameSessionView session, double clock, string why)
    {
        _readMark = session.State.Chat.Total;
        _readSince = clock;
        _readUse = session.Actions.Use(_readScroll, ActionSource.Automation);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"read: the scroll 0x{_readScroll:X8} of {_readName} ({_readSpell}): {why}: read now, between fights (m6-read-looted-scroll)"));
        return ScenarioStatus.Running;
    }

    private ScenarioStatus? ReadPending(IGameSessionView session, double clock, ActionHandle use)
    {
        if (session.State.Self.Spellbook.Knows(_readSpell))
        {
            session.Actions.PlaceOnBar(_readSpell, "on its learning (m6-learn-to-bar)"); // M6 sprint: a spell she learns goes on her bar at once, as a player puts it there
            AskMagicStance(session, clock, "the read put her in peace: ACE's Scroll.ActOnUse sets NonCombat before the read"); // M6 sprint (run 6): the fight's stance back at once
            ScrollsRead++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"read: learned {_readName} ({_readSpell}): the spellbook holds it ({clock - _readSince:0.0} s after the use); scrolls read on the spot in this fight {ScrollsRead} (m6-read-looted-scroll)"));
            _readUse = null;
            return null;
        }

        var answer = ReadAnswer(session.State.Chat, out var busy);
        if (busy && _readBusyResends < ResupplyRun.BusyResends)
        {
            _readBusyResends++;
            _readUse = null;
            _readRetryAt = clock + ScrollBusyPauseSeconds;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"read: ACE answered \"You're too busy!\" to the use of {_readName} ({_readSpell}): the last pick-up or cast is still finishing on the server; the use goes again in {ScrollBusyPauseSeconds:0.#} s (re-send {_readBusyResends} of {ResupplyRun.BusyResends})"));
            return ScenarioStatus.Running;
        }

        var refused = use.Outcome is { Kind: not ActionOutcomeKind.Confirmed } outcome ? "the use ended " + outcome.Kind + (outcome.Detail is { } d ? ": " + d : string.Empty)
            : answer is not null ? "ACE said: " + answer
            : busy ? string.Create(CultureInfo.InvariantCulture, $"ACE answered \"You're too busy!\" {ResupplyRun.BusyResends + 1} times")
            : clock - _readSince > ScrollReadSeconds ? string.Create(CultureInfo.InvariantCulture, $"the book did not hold it within {ScrollReadSeconds:0} s of the use")
            : null;
        if (refused is null)
        {
            return ScenarioStatus.Running;
        }

        _readRefused.Add(_readSpell);
        _readUse = null;
        AskMagicStance(session, clock, "after a refused read: ACE's Scroll.ActOnUse may have set NonCombat"); // M6 sprint (run 6)
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"read: {_readName} ({_readSpell}) was not learned: {refused}; not tried again in this fight (the trip's read is the backstop) (m6-read-looted-scroll)"));
        return null;
    }

    /// <summary>ACE's refusal of the read in the chat since the use (its lines name the reason: not skilled enough, not trained, already known), or null; <paramref name="busy"/> when "You're too busy!" (0x001D) came.</summary>
    private string? ReadAnswer(ChatLog chat, out bool busy)
    {
        busy = false;
        var fresh = (int)Math.Min(chat.Count, chat.Total - _readMark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line is { Kind: ChatKind.WeenieError, ChatType: WeenieErrorText.YoureTooBusy })
            {
                busy = true;
                continue;
            }

            if (line.Text is { Length: > 0 } text && (text.Contains("not skilled enough", StringComparison.Ordinal) || text.Contains("not trained", StringComparison.Ordinal) || text.Contains("already know", StringComparison.Ordinal)))
            {
                return text;
            }
        }

        return null;
    }
}
