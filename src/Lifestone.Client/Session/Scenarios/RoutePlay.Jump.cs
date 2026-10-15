using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The jump leg of a <see cref="RoutePlay"/> (m2 wave 3, dod-8-9-2): after the laps, <see cref="JumpLeg"/> presses the player's jump keys (a standing and a running jump), then where-am-I is
/// asked and recorded in the audit like every other check, so a force-position correction after a jump, or a disagreement between the server and the body, fails the movement verdict.
/// </summary>
public sealed partial class RoutePlay
{
    private bool _jumps;
    private JumpLeg? _jump;

    /// <summary>The jump leg once it began, or null (a play built without <c>jumps</c>, or before the laps ended).</summary>
    public JumpLeg? Jump => _jump;

    private ScenarioStatus Jumping(IGameSessionView session, double now)
    {
        _jump ??= new JumpLeg(_walker!.Controller, _walker.Controller.Options.RunByDefault);
        _jump.Tick(now);
        if (!_jump.IsDone)
        {
            return session.Clock.Now - _stageStart > WalkSeconds
                ? Fail(string.Create(CultureInfo.InvariantCulture, $"jumping: the jump leg did not finish within {WalkSeconds:0.#} s ({_jump.Describe()})"))
                : ScenarioStatus.Running;
        }

        if (_jump.Failure is { } failure)
        {
            return Fail("jumping: " + failure);
        }

        _log.Info(LogCategory, "the jump leg ended", new LogField("jumps", _jump.Jumps), new LogField("landed", _jump.Landed), new LogField("at", _walker!.Position.Frame.Origin));
        _nextWhere = 0;
        return Go("jump-where", session);
    }

    private ScenarioStatus JumpWhere(IGameSessionView session)
    {
        if (_pending is null)
        {
            if (session.Clock.Now < _nextWhere)
            {
                return Within(session, SettleSeconds, "jump-where: the server still had the player teleporting");
            }

            _whereClient = _walker!.Position;
            _where = session.Tools.WhereAmI(ActionSource.Host);
            _pending = _where;
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Within(session, SettleSeconds, "jump-where: no answer from the server about where the player is");
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || _where?.Result is not { } server)
        {
            return Fail("jump-where: asking the server where the player is", outcome);
        }

        if (server.Teleporting)
        {
            _nextWhere = session.Clock.Now + 0.25;
            return Within(session, SettleSeconds, "jump-where: the server still had the player teleporting");
        }

        var metres = _audit!.RecordWhere(_whereClient, server.Position, server.ForcePositionSequence);
        _log.Info(
            LogCategory,
            "where-am-I after the jumps",
            new LogField("metres", metres),
            new LogField("force_position_sequence", server.ForcePositionSequence),
            new LogField("jumps_sent", _audit.Summary.JumpSent));
        return Go("verdict", session);
    }
}
