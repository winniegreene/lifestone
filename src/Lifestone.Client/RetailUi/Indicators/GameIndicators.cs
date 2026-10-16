using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Indicators;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: LinkStatusHolder::GetConnectionStatus 0x00411380.
/// <summary>
/// The indicators window (<c>gmFloatyIndicatorsUI</c>, 0x10000611; docs/retail-ui.md section 21.5): six small buttons on the layout's art. The link indicator (0x100000F8, the status of the connection) reads the
/// session's time since the last server datagram (<see cref="IGameSessionView.SecondsSinceLastServerPacket"/>). M3 2.2 a3 adds the effects (0x100000F5 beneficial and 0x100000F6 harmful), vitae (0x100000F4)
/// and burden (0x100000F7) indicators, <see cref="UiStateIndicator"/> buttons whose state this class sets: the effects indicator is lit (state 1) when the registry holds a beneficial (harmful) spell and ghosted (13)
/// when not (<c>gmUIElement_EffectsIndicator::Update</c>, 11.6096 0x004E76E0, over <c>m_cHelpfulEnchantments</c> and <c>m_cHarmfulEnchantments</c>, <see cref="EffectRules.CountKinds"/>); the vitae indicator is lit
/// when the vitae multiplier is below 1 (<c>gmUIElement_VitaeIndicator::Update</c> 0x004E6C70); the burden indicator shows state 14 below a full load, 15 from a full load to double and 16 from double
/// (<c>gmUIElement_BurdenIndicator::Update</c> 0x004E7970, the load of <c>CACQualities::InqLoad</c>). The mini-game indicator stays the layout's ghosted art (<c>TODO(g4-indicators-gameplay)</c>, mini-games). Clicking an
/// indicator fires its input action (property 0x12: the beneficial page, the harmful page, the vitae page, character information), which the panel host answers (<see cref="GamePanelHost"/>); a ghosted
/// button takes no mouse, as retail's ghosted buttons do.
/// </summary>
public sealed class GameIndicators : IDisposable
{
    /// <summary>The indicators window (<c>classic_floatyindicators</c>).</summary>
    public const uint WindowId = 0x10000611;

    /// <summary>The link-status button inside the window (class 0x10000003).</summary>
    public const uint LinkIndicatorId = 0x100000F8;

    /// <summary>The beneficial-effects (type 1), harmful-effects (type 2), vitae and burden indicators.</summary>
    public const uint BeneficialIndicatorId = 0x100000F5, HarmfulIndicatorId = 0x100000F6, VitaeIndicatorId = 0x100000F4, BurdenIndicatorId = 0x100000F7;

    /// <summary>The burden indicator's states: under a full load, from a full load, from twice the load.</summary>
    public const uint BurdenLightState = 14, BurdenHeavyState = 15, BurdenOverState = 16;

    private readonly IGameSessionView _session;
    private readonly UiElementManager _manager;
    private readonly IDisposable _enchantments;
    private readonly IDisposable _qualities;
    private ushort _unresolved;

    /// <summary>The indicators of <paramref name="window"/> (null when the layout lacks it).</summary>
    public GameIndicators(UiElementManager manager, IGameSessionView session, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _manager = manager;
        Window = window;
        _enchantments = session.Events.Subscribe<EnchantmentChanged>(OnEnchantmentChanged);
        _qualities = session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        if (window is null)
        {
            return;
        }

        if (window.GetChildRecursive(LinkIndicatorId) is UiLinkStatusIndicator link)
        {
            Link = link;
            link.Source = ReadLink;
        }
        else
        {
            manager.LogOnce("indicators:link", LogSeverity.Error, $"gmFloatyIndicatorsUI: the link indicator 0x{LinkIndicatorId:X8} is missing or not the link-status class; it shows nothing (TODO(g4-indicators-link))");
        }

        Beneficial = Find(window, BeneficialIndicatorId);
        Harmful = Find(window, HarmfulIndicatorId);
        Vitae = Find(window, VitaeIndicatorId);
        Burden = Find(window, BurdenIndicatorId);
        if (Beneficial is not null)
        {
            Beneficial.FrameHook = PollSpellTable;
        }

        if (Harmful is not null)
        {
            Harmful.FrameHook = PollSpellTable;
        }

        UpdateEffects();
        UpdateVitae();
        UpdateBurden();
    }

    /// <summary>The indicators window, or null.</summary>
    public UiElement? Window { get; }

    /// <summary>The link-status indicator, or null.</summary>
    public UiLinkStatusIndicator? Link { get; }

    /// <summary>The beneficial-effects indicator, or null when the layout or its class is missing.</summary>
    public UiStateIndicator? Beneficial { get; }

    /// <summary>The harmful-effects indicator, or null.</summary>
    public UiStateIndicator? Harmful { get; }

