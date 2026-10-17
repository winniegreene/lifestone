using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Retail's <c>PowerBarMode</c>: who owns the meter. The combat system sends Begin, SetLevel and Finish notices with one of these (<c>CM_Character::SendNotice_BeginPowerbar</c>, 11.4186 0x006A5BA0).</summary>
public enum PowerBarMode
{
    /// <summary>No bar in use (<c>PBM_UNDEF</c>).</summary>
    Undefined = 0,

    /// <summary>The basic combat UI: the level goes to the combat window's own meter (<see cref="GameCombatPanel.SetPowerbarLevel"/>), the power bar window does not show.</summary>
    Combat = 1,

    /// <summary>The advanced combat UI: the power bar window shows an attack's power (state 0x10000043 in melee, 0x10000044 otherwise).</summary>
    Advanced = 2,

    /// <summary>A jump's charge (state 0x10000042).</summary>
    Jump = 3,

    /// <summary>The runtime download's progress (<c>RecvNotice_RuntimeDDDStatus</c>, state 0x10000045).</summary>
    Dd = 4,
}

/// <summary>
/// The power bar (<c>gmPowerbarUI</c>, class 0x1000000F, window 0x10000044 of the gameplay layout, a 760x15 strip at the bottom of the world view; 11.6096 0x004DB000 to 0x004DB190 and the notices to 0x004DB390, 12
/// functions). Its one meter (0x10000034) shows a charge: an attack's power in the advanced combat UI, a jump's, or the download's progress. The window is hidden until a Begin notice shows it and a Finish notice hides it.
/// </summary>
/// <remarks>
/// <para><b>What drives it.</b> <c>ClientCombatSystem</c> sends the notices: <c>HandleCommenceAttackEvent</c> (11.6096 0x0056BA60) and <c>AttemptStartBuildingAttack</c> (0x0056C850) begin a bar (mode 2 with the advanced UI, mode 1 without
/// it, where the level goes to the combat window's meter); a jump (mode 3) and the runtime download (mode 4) do too; <c>HidePowerBar</c> (0x0056B5F0) and <c>FinishJump</c> finish it. <b>M3 has no attack and no jump</b>, so nothing
/// begins the bar in play: the window stays hidden, and its API (<see cref="Begin"/>, <see cref="SetLevel"/>, <see cref="Finish"/>, <see cref="DddStatus"/>) is what the attack action will call
/// (<c>TODO(m3-melee-attack)</c>) and what the tests and the review frame drive.</para>
/// <para><b>BeginPowerbar</b> (0x004DB390): mode 1 does nothing (the combat window owns it); mode 2 uses the melee state when the combat mode is melee (and shows the recklessness field 0x100005EE when the Recklessness skill
/// is trained or specialized), else state 0x10000044 with the field hidden; mode 3 is state 0x10000042 and mode 4 state 0x10000045, both with the field hidden. Then the mode is remembered, the meter reset to 0 and the window shown.
/// <b>SetPowerbarLevel</b> (0x004DB1B0) sets the meter only for the mode that began; <b>FinishPowerbar</b> (0x004DB1E0) for that mode forgets it, zeroes the meter and hides the window.</para>
/// <para>Nothing here runs per frame.</para>
/// </remarks>
public sealed class GamePowerBar : UiElement
{
    /// <summary>The retail class id (<c>gmPowerbarUI</c>).</summary>
    public const uint RetailClassId = 0x1000000F;

    /// <summary>The window's element id in the gameplay layout.</summary>
    public const uint WindowId = 0x10000044;

    /// <summary>The meter.</summary>
    public const uint MeterId = 0x10000034;

    /// <summary>The meter's text (the download's wording).</summary>
    public const uint TextId = 0x10000035;

    /// <summary>The recklessness field.</summary>
    public const uint RecklessnessFieldId = 0x100005EE;

    /// <summary>The state of an advanced attack in melee mode.</summary>
    public const uint MeleeState = 0x10000043;

    /// <summary>The state of an advanced attack in another mode.</summary>
    public const uint MissileState = 0x10000044;

    /// <summary>The state of a jump.</summary>
    public const uint JumpState = 0x10000042;

    /// <summary>The state of the download.</summary>
    public const uint DdState = 0x10000045;

    /// <summary>The string table enum of <c>ID_Powerbar_DDDModeText</c>.</summary>
    public const uint StringTableEnum = 0x10000001;

    private static readonly uint CurrentVariable = StringHash.Tokenize("CURRENT");
    private static readonly uint TotalVariable = StringHash.Tokenize("TOTAL");

    private readonly UiStringVariables _variables = new();
    private UiLoginContext? _context;
    private UiElement? _meter;
    private UiElement? _recklessness;
    private UiTextElement? _text;

