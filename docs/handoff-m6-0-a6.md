# Handoff, M6 item 0 slice a6 (Winnie's wiring)

Done
- Play host (`AutopilotHost.cs`): in `--autopilot-job=level-up` the plan is the climber's (`AutopilotHostLogic.PlanFor`), the required character and the refusal are `RequiredCharacter` and `StartRefusal`, the checkpoint file is Winnie's own (`winnie.json`), and the run's ground is the ladder's rung for the character's level (`LevelUpGround`, read at each fresh start: first press, `/auto` from Off or Done). A level with no run refuses by the ladder's own name; an unattended press that is refused ends the game with exit code 1. The den job is unchanged.
- Move-on: `AutopilotHostLogic.MoveOn` (host hook) and `MoveOnRestart`: on `stop=rung-moved:*` (ok) the host reads the new level and rung, rebuilds the parts over the new ground (the spell table is shared) and the dial restarts once by itself (still unattended when it was); a refusal or a missing hook stops by name (`failed:rung-restart-refused`, exit 1 when unattended). The next run's first cycle is the after-check (`failed:rung-ground-mismatch`).
- Winnie's buff profile (`BuffProfiles.Climber`): the arena lines, every line `ByPlay`; `BuffCycle.Wants` and `Choose` already cast only the levels the book holds and log each missing line once.
- Way home without Lifestone Recall (`WayHomeRule`, `TravelStations.cs`): a read spellbook lacking the recall walks from an outdoor ground the profile says is hunted on foot (no portal, cells listed) to the home's lifestone point (`TravelPurpose.Bind`, a point goal), purpose logged, arrival checked (`arrival-off-point`); every other case is `recall-home: no way home` (names `m6-first-tie-walk`). The den-travel station's outdoor case walks to `outdoor.cells[0]` (`TravelPurpose.Cell`).
- `RecallHome.ForCharacter` knows Winnie (a missing spell still fails `recall:spell` by name); `GearScenario.WrongCharacter` (the grant gate of `ScriptLeg`) accepts Winnie so a missing item is a failed check, never a grant; the build gate (`WrongBuildCharacter`) stays BSD Mage's.
- `tools/slice-check.ps1`: a backend step in `-Steps` is `[slicecheck] steps SKIP backend step <name>: slice-check never starts the backend`.
- Tests: `ClimberHostTests` (11), `WayHomeTests` (9).

Left (named)
- The trip's recall (`LogisticsStation` through `JourneyTravel` Home) still casts Lifestone Recall: for Winnie it fails `recall:spell`; the walk needs the same `WayHomeRule` in `JourneyTravel.Start` (Home case). `TODO(m6-trip-way-home)`.
- `BuffCycle.PickMana` does not read the spellbook: Winnie's Stamina to Mana / Revitalize levels are picked by skill chance only. `TODO(m6-ladder-mana-line-book)`.
- A death outdoors: the corpse walk is the dungeon's `DeathGroundWalk`; the respawn (Holtburg) is right. `TODO(m6-death-outdoor-corpse-walk)`. `DeathScenario.ChooseCharacter` and `GearScenario.ChooseCharacter` (the headless re-entry) stay BSD Mage's.
- A move-on to a dungeon rung waits for the den's rooms (`KitLoads` reads them on a worker); untested live.

Run: `pwsh -File tools\play.ps1 -Account test -Character Winnie -Offscreen -IsolateUserData -KeepServer -TimeoutSeconds <n> -GameArgs:'--autopilot-job=level-up --autopilot-start --autopilot-minutes=<m> --max-fps=60 --mute'` (the lead's, against the live backend).
