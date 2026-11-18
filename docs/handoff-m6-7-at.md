# M6 7 AT handoff: buffs must not lapse for want of a component; sell in time

**Done.**
1. **Component trip.** A buff pass that skips a level for want of a component names it (`ComponentNeeds`, `buff: component:` line); the counters raise that stock line's keep and trip line to two passes of the lines that burn it, so the next check calls `stock:<component>` once and the trip buys it. Coin short: the existing held-back rule names the line; the buy list keeps the plan's order. A noted component no stock line carries is logged by the resupply (`TODO(m6-component-unstocked)`).
2. **Sale trip.** `LogisticsCause.Sale`, trigger `sale`: the pack's sellable loot is worth `loot.sale_trip_value` (data, default 15,000 Value) while a stock line is held back by the coin or a scroll is wanted and unpaid. Last in the check order, so every stock and learn trip outranks it. Value is read from the pack (`CarriedSaleValue`, the audit's sale-offer filter).
3. **Learn batch.** A learn trip goes when the purse pays the first scroll it would buy and the one after it in the plan's order, or holds `purse.learn_trip_coin` (data, default 15,000).
4. **Rest blocker.** `RestBlockerRule`: an attackable creature within the aggro reach (the ground pass only, `shoot-first` argument) cuts the buff pass once per creature (`buff: pass: cut at`); the fight that follows shoots it, she rests after. Beyond the reach, inside 24 m: the wait stands as before.

**Not done (named).** The sale does not re-plan the same trip's buys: a trip's budget is read before its sale. The sale trip sells, the next check's stock or learn trip buys. `TODO(m6-trip-replan-after-sale)`. The vitals keeper's own rest (fight) keeps its wait line; only the pass wait cuts.

**Gotchas.** `ComponentNeeds.Shared` and `LearnHolds.Shared` are session-wide: tests give their own. The per-pass want counts only lines that skipped; a line with enough does not add to it, so the want grows as lines skip.

**Tests.** 13,358 by slice-check. New: `LogisticsCountersTests.Batch` (8), `ComponentNeedsTests` (2), `RestBlockerRuleTests` (2), a learn-batch case in `ScrivenerLearnTests`. Moved: the M6_1_d hold test sets its hold directly (a learn trip now needs the batch coin); the two ground-pass spec pins; M6_7_X's 417-coin case. `Pack` in `LogisticsCountersTests` takes item values.

**Open TODOs.** m6-trip-replan-after-sale, m6-component-unstocked, m6-pass-cut-live (the cut and the sale trip are unproven live).
