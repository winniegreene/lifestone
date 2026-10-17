using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Suicide"/> (M3 3.2 s0): GameAction 0x0279 with no payload, as retail's <c>CM_Character::Event_Suicide</c> sends it (11.4186 0x006A1B00, 11.6096 0x006A2920) and ACE's <c>GameActionDie</c> reads it.
/// ACE (<c>Player_Death.cs</c> <c>HandleActionDie</c> 299-319, <c>HandleSuicide</c> 330-345) answers a player who is dead or teleporting with WeenieError YoureTooBusy (0x001D) and nothing else; any other player gets five
/// HearSpeech lines 3 s apart and then <c>Die</c> (without the victim notice 0x01AC): the Dead motion, the private health update 0x02E9 to 0, the NumDeaths update 0x02CD (property 43), WeenieError YouKilledYourself, the vitae and the
/// purge. A second request while one is in progress is dropped by ACE without a word (it times out here).
/// </summary>
/// <remarks>
/// <para><b>Done.</b> <see cref="ActionOutcomeKind.Confirmed"/> when, since the send, the player's health current has been updated to 0 (0x02E9) <b>and</b> the NumDeaths property has risen (0x02CD), in either order (ACE sends the health first).
/// The check reads the state after each update, so a later 75% health restore does not undo it. A death by another cause while the suicide is in progress counts too: ACE abandons the suicide when NumDeaths changes, and the player is dead either way.</para>
/// <para><b>Refused.</b> WeenieError YoureTooBusy (<see cref="YoureTooBusy"/>) while in flight is <see cref="ActionOutcomeKind.Refused"/> with that code: ACE refused it after it was sent (the plan's word for the refusal; the outcome kind is
/// the same one a pre-send refusal uses). The error is not specific to a suicide, so a YoureTooBusy that a different action provoked in the same window would also end it (accepted: the suicide runs alone in the scenarios).</para>
/// <para><b>Time limit.</b> <see cref="ActionPolicy.SuicideSeconds"/> (30 s) on the session clock (<see cref="SessionClock"/>), counted from the send: five 3 s lines make the death 15 s after the send.</para>
/// <para>Lane Immediate: it does not wait behind casts, and a death ends the player's other actions anyway. It allocates nothing while it waits.</para>
/// </remarks>
internal sealed class SuicideTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Immediate, ActionPolicy.SuicideSeconds)
{
    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> (0x001D), the answer to a suicide while dead or teleporting.</summary>
    internal const uint YoureTooBusy = 0x001D;

    /// <summary>The vital id of health's maximum, the key the state keeps the vital under (<see cref="PlayerQualities.TryGetVital"/>).</summary>
    private const uint HealthVital = 1;

    private int _deathsBefore;
    private bool _healthZero;
    private bool _deathsRose;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0279 Suicide";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _deathsBefore = api.State.Self.Qualities.TryGetInt(DeathTally.NumDeathsProperty, out var deaths) ? deaths : 0;
        _healthZero = false;
        _deathsRose = false;
        session!.SendSuicide();
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        var q = api.State.Self.Qualities;
        if (e.Change == QualityChange.VitalCurrent && q.TryGetVital(HealthVital, out var health) && health.Current == 0)
        {
            _healthZero = true;
        }
        else if (e.Change == QualityChange.Int && e.Key == DeathTally.NumDeathsProperty && q.TryGetInt(DeathTally.NumDeathsProperty, out var deaths) && deaths > _deathsBefore)
        {
            _deathsRose = true;
        }

        if (_healthZero && _deathsRose)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "the player died (health 0 and NumDeaths up from " + _deathsBefore + ")"));
        }
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        if (e.Line.Kind != ChatKind.WeenieError || e.Line.ChatType != YoureTooBusy)
        {
            return false;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.Refused, YoureTooBusy, "the server refused the suicide (WeenieError YoureTooBusy): the player is dead or teleporting"));
        return true;
    }
}

/// <summary>
/// <see cref="IActionApi.AddSpellFavorite"/> (M3 3.2 s0): GameAction 0x01E3 {spell, index, bar}, as retail's <c>CM_Character::Event_AddSpellFavorite</c> sends it (11.4186 0x006A0F70, 11.6096 0x006A1D90) and ACE's
/// <c>GameActionAddSpellFavorite</c> reads it. ACE answers nothing (<c>Player_Character.cs</c> <c>HandleActionAddSpellFavorite</c> 298-305 ignores a bar above 7 and a spell the player does not know, and keeps the favourite
/// in the character's saved bars), so the action completes as <see cref="ActionOutcomeKind.Sent"/> when it is sent; the next PlayerDescription shows whether it took. Lane Immediate (no reply to wait for, no limit).
/// </summary>
internal sealed class AddSpellFavoriteTracker(ActionHandle handle, uint spellId, int index, int bar) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x01E3 AddSpellFavorite spell=" + spellId + " index=" + index + " bar=" + bar;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendAddSpellFavorite(spellId, (uint)index, (uint)bar);
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, "spell " + spellId + " at index " + index + " on bar " + bar);
    }
}
