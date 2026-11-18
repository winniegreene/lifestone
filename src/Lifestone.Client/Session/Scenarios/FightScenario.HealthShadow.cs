using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 16 E2d (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the fight's part of the health picture's shadow. Report-only: the fight's live health model (<see cref="TargetHealthModel"/>)
/// answers every read exactly as before; beside each read the session's one picture (<see cref="HealthPicture"/>) is asked for the same creature, and a difference beyond the tolerance is one line when it appears, when
/// its cause changes and when it ends (<see cref="HealthShadow"/>). The lanes are the fight's readers: <c>fight-picker</c>, <c>fight-opener</c>, <c>fight-drain</c>, <c>fight-life-through</c>, <c>fight-mana-loop</c>,
/// <c>fight-policy</c>, <c>fight-yield</c> (all through <see cref="ReadHealth"/>), <c>fight-emergency</c> (<see cref="HealthEstimate"/>) and <c>fight-war</c> (<see cref="LiveHealthOrFull"/>). A drain's take is told to the
/// picture where the model is told it (<see cref="NoteYield"/>). At the fight's end one line carries the fight's asks:
/// <c>health-shadow: fight: its asks: health_shadow_agree=&lt;n&gt; health_shadow_differ=&lt;n&gt; (...)</c>, beside the threat shadow's.
/// </summary>
public sealed partial class FightScenario
{
    private HealthPicture? _healthPicture;
    private HealthShadowShare? _healthShare;
    private Action<string>? _healthLine;
    private bool _healthShadowTried;

    /// <summary>The session's picture and the fight's share of its shadow, made once. A shadow that cannot be made is named once and left out: the model decides without it, as before.</summary>
    private HealthPicture? HealthShadowReady()
    {
        if (_healthShadowTried || (_hookSession ?? _view) is not { } session)
        {
            return _healthPicture;
        }

        _healthShadowTried = true;
        try
        {
            var picture = ThreatBoard.For(session).Health;
            _healthLine = text => _log.Info(LogCategory, text);
            _healthShare = new HealthShadowShare(
                picture.Shadow,
                HealthReader.FightPicker,
                HealthReader.FightOpener,
                HealthReader.FightDrain,
                HealthReader.FightLifeThrough,
                HealthReader.FightManaLoop,
                HealthReader.FightPolicy,
                HealthReader.FightYield,
                HealthReader.FightEmergency,
                HealthReader.FightWar);
            _healthPicture = picture;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _log.Error(LogCategory, $"{HealthShadow.Token}fight: TODO(health-shadow-failed): the fight's health shadow could not be made ({error.GetType().Name}: {error.Message}); its model decides without it, as ever");
        }

        return _healthPicture;
    }

    /// <summary>The model answered a read of <paramref name="guid"/>: the one picture is asked for the same creature beside it (report-only). Allocates nothing while the two agree or a difference stands.</summary>
    private void ShadowHealth(HealthReader reader, uint guid, bool known, float fraction, int max, HealthSource source, double age, double clock)
    {
        if (HealthShadowReady() is not { } picture)
        {
            return;
        }

        // the age of the model's last word of the server: a pushed or queried value's own age; a modelled one's is not kept (the model stamps its own casts over it); an unknown creature has none
        var wordAge = !known ? double.PositiveInfinity : source is HealthSource.Pushed or HealthSource.Queried ? age : double.NaN;
        picture.Shadow.Compare(reader, guid, known && max > 0, max * (double)Math.Clamp(fraction, 0f, 1f), max, clock, _healthLine!, _live.DealtSince(guid), wordAge);
    }

    /// <summary>A drain of the fight's took <paramref name="points"/> from <paramref name="guid"/>: the picture is told where the model is.</summary>
    private void HealthPictureTook(uint guid, double points) => HealthShadowReady()?.Took(guid, points);

    /// <summary>The fight ends: its line with the asks of its health readers, when it asked at all.</summary>
    private void EndHealthShadow()
    {
        if (_healthShare?.Line("fight") is { } line)
        {
            _log.Info(LogCategory, line);
        }
    }
}
