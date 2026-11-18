# Handoff, M6 item 0 slice a3a (the fresh climber)

Done
- `MageTemplate`: Winnie's creation plan (`PlanClimberCreation`, `ClimberCreationIntent`, `ClimberCheck`), Alchemy 38; credits read from the dats (`ClimberCreateScenario.PlanFromDats`): War Magic spec 28, Alchemy 6, Life Magic trained 12, Mana Conversion 6 = 52; `TODO(m6-climber-creation-credits)` names Life spec (20), Creature (8), Item (8).
- `--scenario=climber-create --fresh-character` (HeadlessOptions, Program, OptionCatalog.Headless): the D44/D45 re-create of Winnie, no grants, no placing, purge after the template check.
- `PathwardenStation` (kind `pathwarden`, plan `climber`, `--journey=climber`) reading quests/pathwarden.json (now with Thorolf's and the chest's cell and points).
- Winnie in `TestAccounts`, the server mod's `CharPurgeRules` (Graduated, Recreated) and the stand-in.
- Tests: FreshClimberTests (7), FreshClimberOptionsTests (6), loopback AcademyChestLiveTests (not run).

Next
- a3b: the Academy's own run (stations inside the Academy) replaces `--scenario=academy-exit --character=Winnie`; then `academy_done` reads it.
- The lead: `setup-server.ps1 -ModOnly` (the purge rule for Winnie) before the first live `climber-create`; run AcademyChestLiveTests.
- Doc pass: cache/m6/0/doc-notes-a3.md into docs/session.md sections 8 and 42 and CLAUDE.md.

Gotchas
- The key is used with `UseWithTarget` (a heal-shaped handle): its outcome is only logged; the check is the chest opening (`OpenContainer`, the viewed container).
- `TODO(m6-pathwarden-use-range)`: 1.5 m is the chests station's guess; Thorolf's talk radius is read from the first live run.
- `TODO(m6-pathwarden-timers-save)`: the headless journey host has no timers store; the completion is in memory (`PathwardenRun.Timers`) and logged.
- The talk step is checked by `quest has PathwardenFound1111` (read-only verb, not counted in granted).
- The journey's known-state may place Winnie in Holtburg after the exit (recovery_place=1, outside granted and the budget).

Open TODO names: m6-climber-creation-credits, m6-pathwarden-use-range, m6-pathwarden-timers-save, m6-pathwarden-talk-reply, m6-pathwarden-chest-open-check.
