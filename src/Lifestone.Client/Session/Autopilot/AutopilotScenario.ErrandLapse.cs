using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 errand lapse (live ladder57, 2026-11-17 UTC). The buff bot cast the 28 lines of <c>all</c> at 09:40:39, each for an hour; the start quest's Atlan wand ran its hunt-until (16 Pyreal Motes in the Crater Caves, about three
/// hours) from 09:52:39 as a sub-run of the quest station, in the Trip state. The autopilot's fight triggers (<see cref="WatchFight"/>: the upkeep's ask that arms the fight's own refresh, the bot's trip, the lapsed line's trip)
/// run only in the Fight state, so nobody asked that fight to keep her lines: from 10:40:43 every boon ran out (War Magic 378 to 326, Executor's Boon, Boon of the Mace Turner) and the race read lost at 10:42:05.
/// <para>The owner, 2026-11-17: she relies on her own buffs for this; she does not need the bot. A station that steps a fight inside it shows it (<see cref="IStationSubFight"/>), and the autopilot asks it to keep her lines as
/// it asks its own fight, each act with a check before and a check after: at the fight's start, when the bot's lines have under the fight's expected length left, the fight keeps her lines from its first frame; a line under
/// the re-buff margin asks the same. The fight's refresh then casts, from her spell bar, between kills, the highest level of each line her book holds and her live skill lands (its one-line pass's level choice), each as it
/// nears its lapse and spread by the lapse times (its batching); a line the bot stands over is covered by her own cast under it, which takes over when the bot's ends (<see cref="BuffLapseRule"/>); each renewal is read after,
/// and one that replaces the bot's says so. The errand's fight never leaves for the bot's trip. A line of the bot's that nothing in her book covers is warned by name once a lapse (<see cref="BuffLapseRule.WarnToken"/>), and
/// the fight goes on.</para>
/// </summary>
public sealed partial class AutopilotScenario
{
    private IStoppableFight? _errandFight; // the errand's fight last watched (a new one is named once and read at its start)
    private double _errandUpkeepAt = double.NegativeInfinity;
    private double _errandBotReadAt = double.NegativeInfinity;
    private readonly List<BuffCycle.LineState> _errandLines = [];
    private readonly List<BuffBotLineRead> _errandBotReads = [];
    private readonly Dictionary<string, double> _errandLeft = new(StringComparer.Ordinal); // her lines' seconds left at the last read in the errand's fight (the check after)
    private readonly HashSet<uint> _errandUncoveredWarned = []; // the bot's spells warned uncovered in this lapse

    /// <summary>M6 14 errand lapse: the renewals of her lines read in errand fights this run (each a check after).</summary>
    public int ErrandRenewals { get; private set; }

    /// <summary>M6 14 errand lapse: the bot's lines warned with nothing of hers to cover them this run.</summary>
    public int ErrandUncoveredWarnings { get; private set; }

    /// <summary>One frame over the errand's fight (the Trip state running the quest station; <see cref="RunStep"/> calls it before the survival layer). Nothing while no fight runs inside the station or once its fight is stopping.</summary>
    private void WatchErrandFight(IGameSessionView session)
    {
        if (!_questTripActive || _run is not IStationSubFight station || station.SubFight is not { } fight)
        {
            _errandFight = null;
            return;
        }

        if (fight.StopReason is not null)
        {
            return;
        }

        if (!ReferenceEquals(fight, _errandFight))
        {
            _errandFight = fight;
            _errandLeft.Clear();
            _errandUncoveredWarned.Clear();
            (_errandUpkeepAt, _errandBotReadAt) = (double.NegativeInfinity, double.NegativeInfinity);
            ErrandFightStart(session, fight);
        }

        ErrandFightKeepsLines(session, fight);
        ErrandUncoveredLines(session);
    }

    /// <summary>
    /// The fight's start: check before, the bot's lines on her split by what her book covers; when the least of them has under the fight's expected length left (<see cref="QuestStation.FightExpectedSeconds"/>), act: the fight keeps
    /// her lines from its first frame (asked as the upkeep asks), so each is renewed by her own cast as it nears its lapse and they are not all due in one moment when the bot's hour ends.
    /// </summary>
    private void ErrandFightStart(IGameSessionView session, IStoppableFight fight)
    {
        BuffBotRule.Read(BotProfile, session.State.Self.Qualities, session.Clock.Now, _errandBotReads);
        var self = session.State.Self;
        var split = BuffLapseRule.Of(_errandBotReads, line => BuffLapseRule.BookCasts(self, line));
        var up = _errandBotReads.Count(r => r.Up);
        var expected = QuestStation.FightExpectedSeconds;
        var keep = BuffLapseRule.KeepFromStart(_errandBotReads, expected);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={_questKey}: the errand's fight keeps her lines as every fight does, with no trip for the bot (the owner, 2026-11-17: she relies on her own buffs): check before: {up} of the {_errandBotReads.Count} lines of '{BotProfile.Key}' stand on her; {split.Text} (m6-errand-lapse)"));
        if (!keep || fight is not IBuffRefreshFight refresh || refresh.BuffRefreshAsked || refresh.BuffsKeptAhead)
        {
            return;
        }

        _buffOptions ??= _options.Buff ?? new BuffOptions();
        var least = _errandBotReads.Where(r => r.Up).Min(r => r.RemainingSeconds);
        var detail = string.Create(CultureInfo.InvariantCulture, $"the bot's lines end in {least:0} s, inside the fight's expected {expected:0} s");
        _log.Info(LogCategory, $"autopilot quest: key={_questKey}: act: {detail}: the fight keeps her lines from its start: each is renewed by her own cast under the bot's as it nears its lapse, spread by the lapse times (the refresh's batching), from her spell bar, between kills (m6-errand-lapse)");
        refresh.RequestBuffRefresh(_buffOptions.RebuffMarginSeconds, _options.AggroReachMetres, detail);
    }

