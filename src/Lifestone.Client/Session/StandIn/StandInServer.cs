using System.Numerics;
using Lifestone.Client.Session.State;
using Lifestone.Client.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// What the play-mode checks can make the offline stand-in server do from outside its script: boot the player, or go quiet (both end the session in failure,
/// as the network session does, and the retail disconnected screen reports it: its button reconnects, D43), or place a creature in the world (M3 3.1 fb). Made by <see cref="StandInSessions.Server"/>.
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
    /// Places <paramref name="creature"/> at <paramref name="at"/> the next time the stand-in's session ticks (a CreateObject, as the server sends one for a creature it spawns). A creature with
    /// <see cref="StandInObject.MaxHealth"/> then has health: a spell that hits it takes some, QueryHealth answers the fraction, and at zero it dies (<c>StandInWorld.Combat.cs</c>). Game thread only; needs the world part of the
    /// stand-in (<see cref="StandInSessions.AnswerWorld"/>). The guid is the caller's: placing a guid the world already holds replaces nothing and is refused by the reducer as any duplicate create is.
    /// </summary>
    /// <param name="creature">The object (its <see cref="StandInObject.Offset"/> is ignored).</param>
    /// <param name="at">Where it stands.</param>
    public void Place(StandInObject creature, AcPosition at) => World().PlaceCreature(creature, at);

    /// <summary>
    /// Places <paramref name="creature"/> <paramref name="offset"/> metres (AC axes: +X east, +Y north, +Z up) from the player's position now. In an outdoor cell the cell id follows the new position
    /// (<see cref="AcCoordinates.AdjustToOutside"/>); in an EnvCell the cell is the player's own, so the offset must stay inside it (a dungeon caller places by <see cref="Place"/> with a position of the cell graph). Returns where it was placed, or null
    /// when the player has no position yet.
    /// </summary>
    /// <param name="creature">The object (its <see cref="StandInObject.Offset"/> is ignored).</param>
    /// <param name="offset">Metres from the player.</param>
    public AcPosition? PlaceNear(StandInObject creature, Vector3 offset)
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
        Place(creature, at);
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
            world.NotePlaceholder("m4-standin-chest", "the chest is a stand-in object (the human Setup, named as a placeholder, weenie 143), not ACE's chest weenie");
            world.StockChest(guid, items);
        }

        return at;
    }

    /// <summary>The health fraction (0 to 1) the stand-in holds for the creature <paramref name="guid"/>, or null when it holds no health for it (not a creature with health, or not placed).</summary>
    public float? HealthOf(uint guid) => World().HealthFraction(guid);

    /// <summary>Spell projectiles the stand-in has launched with ACE's model (M3 3.1 fb).</summary>
    public int BoltsLaunched => World().BoltsLaunched;

    /// <summary>Projectiles that reached their target.</summary>
    public int BoltsHit => World().BoltsHit;

    /// <summary>Projectiles that flew on past a missing or dead target, or had none.</summary>
    public int BoltsMissed => World().BoltsMissed;

    /// <summary>Creatures the stand-in's spells have killed.</summary>
    public int Kills => World().Kills;

    /// <summary>Takes an object placed by <see cref="Place"/> out of the world (a creature stops attacking; M3 3.2 db).</summary>
    /// <param name="guid">The object's id.</param>
    public void Remove(uint guid) => World().RemoveObject(guid);

    /// <summary>Blows a tusker that attacks (<see cref="StandInObject.Attacks"/>) has landed on the player (M3 3.2 db).</summary>
    public int TuskerHits => World().TuskerHits;

    /// <summary>Strikes the player evaded.</summary>
    public int TuskerEvades => World().TuskerEvades;

    /// <summary>Strikes the lifestone protection turned away.</summary>
    public int ProtectedBlows => World().ProtectedBlows;

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
    /// scenario's den teleport does this before the tuskers come; the offline whole-death check has no den teleport, so it calls this once before its scenario, with the protection of the check's own earlier death.
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

    // --- M4 1.2 s3: the player's attacks, for the play-checks rows (PlayChecks.Attack.cs). The rows send through the Action API (IActionApi.Attack, MissileAttack: s2); these are the stand-in's counters. ---

    /// <summary>The chance that the player's blow or arrow lands: the placeholder 0.85 unless a check sets it (1 makes a row deterministic).</summary>
    public double PlayerHitRate
    {
        get => World().PlayerHitRate;
        set => World().PlayerHitRate = value;
    }

    /// <summary>Melee swings the stand-in has played.</summary>
    public int Swings => World().Swings;

    /// <summary>Arrows the stand-in has launched.</summary>
    public int ArrowsLaunched => World().ArrowsLaunched;

    /// <summary>Blows and arrows that landed.</summary>
    public int PlayerHits => World().PlayerHits;

    /// <summary>Attack sequences that have ended (each sent one 01A7 {0x0036}).</summary>
    public int AttackSequencesEnded => World().AttackSequencesEnded;

    /// <summary>The WeenieError of the last 01A7 the stand-in sent (0 for a swing over, 0x0036 for the sequence over), or null when it has sent none.</summary>
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
