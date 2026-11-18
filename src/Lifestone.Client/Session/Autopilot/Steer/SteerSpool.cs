using System.Collections.Concurrent;
using System.Text.Json;

namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>
/// The file work the host supplies (the client does no file I/O: <c>ArchitectureTests</c>). Relative paths use forward slashes. Called only on the spool's own thread. A failure is an <see cref="IOException"/> or an
/// <see cref="UnauthorizedAccessException"/>, which the spool retries and, if it lasts, names.
/// </summary>
public interface ISteerDisk
{
    /// <summary>The names of the inbox's finished files (<c>*.json</c>; a <c>.tmp</c> is still being written).</summary>
    IReadOnlyList<string> InboxNames();

    /// <summary>An inbox file's size in bytes.</summary>
    /// <param name="name">The file's name.</param>
    long InboxSize(string name);

    /// <summary>When an inbox file was last written.</summary>
    /// <param name="name">The file's name.</param>
    DateTimeOffset InboxWritten(string name);

    /// <summary>An inbox file's bytes.</summary>
    /// <param name="name">The file's name.</param>
    byte[] ReadInbox(string name);

    /// <summary>Moves an inbox file to <c>taken\</c>.</summary>
    /// <param name="name">The file's name.</param>
    void TakeInbox(string name);

    /// <summary>True when <c>outbox\&lt;id&gt;.json</c> exists.</summary>
    /// <param name="id">The order's id.</param>
    bool HasReport(string id);

    /// <summary>Replaces a file in one step, so a reader sees the old text or the new (a temporary file, then a move over the old one).</summary>
    /// <param name="path">The relative path.</param>
    /// <param name="text">The new text.</param>
    void Replace(string path, string text);

    /// <summary>Adds to the end of a file.</summary>
    /// <param name="path">The relative path.</param>
    /// <param name="text">The text.</param>
    void Append(string path, string text);

    /// <summary>Renames a file aside and never deletes it; nothing happens when it is absent.</summary>
    /// <param name="path">The relative path.</param>
    void SetAside(string path);

    /// <summary>The ids of the reports in <c>outbox\</c>.</summary>
    IReadOnlyList<string> ReportIds();

    /// <summary>A report's text.</summary>
    /// <param name="id">The order's id.</param>
    string ReadReport(string id);
}

/// <summary>One inbox file as the spool read it, handed to the game thread.</summary>
/// <param name="FileName">The inbox file's name.</param>
/// <param name="Id">The order's id from the file's name; null when the name is not of the form.</param>
/// <param name="Order">The order; null when refused or a duplicate.</param>
/// <param name="Refusal">The refusal's name; null when not refused.</param>
/// <param name="Detail">The refusal in words.</param>
/// <param name="Kind">The kind when it could be read.</param>
/// <param name="Duplicate">True when the id already has a report: the earlier report stands and nothing acts.</param>
public readonly record struct SteerInbound(string FileName, string? Id, SteerOrder? Order, string? Refusal, string Detail, string? Kind, bool Duplicate = false);

/// <summary>What the game thread asks the spool to write.</summary>
public enum SteerWriteKind
{
    /// <summary>Replace a file in one step.</summary>
    Replace,

    /// <summary>Add to the end of a file.</summary>
    Append,

    /// <summary>Move an answered inbox file to <c>taken\</c>.</summary>
    Take,

    /// <summary>Rename a file aside.</summary>
    SetAside,
}

/// <summary>One write, in the order it was asked for.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Target">The relative path, or for <see cref="SteerWriteKind.Take"/> the inbox file's name.</param>
/// <param name="Text">The text; empty for <see cref="SteerWriteKind.Take"/>.</param>
public readonly record struct SteerWrite(SteerWriteKind Kind, string Target, string Text = "");

/// <summary>
/// The file side of the steer desk (the steer contract, section 2): a pump on its own thread reads the inbox every <see cref="PollSeconds"/> and does every write in the order the game thread asked for, so the game thread never touches the disk. Pure over
/// <see cref="ISteerDisk"/>: the host owns the real files and the thread. An answered file stays in the inbox until its report is written, so a crash between the two leaves a file a restart refuses by name.
/// </summary>
/// <param name="disk">The files.</param>
/// <param name="clientStarted">When the client started: an inbox file older than this answers <c>restarted</c>, unread.</param>
public sealed class SteerSpool(ISteerDisk disk, DateTimeOffset clientStarted)
{
    /// <summary>The largest order read, in bytes; a bigger file is refused unread.</summary>
    public const long MaxOrderBytes = 64 * 1024;