    /// <summary>The vitae indicator, or null.</summary>
    public UiStateIndicator? Vitae { get; }

    /// <summary>The burden indicator, or null.</summary>
    public UiStateIndicator? Burden { get; }

    /// <summary>
    /// The link's reading (<c>LinkStatusHolder::GetConnectionStatus</c>, 11.6096 0x004116E0): the seconds since the server last sent a datagram and whether the link is up. An offline stand-in session has no link
    /// to measure, so it reads as connected and just heard (placeholder, <c>TODO(g4-link-offline)</c>); a live session that never heard the server, or whose connection was lost, reads as not connected. Retail's
    /// no-drop-kick cap of 15 seconds (<c>m_nodropKick</c>) is not modelled (<c>TODO(g4-link-nodrop)</c>).
    /// </summary>
    public LinkReading ReadLink()
    {
        if (_session.State.IsOffline)
        {
            return new LinkReading(0, true);
        }

        if (_session.State.ConnectionLost || _session.SecondsSinceLastServerPacket is not { } seconds)
        {
            return new LinkReading(0, false);
        }

        return new LinkReading(seconds, true);
    }

    private UiStateIndicator? Find(UiElement window, uint id)
    {
        if (window.GetChildRecursive(id) is UiStateIndicator indicator)
        {
            return indicator;
        }

        _manager.LogOnce("indicators:" + id, LogSeverity.Error, $"gmFloatyIndicatorsUI: the indicator 0x{id:X8} is missing or not a state indicator; it keeps the layout's art (TODO(g4-indicators-gameplay))");
        return null;
    }

    private void OnEnchantmentChanged(in EnchantmentChanged change)
    {
        UpdateEffects(); // RecvNotice_EnchantmentsChanged
        if (change.VitaeChanged)
        {
            UpdateVitae(); // RecvNotice_VitaeChanged
        }
    }

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description)
        {
            UpdateEffects(); // RecvNotice_PlayerDescReceived of the three
            UpdateVitae();
            UpdateBurden();
        }
        else if (change.Change is not (QualityChange.VitalCurrent or QualityChange.Options))
        {
            UpdateBurden(); // RecvNotice_LoadChanged: the burden (property 5) and the Strength it is measured against
        }
    }

    /// <summary><c>gmUIElement_EffectsIndicator::Update</c> (11.6096 0x004E76E0): lit when the registry holds an entry of the indicator's kind.</summary>
    public void UpdateEffects()
    {
        if (Beneficial is null && Harmful is null)
        {
            return;
        }

        if (!_session.State.Self.Qualities.IsKnown)
        {
            return;
        }

        EffectRules.CountKinds(_session, out var beneficial, out var harmful, out _unresolved);
        if (_unresolved != 0)
        {
            _manager.LogOnce("indicators:spell-table", LogSeverity.Warning, "gmUIElement_EffectsIndicator: a spell of the registry is not in the SpellTable (it has not loaded yet); the indicator counts it when it does (TODO(m3-effects-spell-table))");
        }

        Light(Beneficial, beneficial);
        Light(Harmful, harmful);
    }

    private static void Light(UiStateIndicator? indicator, int count)
    {
        if (indicator is null)
        {
            return;
        }

        var state = count > 0 ? UiStateIds.Normal : UiStateIds.Ghosted;
        if (indicator.State != state)
        {
            indicator.SetState(state);
        }
    }

    // The frame hook of the effects indicators: asks again once the SpellTable answers for the spell that was missing. One dictionary lookup a frame while a spell is missing, nothing otherwise.
    private void PollSpellTable()
    {
        if (_unresolved != 0 && _session.Spell(_unresolved) is not null)
        {
            UpdateEffects();
        }
    }

    /// <summary><c>gmUIElement_VitaeIndicator::Update</c> (0x004E6C70): lit when the vitae multiplier is below 1.</summary>
    public void UpdateVitae()
    {
        var q = _session.State.Self.Qualities;
        if (Vitae is null || !q.IsKnown)
        {
            return;
        }

        var state = EffectRules.VitaeValue(q) < 1f ? UiStateIds.Normal : UiStateIds.Ghosted;
        if (Vitae.State != state)
        {
            Vitae.SetState(state);
        }
    }

    /// <summary><c>gmUIElement_BurdenIndicator::Update</c> (0x004E7970): state 14 below a full load, 16 from twice the load, else 15.</summary>
    public void UpdateBurden()
    {
        var q = _session.State.Self.Qualities;
        if (Burden is null || !q.IsKnown)
        {
            return;
        }

        var load = GameCharacterInfo.LoadOf(q, out _, out _);
        var state = load < 1f ? BurdenLightState : load >= 2f ? BurdenOverState : BurdenHeavyState;
        if (Burden.State != state)
        {
            Burden.SetState(state);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _enchantments.Dispose();
        _qualities.Dispose();
    }
}
