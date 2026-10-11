// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/SpellProjectile.cs (AGPL-3.0)
// [with changes: GetProjectileKind = GetProjectileSpellType and GetProjectileScriptIntensity are ACE code, taking the
// spell's SpellInfo and its ACE world extract entry instead of a Spell, and SpellCategory values as numbers; the rules of
// who gets which effect follow WorldObject_Magic.cs DoSpellEffects. Script lookup and the CallPES/emitter closure are
// retail (PhysicsScriptTable::GetScript, CPhysicsObj::InitDefaults)].
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using DatCharGen = DatReaderWriter.DBObjs.CharGen;
using DatSetup = DatReaderWriter.DBObjs.Setup;

namespace Lifestone.Data.Magic;

/// <summary>What an object looks like and which tables drive it (the parts of a weenie/PhysicsDesc the effects need).</summary>
/// <param name="Name">Display name.</param>
/// <param name="SetupId">Setup (0x02).</param>
/// <param name="MotionTableId">MotionTable (0x09), 0 = not animated by a motion table.</param>
/// <param name="PhysicsScriptTableId">
/// PhysicsScriptTable (0x34) used to resolve PlayScripts, 0 = none. For a server-created object this is the
/// PhysicsDesc/weenie table alone: retail <c>CPhysicsObj::InitDefaults</c> installs the Setup's <c>DefaultScriptTable</c>,
/// but <c>ACCObjectMaint::CreateObject</c> then calls <c>CPhysicsObj::set_description</c>, which releases that table and
/// installs only the PhysicsDesc's (whose id is null when the PhysicsEffectTable flag is absent).
/// </param>
/// <param name="Scale">Object scale.</param>
public sealed record VisualObject(string Name, uint SetupId, uint MotionTableId, uint PhysicsScriptTableId, float Scale)
{
    /// <summary>CharGen (0x0E000002) heritage/sex entry: <c>SexCG.SetupId</c>, <c>MotionTable</c>, <c>PhysicsTable</c>, <c>Scale</c>%.</summary>
    public static VisualObject FromCharGen(DatSet dats, uint heritage, int gender)
    {
        if (!dats.TryGetPortal<DatCharGen>(0x0E000002, out var chargen))
        {
            throw new InvalidDataException("CharGen 0x0E000002 not found.");
        }

        var group = chargen.HeritageGroups[heritage];
        var sex = group.Genders[gender];
        return new VisualObject($"{group.Name.Value} {sex.Name.Value}", sex.SetupId.DataId, sex.MotionTable.DataId, sex.PhysicsTable.DataId, sex.Scale / 100f);
    }

    /// <summary>Aluvian male (heritage 1, gender 1): Setup 0x02000001, MotionTable 0x09000001, PhysicsScriptTable 0x34000004.</summary>
    public static VisualObject HumanMale(DatSet dats) => FromCharGen(dats, 1, 1);

    public static VisualObject FromCreature(CreatureVisualEntry creature) =>
        new(creature.Name, creature.SetupId, creature.MotionTableId, creature.PhysicsEffectTableId, creature.DefaultScale ?? 1f);
}

/// <summary>A PlayScript resolved through an object's PhysicsScriptTable, with everything it can spawn.</summary>
/// <param name="Type">The PlayScript.</param>
/// <param name="Intensity">The intensity ("mod") used for the lookup.</param>
/// <param name="TableId">PhysicsScriptTable used.</param>
/// <param name="ScriptId">Selected PhysicsScript, 0 when the table has none for (type, intensity).</param>
/// <param name="ScriptClosure">The script plus every script reachable through CallPES hooks.</param>
/// <param name="EmitterInfoIds">ParticleEmitterInfos created by those scripts.</param>
/// <param name="ParticleGfxObjIds">Hardware GfxObjs of those emitters (what the particles draw).</param>
public sealed record ResolvedScript(
    PlayScript Type, float Intensity, uint TableId, uint ScriptId, IReadOnlyList<uint> ScriptClosure, IReadOnlyList<uint> EmitterInfoIds, IReadOnlyList<uint> ParticleGfxObjIds);

/// <summary>ACE <c>ProjectileSpellType</c>.</summary>
public enum ProjectileKind
{
    Undef,
    Bolt,
    Blast,
    Volley,
    Streak,
    Arc,
    Ring,
    Wall,
    Strike,
}

