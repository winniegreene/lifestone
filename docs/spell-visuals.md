# Spell visuals: from a spell id to particles on screen

This is the data layer behind the M1 spell viewer: a human casting a real spell with the retail gestures, the
spell's caster/target particle effects, and war-spell projectiles. Everything here is engine-agnostic
(`Lifestone.Data`, AC coordinates, Z up, seconds, time injected by the caller). The Godot viewer only draws what it
reports.

Sources and licences: [third-party/effects.md](third-party/effects.md). Retail behaviour is re-expressed from the
acclient 11.4186 decompile (function names cited in every type); server behaviour comes from ACE @95ef426d.

## 1. The chain

```
SpellTable 0x0E00000E ─► SpellInfo (school, flags, formula, CasterEffect, TargetEffect, …)
   │  formula decrypted with hash(name)%0x12107680 + hash(desc)%0xBEADCF45   (CSpellBase::InqSpellFormula)
   │  per-account taper colours: RandomizeVersion1/2/3 (InqCustomizedSpellFormula) — never changes gestures/words
   ▼
SpellComponentTable 0x0E00000F ─► words (herb, powder+potion) and gestures (scarab wind-ups, talisman cast)
   ▼
CastPlan (ACE Player.CreatePlayerSpell): words at t=0 → one wind-up per scarab (MagicPowerUpNN ×2.0)
   → talisman cast gesture (Magic* ×2.0) → spell effects → Ready ×1.0 (+1 s recoil)
   ▼
MotionPlayer (retail CMotionTable + MotionTableManager + CSequence) on the caster's MotionTable 0x09000001
   │  animation frames carry hooks: the wind-up glow and hand sparks are CreateParticle hooks in the
   │  MagicPowerUp/Magic* animations themselves (e.g. 0x030005A0 creates 0x3200011E… at frames 0, 9, 19, …)
   ▼
EffectObject (retail CPhysicsObj effect subset) ◄── PlayScript(type, intensity) from the spell
   │  type → caster's/target's PhysicsScriptTable 0x34…: first entry whose threshold ≥ intensity
   ▼
PhysicsScript 0x33… (timeline of hooks; queued back-to-back per object)
   │  CreateParticle / Stop / Destroy / CallPES (nested script after a random delay) / Scale / Transparent / sounds …
   ▼
ParticleEmitter (ParticleEmitterInfo 0x32…) ─► particles: HW GfxObj 0x01…, world position, orientation, scale, translucency
```

War spells additionally launch projectiles:

```
ACE world DB (spell.wcid → weenie) ─► projectile Setup 0x02…, PhysicsEffectTable 0x34…, speed, count/spread
   ▼  at "spell effects": spawn (ACE CalculateProjectileOrigins/Velocity), Setup default script + default animation
   ▼  (CPhysicsObj::InitDefaults), PlayScript.Launch @ intensity by ACE's power-based Spell.Level
   ▼  fly (retail UpdatePhysicsInternal, AlignPath), on hit: ProjectileCollision default script (client collision),
      PlayScript.Explode (server), NoDraw — the particles keep playing.
```

### What the dats give, and what needs the ACE world database

| Need | Source |
|---|---|
| Spell data, formulas, effects, words, gestures | dats (SpellTable, SpellComponentTable) |
| Caster/target PhysicsScriptTables for player characters | dats: CharGen 0x0E000002 `SexCG.PhysicsTable` (Aluvian male: Setup 0x02000001, MotionTable 0x09000001, table 0x34000004) |
| Creature looks (Setup, MotionTable, PhysicsEffectTable, scale) | **ACE world DB** weenie (only 6 Setups carry a `DefaultScriptTable`) |
| Which projectile weenie a war spell launches, its Setup/table/speed/spread | **ACE world DB** (`spell.wcid`, `weenie_properties_*`) |
| Effect intensities sent with PlayScripts, cast timings | ACE server code (retail server unavailable) |
| Scripts, emitters, particle meshes, animations | dats |

The repo carries a minimal AGPL extract for the viewer: `src/Lifestone.Data/Magic/Data/ace-world-spell-visuals.json`
(13 projectiles + the Drudge Skulker: Setup 0x020007DD, MotionTable 0x09000008, table 0x3400001A, scale 0.95).
`AceWorldExtract.Load()` reads it embedded in `Lifestone.Data` (the csproj embeds `Magic/Data/*.json`), falling back to the source tree.

