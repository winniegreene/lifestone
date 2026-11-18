using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What <see cref="HealTracker"/> and <see cref="PotionTracker"/> share (M4 3.1 s1): the <see cref="ActionLane.Heal"/> lane, the in-world precondition, the UseDone that ends the action, the refusal ACE words, the typed result and the busy rule.
/// <para><b>The order</b> (capture.md C1 to C3, live, ACE 95ef426d): a use is F74C at +10 ms, the effect (a kit at +1.48 s with its 02CE and F7E0 line just before the UseDone; a potion's 02E9, F7E0 line and 0197 or 0024 at +0.74 s), GameEvent
/// 0x01C7 UseDone 0 at +1.48 s, and the player's own Ready F74C with it, 10 to 30 ms later (a potion's comes at +0.76 s, before the UseDone). So the action completes on the UseDone, when every line is in.</para>
/// <para><b>The busy rule.</b> ACE is busy until the Ready F74C: a second heal sent 0.2 s after the first was refused 0x001D at +2 ms, and one sent AT the UseDone (8 ms before the Ready) was refused 0x001D too. After a confirmed use the lane
/// holds the next heal until the UseDone and the player's own Ready F74C have both arrived and <see cref="ActionPolicy.BusyChainMarginSeconds"/> has passed; a recording without the F74C frees it <see cref="ActionPolicy.ReturnStanceWaitSeconds"/>
/// after the UseDone (the mechanism of the Inventory lane's hold: <see cref="ItemMoveTracker"/>). A use that ended in a refusal holds nothing: ACE ran no chain for it.</para>
/// <para><b>The refusals.</b> A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/> with that code, named in the detail. A refusal ACE sends as a line before a UseDone 0 (a kit: 0x028B 0x04FF at a full health, 0x02EB text at a full stamina or
/// mana, from <c>Healer.HandleActionUseOnTarget</c>; a potion: 0x028A 0x001D, 0x04EB, from <c>Player_Use</c>) is latched, and ends the action as a <see cref="ActionOutcomeKind.ServerError"/> with its code and text when the use left no trace.
/// <c>TODO(m4-heal-full-vital-live)</c>: the full-vital refusals are read from ACE's source, not captured live; s4's live test records them.</para>
/// </summary>
internal abstract class HealLaneTracker : ActionTracker
{
    /// <summary>ACE's <c>YouArentTrainedInHealing</c>: the UseDone code of a kit used by a character whose Healing is not trained (capture C4: at +2 ms, nothing else sent).</summary>
    internal const uint NotTrainedCode = 0x04FC;

    /// <summary>ACE's <c>YoureTooBusy</c>: ACE is busy (capture C2: a repeat inside the previous use).</summary>
    internal const uint TooBusyCode = 0x001D;

    /// <summary>ACE's <c>YouCantHealThat</c>: the kit's target is not a player.</summary>
    internal const uint CantHealThatCode = 0x04FE;

    /// <summary>ACE's <c>YouCantDoThatWhileInTheAir</c>: the player is jumping.</summary>
    internal const uint InTheAirCode = 0x04EB;

    /// <summary>ACE's 0x028B <c>_IsAtFullHealth</c>: the kit's vital is full (not captured live: <c>TODO(m4-heal-full-vital-live)</c>).</summary>
    internal const uint FullHealthCode = 0x04FF;

    private ActionApi? _api;
    private bool _useDoneSeen;
    private double _useDoneAt = double.NaN;
    private double _readyAt = double.NaN;
    private uint _refusalCode;
    private string? _refusalText;
    private HealResult? _line;
    private HealResult? _result;

    /// <summary>A tracker of an action on <paramref name="item"/>.</summary>
    protected HealLaneTracker(HealHandle handle, uint item)
        : base(handle, ActionLane.Heal, ActionPolicy.HealSeconds)
    {
        Item = item;
    }

