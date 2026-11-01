using System.Globalization;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>The fix a typed failure gets before its one retry (<see cref="RecoveryTable"/>).</summary>
public enum RecoveryFix
{
    /// <summary>No fix of its own: the generic recovery (the recall home, then the known state).</summary>
    Generic,

    /// <summary>Wield the best caster held in the packs (<see cref="HeldCaster.Rearm"/>), then retry.</summary>
    WieldCaster,

    /// <summary>Free a pack slot by the pack rules (an empty mana stone beyond the keep, <see cref="ItemManaKeeper.Shed"/>, is dropped), then retry; nothing to shed stops the run safely.</summary>
    ShedPack,

    /// <summary>The vitals keeper's pass with a mana floor (the stamina-to-mana dance), then retry.</summary>
    ManaDance,

    /// <summary>No fix: the retry picks again (the same target is not asked again: it is gone).</summary>
    RePick,

    /// <summary>Wait <see cref="RecoveryTable.BusyWaitSeconds"/>, then retry.</summary>
    Wait,

    /// <summary>No fix: the retry plans again from the state (the item it needed is not held).</summary>
    PlanAgain,
}

/// <summary>
/// M5 7 D141 part 4 ("on a failure, a named recovery runs: fix the missing precondition and retry once; otherwise stop safely with the reason logged"): the one table of the autopilot that maps a station failure's typed reason
/// (<see cref="ActionOutcome.Reason"/>) to its fix. The autopilot runs the fix (<see cref="RecoveryFixRun"/>) and retries once; the same reason again after the retry, or a fix that cannot be made, stops the run safely with the
/// reason. A reason the table does not name goes to the generic recovery. Pure but for <see cref="ReasonOf"/>, which reads the action log.
/// </summary>
public static class RecoveryTable
{
    /// <summary>The wait of <see cref="RecoveryFix.Wait"/> (ACE's busy chain is a pickup motion and a return stance, well under it; Lifestone's own).</summary>
    public const double BusyWaitSeconds = 2;

    /// <summary>The mana share the <see cref="RecoveryFix.ManaDance"/> pass asks of the keeper (Lifestone's own: the dance's own goal, near full).</summary>
    public const int ManaDancePercent = 90;

    /// <summary>The bound of any fix (Lifestone's own: a wield or a drop takes a second or two; the mana dance up to a few casts).</summary>
    public const double FixSeconds = 120;

    /// <summary>The fix for <paramref name="reason"/>: the table.</summary>
    /// <param name="reason">The failure's typed reason.</param>
    public static RecoveryFix FixFor(ActionReason reason) => reason switch
    {
        ActionReason.NoCasterHeld => RecoveryFix.WieldCaster,
        ActionReason.NoFreeSlot => RecoveryFix.ShedPack,
        ActionReason.NotEnoughMana => RecoveryFix.ManaDance,
        ActionReason.TargetGone => RecoveryFix.RePick,
        ActionReason.Busy => RecoveryFix.Wait,
        ActionReason.NotHeld => RecoveryFix.PlanAgain,
        _ => RecoveryFix.Generic,
    };

    /// <summary>The fix's name in the log (<c>fix=</c>).</summary>
    /// <param name="fix">The fix.</param>
    public static string Name(RecoveryFix fix) => fix switch
    {
        RecoveryFix.WieldCaster => "wield-caster",
        RecoveryFix.ShedPack => "shed-pack",
        RecoveryFix.ManaDance => "mana-dance",
        RecoveryFix.RePick => "re-pick",
        RecoveryFix.Wait => "wait",
        RecoveryFix.PlanAgain => "plan-again",
        _ => "generic",
    };

