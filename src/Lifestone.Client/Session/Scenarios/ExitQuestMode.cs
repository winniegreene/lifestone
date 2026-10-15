using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What an exit scenario does about the quest its portal needs (<see cref="PortalExit.QuestName"/>) before it uses the portal, and
/// what it does afterwards so that the quest registry is as it was found. The quest is the character's own, changed only through the
/// server mod's <c>@lifestone quest</c> verb (<see cref="PlayerTools.HasQuest"/>, <see cref="PlayerTools.StampQuest"/>,
/// <see cref="PlayerTools.EraseQuest"/>); no scenario touches the database. Only the quest is put back: the portal's own emote set (weenie 29338,
/// ACE's Portal.cs:287) also moves the sanctuary, enables recalls and erases nine other quests, and the mod has no verb for those:
/// graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25).
/// </summary>
public enum ExitQuestMode
{
    /// <summary>
    /// The exit is taken: the character must hold the quest for the Use, so the scenario asks (<c>has</c>) and stamps it when it is
    /// missing, remembering what it found. ACE then teleports the character (F751) and the portal's emote set erases the quest; a refusal fails the run.
    /// After the way home the scenario asks again and brings the registry to what it found: nothing to send when the quest is gone and was not held, a
    /// stamp when it was held (a count other than 1 fails loudly), an erase when it was not held and still is.
    /// </summary>
    Stamp = 0,

    /// <summary>
    /// The refusal is taken: the character must not hold the quest for the Use, so the scenario asks and erases it when it is held
    /// (only a quest held once can be put back, so any other count fails the run before anything is changed). ACE refuses the Use
    /// (WeenieError 0x0474, before the emote set runs) and sends no F751, which is the outcome. Afterwards the registry is asked and the quest stamped
    /// again, only when the scenario erased it. A teleport fails the run.
    /// </summary>
    Erase = 1,
}
