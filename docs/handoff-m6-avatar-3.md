# Handoff: M6 avatar 3 (the steer desk's controls, the person hold, the re-seat)

**Done.** The Control orders on the desk: `start`, `resume`, `stop_home`, `stop_here`, `leave`, `cancel`. A person's stop in any mode is a hold only their own press clears (`AutopilotHostLogic.PersonHold`); an order's own stop is not (`StopHereByOrder`, `InterruptedByOrder`). `Leave` is the way out: the stop file's path while running, a log-off where she stands while off or done. `AfterRun` lets the desk claim a run's end, and has no claimant until slice 4. `AskSeat` re-seats a run in place at the Known state or a Lull (`AutopilotScenario.Steer.cs`), carrying `AutopilotSeatCarry`; no order calls it until slices 4 to 6. `SteerOverlay` is stored, restored and set aside by guid; no order changes it yet. After a restart, an order left `running` answers `refused:restarted`.

**Decisions to read.** (1) The carried guards are a new `AutopilotSeed.Seat`, set only by a seat, so hand-backs behave exactly as before. (2) A safe point is good only for the frame that reached it, and a run already stopping is never cut. (3) `leave` works on a run a person started too, or a yield to a gate could hang. It is never the desk's to claim. (4) A person's hold also refuses `leave`, by contract section 7: a person at the keys is the one to log off. (5) Edits to `AutopilotScenario.cs` are kept to four small hooks, because `m6/8` is changing that file.

(6) Only the host's /auto and press say a person pressed (`PersonPressed`): the dial's `Toggle` cannot tell /auto from a move-on's press. (7) A person's stop withdraws an order's press still waiting for the parts, and ends a `leave` in progress. (8) A `start` the host refuses is answered, `failed:not-started`, and never takes the fault exit. (9) `!StopRequested` on the `LadderCycleStart` arm is the one change that reaches runs with no desk: a stop asked during a trip with a move-on due used to be lost, because the run restarted on the next rung.

**Next.** Slice 4 (`trip`, `errand`, `run_quest`) with the `gate-shut` check, or slice 2 (`capture`). Merging master in after item 8 lands will touch those four hooks.

**Gotchas.**
- `SeatAtKnownState` sits above `TaskArrow` and `LadderCycleStart`, so it has to read the same guards itself.
- A `start` refused by the host (wrong character, parts failing) does not answer until its life ends: `failed:not-started`.
- `Interrupted` still counts a `stop_here` by an order. The `interrupted=` line means the dial was interrupted, not that a person was there.

**Tests.** `slice-check -Steps play-checks -Rerun`: build 0 warnings, 14,181 default tests passed, 6 skipped, 0 failed, play-checks 262 passed, 0 failed. A plain script ran 33 of 33 drill rows against the real desk in an offline game. A seat with a move-on due is proved over the scripted ACE: the same world fails `rung-ground-mismatch` fresh and takes `rung-moved` seated. An independent review found ten bugs here, all fixed before this commit. A seat refused for a hostile, a trip, a death, a spend or a pull is checked by reading only: no test reaches those guards. Not run: anything against a live server. `start`, `stop_home` and a seat have never run on a real character.

**Frame times.** The desk opens 5 s after the character is in the world, to keep its first work out of the login. Six alternating offline runs under the same load: frame-time rows failed in 2 of 3 with the desk closed and 1 of 3 with it open, and the worst login frame was 41-51 ms closed, 35-40 ms open. No difference shown, on a small sample. The direct launch fails those rows more often than the smoke's run does, with or without the desk; the reason is not known.

**Qwinnie's identity (the commit after this slice).** Every gate that compared a name to the climber now reads `MageTemplate.IsClimber`, and a repository test fails any new comparison; on merging `m6/8`, its new gates must take the predicate too. `--autopilot-climber`, `--journey=qwinnie`, `--scenario=climber-create --account=qwinnie`, `climb.ps1 -Climber Qwinnie`. The purge lists in the server mod name her, so re-creating her needs the mod rebuilt. Not done: per-account stock lists (the contract's section 10). Nothing here has run against a server: her account does not exist yet.

**TODOs.** `m6-climber-account-stock`: the profiles' stock lists are sized on the account `test`'s formula.
