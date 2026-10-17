# Third-party sources: effects and animation track

This track owns `src/Lifestone.Data/{Animation,Effects,Magic}/` and `tests/Lifestone.Data.Tests/{Animation,Effects,Magic}/`.
Lifestone is AGPL-3.0; every source below is compatible with that. Nothing derived from the user's dats is committed.

## Sources

| Source | Pin | Licence | Used for |
|---|---|---|---|
| Retail client decompile, Mag-nus/acclient-AI-RE (`2013-09 11.4186/acclient-src`, PDB-named) | 11.4186; 11.6096 for one disassembly check | Turbine IP; MIT label on the repo | Behaviour reference only. Functions are cited by name and re-expressed; no decompiled code is in the repo. |
| User's `client/acclient.exe` | 11.6096 (MD5 `116d9a66…`) | user-owned | Read-only byte dump of `ParticleEmitterInfo::ShouldEmitParticle` (VA 0x517F50), whose per-metre test the decompiler lost. Not run. |
| ACEmulator/ACE | v1.78.4816, commit 95ef426d | AGPL-3.0 | Server-side rules (cast sequence, effect intensities, projectile spawn/velocity, spell words), cross-checks of the client ports, and the test oracles. |
| ACE world database (ACEmulator/ACE-World-16PY-Patches) | release `ACE-World-Database-v0.9.296.sql` | AGPL-3.0 | `src/Lifestone.Data/Magic/Data/ace-world-spell-visuals.json`: 13 projectile weenies and one creature (Drudge Skulker). |
| Chorizite/DatReaderWriter | NuGet 2.1.9 (already a project dependency) | MIT | Decoding MotionTable, PhysicsScriptTable, ParticleEmitter(Info), SpellComponentTable, Setup, CharGen; cross-check oracle for Animation, PhysicsScript and SpellTable. |
| eriknihlen/OpenAC | 1ee5a08 | MIT | Read only: agreement on the per-metre emission rule; its interpolating sequencer motivated the `PoseSampling` switch. No code copied. |
| An earlier private prototype (same author) | — | AGPL-3.0 | Read only: the player structure (script timeline → emitters → particles) and the chain-audit idea. No code copied; its approximated kernels were not used. |
| ACEmulator/ACViewer | c5c54fb | GPL-3.0 | Read only (same particle code as ACE). No code copied. |

## Files

### Ported (header names the source)

Product code (mostly server-side rules: the retail server is not available, so these are ACE's, AGPL-3.0 @95ef426d;
paths are under `Source/ACE.Server/` unless they start with another project):

