using System.Diagnostics;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// M3 3.1 lane fc, item 3: prepares what a war spell's bolt needs at start-up, so a bolt draws on the frame its CreateObject is applied and adds no compile, decode or upload to the play path. The projectile Setups are the
/// ACE world extract's (<see cref="AceWorldExtract.ProjectilesBySpell"/>, the table the stand-in's <c>StandInVisit.ProjectileSetup</c> reads and <c>SpellTableHost.ProjectileSetup</c> serves): 13 spells over 9 Setups.
/// </summary>
/// <remarks>
/// <para><b>What is warmed.</b> A worker reads each Setup (<see cref="ModelAssembly.Load"/>), collects its part GfxObjs, its default script and the spells' PhysicsScriptTables, and hands them to the main thread, which (1) asks
/// <see cref="ServerObjectMeshes.Warm"/> for every part under both lighting rules (outdoor, and in a cell: the Black Spawn Den is a dungeon), so the worker pool decodes the GfxObj, resolves its surfaces and textures and the
/// main-thread queue builds the mesh, its materials and its blended twin inside its frame budget; the warmed meshes are pinned (a mesh unasked for 600 frames is otherwise released) and (2) queues the particle meshes of the
/// tables' scripts and of the default scripts (<see cref="ServerObjectParticles.PrepareScriptTable"/>, <see cref="ServerObjectParticles.PrepareScripts"/>: the worker resolves the models, the main thread queues their meshes a few a
/// frame). The shaders and pipelines of the moving-object rules are warmed by <see cref="ServerObjectsWarmUp"/>; this adds no compile of its own.</para>
/// <para><b>What it does not warm.</b> The batch node of a mesh (<c>DynamicObjectBatch</c>, a MultiMesh) is made on the first frame its mesh is drawn, and the particle renderer's own first draw of a bolt's emitters is the
/// world's (the living statics share it); the play-checks report the pipeline compilations in a bolt's window beside its wait (0 measured, Flame Bolt and Shock Wave).</para>
/// </remarks>
public sealed class ProjectileWarmUp
{
    private const string LogCategory = "serverobjects";
    private const int PollEveryFrames = 10;
    private const int GiveUpAfterFrames = 60 * 120;

    private readonly GameServices _services;
    private readonly ServerObjectMeshes _outdoor;
    private readonly ServerObjectMeshes _indoor;
    private readonly long _started = Stopwatch.GetTimestamp();
    private uint[] _gfxObjs = [];
    private string[] _outdoorKeys = [];
    private string[] _indoorKeys = [];
    private int _setups;
    private int _tables;
    private bool _handedOver;
    private bool _logged;
    private int _frames;
    private Task<Plan>? _plan;

    /// <summary>The worker's result: what to warm, with each GfxObj's mesh key under both lighting rules (made once here, so a poll builds no string; package 2 review, finding 12).</summary>
    private sealed record Plan(uint[] Setups, uint[] GfxObjs, string[] OutdoorKeys, string[] IndoorKeys, uint[] Tables, uint[] DefaultScripts);

    public ProjectileWarmUp(GameServices services, ServerObjectMeshes outdoor, ServerObjectMeshes indoor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(outdoor);
        ArgumentNullException.ThrowIfNull(indoor);
        _services = services;
        _outdoor = outdoor;
        _indoor = indoor;
    }

    /// <summary>True once every warmed mesh is attached (and the line was logged).</summary>
    public bool Done => _logged;

    /// <summary>The distinct projectile Setups of the war spells (the ACE extract's table), sorted.</summary>
    public static uint[] ProjectileSetups() => AceWorldExtract.Load().Projectiles.Select(p => p.SetupId).Distinct().Order().ToArray();

    /// <summary>The distinct PhysicsScriptTables of the war spells' projectiles, sorted (the rings have none).</summary>
    public static uint[] ProjectileTables() => AceWorldExtract.Load().Projectiles.Select(p => p.PhysicsEffectTableId ?? 0).Where(id => id != 0).Distinct().Order().ToArray();

