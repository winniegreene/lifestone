# Handoff M6 7 AF (gauntlets)
Done: GearCoverage (Mage/GearFloorCoverage.cs); PathwardenStation.CoverageClosed (coverage rule on the burden gate); GearFloor gauntlets line (Kind Gauntlets, HandsCovered, HandsCoveredBy, IsGauntletWare); 3 scripted tests in GearFloorTests; 2 pinned expectations updated (IsFloorWare(56) stays false; StateOf HandsCovered false with the robe).
Next (lead): add Renald's Leather Gauntlets 56 at 1,485 to holtburg-outskirts.json (vendors, Renald wcid 718, sells); the equip pass (ResupplyStation.Gear.cs) wears a bought pair through the judge: unproven live, check the after-line `equip pass: wear 'Leather Gauntlets'`; the live proof of the chest pair is a fresh climb's pathwarden station: `pick-up: ... taken over the line: the coverage rule`.
Gotchas: the gate is in PathwardenStation.cs (outside the slice list). The baseline json is stale (12,823); the real floor is the lead's. The outskirts vendor line also needs the loader (GroundCatalog) to accept IsGauntletWare.
Open TODO names: m6-no-gauntlet-vendor, m6-pathwarden-gauntlet-spells.
Facts and numbers: cache/m6/7/doc-notes-af.md.
