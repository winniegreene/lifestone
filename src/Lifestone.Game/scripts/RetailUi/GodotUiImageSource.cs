using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Scheduling;
using Lifestone.Data.Textures;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail UI's images in the game (<see cref="IUiImageSource"/>): the first request for an image decodes it on a worker
/// (the shared <see cref="WorkerPool"/>) and answers <see cref="UiImageLookup.Pending"/> meanwhile; the compositor keeps the
/// area dirty and paints it once the image is ready (docs/retail-ui.md §5.4). The decode itself is the runtime's, not the
/// engine's: <see cref="DatUiImageSource"/> over the shared <see cref="TextureLoader"/>, with the six JPEG images going through
/// Lifestone's own <see cref="UiJpegDecoder"/>, so what the screen shows is what the tests check byte for byte (Godot's JPEG
/// loader rounds the inverse DCT differently).
/// </summary>
/// <remarks>
/// Safe from any thread; allocation-free once an image was asked for. An image that is already decoded answers at once, on the
/// caller's thread. <see cref="Prefetch"/> starts decodes ahead of the first ask (the screens' hit-test masks, which the
/// compositor's pending check does not see).
/// </remarks>
public sealed class GodotUiImageSource : IUiImageSource
{
    private readonly DatUiImageSource _decoder;
    private readonly WorkerPool? _workers;
    private readonly ILogSink _log;
    private readonly ConcurrentDictionary<uint, Slot> _slots = new();
    private readonly Func<uint, Slot> _newSlot;
    private int _pending;
    private long _decoded;

    /// <summary>
    /// An image source over <paramref name="decoder"/>. With no <paramref name="workers"/> (a check suite, a tool) every image
    /// decodes on the calling thread at the first request and is never pending.
    /// </summary>
    public GodotUiImageSource(DatUiImageSource decoder, WorkerPool? workers, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        _decoder = decoder;
        _workers = workers;
        _log = log ?? NullLogSink.Instance;
        _newSlot = _ => new Slot();
    }

    /// <summary>Images being decoded now or waiting for a worker.</summary>
    public int PendingCount => Volatile.Read(ref _pending);

    /// <summary>Images decoded (or found missing) so far.</summary>
    public long DecodedCount => Interlocked.Read(ref _decoded);

    /// <inheritdoc/>
    public UiImageLookup TryGetImage(uint did, [NotNullWhen(true)] out UiImage? image)
    {
        if (!_slots.TryGetValue(did, out var slot))
        {
            slot = _slots.GetOrAdd(did, _newSlot);
        }

        if (Volatile.Read(ref slot.State) == Slot.Done)
        {
            image = slot.Image;
            return image is null ? UiImageLookup.Missing : UiImageLookup.Ready;
        }

        if (Interlocked.CompareExchange(ref slot.State, Slot.Requested, Slot.New) == Slot.New)
        {
            Start(did, slot);
        }

        if (Volatile.Read(ref slot.State) == Slot.Done)
        {
            image = slot.Image;
            return image is null ? UiImageLookup.Missing : UiImageLookup.Ready;
        }

        image = null;
        return UiImageLookup.Pending;
    }

    /// <summary>Starts decoding the images <paramref name="dids"/> now, so they are ready when the first element asks.</summary>
    public void Prefetch(IEnumerable<uint> dids)
    {
        ArgumentNullException.ThrowIfNull(dids);
        foreach (var did in dids)
        {
            _ = TryGetImage(did, out _);
        }
    }

    private void Start(uint did, Slot slot)
    {
        if (_workers is null)
        {
            Decode(did, slot);
            return;
        }

        Interlocked.Increment(ref _pending);
        _ = _workers.RunSync(
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                Decode(did, slot);
                return true;
            }).ContinueWith(
            task =>
            {
                Interlocked.Decrement(ref _pending);
                if (task is { IsFaulted: true, Exception: { } ex })
                {
                    _log.Error("retailui", $"image 0x{did:X8} could not be decoded on a worker: {ex.GetBaseException().Message}; retail's default graphic stands in (placeholder)");
                    slot.Image = null;
                    Volatile.Write(ref slot.State, Slot.Done);
                }
            },
            TaskScheduler.Default);
    }

    private void Decode(uint did, Slot slot)
    {
        try
        {
            _decoder.TryGetImage(did, out var image);
            slot.Image = image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error("retailui", $"image 0x{did:X8} could not be decoded: {ex.Message}; retail's default graphic stands in (placeholder)");
            slot.Image = null;
        }

        Interlocked.Increment(ref _decoded);
        Volatile.Write(ref slot.State, Slot.Done);
    }

    private sealed class Slot
    {
        public const int New = 0;
        public const int Requested = 1;
        public const int Done = 2;

        public int State;
        public UiImage? Image;
    }
}
