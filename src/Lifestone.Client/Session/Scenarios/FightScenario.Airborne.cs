using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 airborne (live 2026-11-13 14:12 UTC, the Tusker Abode's pit, <c>cache/m6/14/live/abode5</c>): the climber's first live running jump crossed the pit, and 1.4 s into the flight a Tusker Redeemer's swing made
/// self-defence drop the walk, so the jump's landing check never ran; the fight then cast Flame Bolt VI three times in the air, ACE refused each (0x04EB YouCantDoThatWhileInTheAir), and the war pick dropped the bolt for the fight as
/// a spell ACE refused three times in a row (M6 7 K), and then V, IV and III the same way.
/// </summary>
/// <remarks>
/// <para><b>What a player can do in the air.</b> Nothing useful: ACE refuses a cast while the body is off walkable ground (<c>Player.IsJumping</c>, <c>Player.cs</c>:49-62; <c>Player_Magic.cs</c>:105-109, 296-300 @95ef426d), and a melee or
/// missile attack, a heal and a drink as well (<c>Player_Melee.cs</c>:75-79, <c>Player_Missile.cs</c>:64-68, <c>Healer.cs</c>:69-73, <c>Food.cs</c>:53-57); retail's movement keys change no motion without contact but a turn (<c>CMotionInterp::contact_allows_move</c>, 11.6096 0x00528E50, <see cref="Lifestone.Data.Motion.MotionInterpreter.ContactAllowsMove"/>).</para>
/// <para><b>The hold.</b> While the walk's jump is in flight (<see cref="IFightWalk.JumpInFlight"/>: from the take-off until the runner's landing check ran) the fight's frame ticks the walk (whose landing check is its own) and stops
/// there: no rule that drops the walk runs (self-defence, the emergency, the buff refresh, sight, the stuck give-up, the walk back, the stop asked, the until, the minutes, the win, the seconds limit) and no decision is made. An attack in the
/// air is still heard (the handlers note the attacker as before). The frame after the landing check the fight goes on where it was: self-defence's own rule drops the walk at once for an attacker in range with a line, and the cast follows.
/// The kill switch and a death still end the fight at once.</para>
/// <para><b>The cast.</b> Every cast of the fight goes through <see cref="CastAttempt"/>, whose check before holds a try while the body is in the air (a fall as much as a jump) and whose check after makes a 0x04EB answer a wait, never
/// a try; the war pick's refused-in-a-row rule never counts the code (<see cref="ActionReasons.FromCode"/>: <see cref="ActionReason.InTheAir"/>), and an attempt that ends <see cref="CastEnd.InTheAir"/> is no miss and sets nothing
/// aside (the bolt, the drain and the heal, the opener, the stamina drain, the dance and the refill).</para>
/// </remarks>
public sealed partial class FightScenario
{
    private double _airSince = double.NaN;
    private int _airHolds;

    /// <summary>M6 14 airborne: the jumps the fight held still for, from the take-off to the landing check (report-only).</summary>
    public int AirHolds => _airHolds;

    /// <summary>True while the walk's jump is in flight: the frame stops here (logged at the take-off and at the landing check, once each a jump).</summary>
    private bool AirborneHolds(IGameSessionView session, double clock)
    {
        if (_walk?.JumpInFlight == true)
        {
            if (double.IsNaN(_airSince))
            {
                _airSince = clock;
                _airHolds++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"airborne: the walk's jump is in flight or in its run for the brink with the jump key down (phase {_phase}; m6-pit-hold: a drop in that run leaves her on the brink, in the pit's cell): no rule drops the walk and nothing new starts until its landing check has run (ACE refuses a cast in the air, 0x{CastAttempt.InTheAirCode:X4} YouCantDoThatWhileInTheAir, Player_Magic.cs:105-109 @95ef426d)"));
            }

            return true;
        }

        if (!double.IsNaN(_airSince))
        {
            var attacker = AttackerNow(session, clock);
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"airborne: the jump's landing check has run, {clock - _airSince:0.0#} s after the take-off; the fight goes on{(attacker is null ? string.Empty : $": the attacker {attacker.Name} 0x{attacker.Guid:X8} (heard {clock - attacker.LastAt:0.0#} s ago) is self-defence's at once")}"));
            _airSince = double.NaN;
        }

        return false;
    }

    /// <summary>M6 14 airborne: a cast that ended <see cref="CastEnd.InTheAir"/> (held past <see cref="CastAttempt.MaxAirSeconds"/>): named, no miss and no drop; the fight chooses again.</summary>
    private ScenarioStatus AirEnded(string what, in CastReport report)
    {
        _log.Warn(LogCategory, $"airborne: {what} ended in the air ({report.Why}): not a miss and not a refusal of the spell; the fight chooses again");
        _phase = Phase.Choose;
        return ScenarioStatus.Running;
    }
}