## 2. What the Godot viewer does

The shortest path is `SpellCastSimulation`:

```csharp
using var dats = DatSet.Open();
var world = AceWorldExtract.Load();
var resolver = SpellVisualResolver.Create(dats, world);
var caster = VisualObject.HumanMale(dats);
var target = VisualObject.FromCreature(world.Creatures[0]);
var visual = resolver.Resolve(spellId: 85, caster, target);          // Flame Bolt VI
var sim = new SpellCastSimulation(resolver, visual, casterFrame, targetFrame, seed: 1);

// every frame (t = seconds since start, from the engine clock):
sim.AdvanceTo(t);
foreach (var actor in new[] { sim.Caster, sim.Target }.Concat(sim.Projectiles.Select(p => p.Actor)))
{
    // 1. model: actor.PartFrames[i] is the world frame of Setup part i (flat, not hierarchical);
    //    mesh vertices scale by Setup.DefaultScale[i] * actor.Scale (models track). Skip projectiles with Hidden.
    // 2. material overrides: actor.Effects.Visual (translucency, luminosity, diffusion, per-part, texture velocity).
    // 3. particles: actor.Effects.CollectParticles(list) → each ParticleInstance has GfxObjId, Position,
    //    Orientation, Scale, Translucency. Draw the GfxObj (MultiMesh per GfxObj) with alpha = 1 − translucency
    //    (not drawn at 1.0); blend mode comes from the GfxObj's surfaces (Additive → ONE/ONE, …, rendering.md §6);
    //    apply the GfxObj's degrade billboard mode (DegradeInfo modes 2–5, CPhysicsPart::calc_draw_frame).
    // 4. events: actor.Effects.DrainEvents(into) (a caller-owned List<EffectEvent>) → sounds (Sound/SoundTable/SoundTweaked hooks) etc.
}
// sim.Events is a readable log (chant, motions, scripts, launches, hits) for a HUD.
```

Convert AC → Godot only in `scripts/Core/AcToGodot.cs`. Everything is in the caster's landblock space; keep the
numbers small (put the caster near the origin of a cell) because particle/actor maths is single precision.

Lower-level pieces, for other uses (e.g. M2 driving a character from server messages):

- `MotionPlayer.ApplyInterpretedMotion(style, forwardCommand, speed)` — what the client does for an UpdateMotion.
  `MotionPlayer.Update(dt)` returns fired hooks and the root-motion offset; `Pose` gives per-part frames
  (`PoseSampling.RetailFloor` by default, `Interpolated` as an option).
- `EffectObject.PlayScript(PlayScript, intensity)` — a server PlayScript message; `QueueAnimHook` for animation hooks;
  `Update(now, objectFrame, partFrames)` once per object update.
- `EffectActor.AdvanceTo(t)` — retail `update_object` stepping (≤ 0.2 s quanta, gaps > 2 s skipped) around both.
- `CastGesturePlanner.Plan(...)` — the server's motion messages and their timing.

## 3. Retail rules that matter for the look

- **Pose:** `part_frames[floor(frame_number)]`, no interpolation (`CSequence::get_curr_animframe`). Frame numbers are
  doubles; a negative framerate starts at `high + 1 − 0.0002`.
