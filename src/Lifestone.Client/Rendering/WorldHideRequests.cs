namespace Lifestone.Client.Rendering;

/// <summary>
/// Reference-counted requests to hide the 3D world, by owner: the book-keeping behind the game's
/// <c>WorldVisibility</c> arbiter (the sole writer of the main viewport's <c>Disable3D</c>). The world is hidden while at
/// least one request is held; a request is held until its owner releases it, so the portal tunnel, a full-screen
/// retail UI screen and a loading state can each hide the world without knowing about the others.
/// </summary>
/// <remarks>
/// <para><b>Owners.</b> An owner is a short stable name (<c>"portal-space"</c>, <c>"retail-ui"</c>); it appears in
/// <see cref="Describe"/> and in the arbiter's log. The same owner may hold several requests (<see cref="Request"/> per
/// call); <see cref="Set"/> is the idempotent form for an owner that follows a per-frame state (the portal presenter
/// follows <c>PortalFrame.WorldVisible</c>): it holds exactly zero or one.</para>
/// <para><b>Unbalanced releases</b> never throw (a render path must not crash over a bookkeeping slip): the release is
/// ignored and counted in <see cref="Unbalanced"/>, which the arbiter logs as a WARNING, loudly.</para>
/// <para>Frame thread only. No allocation once an owner has been seen.</para>
/// </remarks>
public sealed class WorldHideRequests
{
    private readonly Dictionary<string, int> _held = new(StringComparer.Ordinal);

    /// <summary>The requests held, over all owners.</summary>
    public int Total { get; private set; }

    /// <summary>True while at least one request is held: the world is hidden.</summary>
    public bool Hidden => Total > 0;

    /// <summary>Releases that matched no request (a bug in the caller; logged by the arbiter).</summary>
    public long Unbalanced { get; private set; }

    /// <summary>The requests <paramref name="owner"/> holds.</summary>
    public int Held(string owner) => _held.GetValueOrDefault(owner);

    /// <summary>Holds one more hide request for <paramref name="owner"/>. Returns true when the world has just become hidden.</summary>
    public bool Request(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        _held[owner] = _held.GetValueOrDefault(owner) + 1;
        return ++Total == 1;
    }

    /// <summary>
    /// Releases one of <paramref name="owner"/>'s requests. Returns true when the world has just become visible; a release
    /// without a request is ignored and counted (<see cref="Unbalanced"/>).
    /// </summary>
    public bool Release(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        if (!_held.TryGetValue(owner, out var held) || held <= 0)
        {
            Unbalanced++;
            return false;
        }

        _held[owner] = held - 1;
        return --Total == 0;
    }

    /// <summary>
    /// Makes <paramref name="owner"/> hold exactly one request (<paramref name="hidden"/>) or none, whatever it held:
    /// for an owner that follows a state every frame. Returns true when the world's visibility changed.
    /// </summary>
    public bool Set(string owner, bool hidden)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        var held = Held(owner);
        var wasHidden = Hidden;
        Total += (hidden ? 1 : 0) - held;
        _held[owner] = hidden ? 1 : 0;
        return wasHidden != Hidden;
    }

    /// <summary>The owners that hold requests, for the log: <c>portal-space(1) retail-ui(2)</c>, or <c>none</c>.</summary>
    public string Describe()
    {
        if (Total == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        foreach (var (owner, held) in _held)
        {
            if (held > 0)
            {
                parts.Add($"{owner}({held})");
            }
        }

        return string.Join(' ', parts);
    }
}
