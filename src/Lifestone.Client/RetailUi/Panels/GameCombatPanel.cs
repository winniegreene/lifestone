using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// What a press of a combat-mode button does (M3 3.2 pa): <c>ClientCombatSystem::ToggleCombatMode</c> (11.6096 0x0056D610) and the mode it picks, <c>ClientCombatSystem::GetDefaultCombatMode</c> (11.6096 0x0056C050).
/// </summary>
/// <remarks>
/// <para><b>ToggleCombatMode.</b> In the non-combat mode it asks for the default mode, in any other it asks for the non-combat mode (both through <c>SetCombatMode</c>, which Lifestone sends as
/// <see cref="IActionApi.SetCombatMode"/>). When the mode it would ask for is the one the player is in (from non-combat: a held item that forbids combat, or a wielded item the object table lacks) nothing is asked: the
/// session's own rule answers a request for the current mode Local at once and sends nothing (docs/session.md, the <c>SetCombatMode</c> row), and the log says why the press did nothing (M3 3.2 review finding 13; what retail's
/// <c>SetCombatMode</c> does for an unchanged mode was not read).</para>
/// <para><b>GetDefaultCombatMode.</b> No player object: non-combat. Something in the melee, missile or two-handed slot (<c>0x2500000</c>): missile mode when it is a missile weapon, else melee. Else something in the held
/// slot (<c>0x1000000</c>): magic when it is a caster (ItemType 0x8000), else the chat line "You can't enter combat mode while wielding the &lt;name&gt;" and non-combat. Nothing wielded: melee (fists).
/// Retail tells a missile weapon from a melee one by the weapon's own CombatUse (the weenie's type, 2 for a missile weapon): Lifestone reads <c>WeenieDesc.CombatUse</c> (1 Melee and 5 TwoHanded give melee, 2 Missile gives missile; Ammo 3 and Shield 4 are not weapons and leave the slot to decide) and falls back to the slot (<c>MissileWeapon</c> 0x400000 against <c>MeleeWeapon</c> and <c>TwoHanded</c>) only when the description has none, <c>TODO(m3-combat-use-absent)</c>. A wielded item whose object the table does not hold (its CreateObject has not arrived, or an F747 alone deleted it) has no kind to read: the mode is non-combat, never a guess, and the item is named once (<see cref="MissingObjectTodo"/>).</para>
/// </remarks>
public static class CombatModeRules
{
    /// <summary>The ItemType bit of a caster (wand, staff, orb).</summary>
    public const uint ItemTypeCaster = 0x8000;

    /// <summary>The TODO named, once per item, when the inventory says an item is wielded but the object table has no such object, so the default mode falls back to non-combat and a press does nothing.</summary>
    public const string MissingObjectTodo = "TODO(m3-combat-default-mode-missing)";

    /// <summary>The TODO named, once, when a wielded weapon's description carries no CombatUse and the slot rule decides.</summary>
    public const string CombatUseAbsentTodo = "TODO(m3-combat-use-absent)";

    private const uint WeaponSlots = (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.TwoHanded);

    /// <summary><c>GetDefaultCombatMode</c>: the mode a press of a combat button asks for from non-combat. <paramref name="blockedBy"/> is the name of the held item that forbids combat, or null.</summary>
    public static CombatMode DefaultMode(GameState state, out string? blockedBy) => DefaultMode(state, out blockedBy, out _);

    /// <summary>
    /// <c>GetDefaultCombatMode</c> with its fallback named: <paramref name="missing"/> is the wielded item the object table has no object for (the mode is then non-combat for want of the item's kind, which the caller names,
    /// <see cref="MissingObjectTodo"/>), or 0.
    /// </summary>
    public static CombatMode DefaultMode(GameState state, out string? blockedBy, out uint missing) => DefaultMode(state, out blockedBy, out missing, out _);

