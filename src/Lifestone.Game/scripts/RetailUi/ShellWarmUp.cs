using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// Paints the in-game shell once before it is needed, so its first real paint costs no wait (docs/retail-ui.md section 21): from the first login screen on (the data check, the
/// character list, the "Entering World" dialog), a second element runtime builds the gameplay root at the real window's scale and a second compositor paints it in the layer's 1 ms
/// slices, with the layer's own image source, so the images are decoded and cached, the fonts' glyphs made and the layout's descriptions resolved. Nothing of it is drawn or kept:
/// no tile is uploaded, no sound plays and the root is deleted when it has painted.
/// </summary>
/// <remarks>
/// <para>Without it the first game screen holds the world hidden for the shell's images to decode (about 0.4 s on the development machine, measured by <c>--play-checks</c>).</para>
/// <para><b>No frame pays for it.</b> The build (one call of 45 to 95 ms in the first version, which fell in the live gate's judged window because character management lasts 0.2 s there)
/// is made a few elements at a time (<see cref="UiStagedRoot"/>) within <see cref="BuildBudgetMilliseconds"/> a frame, then the paint takes its 1 ms slices, so a step is about 4 ms
/// whatever the machine. If the login screens end while it runs (Enter comes before it is done) it is dropped without being deleted: the work it did stays cached (images, glyphs,
/// descriptions), the real shell is built as it always was, and the tree is left to the garbage collector.</para>
/// </remarks>
internal sealed class ShellWarmUp
{
    /// <summary>The most a frame spends building the warm-up's tree (a unit of work is one element or one initialize, so a frame may run a little over).</summary>
    public const double BuildBudgetMilliseconds = 4.0;

    /// <summary>The most a frame spends painting the warm-up's surfaces.</summary>
    public const double PaintBudgetMilliseconds = 2.0;

    /// <summary>The frames after which the warm-up gives up (it is then finished like a painted one; the real shell waits for its own paint as before).</summary>
    public const int FrameLimit = 1200;

    private readonly UiElementManager _manager;
    private readonly UiCompositor _compositor;
    private readonly UiStagedRoot? _staged;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _frames;
    private int _buildFrames;

    /// <summary>A warm-up over the layer's services at <paramref name="scale"/>.</summary>
    public ShellWarmUp(UiServices real, UiScale scale)
    {
        var services = new UiServices
        {
            Descs = real.Descs,
            Log = real.Log,
            Random = new UiSeededRandom(1),
            Sound = NullUiSoundSink.Instance,
            Fonts = real.Fonts,
            Strings = real.Strings,
            RetailGlitches = real.RetailGlitches,
            ExamineIcon = real.ExamineIcon,
            ExamineSetDetails = real.ExamineSetDetails,
            Images = real.Images,
            EnumMaps = real.EnumMaps,
            EnumNames = real.EnumNames,
            ViewportHost = new NoViewportHost(),
        };
        _manager = new UiElementManager(services, scale);
        GamePlayScreen.RegisterClasses(_manager.Registry); // the same classes as the real shell (R7)
        _compositor = new UiCompositor(real.Images ?? throw new ArgumentException("the warm-up needs an image source", nameof(real)), real.Log);
        _compositor.Attach(_manager);
        _staged = _manager.BeginRootElement(UiLayoutEnums.GameplayRoot.LayoutEnum, UiLayoutEnums.GameplayRoot.ElementId);
    }

    /// <summary>True once the shell has painted (or could not be made).</summary>
    public bool Done { get; private set; }

    /// <summary>The wall-clock milliseconds the warm-up took, once <see cref="Done"/>.</summary>
    public double Milliseconds { get; private set; }

    /// <summary>The frames it took, once <see cref="Done"/>.</summary>
    public int Frames => _frames;

    /// <summary>The frames that built the tree.</summary>
    public int BuildFrames => _buildFrames;

    /// <summary>The longest single unit of the build so far (an element or an initialize), in milliseconds.</summary>
    public double LongestBuildUnitMilliseconds => _staged?.LongestUnitMilliseconds ?? 0.0;

    /// <summary>The longest step so far (a whole frame of the warm-up), in milliseconds.</summary>
    public double LongestStepMilliseconds { get; private set; }

    /// <summary>One frame: the runtime ticks, the tree is built for at most <see cref="BuildBudgetMilliseconds"/> or the compositor paints for at most <see cref="PaintBudgetMilliseconds"/>.</summary>
    public void Step(double now)
    {
        if (Done)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _frames++;
        _manager.Update(now);
        if (_staged is { Done: false })
        {
            _buildFrames++;
            _staged.Step(start + (long)(BuildBudgetMilliseconds * Stopwatch.Frequency / 1000.0));
        }
        else
        {
            _compositor.Composite(Stopwatch.GetTimestamp() + (long)(PaintBudgetMilliseconds * Stopwatch.Frequency / 1000.0));
        }

        LongestStepMilliseconds = Math.Max(LongestStepMilliseconds, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (_staged is null || (_staged.Done && (_staged.Root is null || (!_compositor.HasPendingWork && _frames > _buildFrames + 1))) || _frames > FrameLimit)
        {
            Done = true;
            Milliseconds = _clock.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>Deletes the painted tree (a warm-up that finished; one that was dropped half-way is left to the garbage collector).</summary>
    public void Finish()
    {
        if (_staged is { Done: true, Root: { } root })
        {
            _manager.DeleteElement(root);
        }
    }

    private sealed class NoViewportHost : IUiViewportHost
    {
        public void Bind(uint viewportElementId, UiBox screenBox, bool visible)
        {
        }
    }
}
