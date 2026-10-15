using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>Whether an image is available.</summary>
public enum UiImageLookup
{
    /// <summary>The image is decoded and returned.</summary>
    Ready,

    /// <summary>The image is being decoded (a worker); ask again later. The compositor keeps the root's dirty area and retries.</summary>
    Pending,

    /// <summary>The image does not exist or cannot be decoded; retail draws its default graphic (<see cref="UiDefaultGraphic"/>).</summary>
    Missing,
}

/// <summary>
/// Where the painter gets image pixels: a RenderSurface DID to a decoded <see cref="UiImage"/>. Retail loads each
/// <c>Graphic</c>'s RenderSurface through <c>DBObj::Get</c> (<c>Graphic::Graphic</c>, 11.4186 0x00693EE0, 11.6096
/// 0x00694E00). <see cref="DatUiImageSource"/> decodes from the dats; a host may decode on workers and answer
/// <see cref="UiImageLookup.Pending"/> meanwhile.
/// </summary>
public interface IUiImageSource
{
    /// <summary>The image <paramref name="did"/>, if it is ready.</summary>
    UiImageLookup TryGetImage(uint did, [NotNullWhen(true)] out UiImage? image);
}

/// <summary>
/// Where an element reports that part of its surface needs repainting (<c>UIObject::AddDirtyRect</c>): the compositor.
/// </summary>
public interface IUiDirtySink
{
    /// <summary>Marks <paramref name="rect"/> (in <paramref name="surfaceOwner"/>'s surface coordinates) dirty.</summary>
    void AddDirtyRect(IUiRegionView surfaceOwner, UiBox rect);
}
