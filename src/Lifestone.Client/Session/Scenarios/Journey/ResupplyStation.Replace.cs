using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>M6 7 AH: the death handler's replace trip (<see cref="ResupplyOptions.Replace"/>): the floor's missing pieces bought and worn, nothing else.</summary>
public sealed partial class ResupplyRun
{
    /// <summary>
    /// The replace trip. Check before: the floor's plan for what is missing (<see cref="DeathReplace.Plan"/>, the purse as the state holds it) and the player at the home. Act: the walk to the vendors of the plan's lines, each buy through
    /// <see cref="FloorBuy"/> (its own check, act, check), then the equip pass (<see cref="EquipPass"/>: the wear with its own check after). Check after: the caller reads what is worn (<see cref="DeathReplace.Missing"/>). Nothing is counted, sold,
    /// learned or restocked, and the kit is never read. A plan with no line the purse can pay ends at once with no walk.
    /// </summary>
    private IEnumerable<ActionHandle?> ReplaceScript(HuntingGround ground)
    {
        Stage = "replace-plan";
        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        var plan = DeathReplace.Plan(ground, S.State, FloorPurse(), floorLevel);
        FloorPlan = plan;
        Log(plan.Why);
        foreach (var note in plan.Notes)
        {
            Log(note);
        }

        if (plan.Stop is { } stop)
        {
            Log("gear floor: " + stop + " (the replace step: the caster is not bought, the handling goes on)");
        }

        var buys = new List<ResupplyBuy>();
        foreach (var line in plan.Lines)
        {
            Log(line.ToString());
            var buy = ResupplyBuy.ForFloor(line);
            _floorBuys.Add(buy);
            buys.Add(buy);
        }

        _buyList = buys;
        if (buys.Count == 0)
        {
            Log("replace: nothing the purse can pay for: no walk");
            Stage = "done";
            yield break;
        }

        if (S.State.Self.Position is not { } here || (here.CellId >> 16 != ground.Home.LifestoneCell >> 16 && !ground.Vendors.Any(v => v.Cell >> 16 == here.CellId >> 16)))
        {
            Fail(string.Create(CultureInfo.InvariantCulture, $"not at the home: the player stands in 0x{S.State.Self.Position?.CellId ?? 0:X8}, neither the landblock of the home lifestone of {ground.Home.Town} (0x{ground.Home.LifestoneCell:X8}) nor a home vendor's"));
            yield break;
        }

        _visits = ResupplyPlanner.Order(_buyList, [], ResupplyPlanner.WorldMetres(here.CellId, here.Origin));
        if (_visits.Count > 1)
        {
            _visits = ResupplyPlanner.OutdoorStopsFirst(_visits, here.CellId, out var orderWhy);
            Log("resupply: the walk's order: " + orderWhy);
        }

        Log("resupply: the replace walk " + string.Join(" -> ", _visits.Select(v => v.Vendor.Name)));
        if (S.State.Self.Position is { } standing && S.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in standing, RecallHomeStation.OnLifestoneMetres, out var lifestone))
        {
            Stage = "step-off";
            SteppedOff = true;
            Log(Text($"resupply: stands in lifestone {lifestone:X8} (within {RecallHomeStation.OnLifestoneMetres} m): stepping off it to the lifestone point before the first walk (m5-logistics-step-off)"));
            foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone-point", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Bind), "off the lifestone")) { yield return h; }
        }

        var none = new List<(uint Guid, WorldObject Object)>();
        foreach (var visit in _visits)
        {
            foreach (var h in VisitVendor(ground, visit, none)) { yield return h; }
        }

        foreach (var h in EquipPass(ground)) { yield return h; }

        Log("resupply: done: " + VerdictFields() + " " + ReplaceFields);
    }

    /// <summary>The replace trip's floor counters on one line: <c>floor_bought=&lt;n&gt; floor_refused=&lt;n&gt; (&lt;names&gt;)</c>.</summary>
    public string ReplaceFields => string.Create(CultureInfo.InvariantCulture, $"floor_bought={FloorBought} floor_refused={FloorRefused.Count} ({(FloorRefused.Count == 0 ? "none" : string.Join(", ", FloorRefused))})");
}
