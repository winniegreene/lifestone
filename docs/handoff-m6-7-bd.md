# Handoff: M6 7 BD (no state's keeper stands under attack)

## Done

- One rule, one place: `src/Lifestone.Client/Session/Autopilot/AutopilotScenario.KeeperFight.cs`. In a keeper phase (Vitals, Peace) or a station's wait (`IStationWait`), on a ground, with a hostile on her, the keeper stops (`VitalsKeeper.Halt`), she fights back until no hostile is in reach (`keeper-fight-back`), and the state resumes from its start. Covers trip, travel, buff, upkeep, the heal in place, a resumed death, the fight's own phases.
- A logistics or forced trip waits for the lull, by name. A stop line with a hostile on her takes Lifestone Recall when payable, by name, then the trip from home, forced; not payable, she fights on and the trip waits.
- A leave refresh ended by an attack is dropped: no next line, the stop goes ahead.
- The tusk: Bonded 1 (world row 33), so ACE never drops it (`Player_Death.cs:502`). A death cannot take it. No code.

## Next

The lead merges, raises the baseline, and rehearses: grep `autopilot keeper:`, `urgent-trip`, `the trip waits for the lull`.

## Gotchas

- The rule stands down for: the retreat's way home, the urgent recall, the recall before a stop, a death not yet handled, a scripted death, the heal in place under the retreat line, a one-shot task (FXM1's bound stays).
- A creature that never attacks holds a state only to `KeeperFightQuietCapSeconds` (45 s); after that only a new attack stops the keeper.
- `BeginRun` and `EnterKnown` forget a fight-back: a run that begins for any other reason owes nothing to it.
- While a fight-back runs, `WatchFight` reads no other trigger after the retreat rule.
- Heredocs with apostrophes break the Bash tool here: write scripts with Write.

## Tests

AutopilotKeeperFightTests (26), one each in VitalsKeeperTests, FightAZTests, J3bStationTests. AutopilotF23Tests Item3 puts the hostile in after the station starts. Count: 13,971 default.

## Open TODOs

- `keeper-fight-awake-read`: the read model holds no creature's target.
- `keeper-fight-fix-phase`: the ManaDance recovery fix has no hostile check.

Doc notes: `cache/m6/7/doc-notes-bd.md`.