    /// <summary>The kit or the potion.</summary>
    protected uint Item { get; }

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    /// <remarks>M6 14 recall unblock (live 2026-11-14 17:32:03 UTC: a Health Tonic's UseDone went to an older debt, the drink stayed in flight 20 s and no cast could go): the kit's Structure or the potion's stack fell since the send, so ACE ran this use and its UseDone is this one's.</remarks>
    internal override bool ClaimsUseDone(ActionApi api) => !_useDoneSeen && _api is not null && Spent;

    /// <inheritdoc/>
    /// <remarks>M5 7 D141's before-check: in the world, and the kit or the potion held by the player (ACE answers an item it cannot find with a bare UseDone and nothing else: "nothing happened").</remarks>
    internal override string? Refusal(ActionApi api) =>
        WorldTrackers.NotInWorld(api) ?? (api.State.Self.Inventory.Find(Item).Held ? null : Refuse(ActionReason.NotHeld, "the item " + ActionText.Hex(Item) + " to use is not held by the player"));

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _api = api;
        _useDoneSeen = false;
        _useDoneAt = _readyAt = double.NaN;
        _refusalCode = 0;
        _refusalText = null;
        _line = null;
        _result = null;
        Begin(api);
        Transmit(session!);
        return null;
    }

    /// <summary>Reads what the model holds of the item before the send (the baseline a fall is measured from).</summary>
    protected abstract void Begin(ActionApi api);

    /// <summary>Sends the action's message.</summary>
    protected abstract void Transmit(IAcSession session);

    /// <summary>True when the model saw the use spent: the kit's Structure fell, the potion's stack fell, or the item was removed.</summary>
    protected abstract bool Spent { get; }

    /// <summary>True when <paramref name="kind"/> is one this action's line can have.</summary>
    protected abstract bool Fits(HealResultKind kind);

    /// <summary>The use was spent: the outcome and the result to hand the caller, given the line read (null when none was).</summary>
    protected abstract (ActionOutcome Outcome, HealResult? Result) Confirmed(HealResult? line);

    /// <summary>The kit or potion count the model holds after the use (the Structure, the stack), or <see cref="HealResult.Unknown"/>.</summary>
    protected abstract int LeftInModel { get; }

    /// <summary>What the model did not see, for the "nothing happened" detail.</summary>
    protected abstract string Missing { get; }

    /// <inheritdoc/>
    internal override bool HoldsLane
    {
        get
        {
            if (_api is null || double.IsNaN(_useDoneAt) || Resolution is not { Kind: ActionOutcomeKind.Confirmed })
            {
                return false;
            }

            var ready = double.IsNaN(_readyAt) ? _useDoneAt + ActionPolicy.ReturnStanceWaitSeconds : Math.Max(_readyAt, _useDoneAt);
            return _api.Clock.Now < ready + ActionPolicy.BusyChainMarginSeconds;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The Ready F74C is the player's own, not the client's echo (<c>Autonomous</c>), an interpreted state with no command and the Ready (or no) forward command: an action's own F74C (the kit's SkillHealSelf, the potion's Drink)
    /// carries its command. The latest one counts (the hold is measured from the later of it and the UseDone). <c>TODO(m4-heal-ready-stance)</c>: a use in combat stance may send an extra Ready before the effect (the spec's "[NonCombat Ready]"; not captured): it is harmless, the hold is measured from the later of the Ready and the UseDone, and s4's live test reads it.
    /// </remarks>
    internal override void OnMotion(in ObjectMotionChanged e, ActionApi api)
    {
        var motion = e.Motion;
        if (e.IsSelf && !e.Autonomous && motion.MovementType == MovementType.Interpreted && motion.Commands.IsDefaultOrEmpty && motion.Forward is null or ReadyCommand)
        {
            _readyAt = api.Clock.Now;
        }
    }

    /// <summary>The low word of <c>Ready</c> (0x41000003).</summary>
    private const ushort ReadyCommand = 0x0003;

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        var line = e.Line;
        switch (line.Kind)
        {
            case ChatKind.System when _line is null && HealResult.TryParse(line.Text, out var read) && Fits(read.Kind):
                _line = read;
                break;
            case ChatKind.WeenieError:
                _refusalCode = line.ChatType;
                _refusalText = WeenieErrorText.TryFormat(line.ChatType, line.Text, out _, out var text) ? text : (line.Text.Length > 0 ? line.Text : text);
                break;
            case ChatKind.Transient when line.Text.Length > 0:
                _refusalCode = 0;
                _refusalText = line.Text;
                break;
        }

        return false; // an observer: other trackers read the same line
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        if (_useDoneSeen || _api is null)
        {
            return false; // the UseDone of this action came already: a later one is the next action's
        }

        _useDoneSeen = true;
        _useDoneAt = _api.Clock.Now;
        Resolve(Settle(e.WeenieError));
        return true;
    }

    private ActionOutcome Settle(uint code)
    {
        if (code != 0)
        {
            return new ActionOutcome(ActionOutcomeKind.ServerError, code, "the server ended the use with " + Describe(code));
        }

        if (Spent)
        {
            var (outcome, result) = Confirmed(_line is { } line && Fits(line.Kind) ? line with { Left = line.Left != HealResult.Unknown ? line.Left : LeftInModel } : null);
            _result = result;
            return outcome;
        }

        if (_refusalCode != 0 || _refusalText is not null)
        {
            return new ActionOutcome(
                ActionOutcomeKind.ServerError,
                _refusalCode,
                "the server refused the use (" + (_refusalCode != 0 ? Describe(_refusalCode) + ": " : string.Empty) + _refusalText + ")" +
                (_refusalCode == FullHealthCode || _refusalCode == 0 ? "; TODO(m4-heal-full-vital-live): a full vital's refusal is read from ACE's source, not captured live" : string.Empty),
                RefusalReasonOf(_refusalCode, _refusalText));
        }

        return new ActionOutcome(ActionOutcomeKind.ServerError, 0, "nothing happened: UseDone 0, but " + Missing + " and no refusal line (the item not held, or no target?)", ActionReason.NoEffect);
    }

    /// <summary>
    /// M5 7 D141: the typed reason of a refusal line: a full vital (the 0x04FF line, or ACE's "is already at full stamina!" / "is already at full mana!", <c>Healer.cs</c> 94-100 @95ef426d), else the code's own
    /// meaning (<see cref="ActionReasons.FromCode"/>).
    /// </summary>
    internal static ActionReason RefusalReasonOf(uint code, string? text)
    {
        if (code == FullHealthCode || (text is not null && (text.EndsWith(" is already at full stamina!", StringComparison.Ordinal) || text.EndsWith(" is already at full mana!", StringComparison.Ordinal))))
        {
            return ActionReason.VitalFull;
        }

        return ActionReasons.FromCode(code);
    }

    /// <summary>A code as the detail names it: <c>WeenieError 0x04FC YouArentTrainedInHealing: You aren't trained in healing!</c>.</summary>
    internal static string Describe(uint code)
    {
        var name = code switch
        {
            NotTrainedCode => "YouArentTrainedInHealing",
            TooBusyCode => "YoureTooBusy",
            CantHealThatCode => "YouCantHealThat",
            InTheAirCode => "YouCantDoThatWhileInTheAir",
            FullHealthCode => "IsAtFullHealth",
            _ => null,
        };
        var known = WeenieErrorText.TryFormat(code, null, out _, out var text);
        return "WeenieError 0x" + code.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + (name is null ? string.Empty : " " + name) + (known ? ": " + text : string.Empty);
    }

    /// <inheritdoc/>
    internal override bool CompleteHandle(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError) =>
        ((HealHandle)Handle).TryComplete(outcome.Kind == ActionOutcomeKind.Confirmed ? _result : null, outcome, time, onHandlerError);
}