- **Sequence:** every `append_animation` makes the new tail the loop (`first_cyclic`); a new motion first removes the
  loop (including the current node if it is the loop) and appends links + the new cycle. Finished links are dropped
  (`apricot`). Hooks fire for each frame crossed (not for the node's last frame), filtered by direction; leaving a
  link queues an animation-done hook (which completes queued actions in `MotionTableManager`).
- **Root motion:** per crossed frame, `combine(offset, pos_frame)` plus `velocity / framerate`; humans walk purely by
  pos frames (their MotionData velocity is zero). Scaled by the object scale while on the ground.
- **Clamping:** `set_animation_id` clamps link frame ranges: the PowerUp08–10 wind-ups ask for frames past the
  60-frame animation 0x030005A0, so 08–10 look the same, only faster.
- **Script tables:** `GetScript(type, mod)` returns the first entry (stored order) with threshold ≥ mod; above every
  threshold nothing plays. The human table's attribute/skill/health effects use thresholds 0 / 0.5 / 1, so Lead-scarab
  spells (ACE intensity 0.05) pick the middle entry.
- **Scripts queue:** a script played while another is pending starts when that one ends (`AddScriptInternal`).
- **Emitters:** at most one particle per update when due (per-second: `now − last > birthrate`; per-metre:
  `|Δpos|² > birthrate²`, checked against the emitter position, read from the exe); `InitialParticles` are emitted
  at creation; `TotalParticles`/`TotalSeconds` stop emission; an emitter disappears when stopped and empty.
- **Randomness:** one generator (ran2-style), drawn in a fixed order per particle (see `IEffectRandom`).

## 4. Particle kernels (retail `Particle::Update`, t = lifetime, P = parent frame, O = birth offset)

| Type (dat value) | Birth (local vectors are rotated by the parent frame at birth) | Position |
|---|---|---|
| Still (1) | — | P + O |
| LocalVelocity (2) | A local | P + O + tA |
| ParabolicLVGA (3) | A local, B global | P + O + tA + ½t²B |
| ParabolicLVGAGR (4) | A local, B, C global | P-frame moved by O + tA + ½t²B, then rotated by tC (local) |
| Swarm (5) | A local, B, C global | P + O + tA + (cos(B.x t)C.x, sin(B.y t)C.y, cos(B.z t)C.z) |
| Explode (6) | A, B global; C = unit direction from two random angles | P + O + (tB + C·A.x)t, z also + A.z·t |
| Implode (7) | A, B global; O ∘= C; C = O | P + O + cos(A.x t)C + t²B |
| ParabolicLVLA (8) | A, B local | P + O + tA + ½t²B |
| ParabolicLVLALR (9) | A, B, C local | as 4 |
| ParabolicGVGA (10) | A, B global | as 3 (no emitter uses it) |
| ParabolicGVGAGR (11) | A, B, C global | as 4 |
| GlobalVelocity (12) | A global | P + O + tA |

Scale and translucency go linearly from start to final over the lifespan. Parent-local emitters use the parent's
current frame instead of the frame at birth. Only types 4/9/11 rotate particles; others keep identity orientation.

**ACE differs from retail** in types 3, 4, 5, 6, 8, 9, 10, 11 (accumulating parabolic positions, dropped A/B vectors,
Swarm adding C, Explode's z), in `GetRandomFinalScale`/`GetRandomStartTrans`/`GetRandomFinalTrans` (multiply instead
of add), `GetRandomOffset` (length formula), `InitEnd` (emits TotalParticles instead of InitialParticles), the
per-metre test, and the degraded-emitter flag. `ParticleKernelTests` checks each type against the ACE port with
retail corrections, and that ACE's own formulas differ exactly where listed. ACE's `CMotionTable` port differs in
three corners (style change substate, `same_sign(0, x)`, an action `num_anims`); none matter for human tables.

## 5. Showcase (caster: Aluvian male, Setup 0x02000001, MotionTable 0x09000001, table 0x34000004; target: Drudge Skulker)

Intensities are ACE's (`SpellFormula.ScarabScale` for effects; `GetProjectileScriptIntensity` for projectiles, keyed by
ACE's server `Spell.Level` from the spell's power — minimum power 1/50/100/150/200/250/300/400 for levels 1–8 — not
by the client's scarab heuristic `InqSpellLevelByRoughHeuristic`; the two disagree for 250 of the 792 projectile
spells, e.g. the power-200 rings 1783–1789 are level 5 → 0.8, and the power-320 spells 1832–1838 are level 7, which as
rings would give 1.0 instead of 0.4).
Durations are ACE's server timings (link length ÷ 2.0). "—" = none. All rows are asserted by `SpellVisualTests`.

