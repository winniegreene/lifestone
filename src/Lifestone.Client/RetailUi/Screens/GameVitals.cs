using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// One vitals window's meters and labels (<c>gmVitalsUI</c>; docs/retail-ui.md section 21.3): <c>PostInit</c> (11.6096 0x004C0960) finds the health meter 0x100000E6 and label
/// 0x100000EB, the stamina meter 0x100000EC and label 0x100000ED and the mana meter 0x100000EE and label 0x100000EF inside the window; <see cref="Update"/> is
/// <c>gmVitalsUI::Update</c> (11.6096 0x004C0AB0): each meter's position (property 0x69, set directly: retail does not use the meter's smooth goal here) is the vital's current value over
/// its maximum, and its label is the string <c>ID_Vitals_VitalBarLabel</c> (table enum 0x10000001) with the variables <c>ID_Cur_0</c> and <c>ID_Max_0</c> (the hashes of "CUR" and "MAX"); nothing happens until the qualities are
/// known (retail skips when the player description's first word has its high bit set).
/// </summary>
public sealed class VitalsWindow
{
    /// <summary>The health meter.</summary>
    public const uint HealthMeterId = 0x100000E6;

    /// <summary>The health label.</summary>
    public const uint HealthLabelId = 0x100000EB;

    /// <summary>The stamina meter.</summary>
    public const uint StaminaMeterId = 0x100000EC;

    /// <summary>The stamina label.</summary>
    public const uint StaminaLabelId = 0x100000ED;

    /// <summary>The mana meter.</summary>
    public const uint ManaMeterId = 0x100000EE;

    /// <summary>The mana label.</summary>
    public const uint ManaLabelId = 0x100000EF;

    /// <summary>The string table enum of <c>ID_Vitals_VitalBarLabel</c> (0x10000001).</summary>
    public const uint LabelTableEnum = 0x10000001;

    private static readonly uint CurrentVariable = StringHash.Tokenize("CUR");
    private static readonly uint MaxVariable = StringHash.Tokenize("MAX");
    private readonly UiElementManager _manager;
    private readonly UiStringVariables _variables = new();

    /// <summary>The meters and labels of <paramref name="window"/>.</summary>
    public VitalsWindow(UiElementManager manager, UiElement window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(window);
        _manager = manager;
        Window = window;
        HealthMeter = window.GetChildRecursive(HealthMeterId) as UiMeter;
        HealthLabel = window.GetChildRecursive(HealthLabelId) as UiTextElement;
        StaminaMeter = window.GetChildRecursive(StaminaMeterId) as UiMeter;
        StaminaLabel = window.GetChildRecursive(StaminaLabelId) as UiTextElement;
        ManaMeter = window.GetChildRecursive(ManaMeterId) as UiMeter;
        ManaLabel = window.GetChildRecursive(ManaLabelId) as UiTextElement;
    }

    /// <summary>The window (0x100005FA or 0x100006D5).</summary>
    public UiElement Window { get; }

    /// <summary>The health meter, or null when the layout lacks it.</summary>
    public UiMeter? HealthMeter { get; }

    /// <summary>The health label.</summary>
    public UiTextElement? HealthLabel { get; }

    /// <summary>The stamina meter.</summary>
    public UiMeter? StaminaMeter { get; }

    /// <summary>The stamina label.</summary>
    public UiTextElement? StaminaLabel { get; }

    /// <summary>The mana meter.</summary>
    public UiMeter? ManaMeter { get; }

    /// <summary>The mana label.</summary>
    public UiTextElement? ManaLabel { get; }

    /// <summary>How many updates reached the meters (the qualities were known).</summary>
    public int Updates { get; private set; }

    /// <summary><c>gmVitalsUI::Update</c>: sets the three meters and labels from <paramref name="qualities"/>; does nothing until they are known.</summary>
    public void Update(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (!qualities.IsKnown)
        {
            return;
        }

        Updates++;
        Set(qualities, 1, HealthMeter, HealthLabel);
        Set(qualities, 3, StaminaMeter, StaminaLabel);
        Set(qualities, 5, ManaMeter, ManaLabel);
    }

    /// <summary>
    /// What this window shows now, as <c>health/stamina/mana</c> currents (the login check's <c>vitals=</c>): read from the labels' text (the first number is the current, the second the maximum)
    /// and the meters' positions, so it is the UI's value and not the session's. Null when a meter or label is missing or empty, a maximum is 0, or a meter does not agree with its label.
    /// </summary>
    public string? ReadLevels()
    {
        if (!ReadOne(HealthMeter, HealthLabel, out var health) || !ReadOne(StaminaMeter, StaminaLabel, out var stamina) || !ReadOne(ManaMeter, ManaLabel, out var mana))
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{health}/{stamina}/{mana}");
    }

    private static bool ReadOne(UiMeter? meter, UiTextElement? label, out long current)
    {
        current = 0;
        if (meter is null || label is null)
        {
            return false;
        }

        var text = label.Text;
        Span<long> numbers = stackalloc long[2];
        var found = 0;
        for (var i = 0; i < text.Length && found < 2; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                continue;
            }

            long value = 0;
            for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
            {
                value = (value * 10) + (text[i] - '0');
            }

            numbers[found++] = value;
        }

