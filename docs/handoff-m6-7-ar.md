# M6 7 AR handoff: the Burrow rung, money and supplies at her level

**Done, part 1.** The Burrow is a rung again: outskirts 5-15, Burrow 16, Lodge 17-21. A band is levels and the Lodge's gate is not, so she stays on the Burrow past 16 until that gate opens. `rung_gate` became `rung_gates`, climbing order; `RungGateRule.Walk` chains them: a shut gate holds her on the rung before it. The Burrow's own gate: Focus II, Willpower II, Life Mastery III, Armor Self IV, Bludgeoning Protection IV, learned and up. `tusker-burrow.json` at 16: full bars (`pull_full_percent` 100), Flame Bolt III to 21, the Lodge's components, stock and shops. General keep-out: the profile's `hunt` section (`keep_out` cells, `keep_out_under_level`, `pulls`), any ground's: the pick, the tour and the schedule leave those cells alone; an attacker from there is still fought. The Burrow keeps the south hall (0x57480105 to 0x57480114) out under level 30; the Lodge's "first three only" is the same list.

**Done, part 2.** Outskirts: `spell_plan` to 21, Flame Bolt III's components, `max_health` on each outdoor creature (drain and finishing fire from the first shot), Copper and Silver keep 3, trip at none, Mana Potion `trip_under` 0, purse coin 4,500. Monyra added on the outskirts, Burrow and Lodge. Loot: `loot.radius_m` (new), `load_line_percent` up to 200, floor 10 a burden unit: outskirts radius 60, line 125; Burrow and Lodge radius 25, line 110. Health Draught or Potion of Healing drunk at the emergency line before any heal (`emergency: potion:`).

**Coin.** Expect about 60,000 an hour, against 2,000. Why: 12 of 457 corpses were opened; those 12 paid 39,924. Figures in the outskirts' `loot` source. Unmeasured at this rule.

**Next.** The lead reads live: `ladder: gate burrow:`, `hunt: keep-out`, coin on the `[ladder]` line, `emergency: potion:`, a trip's sold=.

**Gotchas.**
- The tusk is not taken at 16: the walk in wakes the hall whole, so a pull needs a stand point and one step that shoots from it. The Lodge has the same need. A due tusk logs `TODO(m6-tusk-pull)` and the hunt goes on. Gate A's tusk stays open.
- The Burrow's exit portal (0x57480100) is under the hall and she has no Lifestone Recall: a trip home walks past the hall on foot. Unwalked at 16.
- A note is bought at coin above 16,000 (floor 4,500 + 11,500): 15% lost if she then sells it for a scroll. Read the order on a rich trip.
- The Lodge keeps 400 Prismatic Tapers; she holds about 390 and they are half her pack. Not changed.
- The drink has no scripted test: the rig's pack has no Health Draught. Live proof owed.
- The outskirts' `road_hitter` and the Burrow's stay the Male Tusker, so the health want is 72 from 5 to the Lodge.

**Tests.** 13,342 by slice-check. New: `GroundTourKeepOutTests`, `MoneyAndSuppliesTests`, gate chain cases in `RungGateRuleTests`. `FightOptions.GroundForTest` (internal) lets `FightPullRestTests` read the floors without the bar. Pins moved: LadderTests, LadderLineTests, TrophyQuestTests, BuffPassMathTests, ClimberHealthRuleTests, XpSpendRuleTests, ProtectivePlanTests, ScrivenerLearnTests, ResupplyStationTests.Notes, HuntingGroundDatTests, GearFloorTests, F8ClimbProbeTests, PullRestRuleTests, RechargeRulesTests, LogisticsCountersTests.Purse, DeathHandlerTests (rig). `GearKitTests`' throw half is gone: Copper is an outskirts line now. None deleted.

**Open TODOs.** m6-tusk-pull, m6-burrow-16-live, m6-burrow-exit-under-hall, m6-emergency-potion-live.
