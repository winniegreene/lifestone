using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Housing;

/// <summary>The two panes of the guardian window (retail <c>gmSlumlordUI</c>, class 0x10000013).</summary>
public enum GuardianPane
{
    /// <summary>The purchase pane: what the dwelling costs, and the stacks placed to pay it.</summary>
    Buy,

    /// <summary>The rent pane: this period's maintenance, what is paid, and the stacks placed to pay it.</summary>
    Rent,
}

/// <summary>How one price line stands in a pane: what it asks, what the guardian already holds, what the placed stacks bring.</summary>
/// <param name="Line">The price line.</param>
/// <param name="Placed">What the stacks placed for this line bring (coins and trade-note value for the money line, a count otherwise).</param>
public readonly record struct PaneLine(HousePayment Line, long Placed)
{
    /// <summary>What is still missing once the placed stacks are counted (0 when the line is covered).</summary>
    public long Missing => Math.Max(0, Line.Remaining - Placed);

    /// <summary>True when the line needs nothing more.</summary>
    public bool Covered => Missing == 0;
}

/// <summary>
/// The guardian window's state, one per session (M6 villa): the stacks the player has dragged into the purchase pane and the rent pane, the
/// window's own error line, and the Buy and Pay buttons. The drawn window (<c>gmSlumlordUI</c>) shows this model and the autopilot uses it, so
/// a purchase or a payment always goes the way a player's does: open the guardian, drag the stacks in, press the button (the owner's player
/// rules, 2026-11-11).
/// </summary>
/// <remarks>
/// <para><b>What a pane takes.</b> A stack the player holds (not worn) whose weenie class matches a line still owed: pyreals (273) or a trade
/// note (ItemType PromissoryNote) for the money line, the item's class for the others. That is what ACE counts (<c>Player_House.HasItems</c>,
/// <c>HousePayment.GetConsumeItems</c>): ACE takes from the placed stacks only what each line needs, smallest stacks first. Retail's own drop
/// rules and error texts (<c>gmSlumlordUI</c>'s handlers) are unread: the window's lines here are Lifestone's (<c>TODO(villa-retail-slumlord-drop)</c>).</para>
/// <para><b>Check, act, check.</b> <see cref="PressBuy"/> and <see cref="PressPay"/> check before sending (a guardian open, the pane covering
/// every line, no house owned for a purchase, rent owed for a payment) and leave the after-check to the caller, which reads the tracker's
/// outcome and then <see cref="GameState.House"/>.</para>
/// </remarks>
public sealed class GuardianWindowModel
{
    private readonly GameState _state;
    private readonly IActionApi _actions;
    private readonly List<uint> _buy = [];
    private readonly List<uint> _rent = [];
    private readonly List<uint> _scratch = [];
    private uint _guardianOfPanes;

    /// <summary>The model over a session's state and actions.</summary>
    public GuardianWindowModel(GameState state, IActionApi actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        _state = state;
        _actions = actions;
    }

    /// <summary>The open guardian, or null (the window is closed).</summary>
    public HouseGuardian? Guardian => _state.Guardian;

    /// <summary>The window's own error line (what the last refused drop or press said), or null.</summary>
    public string? ErrorLine { get; private set; }

    /// <summary>The stacks in <paramref name="pane"/>, in the order they were placed.</summary>
    public IReadOnlyList<uint> Placed(GuardianPane pane)
    {
        Sync();
        return pane == GuardianPane.Buy ? _buy : _rent;
    }

    /// <summary>Each line of <paramref name="pane"/> with what the placed stacks bring (empty when no guardian is open).</summary>
    public ImmutableArray<PaneLine> Lines(GuardianPane pane)
    {
        Sync();
        if (_state.Guardian is not { } guardian)
        {
            return [];
        }

        var lines = pane == GuardianPane.Buy ? guardian.Buy : guardian.Rent;
        var placed = pane == GuardianPane.Buy ? _buy : _rent;
        var result = ImmutableArray.CreateBuilder<PaneLine>(lines.Length);
        foreach (var line in lines)
        {
            long brought = 0;
            foreach (var guid in placed)
            {
                brought += Brings(line, guid);
            }

            result.Add(new PaneLine(line, brought));
        }

        return result.MoveToImmutable();
    }

