using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The intro (retail's <c>gmIntroUI</c>, which plays the opening movies and then starts character management): skipped in M2 (D38).
/// The screen makes no element; its first frame says so and starts character management.
/// </summary>
public sealed class IntroSkippedScreen(UiLoginContext context) : IUiScreen
{
    private bool _started;

    /// <inheritdoc/>
    public void DisplayError(in UiStringRef text)
    {
    }

    /// <inheritdoc/>
    public void Update()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        context.Manager.Services.Log.Info(UiElementManager.Category, "intro movies are skipped in M2 (D38): going to character management");
        context.Flow.QueueUIMode(UiModes.CharacterManagement);
    }

    /// <inheritdoc/>
    public void Show(bool visible)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// The epilogue (retail's <c>gmEpilogueUI</c>, layout 0x10000037 root 0x10000399: the closing movie, which ends the game when it ends or
/// a key action starts): every way out of the game queues it. A live session is ended first (<c>CPlayerSystem::LogOffCharacter</c>
/// in the constructor; here <see cref="IActionApi.Quit"/>), then the application closes (<c>Device::Done</c>). The movie is skipped (the
/// media machine skips movies, TODO(retailui-runtime)), so the epilogue ends the application at once.
/// </summary>
public sealed class EpilogueScreen : IUiScreen
{
    private readonly UiLoginContext _context;
    private bool _exited;

    /// <summary>The epilogue over <paramref name="context"/>: ends a live session now.</summary>
    public EpilogueScreen(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (!context.Session.State.IsOffline)
        {
            context.Session.Actions.Quit(ActionSource.Ui);
        }
    }

    /// <summary>True once the application was asked to close.</summary>
    public bool Exited => _exited;

    /// <inheritdoc/>
    public void DisplayError(in UiStringRef text)
    {
    }

    /// <inheritdoc/>
    public void Update()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        _context.Manager.Services.Log.Info(UiElementManager.Category, "epilogue: the closing movie is skipped (M2) and the application closes");
        _context.Host.Exit();
    }

    /// <inheritdoc/>
    public void Show(bool visible)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
