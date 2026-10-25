namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which of the melee hunt's three window frames a frame of the game triggers (<see cref="HuntUiShotPlan"/> slots 0 to 2).</summary>
[Flags]
public enum HuntMeleeSlots
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Slot 0: <c>c1-combat</c>.</summary>
    Combat = 1,

    /// <summary>Slot 1: <c>c1-powerbar</c>.</summary>
    PowerBar = 2,

    /// <summary>Slot 2: <c>meter</c>.</summary>
    Meter = 4,
}

/// <summary>
/// The melee hunt's window frames' triggers as one pure function (M4 P5b J7): lifted out of the hunt leg runner so the journeys' hunt station takes the same frames on the same conditions
/// (the hunt leg runner may call it). The inputs are what the host reads from the session, the combat window and the selection display.
/// </summary>
public static class HuntMeleeShotTriggers
{
    /// <summary>The slots a frame triggers.</summary>
    /// <param name="near">The target is within <see cref="HuntUiShotPlan.TargetRangeMetres"/>.</param>
    /// <param name="attackActive">An attack sequence is active.</param>
    /// <param name="swings">The sequence's swings so far.</param>
    /// <param name="melee">The combat window is shown in Melee mode.</param>
    /// <param name="meterLevel">The combat window's power meter, or NaN when it has none.</param>
    /// <param name="runPower">The power of the run being charged.</param>
    /// <param name="healthFraction">The toolbar's health meter of the selected target (null when it is not shown for the target).</param>
    /// <param name="healthAtSelection">The meter's fraction when the target was first selected (NaN before).</param>
    public static HuntMeleeSlots Evaluate(bool near, bool attackActive, int swings, bool melee, float meterLevel, float runPower, float? healthFraction, float healthAtSelection)
    {
        var slots = HuntMeleeSlots.None;
        if (near && attackActive && swings >= 1 && melee)
        {
            slots |= HuntMeleeSlots.Combat;
        }

        if (near && attackActive && melee && !float.IsNaN(meterLevel) && HuntUiShotPlan.MidCharge(meterLevel, runPower))
        {
            slots |= HuntMeleeSlots.PowerBar;
        }

        if (near && swings >= 1 && healthFraction is { } now && HuntUiShotPlan.MeterLowered(healthAtSelection, now))
        {
            slots |= HuntMeleeSlots.Meter;
        }

        return slots;
    }
}
