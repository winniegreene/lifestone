# Handoff: M6 7 AE (the rewear after the respawn)

Done
- `DeathHandler`: a new step `rewear` between the rearm and the rebuff (and before the route in the kit-less `rearm=corpse` path). Check before: every piece of the worn memory is logged as worn, in the pack or not held. Act: a `WornRestorer` in its new memory-only mode wears the pieces held and not worn. Check after: each piece logged worn or not worn. Verdict field `rewear=<worn>of<held>|none|n/a` (report-only, D27), appended to the handler's `Fields`.
- A real death is a logged no-op: no piece of the set lies in the pack, so `rewear=none` and no step is entered. The memory-only restorer skips the appraisals, the clothing and caster picks and never re-snapshots the memory, so the take-back still wants what lies on the corpse.
- `WornRestorer`: optional `memoryOnly` ctor argument, `IsDone`.
- Tests (DeathHandlerTests): the undress case (2 pieces in the pack, worn before the rebuff, memory unchanged, the corpse piece left to the take-back) and the real-death no-op; pinned field strings updated.

Gate log read (lane autopilot-mage, slot g1): undress at 13:07:52 moved 4 pieces; death 13:09:06; lost line took Leather Boots, Leather Gauntlets, the wand (plus scarabs, pyreals); no robe in the lost line, so it stayed in the pack. The rebuff at 13:10:25 missed Impen and BludB (no-worn-piece). The take-back never ran in that run: the corpse walk was fought for 12 minutes, the 900 s death watchdog cut the station at 13:22:51 and the handler restarted from the respawn (no `handler wear` line in the log up to its end). So no re-dress was seen.

Next: the lead's live rehearsal should show `rewear: before/after` lines and `rewear=Nof N` after the next scripted death.
Gotchas: the 900 s watchdog vs the trip fights is a separate problem (For the lead). The memory is the autopilot's snapshot; a piece never snapshotted is not rewoven here.
Test count: baseline 12,823 + 2.
Open TODO names: none new.