    /// <summary>
    /// Every <see cref="UpkeepCheckSeconds"/>, her lines in the errand's fight. Check before: a line under the re-buff margin asks the fight to keep the lines itself, as the upkeep asks every fight (F23-F25), once: the fight then keeps
    /// them. Check after, each read: a line whose seconds left rose was renewed (and replaces the bot's when the bot's line of that family stands on her); one that fell to nothing lapsed (a warning: the fight's own expiry line says why).
    /// </summary>
    private void ErrandFightKeepsLines(IGameSessionView session, IStoppableFight fight)
    {
        if (fight is not IBuffRefreshFight refresh || _clock - _errandUpkeepAt < UpkeepCheckSeconds)
        {
            return;
        }

        _errandUpkeepAt = _clock;
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        var cycle = BuffReads(session, inFight: true);
        if (cycle.SeedPending)
        {
            return; // the armour memory is not seeded yet: nothing is counted due (as the upkeep waits)
        }

        cycle.ReadLines(_clock, _errandLines);
        var margin = _buffOptions.RebuffMarginSeconds;
        List<string>? due = null;
        foreach (var line in _errandLines)
        {
            var name = line.Line.Name;
            var left = line.Active ? line.Remaining : 0;
            if (_errandLeft.TryGetValue(name, out var before))
            {
                if (left > before + UpkeepCheckSeconds)
                {
                    ErrandRenewals++;
                    var bot = BotProfile.Lines.FirstOrDefault(l => l.Line == name);
                    var replaces = bot is not null && session.State.Self.Qualities.ActiveEnchantments.Any(e => e.SpellId == bot.SpellId)
                        ? $"; her own cast replaces the bot's {bot.Spell} (level VII) when it ends"
                        : string.Empty;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={_questKey}: check after: {name} renewed in the errand's fight by her own cast: {before:0} -> {left:0} s left{replaces} (m6-errand-lapse)"));
                }
                else if (before > 0 && !line.Active)
                {
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={_questKey}: check after: {name} lapsed in the errand's fight ({before:0} s left at the read before): the fight's refresh did not renew it in time; its expiry line says why (m6-errand-lapse)"));
                }
            }

            _errandLeft[name] = left;
            if (left < margin)
            {
                (due ??= []).Add(string.Create(CultureInfo.InvariantCulture, $"{name} {left:0} s"));
            }
        }

        if (due is null || refresh.BuffRefreshAsked || refresh.BuffsKeptAhead)
        {
            return;
        }

        var detail = string.Create(CultureInfo.InvariantCulture, $"{string.Join(';', due)} under the {margin:0.#} s margin");
        _log.Info(LogCategory, $"autopilot quest: key={_questKey}: check before: a buff line is due in the errand's fight: {detail}: act: the fight keeps the lines itself as every fight does (her book's highest level her skill lands, under the bot's where it stands, from her spell bar: a lapsed line or one under the hard floor between its casts, the next due one between kills, F23 F24); the errand is not stopped for it (m6-errand-lapse)");
        refresh.RequestBuffRefresh(margin, _options.AggroReachMetres, detail);
    }

    /// <summary>
    /// Every <see cref="BotTripReadSeconds"/>: a line of the bot's on her that nothing in her book covers (no level of its family's line, or its school untrained) with under the re-buff margin left is warned by name, once a lapse
    /// (again when its line reads fresh), and the fight goes on: there is nothing of hers to cast for it, and the errand does not leave for the bot.
    /// </summary>
    private void ErrandUncoveredLines(IGameSessionView session)
    {
        if (_clock < _errandBotReadAt)
        {
            return;
        }

        _errandBotReadAt = _clock + BotTripReadSeconds;
        _buffOptions ??= _options.Buff ?? new BuffOptions();
        var margin = _buffOptions.RebuffMarginSeconds;
        BuffBotRule.Read(BotProfile, session.State.Self.Qualities, session.Clock.Now, _errandBotReads);
        var self = session.State.Self;
        foreach (var read in _errandBotReads)
        {
            if (!read.Up || read.RemainingSeconds >= margin)
            {
                _errandUncoveredWarned.Remove(read.Line.SpellId); // not on her, or fresh: a later lapse is warned again
                continue;
            }

            if (BuffLapseRule.BookCasts(self, read.Line.Line) || !_errandUncoveredWarned.Add(read.Line.SpellId))
            {
                continue; // her own line covers it (the refresh's), or it was said for this lapse
            }

            ErrandUncoveredWarnings++;
            var profileLine = BuffProfiles.Climber.LineNamed(read.Line.Line);
            var why = profileLine is null ? $"{read.Line.Line} is no line of her own profile" : $"her book holds no level of {read.Line.Line} her trained skill can cast";
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot quest: key={_questKey}: the bot's {read.Line.Spell} ({read.Line.Line}) has {read.RemainingSeconds:0} s left in the errand's fight and nothing of hers covers it: {why}; no trip is taken for the bot: the fight goes on and the line lapses ({BuffLapseRule.WarnToken})"));
        }
    }
}
