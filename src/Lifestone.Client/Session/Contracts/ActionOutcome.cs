namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// How an action ended (<see cref="ActionHandle.Outcome"/>). An action is never reported done because it was queued or
/// sent: it ends on the server's confirmation, the server's error, a timeout, or a refusal (A0's §1.3).
/// </summary>
/// <param name="Kind">The kind of ending.</param>
/// <param name="Code">The server's code for <see cref="ActionOutcomeKind.ServerError"/> (a CharGenResult, a CharacterError or a
/// WeenieError), else 0.</param>
/// <param name="Detail">One sentence for the log and the screen (why it was refused, the TODO it hit, what the server
/// said), or null. Never holds a secret.</param>
/// <param name="Reason">M5 7 D141 ("check before, act, check after"): the typed precondition or result an action failed on (a refusal before the send, a server
/// answer, a result the server turned into something else), so recovery can fix it and retry once; <see cref="ActionReason.None"/> when the action succeeded or the
/// failure is not typed yet. <see cref="Detail"/> still carries the sentence.</param>
public readonly record struct ActionOutcome(ActionOutcomeKind Kind, uint Code = 0, string? Detail = null, ActionReason Reason = ActionReason.None)
{
    /// <summary>True for the endings that mean the action did what was asked: confirmed, local or sent.</summary>
    public bool Succeeded => Kind is ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local or ActionOutcomeKind.Sent;
}

/// <summary>The kinds of <see cref="ActionOutcome"/>.</summary>
public enum ActionOutcomeKind
{
    /// <summary>The server confirmed it (the action's confirmation arrived: a message, an echo, a state change).</summary>
    Confirmed,

    /// <summary>Done without a server round trip, because none exists (selecting a character).</summary>
    Local,

    /// <summary>Sent, and the protocol has no reply to wait for (the Disconnect that ends a quit).</summary>
    Sent,

    /// <summary>The server answered with an error; <see cref="ActionOutcome.Code"/> holds its code.</summary>
    ServerError,

    /// <summary>No confirmation within the action's timeout (on the session clock).</summary>
    TimedOut,

    /// <summary>
    /// Not sent: a precondition failed (wrong phase, invalid arguments, an offline session, a non-loopback server, the
    /// kill switch). <see cref="ActionOutcome.Detail"/> says which.
    /// </summary>
    Refused,

    /// <summary>The action is not built yet: a loud stub (A0's R7), logged as an ERROR with its <c>TODO(state)</c>.</summary>
    NotImplemented,

    /// <summary>Withdrawn before it completed: the kill switch cancelled a queued automation action, or the session ended.</summary>
    Cancelled,

    /// <summary>An automation action under dry run: logged with what it would have sent, never sent.</summary>
    DryRun,
}

/// <summary>
/// M5 7 D141: why an action failed, typed (<see cref="ActionOutcome.Reason"/>). A before-check names the precondition it found missing (the action is then
/// <see cref="ActionOutcomeKind.Refused"/>, nothing sent); an after-check names the server's answer or the result that differed from the one asked
/// (<see cref="ActionOutcomeKind.ServerError"/>). Recovery reads it to fix the precondition and retry once.
/// </summary>
public enum ActionReason : byte
{
    /// <summary>No typed reason: the action succeeded, or its failure is not typed yet (the detail says why).</summary>
    None,

    /// <summary>The session is not in the world (the phase, or the player not known yet).</summary>
    NotInWorld,

    /// <summary>The player is not in the magic combat mode a cast needs (the stance guard).</summary>
    NotInMagicStance,

    /// <summary>Too little mana for the spell (before: certain from the state; after: ACE's 0x0401).</summary>
    NotEnoughMana,

    /// <summary>A spell component is missing (ACE's 0x0400).</summary>
    NoComponents,

    /// <summary>The spell fizzled (ACE's 0x0402).</summary>
    Fizzled,

    /// <summary>The object the action names is gone: no longer in the object table, or ACE's NoObject (0x0038) or TargetNotAcquired (0x042C).</summary>
    TargetGone,

    /// <summary>The target is out of range (ACE's 0x0406 or 0x0550).</summary>
    OutOfRange,

    /// <summary>The player does not hold an item the action needs (a stack, a kit, a potion, a stone, an item to sell).</summary>
    NotHeld,

    /// <summary>Two stacks to merge are different items (ACE's 0x0427).</summary>
    NotSameItem,

    /// <summary>An item to merge has no stack size: it does not stack.</summary>
    NotStackable,

    /// <summary>A stack holds fewer than the amount asked.</summary>
    StackShort,

    /// <summary>The target stack cannot take the amount (its maximum).</summary>
    StackFull,

    /// <summary>The packs have too few free slots for what the action brings in.</summary>
    NoFreeSlot,

    /// <summary>The player is too encumbered (ACE's 0x002A).</summary>
    Overloaded,

    /// <summary>The player's pyreals do not cover the price.</summary>
    NotEnoughCoin,

    /// <summary>Too little unspent experience for the raise.</summary>
    NotEnoughXp,

    /// <summary>Too few skill credits for the training.</summary>
    NotEnoughCredits,

    /// <summary>The skill is not trained (a raise of an untrained skill, a kit without Healing: ACE's 0x04FC).</summary>
    NotTrained,

    /// <summary>Already done: the skill is trained, the item full of mana.</summary>
    AlreadyDone,

    /// <summary>ACE is busy (0x001D): a wait, then the same action again.</summary>
    Busy,

    /// <summary>The container is locked.</summary>
    Locked,

    /// <summary>The vital the heal would raise is full.</summary>
    VitalFull,

    /// <summary>The server ended the action without its effect (a UseDone 0 and nothing changed).</summary>
    NoEffect,

    /// <summary>The server answered, but the result differs from the one asked (the after-check's counts or coins).</summary>
    ContraryResult,

    /// <summary>M5 7 D139 (l): the magic combat mode needs a caster in the hands and none is held (the before-check), or the server answered the magic mode with peace while none is held (ACE <c>Player_Combat.cs</c> 872-882).</summary>
    NoCasterHeld,
}

/// <summary>M5 7 D141: the reasons the server's codes carry (the WeenieErrors whose meaning is one <see cref="ActionReason"/> whatever the action).</summary>
public static class ActionReasons
{
    /// <summary>The reason a server code names, or <see cref="ActionReason.None"/> for a code with no single meaning (ACE WeenieError.cs @95ef426d).</summary>
    /// <param name="code">The WeenieError (an outcome's <see cref="ActionOutcome.Code"/>).</param>
    public static ActionReason FromCode(uint code) => code switch
    {
        0x001D => ActionReason.Busy, // YoureTooBusy
        0x002A => ActionReason.Overloaded, // YouAreTooEncumbered
        0x0038 => ActionReason.TargetGone, // NoObject
        0x0400 => ActionReason.NoComponents, // YouDontHaveAllTheComponents
        0x0401 => ActionReason.NotEnoughMana, // YouDontHaveEnoughManaToCast
        0x0402 => ActionReason.Fizzled, // YourSpellFizzled
        0x0406 or 0x0550 => ActionReason.OutOfRange, // MagicTargetOutOfRange, MissileOutOfRange
        0x0427 => ActionReason.NotSameItem, // YouCannotMergeDifferentStacks
        0x042C => ActionReason.TargetGone, // TargetNotAcquired
        0x04FC => ActionReason.NotTrained, // YouArentTrainedInHealing
        _ => ActionReason.None,
    };
}