    /// <summary>A power bar of <paramref name="init"/>.</summary>
    public GamePowerBar(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The mode that began the bar (<c>m_pbmCurrentMode</c>), <see cref="PowerBarMode.Undefined"/> when none.</summary>
    public PowerBarMode CurrentMode { get; private set; }

    /// <summary>The meter's level (0..1).</summary>
    public float Level => _meter is { } m && m.TryGetFloatProperty(UiPropertyIds.MeterPosition, out var p) ? p : 0f;

    /// <summary>True while the window is shown (a bar is in use).</summary>
    public bool InUse => Visible;

    /// <summary>The recklessness field (null when the layout lacks it).</summary>
    public UiElement? RecklessnessField => _recklessness;

    /// <summary>The text the meter's text element shows now (the download's progress wording), empty when none.</summary>
    public string Caption => _text?.Text ?? string.Empty;

    /// <summary><c>PostInit</c> (0x004DB140): finds the recklessness field and hides it (the window itself stays as the layout has it, hidden).</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _meter = GetChildRecursive(MeterId);
        _text = GetChildRecursive(TextId) as UiTextElement;
        _recklessness = GetChildRecursive(RecklessnessFieldId);
        _recklessness?.SetVisible(false);
    }

    /// <summary>Hands the bar the session (the combat mode and the skill it reads on Begin) and the strings; hides the window until a bar begins.</summary>
    public void Attach(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        SetVisible(false);
    }

    /// <summary>
    /// <c>RecvNotice_BeginPowerbar</c> (0x004DB390): begins a bar of <paramref name="mode"/>. Retail reads the combat mode from the combat system at that moment; <paramref name="combatMode"/> (<see cref="CombatModes"/>) says
    /// it for a caller whose mode is not the session's (the review frame shows melee without changing a live character's mode); null reads the session's.
    /// </summary>
    public void Begin(PowerBarMode mode, int? combatMode = null)
    {
        if (mode == PowerBarMode.Combat)
        {
            return; // the combat window's own meter
        }

        switch (mode)
        {
            case PowerBarMode.Advanced:
                if ((combatMode ?? CombatMode()) == CombatModes.Melee)
                {
                    SetState(MeleeState);
                    _recklessness?.SetVisible(RecklessnessTrained());
                }
                else
                {
                    SetState(MissileState);
                    _recklessness?.SetVisible(false);
                }

                break;
            case PowerBarMode.Jump:
                SetState(JumpState);
                _recklessness?.SetVisible(false);
                break;
            case PowerBarMode.Dd:
                SetState(DdState);
                _recklessness?.SetVisible(false);
                break;
        }

        CurrentMode = mode;
        _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, 0f);
        SetVisible(true);
    }

    /// <summary><c>RecvNotice_SetPowerbarLevel</c> (0x004DB1B0): the meter shows <paramref name="level"/> when <paramref name="mode"/> is the one that began.</summary>
    public void SetLevel(PowerBarMode mode, float level)
    {
        if (mode == CurrentMode)
        {
            _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, level);
        }
    }

    /// <summary><c>RecvNotice_FinishPowerbar</c> (0x004DB1E0): the bar of <paramref name="mode"/> ends: the mode is forgotten, the meter zeroed and the window hidden.</summary>
    public void Finish(PowerBarMode mode)
    {
        if (mode == CurrentMode)
        {
            CurrentMode = PowerBarMode.Undefined;
            _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, 0f);
            SetVisible(false);
        }
    }

    /// <summary>
    /// <c>RecvNotice_RuntimeDDDStatus</c> (0x004DB220): a download started (<paramref name="start"/>) shows the bar in <see cref="PowerBarMode.Dd"/> with the text <c>ID_Powerbar_DDDModeText</c> (CURRENT and TOTAL, "???" without a total) and
    /// the meter at current over total; ending it finishes the bar of that mode. Lifestone has no runtime download (the dats are local): nothing calls it outside the tests.
    /// </summary>
    public void DddStatus(bool start, uint current, uint total)
    {
        if (!start)
        {
            Finish(PowerBarMode.Dd);
            return;
        }

        if (CurrentMode != PowerBarMode.Dd)
        {
            Begin(PowerBarMode.Dd);
        }

        var progress = total != 0 ? (float)((double)current / total) : 0f;
        _text?.SetText(DddText(current, total));
        _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, progress);
    }

    private string DddText(uint current, uint total)
    {
        var strings = _context?.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, "ID_Powerbar_DDDModeText", out var reference))
        {
            _variables.SetText(CurrentVariable, current.ToString(CultureInfo.InvariantCulture));
            _variables.SetText(TotalVariable, total != 0 ? total.ToString(CultureInfo.InvariantCulture) : "???");
            if (strings.TryGetString(reference, _variables, out var text))
            {
                return text;
            }
        }

        _context?.Manager.LogOnce("powerbar:ddd", LogSeverity.Warning, "gmPowerbarUI: the string ID_Powerbar_DDDModeText does not render in the string tables; the download text is a placeholder (TODO(m3-powerbar-strings))");
        return string.Create(CultureInfo.InvariantCulture, $"Downloading {current} of {(total != 0 ? total.ToString(CultureInfo.InvariantCulture) : "???")}");
    }

    private int CombatMode() => _context?.Session.State.Self.Qualities.CombatMode ?? 0;

    private bool RecklessnessTrained() =>
        _context is { } context && SkillRules.ClassOf(context.Session.State.Self.Qualities, GameCombatPanel.RecklessnessSkill) >= 2;
}
