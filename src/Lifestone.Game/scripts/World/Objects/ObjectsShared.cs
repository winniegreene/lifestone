using Lifestone.Client.Objects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// The engine-agnostic object caches every object path shares (the landscape objects, the statics inside EnvCells, the
/// spell viewer's Holtburg): one <see cref="ObjectModelLibrary"/> (models, LOD chains, rest poses: the one posing path,
/// docs/objects.md §1.3) and one set of <see cref="LivingStaticSources"/> (scripts, emitters, animations). Thread-safe;
/// created on first use for the process's <see cref="GameServices"/>.
/// </summary>
public static class ObjectsShared
{
    private static readonly object Gate = new();
    private static GameServices? _owner;
    private static ObjectModelLibrary? _library;
    private static LivingStaticSources? _sources;

    /// <summary>The shared model library (Setups through the game's <see cref="SetupCache"/>).</summary>
    public static ObjectModelLibrary Library(GameServices services)
    {
        lock (Gate)
        {
            Own(services);
            return _library ??= new ObjectModelLibrary(services.Dats!, services.GfxObjMeshes, services.Setups.Get);
        }
    }

    /// <summary>The shared scripts, emitters and animations of living statics.</summary>
    public static LivingStaticSources Sources(GameServices services)
    {
        lock (Gate)
        {
            Own(services);
            return _sources ??= new LivingStaticSources(new DatEffectSource(services.Dats!), new DatAnimationSource(services.Dats!));
        }
    }

    private static void Own(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Dats is null)
        {
            throw new InvalidOperationException("The dats are not open.");
        }

        if (!ReferenceEquals(_owner, services))
        {
            _owner = services;
            _library = null;
            _sources = null;
        }
    }
}