    /// <summary>
    /// <c>GetDefaultCombatMode</c> reading the weapon's own type: <paramref name="combatUseAbsent"/> is true when the weapon's weenie description carried no CombatUse, so the slot rule decided
    /// (<c>TODO(m3-combat-use-absent)</c>, which the caller names once).
    /// </summary>
    public static CombatMode DefaultMode(GameState state, out string? blockedBy, out uint missing, out bool combatUseAbsent)
    {
        ArgumentNullException.ThrowIfNull(state);
        combatUseAbsent = false;
        blockedBy = null;
        missing = 0;
        var inventory = state.Self.Inventory;
        if (state.Self.Guid == 0)
        {
            return CombatMode.NonCombat;
        }

        if (inventory.EquippedAt(WeaponSlots) is { } weapon)
        {
            if (!state.Objects.TryGet(weapon, out var weaponObject) || !inventory.TryGetEquipped(weapon, out var entry))
            {
                missing = weapon;
                return CombatMode.NonCombat;
            }

            // 0x0056C050 asks the weapon's own weenie (its CombatUse, ACE's 1 Melee, 2 Missile, 3 Ammo, 4 Shield, 5 TwoHanded): Missile gives missile mode, a melee or two-handed weapon melee. Ammo and Shield are not weapons: the slot decides.
            switch (weaponObject.CombatUse)
            {
                case 2:
                    return CombatMode.Missile;
                case 1 or 5:
                    return CombatMode.Melee;
                case null:
                    combatUseAbsent = true;
                    break;
            }

            return (entry.Location & (uint)EquipMask.MissileWeapon) != 0 ? CombatMode.Missile : CombatMode.Melee;
        }

        if (inventory.EquippedAt((uint)EquipMask.Held) is { } held)
        {
            if (!state.Objects.TryGet(held, out var item))
            {
                missing = held;
                return CombatMode.NonCombat;
            }

            if ((item.ItemType & ItemTypeCaster) != 0)
            {
                return CombatMode.Magic;
            }

            blockedBy = item.Name;
            return CombatMode.NonCombat;
        }

        return CombatMode.Melee;
    }

