using System.Numerics;
using Lifestone.Client.Session.State;
using Lifestone.Client.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// What the play-mode checks can make the offline stand-in server do from outside its script: boot the player, or go quiet (both end the session in failure,
/// as the network session does, and the retail disconnected screen reports it: its button reconnects, D43), or place an object in the world (a chest, a lifestone; M4 4.1 t2: never a creature, D23). Made by <see cref="StandInSessions.Server"/>.
/// </summary>
public sealed class StandInServer
{
    private readonly IInboundMessageHandler _handler;

    internal StandInServer(IInboundMessageHandler handler) => _handler = handler;

    /// <summary>The server boots the player <paramref name="after"/> seconds from now (session clock): F7DC with <paramref name="reason"/>, then the failure.</summary>
    public void Boot(double after, string? reason) => StandInSessions.BootLater(_handler, after, reason);

    /// <summary>
    /// Lets a held visit (<see cref="StandInVisit.HoldEntry"/>) begin: the player enters <paramref name="after"/> seconds from now (session clock), as a live server answers Enter about
    /// 0.1 s after it is pressed. False when no visit is held.
    /// </summary>
    public bool Release(double after) => StandInSessions.ReleaseEntry(_handler, after);

    /// <summary>The server stops answering: after <paramref name="after"/> seconds the session fails as a silent server's does (<see cref="SessionFailureKind.Silence"/>).</summary>
    public void GoQuiet(double after, string message) =>
        StandInSessions.FailLater(_handler, after, new SessionFailure(SessionFailureKind.Silence, SessionPhase.InWorld, message));

    /// <summary>
    /// Places <paramref name="obj"/> at <paramref name="at"/> the next time the stand-in's session ticks (a CreateObject, as the server sends one for an object it spawns). Game thread only; needs the world part of the stand-in
    /// (<see cref="StandInSessions.AnswerWorld"/>). The guid is the caller's: placing a guid the world already holds replaces nothing and is refused by the reducer as any duplicate create is. The stand-in holds no creature (M4 4.1 t2, D23, D26):
    /// no check places a monster, and <c>NoStandInCreatureInDrawnRunsTests</c> pins it.
    /// </summary>
    /// <param name="obj">The object (its <see cref="StandInObject.Offset"/> is ignored).</param>
    /// <param name="at">Where it stands.</param>
    public void Place(StandInObject obj, AcPosition at) => World().PlaceObject(obj, at);

    /// <summary>
    /// Places <paramref name="obj"/> <paramref name="offset"/> metres (AC axes: +X east, +Y north, +Z up) from the player's position now. In an outdoor cell the cell id follows the new position
    /// (<see cref="AcCoordinates.AdjustToOutside"/>); in an EnvCell the cell is the player's own, so the offset must stay inside it (a dungeon caller places by <see cref="Place"/> with a position of the cell graph). Returns where it was placed, or null
    /// when the player has no position yet.
    /// </summary>
    /// <param name="obj">The object (its <see cref="StandInObject.Offset"/> is ignored).</param>
    /// <param name="offset">Metres from the player.</param>
    public AcPosition? PlaceNear(StandInObject obj, Vector3 offset)
    {
        if (_handler is not GameStateReducer { State.Self.Position: { } here })
        {
            return null;
        }

        var cell = here.CellId;
        var local = here.Origin + offset;
        if (AcCoordinates.IsOutdoorCell(cell))
        {
            AcCoordinates.AdjustToOutside(ref cell, ref local);
        }

        var at = new AcPosition(cell, local, here.Orientation);
        Place(obj, at);
        return at;
    }