| Lifestone file | ACE source | Notes |
|---|---|---|
| `Magic/SpellCastSimulation.cs` | `WorldObjects/WorldObject_Magic.cs` (`DoSpellEffects`, `CreateSpellProjectiles`, `CalculatePreOffset`, `CalculateProjectileOrigins`, `CalculateProjectileVelocity`, `LaunchSpellProjectiles`), `SpellProjectile.cs` (`Setup`, `ProjectileImpact`, `OnCollideObject`), `Physics/Trajectory.cs`, `Physics/Common/Position.cs` (`CylinderDistance`) | Stationary target; geometric impact test with a 10 s timeout. Client-side playback is the retail code below. |
| `Magic/SpellVisualResolver.cs` | `WorldObjects/SpellProjectile.cs` (`GetProjectileSpellType`, `GetProjectileScriptIntensity`), `WorldObject_Magic.cs` (`DoSpellEffects` rules) | Script lookup and closure are retail (`PhysicsScriptTable::GetScript`, `CPhysicsObj::InitDefaults`, `set_description`). |
| `Magic/CastGesturePlanner.cs` | `WorldObjects/Player_Magic.cs` (`CreatePlayerSpell`, `DoSpellWords`, `DoWindupGestures`, `DoCastGesture`, `FinishCast`), `WorldObject_Networking.EnqueueMotionMagic`, `Entity/SpellFormula.cs`, `ACE.DatLoader/FileTypes/MotionTable.cs` (`GetDefaultMotion`, `GetAnimData`, `GetAnimationLength`) | Non-PK path; a timed step list instead of an ActionChain. |
| `Magic/SpellComponentTableData.cs` (`GetSpellWords` only) | `ACE.DatLoader/FileTypes/SpellComponentsTable.cs` | Component decoding is retail `SpellComponentBase`. |
| `Magic/SpellFormulaRules.cs` (`AceEffectIntensity`, `AceSpellLevel` only) | `Entity/SpellFormula.cs` (`ScarabScale`, `MinPower`, `MaxSpellLevel`), `Entity/Spell.cs` (`Level`) | The rest of the file is retail (table below). |
| `Magic/SpellInfo.cs` (`SpellFlags` only) | `ACE.Entity/Enum/SpellFlags.cs` | Names and values of the retail `CSpellBase::_bitfield` bits. |
| `Animation/MotionCommands.cs` | `ACE.Entity/Enum/MotionCommand.cs`, `CommandMasks.cs` | A subset of the 11.6096 command ids and the class masks, as `uint` constants. |
| `src/Lifestone.Client/Session/StandIn/StandInWorld.Cast.cs` (M3 2.1 s2a, s2b) | `WorldObjects/Player_Magic.cs` (the cast sequence: words, wind-ups, gesture, effect, Ready, UseDone; the armour redirect's acceptance of a creature for Vestements, 1391-1393), `WorldObject_Magic.cs` (`DoSpellEffects`, `HandleCastSpell_Transfer`, the item-enchantment redirects 2067-2121 and their chat lines 430-452), `Creature_Vitals.cs`, `Player_Vitals.cs`, `WorldObject_Networking.cs` (`EnqueueMotionMagic`) | Re-expressed arithmetic and tables, not copied code: the stand-in answers a cast with ACE's messages in ACE's order, from the cast planner's plan; the vital rows are ACE world rows. The file header names ACE@95ef426d and AGPL-3.0. |
| `src/Lifestone.Client/Session/StandIn/StandInWorld.Combat.cs` (M3 3.1 fb) | `WorldObjects/WorldObject_Magic.cs` (`CreateSpellProjectiles`, `CalculateProjectileOrigins`, `CalculateProjectileVelocity`, `LaunchSpellProjectiles`, `GetProjectileSpeed`, `DoSpellEffects`), `WorldObjects/SpellProjectile.cs` (`Setup`, `GetProjectileScriptIntensity`, `ProjectileImpact` 209-248, `OnCollideObject`, `DamageTarget`; the missing killing-blow line, 846-851), `Creature_Death.cs` (`OnDeath`, `GetDeathMessage`, `Die`, `OnDeath_GrantXP`), `Player_Xp.cs` (`UpdateXpAndLevel`), `WorldObject.cs` (`QueryHealth`) | Re-expressed message order, state bits and arithmetic, not copied code: the stand-in's player is the only caster and damager; the target stands still (no intercept solver); the sizes, damage numbers, maximum health and death animation length are placeholders, each a logged `TODO(m3-standin-*)`. The file header names ACE@95ef426d and AGPL-3.0. |
| `src/Lifestone.Client/Session/StandIn/StandInDamageText.cs` (M3 3.1 fb) | `Entity/Strings.cs` (`GetAttackVerb`, `GetDeathMessage`, the death-message lists) | The attack verb (by damage type and the share of the target's maximum health) and the killer's sentence, re-expressed; only the player-as-attacker forms are kept, and the damage type is read from the spell's name (`TODO(m3-standin-spell-damage)`). The file header names ACE@95ef426d and AGPL-3.0. |
| `src/Lifestone.Client/Session/StandIn/StandInWorld.Death.cs` (M3 3.2 db) | `WorldObjects/Player_Death.cs` (`OnDeath`, `InflictVitaePenalty`, `Die`, `ThreadSafeTeleportOnDeath`, `HandleActionDie`, `HandleSuicide`, `CalculateDeathItems`, `GetNumItemsDropped`, `DropMessage`, `SetLifestoneProtection`, `HandleLifestoneProtection`, `LifestoneProtectionTick`), `Creature_Death.cs` (`CreateCorpse`, `GetDeathMessage`), `Monster_Melee.cs` (`MeleeAttack`, `GetCombatManeuver`, `DoSwingMotion`), `Monster_Combat.cs` (`DoAttackStance`), `Player_Combat.cs` (`TakeDamage`, `OnEvade`), `Entity/DamageEvent.cs` (the lifestone protection), `Entity/Strings.cs` (the victim sentences), `WorldObjects/Lifestone.cs` (`ActOnUse`), `WorldObjects/Managers/EnchantmentManager.cs` (`UpdateVitae`, `GetMinVitae`), `Player_Character.cs` and `ACE.Database/CharacterExtensions.cs` (`AddSpellToBar`) | Re-expressed message order, state bits and arithmetic, not copied code; with changes: the stand-in's tusker is the only monster and attacks in a placeholder rhythm with placeholder damage and reach, and the stand-in has no weenie database, so the corpse, the lifestone and the lost items are named placeholders (`TODO(m3-standin-tusker-attack)`, `m3-standin-corpse-weenie`, `m3-standin-lifestone-weenie`, `m3-standin-death-items`) over the stand-in's own pack. The file header names ACE@95ef426d and AGPL-3.0. |
| `src/Lifestone.Client/Session/Mage/BuffStatMods.cs` (M3 2.1 s0a, fix B) | `WorldObjects/WorldObject_Magic.cs` (`HandleCastSpell_Transfer`, lines 699-835) | Re-expressed as `BuffStatMods.Transfer`: the caster is both source and destination of a self cast, the drain resistance is 1; the StatMod, transfer and boost rows are the ACE world database's spell table, read by read-only SELECTs on 2026-10-16 (data, not code). |
| `tests/Lifestone.Client.Tests/Session/Mage/ScriptedCaster.cs` (M3 2.1 s0b, fix B) | `WorldObjects/Creature_Magic.cs` (`CalculateManaUsage` 14-56, `GetManaCost` 58-100), `SkillCheck.cs` (`GetSkillChance` 9-14), `WorldObject_Weapon.cs` (`GetWeaponManaConversionModifier` 235-262) | Test double: the roll is a seeded `Random`; the wand has no ManaConversionMod, so the weapon modifier is 1; the skill is the scripted mage's. |

Tests:

- `tests/Lifestone.Data.Tests/Animation/AceMotionOracle.cs` — ACE `Physics/Animation/MotionTable.cs`, `MotionState.cs`,
  `Sequence.cs` (AGPL-3.0), reduced to the anim-list bookkeeping; used as an independent test oracle.
- `tests/Lifestone.Data.Tests/Effects/AceParticleOracle.cs` — ACE `Physics/Particles/Particle.cs` (AGPL-3.0) with the
  retail corrections switchable; test oracle.
- `tests/Lifestone.Data.Tests/Magic/SpellTableTests.cs` (`AceComputeHash`) — ACE `SpellTable.ComputeHash`, verbatim.

### Re-expressed retail behaviour, cross-checked with ACE (each type cites its retail functions)

| Lifestone file | Retail functions (11.4186) | ACE counterpart (@95ef426d) |
|---|---|---|
| `Animation/AcFrame.cs` | `Frame::combine`, `subtract1`, `rotate`, `grotate`, `set_rotate`, `localtoglobalvec` | `Physics/Animation/AFrame.cs` |
| `Animation/AnimHooks.cs`, `AnimationData.cs` | `CAnimHook::UnPackHook` and per-hook `UnPack`, `CAnimation::UnPack`, `AnimFrame::UnPack` | `ACE.DatLoader/Entity/AnimationHooks/*`, `FileTypes/Animation.cs` |
| `Animation/AnimSequence.cs` | `CSequence::*`, `AnimSequenceNode::*` | `Physics/Animation/Sequence.cs`, `AnimSequenceNode.cs` |
| `Animation/MotionTableLogic.cs` | `CMotionTable::GetObjectSequence`, `get_link`, `is_allowed`, `re_modify`, `StopSequenceMotion`, `StopObjectCompletely`, `SetDefaultState`, `add_motion`, `combine_motion`, `subtract_motion`, `change_cycle_speed`, `same_sign` | `Physics/Animation/MotionTable.cs` |
| `Animation/MotionState.cs`, `MotionTableManager.cs`, `MotionPlayer.cs` | `MotionState::*`, `MotionTableManager::*`, `CMotionInterp::apply_interpreted_movement`, `CPartArray::InitDefaults` | `MotionState.cs`, `MotionInterp.cs` |
| `Animation/PartPose.cs` | `CPartArray::UpdateParts`, `Frame::combine(…, scale)` | `Physics/PartArray.cs` |
| `Effects/PhysicsScriptData.cs`, `PhysicsScriptTableData.cs` | `PhysicsScript::UnPack`, `PhysicsScriptData::Sort`, `PhysicsScriptTable::GetScript`, `PhysicsScriptTableData::GetScript` | `ACE.DatLoader/FileTypes/PhysicsScript*.cs` (ACE's runtime is a stub) |
| `Effects/Particle.cs`, `ParticleEmitter.cs`, `ParticleManager.cs` | `Particle::Init/Update`, `ParticleEmitter::*`, `ParticleEmitterInfo::GetRandom*/ShouldEmitParticle/InitEnd`, `ParticleManager::*` | `Physics/Particles/*` (several differences, see docs/spell-visuals.md) |
| `Effects/ScriptManager.cs` | `ScriptManager::AddScriptInternal/NextHook/UpdateScripts` | stub in ACE |
| `Effects/EffectObject.cs`, `EffectActor.cs` | `CPhysicsObj::play_script*`, `play_default_script`, `CallPES`, `Set*` + `FPHook::Execute`, `process_hooks`, `process_fp_hook`, `UpdateObjectInternal`, `update_object`, `UpdatePhysicsInternal`, `Frame::set_vector_heading`, hook `Execute` overrides | `Physics/Hooks/*`, `PhysicsObj.cs` |
| `Effects/EffectRandom.cs` | `Random::Seed/rand/RollDice` | — (ACE uses `ThreadSafeRandom`) |
| `Magic/SpellTableData.cs`, `SpellFormulaRules.cs` (except the two ACE members above) | `CSpellBase::UnPack`, `InqSpellFormula`, `InqCustomizedSpellFormula`, `InqScarabOnlyFormula`, `InqSpellLevelByRoughHeuristic`, `SpellFormula::Decrypt`, `RandomizeVersion1/2/3`, `PStringBase<char>::compute_hash`, `MagicSystem::DeterminePowerLevelOfComponent/GetLowestTaperID` | `ACE.DatLoader/Entity/SpellBase.cs`, `FileTypes/SpellTable.cs`, `ACE.Server/Entity/SpellFormula.cs` |
| `Magic/SpellComponentTableData.cs` (decoding) | `SpellComponentBase` | `FileTypes/SpellComponentsTable.cs` |
| `Magic/ComponentWeenies.cs` (`DecodeClientEnumToId`, `ReadCompressed`) | `SerializeIntrusiveHashTable<..., IDClass<_tagDataID,32,0>..., SB_Default>` (11.4186 0x00415C60, 11.6096 0x00415F00; called from `EnumIDMap::Serialize`, 11.4186 0x004165F0, 11.6096 0x00416890): a u8 bucket-size index, the count, then (u32 key, u32 value) pairs; the count is `SB_As32Bit_Compressed::Serialize` (11.4186 0x005BE430; the symbol table gives 11.6096 0x00401A80 for it, a surprising address for a function in gmClient.obj, so the 11.6096 value is the table's claim only): one byte below 0x80, two bytes with the top bit set (15 bits), four bytes with the top two bits set (30 bits) | `DatLoader/FileTypes/DualDidMapper` and `BinaryReaderExtensions.ReadCompressedUInt32` read the same bytes (cross-check only; `ComponentWeeniesTests` compare the decoded map with the SELECT-pinned one). Not a port (1.4 review R7) |
| `Magic/SpellVisualResolver.cs`, `SpellCastSimulation.cs` (client side) | `CPhysicsObj::InitDefaults`, `set_description`, `ACCObjectMaint::CreateObject`, `ACCWeenieObject::DoCollision` | (server parts are ACE ports, listed above) |

## Data extract

`src/Lifestone.Data/Magic/Data/ace-world-spell-visuals.json` carries its licence and source in `_license`/`_source`.
It was produced from the ACE world SQL with a read-only scratch script (not committed); the fields are listed in
`AceWorldExtract.cs`. It is AGPL-3.0 data from the ACE world database, not retail data.
