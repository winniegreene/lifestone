using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// The decisions of one <c>quest</c> request that need nothing from ACE, so Lifestone.ServerMod.Tests runs them
/// (QuestChangeTests): when a request is refused, what the reply's data holds for the registry row before and after, and
/// whether that outcome is the one the action promises. QuestVerb reads ACE's registry and gives the counts here.
/// </summary>
/// <remarks>
/// A completion count is the registry row's NumTimesCompleted; a character without a row has no count, which the reply
/// writes as 0 with its held flag false. The check is the mod's loud guard against ACE changing under it (m2-plan §4.12:
/// an unknown is a failure that says so, never a guessed reply): a stamp that leaves no row, an erase that leaves one, a
/// <c>has</c> that changed the registry, or a count that went down.
/// </remarks>
internal static class QuestChange
{
    /// <summary>The refusal sentence for a request that changes the registry of a player who is logging out.</summary>
    public const string LoggingOutMessage = "you are logging out";

    /// <summary>
    /// The refusal for <paramref name="request"/>, or null. A stamp or an erase for a player who is logging out is
    /// <see cref="ToolErrors.Busy"/>: ACE saves the character as it leaves, and a change made after that save would be lost
    /// while the reply claimed it. Reading a quest (<see cref="QuestAction.Has"/>) is never refused.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="isLoggingOut">ACE's Player.IsLoggingOut.</param>
    public static ToolError? Refusal(QuestRequest request, bool isLoggingOut)
    {
        ArgumentNullException.ThrowIfNull(request);
        return isLoggingOut && request.Action != QuestAction.Has ? new ToolError(ToolErrors.Busy, LoggingOutMessage) : null;
    }

    /// <summary>The reply's data from the registry row's completions before and after the request (null: no row).</summary>
    /// <param name="request">The request.</param>
    /// <param name="before">The completions before the request, or null when the character had no row.</param>
    /// <param name="after">The completions after the request, or null when the character has no row.</param>
    public static QuestData Build(QuestRequest request, int? before, int? after)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new QuestData(ToolRequestParser.QuestActionWord(request.Action), request.Name, before is not null, before ?? 0, after is not null, after ?? 0);
    }

    /// <summary>
    /// Null when <paramref name="data"/> is what the request's action promises (a stamp leaves the row with its count the same
    /// or higher, an erase leaves none, a <c>has</c> changes nothing); otherwise the <see cref="ToolErrors.Failed"/> error
    /// that says what ACE did instead.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="data">What <see cref="Build"/> made of the registry before and after.</param>
    public static ToolError? Check(QuestRequest request, QuestData data)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(data);
        var problem = request.Action switch
        {
            QuestAction.Stamp when !data.Held => "ACE did not record the stamp: the quest registry has no row for it afterwards",
            QuestAction.Stamp when data.After < data.Before => "ACE lowered the quest's completions on a stamp",
            QuestAction.Erase when data.Held => "ACE did not erase the quest: the registry still has its row",
            QuestAction.Has when data.Changed() => "the registry changed while the quest was only being read",
            _ => null,
        };
        return problem is null ? null : new ToolError(ToolErrors.Failed, "quest " + request.Name + ": " + problem + "; see ACE_Log.txt");
    }
}