    /// <summary>
    /// Places a chest (<see cref="StandInChest"/>) <paramref name="offset"/> metres from the player, holding <paramref name="items"/> (name, weenie class id, stack) in the order given; the chest opens with a Use (M4 2.1 s3,
    /// <c>TODO(m4-standin-chest)</c>: a labelled placeholder, the stand-in's world has no chest of its own). Returns where it was placed, or null when the player has no position yet.
    /// </summary>
    /// <param name="guid">The chest's object id.</param>
    /// <param name="offset">Metres from the player.</param>
    /// <param name="items">What the chest holds.</param>
    public AcPosition? PlaceChest(uint guid, Vector3 offset, params (string Name, uint Wcid, int Stack)[] items)
    {
        var at = PlaceNear(StandInChest.Make(guid), offset);
        if (at is not null)
        {
            var world = World();
            world.NotePlaceholder("m4-standin-chest", "the chest is a stand-in object (weenie 143's own Setup and icon, but a name that says placeholder, no ACE appearance flags beyond Openable and contents the host stocks), not ACE's chest weenie with its drops");
            world.StockChest(guid, items);
        }

        return at;
    }

    /// <summary>Spell projectiles the stand-in has launched with ACE's model (M3 3.1 fb).</summary>
    public int BoltsLaunched => World().BoltsLaunched;

    /// <summary>Projectiles that reached their target.</summary>
    public int BoltsHit => World().BoltsHit;

    /// <summary>Projectiles that flew on past a missing or dead target, or had none.</summary>
    public int BoltsMissed => World().BoltsMissed;

    /// <summary>Takes an object placed by <see cref="Place"/> out of the world (a check's clean-up).</summary>
    /// <param name="guid">The object's id.</param>
    public void Remove(uint guid) => World().RemoveObject(guid);

    /// <summary>Deaths of the player the stand-in has played (a suicide counts).</summary>
    public int PlayerDeaths => World().PlayerDeaths;

    /// <summary>Corpses of the player the stand-in has created.</summary>
    public int Corpses => World().Corpses;

    /// <summary>True from the player's death until the vitals come back.</summary>
    public bool Dying => World().Dying;

    /// <summary>True while the lifestone protection holds.</summary>
    public bool UnderProtection => World().UnderProtection;

    /// <summary>
    /// Dispels the lifestone protection as a teleport does (<c>Player_Location.cs</c> 702-703, <c>LifestoneProtectionDispel</c>: the 0x028A-less system line "Your actions have dispelled the Lifestone's magic!"). The live death
    /// scenario's den teleport does this before the monsters come.
    /// </summary>
    public void DispelProtectionAsATeleportDoes() => World().DispelProtectionOnTeleport();

    /// <summary>True once a lifestone bound the player (the death's teleport then goes to it; before, to the visit's start).</summary>
    public bool Bound => World().Sanctuary is not null;

    /// <summary>
    /// M3 3.2 dd: when set, the stand-in unhides the player a second after each teleport it sends, as ACE does when the client's LoginComplete arrives (the offline session refuses LoginComplete, so the player would stay at
    /// <c>SelfTravel.TeleportArrived</c>). Off by default; the whole-death-loop play-check sets it, because the death scenario waits for the player to be present after its respawn.
    /// </summary>
    public bool UnhideAfterTeleport
    {
        get => World().UnhideAfterTeleport;
        set => World().UnhideAfterTeleport = value;
    }

    /// <summary>The WeenieError of the last 01A7 the stand-in sent (always 0x0036: an attack on a guid the stand-in does not hold ends as ACE ends one at a missing target), or null when it has sent none.</summary>
    public uint? LastAttackDoneError => World().AttackDoneErrors.Count == 0 ? null : World().AttackDoneErrors[^1];

    private StandInResponder Responder() =>
        _handler is GameStateReducer { LocalResponder: { } responder }
            ? responder
            : throw new InvalidOperationException("the stand-in has no responder (this is not an offline stand-in session)");

    private StandInWorld World() =>
        _handler is GameStateReducer { LocalResponder.World: { } world }
            ? world
            : throw new InvalidOperationException("the stand-in has no world part (StandInSessions.AnswerWorld was not called)");
}
