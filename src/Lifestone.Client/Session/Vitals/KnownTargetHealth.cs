using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 walk drains (the owner, 2026-11-15: the target's health and kind are known data; live ladder23 02:51:43 UTC: the walk's heal pick said "no drain offered ... no target of known health in reach" while Reedshark Ravagers hit her): the health of
/// the walk's fight-back target, as data. Its maximum is the creature's facts (<see cref="CreatureFacts.MaxHealthOf"/>: the ground profiles first, then the world database's table), less what she has dealt it since it became the target:
/// her damage lines ("You scorch Reedshark Ravager for 82 points with Ilservian's Flame.") and her boosts' drains ("With Harm Other VI you drain 40 points of health from Reedshark Ravager."), the System lines of the Magic type naming it
/// (<see cref="FightTally.Classify"/>), attributed to the fight-back's current target, and the takes of her Drain Health Other casts at it (<see cref="NoteTake"/>: the vitals guard, whose casts they are, notes each from ACE's gain line). The
/// facts carry no health regeneration, so none is counted (said once in <see cref="Words"/>). Read on demand (the travel vitals guard's heal pick reads it, <c>AutopilotVitalsGuard</c>), so there is one picture of the target.
/// <para>M6 14 the losing race (live 2026-11-15 04:12:43 to 04:13:11 UTC: the guard's picks read the Assailer at "target health 571" for 28 s, the server's fraction 0.75 of 760 sent once, while her bolts burnt it 192 and her drains
/// took from it; the drains then gave 8 to 12 a cast against the 11 to 23 a second the pick predicted): the server's report is the base it arrived at, not the answer. A report the reader has not seen before re-bases it (what she dealt
/// before the report is in it), and what she deals after it is taken off it, so a report the server does not refresh grows no staler than her own lines.</para>
/// <para>M6 16 Q5 (<c>TODO(engine-health-flip)</c>): the vitals guard and the travel legs read the health number from the session's one picture (<see cref="PictureHealth"/>); a tracking carries the facts (its maximum, Magic
/// Defense, name and guid) and answers only where the picture holds no maximum for the creature.</para>
/// </summary>
public sealed class KnownTargetHealth
{
    private long _scanned;
    private float? _report;
    private double _dealtAtReport;

    /// <summary>The tracking of <paramref name="guid"/>, a <paramref name="wcid"/> of <paramref name="maxHealth"/>, from the chat's <paramref name="chatMark"/> on.</summary>
    /// <param name="guid">The target's guid.</param>
    /// <param name="wcid">Its weenie class.</param>
    /// <param name="name">Its name (the lines name it).</param>
    /// <param name="maxHealth">Its maximum health (the facts).</param>
    /// <param name="magicDefence">Its Magic Defense (the facts).</param>
    /// <param name="source">Where the facts come from, in words.</param>
    /// <param name="chatMark">The chat's <c>Total</c> when it became the target.</param>
    public KnownTargetHealth(uint guid, uint wcid, string name, int maxHealth, int magicDefence, string source, long chatMark)
    {
        (Guid, Wcid, Name, MaxHealth, MagicDefence, Source, _scanned) = (guid, wcid, name, maxHealth, magicDefence, source, chatMark);
    }

    /// <summary>The target's guid.</summary>
    public uint Guid { get; }

    /// <summary>Its weenie class.</summary>
    public uint Wcid { get; }

    /// <summary>Its name.</summary>
    public string Name { get; }

    /// <summary>Its maximum health (the facts).</summary>
    public int MaxHealth { get; }

    /// <summary>Its Magic Defense (the facts).</summary>
    public int MagicDefence { get; }

    /// <summary>Where the facts come from.</summary>
    public string Source { get; }

    /// <summary>The damage, the boosts' drains and the drain takes she has dealt it since it became the target (or was first tracked).</summary>
    public double Dealt { get; private set; }

    /// <summary>The lines and takes counted.</summary>
    public int Lines { get; private set; }

    /// <summary>
    /// M6 14 the losing race: true while the lines naming it count for it: the fight-back's current target (its bolts are at it). A creature tracked by the vitals guard that is not the target counts only the takes noted for it
    /// (<see cref="NoteTake"/>) and the server's reports: a line naming its kind may be another's of the same name.
    /// </summary>
    public bool CountsLines { get; set; } = true;

