// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/AdminCommands.cs HandleCI, GetWeenieForCreate,
// VerifyCreateWeenieType and CreateObjectForCommand (AGPL-3.0) with changes: the call sequence only (the cached weenie, the
// weenie-type and stuck checks, WorldObjectFactory.CreateNewWorldObject, SetStackSize, TryCreateInInventoryWithNetworking), at
// Player level for the requesting player's own pack; the weenie is given by class id only; each refusal has its own error code
// instead of a chat line; no world position is set or remembered (CreateObjectForCommand's InFrontOf and LastSpawnPos belong to
// an object that enters the world, and this one never does); the palette and shade are applied as @ci does (AdminCommands.cs:2559-2563) when the request names them; a pack that has no room is an answer, and the
// object is destroyed to release its guid (Tailoring.Finalize does the same, Tailoring.cs:333-337) where @ci ignores the result;
// no audit-channel broadcast; one machine-readable reply.
using ACE.Database;
using ACE.Server.Command.Handlers;
using ACE.Server.Factories;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary><c>@lifestone item &lt;wcid&gt; [&lt;stack&gt;] [palette=&lt;n&gt;] [shade=&lt;f&gt;]</c>: creates an item in the player's own pack (<see cref="ItemData"/>).</summary>
/// <remarks>
/// <para>ACE's <c>@ci</c> (AdminCommands.cs:2502-2559) looks the weenie up in the world database's cache, refuses a type
/// VerifyCreateWeenieType never spawns and a stuck weenie (GetWeenieForCreate, :2282-2310), creates the object, sets its stack size to
/// the request clamped to MaxStackSize when the item has one (a stack for an item without one is ignored), and calls
/// TryCreateInInventoryWithNetworking (Player_Inventory.cs:90-115), which adds it to the main pack or the first side pack with room and
/// tells the client. The mod does the same, answers each refusal with its own code (<see cref="ItemCreateRules"/>, unit tested), and
/// says in the reply what was created and whether the stack was clamped (<see cref="ItemData.Clamped"/>): HandleCI clamps without a
/// word, and a script that asked for 100 of a thing that holds 50 must be told.</para>
/// <para>The command runs on ACE's world thread, as <c>@ci</c> does. The weenie lookup is the cache's (a first lookup may query the world
/// database, a read). A creature weenie passes <c>@ci</c>'s check and so passes here too; TODO(item-creature): refuse weenies that are
/// creatures if a test ever asks for one.</para>
/// </remarks>
internal static class ItemVerb
{
    /// <summary>Creates the item for <paramref name="player"/> and answers once with what was created, or why not.</summary>
    public static void Run(ToolCall call, Player player, ItemRequest request)
    {
        if (player.IsLoggingOut)
        {
            call.Fail(ToolErrors.Busy, "you are logging out");
            return;
        }

        var weenie = DatabaseManager.World.GetCachedWeenie(request.Wcid);
        var refusal = ItemCreateRules.Refusal(
            request.Wcid,
            weenie is not null,
            weenie is not null && AdminCommands.VerifyCreateWeenieType(weenie.WeenieType),
            weenie?.WeenieType.ToString() ?? string.Empty,
            weenie is not null && ACE.Entity.Models.WeenieExtensions.IsStuck(weenie));
        if (refusal is not null)
        {
            call.Fail(refusal);
            return;
        }

        var item = WorldObjectFactory.CreateNewWorldObject(weenie!);
        if (item is null)
        {
            ModManager.Log("Lifestone tool failed: verb=item: ACE made no object for weenie " + request.Wcid, ModManager.LogLevel.Error);
            call.Fail(ToolErrors.Failed, "ACE could not make an object for weenie " + request.Wcid);
            return;
        }

        if (request.Stack is { } asked && item.MaxStackSize is { } max)
        {
            item.SetStackSize(ItemCreateRules.ResolveStack(asked, max, item.StackSize).Stack);
        }

        // @ci's order: the stack, then the palette and shade, then the pack (AdminCommands.cs:2552-2563). The parser has range-checked both.
        if (request.Palette is { } palette)
        {
            item.PaletteTemplate = palette;
        }

        if (request.Shade is { } shade)
        {
            item.Shade = shade;
        }

        // Read before the pack takes it: the stack the object really has (SetStackSize ignores an object that is not a Stackable).
        var actual = item.StackSize ?? 1;
        var data = new ItemData(item.Guid.Full, request.Wcid, item.Name ?? string.Empty, actual, request.Stack, item.MaxStackSize, request.Stack is { } r && actual < r,
            request.Palette is null ? null : item.PaletteTemplate, request.Shade is null ? null : item.Shade);
        if (!player.TryCreateInInventoryWithNetworking(item))
        {
            item.Destroy();
            call.Fail(ToolErrors.NoRoom, "your pack has no room for " + data.Name + "; make space or lighten your load");
            return;
        }

        call.Succeed(request, data, data.Describe());
    }
}
