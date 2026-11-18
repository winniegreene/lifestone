# Handoff: M6 item 8 slice B2a (the three low-level fetches as quest files)

## Done
- `worcer-heirlooms.json` on the ladder (band 5 to before 11, once): 30 steps from Holtburg's lifestone, go and use the Redoubt portal, eight items (the Wedding Band from the Hope Chest), out by the Surface portal, eight gives to Worcer, home to the lifestone. Walks end to end on the dats.
- `nen-ai-pet-drudge.json` and `four-stones-of-jojii.json`: quest files that load and match the world copy; **off the ladder** (`ladder` removed), with named TODOs on the blocked steps and in `sources`.
- `travel-routes.json`: `holtburg-redoubt` place, 0xAAB1 in `holtburg`, the two Redoubt edges, its way home.
- `pick-up:too-recent`: the named stop for the 20-hour refusal, floor and chest (`QuestRun.PickUpRefusedCheck`).

## Next
- D118's count: two fetches are owed by its swap rule (the lead's call). Before picking Reserve B, check its portal's building cell 0x79660101 for the same stair.
- Live: the first ladder run of Worcer at level 5-10 reads the floor pick-ups and the Hope Chest for the first time.

## Gotchas
- A `go` to a portal stops at its front with no target: the touch is the `use` step's (a go with a target walks to UseRange and would be taken by the portal).
- A chest's `go` point is 1.3 m from the chest on the floor, so the walk ends in reach whether or not the chest is yet in view.
- `QuestCatalogSqlTests.ItemFive` excludes the fetch files by key (they carry `experience`).
- The Town Network hop theory now also covers the Redoubt edges and every Holtburg edge from the Redoubt's Surface drop.

## Tests
- Default tier +14 (`FetchQuestTests` 12, `QuestStationTests` 2). Exhaustive tier: the dat walk and two pins (`TownNetworkWalkDatTests.Fetch.cs`), the SQL check of the three files.

## Open TODO names
- `m6-fetch-tower-stairs`, `m6-fetch-cathedral-ramp`, `m6-fetch-hive-drop` (causes and placement in the slice's doc notes).

## Tests changed for the first ladder quest
- `QuestProfileTests` (the file list, pathwarden's index), `ScrivenerLearnTests` (Holtburg's hop landblocks with 0xAAB1), `AutopilotKeeperFightTests.Climber` (level 5 is in Worcer's band: the rig has no quest station, so Worcer is recorded done there).
