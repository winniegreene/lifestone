using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E5e6: the trip's side of the return pass's guard (<see cref="GearReturn.UnlitGuard"/>; <c>TODO(engine-gear-return-unlit-guard)</c>, docs/automation-engine.md section 6). The plan keeps an attuned piece that a worn piece
/// beats only with its magic off for her; the trip names it once a visit and holds it out of every later Give of the same visit.
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The attuned pieces the guard kept this visit.</summary>
    private readonly HashSet<uint> _returnUnlitKept = [];

    /// <summary>
    /// <paramref name="plan"/> as the Give may act on it: each piece the guard kept is logged by its named line the first time this visit (<see cref="GearReturn.UnlitLine"/>) and remembered, and a piece remembered from an earlier
    /// table of this visit is kept, never given (<see cref="GearReturnPlan.Withheld"/>).
    /// </summary>
    private GearReturnPlan ReturnUnlitGuard(GearReturnPlan plan)
    {
        foreach (var k in plan.Kept)
        {
            if (k.Guard is { } line && _returnUnlitKept.Add(k.Guid))
            {
                Log(line);
            }
        }

        return plan.Withheld(_returnUnlitKept);
    }
}
