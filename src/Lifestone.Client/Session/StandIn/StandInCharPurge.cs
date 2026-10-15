using Lifestone.Client.Session.Contracts;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.StandIn;

/// <summary>What <see cref="StandInCharPurge.Decide"/> decided: the refusal, or the slot to purge.</summary>
/// <param name="Refusal">Why the purge is refused, or null.</param>
/// <param name="Target">The character to purge when there is no refusal.</param>
internal readonly record struct StandInPurgeDecision(ToolError? Refusal, CharacterSlot Target);

/// <summary>
/// The offline stand-in's answer to <c>char purge</c>: the server mod's rules (server/mod/Lifestone.ServerMod/Core/CharPurgeRules.cs, owner
/// decision D59) over the stand-in's character list, in the same order and with the same codes and sentences. The mod's file is not part of
/// this assembly (only server/mod/Shared is), so the two are kept equal by a test that runs one table through both
/// (Lifestone.ServerMod.Tests, CharPurgeRulesTests). The stand-in has one character in the world, the player's own; no other is online.
/// </summary>
internal static class StandInCharPurge
{
    /// <summary>The graduated test characters, which a purge never touches.</summary>
    internal static IReadOnlyList<string> Graduated { get; } = ["Lifestone Smoke", "Lifestone Loopback"];

    /// <summary>Applies the rules to the stand-in's list: refuses, or picks the pending-deletion character of that name.</summary>
    internal static StandInPurgeDecision Decide(CharPurgeRequest request, string selfName, uint selfGuid, IReadOnlyList<CharacterSlot> characters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(characters);
        foreach (var graduated in Graduated)
        {
            if (Same(graduated, request.Name))
            {
                return Refuse(ToolErrors.Graduated, graduated + " is a graduated test character and stays as it is");
            }
        }

        var own = "you are playing " + selfName + "; log in as another character to purge it";
        if (Same(selfName, request.Name))
        {
            return Refuse(ToolErrors.OwnCharacter, own);
        }

        var target = characters
            .Where(c => Same(c.Name, request.Name))
            .OrderByDescending(c => c.PendingDeletion) // the list shows only visible pending characters (no expired IsDeleted rows), so: pending, then
            .ThenByDescending(c => c.DeleteTimeout)     // the latest DeleteTime (the longest delay left), then the lowest id (the mod's PurgeRank order)
            .ThenBy(c => c.Guid)
            .Cast<CharacterSlot?>()
            .FirstOrDefault();
        if (target is not { } slot)
        {
            return Refuse(ToolErrors.UnknownCharacter, "no character named \"" + request.Name + "\" is on your account");
        }

        if (slot.Guid == selfGuid)
        {
            return Refuse(ToolErrors.OwnCharacter, own);
        }

        return slot.PendingDeletion
            ? new StandInPurgeDecision(null, slot)
            : Refuse(ToolErrors.NotPendingDeletion, slot.Name + " is not marked for deletion; delete it on the character screen first");
    }

    private static StandInPurgeDecision Refuse(string code, string message) => new(new ToolError(code, message), default);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
