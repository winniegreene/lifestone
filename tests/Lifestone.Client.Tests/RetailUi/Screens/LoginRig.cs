using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>The application around the login screens, recording what they ask of it.</summary>
internal sealed class FakeLoginHost : IUiLoginHost
{
    /// <summary>Times the application was asked to close.</summary>
    public int Exits { get; private set; }

    /// <summary>Times the disconnected screen asked to reconnect.</summary>
    public int Reconnects { get; private set; }

    /// <summary>What <see cref="Reconnect"/> answers.</summary>
    public bool CanReconnect { get; set; } = true;

    /// <summary>Runs on a reconnect (the rig resets its offline session).</summary>
    public Action? OnReconnect { get; set; }

    public void Exit() => Exits++;

    public bool Reconnect()
    {
        Reconnects++;
        OnReconnect?.Invoke();
        return CanReconnect;
    }
}

/// <summary>
/// The real login screens over the user's dats with the retail fonts and strings, an offline stand-in session on a manual clock (the data
/// check lasts 2 seconds; the responder answers after 0.6 s) and a fake application. <see cref="Frame"/> advances the clock, ticks the
/// session, updates the runtime and runs the flow, as the Godot layer does.
/// </summary>
internal sealed class LoginRig : IDisposable
{
    public const double Step = 1.0 / 60.0;

    private readonly DatUiImageSource _images;
    private double _now;

    public LoginRig(DatSetFixture fixture, Action<IInboundMessageHandler>? standIn = null, bool retailDisconnect = false, uint? startMode = null, int window = 800, bool charGen = true, bool throneOfDestiny = true, int seed = 7, UiRetailGlitches glitches = UiRetailGlitches.None, bool view3D = false, ICg3DViewHost? viewHost = null, bool liveGame = false, int? windowHeight = null, bool stageShell = false, Func<IUiImageSource, IUiImageSource>? wrapImages = null, bool examineIcon = false)
    {
        Fixture = fixture;
        Log = new RecordingLogSink();
        Session = GameSession.Offline(
            standIn ?? (h =>
            {
                StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
                StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
            }),
            Time,
            Log);
        _images = new DatUiImageSource(new TextureLoader(fixture.Dats), Log);
        Manager = new UiElementManager(
            new UiServices
            {
                Descs = UiDescSource.From(new UiLayouts(fixture.Dats)),
                Fonts = new UiFontsSource(fixture.Dats, Log),
                Strings = new UiStringsSource(new UiStrings(new UiStringTables(fixture.Dats)), UiLanguageInfo.Load(fixture.Dats)),
                Images = wrapImages is null ? _images : wrapImages(_images),
                EnumMaps = fixture.Dats.EnumMaps,
                EnumNames = new Lifestone.Data.Dat.EnumMapperNames(fixture.Dats),
                Log = Log,
                RetailGlitches = glitches,
                ExamineIcon = examineIcon,
            },
            UiScalePolicy.Choose(window, windowHeight ?? (window * 3 / 4)));
        Manager.Input.Bindings = RetailKeyBindings.Load(fixture.Dats);
        Host.OnReconnect = () => Session.Reconnect();
        Screens = new RetailUiScreenHost(
            Manager,
            Session,
            Host,
            new UiCursors(fixture.Dats).Default,
            retailDisconnect,
            startMode,
            charGen
                ? new CharGenSetup(
                    CharGenCatalog.Load(fixture.Dats),
                    () => new CharGenRandom(seed),
                    throneOfDestiny,
                    view3D ? Cg3DViewData.FromDats(fixture.Dats, new TextureLoader(fixture.Dats), new GfxObjMeshCache(fixture.Dats)) : null,
                    viewHost)
                : null,
            liveGame,
            stageShell);
    }

    public DatSetFixture Fixture { get; }

    public RecordingLogSink Log { get; }

    public ManualTimeProvider Time { get; } = new();

    public GameSession Session { get; }

    public UiElementManager Manager { get; }

    public FakeLoginHost Host { get; } = new();

    public RetailUiScreenHost Screens { get; }

    public IActionApi Actions => Session.Actions;

    public CharacterListState Characters => Session.State.Characters;

    public UiFlow Flow => Screens.Flow;

    /// <summary>One frame: the clock moves, the session ticks, the runtime updates, the flow runs its screen.</summary>
    public void Frame(double seconds = Step)
    {
        Time.Advance(TimeSpan.FromSeconds(seconds));
        _now += seconds;
        Session.Tick(_now);
        Manager.Update(_now);
        Screens.Update();
    }

    /// <summary>Frames for <paramref name="seconds"/> of time.</summary>
    public void Run(double seconds)
    {
        var frames = Math.Max(1, (int)Math.Ceiling(seconds / Step));
        for (var i = 0; i < frames; i++)
        {
            Frame();
        }
    }

    /// <summary>Frames until the flow shows <paramref name="mode"/> (at most <paramref name="maxSeconds"/>).</summary>
    public bool RunUntilMode(uint mode, double maxSeconds = 10)
    {
        for (var t = 0.0; t < maxSeconds; t += Step)
        {
            if (Flow.CurrentMode == mode && Flow.CurrentScreen is not null)
            {
                return true;
            }

            Frame();
        }

        return Flow.CurrentMode == mode;
    }

    /// <summary>Frames until <paramref name="done"/> holds (at most <paramref name="maxSeconds"/>).</summary>
    public bool RunUntil(Func<bool> done, double maxSeconds = 10)
    {
        for (var t = 0.0; t < maxSeconds; t += Step)
        {
            if (done())
            {
                return true;
            }

            Frame();
        }

        return done();
    }

    /// <summary>Gets to character management (the data check runs its 2 seconds).</summary>
    public CharacterManagementScreen ToCharacterManagement()
    {
        Assert.True(RunUntilMode(UiModes.CharacterManagement), "the flow never reached character management");
        Frame();
        return Assert.IsType<CharacterManagementScreen>(Flow.CurrentScreen);
    }

    public T Screen<T>()
        where T : class, IUiScreen => Assert.IsType<T>(Flow.CurrentScreen);

    /// <summary>An element of the current screen's root.</summary>
    public T Element<T>(uint id)
        where T : UiElement
    {
        var root = Assert.IsAssignableFrom<UiLoginScreen>(Flow.CurrentScreen).Root;
        return Assert.IsAssignableFrom<T>(root!.GetChildRecursive(id));
    }

    /// <summary>Clicks the middle of <paramref name="element"/> with the mouse, as the input router sees it.</summary>
    public bool Click(UiElement element, UiMouseButton button = UiMouseButton.Left)
    {
        var box = element.ScreenBox;
        Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        var down = Manager.Input.MouseButton(button, down: true);
        Manager.Input.MouseButton(button, down: false);
        return down;
    }

    /// <summary>The dialog <paramref name="context"/> names, or null.</summary>
    public UiDialog? Dialog(uint context) => Manager.Dialogs.Find(context);

    /// <summary>The text a dialog shows.</summary>
    public string DialogText(uint context) => Assert.IsAssignableFrom<UiTextElement>(Dialog(context)?.GetChildRecursive(UiDialog.TextElement)).Text;

    public void Dispose()
    {
        Screens.Dispose();
        Session.Dispose();
    }
}
