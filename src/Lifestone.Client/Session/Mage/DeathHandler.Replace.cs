using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AH (the owner's direction: on a corpse run get another robe and gloves, buff, head out): the <c>replace</c> step. The order after a respawn is now: the rearm (a caster wielded), the rewear (the pack's pieces on), the replace (what the
/// body still lacks, bought at the home vendors), the rebuff, the corpse trip. See <see cref="DeathReplace"/> for the pure half.
/// </summary>
public sealed partial class DeathHandler
{
    private string _afterReplace = "rebuff";
    private bool _replaceCorpsePath;
    private bool _replaceTried;
    private IReadOnlyList<GearFloorKind> _replaceMissing = [];
    private readonly HashSet<GearFloorKind> _replacedKinds = [];
    private readonly HashSet<uint> _leftOnCorpse = [];

    /// <summary>
    /// <c>replace=</c> (M6 7 AH, report-only, D27): <see cref="DeathReplace.None"/> when the body missed nothing of the floor's robe, gauntlets and (with no caster at all) caster; the pieces bought and worn, <c>caster+robe+gauntlets</c> in that order;
    /// <see cref="DeathReplace.Short"/> (the purse, no vendor sells it, no walk made); <see cref="DeathReplace.Failed"/> (the walk, the buy, the time or the wear failed); <c>n/a</c> before the step. The handling goes on in every case: never a stop.
    /// </summary>
    public string Replace { get; private set; } = DeathLegVerdict.NotApplicable;

    /// <summary>The corpse entries the take-back left because the replacement fills their slot (<c>take-back: left ...</c>); report-only.</summary>
    public int LeftOnCorpse => _leftOnCorpse.Count;

    private void ResetReplace()
    {
        (_afterReplace, _replaceCorpsePath, _replaceTried, _replaceMissing, Replace) = ("rebuff", false, false, [], DeathLegVerdict.NotApplicable);
        _replacedKinds.Clear();
        _leftOnCorpse.Clear();
    }

    /// <summary>
    /// Check before: what the floor wants and the body lacks (a robe, gauntlets, a caster when none is wielded or held) on the handler's ground. Nothing missing is <c>replace=none</c> with no walk. Missing but nothing the floor can buy (no vendor of the
    /// ground sells it, the robe is blocked) is <c>replace=short</c>. Else one <see cref="ResupplyRun"/> with <see cref="ResupplyOptions.Replace"/> (the same journey context as the rearm, the floor's lines only, never the kit), the step <c>replace</c>.
    /// One try per death: a second entry is a logged no-op. On the corpse path (<c>rearm=corpse</c>: no caster at all) the replace step's Wand is the rearm: wielded, the rebuff comes first and the trip after; a replace that ends with no caster
    /// falls back by name to the corpse-first path.
    /// </summary>
    private ScenarioStatus EnterReplace(IGameSessionView session, double now, string next)
    {
        _afterReplace = next;
        _replaceCorpsePath = Rearm == DeathRearm.Corpse && next == "route";
        Replace = DeathReplace.None;
        if (_replaceTried)
        {
            _log.Info(LogCategory, $"replace: already tried in this death: one try only, never a loop; the {next} follows");
            return ReplaceEnd(session, now);
        }

        _replaceTried = true;
        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        var level = session.State.Self.Qualities.Level;
        var missing = DeathReplace.Missing(session.State, floorLevel);
        var floor = GearFloor.StateOf(session.State, 0);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"replace: before: level {level} (floor level {floorLevel}), casters={floor.Casters} chest={(floor.ChestCovered ? $"'{floor.ChestPiece}'" : "bare")} hands={(floor.HandsCovered ? "covered" : "bare")} bare=0x{floor.Bare:X}: missing {DeathReplace.Describe([.. missing])} on {_ground.Name}"));
        if (level >= floorLevel && floor.ChestCovered && (floor.Bare & ~GearCoverage.Hands) is var otherBare and not 0)
        {
            // M6 item 14 (armour on every part she can cover): the armour set is bonded and stays on through a death, so a part bare here is said by name: no floor line buys a piece for it.
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"replace: the body is bare at 0x{otherBare:X} ({GearFloor.PartNames(otherBare)}) after the rewear (named: armour-bare-after-death): no floor line buys a piece for those parts (TODO(m6-armour-floor-vendor)); the handling goes on"));
        }
        if (missing.Count == 0)
        {
            Replace = DeathReplace.None;
            _log.Info(LogCategory, level < floorLevel
                ? $"replace=none: level {level} is under the floor level {floorLevel}: the floor asks nothing; no walk; the {next} follows"
                : $"replace=none: nothing of the floor's robe, gauntlets or caster is missing; no walk; the {next} follows");
            return ReplaceEnd(session, now);
        }

        var plan = DeathReplace.Plan(_ground, session.State, long.MaxValue, floorLevel);
        var sold = plan.Lines.Select(l => l.Kind).ToList();
        var unsold = missing.Where(k => !sold.Contains(k)).ToList();
        if (sold.Count == 0)
        {
            return ReplaceStopped(session, now, DeathReplace.Short, $"no vendor of {_ground.Home.Town} sells {DeathReplace.Join(missing)} that the body can wear ({(plan.Notes.Count == 0 ? plan.Stop ?? "no line" : string.Join("; ", plan.Notes))})");
        }

        if (_options.Journey is not { } context)
        {
            return ReplaceStopped(session, now, DeathReplace.Failed, "this host has no journey context to shop with: TODO(m5-death-rearm-standalone)");
        }

        if (!string.Equals(context.Dungeon.Key, _ground.Key, StringComparison.Ordinal))
        {
            return ReplaceStopped(session, now, DeathReplace.Failed, $"the journey's dungeon '{context.Dungeon.Key}' is not the handler's ground '{_ground.Key}': the trip would shop for the wrong ground");
        }

        _replaceMissing = missing;
        _shop = new ResupplyRun(context, new ResupplyOptions(Replace: true));
        _log.Info(LogCategory, $"replace: buy {DeathReplace.Join(sold)} at {string.Join(", ", plan.Lines.Select(l => l.Vendor.Name).Distinct())} through the resupply (the floor's lines only, never the kit; the purse the trip holds decides what is paid){(unsold.Count == 0 ? string.Empty : $"; not sold here: {DeathReplace.Join(unsold)}")}: one try, the handling goes on whatever comes of it");
        return Enter("replace", now);
    }

    private ScenarioStatus ReplaceStopped(IGameSessionView session, double now, string verdict, string why)
    {
        Replace = verdict;
        _shop = null;
        _log.Warn(LogCategory, $"replace={verdict}: {why}; the handling goes on to the {_afterReplace} (report-only, never a stop)");
        return ReplaceEnd(session, now);
    }

    private ScenarioStatus ReplaceStep(IGameSessionView session, double now)
    {
        if (_shop is not { } shop)
        {
            return ReplaceStopped(session, now, DeathReplace.Failed, "TODO(state): the replace step has nothing running");
        }

        var status = shop.Step(session, now);
        if (status == ScenarioStatus.Running)
        {
            return now - _stepStart > ShopSeconds
                ? ReplaceStopped(session, now, DeathReplace.Failed, string.Create(CultureInfo.InvariantCulture, $"the replace trip was not over within {ShopSeconds:0} s (stage {shop.Stage})"))
                : ScenarioStatus.Running;
        }

        _shop = null;
        _log.Info(LogCategory, $"replace: the trip ended {status}: {shop.VerdictFields()} {shop.ReplaceFields}");
        if (status != ScenarioStatus.Succeeded || !shop.Ok)
        {
            return ReplaceStopped(session, now, DeathReplace.Failed, "the trip failed: " + (shop.Failure ?? (shop.Problems.Count == 0 ? status.ToString() : shop.Problems[0])));
        }

        // Check after: what is worn now against what was missing.
        var after = DeathReplace.Missing(session.State, GearFloor.FloorLevel(LadderLoader.WarMage));
        var got = _replaceMissing.Where(k => !after.Contains(k)).ToList();
        foreach (var kind in _replaceMissing)
        {
            _log.Info(LogCategory, $"replace: after: {DeathReplace.Name(kind)}: {(got.Contains(kind) ? "now worn" : "still missing")}");
        }

        var planned = shop.FloorPlan?.Lines.Select(l => l.Kind).ToList() ?? [];
        var failedKinds = after.Where(k => _replaceMissing.Contains(k) && planned.Contains(k)).ToList();
        foreach (var kind in got)
        {
            _replacedKinds.Add(kind);
        }

        if (after.Count == 0 || !_replaceMissing.Any(after.Contains))
        {
            Replace = DeathReplace.Join(got);
            _log.Info(LogCategory, $"replace={Replace}: {DeathReplace.Describe(got)} bought for {shop.Spent} pyreals and worn; the {_afterReplace} follows");
            return ReplaceEnd(session, now);
        }

        var still = _replaceMissing.Where(after.Contains).ToList();
        if (failedKinds.Count > 0)
        {
            return ReplaceStopped(session, now, DeathReplace.Failed, $"{DeathReplace.Join(failedKinds)} was in the plan and is still missing after the trip (bought={shop.Bought}, refused: {(shop.FloorRefused.Count == 0 ? "none" : string.Join(", ", shop.FloorRefused))}; worn now: {DeathReplace.Join(got)})");
        }

        return ReplaceStopped(session, now, DeathReplace.Short, $"{DeathReplace.Join(still)} could not be paid or was not sold (spent {shop.Spent}, short={shop.Short}; worn now: {(got.Count == 0 ? "none of it" : DeathReplace.Join(got))})");
    }

    /// <summary>The end of the replace step (whatever came of it): on the corpse path a wielded caster is the rearm and the rebuff goes first; else the step that was asked for.</summary>
    private ScenarioStatus ReplaceEnd(IGameSessionView session, double now)
    {
        var next = _afterReplace;
        if (_replaceCorpsePath)
        {
            if (CombatModeGuard.Read(session.State).Caster is { } held)
            {
                Rearm = DeathRearm.Bought;
                Rebuffed = RebuffedPretrip = DeathLegVerdict.RebuffedSkipped;
                next = "rebuff";
                _log.Info(LogCategory, $"handler: rearm={Rearm}: the {held.Name} is wielded (the replace step's, the floor's caster): the rebuff goes first, then the corpse trip; the original comes back on the corpse and is carried as the spare");
            }
            else
            {
                _log.Warn(LogCategory, "replace: fallback by name: no caster is wielded after the replace step: the rearm=corpse path as before (the trip first, the wear step wields the best caster, the rebuff follows the items back)");
            }
        }

        return Enter(next, now);
    }

    /// <summary>The take-back's rule: a corpse entry whose slot the replacement already fills stays on the corpse (see <see cref="DeathReplace.LeftOnCorpse"/>).</summary>
    private LayoutTakeAll MakeTakeAll(IGameSessionView session) => new(
        new PackRules(_ground),
        SaleMemory.Shared.KeptAside.Contains,
        PackMemory.Shared.Rotation.Contains,
        _replacedKinds.Count == 0 ? null : item => DeathReplace.LeftOnCorpse(item, _replacedKinds, session.State));
}
