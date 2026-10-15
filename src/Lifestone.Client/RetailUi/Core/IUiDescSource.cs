using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// Where the element runtime finds its layouts: retail's <c>LayoutDesc</c> records (<see cref="UiLayouts"/>, the
/// language dat) or, in tests, synthetic layouts. The runtime only reads; the source is shared by every thread.
/// </summary>
public interface IUiDescSource
{
    /// <summary>
    /// Resolves a UILAYOUT enum (group 5) to a LayoutDesc DID, as <c>UIMainFramework::CreateAndAddRootElement</c>
    /// (11.4186 0x006A0C00, 11.6096 0x006A1A20) does; false where retail gets INVALID_DID and creates nothing.
    /// </summary>
    bool TryResolveLayoutEnum(uint layoutEnum, out uint layoutId);

    /// <summary>The layout <paramref name="layoutId"/>; false when the source does not hold it.</summary>
    /// <exception cref="InvalidDataException">The layout exists but does not convert.</exception>
    bool TryGetLayout(uint layoutId, [NotNullWhen(true)] out UiLayout? layout);

    /// <summary>
    /// <c>LayoutDesc::InqFullDesc</c> (11.4186 0x0069A520, 11.6096 0x0069B400): the full desc of
    /// <paramref name="local"/>, or why there is none.
    /// </summary>
    UiResolveResult InqFullDesc(UiElementDesc local);
}

/// <summary>Builds <see cref="IUiDescSource"/>s.</summary>
public static class UiDescSource
{
    /// <summary>The retail layouts of <paramref name="layouts"/> (the language dat).</summary>
    public static IUiDescSource From(UiLayouts layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return new LayoutsSource(layouts);
    }

    private sealed class LayoutsSource(UiLayouts layouts) : IUiDescSource
    {
        public bool TryResolveLayoutEnum(uint layoutEnum, out uint layoutId) => layouts.TryResolveLayoutEnum(layoutEnum, out layoutId);

        public bool TryGetLayout(uint layoutId, [NotNullWhen(true)] out UiLayout? layout) => layouts.TryGetLayout(layoutId, out layout);

        public UiResolveResult InqFullDesc(UiElementDesc local) => layouts.InqFullDesc(local);
    }
}
