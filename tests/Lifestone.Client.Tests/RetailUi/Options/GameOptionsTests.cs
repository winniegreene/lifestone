using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.RetailUi.Options;

/// <summary>
/// M5 6 u1 (D32), the character options on the wire: a change sends both words (GameAction 0x01A1) built from the model, an option with no client effect is named once as a TODO and not ghosted,
/// every reader sees the change at once, and a send the tick refuses ends the change.
/// </summary>
public sealed class GameOptionsTests
{
    private const uint Word1 = 0x50C4A54A;
    private const uint Word2 = 0x00948700;

    private static (WorldActionRig Rig, GameOptions Options) Rig(bool described = true)
    {
        var r = new WorldActionRig();
        if (described)
        {
            r.H.Describe(new PlayerDescriptionMsg { Options1 = Word1, Options2 = Word2 });
        }

        return (r, new GameOptions(new View(r.H), r.H.Log));
    }

    private static PlayerOptionDef Option(string name) => PlayerOptionTable.All.First(o => o.Name == name);

    [Fact]
    public void Apply_sends_both_words_once_with_every_changed_bit()
    {
        var (r, options) = Rig();
        var cloak = Option("ShowCloak"); // word 2, default on
        var run = Option("ToggleRun"); // word 1
        var run1 = options.Get(run);

        Assert.True(options.Apply([(cloak, false), (run, !run1), (Option("ShowHelm"), options.Get(Option("ShowHelm")))])); // the last is no change
        r.Tick();

        var block = Assert.Single(r.H.Net.OptionBlocks);
        Assert.Equal(run1 ? Word1 & ~run.Mask : Word1 | run.Mask, block.Options1);
        Assert.Equal(Word2 & ~cloak.Mask, block.Options2);
        Assert.Equal(2, options.ChangeCount);
        Assert.Equal(1, options.SendCount);
        Assert.Equal((block.Options1, block.Options2), (r.H.State.Self.Qualities.CharacterOptions1, r.H.State.Self.Qualities.CharacterOptions2)); // the model applied them when the tick sent
    }

    [Fact]
    public void An_option_at_its_value_sends_nothing_and_a_change_reads_back_at_once_to_every_reader()
    {
        var (r, options) = Rig();
        var seen = new List<string>();
        options.Changed += o => seen.Add(o.Name + "=" + options.Get(o));

        Assert.True(options.TrySet(PlayerOptionTable.SideBySideVitals, false)); // it is off already
        Assert.Empty(seen);
        Assert.Equal(0, options.SendCount);

        Assert.True(options.TrySet(PlayerOptionTable.SideBySideVitals, true));
        Assert.True(options.SideBySideVitals); // before the tick sends
        Assert.Equal(["SideBySideVitals=True"], seen);
        r.Tick();
        Assert.True(options.SideBySideVitals);
        Assert.Single(r.H.Net.OptionBlocks);
    }

    [Fact]
    public void An_option_with_no_client_effect_is_not_ghosted_and_is_named_once_per_option()
    {
        var (r, options) = Rig();
        var run = Option("ToggleRun");
        var tooltips = Option("ShowTooltips");
        Assert.False(run.Honoured);

        options.TrySet(run, !options.Get(run));
        options.TrySet(run, !options.Get(run));
        options.TrySet(tooltips, !options.Get(tooltips));
        options.TrySet(PlayerOptionTable.SideBySideVitals, true); // has an effect: no TODO
        r.Tick();

        var todos = r.H.Log.Lines.Where(static l => l.Contains("TODO(g6-option-effects)", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, todos.Count);
        Assert.Contains(todos, static l => l.Contains("ToggleRun", StringComparison.Ordinal));
        Assert.Contains(todos, static l => l.Contains("ShowTooltips", StringComparison.Ordinal));
        Assert.DoesNotContain(r.H.Log.Lines, static l => l.Contains("ghosted", StringComparison.Ordinal));
    }

    [Fact]
    public void A_send_the_tick_refuses_ends_the_change_and_tells_the_readers_again()
    {
        var (r, options) = Rig(described: false); // no description: ACE's block cannot be built
        var run = Option("ToggleRun");
        var seen = new List<bool>();
        options.Changed += _ => seen.Add(options.Get(run));

        Assert.True(options.TrySet(run, true));
        Assert.True(options.Get(run));
        r.Tick();

        Assert.False(options.Get(run)); // the model never had the words: the overlay ended
        Assert.Equal([true, false], seen);
        Assert.Empty(r.H.Net.OptionBlocks);
        Assert.Contains(r.H.Log.Lines, static l => l.Contains("the options were not sent", StringComparison.Ordinal));
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }
}
