using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What the four stat actions share (M3 s1b; spec 5.3): the <see cref="ActionLane.Stats"/> lane (one raise in flight at a time),
/// the in-world precondition, and ACE's refusal lines. ACE answers a raise with the spent experience
/// (<c>GameMessagePrivateUpdatePropertyInt64</c> AvailableExperience, 0x02CF) and then the stat's own private update; it tells a
/// refusal only in some cases (a chat line: <c>Player_Attributes.cs</c>, <c>Player_Vitals.cs</c>, <c>Player_Skills.cs</c> in
/// ACEmulator/ACE@95ef426d); otherwise it stays silent and the action times out.
/// </summary>
internal abstract class StatTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Stats, ActionPolicy.StatSeconds)
{
    /// <summary>ACE's raise refusal: <c>Your attempt to raise {stat} has failed.</c> (<c>HandleActionRaiseAttribute</c>, <c>HandleActionRaiseVital</c>).</summary>
    internal const string RaiseFailedStart = "Your attempt to raise ";

    /// <summary>The end of <see cref="RaiseFailedStart"/>'s line.</summary>
    internal const string RaiseFailedEnd = " has failed.";

    /// <summary>ACE's train refusal: <c>Failed to train {skill}! You now have N credits available.</c> (<c>HandleActionTrainSkill</c>).</summary>
    internal const string TrainFailedStart = "Failed to train ";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    /// <remarks>Serialised in <see cref="ActionLane.Stats"/>, so a refusal line seen while this action is in flight is its own.</remarks>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (line.Kind is not (ChatKind.System or ChatKind.Transient))
        {
            return false;
        }

        var text = line.Text;
        var refused = (text.StartsWith(RaiseFailedStart, StringComparison.Ordinal) && text.Contains(RaiseFailedEnd, StringComparison.Ordinal))
            || text.StartsWith(TrainFailedStart, StringComparison.Ordinal);
        if (!refused || !RefusalFits(text))
        {
            return false;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused: " + text));
        return true;
    }

    /// <summary>True when a refusal line is the answer to this kind of action (a raise's line is not a train's).</summary>
    protected abstract bool RefusalFits(string text);

    /// <summary>Confirms with what changed, in the outcome's detail.</summary>
    protected void Confirm(string detail) => Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail));

    /// <summary>A number for a detail.</summary>
    protected static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// <see cref="IActionApi.RaiseAttribute"/>: GameAction 0x0045, as retail's <c>CM_Train::Event_TrainAttribute</c> sends it (11.4186
/// 0x006A7F30, 11.6096 0x006A8E90). ACE spends the experience and sends PrivateUpdateAttribute 0x02E3 (even for an amount that
/// does not buy a rank); the update for the attribute, with more experience spent than before, completes the action. A refusal it
/// tells (the attribute at its upper limit, or more experience than the rank cap allows) is the chat line "Your attempt to raise ...
/// has failed."; more experience than the character has is dropped without a word and times out.
/// </summary>
internal sealed class RaiseAttributeTracker(ActionHandle handle, uint attribute, uint xp) : StatTracker(handle)
{
    private uint _ranks;
    private uint _spent;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0045 RaiseAttribute " + attribute;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (api.State.Self.Qualities.TryGetAttribute(attribute, out var before))
        {
            _ranks = before.Ranks;
            _spent = before.Experience;
        }

        session!.SendRaiseAttribute(attribute, xp);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        if (e.Change != QualityChange.Attribute || e.Key != attribute || !api.State.Self.Qualities.TryGetAttribute(attribute, out var now) || now.Experience <= _spent)
        {
            return;
        }

        Confirm("ranks " + N(_ranks) + " -> " + N(now.Ranks) + ", experience spent " + N(_spent) + " -> " + N(now.Experience));
    }

    /// <inheritdoc/>
    protected override bool RefusalFits(string text) => text.StartsWith(RaiseFailedStart, StringComparison.Ordinal);
}

