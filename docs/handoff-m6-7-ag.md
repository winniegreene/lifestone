# Handoff: M6 item 7, slice AG (the corpse trip against the death watchdog)

**Done.** Read the autopilot-mage lane's log of gate m6-7-g in order and fixed three things it showed.

- The trip's fights have a budget: `DeathHandler.TripFightBudgetSeconds` (300 s). Past it the trip stops only for an attacker (10 s quiet, M5 D88) and walks on.
- The death state's watchdog reads the body: 10 m restarts its clock, as the trip state's does (M6 4 F1). The 900 s is for a body that stands still.
- A cut's second entry runs no respawn: a living body away from the arrival goes straight to the handler's order from the rearm (`respawnRead`), keeps the handling's deaths, and no longer classifies a lifestone it cannot see. `DeathScenario.Dispose` leaves an observed tally running.

**Why this and not the others.** The log's trip worked: 40 kills, 650 s of fights, 76 s of walking, health never under 77%, cut 9.7 m short. So the watchdog was wrong to cut, and nothing bounded the fights. A cut that resumes the walk alone would have kept the unbounded fights.

**Log corrections.** No second death: every count reads 1. The take-back did wear the kit, at 13:23:56 to 13:23:58, after the cut. Bare for 16 min is the scripted undress (AE's).

**Real deaths.** Affected: same handler, same 20 m rule, same 900 s. Scripted only: the bareness and a 75 s head start on the clock.

**Next.** The lead's autopilot-mage rerun is the live proof (the budget, the watchdog, AE's `rewear=`). Doc pass: `cache/m6/7/doc-notes-ag.md`.

**Gotchas.**
- The budget narrows M5 D92 (an owner rule) after 300 s of fights on one trip: a D row, and name it to the owner.
- Past the budget the loot goes on with a creature near, unless it attacks.
- A cut's second entry has a fresh budget (a cut is now a stall, so rare).
- A walking body restarts the death clock, so a stuck leg is caught by the handler's own 90 s trip clock, not the watchdog.
- `AutopilotWatchdogs.BaseSeconds`' doc is stale (outside this slice's files).
- The autopilot's `kills=` and `xp=` leave out the trip fights' kills (report-only, not changed).

**Tests.** 13,166 default (10 new). Exhaustive not run (no folder that needs it). No loopback tests added.

**Open TODO names.** None new.
