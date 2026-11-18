# Handoff: M6 item 7 slice AH (gauntlets on every rung, the replace step of a death)

Branch m6/7-ah. Doc notes: cache/m6/7/doc-notes-ah.md (the lead's copy; the facts and the D row text are there).

## Done
1. Data: Renald sells Leather Gauntlets 56 (1,485) in holtburg-outskirts, tusker-burrow, -lodge, -cave, -temple-20-40, -abode, -quarters; `GroundCatalog` accepts `GearFloor.IsGauntletWare` wares (IsFloorWare(56) stays false).
2. Strength: no wield requirement on the pair; ACE refuses a buy only past 3 x capacity; the StrengthFloor reads the burden at the next spend. No code change.
3. Step `replace` in the death handler (new `DeathHandler.Replace.cs`, `Mage/DeathReplace.cs`, `ResupplyStation.Replace.cs`): between rewear and rebuff/route; `replace=none|<kinds>|short|failed` at the end of `Fields`/`UnmeasuredFields`; one try per death; never a stop.
4. rearm=corpse buys the floor's Wand first (`rearm=bought`), buffs, then the trip; falls back by name.
5. Take-back: robe/gauntlets whose slot the replacement fills stay on the corpse (`take-back: left '<name>': replaced at the vendor`), not skipped, not lost, `corpse_recovered` counts them as accounted for.
6. Ten scripted tests in DeathHandlerTests.cs (none; gauntlets bought, worn before the rebuff and the duplicate left; short purse; failed walk and one try; no journey context; rearm=corpse by the floor wand; fallback; the leave rule; the rung pins; the plan's one caster).

## Next (the lead)
- `replace=` on the gate's and the climb's verdict line (AutopilotScenario.Ladder.cs field list): TODO(m6-replace-verdict-line).
- Live proof of the step: TODO(m6-replace-live) (a real death at level 35 or a staged one; the Renald walk itself has dat tests).

## Gotchas
- The rig's scripted pieces carry the names of GearKit's list, not their locations: the piece at HandWear is named "Leather Boots" in the log.
- A vendor whose walk fails is `vendors_skipped=1` and the trip still ends Succeeded; the handler's check after (what is worn, against the plan) is what says `failed`.
- A caster on the corpse is taken back as the spare (two casters always), never left.
- The test helper `VendorRig.Arrive` takes `validLocations` and `priority`, `ScriptedShop.Shop.Wearables` gives a bought piece its slot data, `RunTravel.FailVendorWalks` fails vendor walks.

Open TODO names: m6-replace-verdict-line, m6-replace-live. Closed: m6-no-gauntlet-vendor (every ladder rung's ground).
