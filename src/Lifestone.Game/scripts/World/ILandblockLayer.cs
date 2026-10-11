using System.Collections;
using Godot;
using Lifestone.Data.Terrain;

namespace Lifestone.Game.World;

/// <summary>
/// One kind of content streamed per landblock (terrain, scenery, statics and buildings, EnvCells, ...). Layers
/// register with <see cref="WorldRoot.RegisterLayer"/>; the <see cref="LandblockStreamer"/> drives them.
/// </summary>
/// <remarks>
/// <para><b>Lifecycle</b> for each landblock the streaming planner wants:</para>
/// <list type="number">
/// <item><see cref="PrepareAsync"/> on a worker thread (the <c>GameServices.Workers</c> pool): read dats, decode,
/// build meshes and textures as plain data or Godot <c>Image</c>s / arrays. No scene-tree access and no
/// RenderingServer resources. Observe the token: the landblock may go stale before you finish. Return null when the
/// landblock has nothing for this layer. Layers prepare in parallel; one layer's failure does not stop the others.</item>
/// <item><see cref="Attach"/> on the main thread, inside the frame-budgeted main-thread queue (nearest landblocks
/// first): create resources and nodes under <paramref name="landblockRoot"/> (a <c>Node3D</c> placed at the
/// landblock's south-west corner in render space, so children use landblock-local AC coordinates converted with
/// <c>AcToGodot</c>). Cheap work can be done in the call (return null). Big work is returned as <b>steps</b>: write
/// the attach as an iterator (<c>yield return null;</c> after each chunk of about a millisecond or less, e.g. one part
/// or one texture upload). The streamer owns the sequence: it runs one step per budget slot, interleaved with other
/// main-thread work, and stops it (disposing the enumerator, so <c>finally</c> blocks run) if the landblock is cancelled
/// or unloaded.</item>
/// <item><see cref="Detach"/> on the main thread when the landblock streams out, or when an attach is abandoned
/// part-way (the layer counts as attached from the moment <see cref="Attach"/> is called): release shared resources
/// (materials, meshes) and forget per-landblock state. Record what each step acquired as you go, so a Detach after
/// any step releases exactly that. The streamer frees the landblock root and everything under it afterwards, so
/// freeing nodes is optional.</item>
/// </list>
/// <para><b>Prepared objects</b> that implement <see cref="IDisposable"/> are disposed by the streamer once the layer's
/// attach has finished (or failed), or when the landblock is dropped before attaching; do not keep them past
/// <see cref="Attach"/>'s last step.</para>
/// <para><b>Floating origin.</b> Landblock roots are moved by the world on a rebase; nodes under them need nothing.
/// Nodes placed elsewhere must listen to <see cref="WorldRoot.OriginShifted"/>.</para>
/// </remarks>
public interface ILandblockLayer
{
    /// <summary>Short name for logs and the perf overlay.</summary>
    string Name { get; }

    /// <summary>Worker thread: prepares the landblock's content, or returns null when there is none.</summary>
    Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation);

    /// <summary>
    /// Main thread, within the frame budget: creates nodes and resources from the prepared object. Returns null when
    /// the work is done, or a sequence of further steps (each <c>MoveNext</c> is one budgeted step).
    /// </summary>
    IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot);

    /// <summary>Main thread: the landblock is leaving (or its attach was abandoned); release what <see cref="Attach"/> acquired so far.</summary>
    void Detach(LandblockCoord coord);
}