    /// <summary>Seconds between two looks at the inbox.</summary>
    public const double PollSeconds = 1;

    /// <summary>How long the files may keep failing before the spool gives up and names why (a reader holding a file delays one write by a moment; a read-only folder never recovers).</summary>
    public const double GiveUpSeconds = 30;

    /// <summary>How many polls one unreadable inbox file gets before it is refused by name.</summary>
    public const int FileTries = 30;

    /// <summary>The live picture's file.</summary>
    public const string StatusFile = "status.json";

    /// <summary>The event stream's file.</summary>
    public const string EventsFile = "events.ndjson";

    private readonly ISteerDisk _disk = disk ?? throw new ArgumentNullException(nameof(disk));
    private readonly ConcurrentQueue<SteerInbound> _inbound = new();
    private readonly ConcurrentQueue<SteerWrite> _writes = new();
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _fileTries = new(StringComparer.Ordinal);
    private SteerWrite? _head;
    private bool _recovered;
    private string? _status;
    private volatile string? _fault;
    private string _lastError = string.Empty;
    private double _nextPoll = double.NegativeInfinity;
    private double _failingSince = double.NaN;

    /// <summary>Why the spool gave up; null while it works. Safe to read from the game thread.</summary>
    public string? Fault => _fault;

    /// <summary>Gives the spool up from outside, when its thread cannot go on; the desk reads it and closes.</summary>
    /// <param name="why">Why.</param>
    public void Fail(string why) => _fault ??= why;

    /// <summary>The next file the pump read (game thread).</summary>
    /// <param name="inbound">The file.</param>
    public bool TryTake(out SteerInbound inbound) => _inbound.TryDequeue(out inbound);

    /// <summary>Asks for a write (game thread). Writes happen in the order asked.</summary>
    /// <param name="write">The write.</param>
    public void Post(SteerWrite write) => _writes.Enqueue(write);

    /// <summary>Offers the live picture (game thread). Only the newest offer is written, so a slow disk never builds a queue of old pictures.</summary>
    /// <param name="json">The picture.</param>
    public void PostStatus(string json) => Interlocked.Exchange(ref _status, json);

    /// <summary>One turn of the pump (the spool's thread): the writes, the picture, and the inbox when a poll is due.</summary>
    /// <param name="now">Seconds on any steady clock.</param>
    public void Step(double now)
    {
        if (_fault is not null)
        {
            return;
        }

        var healthy = _recovered || (_recovered = RecoverUnfinished());
        healthy &= DrainWrites();
        healthy &= WriteStatus();
        if (_recovered && now >= _nextPoll)
        {
            _nextPoll = now + PollSeconds;
            healthy &= Poll();
        }

        if (healthy)
        {
            _failingSince = double.NaN;
        }
        else if (double.IsNaN(_failingSince))
        {
            _failingSince = now;
        }
        else if (now - _failingSince >= GiveUpSeconds)
        {
            _fault = $"the steer folder has failed for {GiveUpSeconds:0} s: {_lastError}";
        }
    }

