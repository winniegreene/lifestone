# M6 7 AN handoff

**Done, piece 1.** The ladder's `vitals` data with its source sentence. Fight: drain at or below 50% on every ground, from the fullest target in reach, level by best heal; an attacker out of the band counts, and an outdoor creature's health comes from its appraisal. The mana loop (`FightScenario.ManaLoop.cs`, `Vitals/ManaLoopRule.cs`): mana under 30 and stamina at least half presses; under half, Drain Stamina from the creature holding the most, read live by appraisal, where one cast gives at least 30. Recharge: she lies down (the walker's stance first, the server's motion checked), `rest: stretch:` and `rest: cycle:` log posture, rates and real seconds. The keeper lies down too, stands with a creature within 24 m, and stands at the first hit. The Lodge: `pull_full_percent` 100, a named check before the entry portal (`enter_check:`, `enter_recharge:`) and before every pull; a creature in sight makes a standing rest, never a pull; only an attacker is fought. The outskirts have a `fight` section: today's line as data (mana 20%, stamina 30%, top 80%), `rest_s_per_kill=` on the verdict. Catalog: Drain Health I and II, Drain Stamina I, Heal Self I and II, Regeneration and Rejuvenation I to IV; Alder Talisman and Damiana in stock. Coin order: first scrolls and bootstrap, drains and heal, regeneration lines, the rest.

**Done, piece 2.** A climber's pass never fails on mana. It waits lying down, presses and drinks, to `buff_pass_wait_seconds` (900 s), then ends done with what stands, names what is missing, and those lines are not due for another 900 s. `start_only_lines` in the ladder, default empty: all lines kept up. `buff: pass:` adds seconds, potions, rests, presses, stands, levels.

**Next.** The lead proves it live: read `rest: stretch:`, `rest: cycle:`, `enter_check:`, `buff: pass:`, `mana loop:`.

**Gotchas.**
- The mod guard and the climb are untouched. Nothing here ran live.
- The stamina drain's target choice has rule tests only; no scripted run has an appraisal with stamina in it.
- A tied portal recall into the Lodge skips the door check; the first pull's check still holds her.
- A stop at the door (`enter-recharge:not-full`, 600 s) fails the station by name.
- Under the bar with no way to rest she holds; the fight's no-kill watchdog is the last word there.
- The gate crossing is tested through `RungGateRule` and `MoveOnDue`, not a whole scripted climb.
- `KeepOnePress` is now covered by the loop at her bolts' sizes; it stays for a dearer bolt.

**Tests.** 13,272 by slice-check. New: `RechargeRulesTests.cs` (four classes), `BuffPassMathTests.cs`. Pins moved: ProtectivePlanTests, ScrivenerCatalogTests, ScrivenerLearnTests (a recovery line now calls a trip when paid), VitalsKeeperTests, FightPullRestTests, F8ClimbProbeTests, HuntingGroundDatTests and ResupplyStationTests (two stock lines), PullRestRuleTests (the outskirts case moved from "no rule" to the rule row). None deleted.

**Open TODOs.** m6-rest-lie-echo, m6-target-stamina-unread, m6-buff-pass-mana (closed by the limit, the number still to read live), m6-keeper-attacker-reply (the keeper stands at a hit; the guards answer).