    /// <summary>
    /// The typed reason of a station's failure: the newest failed action since <paramref name="since"/> (<see cref="ActionLog.Total"/> at the station's start) whose typed reason is set and whose detail the failure text quotes (the
    /// stations pass the failing action's outcome up as text, so the quote ties the reason to this failure and not to an earlier failure the station got over). The recall's own "no caster held" with no action behind it (the packs hold no
    /// caster: <see cref="SetCombatModeTracker.NoCasterHeld"/>) is <see cref="ActionReason.NoCasterHeld"/>. Else <see cref="ActionReason.None"/>.
    /// </summary>
    /// <param name="log">The session's action log, or null.</param>
    /// <param name="since">The log's total at the station's start.</param>
    /// <param name="why">The station's failure text.</param>
    public static ActionReason ReasonOf(ActionLog? log, long since, string? why)
    {
        if (string.IsNullOrEmpty(why))
        {
            return ActionReason.None;
        }

        if (log is not null)
        {
            var newer = (int)Math.Min(log.Count, Math.Max(0, log.Total - since));
            for (var i = log.Count - 1; i >= log.Count - newer; i--)
            {
                var outcome = log[i].Outcome;
                if (!outcome.Succeeded && outcome.Reason != ActionReason.None && outcome.Detail is { Length: > 0 } detail && why.Contains(detail, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }
        }

        return why.Contains(SetCombatModeTracker.NoCasterHeld, StringComparison.Ordinal) ? ActionReason.NoCasterHeld : ActionReason.None;
    }
}

/// <summary>The state of a <see cref="RecoveryFixRun"/>.</summary>
public enum RecoveryFixStatus
{
    /// <summary>The fix is under way.</summary>
    Running,

    /// <summary>The fix is made (checked on the state, not on a confirmation alone): the retry follows.</summary>
    Done,

    /// <summary>The fix cannot be made: the run stops safely with <see cref="RecoveryFixRun.Why"/>.</summary>
    Failed,
}

/// <summary>
/// One fix of the <see cref="RecoveryTable"/>, stepped by the autopilot each frame. Each fix checks before (is there anything to do, can it be done), acts (one action), and checks after on the state (the hands hold a caster, a pack slot
/// is free, the keeper is ready), D141's order. <see cref="Why"/> says what it did or why it could not.
/// </summary>
public sealed class RecoveryFixRun
{
    private readonly RecoveryFix _fix;
    private readonly VitalsKeeper? _keeper;
    private ActionHandle? _pending;
    private string _what = string.Empty;
    private double _start = double.NaN;
    private bool _begun;

    /// <summary>A fix to run.</summary>
    /// <param name="fix">The fix.</param>
    /// <param name="keeper">The journey's vitals keeper (the mana dance's), or null.</param>
    public RecoveryFixRun(RecoveryFix fix, VitalsKeeper? keeper)
    {
        _fix = fix;
        _keeper = keeper;
    }

    /// <summary>What the fix did, or why it could not.</summary>
    public string Why { get; private set; } = string.Empty;

    /// <summary>One step at the session clock's <paramref name="now"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The session clock's seconds.</param>
    public RecoveryFixStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (double.IsNaN(_start))
        {
            _start = now;
        }

        if (now - _start > RecoveryTable.FixSeconds)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the {RecoveryTable.Name(_fix)} fix took over {RecoveryTable.FixSeconds:0} s{(_what.Length > 0 ? " (" + _what + ")" : string.Empty)}"));
        }

        return _fix switch
        {
            RecoveryFix.WieldCaster => WieldCaster(session),
            RecoveryFix.ShedPack => ShedPack(session),
            RecoveryFix.ManaDance => ManaDance(now),
            RecoveryFix.Wait => now - _start >= RecoveryTable.BusyWaitSeconds ? Done(string.Create(CultureInfo.InvariantCulture, $"waited {RecoveryTable.BusyWaitSeconds:0} s for the server's busy chain")) : RecoveryFixStatus.Running,
            RecoveryFix.RePick => Done("nothing to fix: the object is gone; the retry picks again"),
            RecoveryFix.PlanAgain => Done("nothing to fix: the item is not held; the retry plans again from the state"),
            _ => Fail("TODO(state): the generic recovery has no fix run"),
        };
    }

