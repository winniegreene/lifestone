# M6 7 AX handoff: the tusk pull, the Cave's gate, the buy check

Branch m6/7-ax. Scripted and dats only: nothing here has run live.

## Done
1. **The buy check.** Cause: a focus arrives as ContainerType 2, beside the side packs, so the check and `LogisticsCounters` never saw it; that was the double buy, not only a slow update. The check reads every held place, waits a named 10 s (`BoughtArrivalSeconds`), and a ware that never shows holds its line out of one plan (`TODO(m6-bought-unseen)`). The spare focus stays, pinned.
2. **The Cave.** A gate of the Lodge's lines with Armor Self V and Bludgeoning Protection V. Fight data on the Cave and the Temple. 65 is the Crimsonback's mean hit: largest 104, critical 208, not the audit's 130. Model at health 95: pack of 4 at IV 8.5%, at V 2.2%; pack of 6 at IV 84%, at V 19.8%. Health hardly moves it, so no health condition. A test proves the plan on the Lodge names both V scrolls, so the gate can open.
3. **The tusk.** The owner's decision: the Female Tusk counts as gate A's tusk, changing D76. `RungOwner.Decide` has an additive job (`RungAsk.TrophyDue`, reason `job:tusk-female`); `hunt.pulls[0].stand` is the data; `FightScenario.Pull.cs` is the pull; `TrophyPullMemory` keeps tries (2) and a named failure on the session. Burrow retreat 0 to 40.

## Stand point
0x5748013D (103.84, -115.50, -15.30), holder slot 6, a 5-pack, 35.1 m. Not 0x0142 or 0x0149: those reach only the 6-pack holders.

## Gotchas
- **The clear line is unproved.** The sweep is noisy on these floors: 0 of 546 points were clear on all 18 jittered samples, even at 15 m. So the shot does not read it; the server sending the holder is the check. A bolt on the ledge ends the pull `failed:shot-blocked`, and that end has no scripted test.
- If the client is not sent the holder at 35 m, she waits 420 s, then `failed:holder-absent`.
- Model at her kit: about 1% death a try, but 47% if a Female's hits are twice the Male's. No Female has hit her at this kit.
- A refused recall means a walk to the exit under the hall, through the pack. Not expected to live.
- No scripted test runs the whole chain: job, pull, turn-in, back to the rung. Each link has its own.
- A tusk on a corpse after a death is not fetched; the 20 hour pickup rule then blocks another (the survey).
- `WholeClimbGateTests` runs with `TrophyQuests = []`: it proves the gates only.
- A pack of 8 is 97% death even at V and V (`m6-cave-big-packs`).

## Tests
Count in the commit's slice-check line. New: `ResupplyStationTests.BuyCheck`, `CaveGateTests`, `TrophyJobTests`, `FightScenarioTests.Pull`, `TuskPullDatTests`. Pins moved: `RungGateRuleTests` (22 held at 21; the gate list), `PullRestRuleTests` (no-rule ground now the Abode). None deleted. Loopback: none.

## Open TODOs
m6-tusk-pull-live, m6-burrow-choke-live, m6-bought-unseen, m6-cave-big-packs, m6-gate-past-the-cave, m6-cave-fight-live, m6-temple-fight-live.
