# M6 7 AS handoff: Lifestone Recall and Tie in the coin order

Done
- `ScrivenerLearnPlan`: new `TravelLines` (Lifestone Recall, then Lifestone Tie); `LinesFor` places them right after the protection lines (so right after the lines `first_scrolls` names) and before the drains; `IsBuffLine` includes `Travel`, so they are read at the 0.75 buff bar on the skill the bootstrap reaches, and a short purse calls a trip by the same budget rule as the other buff lines. Until the first scrolls are read they show as `Saving`, by name. Portal Recall and the portal ties stay unbought.
- `ResupplyStation.Learn.cs` `TieAfterLearn` (end of `ReadScrolls`): Recall and Tie both in the book, `JourneyFacts.LifestoneTied` not true, body in Holtburg's landblock: runs the existing `BindStation` embedded (walk, Use, Lifestone Tie with its fizzle retries by name). Check after: `LifestoneTied`; a failure is one logged line (`TODO(m6-tie-after-learn)`), not retried here; the recall's 0x049E refusal binds again (F14).
- Existing travel rules (rung change, buy trip, town trip, quest) already read `Spellbook.Knows(1635)`: nothing rebuilt.
- Item skill under the bar: the need is NoLevel or SkillShort with the numbers logged ("learn: plan: ...: no-level"), nothing is bought, the book lacks Recall and every way home stays on foot or by portals until the skill (levels, Item buffs) reaches 0.75 on power 100 (about 136).

Tests: +3 (BuffBootstrapPlanTests), two ProtectivePlanTests expectations updated for the new lines. Count 13,345.
Gotchas: Winnie's Item skill just after 16 is about 95 with the bootstrap, chance 0.46: Recall waits for a later level. The tie step has no unit test (needs a live station); the lead's rehearsal covers it.
Open TODO: m6-tie-after-learn (named stop, not retried in the same trip).
