namespace Lifestone.ServerMod;

/// <summary>
/// One Corpse world object as <see cref="CorpseRotRules"/> reads it: the four values the verb takes from ACE's <c>Corpse</c>, so the
/// rules need no ACE type. CorpsesVerb makes these from ACE's corpses; the tests make them by hand.
/// </summary>
/// <param name="Name">ACE's <c>WorldObject.Name</c>: "Corpse of " and the victim's name for a corpse ACE made at a death.</param>
/// <param name="HasLevel">Whether <c>WorldObject.Level</c> has a value. ACE gives a corpse a Level only in <c>Corpse.RecalculateDecayTime</c>, which it
/// calls for a player's corpse alone (Corpse.cs:115, Creature_Death.cs:540 and :551), and ACE's own player-corpse tests read it
/// (Landblock.cs:905, WorldObject_Decay.cs:62, Corpse.cs:68); it is kept in the database with the corpse, so it holds for one loaded after a restart.</param>
/// <param name="IsMonster">ACE's <c>Corpse.IsMonster</c>, set for a creature's corpse when ACE makes it (Creature_Death.cs:584). It is a field, not a
/// property, so a corpse loaded from the database has it false; <see cref="HasLevel"/> is what tells a reloaded player corpse from a monster's.</param>
/// <param name="TimeToRot">ACE's <c>WorldObject.TimeToRot</c>, the seconds left before the object rots (WorldObject_Properties.cs:2765-2775): null when unset
/// (ACE then gives it 5 minutes at its next heartbeat, WorldObject_Decay.cs:58-65), -1 for never, 0 or below (but not -1) for expired.</param>
internal readonly record struct CorpseCandidate(string? Name, bool HasLevel, bool IsMonster, double? TimeToRot)
{
    /// <summary>True for a corpse a player left: it has a Level and is not a monster's.</summary>
    public bool IsPlayerCorpse => HasLevel && !IsMonster;
}

/// <summary>What <see cref="CorpseRotRules.Plan"/> decided over a list of corpses.</summary>
/// <param name="Found">How many corpses matched.</param>
/// <param name="SetIndexes">The positions (in the list given to <see cref="CorpseRotRules.Plan"/>) of the matching corpses whose time to rot is to be set.</param>
internal sealed record CorpseRotPlan(int Found, IReadOnlyList<int> SetIndexes)
{
    /// <summary>How many corpses are to be set: at most <see cref="Found"/>.</summary>
    public int Rotted => SetIndexes.Count;
}

/// <summary>
/// The rules of <c>@lifestone corpses rot</c>, with nothing from ACE in them, so Lifestone.ServerMod.Tests runs them (CorpseRotRulesTests).
/// </summary>
/// <remarks>
/// <para><b>Which corpses.</b> Only a player corpse (<see cref="CorpseCandidate.IsPlayerCorpse"/>) whose name is exactly
/// <see cref="NamePrefix"/> and the caller's own character's name, compared ordinally and case-sensitively (the server's culture must not change
/// the match). Every generation of a re-created character shares its name, which is why the name, not the character's id, is the match: a
/// corpse of a deleted generation is the point of the verb. Nothing else matches: not a creature's corpse, not another player's (a
/// different name), not a name that only contains the caller's (<c>Corpse of BSD Mage 2</c>).</para>
/// <para><b>Which of those are set.</b> The same ones ACE's own limit would shorten, and more: a corpse is set when its time to rot is more than ACE's
/// <c>Corpse.EmptyDecayTime</c> (the test in Landblock.cs:912), or unset, or -1 (never rots; ACE's decay skips it, WorldObject_Decay.cs:41-42, so
/// without this it would stay for good). A corpse already at or under the empty-decay time, expired (0 or below, but not -1) or already decayed
/// (ACE leaves -2 behind, WorldObject_Decay.cs:95) is found and not set: setting it would only lengthen its life.</para>
/// </remarks>
internal static class CorpseRotRules
{
    /// <summary>The start of a player corpse's name (Creature_Death.cs:496: "{prefix} of {Name}" with the prefix "Corpse" for a non-treasure corpse).</summary>
    public const string NamePrefix = "Corpse of ";

    /// <summary>ACE's marker for an object that never rots (WorldObject_Properties.cs:2767).</summary>
    public const double NeverRots = -1;

    /// <summary>
    /// True when a corpse is one of the caller's own character's player corpses.
    /// </summary>
    /// <param name="corpseName">The corpse's name.</param>
    /// <param name="isPlayerCorpse">Whether it is a player's corpse (<see cref="CorpseCandidate.IsPlayerCorpse"/>).</param>
    /// <param name="callerName">The name of the character the caller is playing.</param>
    public static bool Matches(string? corpseName, bool isPlayerCorpse, string? callerName) =>
        isPlayerCorpse
        && !string.IsNullOrEmpty(callerName)
        && string.Equals(corpseName, NamePrefix + callerName, StringComparison.Ordinal);

    /// <summary>
    /// True when a matching corpse with this time to rot is to be set to <paramref name="emptyDecayTime"/>: it is unset, never rots, or has more
    /// than that left. False when it has at most that left, which includes every expired or decayed value except -1.
    /// </summary>
    /// <param name="timeToRot">The corpse's time to rot.</param>
    /// <param name="emptyDecayTime">ACE's <c>Corpse.EmptyDecayTime</c>.</param>
    public static bool NeedsShortening(double? timeToRot, double emptyDecayTime) =>
        timeToRot is null || timeToRot == NeverRots || timeToRot > emptyDecayTime;

    /// <summary>
    /// Decides over a landblock's corpses: how many belong to the caller and which of those are to be set.
    /// </summary>
    /// <param name="corpses">The corpses in the landblock, in any order.</param>
    /// <param name="callerName">The name of the character the caller is playing.</param>
    /// <param name="emptyDecayTime">ACE's <c>Corpse.EmptyDecayTime</c> (15 seconds).</param>
    public static CorpseRotPlan Plan(IEnumerable<CorpseCandidate> corpses, string? callerName, double emptyDecayTime)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        var found = 0;
        var set = new List<int>();
        var index = 0;
        foreach (var corpse in corpses)
        {
            if (Matches(corpse.Name, corpse.IsPlayerCorpse, callerName))
            {
                found++;
                if (NeedsShortening(corpse.TimeToRot, emptyDecayTime))
                {
                    set.Add(index);
                }
            }

            index++;
        }

        return new CorpseRotPlan(found, set);
    }
}
