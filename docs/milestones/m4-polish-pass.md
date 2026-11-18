# Polish pass inside M4 (between items 4.1 and 4.2)

**Status: P1-P3 done (2026-10-22, merged as "Merge m4/p (Polish pass)"); P4, the parallel live gate (D31), done (2026-10-23, merged as "Merge m4/p4 (The parallel live gate)"; m4-plan.md §8).** The owner asked, after item 4.1's merge (master d2e000e), for the polish pass that D28
had scheduled after M4 to run first, before item 4.2 ("The end"), and took the hero video out of 4.2 as a step of its own
(m4-plan.md D29). The branch is `m4/p`; the plan of record stays m4-plan.md (§4.10, §7 D29, §8 "Polish pass").

## 1. The items

| # | Area | What the owner asked for | Detail to settle |
|---|---|---|---|
| P1 | Tests: the near-duplicates (`m4-test-weight`, D28) | Reduce the near-duplicate tests (again, as the M3 to M4 pass did): about 5,000 test methods expand to 9,609 tests; one rule tested many times costs upkeep and agent tokens at every behaviour change | **Measure first:** read-only surveys list, per class, the near-duplicate rows (the same branch of the same rule with other constants; facts that differ by a value; theory rows past the equivalence classes of the rule) and the behaviours each class proves. **Then fold:** facts into table tests, rows down to one per equivalence class and boundary, without losing a behaviour; every deleted row named with the row that still proves its behaviour (D28). The count drops; the lead lowers the baseline with the list. |
| P1b | Tests: the deeper cuts (D30, the owner's, after P1 measured about 7%) | The ruling: not a big drop: all four cuts | **A** the prose text tests deleted (behaviour pins kept); **B** one golden-line test per leg verdict; **C** the stand-in's protocol tests kept only where a play-check relies on them; **D** the dat sweeps and pins to the gate tier (Tier=Exhaustive), nothing deleted. Each lane lists what it deletes or moves. |
| P2 | Visual: the bow | "When there's a bow I want to see the bow" (D27): not reproduced by a plain wield (4.1's look capture) nor by a scripted swap test (4.1 v1) | Reproduce live where the owner sees it: the missile hunt (`--hunt-weapon=missile`), its swap from the Mace to the Shortbow and back, frames at the swap and the shots; fix what is found, or name the place it was not seen. |
| P3 | Visual: the look capture's camera | The mage's magic-stance frame is taken from behind (4.1 §8) | The look camera's re-aim holds for every stance (the frame faces the player's front three-quarter). |

## 1b. What was built (branch `m4/p` at b527b0d, before the gate; the review's fixes and the gate's numbers are added at the merge)

**Tests (P1 and P1b; D28 and D30).** The default run went from 9,609 to 8,294 tests and the Exhaustive tier from 76 to 664. Coverage is the rule (D28): every deleted
test is named in the lane's record in the cache (`fold-*.md`, `cut-*.md`) with the test that still proves its behaviour, or with "prose only".

| Lane | What | Default count |
|---|---|---|
| P1 fold, client-session | facts folded into table tests, rows down to one per equivalence class and boundary (Session) | -123 |
| P1 fold, client-other | the same outside Session; the `WieldedSetups` matrix folded | -195 |
| P1 fold, data-net-mod | the same in Data, Net and ServerMod (34 classes, 1,221 to 916 rows) | -307 |
| P1b A | the prose text tests (6 whole tests; the wording asserts of 17 more) | -6 |
| P1b B | one golden-line test per leg verdict (Hunt, Death, Fight, Buff) | -11 |
| P1b C | the offline stand-in's own protocol tests, kept only where a play-check relies on the answer | -103 |
| P1b D | the dat-reading tests moved, not deleted, to `Tier=Exhaustive` (588 rows; the default stage about 28 s from about 60 s) | -588 (Exhaustive 76 to 664) |
| P2/P3 camera | `CameraPolishRulesTests` (10 facts, 9 rows) | +19 |

The arithmetic: 9,609 - 123 - 195 - 307 = 8,984, measured 8,983 at the start of D; then -588 -6 -11 -103 +19 = 8,294. The review (P-R1 to P-R3, P-R6) found
boundary rows, loud-stub tests and capture values that the cuts had lost; the restore lane puts them back, so the count at the merge is 8,294 plus what it restores.
The rule that follows from D: a slice that touches `src/Lifestone.Data`, or the Client folders whose dat tests moved (`Audio`, `Effects`, `Interiors`, `Movement`,
`Navigation`, `Objects`, `Play`, `PortalSpace`, `Rendering`, `Sky`), runs `slice-check.ps1 -Exhaustive` (m4-plan.md §4). Not folded, listed for later:
the duplicate product implementations `Objects/DegradeFrames` and `Effects/DrawFrameRules` (`m4-degrade-frames-dup`, a production merge the lead lists in m4-plan.md §9).

**P2, the bow, and P3, the look camera (the cam slice).**
- *The cause of the missing bow in the live frames.* The missile hunt never framed its camera: `HuntLegRunner.StepUiShots` returned before `FrameCamera()` for every non-melee hunt, so
  the live arrow frame came from retail's default chase camera (pivot 1.5 m above the player, viewer 2.5 m behind and 0.75 m up, level; all checked against retail and unchanged),
  where the player sits low and the combat window covers the lower half. The bow was also edge-on from behind. The hunt now frames both weapons (scale 1.2, turned -40 degrees so the bow is seen from its side);
  the arrow frame is taken only from the settled, unblocked framed camera and logs `camera_scale camera_pivot_m heading_offset player_box player_above_window combat_window_top`.
- *The field of view.* Lifestone drew the play view at 60 degrees vertical. Retail draws its 90-degree `Render.FieldOfView` preference over (aspect - 0.1) as a vertical angle: 73 degrees by about 89 at
  800x600 (`SmartBox::RenderNormalMode` 11.6096 0x00453B40, `SmartBox::SetDefaultFov` 0x00451EA0). The play camera now uses it (`Camera/RetailLens.cs`, `Main.ApplyRetailFov`; `--fov` given wins, `--fov=60`
  restores the old view; explore keeps 60), so the same camera shows more and puts the player higher. The aspect is the viewport's width over its height (`RenderDevice::ComputeAspectForViewport`
  0x0054FD60, closing `TODO(cam-aspect-unverified)`). `TODO(cam-fov-resize)` stays open: the angle is computed once at start. If a frame-time row fails in the live gate, compare one run with `--fov=60` before calling it contention.
- *The look capture (P3).* The humans' melee and bow stances turn the root part about -85 and +83 degrees, so the fixed 130-degree offset showed the archer from the side or back. The capture's aim is now
  130 degrees plus the body's turn in the stance (`AvatarFacing`), the pivot is lowered to 1.15 m so the hands are not behind the spell window, and each frame logs `body_turn_deg body_baseline_deg aim_degrees`.
  `--play-look-view=close|hunt` is registered. Offline, the frames show the real Mace, Shortbow and orb from the front three-quarter; **the live magic stance may differ** (the lead reads `body_turn_deg` on BSD Mage).
  The live proof of both (the missile `arrow` line, BSD Mage's `body_turn_deg`) is the lead's gate.

## 2. Out of this pass

- **The hero video** (M2's and M3's had one): its own step after item 4.2, from 4.2's lead (D29).
- 4.2's cluster reviews, judges, audit fixes and doc pass stay 4.2's (§4.9).