/// <summary>
/// <see cref="IActionApi.UseWithTarget"/> with a healing kit: GameAction 0x0035 {kit, target}, retail <c>CM_Inventory::Event_UseWithTargetEvent</c> (11.6096 0x006AD3E0). ACE (<c>Healer.HandleActionUseOnTarget</c> and <c>DoHealing</c>)
/// plays the heal motion (F74C SkillHealSelf), then in one step spends a use (02CE Structure, property 92), rolls, and on a win sends 02E9 for the stamina and the vital, 02CF and the line "You [expertly ]heal yourself for N Health points. Your
/// Handy Healing Kit has M uses left."; on a loss only the line "You fail to heal yourself. Your Handy Healing Kit has M uses left." (capture C1: five failed rolls in six at 22% health); the last use ends in 0024 and 02CD. Then UseDone 0 and the Ready.
/// <para>Confirmed iff the UseDone 0 came and the kit's Structure fell since the send (or the kit was removed: consumed): <see cref="HealResultKind.Healed"/> and <see cref="HealResultKind.FailedRoll"/> both confirm, a lost roll is a use spent,
/// never an error. The refusals and the lane: <see cref="HealLaneTracker"/>.</para>
/// </summary>
internal sealed class HealTracker(HealHandle handle, uint kit, uint target) : HealLaneTracker(handle, kit)
{
    /// <summary>ACE's <c>PropertyInt.Structure</c>: the uses left of a kit.</summary>
    internal const uint StructureProperty = 92;

