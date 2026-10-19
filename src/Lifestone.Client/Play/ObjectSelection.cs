using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Play;

/// <summary>
/// Where an object selection is on the screen (m2-plan §4.13; docs/avatars.md §18). The in-game shell's vivid target indicator
/// and the inspector read it; the game implements it over the world camera. Rectangles are in logical UI pixels
/// (<see cref="ScreenRect"/>), the shell's own and not the engine's rectangle type, so the engine-agnostic screens can use it.
/// </summary>
public interface ISelectionProjector
{
    /// <summary>
    /// Projects the object's selection sphere (the Setup's <c>selection_sphere</c>, scaled by <c>CPartArray::GetSelectionSphere</c>) to the
    /// screen: retail uses that sphere for the vivid target indicator's box and for nothing else; picking tests the parts' drawing spheres and
    /// polygons (<see cref="RayPicking"/>). Returns false when the object is not known or not drawn. When it returns
    /// true, <paramref name="onScreen"/> is false if the object is behind the camera (the rectangle is then empty) or entirely outside the view.
    /// </summary>
    bool TryProject(uint guid, out ScreenRect rect, out bool onScreen);
}

/// <summary>
/// The selected object (M2 G5 W7): one guid, or none. A left click on the world selects (the player controller), a click on empty
/// space clears, and the rule for losing it is below. <see cref="Changed"/> tells the shell and the inspector.
/// </summary>
/// <remarks>
/// <para><b>When the selection clears.</b> The selected object is deleted (<see cref="ObjectDeleted"/>, the reducer event of
/// <c>SmartBox::HandleDeleteObject</c>), a player teleport is announced (<see cref="PlayerTeleportAnnounced"/>: the world around the
/// player is replaced) or the session is logged off (the host calls <see cref="Clear"/> when it renews the avatar set). This is the rule
/// this port uses; retail clearing code for the selected object is not among the researched addresses, so a difference found against
/// retail is a fix here.</para>
/// <para>The player itself is never selected: the picking host leaves it out of the targets.</para>
/// </remarks>
public sealed class ObjectSelection : IDisposable
{
    private readonly SubscriptionSet _subscriptions = new();

    /// <summary>The selected object, or 0 for none.</summary>
    public uint Selected { get; private set; }

    /// <summary>True when an object is selected.</summary>
    public bool HasSelection => Selected != 0;

    /// <summary>Raised after the selection changed: (previous, current), 0 for none.</summary>
    public event Action<uint, uint>? Changed;

    /// <summary>Selects <paramref name="guid"/> (0 clears). Returns true when the selection changed.</summary>
    public bool Select(uint guid)
    {
        if (guid == Selected)
        {
            return false;
        }

        var previous = Selected;
        Selected = guid;
        Changed?.Invoke(previous, guid);
        return true;
    }

    /// <summary>Clears the selection. Returns true when there was one.</summary>
    public bool Clear() => Select(0);

    /// <summary>Follows the session: the selected object delete and a teleport clear it. Dispose the selection to unsubscribe.</summary>
    public void Attach(IEventBus events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _subscriptions.Add(events.Subscribe<ObjectDeleted>(OnDeleted));
        _subscriptions.Add(events.Subscribe<PlayerTeleportAnnounced>(OnTeleport));
        _subscriptions.Add(events.Subscribe<AutoTargetRequested>(OnAutoTarget));
    }

    /// <inheritdoc/>
    public void Dispose() => _subscriptions.Dispose();

    private void OnDeleted(in ObjectDeleted e)
    {
        if (e.Guid == Selected)
        {
            Clear();
        }
    }

    private void OnTeleport(in PlayerTeleportAnnounced e) => Clear();

    // ClientCombatSystem::AutoTarget (11.6096 0x0056C9D0): the attacker is selected, only when nothing is (the handlers' own test of ACCWeenieObject::selectedID).
    private void OnAutoTarget(in AutoTargetRequested e)
    {
        if (Selected == 0 && e.Attacker != 0)
        {
            Select(e.Attacker);
        }
    }
}
