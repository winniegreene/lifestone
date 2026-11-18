using System.Reflection;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.Chat;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>The command registry (2026-11-17): the one parser, every command's declared help, preconditions and report, the stubs' named TODOs, and the chat's way in.</summary>
public sealed class CommandRegistryTests
{
    [Theory]
    [InlineData("/trip", "trip", "", CommandThen.Resume)]
    [InlineData("  TRIP  ", "trip", "", CommandThen.Resume)]
    [InlineData("/quest tusk-male then idle", "quest", "tusk-male", CommandThen.Idle)]
    [InlineData("rebuff bot then resume", "rebuff", "bot", CommandThen.Resume)]
    [InlineData("/quest-step\tatlan-wand 3", "quest-step", "atlan-wand 3", CommandThen.Resume)]
    public void A_line_parses_to_its_name_arguments_and_what_she_does_after(string text, string name, string args, CommandThen then)
    {
        Assert.True(CommandLine.TryParse(text, out var request, out var error));
        Assert.Null(error);
        Assert.Equal((name, args, then), (request!.Name, request.ArgText, request.Then));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("/", "empty")]
    [InlineData("trip then", "bad-then")]
    [InlineData("trip then nap", "bad-then")]
    [InlineData("quest then idle x", "bad-then")]
    public void A_bad_line_is_refused_by_name(string text, string named)
    {
        Assert.False(CommandLine.TryParse(text, out var request, out var error));
        Assert.Null(request);
        Assert.Equal(named, error);
        Assert.False(CommandLine.TryParse(new string('a', CommandLine.MaxLength + 1), out _, out var tooLong));
        Assert.Equal("too-long", tooLong);
    }

    [Fact]
    public void Every_command_declares_its_usage_help_preconditions_and_report_and_every_stub_names_its_todo()
    {
        var all = CommandRegistry.Default.All;
        Assert.Equal(all.Count, all.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (var spec in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Help), spec.Name);
            Assert.False(string.IsNullOrWhiteSpace(spec.Before), spec.Name);
            Assert.False(string.IsNullOrWhiteSpace(spec.After), spec.Name);
            Assert.StartsWith("/" + spec.Name, spec.Usage, StringComparison.Ordinal);
            Assert.True(spec.MinArgs <= spec.MaxArgs, spec.Name);
            Assert.Equal(spec.Built, spec.Action != CommandAction.Stub);
            if (!spec.Built)
            {
                Assert.Equal("m6-cmd-" + spec.Name, spec.Todo);
                Assert.Equal($"not-built: TODO(m6-cmd-{spec.Name})", spec.StubRefusal);
            }

            Assert.Same(spec, CommandRegistry.Default.Find(spec.Name.ToUpperInvariant()));
        }

        // The owner's four (2026-11-17), the controls and the desk's answers are built; the roadmap's groups are all listed.
        foreach (var name in new[] { "trip", "rebuff", "quest", "ladder", "sell", "restock", "learn", "home", "den", "quest-step", "stop", "pause", "resume", "help", "status", "queue", "cancel", "clear" })
        {
            Assert.True(CommandRegistry.Default.Find(name)!.Built, name);
        }

        Assert.Equal(["logistics", "buffs", "vitals", "travel", "hunting", "quests", "gear", "items", "villa", "control", "desk"], all.Select(s => s.Group).Distinct());
        Assert.Same(CommandRegistry.Default.Find("home"), CommandRegistry.Default.Find("recall"));
        Assert.Null(CommandRegistry.Default.Find("tell"));
    }

    private sealed class Controls : IAutopilotChatControls
    {
        public List<(string Name, string Line)> Commands { get; } = [];

        public string Stop() => "stopped";

        public string Auto() => "auto";

        public IReadOnlyList<string>? Command(string name, string line)
        {
            if (CommandRegistry.Default.Find(name) is null)
            {
                return null;
            }

            Commands.Add((name, line));
            return ["Lifestone: order " + name];
        }
    }

    private static ChatCommands Chat(Controls controls, IActionApi actions) => new(actions, () => null, () => false) { Autopilot = controls };

    [Fact]
    public void The_chats_orders_reach_the_autopilot_and_every_other_line_goes_its_usual_way()
    {
        var actions = DispatchProxy.Create<IActionApi, ChatCommandsTests.Recorder>();
        var recorder = (ChatCommandsTests.Recorder)(object)actions;
        var controls = new Controls();
        var chat = Chat(controls, actions);

        foreach (var line in new[] { "/trip", "/rebuff bot", "/quest tusk-male then idle", "/ladder", "/help trip", "/status", "/clear", "/buy scarab" })
        {
            var result = chat.Run(line);
            Assert.True(result.ClientHandled, line);
            Assert.Equal("Lifestone: order " + line.Split(' ')[0][1..], Assert.Single(result.Lines).Text);
        }

        Assert.Equal(["trip", "rebuff", "quest", "ladder", "help", "status", "clear", "buy"], controls.Commands.Select(c => c.Name));
        Assert.Equal("/quest tusk-male then idle", controls.Commands[2].Line);
        Assert.Equal("stopped", Assert.Single(chat.Run("/stop").Lines).Text); // /stop stays the client's own command (the host routes it through the runner)
        Assert.Empty(recorder.Calls);

        chat.Run("/fly away"); // no order: retail's way, the line goes to the server as talk
        Assert.Equal(["@fly away"], Assert.Single(recorder.Calls).Args);

        var none = Chat(new Controls(), actions);
        none.Autopilot = null;
        Assert.Contains("not built yet", Assert.Single(none.Run("/help").Lines).Text, StringComparison.Ordinal); // no autopilot: retail's unbuilt /help, as before
    }
}
