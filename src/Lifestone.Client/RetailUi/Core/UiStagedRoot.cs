using System.Diagnostics;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// A mode root made over several frames: the same tree <see cref="UiElementManager.CreateRootElement(uint, uint)"/> creates (each element resolved, made, its children made and
/// attached, then initialized), in the same order, but interruptible between two elements, so a host can spend a few milliseconds a frame on a tree that costs tens in one go.
/// The root joins the manager only when every element is made (so nothing is painted or ticked half-built), and then the tail of <c>CreateRootElement</c> runs.
/// </summary>
/// <remarks>
/// Lifestone's own way to spread a cost (retail built its roots in one call; <c>UIElementManager::CreateRootElement</c>, 11.6096 0x0045D100). The shell's warm-up uses it
/// (docs/retail-ui.md section 21.10); the real screens still build in one call. Game thread only, like the manager.
/// </remarks>
public sealed class UiStagedRoot
{
    private readonly IEnumerator<bool> _work;
    private readonly Func<long> _timestamp;

    internal UiStagedRoot(Func<UiStagedRoot, IEnumerator<bool>> work, Func<long>? timestamp = null)
    {
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _work = work(this);
    }

    /// <summary>The root, once <see cref="Done"/> (null when the layout, the element or its description could not be made, which is logged).</summary>
    public UiElement? Root { get; internal set; }

    /// <summary>True when the root is made (or could not be made).</summary>
    public bool Done { get; private set; }

    /// <summary>The units of work done so far (an element made and attached, an initialize or a post-init).</summary>
    public int Units { get; private set; }

    /// <summary>The phase the staged work last announced (report-only).</summary>
    public string Phase { get; internal set; } = "start";

    /// <summary>The phase the longest unit ended in (report-only).</summary>
    public string LongestUnitPhase { get; private set; } = string.Empty;

    /// <summary>The longest single unit so far, in milliseconds (a host budgets by it: a unit cannot be split).</summary>
    public double LongestUnitMilliseconds { get; private set; }

    /// <summary>
    /// Does units of work until <paramref name="deadlineTimestamp"/> (a <see cref="Stopwatch.GetTimestamp"/> value) has passed; at least one unit runs per call.
    /// </summary>
    /// <returns>True once the root is done.</returns>
    public bool Step(long deadlineTimestamp)
    {
        while (!Done)
        {
            var gcBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            var started = _timestamp();
            var more = _work.MoveNext();
            var ended = _timestamp();
            var unitMilliseconds = (ended - started) * 1000.0 / Stopwatch.Frequency;
            if (unitMilliseconds > LongestUnitMilliseconds)
            {
                LongestUnitMilliseconds = unitMilliseconds;
                LongestUnitPhase = Phase + ((GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2)) != gcBefore ? " (a GC ran in it)" : string.Empty); // report-only: what the slowest unit was doing (the unit ended at the phase set before its yield)
            }

            Units++;
            if (!more)
            {
                Done = true;
                break;
            }

            if (ended >= deadlineTimestamp)
            {
                break;
            }
        }

        return Done;
    }
}

public sealed partial class UiElementManager
{
    /// <summary>
    /// Begins the same root <see cref="CreateRootElement(uint, uint)"/> would create, to be made over frames with <see cref="UiStagedRoot.Step"/>. Null, with an error logged, when the enum,
    /// the layout or the element is missing (as <see cref="CreateRootElement(uint, uint)"/>).
    /// </summary>
    public UiStagedRoot? BeginRootElement(uint layoutEnum, uint elementId)
    {
        if (!Services.Descs.TryResolveLayoutEnum(layoutEnum, out var layoutId))
        {
            Services.Log.Error(Category, $"UILAYOUT enum 0x{layoutEnum:X8} does not resolve; root 0x{elementId:X8} is not created (as retail)");
            return null;
        }

        return BeginRootElementByDataId(layoutId, elementId);
    }

    /// <summary>The staged form of <see cref="CreateRootElementByDataId"/>: a mode root from a layout DID, made over frames.</summary>
    public UiStagedRoot? BeginRootElementByDataId(uint layoutId, uint elementId) =>
        TryGetTopLevelDesc(layoutId, elementId, out var layout, out var desc) ? new UiStagedRoot(staged => StageRoot(staged, layout, desc)) : null;

    private IEnumerator<bool> StageRoot(UiStagedRoot staged, UiLayout layout, UiElementDesc desc)
    {
        var made = new StagedElement();
        var work = StageElement(staged, layout, desc, made);
        while (work.MoveNext())
        {
            yield return true;
        }

        var element = made.Element;
        if (element is null)
        {
            yield break;
        }

        staged.Phase = "root made";
        yield return true;
        element.Initialize();
        element.SetShouldOwnObject(true);
        element.SetIsRootElement(true);
        staged.Phase = "root initialized";
        yield return true;
        staged.Phase = "root post-init";
        element.PostInit();
        _roots.Add(element);
        if (element.IsShown)
        {
            element.NotifyCreatedShown();
        }

        Input.RequestMouseHitTest();
        staged.Root = element;
    }

    // CreateElementRecursiveFromPartialDesc / FromFullDesc, one yield per element made and per child attached.
    private IEnumerator<bool> StageElement(UiStagedRoot staged, UiLayout layout, UiElementDesc desc, StagedElement result)
    {
        var resolved = Services.Descs.InqFullDesc(desc);
        if (!resolved.Succeeded)
        {
            LogOnce($"resolve:{desc.SourceLayout:X8}/{desc.ElementId:X8}", LogSeverity.Error, $"{resolved.Failure}; it is not created (as retail)");
            yield break;
        }

        var full = resolved.Full!;
        var element = Registry.Create(new UiElementInit(this, layout, full), Services.Log);
        if (element is null)
        {
            yield break;
        }

        _elements.Add(element);
        staged.Phase = $"made {element.GetType().Name} {desc.ElementId:X8}"; // report-only: names the element whose construction was the unit
        yield return true;
        foreach (var childDesc in full.Children.Values)
        {
            var made = new StagedElement();
            var work = StageElement(staged, layout, childDesc, made);
            while (work.MoveNext())
            {
                yield return true;
            }

            if (made.Element is { } child)
            {
                child.Initialize();
                child.SetParent(element);
                staged.Phase = $"attached {child.GetType().Name} to {element.GetType().Name} {desc.ElementId:X8}";
                yield return true;
            }
        }

        result.Element = element;
    }

    private sealed class StagedElement
    {
        public UiElement? Element { get; set; }
    }
}
