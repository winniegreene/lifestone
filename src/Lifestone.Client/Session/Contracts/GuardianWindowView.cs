using System.Collections.Immutable;
using Lifestone.Client.Session.Housing;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>The two panes of the guardian window as the state contract names them (<see cref="GuardianWindowView"/>; the model's own enum is <see cref="GuardianPane"/>).</summary>
public enum GuardianWindowPane
{
    /// <summary>The purchase pane.</summary>
    Buy,

    /// <summary>The rent (maintenance) pane.</summary>
    Rent,
}

/// <summary>One price line of a guardian window pane, in the contract's own terms (<see cref="GuardianWindowView.Lines"/>).</summary>
/// <param name="Required">How many the line asks for (pyreals for the money line).</param>
/// <param name="Paid">How many the guardian already holds this period.</param>
/// <param name="Placed">What the stacks placed in the pane bring to it.</param>
/// <param name="Missing">What is still short once the placed stacks are counted.</param>
/// <param name="WeenieClassId">The weenie class asked for (273 for pyreals).</param>
/// <param name="Name">The item's name.</param>
/// <param name="PluralName">The item's plural name.</param>
public readonly record struct GuardianPriceLine(long Required, long Paid, long Placed, long Missing, uint WeenieClassId, string Name, string PluralName)
{
    /// <summary>The name for <paramref name="count"/> of the item (the plural for any count but one, when the line names one).</summary>
    public string NameFor(long count) => count == 1 || PluralName.Length == 0 ? Name : PluralName;
}

/// <summary>
/// The guardian window as the retail UI reads it (M6 villa): the open guardian (<see cref="GameState.Guardian"/>) and the guardian window's model
/// (<see cref="GuardianWindowModel"/>, the panes the drawn <c>gmSlumlordUI</c> and the autopilot share) in the state contract's terms, so the window
/// names neither the session's housing types nor the wire's (the retail UI's architecture rule). It keeps no state of its own: every read and every
/// call goes to the state or the model. A view without a model (<see cref="HasModel"/> false) reads the profile alone and refuses every change.
/// </summary>
public sealed class GuardianWindowView
{
    private readonly GameState _state;
    private readonly GuardianWindowModel? _model;

    /// <summary>The view over <paramref name="state"/> and the session's <paramref name="model"/> (null: read-only).</summary>
    public GuardianWindowView(GameState state, GuardianWindowModel? model)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _model = model;
    }

    /// <summary>True when the view forwards to a model (places, removes and presses act).</summary>
    public bool HasModel => _model is not null;

    /// <summary>True while a guardian is open.</summary>
    public bool IsOpen => _state.Guardian is not null;

    /// <summary>The open guardian's guid (0 when none).</summary>
    public uint Guardian => _state.Guardian?.Guardian ?? 0;

    /// <summary>True when someone owns the dwelling.</summary>
    public bool IsOwned => _state.Guardian?.IsOwned ?? false;

    /// <summary>The owner's name (empty when unowned).</summary>
    public string OwnerName => _state.Guardian?.OwnerName ?? string.Empty;

    /// <summary>The lowest level that may buy (-1 for none).</summary>
    public int MinLevel => _state.Guardian?.MinLevel ?? -1;

    /// <summary>True when no maintenance is due this period (the profile's maintenance-free flag).</summary>
    public bool MaintenanceFree => _state.Guardian?.Profile.MaintenanceFree ?? false;

    /// <summary>True when no rent is owed this period (<see cref="HouseGuardian.IsRentPaid"/>).</summary>
    public bool IsRentPaid => _state.Guardian?.IsRentPaid ?? false;

    /// <summary>The name of the dwelling's kind ("Dwelling" for a kind ACE does not name; empty when none is open).</summary>
    public string KindName => _state.Guardian is not { } guardian ? string.Empty : guardian.Type switch
    {
        HouseType.Apartment => "Apartment",
        HouseType.Cottage => "Cottage",
        HouseType.Villa => "Villa",
        HouseType.Mansion => "Mansion",
        _ => "Dwelling",
    };

    /// <summary>The model's error line (what the last refused drop or press said), or null.</summary>
    public string? ErrorLine => _model?.ErrorLine;

    /// <summary>Each price line of <paramref name="pane"/> with what the placed stacks bring (<see cref="GuardianWindowModel.Lines"/>; without a model, the profile's lines with nothing placed).</summary>
    public ImmutableArray<GuardianPriceLine> Lines(GuardianWindowPane pane)
    {
        if (_state.Guardian is not { } guardian)
        {
            return [];
        }

        if (_model is not null)
        {
            var lines = _model.Lines(Of(pane));
            var result = ImmutableArray.CreateBuilder<GuardianPriceLine>(lines.Length);
            foreach (var line in lines)
            {
                result.Add(Line(line.Line, line.Placed, line.Missing));
            }

            return result.MoveToImmutable();
        }

        var profile = pane == GuardianWindowPane.Buy ? guardian.Buy : guardian.Rent;
        var plain = ImmutableArray.CreateBuilder<GuardianPriceLine>(profile.Length);
        foreach (var line in profile)
        {
            plain.Add(Line(line, 0, line.Remaining));
        }

        return plain.MoveToImmutable();
    }

    /// <summary>The stacks placed in <paramref name="pane"/>, in the order they were placed (empty without a model).</summary>
    public IReadOnlyList<uint> Placed(GuardianWindowPane pane) => _model?.Placed(Of(pane)) ?? [];

    /// <summary>True when <paramref name="pane"/> covers every line (<see cref="GuardianWindowModel.Covers"/>).</summary>
    public bool Covers(GuardianWindowPane pane) => _model?.Covers(Of(pane)) ?? false;

    /// <summary>A drag of <paramref name="item"/> into <paramref name="pane"/> (<see cref="GuardianWindowModel.Place"/>).</summary>
    public bool Place(GuardianWindowPane pane, uint item) => _model?.Place(Of(pane), item) ?? false;

    /// <summary>Takes <paramref name="item"/> back out of <paramref name="pane"/> (<see cref="GuardianWindowModel.Remove"/>).</summary>
    public bool Remove(GuardianWindowPane pane, uint item) => _model?.Remove(Of(pane), item) ?? false;

    /// <summary>The Buy button (<see cref="GuardianWindowModel.PressBuy"/>).</summary>
    public ActionHandle? PressBuy(ActionSource source) => _model?.PressBuy(source);

    /// <summary>The Pay button (<see cref="GuardianWindowModel.PressPay"/>).</summary>
    public ActionHandle? PressPay(ActionSource source) => _model?.PressPay(source);

    private static GuardianPane Of(GuardianWindowPane pane) => pane == GuardianWindowPane.Rent ? GuardianPane.Rent : GuardianPane.Buy;

    private static GuardianPriceLine Line(HousePayment line, long placed, long missing) =>
        new(line.Required, line.Paid, placed, missing, line.WeenieClassId, line.Name, line.PluralName);
}
