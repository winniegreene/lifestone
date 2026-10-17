using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// What the offline stand-in answers in the world when a host opts in (<see cref="StandInSessions.AnswerWorld"/>; the play-mode checks, m2-plan G6 W11):
/// a server-mod <c>place</c> request (its reply line, then the teleport: F751 and the new position), a Use of a portal object of the visit (UseDone, then the
/// teleport) and a log off (the phases back to the character list and F658). Everything goes through the session's own reducer, so the events are the ones a
/// live session publishes, <see cref="DelaySeconds"/> later on the session clock. Nothing here is ACE's or retail's; it is what the checks need a server to say.
/// </summary>
/// <remarks>Only an offline session's reducer has one; a live session never reaches it. Game thread only.</remarks>
internal sealed partial class StandInWorld(GameStateReducer reducer, StandInVisit visit, double delaySeconds)
{
    private readonly GameStateReducer _reducer = reducer;
    private readonly StandInVisit _visit = visit;
    private TeleportCatalog? _catalog;
    private long _xpTotal = PlaceholderExperience;
    private readonly HashSet<uint> _purged = [];

    /// <summary>The experience the stand-in character starts with (a placeholder; the stand-in has no experience of its own).</summary>
    internal const long PlaceholderExperience = 1_000_000;

    /// <summary>How long each answer takes, in session-clock seconds.</summary>
    internal double DelaySeconds { get; } = delaySeconds;

    /// <summary>Teleports the stand-in has sent for a request (a tool's place or a portal's Use).</summary>
    internal int Teleports { get; private set; }

    /// <summary>The actions the world part answers.</summary>
    internal static bool Answers(Contracts.ActionKind kind) =>
        kind is Contracts.ActionKind.ToolPlace or Contracts.ActionKind.ToolWhereAmI or Contracts.ActionKind.ToolAddXp or Contracts.ActionKind.ToolStatus or Contracts.ActionKind.ToolCharPurge
            or Contracts.ActionKind.ToolSpell or Contracts.ActionKind.ToolItem
            or Contracts.ActionKind.Use or Contracts.ActionKind.QueryHealth or Contracts.ActionKind.LogOff or Contracts.ActionKind.Talk
            or Contracts.ActionKind.RaiseAttribute or Contracts.ActionKind.RaiseVital or Contracts.ActionKind.RaiseSkill or Contracts.ActionKind.TrainSkill
            or Contracts.ActionKind.CastSpell or Contracts.ActionKind.SetCombatMode or Contracts.ActionKind.Wield or Contracts.ActionKind.Unwield or Contracts.ActionKind.MoveToPack
            or Contracts.ActionKind.MergeStack or Contracts.ActionKind.Suicide or Contracts.ActionKind.AddSpellFavorite;

    private OutboundTicket Ticket() => new(0, _reducer.Clock.Time.GetTimestamp());

    private void Later(Action<IAcSessionSink> step) => _reducer.StandIn!.At(_reducer.Clock.Now + DelaySeconds, step);

