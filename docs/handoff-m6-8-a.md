# M6 item 8 slice A handoff

## Done

- `combine` and `apply` are real: `QuestRun.RecipeStep` and `RecipeUse` (one runner), the loader's field rules in `QuestStep` and `QuestCatalog.Step`.
- The reader: `Session/Quests/RecipeOutcome.cs` (`RecipeOutcomes.Read`, pure). Items first, chat second; lines compared whole.
- `IActionApi.UseRecipe` with `RecipeTracker`: the kit's tracker gave a false failure for a kept source and for a dialog's early UseDone. The handle says how the server ended the use, never what the recipe made.
- `m6-quest-pick-up-container`: `QuestRun.PickUpFromContainer`, closed on every way out.

## File format

`item`, `target`, `expect.item`, `expect.reply` required; `count` optional; `fail_item` and `fail_reply` optional on a combine, `fail_reply` required on an apply; a `fail_item` needs a `fail_reply`. `expect.reply` and `fail_reply` are the recipe row's whole lines. No place, no stamp.

## Next (slice B)

The Atlan and fetch quest files use these fields. The bars-to-ingot recipe (789) rolls, so its step names `fail_item` and `fail_reply`; the lost roll stops the step by name (`combine:failed-roll`) and states what was lost.

## Gotchas

- A step of one weenie class checks for twice its count of different objects before the first use (`combine:short`). A run that resumes partway needs a smaller count or the `steps=` range.
- A recipe that makes the same item on both branches (6027, 6068): the file still names `fail_item` and `fail_reply` as the row has them; only a fail line of its own text reads as a failed roll.
- `HeldItems` reads the main pack, the side packs' contents and what is worn; `CountHeld` still reads only the main pack, so a pick-up's arrival check is the main pack's (the PickUp sends to the main pack).
- `QuestRig.Acts` holds the mode changes and the recipe uses in send order; `QuestRig.Events` was left without the mode changes because a kill test asserts it empty.
- The reader's facts are from ACE's source, not captured live: the first real recipe is slice B's loopback.

## Tests

14,132 passed in the default run (6 skipped), the floor given 14,127. Two theory rows of the old "not yet (item 8)" test were replaced by the field-rule theory that took their place; `combine` and `apply` load now, so the behaviour they proved no longer exists.

## Open TODO names

`m6-quest-chest-key`: a locked chest on a pick-up stops by name; the lead places it.
