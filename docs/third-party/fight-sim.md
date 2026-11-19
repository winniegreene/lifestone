# Third-party sources: the fight simulator

The fight simulator (`tools/fight-sim`, [docs/fight-sim.md](../fight-sim.md)) runs ACE's own combat code offline. It
links the pinned ACE build's assemblies at build time and loads them from `server/runtime/ace` at run time, as the
server mod and the loot simulator do. Lifestone is AGPL-3.0, and the source below is compatible with that.

**What is ported:** no ACE source is copied. Every number the tool prints is the return of a call into ACE's public
functions on ACE's own objects. Where ACE runs a sequence that needs a live world (a monster's tick, a projectile's
flight and strike, a generator's spawn step, a player's cast chain), the tool re-expresses the sequence around those
calls. Those sequences are listed under "Sequences followed", each with its port header.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d412459ff619a12cc18f82bcf72c1c2a8 (`vendor-eval/ACE`, the commit `server/versions.json` pins) | The assemblies the tool calls; the sequences followed (below). |
| ACE's world database | as ACE-World-16PY-Patches publishes it | the pinned world SQL the backend installs | Read through ACE's own queries: weenies, placements, generators, encounters, portals. Nothing derived from it is committed. |
| Lifestone, the loot simulator (`tools/loot-sim` on `m5/loot-sim`) | AGPL-3.0 | | The offline boot (`AceOffline.Boot`, the in-memory GUID allocator). |

## Sequences followed

| Lifestone file | Follows (ACEmulator/ACE@95ef426d, AGPL-3.0, under `Source/ACE.Server/`) | What is followed, and what is ACE's own |
|---|---|---|
| `tools/fight-sim/Blows.cs` | `WorldObjects/Monster_Tick.cs` `Monster_Tick`; `Monster_Combat.cs` `AttackReady`, `NextMagicAttackTime`, `Attack`; `Monster_Melee.cs` `MeleeAttack`, `DoSwingMotion`; `Monster_Magic.cs` `MagicAttack`, `UseMana`; `Monster_Missile.cs` `LaunchMissile` | The order and timing of a monster's attacks. ACE's own: `GetNextAttackType`, `GetCombatManeuver`, `MotionTable.GetAnimationLength` and `GetAttackFrames`, `DamageEvent.CalculateDamage`, `PreCastMotion`, `GetPostCastTime`, `CalculateManaUsage`, `TryResistSpell`, `TryCastSpell`, `VitalHeartBeat`. |
| `tools/fight-sim/Strikes.cs` | `WorldObjects/SpellProjectile.cs` `OnCollideObject`, `DamageTarget` (the ratings step of the health branch); `Player_Magic.cs` `GetCastingPreCheckStatus`, `DoWindupGestures`, `DoCastGesture`, `FinishCast` | A projectile's strike after `CalculateDamage`, and a cast's parts. ACE's own: `SpellProjectile.CalculateDamage`, the rating functions, `SkillCheck.GetMagicSkillChance`, `MagicDefenseCheck`, `SpellFormula.GetCastTime`, `MotionTable.GetAnimationLength`. |
| `tools/fight-sim/Census.cs` | `WorldObjects/WorldObject_Generators.cs` `Generator_Generate` (the first fill's loop), `AddGeneratorLinks`; `Entity/GeneratorProfile.cs` `Delay`; `Entity/Landblock.cs` `SpawnEncounters`; `WorldObjects/Monster_Awareness.cs` `AlertFriendly`, `AuralAwarenessRangeSq` | What a generator's first fill makes, a linked child's slot and delay, and which neighbours a woken creature alerts. ACE's own: `SelectAProfile`, `GenStopSelectProfileConditions`. |
| `tools/fight-sim/Economy.cs` | `WorldObjects/WorldObject_Magic.cs` `HandleCastSpell_Transfer`, `HandleCastSpell_Boost` (read, to choose the state each cast starts from) | Nothing re-expressed: the casts are ACE's `TryCastSpell`. |
| `tools/fight-sim/AceOffline.cs` | `Managers/WorldManager.cs` `DoPlayerEnterWorld` (the player's restore), the static constructor (the physics engine object); `Managers/GuidManager.cs` (the dynamic allocator's fields) | The objects a login builds, without a world. ACE's own: the `Player` constructor, `ShardDatabase`'s reads, `BiotaConverter`. |
| `tools/fight-sim/Commands.cs` | `WorldObjects/Creature_Death.cs` (the experience of a kill), `Player_Xp.cs` `EarnXP` | The experience a kill pays. ACE's own: `GetXPAndLuminanceModifier`, `PropertyManager.GetDouble`. |
| `tools/fight-sim/InFight.cs` | `WorldObjects/Player_Magic.cs` `CalculateManaUsage` (a fizzle's 5 mana, the refusal of a cast whose rolled cost is over the mana held); `Player_Combat.cs` (the stamina an attack thrown at her costs: an evade, with Endurance's chance to spare it, and a hit); `Creature_Vitals.cs` `VitalHeartBeat` | Her side of a fight: the order of her casts, their costs and their effects, the blows and the heartbeats between them. ACE's own: `CalculateManaUsage`, `TryCastSpell` (heals, drains, presses), `VitalHeartBeat`, `MagicDefenseCheck`, `GetResistanceMod`, and every blow and bolt from `Blows.cs` and `Strikes.cs`. The choice of each cast is not ACE's: it is the Lifestone client's own rules (`InFightRules.cs`, docs/fight-sim.md "With her heals"). |
| `tools/fight-sim/BestiaryWorld.cs`, `BestiaryTables.cs`, `BestiaryRows.cs` (the `bestiary` command) | outside `ACE.Server`: `Source/ACE.Database/Models/World/` (the model classes the reads go through), `Source/ACE.Entity/Enum/` and `Enum/Properties/` (the enum members a column names, by name and number) | Nothing re-expressed: the world database is read through ACE's `WorldDbContext`, and each property's name and number is checked against the ACE build at every run. The picks in `BestiaryRows.cs` follow Lifestone's own first SELECTs (quoted in the tables' `sources` headers), not ACE code. |
