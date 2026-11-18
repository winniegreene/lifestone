using System.IO;
using System.Text.Json;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Steer;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The steer desk on real files and its real thread, offline (<c>--autopilot-steer-dir</c>; the rules are <c>SteerDeskTests</c>): the desk opens and writes the live picture; orders dropped as a harness drops them (a temporary file, then a rename) get their
/// reports, the sheets are written, a torn file and a kind not built are refused by name, the events are appended, and closing says so in a last picture. The dial stays off: nothing here starts it. The folder is <c>cache/avatar/play-check</c> and is left for reading.
/// </summary>
public sealed partial class PlayChecks
{
    private const string SteerCheckFolder = "cache/avatar/play-check";

    private async Task AutopilotSteerChecksAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        var root = Path.Combine(Diagnostics.ScreenshotWriter.RepositoryRoot, SteerCheckFolder);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        var logic = new AutopilotHostLogic(_ => throw new InvalidOperationException("this check never starts the dial"), GodotLogSink.Instance, new AutopilotOptions());
        var host = AutopilotHost.InstallWith(_main, _host, ui, logic, new AutopilotHostOptions { SteerDirectory = SteerCheckFolder });
        var opened = await WaitFor(() => host.Desk is { Open: true } && TryReadSteer(root, SteerSpool.StatusFile) is not null, 20.0);
        Check("the-steer-desk-opens-and-writes-the-live-picture", opened, $"desk_open={host.Desk?.Open} closed_why={host.Desk?.ClosedWhy ?? "none"}");
        if (opened)
        {
            await SteerOrdersAsync(session, root);
        }

        logic.Dispose();
        _main.RemoveChild(host); // _ExitTree closes the desk and waits for the spool's last writes
        host.Free();
        if (ui?.Screens?.Flow.CurrentScreen is GamePlayScreen { Chat: { } chat, Hotkeys: { } hotkeys, ToolsUi: { } panel, Inspector: { } inspector })
        {
            chat.Autopilot = null;
            hotkeys.AutopilotKeyHint = null;
            panel.ToggleAutopilot = null;
            inspector.Autopilot = null;
        }

        var last = TryReadSteer(root, SteerSpool.StatusFile);
        Check("closing-the-steer-desk-says-so-in-the-last-picture", last is not null && !JsonDocument.Parse(last).RootElement.GetProperty("desk").GetProperty("open").GetBoolean(), $"status={(last is null ? "unread" : "read")}");
    }

    private async Task SteerOrdersAsync(IGameSessionView session, string root)
    {
        var guid = SteerJson.Guid(session.State.Self.Guid);
        var at = SteerJson.Utc(session.Clock.Time.GetUtcNow());
        var guard = $"\"guard\":{{\"character_guid\":\"{guid}\"}},";
        string Order(string id, string kind, string args, string g) => $"{{\"v\":1,\"id\":\"{id}\",\"kind\":\"{kind}\",\"by\":\"check\",\"issued_utc\":\"{at}\",\"expires_s\":300,{g}\"args\":{{{args}}}}}";

        DropSteer(root, "1-ping.json", Order("ping", "ping", string.Empty, string.Empty));
        var seq = 2;
        foreach (var section in SteerPicture.Sections)
        {
            DropSteer(root, $"{seq++}-sheet-{section}.json", Order("sheet-" + section, "sheet", $"\"section\":\"{section}\"", guard));
        }

        DropSteer(root, $"{seq++}-torn.json", "{\"v\":1,\"id\":");
        DropSteer(root, $"{seq++}-trip.json", Order("trip", "trip", string.Empty, guard));
        DropSteer(root, $"{seq++}-resume.json", Order("resume", SteerKinds.Resume, string.Empty, guard));
        DropSteer(root, $"{seq++}-cancel.json", Order("cancel", SteerKinds.Cancel, "\"target\":\"nope\"", guard));
        DropSteer(root, $"{seq}-stale.json", Order("stale", "sheet", "\"section\":\"skills\"", "\"guard\":{\"character_guid\":\"0x00000001\"},"));

        var answered = await WaitFor(() => TryReadSteer(root, "outbox/stale.json") is not null, 15.0);
        Check("the-steer-orders-are-answered-from-real-files", answered, $"outbox={(Directory.Exists(Path.Combine(root, "outbox")) ? Directory.GetFiles(Path.Combine(root, "outbox")).Length : -1)}");
        if (!answered)
        {
            return;
        }

        CheckSteerReport(root, "ping", "done", "pong");
        foreach (var section in SteerPicture.Sections)
        {
            CheckSteerReport(root, "sheet-" + section, "done", "sheet:" + section);
            var sheet = TryReadSteer(root, $"sheets/{section}.json");
            Check("the-steer-sheet-is-written:" + section, sheet is not null && JsonDocument.Parse(sheet).RootElement.GetProperty("section").GetString() == section, $"chars={sheet?.Length ?? -1}");
        }

        CheckSteerReport(root, "torn", "refused", "bad-json");
        CheckSteerReport(root, "trip", "refused", "not-built");
        CheckSteerReport(root, "resume", "refused", "wrong-mode");
        CheckSteerReport(root, "cancel", "refused", "unknown-order");
        CheckSteerReport(root, "stale", "refused", "guard-character");
        var emptied = await WaitFor(() => Directory.GetFiles(Path.Combine(root, "inbox")).Length == 0, 5.0);
        Check("the-steer-inbox-is-emptied-into-taken", emptied && Directory.GetFiles(Path.Combine(root, "taken")).Length == seq, $"inbox={Directory.GetFiles(Path.Combine(root, "inbox")).Length} taken={Directory.GetFiles(Path.Combine(root, "taken")).Length} sent={seq}");
        var events = TryReadSteer(root, SteerSpool.EventsFile);
        Check("the-steer-events-are-appended", events is not null && events.Contains("\"kind\":\"order\"", StringComparison.Ordinal), $"chars={events?.Length ?? -1}");
    }

    private void CheckSteerReport(string root, string id, string state, string name)
    {
        var text = TryReadSteer(root, $"outbox/{id}.json");
        var report = text is null ? default : JsonDocument.Parse(text).RootElement;
        var (gotState, gotName) = text is null ? (null, null) : (report.GetProperty("state").GetString(), report.GetProperty("name").GetString());
        Check("the-steer-report-reads:" + id, gotState == state && gotName == name, $"state={gotState ?? "none"} name={gotName ?? "none"}");
    }

    /// <summary>Drops an order as a harness does: a temporary file, then a rename.</summary>
    private static void DropSteer(string root, string name, string text)
    {
        var target = Path.Combine(root, "inbox", name);
        File.WriteAllText(target + ".tmp", text);
        File.Move(target + ".tmp", target);
    }

    /// <summary>A file's text, or null when it is absent or the writer has it for a moment.</summary>
    private static string? TryReadSteer(string root, string path)
    {
        try
        {
            return File.ReadAllText(Path.Combine(root, path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