    /// <summary>
    /// <c>ToggleCombatMode</c>: asks the session for the next mode and returns the handle, or null when the mode it would ask for is the one the player is in (nothing is asked: the session would answer Local and send nothing,
    /// see the remarks). <paramref name="notice"/> gets the chat line of a held item that forbids combat (null: only logged); <paramref name="manager"/> logs that line and, once per item, <see cref="MissingObjectTodo"/>.
    /// </summary>
    public static ActionHandle? Toggle(IGameSessionView session, UiElementManager manager, Action<string>? notice)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manager);
        var current = session.State.Self.Qualities.CombatMode;
        var requested = CombatMode.NonCombat;
        if (current is 0 or CombatModes.NonCombat)
        {
            requested = DefaultMode(session.State, out var blockedBy, out var missing, out var combatUseAbsent);
            if (combatUseAbsent)
            {
                manager.LogOnce("combat:combat-use-absent", LogSeverity.Warning, CombatUseAbsentTodo + ": the wielded weapon's description has no CombatUse; the slot rule (MissileWeapon 0x400000) decides the default combat mode");
            }

            if (missing != 0)
            {
                var hex = "0x" + missing.ToString("X8", CultureInfo.InvariantCulture);
                manager.LogOnce("combat:default-mode-missing:" + hex, LogSeverity.Warning, MissingObjectTodo + ": the inventory says " + hex + " is wielded but the object table has no such object (its CreateObject has not arrived, or it was deleted), so GetDefaultCombatMode cannot tell its kind: the default mode is non-combat and the press does nothing");
            }

            if (blockedBy is not null)
            {
                Say(manager, notice, $"You can't enter combat mode while wielding the {blockedBy}");
            }

            if (requested == CombatMode.NonCombat)
            {
                return null; // the player is in non-combat already: nothing to ask
            }
        }

        // ClientCombatSystem::SetCombatMode (0x0056CB80), a player request: CompatibleCombatMode first, then the portal-space refusal.
        if (!Compatible(session.State, requested, out var wielded))
        {
            if (wielded is not null)
            {
                Say(manager, notice, $"You can't enter {ModeWord(requested)} mode while wielding the {wielded}");
            } // retail returns silently when the weapon-ready slot's object is unknown

            return null;
        }

        // M4 4.1 v3: the client never asks for a mode the wielded weapons forbid (a melee mode with a caster or a launcher wielded, the case retail's picker cannot produce but a mixed-up setup can: a mace and a wand together). ACE would
        // answer ActionCancelled and flash the wrong stance, and the Action API's guard refuses the send anyway; saying so here keeps the press from creating a refused handle and puts the reason in the chat.
        var wieldedNow = CombatModeGuard.Read(session.State);
        var problem = CombatModeGuard.Judge((int)requested, in wieldedNow);
        if (problem != CombatModeProblem.None)
        {
            manager.Services.Log.Warn("retailui", "combat toggle: " + CombatModeGuard.Describe(problem, (int)requested, in wieldedNow));
            Say(manager, notice, CombatModeGuard.Culprit(problem, in wieldedNow) is { } culprit
                ? $"You can't enter {ModeWord(requested)} mode while wielding the {culprit.Name}"
                : $"[Lifestone] You can't enter {ModeWord(requested)} mode"); // no culprit to name: retail's own text (ClientCombatSystem::SetCombatMode 11.6096 0x0056CB80) always names the weapon, so this line is Lifestone's and says so
            return null;
        }

        if (InPortalSpace(session.State.Self.Travel))
        {
            Say(manager, notice, PortalSpaceText);
            return null;
        }

        manager.LogOnce("combat:pending-mode", LogSeverity.Warning, "TODO(m3-combat-pending-mode): retail keeps a mode request made while the player's motions are pending and applies it when ready (ClientCombatSystem::PlayerInReadyPosition); Lifestone sends it at once and ACE queues it");
        return session.Actions.SetCombatMode(requested, ActionSource.Ui);
    }

    /// <summary>Retail's refusal text for a combat press during a teleport (<c>SetCombatMode</c>, 11.6096 0x0056CB80).</summary>
    public const string PortalSpaceText = "You can't enter combat mode while in portal space";

    /// <summary>
    /// <c>CPlayerSystem::teleportInProgress</c> read from the travel state: an F751 teleport is announced and its destination position has not arrived. Arrival (<c>Arriving</c>, <c>TeleportArrived</c>: the login's own, and what the offline stand-in stays in)
    /// is not counted. The flag's exact setting and clearing are not read: <c>TODO(m3-portal-space-flag)</c>.
    /// </summary>
    public static bool InPortalSpace(SelfTravel travel) => travel is SelfTravel.TeleportAnnounced;

    /// <summary>
    /// <c>ClientCombatSystem::CompatibleCombatMode</c> (11.6096 0x0056B2C0) over the equipped slots: missile needs a missile weapon (0x400000), magic a held item (0x1000000); melee fails when something is in the melee, missile, held or
    /// two-handed slots (0x3500000) but nothing in melee (0x100000) or two-handed (0x2000000). Peace always fits. <paramref name="wielded"/> is the name retail prints (the weapon-ready slot's item), or null when none is known.
    /// </summary>
    public static bool Compatible(GameState state, CombatMode mode, out string? wielded)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var ok = mode switch
        {
            CombatMode.Missile => inventory.EquippedAt((uint)EquipMask.MissileWeapon) is not null,
            CombatMode.Magic => inventory.EquippedAt((uint)EquipMask.Held) is not null,
            CombatMode.Melee => inventory.EquippedAt((uint)(EquipMask.MeleeWeapon | EquipMask.TwoHanded)) is not null
                || inventory.EquippedAt(0x3500000u) is null,
            _ => true,
        };
        wielded = null;
        if (!ok && inventory.EquippedAt(0x3500000u) is { } guid && state.Objects.TryGet(guid, out var item))
        {
            wielded = item.Name;
        }

        return ok;
    }

    /// <summary>The mode word of retail's refusal line; the string table of <c>CombatEnumMapper::CombatModeToString</c> (0x005CAAF0) was not read: <c>TODO(m3-combat-mode-word)</c>.</summary>
    public static string ModeWord(CombatMode mode) => mode switch
    {
        CombatMode.Melee => "melee",
        CombatMode.Missile => "missile",
        CombatMode.Magic => "magic",
        _ => "combat",
    };

    private static void Say(UiElementManager manager, Action<string>? notice, string line)
    {
        manager.Services.Log.Info("retailui", line);
        notice?.Invoke(line);
    }
}