/// <summary>How a war spell's projectile looks and moves.</summary>
public sealed record ProjectileVisual
{
    public required WarProjectileEntry Source { get; init; }

    public required ProjectileKind Kind { get; init; }

    /// <summary>Setup default script (played on creation by retail <c>CPhysicsObj::InitDefaults</c>), 0 = none.</summary>
    public required uint SetupDefaultScriptId { get; init; }

    /// <summary>Setup default animation (looped at 30 fps by <c>CPartArray::InitDefaults</c>), 0 = none.</summary>
    public required uint SetupDefaultAnimationId { get; init; }

    /// <summary>Intensity ACE sends with the Launch and Explode scripts (<c>SpellProjectile.GetProjectileScriptIntensity</c>, by the power-based <c>Spell.Level</c>).</summary>
    public required float ScriptIntensity { get; init; }

    /// <summary>
    /// PlayScript.Launch on the projectile at creation; null when the weenie has no PhysicsEffectTable. The Setup's
    /// <c>DefaultScriptTable</c> is deliberately not a fallback: retail <c>ACCObjectMaint::CreateObject</c> runs
    /// <c>CPhysicsObj::set_description</c> after <c>InitDefaults</c>, and it releases the Setup's table and installs
    /// only the PhysicsDesc's (none when the CreateObject has no PhysicsEffectTable).
    /// </summary>
    public ResolvedScript? Launch { get; init; }

    /// <summary>PlayScript.Explode on the projectile at impact.</summary>
    public ResolvedScript? Explode { get; init; }

    /// <summary>ACE base speed (m/s), <c>PropertyFloat.MaximumVelocity</c>.</summary>
    public required float Speed { get; init; }

    /// <summary>Arcs fly under gravity (ACE <c>useGravity</c>).</summary>
    public bool UsesGravity => Kind == ProjectileKind.Arc;
}

/// <summary>Everything the viewer needs to show one spell between a caster and a target.</summary>
public sealed record SpellVisual
{
    public required SpellInfo Spell { get; init; }

    public required CastPlan Plan { get; init; }

    public required VisualObject Caster { get; init; }

    public required VisualObject Target { get; init; }

    /// <summary>The spell's CasterEffect on the caster (null when the spell has none).</summary>
    public ResolvedScript? CasterEffect { get; init; }

    /// <summary>The spell's TargetEffect on the target (on the caster for self-targeted spells); null when none.</summary>
    public ResolvedScript? TargetEffect { get; init; }

    /// <summary>The projectile of a war/void projectile spell, when the ACE extract has it.</summary>
    public ProjectileVisual? Projectile { get; init; }

    /// <summary>
    /// ACE <c>Spell.IsProjectile</c> (<c>NumProjectiles &gt; 0</c>): from the extract's weenie when present, else from the
    /// spell's <see cref="SpellInfo.MetaSpellType"/> (Projectile, LifeProjectile, EnchantmentProjectile). A projectile
    /// spell's <see cref="TargetEffect"/> plays only when a projectile hits (ACE <c>DoSpellEffects(projectileHit)</c>).
    /// </summary>
    public required bool IsProjectileSpell { get; init; }

    /// <summary>
    /// What a fizzle looks like on the caster: ACE plays <c>PlayScript.Fizzle</c> at intensity 0.5 (the table's
    /// <see cref="SpellInfo.FizzleEffect"/> is 0 for every spell).
    /// </summary>
    public required ResolvedScript Fizzle { get; init; }

