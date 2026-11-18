using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.UseRecipe"/> (M6 item 8): GameAction 0x0035 UseWithTarget {source, target}, retail <c>CM_Inventory::Event_UseWithTargetEvent</c> (11.6096 0x006AD3E0), for a recipe. The healing kit's tracker misreads one: it confirms only a Structure that fell or the item's removal, so a recipe whose
/// source stays (6068) and a crafting dialog's early UseDone 0 (@95ef426d <c>Managers/RecipeManager.cs:351</c>) are its "nothing happened". This one says only how the server ended the use; what the recipe did is <see cref="RecipeOutcomes.Read"/>'s, from the items.
/// <para>ACE plays a clap, runs <c>HandleRecipe</c> and ends with the UseDone in the action that clears its busy flag (<c>RecipeManager.cs:135-143</c>), so nothing is held after it. Lane <see cref="ActionLane.Heal"/>, as <see cref="ManaStoneTracker"/>.</para>
/// </summary>
internal sealed class RecipeTracker(ActionHandle handle, uint sourceItem, uint targetItem) : ActionTracker(handle, ActionLane.Heal, ActionPolicy.HealSeconds)
{
    private bool _useDoneSeen;
    private uint _refusalCode;
    private string? _refusal;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0035 UseWithTarget (recipe) " + ActionText.Hex(sourceItem) + " on " + ActionText.Hex(targetItem);

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    /// <remarks>M5 D141's before-check: in the world, and both items held (ACE answers an item it cannot find with a bare UseDone and nothing else, @95ef426d <c>WorldObjects/Player_Use.cs:42-56</c>).</remarks>
    internal override string? Refusal(ActionApi api)
    {
        if (WorldTrackers.NotInWorld(api) is { } notInWorld)
        {
            return notInWorld;
        }

        var inventory = api.State.Self.Inventory;
        if (!inventory.Find(sourceItem).Held)
        {
            return Refuse(ActionReason.NotHeld, "the recipe's source " + ActionText.Hex(sourceItem) + " is not held by the player");
        }

        return inventory.Find(targetItem).Held ? null : Refuse(ActionReason.NotHeld, "the recipe's target " + ActionText.Hex(targetItem) + " is not held by the player");
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _useDoneSeen = false;
        _refusalCode = 0;
        _refusal = null;
        session!.SendUseWithTarget(sourceItem, targetItem);
        return null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        if (_useDoneSeen)
        {
            return false;
        }

        if (line.Kind == ChatKind.WeenieError && line.ChatType != 0)
        {
            _refusalCode = line.ChatType;
            _refusal = WeenieErrorText.TryFormat(line.ChatType, line.Text, out _, out var text) ? text : line.Text;
        }
        else if (RecipeOutcomes.OfLine(line) is not null)
        {
            _refusal = line.Text;
        }

        return false; // an observer: the reader and the chat read the same line
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        if (_useDoneSeen)
        {
            return false;
        }

        _useDoneSeen = true;
        Resolve(Settle(e.WeenieError));
        return true;
    }

    private ActionOutcome Settle(uint code)
    {
        if (code != 0)
        {
            return new ActionOutcome(ActionOutcomeKind.ServerError, code, "the server refused the recipe (WeenieError 0x" + code.ToString("X4", CultureInfo.InvariantCulture) + (_refusal is null ? string.Empty : ": " + _refusal) + ")", ActionReasons.FromCode(code));
        }

        return _refusal is not null
            ? new ActionOutcome(ActionOutcomeKind.ServerError, _refusalCode, "the server refused the recipe (" + _refusal + ")", _refusalCode != 0 ? ActionReasons.FromCode(_refusalCode) : ActionReason.NoEffect)
            : new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "UseDone 0: the use ended; what it made is the recipe reader's to say");
    }
}