/// <summary>
/// The combat window's holding floaty (<c>gmFloatyCombatPanelUI</c>, class 0x10000054, window 0x100006B5): a plain container that shows while one of its panel windows (a child with a panel id, property 0x10000029: the
/// combat window 0x1000005C, the spellcasting window 0x10000061) shows and hides when none does. Retail's <c>gmCombatPanelUI</c> hears each child's visibility message (<c>ListenToElementMessage</c>, message 24) and
/// answers <c>RecvNotice_SetPanelVisibility</c> (11.4186 0x004F5900); Lifestone keeps the one rule that matters here (the container follows its windows) without the host's remembered-panel stack. The environment floaty
/// (<see cref="UiFloatyEnvPanel"/>: the container window, the vendor's) sits at the same place (0,400), and retail's host shows one floaty at a time: while the environment floaty shows, this one is hidden, and it shows again when the
/// environment floaty hides if a window of it still shows (M4 2.2 fix2, 2.2-R12; the host's function for this is not read, <c>TODO(m4-floaty-stack-retail)</c>, address unknown).
/// </summary>
public sealed class UiFloatyCombatPanel : UiElement
{
    /// <summary>The class id.</summary>
    public const uint RetailClassId = 0x10000054;

    /// <summary>A floaty of <paramref name="init"/>.</summary>
    public UiFloatyCombatPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>Shows the floaty when one of its panel windows is visible and the environment floaty beside it is not, hides it otherwise. Allocation-free once the TODO is logged.</summary>
    public void UpdateVisibility()
    {
        var any = false;
        var children = Children;
        for (var i = 0; i < children.Count && !any; i++)
        {
            any = children[i].Visible && children[i].TryGetEnumProperty(UiPropertyIds.ElementPanelId, out var id) && id != 0;
        }

        if (any && EnvironmentFloatyShows())
        {
            any = false; // one floaty at a time: the environment floaty is over this one's place
            Manager.LogOnce("floaty:stack", LogSeverity.Warning, "gmFloatyCombatPanelUI: hidden while gmFloatyEnvPanelUI shows (they share the place 0,400; retail's host keeps one floaty at a time with its remembered-panel stack, TODO(m4-floaty-stack-retail), address unknown); it shows again when the environment floaty hides");
        }

        if (Visible != any)
        {
            SetVisible(any);
        }
    }