    /// <summary>Known gaps (e.g. projectile spells missing from the extract).</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// Resolves a spell's full visual chain: cast gestures (<see cref="CastGesturePlanner"/>), the caster/target
/// PlayScripts through each object's PhysicsScriptTable, and the war projectile (setup, Launch/Explode scripts).
/// </summary>
/// <remarks>
/// Server rules follow ACE <c>WorldObject.DoSpellEffects</c>: the caster effect plays on the caster unless the spell is
/// a projectile that hit; the target effect plays on the target for non-projectiles (projectiles play it on hit);
/// both with intensity <see cref="SpellFormulaRules.AceEffectIntensity"/>. The client picks the script with
/// <see cref="PhysicsScriptTableData.GetScript"/>.
/// </remarks>
public sealed class SpellVisualResolver(
    SpellTableData spells, SpellComponentTableData components, IEffectSource effects, DatAnimationSource animations, AceWorldExtract? world)
{
    public SpellTableData Spells { get; } = spells;

    public SpellComponentTableData Components { get; } = components;

    public IEffectSource Effects { get; } = effects;

    public DatAnimationSource Animations { get; } = animations;

    public AceWorldExtract? World { get; } = world;

    public static SpellVisualResolver Create(DatSet dats, AceWorldExtract? world) =>
        new(SpellTableData.Load(dats), SpellComponentTableData.Load(dats), new DatEffectSource(dats), new DatAnimationSource(dats), world);

    public SpellVisual Resolve(uint spellId, VisualObject caster, VisualObject target, string accountName = "")
    {
        if (!Spells.Spells.TryGetValue(spellId, out var spell))
        {
            throw new KeyNotFoundException($"Spell {spellId} is not in the SpellTable.");
        }

        var table = Animations.GetMotionTable(caster.MotionTableId)
            ?? throw new KeyNotFoundException($"MotionTable 0x{caster.MotionTableId:X8} of '{caster.Name}' not found.");
        var plan = CastGesturePlanner.Plan(spell, Components, table, Animations, accountName);
        var notes = new List<string>();
        var isProjectile = IsProjectileMetaType(spell);
        var effectTarget = (spell.Flags & SpellFlags.SelfTargeted) != 0 ? caster : target;

        ResolvedScript? casterEffect = spell.CasterEffect != PlayScript.Invalid
            ? ResolveScript(caster.PhysicsScriptTableId, spell.CasterEffect, plan.EffectIntensity)
            : null;
        ResolvedScript? targetEffect = spell.TargetEffect != PlayScript.Invalid
            ? ResolveScript(effectTarget.PhysicsScriptTableId, spell.TargetEffect, plan.EffectIntensity)
            : null;

        ProjectileVisual? projectile = null;
        if (World?.ProjectilesBySpell.TryGetValue(spellId, out var entry) == true)
        {
            isProjectile = (entry.NumProjectiles ?? 0) > 0;
            if (isProjectile)
            {
                projectile = ResolveProjectile(spell, entry);
            }
            else
            {
                // ACE CreateSpellProjectiles logs an error and launches nothing when NumProjectiles == 0.
                notes.Add($"Spell {spellId}'s weenie in {AceWorldExtract.FileName} has no NumProjectiles; ACE launches nothing.");
            }
        }
        else if (isProjectile)
        {
            notes.Add($"Spell {spellId} launches a projectile, but its weenie is not in {AceWorldExtract.FileName} (needs the ACE world DB).");
        }

        return new SpellVisual
        {
            Spell = spell,
            Plan = plan,
            Caster = caster,
            Target = effectTarget,
            CasterEffect = casterEffect,
            TargetEffect = targetEffect,
            Projectile = projectile,
            IsProjectileSpell = isProjectile,
            Fizzle = ResolveScript(caster.PhysicsScriptTableId, PlayScript.Fizzle, 0.5f),
            Notes = notes,
        };
    }

    /// <summary>Retail <c>PhysicsScriptTable::GetScript</c> plus the CallPES/emitter closure of the chosen script.</summary>
    public ResolvedScript ResolveScript(uint tableId, PlayScript type, float intensity)
    {
        var table = tableId != 0 ? Effects.GetScriptTable(tableId) : null;
        var scriptId = table?.GetScript(type, intensity) ?? 0;
        var closure = new List<uint>();
        var emitters = new SortedSet<uint>();
        var gfx = new SortedSet<uint>();
        var pending = new Stack<uint>();
        if (scriptId != 0)
        {
            pending.Push(scriptId);
        }

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (closure.Contains(id))
            {
                continue;
            }

            closure.Add(id);
            var script = Effects.GetScript(id) ?? throw new KeyNotFoundException($"PhysicsScript 0x{id:X8} (from table 0x{tableId:X8}) is missing.");
            foreach (var entry in script.Hooks)
            {
                switch (entry.Hook)
                {
                    case CallPesHook call:
                        pending.Push(call.ScriptId);
                        break;
                    case CreateParticleHook create:
                        AddEmitter(create.EmitterInfoId);
                        break;
                    case CreateBlockingParticleHook create:
                        AddEmitter(create.EmitterInfoId);
                        break;
                }
            }
        }

        return new ResolvedScript(type, intensity, tableId, scriptId, closure, emitters.ToArray(), gfx.ToArray());

        void AddEmitter(uint infoId)
        {
            emitters.Add(infoId);
            if (Effects.GetEmitterInfo(infoId) is { } info && info.HwGfxObjId != 0)
            {
                gfx.Add(info.HwGfxObjId);
            }
        }
    }