    /// <summary>Starts the worker (once). Safe to call before the dats are read; with no dats it logs and does nothing.</summary>
    public void Start()
    {
        if (_plan is not null)
        {
            return;
        }

        if (_services.Dats is not { } dats)
        {
            _logged = true;
            Log.Warn(LogCategory, "the projectile warm-up needs the dats; a bolt's meshes are prepared when it first appears");
            return;
        }

        var meshes = _services.GfxObjMeshes;
        var (outdoor, indoor) = (_outdoor, _indoor);
        _plan = _services.Workers.RunSync(_ =>
        {
            var setups = ProjectileSetups();
            var gfx = new SortedSet<uint>();
            var scripts = new SortedSet<uint>();
            foreach (var id in setups)
            {
                if (ModelAssembly.Load(dats, id, meshes) is not { } setup)
                {
                    Log.Warn(LogCategory, "a war spell's projectile Setup does not load", ("setup", $"0x{id:X8}"));
                    continue;
                }

                foreach (var part in setup.Parts)
                {
                    // Every level of the part's degrade chain: the layer asks for the level the viewer's distance picks (AvatarFrame.Add), not for the Setup's own GfxObj.
                    foreach (var level in LodChain.Load(dats, part.GfxObjId).GfxObjIds)
                    {
                        gfx.Add(level);
                    }
                }

                if (setup.DefaultScriptId != 0)
                {
                    scripts.Add(setup.DefaultScriptId);
                }
            }

            gfx.Remove(0);
            var ids = gfx.ToArray();
            var outdoorKeys = new string[ids.Length];
            var indoorKeys = new string[ids.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                outdoorKeys[i] = outdoor.WarmKey(ids[i]);
                indoorKeys[i] = indoor.WarmKey(ids[i]);
            }

            return new Plan(setups, ids, outdoorKeys, indoorKeys, ProjectileTables(), scripts.ToArray());
        });
    }

    /// <summary>
    /// Main thread, every frame (cheap: a flag test once done; a poll every <see cref="PollEveryFrames"/> frames while meshes are queued, with the plan's keys, so it builds no string). Hands the worker's plan over,
    /// then logs the warm-up once, when every mesh is attached.
    /// </summary>
    public void Poll()
    {
        if (_logged || _plan is null)
        {
            return;
        }

        _frames++;
        if (!_handedOver)
        {
            if (!_plan.IsCompleted)
            {
                return;
            }

            if (!_plan.IsCompletedSuccessfully)
            {
                _logged = true;
                Log.Warn(LogCategory, "TODO(m3-bolt-warm-up): the projectile Setups could not be read; a bolt's meshes are prepared when it first appears", ("problem", _plan.Exception?.GetBaseException().Message ?? "cancelled"));
                return;
            }

            var plan = _plan.Result;
            _gfxObjs = plan.GfxObjs;
            _outdoorKeys = plan.OutdoorKeys;
            _indoorKeys = plan.IndoorKeys;
            _setups = plan.Setups.Length;
            _tables = plan.Tables.Length;
            foreach (var table in plan.Tables)
            {
                ServerObjectParticles.PrepareScriptTable(_services, table);
            }

            ServerObjectParticles.PrepareScripts(_services, plan.DefaultScripts);
            for (var i = 0; i < _gfxObjs.Length; i++)
            {
                _outdoor.Warm(_gfxObjs[i], _outdoorKeys[i]);
                _indoor.Warm(_gfxObjs[i], _indoorKeys[i]);
            }

            _handedOver = true;
            return;
        }

        if (_frames % PollEveryFrames != 0 && _frames < GiveUpAfterFrames)
        {
            return;
        }

        var ready = true;
        for (var i = 0; i < _gfxObjs.Length; i++)
        {
            ready &= _outdoor.Warm(_gfxObjs[i], _outdoorKeys[i]) & _indoor.Warm(_gfxObjs[i], _indoorKeys[i]); // the plan's keys: no string built in the poll
        }

        // 3.1-R9: the line also waits for the particle models of the tables and default scripts to be resolved and queued for their meshes (a few a frame, PumpPending), which came after it before.
        var backlog = ServerObjectParticles.PrepareBacklog;
        if ((!ready || backlog > 0) && _frames < GiveUpAfterFrames)
        {
            return;
        }

        _logged = true;
        var ms = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        if (ready)
        {
            Log.Info(LogCategory, "projectile warm-up done: the war spells' bolts are prepared (the part meshes are attached, the particle models are queued; the particle library's workers finish those meshes)", ("setups", _setups), ("tables", _tables), ("gfxobjs", _gfxObjs.Length), ("meshes", _gfxObjs.Length * 2), ("particles", Lifestone.Game.World.Objects.LivingStaticsSystem.Instance is { Enabled: true }), ("particle_models_queued", ServerObjectParticles.PumpedModelCount), ("particle_backlog", backlog), ("ms", ms));
        }
        else
        {
            Log.Warn(LogCategory, "TODO(m3-bolt-warm-up): the projectile meshes were not all ready after the warm-up's patience; the rest are prepared when a bolt first appears", ("setups", _setups), ("gfxobjs", _gfxObjs.Length), ("ms", ms));
        }
    }
}