    /// <summary>True when every line of <paramref name="pane"/> is covered by the guardian's holdings plus the placed stacks.</summary>
    public bool Covers(GuardianPane pane) => _state.Guardian is not null && Lines(pane).All(static l => l.Covered);

    /// <summary>
    /// Drags <paramref name="item"/> into <paramref name="pane"/>. Refused (false, with <see cref="ErrorLine"/>) when no guardian is open, the
    /// item is not held or is worn, it is already placed, or it pays no line that is still owed.
    /// </summary>
    public bool Place(GuardianPane pane, uint item)
    {
        Sync();
        if (_state.Guardian is not { } guardian)
        {
            return Refuse("No guardian is open.");
        }

        var placed = pane == GuardianPane.Buy ? _buy : _rent;
        if (placed.Contains(item))
        {
            return Refuse("That is already in the window.");
        }

        if (!_state.Self.Inventory.Contains(item) || _state.Self.Inventory.TryGetEquipped(item, out _))
        {
            return Refuse("You can only place items from your packs.");
        }

        var lines = pane == GuardianPane.Buy ? guardian.Buy : guardian.Rent;
        foreach (var line in Lines(pane))
        {
            if (!line.Covered && Brings(line.Line, item) > 0)
            {
                placed.Add(item);
                ErrorLine = null;
                return true;
            }
        }

        return Refuse(lines.Any(l => Brings(l, item) > 0) ? "That line is already paid." : "The guardian does not ask for that.");
    }

    /// <summary>Takes <paramref name="item"/> back out of <paramref name="pane"/>.</summary>
    public bool Remove(GuardianPane pane, uint item)
    {
        Sync();
        return (pane == GuardianPane.Buy ? _buy : _rent).Remove(item);
    }

    /// <summary>Empties both panes.</summary>
    public void Clear()
    {
        _buy.Clear();
        _rent.Clear();
        ErrorLine = null;
    }

    /// <summary>
    /// Drags in, line by line, the stacks a player would pick to pay <paramref name="pane"/>: for each line still owed, held stacks of its
    /// class, largest first, until the line is covered (pyreal stacks before trade notes, smallest note first, for the money line). Returns
    /// true when the pane then covers every line; otherwise <see cref="ErrorLine"/> names the first line short and by how much.
    /// </summary>
    public bool FillFromPacks(GuardianPane pane)
    {
        Sync();
        if (_state.Guardian is null)
        {
            return Refuse("No guardian is open.");
        }

        GearInventory.Collect(_state, _scratch);
        foreach (var line in Lines(pane))
        {
            if (line.Covered)
            {
                continue;
            }

            var missing = line.Missing;
            foreach (var guid in Candidates(line.Line))
            {
                if (missing <= 0)
                {
                    break;
                }

                if (Place(pane, guid))
                {
                    missing -= Brings(line.Line, guid);
                }
            }
        }

        foreach (var line in Lines(pane))
        {
            if (!line.Covered)
            {
                return Refuse("You need " + line.Missing.ToString("N0", CultureInfo.InvariantCulture) + " more " + (line.Missing == 1 ? line.Line.Name : line.Line.PluralName) + ".");
            }
        }

        ErrorLine = null;
        return true;
    }

