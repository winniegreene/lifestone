namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 redoubt (the owner's decision of 2026-11-18: the rung <c>outskirts</c> hunts the Holtburg Redoubt, whose portal admits levels 1 to 20): a rung's outdoor fallback ground. A rung whose own ground is entered by a level-limited
/// portal may list, among its alternates, an outdoor ground hunted from home on foot (no portal, no entry: the profile's own facts). That ground is the rung's too:
/// <list type="bullet">
/// <item>a climber the own ground's portal does not admit (a level above its limit, held down to the rung by shut gates or by the vitae burn) hunts it in the own ground's place
/// (<see cref="Autopilot.RungOwner"/>, the refusal named on the choice and logged loudly);</item>
/// <item>a run that stands on it (that fallback, a test, a run named by hand) is a run of that rung: it hunts on there (<c>AutopilotScenario.DecideRung</c>: the ladder sends a new run to the rung's own ground and never pulls one off
/// the outdoor ground), and the band's learn plan and scroll rules read the rung by it (<see cref="Mage.ScrivenerLearnPlan.RungOf"/>).</item>
/// </list>
/// It is never taken by an approach's verdict (<see cref="Autopilot.RungOwner.AlternateRefusal"/> refuses a ground with no approach recorded). No ground or level is named here (D26): the ladder lists the alternate, the profiles say
/// which ground is outdoors and what the portal admits.
/// </summary>
public static class RungFallback
{
    /// <summary>The rung's outdoor fallback: its first alternate with a profile that is an outdoor ground hunted from home on foot, or null.</summary>
    /// <param name="rung">The rung.</param>
    public static LadderAlternate? Of(LadderRung rung)
    {
        ArgumentNullException.ThrowIfNull(rung);
        foreach (var alternate in rung.Alternates)
        {
            if (!alternate.Pending && GroundCatalog.TryGet(alternate.GroundKey) is { } ground && OnFoot(ground))
            {
                return alternate;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="groundKey"/> is <paramref name="rung"/>'s outdoor fallback (<see cref="Of"/>).</summary>
    /// <param name="rung">The rung.</param>
    /// <param name="groundKey">A ground's profile key.</param>
    public static bool Names(LadderRung rung, string groundKey) => Of(rung) is { } fallback && string.Equals(fallback.GroundKey, groundKey, StringComparison.Ordinal);

    /// <summary>True when the ground is outdoors, lists cells and has neither a portal nor an entry: it is walked to from its home (the rule <c>WayHomeRule.HomeOnFoot</c> reads, and no entry portal).</summary>
    private static bool OnFoot(HuntingGround ground) => ground.Kind == GroundKind.Outdoor && ground.Portal is null && ground.Entry is null && ground.Outdoor is { Cells.Count: > 0 };
}
