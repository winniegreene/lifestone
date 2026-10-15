using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// One <c>xp</c> request's count of the experience ACE added, across the player's action queue, with nothing from ACE in
/// it, so Lifestone.ServerMod.Tests runs it (XpGrantTests). XpVerb queues <see cref="Before"/> just ahead of ACE's
/// UpdateXpAndLevel and the reply, which calls <see cref="After"/>, right behind it.
/// </summary>
/// <remarks>
/// The queue runs its actions one at a time, in order (ActionQueue.cs RunActions), and the three are queued together, so
/// nothing runs between them: <see cref="XpData.Added"/> is exactly what this request's update added, even with other
/// experience queued before or after it. It is less than requested at the level cap, and 0 for an Olthoi player, whom
/// GrantXP skips (Player_Xp.cs:55-61).
/// </remarks>
/// <param name="request">The request.</param>
internal sealed class XpGrant(XpRequest request)
{
    private long before;
    private bool read;

    /// <summary>The request.</summary>
    public XpRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));

    /// <summary>Records the player's total experience just before ACE's update.</summary>
    public void Before(long totalExperience)
    {
        before = totalExperience;
        read = true;
    }

    /// <summary>
    /// The reply's data once ACE's update has run: what it added (<paramref name="totalExperience"/> minus the total
    /// <see cref="Before"/> recorded) and the player's experience and level now. Throws
    /// <see cref="InvalidOperationException"/> when <see cref="Before"/> never ran, which would mean ACE's queue did not
    /// run the actions in order: a loud failure, never a guessed count.
    /// </summary>
    public XpData After(long totalExperience, long availableExperience, int level)
    {
        if (!read)
        {
            throw new InvalidOperationException("xp: the reply ran before the experience was read, so ACE's action queue did not run the request's actions in order");
        }

        return new XpData(Request.Amount, totalExperience - before, totalExperience, availableExperience, level);
    }
}
