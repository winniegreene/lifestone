using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Indicators;

/// <summary>What the link status reads: seconds since the last datagram from the server, and whether the link is up (<c>LinkStatusHolder::GetConnectionStatus</c>, 11.6096 0x004116E0).</summary>
/// <param name="Seconds">Seconds since the server was last heard (0 when the link is not connected).</param>
/// <param name="Connected">True when the link is connected.</param>
public readonly record struct LinkReading(double Seconds, bool Connected);

/// <summary>The link states of <c>gmUIElement_LinkStatusIndicator</c> and the rules that choose them.</summary>
public static class LinkStatusRules
{
    /// <summary>Good: the server was heard within <see cref="GoodSeconds"/>.</summary>
    public const int Good = 1;

    /// <summary>Slow: heard within <see cref="SlowSeconds"/>.</summary>
    public const int Slow = 2;

    /// <summary>Failing: heard within <see cref="FailingSeconds"/>; the indicator flashes between states 18 and 19.</summary>
    public const int Failing = 3;

    /// <summary>Bad: silent for longer, or not connected.</summary>
    public const int Bad = 4;

    /// <summary>The comparison constants of <c>UpdateLinkState</c> (11.6096 0x004E73C0): doubles at 0x007C2108, 0x007C2110 and 0x007C2118 (read from the exe, D55).</summary>
    public const double GoodSeconds = 5.0, SlowSeconds = 20.0, FailingSeconds = 40.0;

    /// <summary>The indicator re-reads the link when more than this many seconds passed (<c>UseTime</c>, 11.6096 0x004E7450; the double at 0x007C20F8).</summary>
    public const double UpdateIntervalSeconds = 4.0;

    /// <summary>The flashing state alternates this often (<c>UseTime</c>; the 11.4186 decompile's constant, not read from the exe).</summary>
    public const double FlashSeconds = 0.75;

    /// <summary>The element state a link state shows (<c>SetLinkState</c>, 11.6096 0x004E7270): 1 to 17, 2 to 18, 3 to 19, 4 to 20.</summary>
    public static uint StateFor(int linkState) => (uint)(16 + linkState);

    /// <summary>The link state for a reading (<c>UpdateLinkState</c>, 11.6096 0x004E73C0).</summary>
    public static int LinkStateFor(in LinkReading reading)
    {
        if (reading.Connected)
        {
            if (reading.Seconds <= GoodSeconds)
            {
                return Good;
            }

            if (reading.Seconds <= SlowSeconds)
            {
                return Slow;
            }

            if (reading.Seconds <= FailingSeconds)
            {
                return Failing;
            }
        }

        return Bad;
    }
}

/// <summary>
/// The link indicator (<c>gmUIElement_LinkStatusIndicator</c>, UI class 0x10000003; the button 0x100000F8 of the indicators window): a button whose state shows how long ago the server was last
/// heard. <c>PostInit</c> (11.6096 0x004E7360) enters link state 1 (element state 17) and listens for the frame message; <see cref="UseTime"/> (0x004E7450) re-reads the link every more than 4 seconds
/// (<see cref="LinkStatusRules"/>) and, in link state 3, flashes between states 18 and 19 every 0.75 seconds. Retail reads the link from its client singleton; here <see cref="Source"/> says.
/// </summary>
public sealed class UiLinkStatusIndicator : UiButton
{
    private int _linkState;
    private double? _lastUpdate;
    private double _lastFlash;

    /// <summary>The indicator of <paramref name="init"/>.</summary>
    public UiLinkStatusIndicator(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>What the link status reads now; null reads as not connected (state 4).</summary>
    public Func<LinkReading>? Source { get; set; }

    /// <summary>The link state (1 to 4); 0 before <c>PostInit</c>.</summary>
    public int LinkState => _linkState;

    /// <summary>The link state 1 and element state 17 retail enters in <c>PostInit</c>, then the frame message.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        SetLinkState(LinkStatusRules.Good);
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame)
        {
            UseTime();
        }

        base.ListenToGlobalMessage(messageId, data);
    }

    /// <summary>
    /// <c>UseTime</c> (11.6096 0x004E7450). Retail's last-update time starts at 0 with a clock far past 4 seconds, so the first call always reads the link; the first call here does too.
    /// </summary>
    public void UseTime()
    {
        var now = Manager.Now;
        if (_lastUpdate is not { } last || now - last > LinkStatusRules.UpdateIntervalSeconds)
        {
            UpdateLinkState();
            _lastUpdate = now;
        }

        if (_linkState == LinkStatusRules.Failing && now - _lastFlash >= LinkStatusRules.FlashSeconds)
        {
            SetState(State == 18 ? 19u : 18u);
            _lastFlash = now;
        }
    }

    /// <summary><c>UpdateLinkState</c> (11.6096 0x004E73C0).</summary>
    public void UpdateLinkState() => SetLinkState(LinkStatusRules.LinkStateFor(Source?.Invoke() ?? new LinkReading(0, false)));

    /// <summary><c>SetLinkState</c> (11.6096 0x004E7270): a changed link state shows its element state.</summary>
    public void SetLinkState(int linkState)
    {
        if (_linkState == linkState)
        {
            return;
        }

        _linkState = linkState;
        SetState(LinkStatusRules.StateFor(linkState));
    }
}
