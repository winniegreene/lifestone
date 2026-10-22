namespace Lifestone.Client.Session.Contracts;

/// <summary>What an <see cref="ActionHandle"/> is for. One value per <see cref="IActionApi"/>, <see cref="PlayerTools"/> and
/// <see cref="MoveIntents"/> member.</summary>
public enum ActionKind
{
    /// <summary><see cref="IActionApi.Login"/>.</summary>
    Login,

    /// <summary><see cref="IActionApi.SelectCharacter"/>.</summary>
    SelectCharacter,

    /// <summary><see cref="IActionApi.CreateCharacter"/>.</summary>
    CreateCharacter,

    /// <summary><see cref="IActionApi.DeleteCharacter"/>.</summary>
    DeleteCharacter,

    /// <summary><see cref="IActionApi.RestoreCharacter"/>.</summary>
    RestoreCharacter,

    /// <summary><see cref="IActionApi.EnterWorld"/>.</summary>
    EnterWorld,

    /// <summary><see cref="IActionApi.LoginComplete"/>.</summary>
    LoginComplete,

    /// <summary><see cref="IActionApi.Use"/>.</summary>
    Use,

    /// <summary><see cref="IActionApi.QueryHealth"/>.</summary>
    QueryHealth,

    /// <summary><see cref="IActionApi.Talk"/>.</summary>
    Talk,

    /// <summary><see cref="IActionApi.Tell"/>.</summary>
    Tell,

    /// <summary><see cref="IActionApi.LogOff"/>.</summary>
    LogOff,

    /// <summary><see cref="IActionApi.Quit"/>.</summary>
    Quit,

    /// <summary><see cref="PlayerTools.Status"/>.</summary>
    ToolStatus,

    /// <summary><see cref="PlayerTools.Place"/>.</summary>
    ToolPlace,

    /// <summary><see cref="PlayerTools.WhereAmI"/>.</summary>
    ToolWhereAmI,

    /// <summary><see cref="PlayerTools.AddXp"/>.</summary>
    ToolAddXp,

    /// <summary><see cref="PlayerTools.StampQuest"/>, <see cref="PlayerTools.EraseQuest"/> and <see cref="PlayerTools.HasQuest"/>.</summary>
    ToolQuest,

    /// <summary><see cref="PlayerTools.PurgeCharacter"/>.</summary>
    ToolCharPurge,

    /// <summary>A <see cref="MoveIntents"/> intent (automation driving the player's movement controller).</summary>
    MoveIntent,

    /// <summary><see cref="IActionApi.RaiseAttribute"/> (M3 1.1; appended, so the values above stay stable).</summary>
    RaiseAttribute,

    /// <summary><see cref="IActionApi.RaiseVital"/>.</summary>
    RaiseVital,

    /// <summary><see cref="IActionApi.RaiseSkill"/>.</summary>
    RaiseSkill,

    /// <summary><see cref="IActionApi.TrainSkill"/>.</summary>
    TrainSkill,

    /// <summary><see cref="PlayerTools.LearnSpells"/> (M3 1.1 s2b; appended).</summary>
    ToolSpell,

    /// <summary><see cref="PlayerTools.SpawnItem"/>.</summary>
    ToolItem,

    /// <summary><see cref="IActionApi.Cast(uint, ActionSource)"/> and <see cref="IActionApi.Cast(uint, uint, ActionSource)"/> (M3 1.2 s4a; appended).</summary>
    CastSpell,

    /// <summary><see cref="IActionApi.SetCombatMode"/>.</summary>
    SetCombatMode,

    /// <summary><see cref="IActionApi.Wield"/>.</summary>
    Wield,

    /// <summary><see cref="IActionApi.Unwield"/>.</summary>
    Unwield,

    /// <summary><see cref="IActionApi.MoveToPack"/>.</summary>
    MoveToPack,

    /// <summary><see cref="IActionApi.MergeStack"/> (M3 1.4 fix A; appended).</summary>
    MergeStack,

    /// <summary><see cref="IActionApi.Suicide"/> (M3 3.2 s0; appended).</summary>
    Suicide,

    /// <summary><see cref="IActionApi.AddSpellFavorite"/> (M3 3.2 s0).</summary>
    AddSpellFavorite,

    /// <summary><see cref="IActionApi.RemoveSpellFavorite"/> (M3 4.2 p1).</summary>
    RemoveSpellFavorite,

    /// <summary><see cref="IActionApi.Attack"/> (M4 1.2 s2; appended).</summary>
    Attack,

    /// <summary><see cref="IActionApi.MissileAttack"/>.</summary>
    MissileAttack,

    /// <summary><see cref="IActionApi.CancelAttack"/>.</summary>
    CancelAttack,

    /// <summary><see cref="IActionApi.OpenContainer"/> (M4 2.1 s2; appended).</summary>
    OpenContainer,

    /// <summary><see cref="IActionApi.CloseContainer"/>.</summary>
    CloseContainer,

    /// <summary><see cref="IActionApi.PickUp"/>.</summary>
    PickUp,

    /// <summary><see cref="IActionApi.Drop"/>.</summary>
    Drop,

    /// <summary><see cref="IActionApi.SplitToContainer"/>.</summary>
    SplitToContainer,

    /// <summary><see cref="IActionApi.SplitTo3D"/>.</summary>
    SplitTo3D,

    /// <summary><see cref="IActionApi.SplitToWield"/>.</summary>
    SplitToWield,

    /// <summary><see cref="IActionApi.Give"/>.</summary>
    Give,

    /// <summary><see cref="IActionApi.UseWithTarget"/> (M4 3.1 s1; appended).</summary>
    UseWithTarget,

    /// <summary><see cref="IActionApi.Drink"/>.</summary>
    Drink,

    /// <summary><see cref="IActionApi.AddShortcut"/> (M4 3.2 h1; appended).</summary>
    AddShortcut,

    /// <summary><see cref="IActionApi.RemoveShortcut"/>.</summary>
    RemoveShortcut,

    /// <summary><see cref="IActionApi.UseShortcut"/>: only the refused or not-built answer carries this kind; a routed use returns the underlying action's handle.</summary>
    UseShortcut,
    /// <summary><see cref="IActionApi.Identify"/> (M4 3.2 s2; appended).</summary>
    Identify,

    /// <summary><see cref="IActionApi.OpenVendor"/> (M4 3.2 s1; appended).</summary>
    OpenVendor,

    /// <summary><see cref="IActionApi.Buy"/>.</summary>
    Buy,

    /// <summary><see cref="IActionApi.Sell"/>.</summary>
    Sell,
}

/// <summary>The height of an attack (ACE's <c>AttackHeight</c>, the value on the wire; retail's group box 0x10000056 selects it).</summary>
public enum AttackHeight : uint
{
    /// <summary>A high attack.</summary>
    High = 1,

    /// <summary>A medium attack.</summary>
    Medium = 2,

    /// <summary>A low attack.</summary>
    Low = 3,
}

/// <summary>Where an <see cref="ActionHandle"/> is in its life.</summary>
public enum ActionStatus
{
    /// <summary>Accepted and waiting: for its turn (one session-phase action at a time), or for a rate limit.</summary>
    Queued,

    /// <summary>Sent to the server; waiting for the confirmation.</summary>
    Sent,

    /// <summary>Finished; <see cref="ActionHandle.Outcome"/> says how.</summary>
    Completed,
}
