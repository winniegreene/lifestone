using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The fighters' gear recharge (closes <c>TODO(m4-gear-mana)</c>): the magic gear's own mana burns while its spells are active (ACE 95ef426d <c>Player_Tick.cs:623-659</c> <c>ManaConsumersTick</c>, every ~5 s at the item's ManaRate) and at 0 the spells are
/// removed (<c>HandleManaDepleted</c>, :680-699), so over repeated gates the fighters' <c>spells_active=</c> would fall. Retail's remedy is a charged mana stone, and this step uses one, through the Action API, when the gear runs low.
/// </summary>
/// <remarks>
/// <para><b>Reading.</b> ACE sends nothing while an item's mana burns, so the step appraises every piece of the set the fighter wears (and the Archer's bow kept in the pack) with <see cref="IActionApi.Identify"/>: ItemCurMana and ItemMaxMana (PropertyInt 107 and 108)
/// are assessment properties (<c>AppraiseInfo.cs:350-358</c>). It appraises the held charges too: only a stone with mana is ever used (an empty stone used on an item destroys the item, <c>ManaStone.cs:84-113</c>).</para>
/// <para><b>Charging.</b> When a worn piece's mana is under <see cref="RechargeBelow"/> of its maximum, one charge is used on the player (<see cref="IActionApi.UseManaStone"/>; ACE pours it into every equipped item below its maximum, rationed evenly, <c>ManaStone.cs:121-190</c>);
/// when only the pack bow is low, on the bow (:192-225). Then everything is appraised again, and another charge is used while a piece is still low and the kit's charges last (<see cref="FighterKit.ManaCharges"/>; the build tops a used one up afterwards).</para>
/// <para><b>Waking the spells.</b> ACE casts an item's spells only when it is wielded (<c>Player_Inventory.cs:313-315</c> <c>TryActivateSpells</c>); a stone gives mana and nothing else, so a piece whose spells were removed at 0 stays silent after its charge.
/// With a spell table the step takes such a piece (silent in <see cref="FighterGearCheck"/>, with mana now) off and wears it again in the same place, once, and waits up to <see cref="SettleSeconds"/> for its spells. <c>TODO(m4-gear-mana-reactivate)</c>: whether retail
/// re-cast a recharged item's spells by itself is unread; ACE's rule is followed.</para>
/// <para><b>Report-only.</b> The step never fails the build (its fields are report-only in their first gate): an unread appraisal, no charge held or a refused use ends it with a warning and the reason in <c>recharged=</c>. Verdict:
/// <c>gear_mana_min=&lt;percent&gt;|unread|not_entered recharged=&lt;charges used&gt;[:no-charge|:refused|:unread]</c>, the lowest piece's mana after the step.</para>
/// <para><b>Not retail:</b> the trigger and the kit's count are Lifestone's own sizing; the stone, its use and its effect are ACE's.</para>
/// </remarks>
public sealed class GearRecharge
{
    /// <summary>The trigger: a piece below half its maximum (the lowest piece is about three hours from empty: <see cref="FighterKit.ManaChargeWcid"/>). Not retail: Lifestone's own.</summary>
    public const double RechargeBelow = 0.5;

    /// <summary>How long a precondition (the inventory settling, a re-worn piece's spells) is waited for, on the session clock.</summary>
    public const double SettleSeconds = 10;

    private readonly FighterGearSet _set;
    private readonly uint _chargeWcid;
    private readonly int _maxCharges;
    private readonly IReadOnlyDictionary<uint, SpellInfo>? _spells;
    private readonly ILogSink _log;
    private readonly string _category;
    private readonly Action<string>? _onSent;
    private readonly List<uint> _scratch = [];
    private readonly List<Read> _reads = [];
    private readonly HashSet<uint> _spent = [];
    private readonly List<string> _rewielded = [];
    private readonly HashSet<uint> _rewieldedGuids = [];
    private readonly ReasonTimer _wait = new(SettleSeconds);
    private Phase _phase = Phase.Start;
    private ActionHandle? _pending;
    private string _label = string.Empty;
    private uint _stone;
    private (uint Guid, uint Location, GearPiece Piece) _swap;
    private double _settleFrom = double.NaN;
    private bool _started;
    private int? _minBefore;
    private int? _minAfter;
    private string? _problem;

    /// <summary>The step for one fighter's set.</summary>
    /// <param name="set">The fighter's magic gear.</param>
    /// <param name="chargeWcid">The charge the kit carries (<see cref="FighterKit.ChargeWcid"/>).</param>
    /// <param name="maxCharges">The most charges one run uses (<see cref="FighterKit.ManaCharges"/>).</param>
    /// <param name="spells">The dat's spells (for the silent pieces), or null: the pieces are then charged but none is worn again.</param>
    /// <param name="log">Where the step logs.</param>
    /// <param name="category">The log category (the build's).</param>
    /// <param name="onSent">Told the label of each action that changes the character (a charge used, a piece worn again), for the build's step list; null for none.</param>
    public GearRecharge(FighterGearSet set, uint chargeWcid, int maxCharges, IReadOnlyDictionary<uint, SpellInfo>? spells, ILogSink log, string category, Action<string>? onSent = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentOutOfRangeException.ThrowIfZero(chargeWcid);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharges);
        _set = set;
        _chargeWcid = chargeWcid;
        _maxCharges = maxCharges;
        _spells = spells;
        _log = log;
        _category = category;
        _onSent = onSent;
    }

    private enum Phase
    {
        Start,
        Reading,
        Charging,
        Silent,
        TakingOff,
        PuttingOn,
        Settling,
        Done,
    }

    /// <summary>True between the first step and the end: the caller lets the step run alone (a charge it used is topped up only after it ends).</summary>
    public bool InProgress => _started && _phase != Phase.Done;

    /// <summary>True when the step has ended.</summary>
    public bool Finished => _phase == Phase.Done;

    /// <summary>The charges used.</summary>
    public int Charges => _spent.Count;

    /// <summary>The lowest piece's mana in percent at the first reading, or null (not read).</summary>
    public int? MinBefore => _minBefore;

    /// <summary>The lowest piece's mana in percent at the last reading, or null (not read).</summary>
    public int? MinAfter => _minAfter;

    /// <summary>The pieces taken off and worn again to wake their spells, by name.</summary>
    public IReadOnlyList<string> Rewielded => _rewielded;

    /// <summary>Why the step did less than it would have (<c>no-charge</c>, <c>refused</c>, <c>unread</c>), or null.</summary>
    public string? Problem => _problem;

    /// <summary>The verdict fields: <c> gear_mana_min=&lt;percent&gt;|unread|not_entered recharged=&lt;n&gt;[:&lt;problem&gt;]</c> (report-only in their first gate).</summary>
    public string VerdictFields()
    {
        var min = _minAfter is { } after ? after.ToString(CultureInfo.InvariantCulture) : _problem == "unread" ? "unread" : "not_entered";
        return string.Create(CultureInfo.InvariantCulture, $" gear_mana_min={min} recharged={Charges}{(_problem is null ? string.Empty : ":" + _problem)}");
    }

    /// <summary>Runs the step: Running while it works, null once it has ended (it never fails the caller).</summary>
    /// <param name="session">The session.</param>
    public ScenarioStatus? Step(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        switch (_phase)
        {
            case Phase.Start:
                return Start(session);
            case Phase.Reading:
                return Reading(session);
            case Phase.Charging:
                return Charging(session);
            case Phase.Silent:
                return Silent(session);
            case Phase.TakingOff:
                return TakingOff(session);
            case Phase.PuttingOn:
                return PuttingOn(session);
            case Phase.Settling:
                return Settling(session);
            default:
                return null;
        }
    }

    // ------------------------------------------------------------------------------------------------ reading

    private ScenarioStatus? Start(IGameSessionView session)
    {
        var state = session.State;
        if (!state.Self.Inventory.IsKnown || state.Self.Guid == 0)
        {
            return Wait(session, "the inventory is not known");
        }

        GearInventory.Collect(state, _scratch);
        _reads.Clear();
        var bowRead = false;
        foreach (var guid in _scratch)
        {
            if (!state.Objects.TryGet(guid, out var o))
            {
                return Wait(session, "a held item has no world object yet");
            }

            var wcid = o.Weenie.WeenieClassId;
            var onBody = state.Self.Inventory.TryGetEquipped(guid, out var e) && e.Location != 0;
            if (wcid == _chargeWcid && !onBody)
            {
                _reads.Add(new Read(guid, null, false, session.Actions.Identify(guid, ActionSource.Host)));
            }
            else if (_set.PieceOf(wcid) is { } piece && (onBody || (!bowRead && piece.Kind == GearPieceKind.Weapon && !piece.IsMelee)))
            {
                bowRead |= !onBody;
                _reads.Add(new Read(guid, piece, onBody, session.Actions.Identify(guid, ActionSource.Host)));
            }
        }

        _wait.Reset();
        _started = true;
        if (!_reads.Any(r => r.Piece is not null))
        {
            _log.Warn(_category, "gear mana: no piece of the set is worn, so there is nothing to read (the build wears it first)");
            _problem = "unread";
            return End();
        }

        _phase = Phase.Reading;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus? Reading(IGameSessionView session)
    {
        if (_reads.Any(r => r.Handle.Outcome is null))
        {
            return ScenarioStatus.Running;
        }

        var unread = new List<string>();
        int? lowest = null;
        string? lowestName = null;
        foreach (var read in _reads)
        {
            var appraisal = read.Handle.Outcome is { Kind: ActionOutcomeKind.Confirmed } && read.Handle.Result is { Kind: IdentifyResultKind.Appraised } result ? result.Appraisal : null;
            read.Mana = appraisal?.ItemMana;
            read.MaxMana = appraisal?.ItemMaxMana;
            if (read.Piece is not { } piece)
            {
                continue;
            }

            if (read.MaxMana is not > 0)
            {
                unread.Add(piece.Name + " (" + (read.Handle.Outcome is { } o && o.Kind != ActionOutcomeKind.Confirmed ? o.Kind + (o.Detail is null ? string.Empty : ": " + o.Detail) : "no ItemMaxMana in the appraisal") + ")");
                continue;
            }

            var percent = read.Percent;
            if (lowest is null || percent < lowest)
            {
                (lowest, lowestName) = (percent, piece.Name);
            }
        }

        if (unread.Count > 0)
        {
            _log.Warn(_category, "gear mana: unread, so nothing is charged: " + string.Join(", ", unread));
            _problem = "unread";
            return End();
        }

        _minAfter = lowest;
        _minBefore ??= lowest;
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear mana: {string.Join(", ", _reads.Where(r => r.Piece is not null).Select(r => $"{r.Piece!.Name} {r.Mana}/{r.MaxMana}{(r.OnBody ? string.Empty : " (pack)")}"))}; lowest {lowest}% ({lowestName}); charges held {_reads.Count(r => r.Piece is null && r.Mana > 0)}"));

        var worn = _reads.Where(r => r.Piece is not null && r.OnBody && r.Low).ToList();
        var pack = _reads.Where(r => r.Piece is not null && !r.OnBody && r.Low).ToList();
        if (worn.Count == 0 && pack.Count == 0)
        {
            return ToSilent(session);
        }

        var low = string.Join(", ", worn.Concat(pack).Select(r => r.Piece!.Name + " " + r.Percent.ToString(CultureInfo.InvariantCulture) + "%"));
        if (Charges >= _maxCharges)
        {
            _log.Warn(_category, $"gear mana: still low after the kit's {_maxCharges} charge(s): {low}; the next build tops the charge up");
            return ToSilent(session);
        }

        if (_reads.FirstOrDefault(r => r.Piece is null && r.Mana > 0 && !_spent.Contains(r.Guid)) is not { } stone)
        {
            _log.Warn(_category, $"gear mana: low ({low}) and no charged stone (weenie {_chargeWcid}) is held: nothing is charged");
            _problem = "no-charge";
            return ToSilent(session);
        }

        var self = session.State.Self.Guid;
        var target = worn.Count > 0 ? self : pack[0].Guid;
        _stone = stone.Guid;
        _label = string.Create(CultureInfo.InvariantCulture, $"use a mana charge (object 0x{stone.Guid:X8}, {stone.Mana} mana) on {(target == self ? "the player (every equipped item below its maximum)" : "the " + pack[0].Piece!.Name + " in the pack")}: {low} below {RechargeBelow * 100:0}%");
        _log.Info(_category, _label);
        _onSent?.Invoke(_label);
        _pending = session.Actions.UseManaStone(stone.Guid, target, ActionSource.Host);
        _phase = Phase.Charging;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus? Charging(IGameSessionView session)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            _log.Warn(_category, _label + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));
            _problem = "refused";
            return ToSilent(session);
        }

        _spent.Add(_stone);
        _log.Info(_category, _label + ": confirmed" + (outcome.Detail is null ? string.Empty : " (" + outcome.Detail + ")"));
        _phase = Phase.Start; // read everything again: the charge's effect, and whether another is needed
        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ waking the spells

    private ScenarioStatus? ToSilent(IGameSessionView session)
    {
        _phase = Phase.Silent;
        return Silent(session);
    }

    /// <summary>Takes off the next worn piece that has mana but none of its wearer spells active (once per piece), or settles.</summary>
    private ScenarioStatus? Silent(IGameSessionView session)
    {
        if (_spells is null)
        {
            if (Charges > 0)
            {
                _log.Warn(_category, "gear mana: no spell table, so a charged piece whose spells were removed is not worn again (TODO(m4-gear-mana-reactivate))");
            }

            return End();
        }

        var state = session.State;
        var reading = FighterGearCheck.Read(state, _set, _spells, _scratch);
        foreach (var piece in reading.Silent)
        {
            var read = _reads.FirstOrDefault(r => r.Piece == piece && r.OnBody);
            if (read is null || _rewieldedGuids.Contains(read.Guid) || !state.Self.Inventory.TryGetEquipped(read.Guid, out var worn) || worn.Location == 0)
            {
                continue;
            }

            if (read.Mana is not > 0)
            {
                _log.Warn(_category, $"gear mana: the {piece.Name} (weenie {piece.Wcid}) has no mana and no charge reached it: its spells stay off");
                continue;
            }

            _rewieldedGuids.Add(read.Guid);
            _swap = (read.Guid, worn.Location, piece);
            _label = string.Create(CultureInfo.InvariantCulture, $"take off the {piece.Name} (object 0x{read.Guid:X8}, {read.Mana}/{read.MaxMana} mana, none of its wearer spells active) to wear it again: ACE casts an item's spells only when it is wielded (TODO(m4-gear-mana-reactivate))");
            _log.Info(_category, _label);
            _onSent?.Invoke(_label);
            _pending = session.Actions.Unwield(read.Guid, ActionSource.Host);
            _phase = Phase.TakingOff;
            return ScenarioStatus.Running;
        }

        if (_rewielded.Count == 0)
        {
            return End();
        }

        _settleFrom = session.Clock.Now;
        _phase = Phase.Settling;
        return Settling(session);
    }

    private ScenarioStatus? TakingOff(IGameSessionView session)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            _log.Warn(_category, $"gear mana: taking off the {_swap.Piece.Name}: {outcome.Kind}{(outcome.Detail is null ? string.Empty : ": " + outcome.Detail)}");
            _problem ??= "refused";
            return ToSilent(session);
        }

        _label = string.Create(CultureInfo.InvariantCulture, $"wear the {_swap.Piece.Name} again at 0x{_swap.Location:X}");
        _log.Info(_category, _label);
        _onSent?.Invoke(_label);
        _pending = session.Actions.Wield(_swap.Guid, (EquipMask)_swap.Location, ActionSource.Host);
        _phase = Phase.PuttingOn;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus? PuttingOn(IGameSessionView session)
    {
        if (_pending?.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            // The build's wield step wears it again when the step has ended (the piece is in the pack).
            _log.Warn(_category, $"gear mana: wearing the {_swap.Piece.Name} again: {outcome.Kind}{(outcome.Detail is null ? string.Empty : ": " + outcome.Detail)}; the build's wield step wears it");
            _problem ??= "refused";
            return End();
        }

        _rewielded.Add(_swap.Piece.Name);
        return ToSilent(session);
    }

    private ScenarioStatus? Settling(IGameSessionView session)
    {
        var silent = FighterGearCheck.Read(session.State, _set, _spells, _scratch).Silent.Where(p => _rewielded.Contains(p.Name)).ToList();
        if (silent.Count == 0)
        {
            _log.Info(_category, "gear mana: the spells of " + string.Join(", ", _rewielded) + " are active again");
            return End();
        }

        if (session.Clock.Now - _settleFrom > SettleSeconds)
        {
            _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"gear mana: worn again, but no wearer spell of {string.Join(", ", silent.Select(p => p.Name))} is active after {SettleSeconds:0.#} s"));
            return End();
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ plumbing

    private ScenarioStatus? Wait(IGameSessionView session, string reason)
    {
        if (!_wait.Expired(session.Clock.Now, reason))
        {
            return ScenarioStatus.Running;
        }

        _log.Warn(_category, string.Create(CultureInfo.InvariantCulture, $"gear mana: still waiting after {SettleSeconds:0.#} s: {reason}; nothing is charged"));
        _problem = "unread";
        return End();
    }

    private ScenarioStatus? End()
    {
        _phase = Phase.Done;
        _log.Info(_category, string.Create(CultureInfo.InvariantCulture, $"gear mana: lowest {(_minBefore is { } b ? b + "%" : "unread")} before, {(_minAfter is { } a ? a + "%" : "unread")} after; {Charges} charge(s) used{(_rewielded.Count == 0 ? string.Empty : "; worn again: " + string.Join(", ", _rewielded))}{(_problem is null ? string.Empty : "; " + _problem)}"));
        return null;
    }

    /// <summary>One appraisal: a piece of the set (worn, or the bow in the pack) or a held charge (<see cref="Piece"/> null).</summary>
    private sealed class Read(uint guid, GearPiece? piece, bool onBody, IdentifyHandle handle)
    {
        public uint Guid { get; } = guid;

        public GearPiece? Piece { get; } = piece;

        public bool OnBody { get; } = onBody;

        public IdentifyHandle Handle { get; } = handle;

        public int? Mana { get; set; }

        public int? MaxMana { get; set; }

        /// <summary>The mana in whole percent of the maximum (rounded down).</summary>
        public int Percent => MaxMana is > 0 ? (int)Math.Floor(100.0 * Math.Max(0, Mana ?? 0) / MaxMana.Value) : 0;

        /// <summary>True when the piece is under the trigger.</summary>
        public bool Low => MaxMana is > 0 && (Mana ?? 0) < RechargeBelow * MaxMana.Value;
    }
}
