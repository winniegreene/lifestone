# Handoff: M6 item 8 slice B2b (the wand's chain, the stone, Burun, the two offline fetches)

## Done
- Five quest files: `atlan-wand` (ladder 75-126 once), `minor-smoldering-stone` (45-126 once), `burun-history` (78-126 once, three reports), `repaired-shadow-stone` and `shield-of-yanshi` (no ladder).
- The wand's chain, in order: go Crater Lake, go the north shore via, go the Caves portal, use, hunt-until 16 motes, recall home, combine 8/4/2/1, go Crater Lake again, seven tower flights, buy the glyph, apply it (6027), give the golem the ingot for the wand, recall home, go the stone, pick up, apply it (6068), wear the wand.
- Recipe 789 names its fail result 6330 and line; the file's `todo` names D34's grant (`TODO(m7-atlan-alchemy-by-play)`). No grant, no Alchemy: `combine:untrained`, nothing spent. A lost roll is `combine:failed-roll`.
- `m6-atlan-mana-recharge` remainder: `CasterManaRuleTests` on 46128's real numbers (500, -0.025 a second), `CasterManaRule.ChargeMana` cites its SELECT.

## Gotchas
- Two walks the brief assumed do not exist, found on the dats: back round the crater from the Caves' Surface arrival to the village (refused at the lake floor; the same walk outbound passes) and down from the roof (the roof is outdoor cell 0x90D00015, no plan back into the tower). So the file recalls home twice (Lifestone Recall, in the book at 75); no Surface portal, return walk or descent in it.
- The Underway portal's facing side is a wall; the go stands on the side she comes from (noted in its step's `source`). Burun's two dungeon walks plan and confirm in the dat test.
- The Yanshi town is not in the travel file: Burun's go to Ydnew stops `go:town-network` by name, `TODO(m6-yanshi-route)`. Burun is on the ladder and will fail by name at 78 until the route is added.

## Next
- The doc pass: `cache/m6/8/doc-notes-b2b.md` (the new TODO names and their placement, the two recalls, D34's exact gate steps).
- Item 10: the frost line opens the motes (the file's `TODO(m6-fight-element-line)`, also on the stone's go). The stone's band is the lead's call (see the reply).
- D34: the gate's named grant (the doc notes' exact steps; there is no skill-training verb in the mod, so read her credits first).

## Test count
Default: floor 14,240 + 18 new (13 AtlanChainTests, 5 FetchQuestTests). Exhaustive: `Every_value_of_the_chain_and_offline_files_...`, the three `The_go_legs_of_a_chain_file_...` rows, the flights equal B1's.

## Open TODO names
`m6-yanshi-route`, `m6-burun-surface-walk`, `m6-offline-fetch-walks`, `m6-repository-cliff-climb`, `m6-xarabydun-route` (new); `m6-fight-element-line`, `m7-atlan-alchemy-by-play` (carried).