    /// <summary>
    /// The Buy button: checks a guardian is open, the dwelling is unowned, the player owns no house and the purchase pane covers the price,
    /// then sends BuyHouse with the pane's stacks. Null (with <see cref="ErrorLine"/>) when a check refused.
    /// </summary>
    public ActionHandle? PressBuy(ActionSource source)
    {
        Sync();
        if (_state.Guardian is not { } guardian)
        {
            return RefuseHandle("No guardian is open.");
        }

        if (guardian.IsOwned)
        {
            return RefuseHandle("This dwelling is owned by " + (guardian.OwnerName.Length > 0 ? guardian.OwnerName : "someone") + ".");
        }

        if (_state.HouseStatus == HouseOwnership.Owned)
        {
            return RefuseHandle("You already own a house.");
        }

        if (!Covers(GuardianPane.Buy))
        {
            return RefuseHandle("The purchase pane does not cover the price.");
        }

        ErrorLine = null;
        return _actions.BuyHouse(guardian.Guardian, [.. _buy], source);
    }

    /// <summary>The Pay button: checks a guardian is open, the dwelling is owned (ACE lets anyone pay an owned house's rent), rent is owed and the rent pane is not empty, then sends RentHouse with the pane's stacks.</summary>
    public ActionHandle? PressPay(ActionSource source)
    {
        Sync();
        if (_state.Guardian is not { } guardian)
        {
            return RefuseHandle("No guardian is open.");
        }

        if (!guardian.IsOwned)
        {
            return RefuseHandle("This dwelling has no owner: there is no maintenance to pay.");
        }

        if (guardian.IsRentPaid)
        {
            return RefuseHandle("The maintenance is already paid for this period.");
        }

        if (_rent.Count == 0)
        {
            return RefuseHandle("Place the maintenance in the rent pane first.");
        }

        ErrorLine = null;
        return _actions.PayRent(guardian.Guardian, [.. _rent], source);
    }

    /// <summary>What <paramref name="item"/> brings to <paramref name="line"/>: coins or a note's value for the money line, the stack for a matching class, else 0.</summary>
    internal long Brings(HousePayment line, uint item)
    {
        if (!_state.Objects.TryGet(item, out var obj))
        {
            return 0;
        }

        if (obj.Weenie.WeenieClassId == line.WeenieClassId)
        {
            return GearInventory.StackOf(_state, obj);
        }

        if (line.IsMoney && (obj.ItemType & VendorStock.PromissoryNoteType) != 0)
        {
            return _state.Self.Inventory.TryGetStack(item, out _, out var value) ? value : obj.Weenie.Value ?? 0;
        }

        return 0;
    }

    private IEnumerable<uint> Candidates(HousePayment line)
    {
        var items = new List<(uint Guid, long Brings, int Order)>();
        foreach (var guid in _scratch)
        {
            if (_state.Self.Inventory.TryGetEquipped(guid, out _))
            {
                continue;
            }

            var brings = Brings(line, guid);
            if (brings > 0)
            {
                var isCoin = _state.Objects.TryGet(guid, out var obj) && obj.Weenie.WeenieClassId == line.WeenieClassId;
                items.Add((guid, brings, isCoin ? 0 : 1));
            }
        }

        // Coins (or the line's own items) largest first, then trade notes smallest first.
        return items.OrderBy(static i => i.Order).ThenBy(static i => i.Order == 0 ? -i.Brings : i.Brings).Select(static i => i.Guid);
    }

    /// <summary>The panes belong to one guardian: another (or none) empties them.</summary>
    private void Sync()
    {
        var open = _state.Guardian?.Guardian ?? 0;
        if (open != _guardianOfPanes)
        {
            _buy.Clear();
            _rent.Clear();
            ErrorLine = null;
            _guardianOfPanes = open;
        }

        _buy.RemoveAll(g => !_state.Self.Inventory.Contains(g));
        _rent.RemoveAll(g => !_state.Self.Inventory.Contains(g));
    }

    private bool Refuse(string line)
    {
        ErrorLine = line;
        return false;
    }

    private ActionHandle? RefuseHandle(string line)
    {
        ErrorLine = line;
        return null;
    }
}
