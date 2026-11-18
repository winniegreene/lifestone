# M6 7 AO handoff

**Done, 1: the Lodge gate.** A line at level n is learned when the book holds level n or any higher one (`RungGateRule.BookKnows`): the plan skips level I, so the gate read "Focus Self I: NOT MET" with II and III learned. The line is found by the profile's name or its spells' name (`LineOf`), so "Bludgeon Protection Self" and "Bludgeoning Protection Self" are one line; a name that is neither throws `TODO(rung-gate-line)`.

**Done, 2: lying down.** The motion was never sent: `CreatureAwakeNear` counted the Scrivener of Item Magic (0x7A9B301B, a town NPC) as an awake creature within 24 m, so she rested standing at 0.63 stamina a second. Now only an attackable creature keeps her up (`WorldObjectTable.NearestAttackableCreature`; the keeper, the buff pass's wait and the Lodge's `enter_recharge`). Check after: `Vitals/RestEcho.cs` reads the server's motion: `vitals: check after:`, `failed=no-echo` within 3 s (one try a keeper's pass), `failed=posture-lost` (twice, then on her feet). `vitals: rest ended: posture lie (the server's motion 0x0014) ... ACE 1.8 before Rejuvenation` names the posture the server showed, not the one asked. The pass: `buff: pass: check after:`, `buff: pass: rest: posture ...`; peace is its check before. A hit reads against the last health seen, not the rest's start.

**Done, 3: the pass after the book.** On the outskirts a trip ends in the ground, so the Home arrow never ran, and one enchantment up read as buffed: the audit cannot judge lines while the armour memory is unseeded, and only a pass seeds it. `Mage/LearnedPassRule.cs`: a climber's run owes a whole start pass (bootstrap included) on its first run and whenever the book's size changed since the last pass; `ladder: pass: check before:` and `check after:`. "At home": the outskirts' trip ends at the first cell's point in Holtburg; the pass runs where she stands.

**Two traps found on the way, fixed because the pass could not run without them.** (a) The buff state's watchdog was 300 s against AN's 900 s wait: `AutopilotWatchdogs.ClimberPassSeconds` (limit + 300) for Buff and Upkeep on the level up job. (b) AN's "never a fail" stopped at the cycle: `BuffScenario.Judge` and `BuffLegVerdict.Compose` failed the station on any missed line or `active < wanted`: `BuffResult.EndsWithWhatStands`, read by `BuffLegVerdict.EndsWithWhatStands` for the autopilot's own station only.

**Next.** The lead proves it live: read `ladder: pass:`, `buff: bootstrap:`, `buff: pass:`, `vitals: check after:`, `rest ended: posture lie`. This climb runs the old tree; none of this reaches it.

**Gotchas.**
- The book-changed arrow is the first run's arrow: the scripted test proves the first run on the ground and `LearnedPassRule` proves the change; the rig's trip cannot end in the ground (no dats on the way back).
- The rule reads the book's size, so a non-buff scroll (a drain) also costs one pass; lines already standing are not recast.
- A trip due straight after a pass spends that pass's walk; the audit's trip still comes first at a cycle's start.
- The fight's pull rest keeps AN's warning at its stretch's end; it does not cut the rest.
- `m6-keeper-attacker-reply` stays: the keeper stands at a hit and the guards answer.

**Tests.** 18 new, none deleted. `C2VitalsRig` now echoes postures through the reducer. The known-state arrow was disabled by hand and its scripted test failed. The other new tests were not run against the old code: most call members that did not exist, so they could not compile there; the NPC test's reason is the live line, and its creature stands within the 24 m by construction.

**Open TODOs.** m6-rest-lie-echo (now a named failure in the keeper, the pass and the entry; first live read owed), rung-gate-line (a guard), m6-keeper-attacker-reply.
