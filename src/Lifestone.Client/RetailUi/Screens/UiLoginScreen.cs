using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// What the login screens need from the application around them (retail's <c>Device::Done</c> and the reconnect that owner decision
/// D43 adds to the disconnected screen). The Godot host implements it; tests fake it.
/// </summary>
public interface IUiLoginHost
{
    /// <summary>
    /// Closes the application (<c>Device::Done</c>, which the epilogue screen calls). The screens end a live session first
    /// (<see cref="IActionApi.Quit"/>); the host only has to close its window.
    /// </summary>
    void Exit();

    /// <summary>
    /// The disconnected screen's button (D43): starts again with a new connection for the same account, the way
    /// <c>GameSession.Reconnect</c> does. False when the host cannot (the screen then exits, as retail's does).
    /// </summary>
    bool Reconnect();
}

/// <summary>
/// What the login screens share (made by <see cref="RetailUiScreenHost"/>): the element runtime, the session they read and act on
/// (state only through <see cref="IGameSessionView.State"/> and the events, every action through <see cref="IGameSessionView.Actions"/>),
/// the flow they queue modes with, and the application around them.
/// </summary>
/// <param name="Manager">The element runtime.</param>
/// <param name="Session">The session view (a live session, or the offline stand-in).</param>
/// <param name="Flow">The mode flow (<see cref="UiFlow.QueueUIMode"/>).</param>
/// <param name="Host">The application (exit, reconnect).</param>
/// <param name="RetailDisconnect">True for retail's disconnected button (exit, <c>--retail-disconnect</c>) instead of D43's reconnect.</param>
/// <param name="CharGen">What character creation needs (the CharGen data and the dice), or null for a host without the dats' CharGen data: the mode then shows its stand-in.</param>
/// <param name="LiveGame">True in play mode: a live session can enter the world, kept for the application; the game screen is <see cref="GamePlayScreen"/> either way.</param>
/// <param name="StageShell">True when the game screen builds its shell over frames (<see cref="GamePlayScreen.BuildBudgetMilliseconds"/> a frame, <see cref="UiStagedRoot"/>) instead of in one call; the live host sets it, so Enter never costs one big frame (docs/session.md section 27).</param>
/// <param name="Notices">Where the game's text notices go (the host's router); the game screen registers its spew box and chat windows on it, or null for a host without a router (tests).</param>
public sealed record UiLoginContext(UiElementManager Manager, IGameSessionView Session, UiFlow Flow, IUiLoginHost Host, bool RetailDisconnect = false, CharGenSetup? CharGen = null, bool LiveGame = false, UiNoticeRouter? Notices = null, bool StageShell = false);

/// <summary>
/// What character creation (<see cref="CharGenMainScreen"/>) takes from the application: the CharGen data of the user's dats, a source of dice
/// for the screen's <see cref="CharGenState"/>, and whether the account has the Throne of Destiny expansion.
/// </summary>
/// <param name="Catalog">The CharGen record, the SkillTable and the weenie classes.</param>
/// <param name="NewRandom">Makes the dice of one character creation (a seeded one in a scripted run).</param>
/// <param name="HasThroneOfDestiny">
/// <c>CPlayerSystem::AccountHasThroneOfDestiny</c>: whether Viamontian is offered (and the random heritage may be 1 to 4 and Sanamar the start area).
/// Every account of the end of retail has the expansion, so it defaults to true; the session does not carry it (no offline or live source of it exists in
/// the state contract), and a false here makes the Viamontian button show retail's "Throne of Destiny required" message.
/// </param>
/// <param name="View3D">
/// What the 3D character and the colour wheel read (<see cref="Cg3DViewData"/>), or null for a host without it: the appearance page then has no colour
/// swatches and the appearance and summary pages no character (each says so on screen and in the log).
/// </param>
/// <param name="ViewHost">Where the 3D character is drawn (the Godot layer), or null for none: the viewport holes then stay empty and say so.</param>
public sealed record CharGenSetup(
    CharGenCatalog Catalog,
    Func<ICharGenRandom> NewRandom,
    bool HasThroneOfDestiny = true,
    Cg3DViewData? View3D = null,
    ICg3DViewHost? ViewHost = null);

/// <summary>
/// Where character creation's 3D characters are drawn: the host binds each page's <see cref="Cg3DViewModel"/> to its viewport element's hole
/// (<c>CharGenViewportBinder</c> in the Godot layer) and draws it there until the attachment is disposed.
/// </summary>
public interface ICg3DViewHost
{
    /// <summary>Draws <paramref name="model"/> in viewport element <paramref name="viewportElementId"/>'s hole; disposing the result takes it away.</summary>
    IDisposable Attach(uint viewportElementId, Cg3DViewModel model);
}