    /// <summary>After a restart every unfinished order answers <c>restarted</c>: nothing is replayed, and the harness decides again from fresh status.</summary>
    private bool RecoverUnfinished()
    {
        try
        {
            foreach (var id in _disk.ReportIds())
            {
                if (!UnfinishedKind(_disk.ReadReport(id), out var kind))
                {
                    continue;
                }

                _disk.Replace($"outbox/{id}.json", new SteerReport(id, kind, SteerState.Refused, "restarted", "the client restarted before this order ended: decide again from fresh status", clientStarted, 0, null, 0).ToJson());
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _lastError = "recovering the unfinished orders: " + e.GetType().Name;
            return false;
        }
    }

    /// <summary>True when a report's text says its order had not ended. A torn or malformed report says nothing, and never stops the others being answered.</summary>
    private static bool UnfinishedKind(string text, out string kind)
    {
        kind = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String || state.GetString() != SteerState.Running)
            {
                return false;
            }

            if (root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
            {
                kind = k.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool DrainWrites()
    {
        while (_head is not null || _writes.TryDequeue(out var next) && (_head = next) is not null)
        {
            var write = _head.Value;
            if (!Attempt(write))
            {
                return false; // kept at the head: a later write must never overtake it
            }

            _head = null;
            if (write.Kind == SteerWriteKind.Take)
            {
                _inFlight.Remove(write.Target);
            }
        }

        return true;
    }

    private bool Attempt(SteerWrite write)
    {
        try
        {
            switch (write.Kind)
            {
                case SteerWriteKind.Replace:
                    _disk.Replace(write.Target, write.Text);
                    break;
                case SteerWriteKind.Append:
                    _disk.Append(write.Target, write.Text);
                    break;
                case SteerWriteKind.SetAside:
                    _disk.SetAside(write.Target);
                    break;
                default:
                    _disk.TakeInbox(write.Target);
                    break;
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _lastError = $"{write.Kind} {write.Target}: {e.GetType().Name}";
            return false;
        }
    }

    private bool WriteStatus()
    {
        if (Interlocked.Exchange(ref _status, null) is not { } json)
        {
            return true;
        }

        if (Attempt(new SteerWrite(SteerWriteKind.Replace, StatusFile, json)))
        {
            return true;
        }

        Interlocked.CompareExchange(ref _status, json, null); // tried again next turn, unless a newer picture came first
        return false;
    }

    private bool Poll()
    {
        IReadOnlyList<string> names;
        try
        {
            names = _disk.InboxNames();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _lastError = "listing the inbox: " + e.GetType().Name;
            return false;
        }

        foreach (var name in names.OrderBy(Sequence).ThenBy(n => n, StringComparer.Ordinal))
        {
            if (!_inFlight.Contains(name) && Examine(name) is { } inbound)
            {
                _inFlight.Add(name);
                _inbound.Enqueue(inbound);
            }
        }

        return true;
    }

    private static long Sequence(string name) => SteerFileName.TryParse(name, out var sequence, out _) ? sequence : long.MaxValue;

    private SteerInbound? Examine(string name)
    {
        if (!SteerFileName.TryParse(name, out _, out var id))
        {
            return Refuse(name, null, "bad-file-name", "the name is not <seq>-<id>.json");
        }

        try
        {
            if (_seen.Contains(id) || _disk.HasReport(id))
            {
                return new SteerInbound(name, id, null, null, "the id already has a report", null, Duplicate: true);
            }

            var inbound = Read(name, id);
            _seen.Add(id);
            _fileTries.Remove(name);
            return inbound;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var tries = _fileTries.GetValueOrDefault(name) + 1;
            _fileTries[name] = tries;
            if (tries < FileTries)
            {
                return null; // most often the harness still has it open: the next poll tries again
            }

            _fileTries.Remove(name);
            _seen.Add(id);
            return Refuse(name, id, "unreadable", $"{e.GetType().Name} on {tries} polls");
        }
    }

    private SteerInbound Read(string name, string id)
    {
        var size = _disk.InboxSize(name);
        if (size > MaxOrderBytes)
        {
            return Refuse(name, id, "too-large", $"{size} bytes, over the {MaxOrderBytes} limit; not read");
        }

        if (_disk.InboxWritten(name) < clientStarted)
        {
            return Refuse(name, id, "restarted", "the file is older than this client: decide again from fresh status");
        }

        var parsed = SteerOrder.Parse(_disk.ReadInbox(name));
        if (parsed.Order is not { } order)
        {
            return new SteerInbound(name, id, null, parsed.Refusal, parsed.Detail, parsed.Kind);
        }

        return order.Id == id ? new SteerInbound(name, id, order, null, string.Empty, order.Kind) : new SteerInbound(name, id, null, "id-mismatch", $"the file's name says '{id}', its id says '{order.Id}'", order.Kind);
    }

    private static SteerInbound Refuse(string name, string? id, string refusal, string detail) => new(name, id, null, refusal, detail, null);
}
