# Handoff: M6 item 6, slice FXU (the menu usable on screen; the play-checks clean)

Branch `m6/6-fxu`, cut from `m6/6` after MD2's merge (f0b13b66).

## Done

- **A mouse click on a menu row chooses it** (closes `m6-menu-row-click`): `AutomationMenu(manager, parent)`; `GamePlayScreen` passes its root, so the window (0x7F000300) is a child of the shell's root (0x10000495) as the tools panel's is. As a manager root (z level 20) it lost every hit test to the shell's root (z level 1; a lower z level is in front).
  `menu.json`: the window's z level 20 -> 21, just behind the tools panel (20) and in front of the main chat (600) and the SmartBox (9999), so the tools panel's Jobs button stays clickable where the two overlap. The host's Jobs press opens the menu and closes the tools panel (F7 brings it back, its Jobs button closes the menu); MD2's `BringToFront` in the host is gone.
- **The quest rows show their xp** (closes `m6-menu-row-xp-clipped`): `FormatRow` puts the xp first (`xp 12,000 | do a quest: ...`, `> xp ? | level up | current`, `(refused) xp 900 | Bone Dust | not-due:...`); an open row cuts its label, never the xp or `| current`. `AutomationMenu.XpText`.
- **No ERROR under the reconnect play-check**: the check holds the kit's wand before `/auto` (`HoldCasterAsync`), and `StandInRelogin` (whose stand-in world is made anew) creates and wields it again before it reports the login's success (check, act, check; a refusal fails the login by name). The audit's ERROR is unchanged. The wand is put away at the end.
- **6-M9's judge live**: `AutopilotJobSource.BuildInput()` (null while the level is 0; the rows use the same input); `AutopilotHost.Setup` sets `logic.MenuInput ??= jobs.BuildInput`.
- Play-checks (`PlayChecks.Menu.cs`): the errand and stop rows are chosen by a mouse click only (no `ChooseRow` fallback); `menu-row-click` and `menu-quest-row-shows-its-xp` stay report-only and log `pass` or `FAIL`.

## Gotchas

- The rig's z levels of the shell root's children: SmartBox 9999, main chat 600, the Lifestone windows 20 (the menu 21), the retail windows 0 (in front of all of them).
- Frame `11-shell-automation-menu`: every quest row starts with its xp; a long row is cut on screen by the list's width (about 58 characters at 800x600) before the 68-character limit, so a long current row's `| current` can fall off the right edge (the `>` stays).
- A judged check's condition was not widened: the caster facts and the tools panel's state are in the details only.

## Tests

- New: `AutomationMenuTests` +2 (`A_long_quest_name_is_cut_but_never_its_experience_or_the_current_mark`, `A_mouse_click_on_a_row_chooses_it_after_a_click_on_the_shell`), `AutopilotJobSourceTests` +2; four `AutomationMenuTests` row texts updated. Default count 12,820 (slice-check; 12,816 before + 4), exhaustive 967.

## Open TODO names

- None new; `m6-menu-row-click` and `m6-menu-row-xp-clipped` closed.
