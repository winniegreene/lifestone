# Handoff: M6 7 BE (keep the way out)

**Done.** On the ladder's job, with Lifestone Recall ready, she keeps the mana for it. `ManaWayOut` (`Session/Vitals`) is the pure rule: the line is the recall's need (`RecallNeeds`: the cast's ceiling or the station's 20 %, the more) plus Drain Health Other I, 60 at level 22.

- Leaving (`Decide`): the recall ready, a live attacker in the last 10 s, the mana at or under the line or the next spend refused, the mana still paying the recall, no kill of the last attacker pending, no press on its way. It is asked in `WatchFight` after the health retreat and takes the same stop (`RetreatRule.StopReason`).
- Spending (`Spend`), in order: no attackers: free. The kill of the last attacker: free. Leaves the mana at or over the line: free. A buff under that: refused, always. Under the recall's need: free (`recall-gone`, the old order). The drain or Heal Self: free while the recall stays paid, or when the next hit kills. The rest: refused.
- The fight's half is `FightScenario.WayOut.cs`: gates in `BeginTarget` (the bolt), `EmergencyStep` and `HealDecide` (the heal), `RefreshStep` (hard and leave lines). `ArmWayOut` gives the fight the line each frame; without it the fight is as it was.
- The way home is `RecallHomeSpec` by name, not `HomeSpec()`: from the outskirts the rule walks home.
- Check, act, check: `TakeManaRetreat` reads the book, tie, components and mana again; unpaid, no cast, back to the fight by name, the rule aside 10 s. A failed recall logs `retreat: mana: the way home did not land`, then the station failure's recovery; refused for components, M6 7 AV's way home.

**Decisions to read.** (1) Today a second retreat in a cycle stops `retreat:again:<ground>`, ok=no. A retreat for mana counts in `retreats=` and never toward that stop. (2) The press: `mana_loop_under` 45 is under the recall's 50, so with the line armed the loop could never run with attackers on her. A refused bolt therefore presses first when a level leaves the recall paid and nets half a bolt (`way out: a press before the line`); the loop's sums are untouched. (3) Scope: `_rung` set, job level-up, task job: the den, gates' lanes and journeys never arm. (4) Attackers: the fight's own list, so a dead attacker's notice does not hold the reserve; a buff also counts a hostile within the refresh's reach (the 04:59:27 pass, 176 mana to 8, came before the first hit). A leaving does not: an awake creature that has not attacked is the rest's business.

**Next.** The lead's live proof: a Lodge fight that reaches the line; read `retreat: mana: decide`, `retreat: taken for mana`, `retreat: mana: check after`, `why=retreat-mana`. The walk back to the Lodge after a retreat is proved only on the outskirts' rig (no scripted rung change here).

**Gotchas.**
- `PickerDecide` sits behind the bolt gate, which weighs the bolt's cost: a dearer pick (a ring, a debuff) can land under the line; the next frame leaves.
- Revitalize's hook in `BoltNeedsMana` keeps the line plus the bolt when armed; the mana order's own Revitalize under the bolt's floor is not gated (under the recall's need anyway, the old order).
- The fight tests' rig casts a level VII bolt at 70 mana and models its bolts against the target: `AtTheLine` pushes the tusker back to full, and stamina under full brings a Revitalize or a stamina drain first.
- `ScrivenerCatalog.Default` holds Lifestone Recall: a test of a book without it must filter it.
- Rigs: `PlayerDescription` replaces the qualities, set the level after it.

**Tests.** 54 new (`ManaWayOutTests` 32, `FightScenarioTests.WayOut` 13, `AutopilotWayOutTests` 9); one is Tier=Timing, so 53 count: 14,024 default passed, 6 skipped. The branch's start therefore counts 13,971, 2 under the brief's 13,973. No test deleted. No loopback test added.

**TODOs.** New: `m6-way-out-drain-row` (no row for Drain Health Other I: the way out stays off, named). None closed.