    private int _baseline = -1;
    private int _structure = HealResult.Unknown;
    private bool _fell;
    private bool _removed;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0035 UseWithTarget " + ActionText.Hex(Item) + " on " + ActionText.Hex(target);

    /// <inheritdoc/>
    protected override void Begin(ActionApi api)
    {
        _structure = HealResult.Unknown;
        _fell = _removed = false;
        _baseline = -1;
        var inventory = api.State.Self.Inventory;
        if (inventory.TryGetInt(Item, StructureProperty, out var uses))
        {
            _baseline = uses;
        }
        else if (api.State.Objects.TryGet(Item, out var item) && item.Weenie.Structure is { } structure)
        {
            _baseline = structure;
        }
    }

    /// <inheritdoc/>
    protected override void Transmit(IAcSession session) => session.SendUseWithTarget(Item, target);

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid != Item)
        {
            return;
        }

        if (e.Kind == InventoryChangeKind.PropertyChanged && e.Property == StructureProperty)
        {
            _structure = e.PropertyValue;
            if (_baseline < 0 || e.PropertyValue < _baseline)
            {
                _fell = true; // with no baseline (a kit the model never described) any Structure update is a use spent: ACE sends it only for a use
            }
        }
        else if (e.Kind == InventoryChangeKind.Removed)
        {
            _removed = true; // the last use: 0024 (ACE consumes the kit at 0 uses)
        }
    }

    /// <inheritdoc/>
    protected override bool Spent => _fell || _removed;

    /// <inheritdoc/>
    protected override bool Fits(HealResultKind kind) => kind is HealResultKind.Healed or HealResultKind.FailedRoll;

    /// <inheritdoc/>
    protected override int LeftInModel => _removed ? 0 : _structure;

    /// <inheritdoc/>
    protected override string Missing => "the kit's Structure did not fall (" + (_baseline < 0 ? "no baseline" : "was " + ActionText.Count(_baseline)) + ") and the kit was not removed";

    /// <inheritdoc/>
    protected override (ActionOutcome Outcome, HealResult? Result) Confirmed(HealResult? line)
    {
        if (line is null)
        {
            return (new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "a use was spent (uses left " + Uses(LeftInModel) + "); the heal line was not read"), null);
        }

        var left = " (uses left " + Uses(line.Left) + ")";
        var detail = line.Kind == HealResultKind.Healed
            ? "healed " + ActionText.Count(line.Amount) + " " + line.Vital + (line.Critical ? " (critical)" : string.Empty) + left
            : "failed roll: a use was spent and nothing healed" + left;
        return (new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail), line);
    }

    private static string Uses(int left) => left == HealResult.Unknown ? "unknown" : ActionText.Count(left);
}

