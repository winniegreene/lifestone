# M6 item 7 slice AZ: handoff

Done
- `BuffCycle.Abort(why)` / `Aborting`: ends a running pass at the next point between casts (a cast in flight finishes first); from the mana wait or a stamina rest she comes back to the magic mode, then the pass ends as done.
- `FightScenario.RefreshPassWatch` (Refresh.cs): every step of a refresh pass checks first. A soft pass ends when she was attacked (hit or evade since it began), a hostile is in reach, or a creature that can attack is awake in reach. A hard pass (line under the floor) still casts under fire; it ends only when it has to wait for mana or change mode. The line is held 30 s; `RefreshAborts` counts.
- Mana reserve: `refresh_mana_reserve` 60 in `war-mage.json` vitals (`LadderVitals.RefreshManaReserve`). The lookahead starts no line below it (logged once: "lookahead waits: mana ...").
- No lookahead pass starts while a creature is awake in reach ("waits: ... is awake in reach").
- Lookahead horizon is the margin alone (60 s); the fight's mean is read and logged, not added. Protections first is the existing group order. A full pass at home stays AW's.
- Tests: FightAZTests (2 new: reserve; refresh in a lull, mana gone, attacked, she fights in magic mode). Updated to the 60 s horizon: FightF25Tests, FightD126Tests (lull batch), AutopilotF23Tests item 1.

Next
- Lead's live rehearsal; watch the new log lines. If a hard pass at near-zero mana in a fight loops (end, 30 s hold, start), the hold bounds it to one try per 30 s per line.

Gotchas
- The lull batch still chains lines within 60 s of the last (D126); the reserve and the awake gate bound it.
- Test count: baseline 13926 + 2 = 13928 minimum.
- Open TODO names: none.