| Spell | Words | Gestures | Effect | Projectile |
|---|---|---|---|---|
| 27 Flame Bolt I (war) | Zojak Quaguz | (Lead: no wind-up) → MagicRecoilMissile 0.52 s | — | Bolt: Setup 0x0200040D, table 0x34000005, 15 m/s, Launch@0 0x3300011B, Explode 0x3300011E |
| 85 Flame Bolt VI (war) | Zojak Quaguz | MagicPowerUp10 1.78 s → MagicRecoilMissile 0.52 s | — | Bolt, Launch@1 0x3300011D, Explode 0x33000120 |
| 28 Frost Bolt I (war) | Zojak Quapaj | → MagicRecoilMissile | — | Bolt: Setup 0x020003F4 (default anim 0x03000623), table 0x34000006, Launch 0x33000115, Explode 0x33000118 |
| 75 Lightning Bolt I (war) | Zojak Quasith | → MagicRecoilMissile | — | Bolt: Setup 0x020003F0 (default script 0x33000104, anim 0x03000620), table 0x34000007, Launch 0x3300013A, Explode 0x3300013D |
| 1796 Flame Streak I (war, FastCast) | Jevak Quaguz | MagicRecoilMissile | — | Streak, 45 m/s, Launch 0x3300011B |
| 2739 Flame Arc I (war) | Zojak Quaguz | → MagicRecoilMissile | — | Arc (gravity), 40 m/s, Launch 0x3300011B |
| 1785 Cassius' Ring of Fire (war) | Volae Quaguz | MagicPowerUp04 ×2 (Diamond scarabs) → MagicVision | — | Ring: 9 × Setup 0x02000881 (default script 0x330008C3), 2 m/s, intensity 0.8 (power 200 → level 5), no PhysicsEffectTable |
| 6 Heal Self I (life) | Malar Zhapaj | → MagicSelfHeart 0.31 s | target (self) HealthUpRed@0.05 → 0x33000096 | — |
| 2 Strength Self I (creature, FastCast) | Malar Cazael | MagicSelfHead 0.38 s | self AttribUpRed@0.05 → 0x33000046 | — |
| 24 Armor Self I (life, FastCast) | Helkas Quasith | MagicPray 0.33 s | self ShieldUpGrey@0.05 → 0x3300068F | — |
| 35 Aura of Blood Drinker Self I (item) | Malar Aeril | → MagicSelfHead 0.38 s | self EnchantUpRed@0.05 → 0x330000EF | — |
| 25 Imperil Other I (life) | Cruath Quasith | → MagicClap 0.90 s | drudge ShieldDownGrey@0.05 → 0x33000687 (table 0x3400001A) | — |
| 7 Harm Other I (life) | Equin Zhapaj | → MagicHarm 0.44 s | drudge HealthDownRed@0.05 → 0x330001C9 | — |
| 2645 Portal Recall (item) | Shurov Thipaj | MagicPowerUp04 1.01 s → MagicSelfHead 0.38 s | caster AttribUpPurple@0.4 → 0x33000040 | — |
| 1635 Lifestone Recall (item) | Shurov Thiguz | MagicPowerUp04 1.01 s → MagicSelfHead 0.38 s | caster AttribUpBlue@0.4 → 0x3300002B | — |

A fizzle (ACE: `PlayScript.Fizzle` @0.5 on the caster) resolves to 0x33000103 in the human table. ACE sends the fizzle after the words and the gestures, not instead of them: the pre-check status is read at the end of the chain (M3 2.1 s2a; the stand-in plays it so, `docs/session.md` section 16).

Chain audit (exhaustive test): every caster/target effect of all 6,266 spells resolves through the human table
(10,944 resolutions with the drudge table; only 7 special-event PlayScripts are absent from the drudge's), all
nested scripts and emitters exist, and the 73 particle GfxObjs involved exist.

## 6. Data facts (user's end-of-retail portal, iteration 2072)

- 6,266 spells (FormulaVersion 1: 5,888; 2: 163; 3: 215), 139 spell sets, 163 components. Retail-exact decryption
  gives only valid component ids; ACE/DatReaderWriter's `& 0xFF` fix-up never triggers. Account randomisation
  rewrites slots 1, 3 and 6 by position; some NPC/item formulas lose a real component there (e.g. Portal Sending).
- `SpellComponentBase.Time` = Σ (high − low)/|fps| of the gesture's Magic-stance links (talismans include the link
  back to Ready), except Pyreal Scarab (unclamped range), Hazel Talisman (24 vs 30 fps) and Dark Scarab.
- 2,066 Animations (357 with root motion), 436 MotionTables (no missing animation references), 4,248 PhysicsScripts
  (12,268 hooks), 164 PhysicsScriptTables, 2,051 ParticleEmitterInfos (11 particle types in use; type 10 unused;
  per-second and per-metre emitters).
- Hook types present: in animations Sound, SoundTable, Attack, ReplaceObject, Ethereal, TransparentPart,
  CreateParticle, StopParticle, DefaultScript, CallPES, Transparent, SoundTweaked, SetOmega (13); in scripts
  SoundTable, Scale, CreateParticle, DestroyParticle, StopParticle, NoDraw, CallPES, Transparent, SoundTweaked,
  TextureVelocity, SetLight, CreateBlockingParticle (12). The decoder handles all 27 retail types.