/// <summary>
/// <see cref="IActionApi.Drink"/>: GameAction 0x0036 Use on a potion or food, whose chain ACE builds in <c>Food.HandleActionUseOnTarget</c> and <c>ApplyBoost</c> (spec §5.7): F74C Drink at +10 ms, at +0.74 s the effect (02E9 of the vital, the line "The Potion
/// of Healing restores N points of your Health.", F750, then 0197 {stack - 1} or, for the last one, 0024 and 02CD) and the Ready F74C, the UseDone 0 at +1.48 s (capture C3). The restore is the potion's BoostValue (25 for 377), no roll. A potion drunk at a
/// full vital is spent: 0197 and the line "restores 0 points", no 02E9 (capture C3).
/// <para>Confirmed iff the UseDone 0 came and the stack fell (0197 with a smaller size) or the item was removed (0024); the line's amount is the result (<see cref="HealResultKind.Drank"/>), and 0 is <see cref="HealResultKind.Wasted"/>, still Confirmed.
/// The refusals and the lane: <see cref="HealLaneTracker"/>.</para>
/// </summary>
internal sealed class PotionTracker(HealHandle handle, uint item) : HealLaneTracker(handle, item)
{
    private int _baseline = -1;
    private int _stack = HealResult.Unknown;
    private bool _fell;
    private bool _removed;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0036 Use (drink) " + ActionText.Hex(Item);

    /// <inheritdoc/>
    /// <remarks>M6 15: after the lane's own check, the player's rule (<see cref="PlayerRulesGate.DrinkRefusal"/>): the automation's drink is on the shortcut bar, or it is refused by name.</remarks>
    internal override string? Refusal(ActionApi api) => base.Refusal(api) ?? api.PlayerRules.DrinkRefusal(Item, Handle.Source);

    /// <inheritdoc/>
    protected override void Begin(ActionApi api)
    {
        api.PlayerRules.DrinkSent(Item, Handle); // M6 15: the toolbar shows the automation's drink on its slot, as a person's use shows
        _stack = HealResult.Unknown;
        _fell = _removed = false;
        _baseline = -1;
        var inventory = api.State.Self.Inventory;
        if (inventory.TryGetStack(Item, out var size, out _))
        {
            _baseline = (int)Math.Min(size, int.MaxValue);
        }
        else if (api.State.Objects.TryGet(Item, out var held) && held.Weenie.StackSize is { } stack)
        {
            _baseline = stack;
        }
    }

    /// <inheritdoc/>
    protected override void Transmit(IAcSession session) => session.SendUse(Item);

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid != Item)
        {
            return;
        }

        if (e.Kind == InventoryChangeKind.StackChanged)
        {
            _stack = (int)Math.Min(e.StackSize, int.MaxValue);
            if (_baseline < 0 || _stack < _baseline)
            {
                _fell = true;
            }
        }
        else if (e.Kind == InventoryChangeKind.Removed)
        {
            _removed = true; // the last potion: 0024
        }
    }

    /// <inheritdoc/>
    protected override bool Spent => _fell || _removed;

    /// <inheritdoc/>
    protected override bool Fits(HealResultKind kind) => kind is HealResultKind.Drank or HealResultKind.Wasted;

    /// <inheritdoc/>
    protected override int LeftInModel => _removed ? 0 : _stack;

    /// <inheritdoc/>
    protected override string Missing => "the stack did not fall (" + (_baseline < 0 ? "no baseline" : "was " + ActionText.Count(_baseline)) + ") and the item was not removed";

    /// <inheritdoc/>
    protected override (ActionOutcome Outcome, HealResult? Result) Confirmed(HealResult? line)
    {
        if (line is null)
        {
            return (new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "the item was spent; the restore line was not read"), null);
        }

        var left = " (stack left " + (line.Left == HealResult.Unknown ? "unknown" : ActionText.Count(line.Left)) + ")";
        var detail = line.Kind == HealResultKind.Wasted
            ? "wasted: the potion was spent at a full " + line.Vital + ", restoring 0 points" + left
            : "restored " + ActionText.Count(line.Amount) + " " + line.Vital + left;
        return (new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail), line);
    }
}
