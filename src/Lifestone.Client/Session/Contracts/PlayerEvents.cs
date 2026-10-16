namespace Lifestone.Client.Session.Contracts;

/// <summary>A line was added to the chat (<see cref="GameState.Chat"/>).</summary>
/// <param name="Line">The line.</param>
public readonly record struct ChatReceived(ChatLine Line) : IGameEvent;

/// <summary>
/// The Lifestone server mod answered a tool request: an F7E0 line starting <c>#lifestone/1 </c>, parsed with the mod's
/// shared wire contract (ToolsWire) and kept out of the chat. The typed result reaches the request's
/// <see cref="ToolHandle{T}"/>; this event is for the inspector and the action log.
/// </summary>
/// <param name="RequestId">The request id the reply echoes (correlates it with a <see cref="ToolHandle{T}"/>), or null.</param>
/// <param name="Verb">The verb answered (<c>status</c>, <c>where</c>, <c>place</c>, <c>xp</c>, <c>net</c>, <c>places</c>).</param>
/// <param name="Ok">True when the tool succeeded.</param>
/// <param name="ErrorCode">The mod's error code when it failed (<c>bad-args</c>, <c>not-found</c>, <c>not-loopback</c> ...), else null.</param>
/// <param name="Message">The human reply line (also shown in the chat as a <see cref="ChatKind.ToolReply"/> line), or null.</param>
public readonly record struct ToolReplyReceived(string? RequestId, string Verb, bool Ok, string? ErrorCode, string? Message) : IGameEvent;

/// <summary>
/// The player tools cannot work (<see cref="PlayerTools.Availability"/>): the server answered <c>Unknown command:
/// lifestone</c> (<see cref="ToolAvailability.ModMissing"/>: the mod is not loaded), or a tool request met a server that
/// is not loopback (<see cref="ToolAvailability.NotLoopback"/>, once per session). Pending tool requests fail with
/// <see cref="ActionOutcomeKind.Refused"/>. Leaving the world refuses pending requests too, without this event (the
/// availability is <see cref="ToolAvailability.NotInWorld"/> then).
/// </summary>
/// <param name="Availability">The new availability.</param>
/// <param name="Reason">One sentence for the log and the tools panel's dialog.</param>
public readonly record struct ToolsUnavailable(ToolAvailability Availability, string Reason) : IGameEvent;

/// <summary>GameEvent 0x01C7 UseDone: the server finished (or refused) a Use.</summary>
/// <param name="WeenieError">0 when the use succeeded, else the WeenieError code.</param>
public readonly record struct UseDone(uint WeenieError) : IGameEvent;

/// <summary>GameEvent 0x01C0 UpdateHealth: an object's health, the answer to <see cref="IActionApi.QueryHealth"/>.</summary>
/// <param name="Guid">The object.</param>
/// <param name="Health">Its health as a fraction, 0 to 1 (also <see cref="WorldObject.Health"/>).</param>
public readonly record struct HealthUpdated(uint Guid, float Health) : IGameEvent;

/// <summary>The player's qualities changed (<see cref="SelfState.Qualities"/>; owner decision D16).</summary>
/// <param name="Change">What kind of quality changed.</param>
/// <param name="Key">The property, attribute, vital or skill id; 0 for <see cref="QualityChange.Description"/>.</param>
public readonly record struct QualitiesChanged(QualityChange Change, uint Key) : IGameEvent;

/// <summary>The kinds of <see cref="QualitiesChanged"/>.</summary>
public enum QualityChange
{
    /// <summary>A whole PlayerDescription (GameEvent 0x0013) replaced every quality.</summary>
    Description,

    /// <summary>An integer property (PrivateUpdatePropertyInt).</summary>
    Int,

    /// <summary>A 64-bit integer property (PrivateUpdatePropertyInt64).</summary>
    Int64,

    /// <summary>A primary attribute (PrivateUpdateAttribute).</summary>
    Attribute,

    /// <summary>A vital's ranks and experience (PrivateUpdateVital).</summary>
    Vital,

    /// <summary>A vital's current value (PrivateUpdateAttribute2ndLevel).</summary>
    VitalCurrent,

    /// <summary>A skill (PrivateUpdateSkill).</summary>
    Skill,

    /// <summary>The player changed an option bit for this session (<c>IActionApi.SetSessionOption</c>); <c>Key</c> is the option's bit.</summary>
    Options,

    /// <summary>
    /// The player's enchantment registry changed (a 0x02C2 to 0x02C8 or 0x0312 applied; <see cref="PlayerQualities.Enchantments"/>): the effective values of the vitals, attributes and skills
    /// moved with it. Published beside <see cref="EnchantmentChanged"/> on every registry change; <c>Key</c> is 0. A screen that shows effective values refreshes on this alone.
    /// </summary>
    Enchantments,
}
