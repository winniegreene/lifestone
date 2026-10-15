using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// A page of character creation (retail's <c>gmCGHeritagePage</c> and its five siblings, each a field of the master page that
/// <see cref="CharGenMainScreen"/> shows one at a time). The page listens to the messages of its own elements (retail's pages override
/// <c>ListenToElementMessage</c>; the main screen hears the same messages after it) and shows the screen's <see cref="CharacterCreation.CharGenState"/>.
/// </summary>
public abstract class CharGenPage : IUiElementListener, IDisposable
{
    private bool _disposed;

    /// <summary>The page over <paramref name="root"/>, one of the master page's six fields; it listens to everything under it.</summary>
    protected CharGenPage(CharGenMainScreen main, UiElement root, string name)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(root);
        Main = main;
        Root = root;
        Name = name;
        root.RegisterForElementMessages(this);
    }

    /// <summary>The screen the page belongs to (its state, dialogs and navigation).</summary>
    protected CharGenMainScreen Main { get; }

    /// <summary>The element runtime.</summary>
    protected UiElementManager Manager => Main.ElementManager;

    /// <summary>The page's element.</summary>
    public UiElement Root { get; }

    /// <summary>The page's retail class name, for logs.</summary>
    public string Name { get; }

    /// <summary>True once <see cref="Dispose"/> ran.</summary>
    protected bool IsDisposed => _disposed;

    /// <summary>The line the screen shows as a placeholder note while this page is the one on screen, or null for a page that is built.</summary>
    public virtual string? PlaceholderText => null;

    /// <summary>The page's <c>Update</c>: its elements show the screen's character again (the page has just opened, or the character changed under it).</summary>
    public abstract void Update();

    /// <summary>The page's messages; the default takes none.</summary>
    protected virtual UiListenResult OnElementMessage(in UiElementMessage message) => UiListenResult.Default;

    /// <inheritdoc/>
    UiListenResult IUiElementListener.ListenToElementMessage(in UiElementMessage message) => _disposed ? UiListenResult.Default : OnElementMessage(message);

    /// <summary>A string of the pregame table (<c>StringInfo::SetStringIDandTableEnum</c> with table enum 0x10000002) by its token.</summary>
    protected static UiStringRef Pregame(string token) => new(StringHash.Tokenize(token), GmUiFlow.PregameTableId);

    /// <summary>The element <paramref name="elementId"/> under the page as a <typeparamref name="T"/>, or null with a loud error (the layout is not what the page expects).</summary>
    protected T? Find<T>(uint elementId)
        where T : UiElement
    {
        var found = Root.GetChildRecursive(elementId);
        if (found is T typed)
        {
            return typed;
        }

        Manager.LogOnce(
            $"chargen:{Name}:{elementId:X8}",
            LogSeverity.Error,
            found is null
                ? $"{Name}: element 0x{elementId:X8} is missing from the layout; what it does is not shown"
                : $"{Name}: element 0x{elementId:X8} is a {found.GetType().Name}, not the {typeof(T).Name} the page expects; what it does is not shown");
        return null;
    }

    /// <summary>The page stops listening; its elements go with the screen's root.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Root.UnregisterForElementMessages(this);
        OnDisposing();
    }

    /// <summary>The page is going: drop what it registered outside its own tree.</summary>
    protected virtual void OnDisposing()
    {
    }
}