    private bool EnvironmentFloatyShows()
    {
        var siblings = Parent?.Children;
        for (var i = 0; siblings is not null && i < siblings.Count; i++)
        {
            if (siblings[i] is UiFloatyEnvPanel { Visible: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The floaty starts hidden with its windows (<see cref="GameCombatPanel"/> and <see cref="GameSpellcastingPanel"/> hide in their <c>PostInit</c>, which runs before this one's base returns), so no build frame draws it in NonCombat.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        UpdateVisibility();
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityChanged && ReferenceEquals(message.Element.Parent, this) && message.Element.TryGetEnumProperty(UiPropertyIds.ElementPanelId, out var id) && id != 0)
        {
            UpdateVisibility();
        }

        return base.ListenToElementMessage(message);
    }
}

/// <summary>
/// The combat window (<c>gmCombatUI</c>, class 0x1000000C, window 0x1000005C of the gameplay layout; 11.6096 0x004CCC20 to 0x004CD210, 11 functions): the power slider, the power meter, three check boxes and the three attack
/// height buttons. Retail's class is its own element, so Lifestone's is too: registered for the class, it hears its children's messages and the session's combat mode (<see cref="Attach"/>).
/// </summary>
/// <remarks>
/// <para><b>Shown by the mode</b> (<c>RecvNotice_SetCombatMode</c>, 0x004CD210). With the advanced combat UI option on, the window hides in every mode (the power bar window is used instead). Else melee puts it in state
/// 0x10000003 and shows it, missile in state 0x10000004 and shows it, any other mode hides it. Melee also shows the recklessness field (0x100005EF) when the Recklessness skill (50) is trained or specialized; every other mode
/// hides the field, and a move of the power slider re-reads the same rule, so it cannot bring the field back in missile mode (M3 3.2 review finding 14). The floaty that holds it (<see cref="UiFloatyCombatPanel"/>) follows. The toolbar's four combat buttons follow the same mode (<see cref="GameToolbar.SetCombatModeButtons"/>, <c>gmToolbarUI::RecvNotice_SetCombatMode</c> 0x004BE2A0): their
/// notice rides this subscription because the toolbar has no end of life of its own.</para>
/// <para><b>Its controls</b> (<c>PostInit</c> 0x004CCD90, <c>ListenToElementMessage</c> 0x004CD020). The three check boxes (0x10000053 auto-repeat attack, 0x10000054 auto-target, 0x10000055 view combat target) show the
/// player options of those names with the <c>ID_CombatPanelOption_*</c> labels; a click sets the option and sends it (M5 6 u1, D32; the boxes are no longer ghosted and
/// <c>TODO(m3-combat-options)</c> is closed). A mouse down on an attack height button (0x10000057 high, 0x10000058 medium, 0x10000059 low) requests that height; the click is the attack
/// (<c>ClientCombatSystem::EndAttackRequest</c>, 0x0056CE30): <b>it sends the attack through the Action API, with the power <see cref="AttackPowerDriver"/> works out</b> (M4 1.2; the M3 stand-in that answered NotInM3 is gone). The power slider (0x1000004F) sets the requested power
/// (its position as thousandths, clamped to 0..1, <c>m_rUIRequestedPower</c>). Notices the attack code will send: <see cref="SetPowerbarLevel"/> (the meter 0x10000050, power bar mode 1),
/// <see cref="DesiredAttackPowerChanged"/> (the slider's goal) and <see cref="AttackHeightChanged"/> (the group box 0x10000056's selection).</para>
/// <para>Nothing here runs per frame; every change is an event.</para>
/// </remarks>
public sealed class GameCombatPanel : UiElement
{
    /// <summary>The retail class id (<c>gmCombatUI</c>).</summary>
    public const uint RetailClassId = 0x1000000C;

    /// <summary>The window's element id in the gameplay layout.</summary>
    public const uint WindowId = 0x1000005C;

    /// <summary>The state of the window in melee mode.</summary>
    public const uint MeleeState = 0x10000003;

    /// <summary>The state of the window in missile mode.</summary>
    public const uint MissileState = 0x10000004;

    /// <summary>The power slider.</summary>
    public const uint SliderId = 0x1000004F;

    /// <summary>The power meter (inside the slider).</summary>
    public const uint MeterId = 0x10000050;

    /// <summary>The recklessness field (inside the meter).</summary>
    public const uint RecklessnessFieldId = 0x100005EF;

    /// <summary>The attack height group box.</summary>
    public const uint HeightBoxId = 0x10000056;

    /// <summary>The high attack button.</summary>
    public const uint HighButtonId = 0x10000057;

    /// <summary>The medium attack button.</summary>
    public const uint MediumButtonId = 0x10000058;

    /// <summary>The low attack button.</summary>
    public const uint LowButtonId = 0x10000059;

    /// <summary>The Recklessness skill (retail's <c>InqSkillAdvancementClass</c> argument 0x32).</summary>
    public const uint RecklessnessSkill = 0x32;

    /// <summary>The string table enum of the option labels (<c>ID_CombatPanelOption_*</c>).</summary>
    public const uint OptionTableEnum = 0x10000003;

    private static readonly (uint BoxId, string Option, string Label)[] OptionBoxes =
    [
        (0x10000053, "AutoRepeatAttack", "ID_CombatPanelOption_AutoRepeatAttack"),
        (0x10000054, "AutoTarget", "ID_CombatPanelOption_AutoTarget"),
        (0x10000055, "ViewCombatTarget", "ID_CombatPanelOption_ViewCombatTarget"),
    ];

    private readonly List<UiOptionCheckbox> _boxes = new(3);
    private UiLoginContext? _context;
    private CombatToggleKey? _toggleKey;
    private GameOptions? _options;
    private GameToolbar? _toolbar;
    private PlayerOptionDef? _advancedOption;
    private UiElement? _recklessness;
    private UiElement? _meter;
    private UiElement? _slider;
    private UiElement? _heights;
    private IDisposable? _modeSubscription;
    private IDisposable? _qualitySubscription;
    private PowerBarNotices? _notices;
    private AttackTick? _tick;

    /// <summary>A combat window of <paramref name="init"/>.</summary>
    public GameCombatPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The combat mode the window last took (<see cref="CombatModes"/>; 0 before any).</summary>
    public int Mode { get; private set; }

    /// <summary>The attack height requested (1 high, 2 medium, 3 low; retail's <c>requestedAttackHeight</c>, medium at first as the group box's default button).</summary>
    public int RequestedHeight { get; private set; } = 2;

    /// <summary>The attack power requested (0..1; the slider's position).</summary>
    public float RequestedPower { get; private set; } = 0.5f;

    /// <summary>The attack side (<c>ClientCombatSystem</c>'s request, build and send; M4 1.3 s1), null until <see cref="Attach"/>.</summary>
    public AttackPowerDriver? Attacks { get; private set; }

    /// <summary>The selected object (0 when none): the target of a click on an attack button. The screen gives the game's selection; a test gives its own.</summary>
    public Func<uint>? SelectedProvider { get; set; }

    /// <summary>Where the combat system's chat lines go (the screen's chat); null only logs them.</summary>
    public Action<string>? ChatNotice { get; set; }

    /// <summary>True when the player's advanced combat UI option is on (the window never shows then).</summary>
    public bool AdvancedCombatUi => _advancedOption is not null && _options is not null && _options.Get(_advancedOption);

    /// <summary>The three option check boxes.</summary>
    public IReadOnlyList<UiOptionCheckbox> OptionCheckboxes => _boxes;

    /// <summary>The combat key's registration (retail's accent-grave key, see <see cref="CombatToggleKey"/>), or null before <see cref="Attach"/>.</summary>
    public CombatToggleKey? ToggleKey => _toggleKey;

    /// <summary>The window's recklessness field (null when the layout lacks it).</summary>
    public UiElement? RecklessnessField => _recklessness;

    /// <summary>The attack height button of <paramref name="height"/> (1, 2, 3), or null.</summary>
    public UiElement? HeightButton(int height) => _heights?.GetChild(HeightButtonId(height));

    /// <summary>
    /// <c>PostInit</c>: finds the controls and hides the recklessness field. Subscribes to nothing until <see cref="Attach"/>. The window also hides itself (and its floaty follows) so that no frame of the shell's staged build draws it in
    /// NonCombat: the layout shows it by default and <see cref="Attach"/> shows it by mode (3.2-R9).
    /// </summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _recklessness = GetChildRecursive(RecklessnessFieldId);
        _recklessness?.SetVisible(false);
        SetVisible(false);
        (Parent as UiFloatyCombatPanel)?.UpdateVisibility();
        _meter = GetChildRecursive(MeterId);
        _slider = GetChildRecursive(SliderId);
        _heights = GetChildRecursive(HeightBoxId);
        foreach (var (boxId, _, _) in OptionBoxes)
        {
            if (GetChildRecursive(boxId) is UiOptionCheckbox box)
            {
                _boxes.Add(box);
            }
        }
    }

    /// <summary>
    /// Hands the window the session: it labels its check boxes, follows <see cref="CombatModeChanged"/> (and the description, which may carry the mode) and takes the mode the session holds now. The
    /// <paramref name="toolbar"/>, when given, has its combat buttons driven by the same notice.
    /// </summary>
    public void Attach(UiLoginContext context, GameOptions? options, GameToolbar? toolbar)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _options = options;
        _toolbar = toolbar;
        foreach (var def in PlayerOptionTable.All)
        {
            if (def.Name == "AdvancedCombatUI")
            {
                _advancedOption = def;
            }
        }

        LabelBoxes();
        _modeSubscription = context.Session.Events.Subscribe<CombatModeChanged>(OnCombatModeChanged);
        _qualitySubscription = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        _notices = new PowerBarNotices(this);
        _tick = new AttackTick(this);
        Attacks = new AttackPowerDriver(
            context.Session,
            _notices,
            () => context.Manager.Now,
            () => SelectedProvider?.Invoke() ?? 0u,
            () => RequestedPower,
            () => AdvancedCombatUi,
            line =>
            {
                context.Manager.Services.Log.Info("retailui", line);
                ChatNotice?.Invoke(line);
            },
            wanted => _tick.Want(wanted),
            todo => context.Manager.LogOnce("combat:" + todo[..Math.Min(todo.Length, 48)], LogSeverity.Warning, todo));
        _toggleKey = new CombatToggleKey(context.Session, context.Manager, () => ChatNotice); // retail's combat key (M4 4.1 v3)
        context.Manager.Services.Log.Warn("retailui", "TODO(m3-combat-tooltips): the combat window's check boxes have no tooltips (the _Help strings are not shown)");
        SetCombatMode(context.Session.State.Self.Qualities.CombatMode);
    }

    /// <summary>
    /// <c>gmCombatUI::RecvNotice_SetCombatMode</c> (11.6096 0x004CD210) and <c>gmToolbarUI::RecvNotice_SetCombatMode</c> (0x004BE2A0): shows or hides the window for <paramref name="mode"/> (<see cref="CombatModes"/>) and
    /// shows the toolbar's button for it.
    /// </summary>
    public void SetCombatMode(int mode)
    {
        Mode = mode;
        _toolbar?.SetCombatModeButtons(mode);
        if (AdvancedCombatUi)
        {
            SetVisible(false);
        }
        else if (mode == CombatModes.Melee)
        {
            SetState(MeleeState);
            SetVisible(true);
            RefreshOptions();
        }
        else if (mode == CombatModes.Missile)
        {
            SetState(MissileState);
            SetVisible(true);
            RefreshOptions();
        }
        else
        {
            SetVisible(false);
        }

        UpdateRecklessness(); // after the state (a state change re-applies the state's own attributes): melee shows the field by skill, every other mode hides it
        (Parent as UiFloatyCombatPanel)?.UpdateVisibility();
    }

    /// <summary><c>RecvNotice_SetPowerbarLevel</c> (0x004CCCD0): the meter shows <paramref name="level"/> in power bar mode 1 (<see cref="PowerBarMode.Combat"/>), the basic combat UI's own meter; other modes are the power bar window's.</summary>
    public void SetPowerbarLevel(PowerBarMode mode, float level)
    {
        if (mode == PowerBarMode.Combat)
        {
            _meter?.SetFloatProperty(UiPropertyIds.MeterPosition, level);
        }
    }

    /// <summary><c>RecvNotice_DesiredAttackPowerChanged</c> (0x004CCD00): the slider's goal is <paramref name="level"/>.</summary>
    public void DesiredAttackPowerChanged(float level) => _slider?.SetFloatProperty(UiPropertyIds.ScrollbarGoalPosition, level);

    /// <summary><c>RecvNotice_AttackHeightChanged</c> (0x004CCC70): the group box selects the button of <paramref name="height"/> (1 high, 2 medium, 3 low).</summary>
    public void AttackHeightChanged(int height)
    {
        if (_heights is not null && height is >= 1 and <= 3)
        {
            _heights.SetEnumProperty(UiPropertyIds.GroupBoxSelectedButton, HeightButtonId(height));
        }
    }

    /// <summary>The window's messages (<c>ListenToElementMessage</c>, 0x004CD020): a mouse down on a height button requests that height, a click on one is the attack (the Action API), the slider sets the requested power.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var id = message.ElementId;
        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            if (id is HighButtonId or MediumButtonId or LowButtonId)
            {
                // EndAttackRequest (0x0056CE30): the click sends the attack through the Action API (the closed TODO(m3-melee-attack)).
                Attacks?.ReleaseHeight(HeightOf(id));
                return UiListenResult.Handled;
            }

            if (message.Element is UiOptionCheckbox box && _boxes.Contains(box))
            {
                OptionBoxClicked(box);
                return UiListenResult.Handled;
            }
        }
        else if (message.MessageId == UiMessageIds.MouseDown && id is HighButtonId or MediumButtonId or LowButtonId)
        {
            RequestedHeight = id == HighButtonId ? 1 : id == MediumButtonId ? 2 : 3; // SetRequestedAttackHeight (0x0056D640): and so StartAttackRequest
            Attacks?.PressHeight(HeightOf(id));
        }
        else if (message.MessageId == UiMessageIds.ScrollbarPosition && id == SliderId)
        {
            RequestedPower = Math.Clamp(message.Param1 * 0.001f, 0f, 1f);
            UpdateRecklessness();
            return UiListenResult.Handled;
        }