    private RecoveryFixStatus WieldCaster(IGameSessionView session)
    {
        var state = session.State;
        if (_pending is null)
        {
            if (HeldCaster.Held(state))
            {
                return Done("a caster is held already: nothing to wield");
            }

            // The recall's rearm (M5 7 D139 l): the self-equip's pick of the casters in the packs, else the usable one with the best war factor, else any.
            if (HeldCaster.Rearm(state, new SelfEquipJudge([], 0, _ => null), out var why) is not { } caster)
            {
                return Fail("no caster to wield: " + why);
            }

            _what = "wield " + why;
            _pending = session.Actions.Wield(caster.Guid, (EquipMask)SelfEquipJudge.Held, ActionSource.Automation);
            return RecoveryFixStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return RecoveryFixStatus.Running;
        }

        return HeldCaster.Held(state)
            ? Done(_what + ": " + Describe(outcome) + "; a caster is held now")
            : Fail(_what + ": " + Describe(outcome) + "; the hands still hold no caster");
    }

    private RecoveryFixStatus ShedPack(IGameSessionView session)
    {
        var state = session.State;
        if (_pending is null)
        {
            if (BuyTracker.FreeSlots(state) is > 0 and var free)
            {
                return Done(string.Create(CultureInfo.InvariantCulture, $"the packs have {free} free slot(s) already: nothing to shed"));
            }

            // The pack rules (M5 7 D139 d): an empty mana stone beyond the keep is loot, the one thing the run may shed away from a vendor (dropped, as where no vendor buys stones); nothing else is shed here.
            var stones = new List<HeldStone>();
            foreach (var item in PackLayout.Items(state))
            {
                if ((item.ItemType & PackRules.ManaStoneType) != 0 && session.TryGetAppraisal(item.Guid, out var a))
                {
                    stones.Add(new HeldStone(item.Guid, item.Name, a.ItemMana, a.Float(ItemEfficiencyFloat) ?? ItemManaKeeper.EfficiencyByWcid.GetValueOrDefault(item.Wcid)));
                }
            }

            var shed = ItemManaKeeper.Shed(stones, _ => false, out var keepWhy);
            if (shed.Count == 0)
            {
                return Fail("nothing can be shed by the pack rules (" + keepWhy + "; an empty mana stone beyond the keep is the only thing shed away from a vendor)");
            }

            var stone = shed[0];
            _what = string.Create(CultureInfo.InvariantCulture, $"drop '{stone.Name}' 0x{stone.Guid:X8}, an empty stone beyond the keep ({keepWhy})");
            _pending = session.Actions.Drop(stone.Guid, ActionSource.Automation);
            return RecoveryFixStatus.Running;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return RecoveryFixStatus.Running;
        }

        var after = BuyTracker.FreeSlots(state);
        return after is > 0
            ? Done(_what + ": " + Describe(outcome) + string.Create(CultureInfo.InvariantCulture, $"; {after} free slot(s) now"))
            : Fail(_what + ": " + Describe(outcome) + "; still no free pack slot");
    }

    private RecoveryFixStatus ManaDance(double now)
    {
        if (_keeper is null)
        {
            return Fail("no vitals keeper to dance the mana");
        }

        if (!_begun)
        {
            _begun = true;
            _what = string.Create(CultureInfo.InvariantCulture, $"the keeper's pass to {RecoveryTable.ManaDancePercent}% mana");
            if (_keeper.NeedsFor(new VitalsNeeds(MinManaPercent: RecoveryTable.ManaDancePercent)))
            {
                return Done(_what + ": met already");
            }
        }

        return _keeper.Step(now) switch
        {
            VitalsKeeperState.Ready => Done(_what + ": done"),
            VitalsKeeperState.Blocked => Fail(_what + ": blocked: " + (_keeper.Why ?? "blocked")),
            _ => RecoveryFixStatus.Running,
        };
    }

    private RecoveryFixStatus Done(string why)
    {
        Why = why;
        return RecoveryFixStatus.Done;
    }

    private RecoveryFixStatus Fail(string why)
    {
        Why = why;
        return RecoveryFixStatus.Failed;
    }

    private static string Describe(ActionOutcome outcome) => outcome.Kind + (outcome.Detail is null ? string.Empty : " (" + outcome.Detail + ")");

    /// <summary>A stone's efficiency, PropertyFloat 87 (the resupply's <c>ItemEfficiencyFloat</c>).</summary>
    private const uint ItemEfficiencyFloat = 87;
}
