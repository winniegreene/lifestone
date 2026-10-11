using System.Collections.Concurrent;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;

namespace Lifestone.Client.Objects;

/// <summary>One part of an <see cref="ObjectModel"/>: its LOD chain and its rest pose at object scale 1.</summary>
/// <param name="Index">Part index in the Setup.</param>
/// <param name="Chain">The part's drawable GfxObj per LOD level (<c>CPhysicsPart::LoadGfxObjArray</c>).</param>
/// <param name="RestOrigin">Part origin relative to the object frame, at object scale 1.</param>
/// <param name="RestOrientation">Part orientation relative to the object frame (normalised).</param>
/// <param name="DefaultScale"><c>CSetup::default_scale[i]</c>, the part's <c>gfxobj_scale</c> at object scale 1.</param>
/// <param name="SortCenter">
/// <c>CGfxObj::sort_center</c> of the chain's level-0 GfxObj (<c>CPhysicsPart::gfxobj[0]</c>), in part-local space;
/// retail measures the per-part LOD distance to this point (<c>CPhysicsPart::UpdateViewerDistance</c>).
/// </param>
public sealed record ObjectModelPart(int Index, LodChain Chain, Vector3 RestOrigin, Quaternion RestOrientation, Vector3 DefaultScale, Vector3 SortCenter);

/// <summary>
/// A model (Setup <c>0x02</c> or bare GfxObj <c>0x01</c>) resolved for drawing: every part with its LOD chain and its
/// rest pose, or the reason retail would refuse to create the object.
/// </summary>
/// <remarks>
/// <para><b>Rest pose.</b> <c>CPartArray::SetPlacementFrame(0x65)</c>: the Resting placement if present, else Default
/// (<see cref="ModelAssembly.ComputeRestPose"/>). Objects with a default animation or motion table are posed from it at
/// run time (<see cref="LivingStatic"/>, docs/objects.md §1.7).</para>
/// <para><b>Object scale</b> <c>s</c> (scenery's <c>CPhysicsObj::SetScaleStatic</c>) is applied per instance:
/// part origin <c>× s</c> and part scale <c>DefaultScale × s</c> (<c>CPartArray::SetScaleInternal</c>,
/// <see cref="ModelAssembly.ComputePose"/>).</para>
/// <para><b>Loadability.</b> Retail <c>CPartArray::InitParts</c> creates the object only if every part loads
/// (<c>CPhysicsPart::makePhysicsPart</c>: a GfxObj id whose LOD chain is loadable, <see cref="LodChain.IsLoadable"/>);
/// otherwise <c>CPhysicsObj::makeObject</c> fails and the static or scenery object is not created.</para>
/// </remarks>
public sealed class ObjectModel
{
    internal ObjectModel(uint id, ModelAssembly? assembly, IReadOnlyList<ObjectModelPart> parts, string? problem)
    {
        Id = id;
        Assembly = assembly;
        Parts = parts;
        Problem = problem;
    }

    /// <summary>The Setup or GfxObj id.</summary>
    public uint Id { get; }

    /// <summary>The decoded assembly, or null when the record does not exist.</summary>
    public ModelAssembly? Assembly { get; }

    /// <summary>The parts (empty when <see cref="IsLoadable"/> is false).</summary>
    public IReadOnlyList<ObjectModelPart> Parts { get; }

    /// <summary>Why retail would not create the object, or null.</summary>
    public string? Problem { get; }

    /// <summary>True when retail's <c>CPhysicsObj::makeObject</c> succeeds for this id.</summary>
    public bool IsLoadable => Problem is null;

    /// <summary>
    /// The Setup's default PhysicsScript (<c>CSetup::default_script_id</c>), which <c>CPhysicsObj::InitDefaults</c> plays
    /// when the object is created (torch flames, chimney smoke, ...); 0 for none and for bare GfxObjs.
    /// </summary>
    public uint DefaultScriptId => Assembly?.DefaultScriptId ?? 0;

    /// <summary>The Setup's default animation (<c>CPartArray::InitDefaults</c>, looped at 30 fps); 0 for none.</summary>
    public uint DefaultAnimationId => Assembly?.DefaultAnimationId ?? 0;

    /// <summary>The Setup's default motion table (<c>CPhysicsObj::InitDefaults</c> → <c>SetMotionTableID</c>); 0 for none.</summary>
    public uint DefaultMotionTableId => Assembly?.DefaultMotionTableId ?? 0;