        return base.ListenToElementMessage(message);
    }

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        _modeSubscription?.Dispose();
        _qualitySubscription?.Dispose();
        _modeSubscription = _qualitySubscription = null;
        _tick?.Want(false);
        Attacks?.Dispose();
        Attacks = null;
        _toggleKey?.Dispose();
        _toggleKey = null;
        base.OnDeleting();
    }

    private static AttackHeight HeightOf(uint id) => id == HighButtonId ? AttackHeight.High : id == LowButtonId ? AttackHeight.Low : AttackHeight.Medium;

    private static uint HeightButtonId(int height) => height switch { 1 => HighButtonId, 3 => LowButtonId, _ => MediumButtonId };

    private void OnCombatModeChanged(in CombatModeChanged change) => SetCombatMode(change.Mode);

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description && _context is { } context)
        {
            SetCombatMode(context.Session.State.Self.Qualities.CombatMode); // the description may carry the mode; the skill class may have changed
        }
    }

    // RecvNotice_SetCombatMode's rule for the field (11.6096 0x004CD210): melee shows it while the skill is trained or specialized (class 2 or more); any other mode hides it. The slider's move re-reads the same rule, so a move in
    // missile mode shows nothing (M3 3.2 review finding 14). Whether the missile state's own attributes also hide the child is not known without the layout dump, so the window does not rely on them.
    private void UpdateRecklessness()
    {
        if (_recklessness is null || _context is null)
        {
            return;
        }

        var trained = SkillRules.ClassOf(_context.Session.State.Self.Qualities, RecklessnessSkill) >= 2;
        _recklessness.SetVisible(Mode == CombatModes.Melee && trained);
    }

    // PostInit's SetPlayerOption and labels: each box shows its option, labelled with the combat panel's own string.
    private void LabelBoxes()
    {
        var strings = _context?.Manager.Services.Strings;
        for (var i = 0; i < _boxes.Count; i++)
        {
            if (strings is not null && strings.TryReference(OptionTableEnum, OptionBoxes[i].Label, out var label))
            {
                _boxes[i].SetStringInfo(label);
            }
        }

        RefreshOptions();
    }

    // Each box shows the option's value (M5 6 u1: none is ghosted any more).
    private void RefreshOptions()
    {
        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            PlayerOptionDef? def = null;
            foreach (var candidate in PlayerOptionTable.All)
            {
                if (candidate.Name == OptionBoxes[i].Option)
                {
                    def = candidate;
                    break;
                }
            }

            box.Option = def;
            var value = def is not null && _options is not null && _options.Get(def);
            box.Saved = value;
            box.Current = value;
        }
    }

    // The combat system's notices (CM_Character::SendNotice_*): the combat window's own meter for power bar mode 1, the power bar window for the others.
    private sealed class PowerBarNotices(GameCombatPanel panel) : IPowerBarNotices
    {
        private GamePowerBar? _bar;

        private GamePowerBar? Bar => _bar ??= panel.GetWindowRoot()?.GetChildRecursive(GamePowerBar.WindowId) as GamePowerBar;

        public void Begin(PowerBarMode mode) => Bar?.Begin(mode);

        public void SetLevel(PowerBarMode mode, float level)
        {
            panel.SetPowerbarLevel(mode, level);
            Bar?.SetLevel(mode, level);
        }

        public void Finish(PowerBarMode mode) => Bar?.Finish(mode);
    }

    // The per-frame hook (UseTime), registered with the manager only while a build or a sent attack waits: an idle window costs nothing.
    private sealed class AttackTick(GameCombatPanel panel) : IUiTickTarget
    {
        private bool _registered;

        public void Want(bool wanted)
        {
            if (wanted == _registered)
            {
                return;
            }

            _registered = wanted;
            if (wanted)
            {
                panel.Manager.RegisterTick(this);
            }
            else
            {
                panel.Manager.UnregisterTick(this);
            }
        }

        public void Tick(double now) => panel.Attacks?.Tick();
    }

    // M5 6 u1 (D32): the combat window's option boxes are settable like the options panel's: a click sets the option and sends both words (GameOptions.Apply); a refused send puts the box back.
    private void OptionBoxClicked(UiOptionCheckbox box)
    {
        if (!box.TogglesItself)
        {
            box.Current = !box.Current;
        }

        if (box.Option is { } option && _options is not null && _options.TrySet(option, box.Current))
        {
            box.Saved = box.Current;
            return;
        }

        box.Current = box.Saved;
    }
}