/// <summary>
/// A login screen (retail's <c>UIMainFramework</c> subclasses <c>gmDataPatchUI</c>, <c>gmDisconnectedUI</c> and
/// <c>gmCharacterManagementUI</c>): the 800x600 display forced and the mode's root element made from its layout, the screen
/// listening to the messages of everything under the root, the root shown with the screen and removed with it
/// (<c>UIMainFramework::CreateAndAddRootElement</c> and <c>RemoveRootElement</c>). A root that does not exist is logged as a loud
/// error and the screen then shows nothing.
/// </summary>
public abstract class UiLoginScreen : IUiScreen, IUiElementListener
{
    private bool _disposed;

    /// <summary>The screen of <paramref name="rootRef"/> over <paramref name="context"/>.</summary>
    protected UiLoginScreen(UiLoginContext context, UiRootRef rootRef, string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Name = name;
        Manager.ForceDisplayResolution(true);
        Root = Manager.CreateRootElement(rootRef.LayoutEnum, rootRef.ElementId);
        if (Root is null)
        {
            Log.Error(UiElementManager.Category, $"{name}: root {rootRef} does not exist in the layouts; the screen shows nothing");
        }
        else
        {
            Root.RegisterForElementMessages(this);
        }
    }

    /// <summary>What the screens share.</summary>
    protected UiLoginContext Context { get; }

    /// <summary>The screen's retail class name, for logs.</summary>
    protected string Name { get; }

    /// <summary>The element runtime.</summary>
    protected UiElementManager Manager => Context.Manager;

    /// <summary>The session the screen reads (<see cref="IGameSessionView.State"/>) and acts on (<see cref="IGameSessionView.Actions"/>).</summary>
    protected IGameSessionView Session => Context.Session;

    /// <summary>Where the screen logs.</summary>
    protected ILogSink Log => Manager.Services.Log;

    /// <summary>The mode root, or null when its layout lacks it.</summary>
    public UiElement? Root { get; }

    /// <summary>True once <see cref="Dispose"/> ran.</summary>
    protected bool IsDisposed => _disposed;

    /// <inheritdoc/>
    public virtual void DisplayError(in UiStringRef text) =>
        Manager.LogOnce($"screen:{Name}:error", LogSeverity.Info, $"{Name} was queued with an error ({text}) but shows none");

    /// <inheritdoc/>
    public virtual void Update()
    {
    }

    /// <inheritdoc/>
    public void Show(bool visible) => Root?.SetVisible(visible);

    /// <inheritdoc/>
    UiListenResult IUiElementListener.ListenToElementMessage(in UiElementMessage message) => _disposed ? UiListenResult.Default : OnElementMessage(message);

    /// <summary>
    /// <c>ListenToElementMessage</c> of the screen: hears the messages of its elements (the root's and every descendant's). Retail
    /// ends each case with the framework's own handling, which takes nothing here, so a message is never consumed.
    /// </summary>
    protected virtual UiListenResult OnElementMessage(in UiElementMessage message) => UiListenResult.Default;

    /// <summary>Removes the screen's root and everything the screen registered; safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnDisposing();
        if (Root is not null)
        {
            Root.UnregisterForElementMessages(this);
            Manager.DeleteElement(Root);
        }
    }

    /// <summary>The screen is going: unsubscribe, close its dialogs, unregister its input map. The root is still there.</summary>
    protected virtual void OnDisposing()
    {
    }

    /// <summary>The child <paramref name="elementId"/> of the root as a <typeparamref name="T"/>, or null with a loud error (the layout is not what the screen expects).</summary>
    protected T? Find<T>(uint elementId)
        where T : UiElement
    {
        var found = Root?.GetChildRecursive(elementId);
        if (found is T typed)
        {
            return typed;
        }

        if (Root is not null)
        {
            Manager.LogOnce(
                $"screen:{Name}:{elementId:X8}",
                LogSeverity.Error,
                found is null
                    ? $"{Name}: element 0x{elementId:X8} is missing from the layout; what it does is not shown"
                    : $"{Name}: element 0x{elementId:X8} is a {found.GetType().Name}, not the {typeof(T).Name} the screen expects; what it does is not shown");
        }

        return null;
    }

    /// <summary>A string of the pregame table (<c>StringInfo::SetStringIDandTableEnum</c> with table enum 0x10000002) by its token.</summary>
    protected static UiStringRef Pregame(string token) => new(StringHash.Tokenize(token), GmUiFlow.PregameTableId);

    /// <summary>A text element's text, or the empty string.</summary>
    protected static string TextOf(UiElement? element) => element is UiTextElement text ? text.Text : string.Empty;
}
