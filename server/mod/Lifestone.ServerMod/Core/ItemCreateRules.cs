using System.Globalization;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// The decisions of <c>@lifestone item</c> that need nothing from ACE, so Lifestone.ServerMod.Tests runs them
/// (ItemCreateRulesTests): which weenies are refused and how the stack size is settled. ItemVerb feeds them ACE's facts.
/// </summary>
/// <remarks>
/// The rules are those of ACE's <c>@ci</c> (AdminCommands.HandleCI, :2502-2559): a weenie that GetWeenieForCreate refuses
/// (not found, a type VerifyCreateWeenieType never spawns, a stuck weenie when it is for an inventory) is refused here with its own code
/// instead of a chat line, and the stack size is clamped to the item's MaxStackSize when it has one and otherwise left out (HandleCI
/// sets it only when MaxStackSize is not null).
/// </remarks>
internal static class ItemCreateRules
{
    /// <summary>
    /// The refusal for a weenie, or null when it may be created in a pack.
    /// </summary>
    /// <param name="wcid">The requested class id.</param>
    /// <param name="found">Whether the world database has the weenie.</param>
    /// <param name="typeAllowed">ACE's AdminCommands.VerifyCreateWeenieType for its type.</param>
    /// <param name="typeName">The weenie type's name, for the sentence.</param>
    /// <param name="stuck">The weenie's Stuck property: it cannot be picked up, so it cannot go in a pack.</param>
    public static ToolError? Refusal(uint wcid, bool found, bool typeAllowed, string typeName, bool stuck)
    {
        if (!found)
        {
            return new ToolError(ToolErrors.UnknownWcid, "no weenie has class id " + wcid.ToString(CultureInfo.InvariantCulture));
        }

        if (!typeAllowed)
        {
            return new ToolError(ToolErrors.NotCreatable, "weenie " + wcid.ToString(CultureInfo.InvariantCulture) + " is a " + typeName + ", which ACE never spawns");
        }

        return stuck
            ? new ToolError(ToolErrors.NotCreatable, "weenie " + wcid.ToString(CultureInfo.InvariantCulture) + " cannot be picked up, so it cannot go in your pack")
            : null;
    }

    /// <summary>
    /// The stack size to create with, and whether it is below the request: <paramref name="requested"/> clamped to
    /// <paramref name="maxStackSize"/> when the item stacks, and ignored (<paramref name="weenieStack"/> or 1) when it does not.
    /// </summary>
    /// <param name="requested">The stack asked for, or null.</param>
    /// <param name="maxStackSize">The item's MaxStackSize, or null when it does not stack.</param>
    /// <param name="weenieStack">The stack size the new object already has (the weenie's), or null.</param>
    public static (int Stack, bool Clamped) ResolveStack(int? requested, int? maxStackSize, int? weenieStack)
    {
        var stack = requested is { } asked && maxStackSize is { } max ? Math.Min(asked, max) : weenieStack ?? 1;
        return (stack, requested is { } r && stack < r);
    }
}
