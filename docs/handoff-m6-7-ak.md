# M6 7 AK handoff

**Done.** The ladder (outside through 15, Lodge from 16, commit 989d239e). The scrivener catalog: 12 self buff lines and Impenetrability and Bludgeon Bane, I to IV, with the Scrivener of Item Magic added. `plan_buff_chance` 0.75 in the ladder beside `plan_cast_chance` 0.95, and `first_scrolls`. `Mage/BuffReach.cs` (the bootstrap on live numbers, and the push). The plan learns the buff lines on the skill the bootstrap reaches, each bootstrap step its own scroll; the first scrolls lead, paid from the whole purse, and every other line saves (`saving`). BuffCycle: 0.75 for the climber on the live skill, her order, a bootstrap sweep before the rest, a report line per step and per pass. The spend: the push is paid from the magic share, the health floor unchanged. Component rows on the outskirts and the Lodge. Report-only `armor_buffs= damage_factor=` on the `[climb]` verdict.

**Next.** The lead proves it live: Throwaway or the climb's first pass. Read `buff: bootstrap:` and `buff: pass:`.

**Gotchas.**
- A full pass is about 650 mana on a pool of 154 (doc notes). BuffCycle fails the pass when its mana wait runs out. This is the first thing to watch.
- She stops at 16 with `rung-moved:lodge` and no trip between; the learn trip is the next run's cycle start, on the Lodge's ground, whose home is Holtburg. Not run live.
- Other rungs after the Lodge do not stock the new components: a missing one drops the level by name.
- ScrollKeepRule still judges at 0.95, so it may sell a looted buff scroll she could use.
- The vitae path is read by the same code (ACE's order) but has no test of its own.
- The bootstrap sweep runs in a start's pass; a rebuff pass reads the skill the buffs still hold.

**Tests.** 9 new in `BuffBootstrapPlanTests`, 8 in `RungGateRuleTests`. LadderLineTests' scripted move-on now proves the gate holds her; no scripted run crosses the gate to the Lodge with every condition met. Pins moved: LadderTests, LadderLineTests, ClimberHealthRuleTests, TrophyQuestTests, XpSpendRuleTests, ScrivenerCatalogTests, ProtectivePlanTests, HuntingGroundDatTests. None deleted.

**Open TODOs.** m6-self-buff-lines-v-vi, m6-armor-buffs-live, m6-buff-pass-mana, m6-buff-components-early. The worked table is in `cache/m6/7/doc-notes-ak.md`.

**The Lodge gate (added last).** `rung_gate` in the ladder and `Mage/RungGateRule.cs`: she stays on the outskirts until every condition holds, each logged as `ladder: gate lodge:`. The run's check and the mid-fight move-on read it, once every 30 s while held; the host reads only the skills and the book before a run. A run built on the Lodge does not read the gate again: buffs that drop after the check do not send her back.