        if (found < 2 || numbers[1] == 0)
        {
            return false;
        }

        current = numbers[0];
        return Math.Abs(meter.Position - ((double)numbers[0] / numbers[1])) < 0.01;
    }

    private void Set(PlayerQualities qualities, uint maxId, UiMeter? meter, UiTextElement? label)
    {
        if (meter is null || !qualities.TryGetVitalLevels(maxId, out var current, out var max))
        {
            return;
        }

        // Retail divides without a guard; a vital with no maximum would be a NaN or infinite level, so it shows empty here.
        meter.SetFloatProperty(UiPropertyIds.MeterPosition, max == 0 ? 0f : (float)((double)current / max));
        if (label is null)
        {
            return;
        }

        var strings = _manager.Services.Strings;
        if (strings is null || !strings.TryReference(LabelTableEnum, "ID_Vitals_VitalBarLabel", out var reference))
        {
            label.SetText(string.Create(CultureInfo.InvariantCulture, $"{current} / {max}"));
            _manager.LogOnce("vitals:label", LogSeverity.Warning, "gmVitalsUI: ID_Vitals_VitalBarLabel is not in the string tables; the labels show 'cur / max' as a placeholder");
            return;
        }

        if (strings.Language is { } language)
        {
            _variables.SetInteger(CurrentVariable, current, language).SetInteger(MaxVariable, max, language);
        }
        else
        {
            _variables.SetText(CurrentVariable, current.ToString(CultureInfo.InvariantCulture)).SetText(MaxVariable, max.ToString(CultureInfo.InvariantCulture));
        }

        label.SetStringInfo(reference, _variables);
    }
}

/// <summary>
/// The in-game shell's vitals (D16; docs/retail-ui.md section 21.3): both floaty windows, the stacked 0x100005FA (<c>gmFloatyVitalsUI</c>) and the side-by-side 0x100006D5
/// (<c>gmFloatySideVitalsUI</c>), and the choice between them. <c>UpdateFromPlayerModule</c> of each (11.4186 0x004CF140 and 0x004D0810, 11.6096 0x004CFD50 and 0x004D15C0)
/// shows its window by <c>PlayerModule::SideBySideVitals</c> (11.6096 0x005D4070: option bit 21 of the character's options): the stacked one when it is clear, the side-by-side one
/// when it is set. The options arrive in the PlayerDescription, so the stacked window shows until it does. Each window's saved place, size and lock state
/// (<c>UpdateFromPlayerModule</c>'s other half, options 0x10000080 to 0x10000089 and <c>UpdateLockedStatus</c>) are not applied (TODO(g4-vitals-place): D56 settles what Lifestone saves).
/// Both windows update on every quality change (<c>gmVitalsUI::OnQualityChanged</c>, 11.6096 0x004C0E70) and when the description arrives
/// (<c>RecvNotice_PlayerDescReceived</c>, 0x004C0E80).
/// </summary>
public sealed class GameVitals : IDisposable
{
    private readonly UiLoginContext _context;
    private readonly IDisposable _subscription;
    private bool _disposed;

    /// <summary>The vitals over the windows the layout has; a missing window is skipped.</summary>
    public GameVitals(UiLoginContext context, UiElement? stacked, UiElement? sideBySide)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Stacked = stacked is null ? null : new VitalsWindow(context.Manager, stacked);
        SideBySide = sideBySide is null ? null : new VitalsWindow(context.Manager, sideBySide);
        _subscription = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        Refresh(chooseWindow: true);
    }

    private void OnQualitiesChanged(in QualitiesChanged change) => Refresh(change.Change == QualityChange.Description);

    /// <summary>The stacked window (0x100005FA).</summary>
    public VitalsWindow? Stacked { get; }

    /// <summary>The side-by-side window (0x100006D5).</summary>
    public VitalsWindow? SideBySide { get; }

    /// <summary>The window the character's options choose: <see cref="SideBySide"/> when option bit 21 is set, else <see cref="Stacked"/>.</summary>
    public VitalsWindow? Chosen => _context.Session.State.Self.Qualities.SideBySideVitals ? SideBySide : Stacked;

    /// <summary><c>UpdateFromPlayerModule</c> (when <paramref name="chooseWindow"/>: the description just arrived) and <c>Update</c> for both windows: shows the chosen one, hides the other, sets the meters.</summary>
    public void Refresh(bool chooseWindow = true)
    {
        if (_disposed)
        {
            return;
        }

        var qualities = _context.Session.State.Self.Qualities;
        var side = qualities.SideBySideVitals;
        if (chooseWindow)
        {
            Stacked?.Window.SetVisible(!side);
            SideBySide?.Window.SetVisible(side);
        }

        if (chooseWindow && qualities.IsKnown)
        {
            _context.Manager.LogOnce(
                $"vitals:window:{side}",
                LogSeverity.Info,
                $"vitals: the character's options 0x{qualities.CharacterOptions1:X8} (bit 21, side-by-side vitals, is {(side ? "set" : "clear")}) choose the {(side ? "side-by-side (0x100006D5)" : "stacked (0x100005FA)")} window");
        }

        Stacked?.Update(qualities);
        SideBySide?.Update(qualities);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
    }
}
