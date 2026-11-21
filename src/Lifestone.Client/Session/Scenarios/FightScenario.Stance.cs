using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 sprint (run 6, 2026-11-19 21:14:45 to 21:19 UTC): an on-the-spot scroll read (<see cref="ReadStep"/>) left her in peace: ACE puts a reader in NonCombat before the read (<c>Scroll.ActOnUse</c>, Scroll.cs 66-72 @95ef426d), and
/// nothing asked the magic stance again. Every bolt after it was refused by the stance guard (<see cref="ActionReason.NotInMagicStance"/>) and the fight decided again in the next frame: 5,016 refusals in four minutes, about 80 a
/// second, while a Drudge Skulker hit her. Now one rule owns the stance in a fight: a read's end, and a cast refused for the stance, ask the magic stance (check before: not in it already; act: <c>SetCombatMode</c> Magic; check after:
/// the mode the server answered); while that ask is in flight, and for <see cref="StanceGapSeconds"/> after it, the fight casts nothing (<see cref="StanceHold"/>), so a stance that does not land is asked again at that pace, never every frame.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The longest wait for the stance's answer before the hold lets the fight go on (Lifestone's own: ACE answers a mode change in tens of milliseconds live).</summary>
    private const double StanceAskSeconds = 5;

    /// <summary>The least time between two asks of the stance (Lifestone's own): the bound on how often a stance that does not land is asked.</summary>
    private const double StanceGapSeconds = 2;

    private ActionHandle? _stanceAsk;
    private double _stanceAskAt = double.NegativeInfinity;

    /// <summary>The stance asks this fight made (a read's end, or a cast refused for the stance).</summary>
    public int StanceAsks { get; private set; }

    /// <summary>Asks the magic stance when she is not in it and no ask is in flight; <paramref name="why"/> names the cause in the line.</summary>
    private void AskMagicStance(IGameSessionView session, double clock, string why)
    {
        var mode = session.State.Self.Qualities.CombatMode;
        if (_stanceAsk is not null || mode == CombatModes.Magic || clock - _stanceAskAt < StanceGapSeconds)
        {
            return;
        }

        StanceAsks++;
        _stanceAskAt = clock;
        _stanceAsk = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"stance: check before: the mode is {mode}, not magic ({why}); act: the magic stance is asked (ask {StanceAsks} of this fight); the casts wait for its answer (m6-stance-after-read)"));
    }

    /// <summary>
    /// True while a stance ask is in flight, or under <see cref="StanceGapSeconds"/> since it: the fight casts nothing. The ask's end is logged once (check after), when the fight next chooses a cast: run 6's first
    /// line read "15.5 s after the ask" for an answer the server gave in 0.8 s (2026-11-19 21:54:19 to 21:54:34 UTC, a 17 s walk between), so the line says what its number measures.
    /// </summary>
    private bool StanceHold(IGameSessionView session, double clock)
    {
        if (_stanceAsk is { } ask)
        {
            if (ask.Outcome is null && clock - _stanceAskAt < StanceAskSeconds)
            {
                return true;
            }

            _stanceAsk = null;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"stance: check after: {(ask.Outcome is { } outcome ? outcome.Kind.ToString() : "no answer in " + StanceAskSeconds.ToString("0", CultureInfo.InvariantCulture) + " s")}; the mode is {session.State.Self.Qualities.CombatMode}, read when the fight next chose a cast, {clock - _stanceAskAt:0.0} s after the ask (a walk or a loot between them counts; the server's answer time is the SetCombatMode action line's ms) (m6-stance-after-read)"));
        }

        return clock - _stanceAskAt < StanceGapSeconds && session.State.Self.Qualities.CombatMode != CombatModes.Magic;
    }
}