    private ProjectileVisual ResolveProjectile(SpellInfo spell, WarProjectileEntry entry)
    {
        var kind = GetProjectileKind(spell, entry);
        var intensity = GetProjectileScriptIntensity(spell, kind);
        uint defaultScript = 0, defaultAnim = 0;
        if (Animations.Dats.TryGetPortal<DatSetup>(entry.SetupId, out var setup))
        {
            defaultScript = setup.DefaultScript.DataId;
            defaultAnim = setup.DefaultAnimation.DataId;
        }

        return new ProjectileVisual
        {
            Source = entry,
            Kind = kind,
            SetupDefaultScriptId = defaultScript,
            SetupDefaultAnimationId = defaultAnim,
            ScriptIntensity = intensity,
            Launch = entry.PhysicsEffectTableId is { } t1 ? ResolveScript(t1, PlayScript.Launch, intensity) : null,
            Explode = entry.PhysicsEffectTableId is { } t2 ? ResolveScript(t2, PlayScript.Explode, intensity) : null,
            Speed = entry.MaximumVelocity ?? 0f,
        };
    }

    /// <summary>Projectile spell by meta type, the dats' stand-in for ACE's world-DB <c>NumProjectiles &gt; 0</c>.</summary>
    public static bool IsProjectileMetaType(SpellInfo spell) =>
        spell.MetaSpellType is SpellType.Projectile or SpellType.LifeProjectile or SpellType.EnchantmentProjectile;

    /// <summary>ACE <c>SpellProjectile.GetProjectileSpellType</c> (categories from ACE <c>SpellCategory</c>).</summary>
    public static ProjectileKind GetProjectileKind(SpellInfo spell, WarProjectileEntry entry)
    {
        var count = entry.NumProjectiles ?? 0;
        var category = spell.Category;
        var spread = entry.SpreadAngle ?? 0f;
        if (count == 1)
        {
            if (category is >= 243 and <= 249 || category == 639 || category == 409)
            {
                return ProjectileKind.Streak; // AcidStreak..SlashingStreak, NetherStreak, Fireworks
            }

            return entry.NonTracking == true ? ProjectileKind.Arc : ProjectileKind.Bolt;
        }

        if (category is >= 222 and <= 228 || spread == 360f)
        {
            return ProjectileKind.Ring; // AcidRing..SlashingRing
        }

        if (category is >= 131 and <= 137 || category == 638)
        {
            return ProjectileKind.Blast; // AcidBurst..SlashingBurst, NetherDamageOverTimeRaising3
        }

        if (category is >= 207 and <= 213 || spell.Name.Contains("Volley", StringComparison.Ordinal))
        {
            return ProjectileKind.Volley; // AcidVolley..BladeVolley
        }

        if (category is >= 229 and <= 235)
        {
            return ProjectileKind.Wall;
        }

        return category is >= 236 and <= 242 ? ProjectileKind.Strike : ProjectileKind.Undef;
    }

    /// <summary>
    /// ACE <c>SpellProjectile.GetProjectileScriptIntensity</c>. The level is ACE's server, power-based
    /// <c>Spell.Level</c> (<see cref="SpellFormulaRules.AceSpellLevel"/>), not the client's scarab heuristic: e.g. the
    /// power-200 rings (1783–1789) are level 5 → 0.8 (the heuristic says 6 → 0.4); power 320 is level 7 (a ring → 1.0).
    /// </summary>
    public static float GetProjectileScriptIntensity(SpellInfo spell, ProjectileKind kind)
    {
        ArgumentNullException.ThrowIfNull(spell);
        var level = SpellFormulaRules.AceSpellLevel(spell.Power);
        if (kind == ProjectileKind.Wall)
        {
            return 0.4f;
        }

        if (kind == ProjectileKind.Ring)
        {
            if (level == 6 || spell.Id == 3818)
            {
                return 0.4f;
            }

            if (level == 7)
            {
                return 1.0f;
            }
        }

        return level switch
        {
            1 => 0f,
            2 => 0.2f,
            3 => 0.4f,
            4 => 0.6f,
            5 => 0.8f,
            6 or 7 or 8 => 1.0f,
            _ => 0f,
        };
    }
}