    /// <summary>
    /// A Talk line. Plain speech is heard back as the player's own (a HearSpeech from the player, as ACE echoes it: the line the Talk completes on); a server-mod
    /// request (<c>@lifestone ...</c>): <c>place</c>, <c>where</c>, <c>xp</c>, <c>char purge</c>, <c>spell learn</c>, <c>item</c> and <c>status</c> answer, every other verb gets an error reply (the stand-in does not do that); any other '@' command is refused.
    /// </summary>
    internal OutboundTicket Talk(ReadOnlySpan<char> text)
    {
        var said = text.ToString();
        const string prefix = "@" + ToolsWire.Command + " ";
        if (!said.StartsWith(prefix, StringComparison.Ordinal))
        {
            if (said.StartsWith('@'))
            {
                throw new NotSupportedException("the offline stand-in has no server commands; only the server mod's requests are answered");
            }

            var self = _reducer.State.Self;
            Later(sink => sink.OnSpeech(Context(sink), new SpeechMsg(said, self.Name ?? "You", self.Guid, 2, null)));
            return Ticket();
        }

        if (!ToolsWire.TryParseRequest(said[prefix.Length..], out var request, out var error))
        {
            Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Failure(null, ToolVerbs.Status, error), error.Message)));
            return Ticket();
        }

        switch (request)
        {
            case PlaceLocRequest loc:
                var at = loc.At;
                Place(request.Id, ToolVerbs.PlaceLoc, new PlaceData("loc", null, ToolsWire.FormatCell(at.Cell), at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz), at.Cell, new Vector3(at.X, at.Y, at.Z));
                break;
            case PlacePoiRequest poi:
                _catalog ??= TeleportCatalog.FromAce();
                if (_catalog.Find(poi.Name) is { } found)
                {
                    var p = found.Location.Position;
                    Place(request.Id, ToolVerbs.PlacePoi, new PlaceData("poi", poi.Name, ToolsWire.FormatCell(p.CellId), p.Local.X, p.Local.Y, p.Local.Z, 1f, 0f, 0f, 0f), p.CellId, p.Local);
                    break;
                }

                Refuse(request, "not-found: the stand-in has no point of interest of that name");
                break;
            case WhereRequest:
                // G7: the stand-in's answer to where: the session's own position, standing as the server's (a placeholder; so the agreement check is trivially met).
                Where(request);
                break;
            case XpRequest xp:
                // G7: a placeholder experience total that adds what is asked (no level table, no cap).
                Xp(request.Id, xp.Amount);
                break;
            case CharPurgeRequest purge:
                // The stand-in's purge: the server mod's rules over the stand-in's character list (a placeholder: the counts are not a database's).
                CharPurge(purge);
                break;
            case SpellLearnRequest spell:
                SpellLearn(spell);
                break;
            case ItemRequest item:
                Item(item);
                break;
            case StatusRequest:
                Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Status, PlaceholderStatus), PlaceholderStatus.Describe())));
                break;
            default:
                Refuse(request, "failed: the stand-in only answers place, where, xp, char purge, spell learn, item and status");
                break;
        }

        return Ticket();
    }

    /// <summary>The status the stand-in reports: a Player account on a mod that answers (the stand-in's own placeholder values).</summary>
    private static StatusData PlaceholderStatus { get; } = new(1, "stand-in", new string('0', 40), new string('0', 64), "127.0.0.1", 0, false, "Player", "stand-in", "Stand-in", "Open", [ToolVerbs.Status, ToolVerbs.Where, ToolVerbs.Xp, ToolVerbs.PlacePoi, ToolVerbs.PlaceLoc, ToolVerbs.Char + " " + ToolVerbs.CharPurge, ToolVerbs.Spell + " " + ToolVerbs.SpellLearn, ToolVerbs.Item]);

    private void Where(ToolRequest request)
    {
        var id = request.Id;
        var self = _reducer.State.Self;
        if (self.Position is not { } at)
        {
            Refuse(request, "failed: TODO(g7-standin-where): the stand-in has no position for the player to report yet");
            return;
        }

        var o = at.Frame.Origin;
        var q = at.Frame.Orientation;
        var data = new WhereData(ToolsWire.FormatCell(at.CellId), o.X, o.Y, o.Z, q.W, q.X, q.Y, q.Z, "0x" + (at.CellId | 0xFFFFu).ToString("X8", System.Globalization.CultureInfo.InvariantCulture), ToolsWire.FormatCell(at.CellId), false, self.Stamps.ForcePosition, self.Stamps.Teleport);
        Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Where, data), data.Describe())));
    }

    private void Xp(string? id, long amount)
    {
        _xpTotal += amount;
        var data = new XpData(amount, amount, _xpTotal, _xpTotal, 1);
        Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Xp, data), data.Describe())));
    }

    /// <summary>The characters the stand-in has purged; its log off lists the others only.</summary>
    internal IReadOnlySet<uint> Purged => _purged;

    /// <summary>The counts the stand-in answers a purge with (placeholders: the stand-in has no database; a character, its biota and no possessions).</summary>
    internal const int PlaceholderPurgeCharacters = 1;

    /// <summary>See <see cref="PlaceholderPurgeCharacters"/>.</summary>
    internal const int PlaceholderPurgeBiotas = 1;

    private void CharPurge(CharPurgeRequest request)
    {
        var self = _reducer.State.Self;
        var characters = _reducer.State.Characters.Entries.Where(c => !_purged.Contains(c.Guid)).ToList();
        var decision = StandInCharPurge.Decide(request, self.Name ?? string.Empty, self.Guid, characters);
        if (decision.Refusal is { } refusal)
        {
            Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Char, refusal), refusal.Message)));
            return;
        }

        var target = decision.Target;
        var data = new CharPurgeData(target.Name, PlaceholderPurgeCharacters, PlaceholderPurgeBiotas, 0);
        _purged.Add(target.Guid);
        var remaining = characters.Where(c => c.Guid != target.Guid).ToList();
        Later(sink =>
        {
            Say(sink, ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Char, data), data.Describe()));
            var entries = ImmutableArray.CreateBuilder<CharacterListEntry>(remaining.Count);
            foreach (var slot in remaining)
            {
                entries.Add(new CharacterListEntry(slot.Guid, slot.Name, slot.DeleteTimeout));
            }

            sink.OnCharacterList(Context(sink), new CharacterListMsg(0, entries.MoveToImmutable(), 0, _reducer.State.Characters.SlotCount, "stand-in", 0, 1));
        });
    }

    private void Refuse(ToolRequest request, string sentence)
    {
        var unsupported = new ToolError(ToolErrors.All[0], sentence);
        Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Status, unsupported), unsupported.Message)));
    }

    private void Place(string? id, string verb, PlaceData data, uint cell, Vector3 origin)
    {
        Later(sink =>
        {
            Say(sink, ToolsWire.Reply(ToolReply.Success(id, verb, data), data.Describe()));
            Teleport(sink, new StandInTeleport(0, cell, origin));
        });
    }

    /// <summary>A Use: a lifestone binds the player (M3 3.2 db); a portal object of the visit answers UseDone and teleports the player; any other object is answered with UseDone and nothing else.</summary>
    internal OutboundTicket Use(uint guid)
    {
        if (IsLifestone(guid))
        {
            Later(sink => UseLifestone(sink, guid)); // M3 3.2 db: StandInWorld.Death.cs
            return Ticket();
        }

        var portal = _visit.Objects.FirstOrDefault(o => o.Guid == guid && o.PortalTo is not null);
        Later(sink =>
        {
            sink.OnUseDone(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01C7), new UseDoneEvent(0));
            if (portal.PortalTo is { } jump)
            {
                Teleport(sink, jump);
            }
        });
        return Ticket();
    }

    /// <summary>The health fraction the stand-in answers a QueryHealth with for an attackable object that has no health model (a placeholder value).</summary>
    internal const float PlaceholderHealth = 0.6f;

    /// <summary>
    /// A QueryHealth: ACE answers (GameEvent 0x01C0 UpdateHealth) only for a creature, with <c>Health.Current / MaxValue</c> read when the query arrives (<c>WorldObject.cs</c> <c>QueryHealth</c>). The stand-in answers for a creature
    /// it holds health for (<see cref="StandInObject.MaxHealth"/>, M3 3.1 fb) with its real fraction, and for any other object of the visit whose description carries the attackable bit (0x10) with <see cref="PlaceholderHealth"/>;
    /// any other object, and object 0 (the cancel), is not answered.
    /// </summary>
    internal OutboundTicket QueryHealth(uint guid)
    {
        if (TryCreature(guid, out var creature))
        {
            if (_reducer.State.Objects.Contains(guid))
            {
                var fraction = creature.Fraction;
                Later(sink => sink.OnUpdateHealth(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01C0), new UpdateHealthEvent(guid, fraction)));
            }
        }
        else if (_visit.Objects.Any(o => o.Guid == guid && (o.DescriptionFlags & 0x10) != 0))
        {
            Later(sink => sink.OnUpdateHealth(Context(sink), new GameEventHeader(_reducer.State.Self.Guid, 0, 0x01C0), new UpdateHealthEvent(guid, PlaceholderHealth)));
        }

        return Ticket();
    }

    /// <summary>A log off: the phases and the character list come back after the visit's log-off delay.</summary>
    internal void LogOff() => Later(sink => StandInSessions.BeginLogOff(sink, _visit.LogOffDelay, _reducer.StandIn!, _reducer, _purged));

    private void Teleport(IAcSessionSink sink, StandInTeleport jump)
    {
        DispelProtectionOnTeleport(sink); // M3 3.2 db: a teleport dispels the lifestone protection (Player_Location.cs 702)
        Teleports++;
        var sequence = (ushort)(_reducer.State.Self.Stamps.Teleport + 1);
        StandInSessions.SendTeleport(sink, _reducer.State.Self.Guid, jump, sequence);
        if (UnhideAfterTeleport)
        {
            _reducer.StandIn!.At(_reducer.Clock.Now + UnhideAfterTeleportSeconds, UnhidePlayer);
        }
    }

    /// <summary>
    /// M3 3.2 dd (opt-in, <see cref="StandInServer.UnhideAfterTeleport"/>): ACE clears the player's Hidden state when the client's LoginComplete arrives after a teleport (<c>Player.OnTeleportComplete</c>), which ends the arrival
    /// (<c>SelfTravel.Present</c>). The offline session refuses LoginComplete (there is no server), so a stand-in teleport leaves the player at <c>SelfTravel.TeleportArrived</c> for good; a scenario that waits for the player to be
    /// present after a death's teleport (<c>DeathScenario</c>'s respawn) needs this. Off by default: the other checks run as they did.
    /// </summary>
    internal bool UnhideAfterTeleport { get; set; }

    private const double UnhideAfterTeleportSeconds = 1.0;

    private void UnhidePlayer(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var stamps = self.Stamps;
        sink.OnSetState(Context(sink), new SetStateMsg(self.Guid, PhysicsState.Gravity | PhysicsState.EdgeSlide, stamps.Instance, unchecked((ushort)(stamps.State + 1))));
    }

    private static void Say(IAcSessionSink sink, string text) => sink.OnServerMessage(Context(sink), new ServerMessageMsg(text, ToolsWire.ReplyChatType));

    private static InboundContext Context(IAcSessionSink sink) => new(0, MessageQueue.SmartBox, sink is GameStateReducer r ? r.Clock.Time.GetTimestamp() : 0);
}