    /// <summary>The Setup's default PhysicsScriptTable (<c>CPhysicsObj::InitDefaults</c>); 0 for none.</summary>
    public uint DefaultScriptTableId => Assembly?.DefaultScriptTableId ?? 0;

    /// <summary>
    /// A static retail animates every physics tick (<c>CPhysicsObj::InitDefaults</c> sets <c>HAS_DEFAULT_ANIM</c> and/or
    /// <c>HAS_DEFAULT_SCRIPT</c> and registers it with <c>CPhysics::AddStaticAnimatingObject</c>; docs/objects.md §1.7).
    /// </summary>
    public bool IsAnimatingStatic => IsLoadable && (DefaultScriptId != 0 || DefaultAnimationId != 0);
}

/// <summary>
/// Thread-safe cache of <see cref="ObjectModel"/>s and <see cref="LodChain"/>s over the dats, shared by the scenery and
/// static-object paths (each id is resolved once). Bounded by the dat: 5,935 Setups and 15,318 GfxObjs at most.
/// </summary>
public sealed class ObjectModelLibrary
{
    private readonly DatSet _dats;
    private readonly GfxObjMeshCache _meshes;
    private readonly Func<uint, ModelAssembly?> _assemblies;
    private readonly ConcurrentDictionary<uint, Lazy<ObjectModel>> _models = new();
    private readonly ConcurrentDictionary<uint, Lazy<LodChain>> _chains = new();

    /// <summary>Creates a library.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="meshes">The shared GfxObj mesh cache (sort centres, mesh geometry).</param>
    /// <param name="assemblies">Optional shared assembly loader (e.g. the game's Setup cache); default loads from the dats.</param>
    public ObjectModelLibrary(DatSet dats, GfxObjMeshCache meshes, Func<uint, ModelAssembly?>? assemblies = null)
    {
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _assemblies = assemblies ?? (id => ModelAssembly.Load(dats, id, meshes));
    }

    /// <summary>The dats this library reads.</summary>
    public DatSet Dats => _dats;

    /// <summary>The GfxObj mesh cache.</summary>
    public GfxObjMeshCache Meshes => _meshes;

    /// <summary>Models resolved so far.</summary>
    public int ModelCount => _models.Count;

    /// <summary>The LOD chain of a part GfxObj (cached).</summary>
    public LodChain GetChain(uint gfxObjId) =>
        _chains.GetOrAdd(gfxObjId, id => new Lazy<LodChain>(() => LodChain.Load(_dats, id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>The resolved model of a Setup or GfxObj id (cached). Never null: check <see cref="ObjectModel.IsLoadable"/>.</summary>
    public ObjectModel GetModel(uint modelId) =>
        _models.GetOrAdd(modelId, id => new Lazy<ObjectModel>(() => Resolve(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private ObjectModel Resolve(uint id)
    {
        if (id >> 24 is not (0x01 or 0x02))
        {
            return new ObjectModel(id, null, [], $"0x{id:X8} is neither a GfxObj nor a Setup");
        }

        ModelAssembly? assembly;
        try
        {
            assembly = _assemblies(id);
        }
        catch (InvalidDataException ex)
        {
            return new ObjectModel(id, null, [], $"0x{id:X8} does not decode: {ex.Message}");
        }

        if (assembly is null)
        {
            return new ObjectModel(id, null, [], $"0x{id:X8} does not exist");
        }

        PartPose[] poses;
        try
        {
            poses = assembly.ComputeRestPose();
        }
        catch (InvalidDataException ex)
        {
            // None on end-of-retail data; retail would leave the parts at the identity frame.
            return new ObjectModel(id, assembly, [], $"TODO(objects): {ex.Message}");
        }

        var parts = new ObjectModelPart[assembly.Parts.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = assembly.Parts[i];
            if (!part.IsGfxObj)
            {
                return new ObjectModel(id, assembly, [], $"part {i} 0x{part.GfxObjId:X8} is not a GfxObj (CPhysicsPart::makePhysicsPart fails)");
            }

            var chain = GetChain(part.GfxObjId);
            if (!chain.IsLoadable || _meshes.Get(chain.NearestGfxObjId) is not { } nearest)
            {
                return new ObjectModel(id, assembly, [], $"part {i} 0x{part.GfxObjId:X8} does not load (CPartArray::InitParts fails)");
            }

            var pose = poses[i];
            parts[i] = new ObjectModelPart(i, chain, pose.Origin, Quaternion.Normalize(pose.Orientation), pose.Scale, nearest.SortCenter);
        }

        return new ObjectModel(id, assembly, parts, null);
    }
}
