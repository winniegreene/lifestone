using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 6 F1: the vitae over the climb. Counts ACE's "vitae reduced" Magic line (one per 0.01 step the earned experience works off: @95ef426d <c>Player_Xp.cs</c>:161-166) and reads the vitae itself from the player's
/// enchantments (<see cref="PlayerQualities.TryGetVitae"/>), so the autopilot's cycle and done lines can carry <see cref="Fields"/>. Pure apart from its one subscription.
/// </summary>
public sealed class VitaeLedger : IDisposable
{
    /// <summary>ACE's chat type for the line (Magic).</summary>
    public const uint MagicChatType = 7;

    private IGameSessionView? _view;
    private IDisposable? _subscription;

    /// <summary>The vitae-reduced lines seen since <see cref="Start"/>.</summary>
    public int Steps { get; private set; }

    /// <summary>The vitae now as a whole percentage penalty (<c>(1 - multiplier) x 100</c>, rounded as the death tally rounds it), or null when the player has none.</summary>
    public int? Percent
    {
        get
        {
            if (_view is null || !_view.State.Self.Qualities.TryGetVitae(out var vitae))
            {
                return null;
            }

            return (int)Math.Round((1.0 - vitae.Enchantment.StatModValue) * 100.0, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>The log fields: <c>vitae=&lt;pct&gt; vitae_steps=&lt;n&gt;</c>, or <c>vitae=none vitae_steps=&lt;n&gt;</c> when the player has no vitae.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"vitae={(Percent is { } p ? p.ToString(CultureInfo.InvariantCulture) : "none")} vitae_steps={Steps}");

    /// <summary>Starts counting on <paramref name="view"/>'s chat. A second call moves to the new view and keeps the count.</summary>
    /// <param name="view">The session view.</param>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _subscription?.Dispose();
        _view = view;
        _subscription = view.Events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>Stops counting.</summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.Kind == ChatKind.System && e.Line.ChatType == MagicChatType
            && string.Equals(e.Line.Text, DeathScenario.VitaeReducedLine, StringComparison.Ordinal))
        {
            Steps++;
        }
    }
}
