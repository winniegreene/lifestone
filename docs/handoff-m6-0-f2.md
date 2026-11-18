# Handoff, M6 item 0 fix slice F2 (the climb's remaining wiring)

Done: items 1 to 7 as the slice states them (the trip's way home, the outdoor death's targets and walk, the mana lines by the spellbook, the foci audit with a named stop, the outdoor arrival without an interior, `GameFightWalk.WalkSecondsTotal`, tests in `F2ClimbWiringTests`).

Left (named)
- `TODO(m6-death-outdoor-kit)`: a death on an outdoor ground throws `ground-plan-gap` when the death station builds its mage kit (`DeathScenario` constructor, `GearKit.Build`), because an outdoor ground has no worn kit. A kit-less death path (no gear stage, rearm from the corpse or a pack caster) is needed before Winnie can die on the outskirts rung. Needs `DeathScenario.cs` and the rearm in `DeathHandler.cs`.
- `TODO(m6-account-formula-plan)`: the foci audit stops by name (`m6-foci-missing`); planning with the account's full formula is not done.
- `TODO(m6-trip-way-back)`: the logistics trip's way back to an outdoor ground is a place, not a walk to the first cell (`LogisticsStation.cs`).
- The handler's trip look-out reads the outdoor targets now, but only the death station's walk choice is pinned by a test (the handler's own look-out sits in a private test rig).