    /// <summary>
    /// Its health now: the server's last report as the base it arrived at (the creature's health fraction of its maximum), less what she dealt it since; with no report, the maximum less what she dealt it (at least 1 while it stands: a
    /// creature at 0 is dead and gone). Reads the chat lines since the last read (allocation-free).
    /// </summary>
    /// <param name="state">The game state.</param>
    public uint Health(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (CountsLines)
        {
            Scan(state.Chat);
        }
        else
        {
            _scanned = state.Chat.Total;
        }

        if (state.Objects.TryGet(Guid, out var o) && o.Health is { } fraction and > 0f)
        {
            if (_report != fraction)
            {
                (_report, _dealtAtReport) = (fraction, Dealt); // a report not seen before: what she dealt before it is in it
            }

            return (uint)Math.Max(1, Math.Round((fraction * MaxHealth) - (Dealt - _dealtAtReport)));
        }

        if (_report is { } last)
        {
            return (uint)Math.Max(1, Math.Round((last * MaxHealth) - (Dealt - _dealtAtReport))); // M6 14 the killing blow: a creature reported dead or gone is read from its last living report, not its full maximum
        }

        return (uint)Math.Max(1, Math.Round(MaxHealth - Dealt));
    }

    /// <summary>What she dealt it since the server's last report was first read (all she dealt with no report).</summary>
    public double DealtSinceReport => _report is null ? Dealt : Dealt - _dealtAtReport;

    /// <summary>M6 16 E2d: true once a report of the server was read for it (the health shadow states it; no decision reads it).</summary>
    public bool HasReport => _report is not null;

    /// <summary>
    /// M6 14 the losing race: one of her Drain Health Other casts at it took <paramref name="take"/> (ACE's gain line names only her gain; the caller turns it back into the target's loss, the gain over 1 minus the spell's LossPercent, cut to
    /// its TransferCap: <c>WorldObject_Magic.cs:721-747</c> @95ef426d).
    /// </summary>
    /// <param name="take">The health the drain took.</param>
    public void NoteTake(double take)
    {
        if (take > 0)
        {
            Dealt += take;
            Lines++;
        }
    }

    /// <summary>
    /// M6 14 the losing race: a fresh tracking of the same creature carries on from <paramref name="earlier"/> (a fight-back that takes a creature for its target again, or the vitals guard's own tracking of it): with no report on either,
    /// all the earlier dealt; on the same report (or none read here yet), the earlier's report and what it dealt since; a newer report read here holds what was dealt before it, and nothing is carried.
    /// </summary>
    /// <param name="earlier">The earlier tracking of the same guid.</param>
    public void CarryFrom(KnownTargetHealth earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        if (earlier.Guid != Guid || ReferenceEquals(earlier, this))
        {
            return;
        }

        if (earlier._report is not { } report)
        {
            (Dealt, Lines) = (Dealt + earlier.Dealt, Lines + earlier.Lines);
            return;
        }

        if (_report is null)
        {
            (_report, _dealtAtReport) = (report, Dealt);
        }

        if (_report == report)
        {
            (Dealt, Lines) = (Dealt + earlier.DealtSinceReport, Lines + earlier.Lines);
        }
    }

    /// <summary>The health in words, with its numbers and source.</summary>
    /// <param name="state">The game state.</param>
    public string Words(GameState state)
    {
        var health = Health(state);
        var reported = state.Objects.TryGet(Guid, out var o) && o.Health is > 0f;
        var how = reported
            ? string.Create(CultureInfo.InvariantCulture, $"the server's report of {(_report ?? 0f) * MaxHealth:0} less the {DealtSinceReport:0} she dealt it since")
            : string.Create(CultureInfo.InvariantCulture, $"less the {Dealt:0} she dealt it in {Lines} line(s)");
        return string.Create(CultureInfo.InvariantCulture, $"'{Name}' 0x{Guid:X8} health {health} of {MaxHealth} ({Source}; {how}; the facts carry no health regeneration: none counted, TODO(m6-creature-regen))");
    }

    private void Scan(ChatLog chat)
    {
        var fresh = (int)Math.Min(chat.Count, chat.Total - _scanned);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind != ChatKind.System || line.ChatType != FightTally.MagicChatType || !line.Text.Contains(Name, StringComparison.Ordinal))
            {
                continue;
            }

            var kind = FightTally.Classify(line.Text, out var amount, out _);
            if (kind == MagicLineKind.DamageDealt || (kind == MagicLineKind.DrainDealt && line.Text.Contains(" points of health from ", StringComparison.Ordinal)))
            {
                Dealt += amount;
                Lines++;
            }
        }

        _scanned = chat.Total;
    }
}
