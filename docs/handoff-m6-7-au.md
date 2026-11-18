# M6 7 AU handoff: the foci, the note cash, the potions

**Done.**
1. Foci: `scriveners.json` `foci` section, `ScrivenerCatalog.Foci`, `Mage/FociPlan.cs`, `LogisticsCause.Foci` (climber only, before the learn trips), `PlanFoci` in the resupply (first in the list, whole purse, check before and after, `SaleMemory.FociRefused`). Real price 25,000 each (SellPrice 50 x Value 500).
2. Short purse or full load: no trip, named `fallback=full-formula-components-two-passes` (AT's rule).
3. Purse: `min_note` 250,000 on the outskirts, Burrow and Lodge (the climber keeps coin; a note cost 15%).
4. The 100,000 note: `NextNote` now reads all ten notes (it saw only the profile's 10,000); a scrivener takes a note at face.
5. Potions: an unsellable Health or Mana Draught (and the Potions) is kept, not dropped; the mana rule drinks a Mana Draught when no Mana Potion is held.

**Next.** Live: the first foci buy at a trip with a note held (watch `foci: check before/after`, `purse: changed Trade Note (100,000)`), her load line, the "needs Blue Taper" lines stopping.

**Gotchas.** The Burrow gate's conditions gate the foci (Item Enchantment trained, the scrolls learned). `AutopilotScenario`'s floor-only set is built once (`TODO(m6-floor-only-live)`).

**Tests.** 13,365 by slice-check (+7): `FociPlanTests` (4), the note-cash and draught cases (3), a rewritten outskirts note test; two pins moved (min_note, catalog sources).

**Open TODOs.** m6-foci-refused, m6-floor-only-live. Doc notes: cache/m6/7/doc-notes-au.md.