- DatReaderWriter 2.1.9 reads no payload for CreateBlockingParticle (retail reads 40 bytes). All 15 such hooks are the
  last entry of their script, so DatReaderWriter's other fields still line up, but it loses the emitter data — hence
  Lifestone's own hook decoder. Its `MotionCommand` enum matches the 11.6096 numbering for every command used here.

## 7. Known deviations and open items

Each is either measured or fails loudly; none is silent.

| Item | Status |
|---|---|
| Ties in PhysicsScript start times | Retail sorts with MSVC `qsort` (comparator never returns 0; unstable). We keep dat order for ties. 2,294 scripts have ties; only 166 have > 8 hooks (where qsort's partitioning could reorder); no tie touches the same emitter id twice, so only the order of random draws could differ. |
| Emitter update order | Retail iterates a hash table; we use creation order (affects only random-draw order). |
| Random numbers | Retail's single global generator is seeded from `time(0)`; each Lifestone effect object has its own seeded generator. Draw order per particle is retail's. |
| Float precision | Retail mixes x87 extended precision and float temporaries; we compute kernels in double and store floats. |
| `ShouldDrawParticles` | Viewer-distance culling against the particle GfxObj's max degrade distance is an input (`ParticlesInDrawRange`, default true); the degraded path is ported. |
| Billboarding | Particle orientation from the GfxObj DegradeInfo mode (2–5) is the renderer's job (rendering.md §6). |
| Client input layer | `ApplyInterpretedMotion` covers style + forward command on the ground (no sidestep/turn/jump; `CMotionInterp` is otherwise not ported — M2). |
| Collision | No collision system yet: `SpellCastSimulation` hits when the projectile reaches the aim point (radius test) or passes it, and only a hit plays a projectile spell's target effect (ACE `OnCollideObject` → `DoSpellEffects(projectileHit: true)`). A flight that has not hit after `MaxFlightSeconds` (10 s) impacts where it is without the target effect (like ACE `OnCollideEnvironment`). A projectile spell whose weenie is missing from the extract launches nothing and plays no target effect. Ground friction is not modelled. |
| Script table of server objects | Retail `CPhysicsObj::InitDefaults` installs a Setup's `DefaultScriptTable`, but `ACCObjectMaint::CreateObject` then calls `CPhysicsObj::set_description`, which releases it and installs only the PhysicsDesc's table (null when the PhysicsEffectTable flag 0x1000 is absent). Creatures and projectiles therefore use the weenie's PhysicsEffectTable only; the Setup table matters for client-created objects, which M1 does not spawn. |
| Server rules | Cast timing, intensities, projectile spawn/velocity and spell words are ACE's (the retail server is not available). ACE's PK "FastTick" path differs in timing only; we follow the default path. |
| `Frame::set_vector_heading` | The decompiler prints `atan2(x, y)`; we use `atan2(y, x)` like ACE so that identity faces +Y. Used only to orient projectiles. |
| Out-of-range part index in a hook | Retail would read past the part array; we attach to the object frame and record a warning (`EffectObject.Warnings`). |
| Empty PhysicsScript | Retail dereferences its first hook; we treat it as due at once (none exist in the dats). |
| `RandomizeVersion2` with a zero divisor | Retail faults; we throw `InvalidOperationException` (no spell triggers it). |
| Modifier with no MotionData in `StopObjectCompletely` | Retail would loop forever; we throw. |
| Interpolated poses | `PoseSampling.Interpolated` is a non-retail option (lerp/slerp inside the current node). |
| Embedded JSON | The extract is loaded from the source tree until `Lifestone.Data.csproj` embeds `Magic/Data/*.json` (requested). |
| Profile spells (M3 2.1 s2b; a fact, not a deviation) | No profile spell has two scarabs (`CastGesturePlanner.IsAceScarab` counts 1 for every line), the creature-school buffs are all FastCast (a gesture, no wind-up; Adja's Blessing 2215 is one), and no profile spell has a caster effect (CasterEffect is Invalid for all; the target effect plays on the player for a self spell). The play-check's cast is Revitalize Self VI (1182): a wind-up (0x10000078 at 0 s), the gesture (0x4000002D at 1.78 s), the effect at 2.09 s and the target effect on the player. |
