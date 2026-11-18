# Handoff: M6 avatar 1 (the steer desk's probes)

**Done.** `--autopilot-steer-dir=DIR` opens a file-based order desk on the play host: the option, the spool, `status.json`, `events.ndjson`, and the probes `ping` and `sheet` (skills, attributes, spells, buffs, inventory, equipped, quests). Every other kind in the contract is refused by name, `not-built`. Nothing here moves the character or changes what the autopilot does. The design is `docs/autopilot-steer-contract.md`, sections 2, 3 and 8, and its "Slice 1 as built" note. Cut from `master` at `5966212e`, so `m6/8`'s scroll rule is not in it.

**Decisions to read.** (1) The spool is pure over `ISteerDisk`, so every failure case is a unit test; the real files and the thread are `AutopilotHost.Steer.cs`, covered by `PlayChecks.Steer.cs`. (2) A file stays in the inbox until its report is written, so a crash between the two is refused as `restarted` and never lost. (3) Status is coalesced: only the newest picture is written. Reports and events keep their order, and a failure is retried at the head of the queue. (4) A normal close is an information line and a `notice`; only a real fault is an ERROR, because the smoke fails on ERROR lines. (5) `AutopilotScenario.Ground` is the one addition to existing autopilot code, a read-only property.

**Next.** Slice 2 (`capture`) and slice 3 (the desk's Control orders, overlay, `Reseat`). Before slice 3, re-read sections 4 and 5 against the base and re-run `check_citations.py`. Before this lands on master, record the exception to M6 D14 in `m6-plan.md`, and remove this file.

**Gotchas.**
- A reader that holds `status.json` or a report open can make the replace fail on Windows for a moment. The pump retries it; a reader should expect a brief sharing error too.
- The poll is 1 s, so a report takes about a second. That is the poll, not the frame.
- `logic.Lines` only grows while a scenario lives; the stream re-reads tallies without an event when the scenario changes.
- The stand-in has no skills, so the play-check's skills sheet is nearly empty. The skill table path is covered only by reading the code.

**Tests.** 74 new in `SteerDeskTests` plus 23 rows in the play-checks. Default tests: 14,139 passed, 6 skipped, 0 failed. `slice-check -Steps play-checks -Rerun`: build 0 warnings, play-checks 260 passed, 0 failed, no Godot errors. A plain script ran 26 of 26 drill rows against the real desk in an offline game. With the desk open from login, one offline run failed five frame-time rows and the rerun passed; the cause of the first is unknown. In the rerun the desk cost 7.5 ms on the frame it opened and 3.1 ms at worst after that, with the drill running (logged at close as `the desk's cost on the game thread`), so it is far under the 140 ms spikes of the failed run. Not run: anything against a live server, or with the dial running on a real character.

**TODOs.** `gate` events have no seam yet. `seat` events come with slice 3. `rung.reason` is null until the rung seam.
