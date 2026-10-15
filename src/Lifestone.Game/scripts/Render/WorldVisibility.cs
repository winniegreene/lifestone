using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>
/// The arbiter of whether the 3D world is drawn: the <b>sole writer</b> of the main viewport's <c>Disable3D</c>. Anything
/// that covers the world asks it (the portal tunnel while state 3 holds, a full-screen retail UI screen, the play mode's
/// loading states) and the world is hidden while at least one request is held (<see cref="WorldHideRequests"/>), so
/// no owner can show the world under another's cover and none needs to know the others exist.
/// </summary>
/// <remarks>
/// <para><b>What hiding does.</b> <c>Disable3D</c> on the main viewport skips its 3D pass: no terrain, objects, interiors
/// or sky are drawn, the 2D layers (the UI, the SubViewport textures the presenter shows) are. The world keeps
/// <i>streaming</i>: hiding is a draw switch, not an unload (portal space shows the tunnel while the destination loads).
/// A <c>SubViewport</c> keeps rendering whatever the main viewport does (pinned by <c>--portal-space-checks</c>), so the
/// tunnel's view is unaffected; the sky's own view is paused with the world (<c>SkyView.Paused</c>, wired by the
/// sky controller through <see cref="Changed"/>).</para>
/// <para><b>Use.</b> A request is held per owner: <see cref="Hide"/> and <see cref="Unhide"/> are balanced calls, and
/// <see cref="Follow"/> is the idempotent form for an owner that follows a per-frame state (the portal presenter follows
/// <c>PortalFrame.WorldVisible</c>). Owners are short stable names (<see cref="PortalSpaceOwner"/>).</para>
/// <para>Created by <see cref="GameServices"/> (<see cref="GameServices.WorldVisibility"/>). Main thread. The viewport is
/// written only when the state changes, and each change is logged with the owners that hold requests.</para>
/// </remarks>
public sealed class WorldVisibility
{
    /// <summary>The owner name the portal-space presenter uses.</summary>
    public const string PortalSpaceOwner = "portal-space";

    private readonly Viewport _viewport;
    private readonly WorldHideRequests _requests = new();
    private long _reportedUnbalanced;

    /// <summary>An arbiter writing <paramref name="viewport"/>'s <c>Disable3D</c> (the main window's root viewport).</summary>
    public WorldVisibility(Viewport viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _viewport.Disable3D = false;
    }

    /// <summary>True while no request is held: the world is drawn.</summary>
    public bool Visible => !_requests.Hidden;

    /// <summary>The hide requests held, over all owners.</summary>
    public int Requests => _requests.Total;

    /// <summary>Raised after the world's visibility changed, with the new state (true = the world is drawn again).</summary>
    public event Action<bool>? Changed;

    /// <summary>The owners holding requests, for the log and the HUD (<c>none</c> while the world shows).</summary>
    public string Describe() => _requests.Describe();

    /// <summary>The requests <paramref name="owner"/> holds.</summary>
    public int Held(string owner) => _requests.Held(owner);

    /// <summary>Holds one more request to hide the world for <paramref name="owner"/>; balance it with <see cref="Unhide"/>.</summary>
    public void Hide(string owner) => Apply(owner, _requests.Request(owner));

    /// <summary>Releases one of <paramref name="owner"/>'s requests; a release without a request is a WARNING and ignored.</summary>
    public void Unhide(string owner) => Apply(owner, _requests.Release(owner));

    /// <summary>
    /// Makes <paramref name="owner"/> hold exactly one request while <paramref name="worldVisible"/> is false, none while
    /// it is true: for an owner that follows a state every frame. Cheap when nothing changes.
    /// </summary>
    public void Follow(string owner, bool worldVisible) => Apply(owner, _requests.Set(owner, hidden: !worldVisible));

    private void Apply(string owner, bool changed)
    {
        if (_requests.Unbalanced != _reportedUnbalanced)
        {
            _reportedUnbalanced = _requests.Unbalanced;
            Log.Warn(
                "render",
                "world visibility: a release matched no hide request (ignored); the owner's calls are unbalanced",
                ("owner", owner),
                ("unbalanced", _reportedUnbalanced),
                ("holding", _requests.Describe()));
        }

        if (!changed)
        {
            return;
        }

        var visible = Visible;
        _viewport.Disable3D = !visible;
        Log.Info("render", visible ? "world shown" : "world hidden", ("by", owner), ("holding", _requests.Describe()));
        Changed?.Invoke(visible);
    }
}
