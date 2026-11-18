# Handoff: M6 item 6, slice MD2 (the menu on screen, wired and checked)

Branch `m6/6-md2`, cut from `m6/6` after MC and MD1 (475ce25d).

## Done

- `AutopilotJobSource` (new `src/Lifestone.Client/Session/Autopilot/AutopilotJobSource.cs`): the menu's `IAutopilotJobSource`. It builds `JobMenu.Build` from the install job, the level, the next level's experience (XpTable), `QuestCatalog.All`,
  the timers (a delegate: the host's live run, else the file read-only), now and `LadderQuestKeys(LadderLoader.WarMage)`. `Refresh()` raises `Changed` when the rows or the task move, and logs the summary and the row lines once per change of the rows.
  Every `Jobs` read rebuilds against the clock (review 6-M14: the menu's minute rebuild shows a quest coming due; cheap, no I/O). An unknown next level gives the proportional rows `xp ?` (`TODO(m6-menu-xp-table)`, once). `For(logic, job, session, timers, log)` is the host's factory.
- `AutopilotHost`: owns the source (`Jobs`); `WireShell(..., MenuWiring? menu)` wires `screen.Menu` (`Source`, `Choose` = `RunTask(task, by menu)`, `Stop` = `StopHere("the menu's stop row")`), `screen.Inspector.Jobs` and
  `ToolsPanel.ToggleJobs` = `Menu.Toggle`, once per game screen. The answers also go to the chat. `RefreshJobs` at most once a second once a level is known, and at once after a choice. `TimersNow`: `AutopilotKit.Context.QuestTimersInUse`
  (new `Context` property), else `QuestTimers.Load` of `user://quests/<character>.json` once (`TODO(m6-menu-timers-file)` when unreadable). `RunTask` gains `by` (default chat) and restores the previous task when the start is refused (review 6-M12; the line says "The task stays <prev>."); 6-M16's typo fixed. `InstallWith` takes an optional source.
  `FightWalkRefusals.Sink ??= GodotLogSink.Instance` in `Install`.
- Tools panel: a "Jobs" button (0x7F000129, `tools.json`, x 206 y 226, above Autopilot), `ToggleJobs`, `JobsPresses`, `PressJobs()` (unwired: status and chat say so).
- Play-checks `PlayChecks.Menu.cs` (`AutomationMenuChecksAsync`, called at the end of `AutopilotDialAsync`), frame `11-shell-automation-menu` in `ShellFramesPlan.FrameNames`.

## For the lead

- `tools/smoke.ps1` line 1938: `$expectedFrames = 10 # ShellFramesPlan.FrameNames.Count (G7: 09 the automation inspector, 10 the tools panel)` -> `11` (", 11 the automation menu"); `tests/Lifestone.Client.Tests/Repository/SliceCheckAndSmokeTests.cs` 860 pins that text.
- Outside the file list: `tests/Lifestone.Client.Tests/Session/LoginCheckScenarioTests.cs` 228 pinned the frame count 10 (now 11).
- **A mouse click on a menu row chooses nothing** (play-check, report-only `menu-row-click`, `TODO(m6-menu-row-click)`): the menu (and the inspector) is a manager root made by `CreateRootElementByDataId`; the shell's root
  0x10000495 (its SmartBox viewport 0x10000436) lies above it for the hit test once the shell is clicked, though the menu draws on top. The host's Jobs toggle brings the opened window to the front of its z level: not enough.
  Fix in MC's `AutomationMenu` (make the window a child of the screen root as `ToolsPanel` does, or raise it above the shell's root on open). The check then chooses by `ChooseRow` and says so in its detail.
- **No quest row shows its xp on screen** (report-only `menu-quest-row-shows-its-xp`, `TODO(m6-menu-row-xp-clipped)`): `AutomationMenu.FormatRow` puts `| xp n` after the label and clips at 68 characters; the catalog's names are long.
  Put the xp first (or clip the label only).
- The play-checks step also logs two ERROR lines `autopilot audit caster: the hands and the packs hold no caster` from MH's reconnect check (`the-dial-runs-before-the-drop`, idle stations, after MF's audit): not this slice's; the step fails on them (pass=240 fail=0).
- The login-check leg's `AutopilotLegRunner.WireShell` call passes no source: its menu says "No job source". Pass one there if the leg should list jobs.

## Gotchas

- The play-check closes a retail panel page the dial check's panel director left open (the inventory) before the menu, so frame 11 shows the menu whole.
- A `Jobs` read that finds the rows moved raises `Changed` inside the menu's rebuild (which cleared its dirty flag first): one extra rebuild the next frame, then it settles.
- The source's catalog and ladder are read at the host's install (embedded files).

## Tests

- New: `AutopilotJobSourceTests` (8), `ToolsPanelTests` (+2). Default count 12,763 (12,753 before + 10); exhaustive 967.

## Open TODO names

- `m6-menu-xp-table`, `m6-menu-timers-file` (guards; close as designed); `m6-menu-row-click`, `m6-menu-row-xp-clipped` (MC's window: for the lead).