/// <summary>
/// <see cref="IActionApi.RaiseVital"/>: GameAction 0x0044, as retail's <c>CM_Train::Event_TrainAttribute2nd</c> sends it (11.4186
/// 0x006A8040, 11.6096 0x006A8FA0). ACE answers with PrivateUpdateVital 0x02E7 for the vital (named by its maximum's id 1, 3 or 5);
/// more experience than the character has gets the chat line "Your attempt to raise ... has failed." (a vital's only told refusal).
/// </summary>
internal sealed class RaiseVitalTracker(ActionHandle handle, uint vital, uint xp) : StatTracker(handle)
{
    private uint _ranks;
    private uint _spent;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0044 RaiseVital " + vital;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (api.State.Self.Qualities.TryGetVital(vital, out var before))
        {
            _ranks = before.Ranks;
            _spent = before.Experience;
        }

        session!.SendRaiseVital(vital, xp);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        if (e.Change != QualityChange.Vital || e.Key != vital || !api.State.Self.Qualities.TryGetVital(vital, out var now) || now.Experience <= _spent)
        {
            return;
        }

        Confirm("ranks " + N(_ranks) + " -> " + N(now.Ranks) + ", experience spent " + N(_spent) + " -> " + N(now.Experience));
    }

    /// <inheritdoc/>
    protected override bool RefusalFits(string text) => text.StartsWith(RaiseFailedStart, StringComparison.Ordinal);
}

/// <summary>
/// <see cref="IActionApi.RaiseSkill"/>: GameAction 0x0046, as retail's <c>CM_Train::Event_TrainSkill</c> sends it (11.4186 0x006A8150,
/// 11.6096 0x006A90B0). ACE answers with PrivateUpdateSkill 0x02DD for a trained or specialised skill. It tells no refusal (an
/// untrained skill, too much experience and the rank cap are all silent), so those time out.
/// </summary>
internal sealed class RaiseSkillTracker(ActionHandle handle, uint skill, uint xp) : StatTracker(handle)
{
    private ushort _ranks;
    private uint _spent;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0046 RaiseSkill " + skill;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (api.State.Self.Qualities.TryGetSkill(skill, out var before))
        {
            _ranks = before.Ranks;
            _spent = before.Experience;
        }

        session!.SendRaiseSkill(skill, xp);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        if (e.Change != QualityChange.Skill || e.Key != skill || !api.State.Self.Qualities.TryGetSkill(skill, out var now) || now.Experience <= _spent)
        {
            return;
        }

        Confirm("ranks " + N(_ranks) + " -> " + N(now.Ranks) + ", experience spent " + N(_spent) + " -> " + N(now.Experience));
    }

    /// <inheritdoc/>
    protected override bool RefusalFits(string text) => false;
}

/// <summary>
/// <see cref="IActionApi.TrainSkill"/>: GameAction 0x0047, as retail's <c>CM_Train::Event_TrainSkillAdvancementClass</c> sends it
/// (11.4186 0x006A8260, 11.6096 0x006A91C0). ACE answers a trained skill with PrivateUpdateSkill 0x02DD, then PrivateUpdatePropertyInt
/// 0x02CD AvailableSkillCredits (24), then the line "{skill} trained."; both updates complete the action. A skill that is already
/// trained gets "Failed to train {skill}!"; credits that differ from the skill's cost, or more than the character has, are dropped
/// without a word and time out.
/// </summary>
internal sealed class TrainSkillTracker(ActionHandle handle, uint skill, int credits) : StatTracker(handle)
{
    /// <summary>ACE's <c>SkillAdvancementClass.Trained</c>: the class a trained skill reports (2; specialised is 3).</summary>
    internal const uint TrainedClass = 2;

    private bool _skillSeen;
    private bool _creditsSeen;
    private int _creditsBefore;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0047 TrainSkill " + skill;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _creditsBefore = api.State.Self.Qualities.AvailableSkillCredits;
        session!.SendTrainSkill(skill, credits);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnQuality(in QualitiesChanged e, ActionApi api)
    {
        var q = api.State.Self.Qualities;
        if (e.Change == QualityChange.Skill && e.Key == skill && q.TryGetSkill(skill, out var now) && now.AdvancementClass >= TrainedClass)
        {
            _skillSeen = true;
        }
        else if (e.Change == QualityChange.Int && e.Key == PlayerQualities.AvailableSkillCreditsProperty)
        {
            _creditsSeen = true;
        }

        if (_skillSeen && _creditsSeen)
        {
            Confirm("trained; skill credits " + N(_creditsBefore) + " -> " + N(q.AvailableSkillCredits));
        }
    }

    /// <inheritdoc/>
    protected override bool RefusalFits(string text) => text.StartsWith(TrainFailedStart, StringComparison.Ordinal);
}
