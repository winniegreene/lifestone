using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>What the avatar set did with an event.</summary>
public enum AvatarApply
{
    /// <summary>Applied.</summary>
    Applied,

    /// <summary>Retail ignores it (the player's autonomous motion echo, a position without contact, the player's own
    /// position while a player body moves it).</summary>
    Ignored,

    /// <summary>No avatar has the guid (the reducer should have parked the message; counted and logged).</summary>
    UnknownObject,

    /// <summary>It could not be applied (a missing Setup, an unknown motion word, a missing holding location); logged.</summary>
    Failed,
}

/// <summary>Kinds of <see cref="AvatarNotice"/>.</summary>
public enum AvatarNoticeKind
{
    /// <summary>An avatar was created.</summary>
    Created,

    /// <summary>An avatar replaced an older one with the same guid.</summary>
    Recreated,

    /// <summary>An avatar could not be created (the detail says why).</summary>
    CreateFailed,

    /// <summary>An avatar was deleted.</summary>
    Deleted,

    /// <summary>The player's Hidden bit changed (detail "hide" or "unhide"); the portal track's TeleportProgress reads the
    /// reducer's events first, this is for presenters.</summary>
    PlayerHiddenChanged,

    /// <summary>An ObjDesc change failed in part (the detail lists the problems).</summary>
    AppearanceProblem,

    /// <summary>A movement could not be applied (the detail names the low word or the reason).</summary>
    MotionFailed,

    /// <summary>A parent event could not be applied.</summary>
    ParentFailed,

    /// <summary>An event named an object the set does not have.</summary>
    UnknownObject,
}

/// <summary>Something an advance or an event did that a presenter or a test may want: one per event, no C# events.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Guid">The object.</param>
/// <param name="Detail">Why, for failures and problems.</param>
public readonly record struct AvatarNotice(AvatarNoticeKind Kind, uint Guid, string? Detail = null);

/// <summary>Counters of the avatar set; every unknown and every ignored message is counted, never silently dropped.</summary>
public sealed class AvatarStats
{
    /// <summary>Avatars created.</summary>
    public long Created { get; internal set; }

    /// <summary>Creates that failed.</summary>
    public long CreateFailures { get; internal set; }

    /// <summary>Avatars replaced by a recreate.</summary>
    public long Recreated { get; internal set; }

    /// <summary>Avatars deleted.</summary>
    public long Deleted { get; internal set; }

    /// <summary>Server positions applied to remote objects.</summary>
    public long Moves { get; internal set; }

    /// <summary>Server positions without contact, which retail's <c>MoveOrTeleport</c> ignores.</summary>
    public long MovesWithoutContact { get; internal set; }

    /// <summary>The player's own positions left to the player body.</summary>
    public long SelfMovesLeftToBody { get; internal set; }

    /// <summary>Movements applied.</summary>
    public long Motions { get; internal set; }

    /// <summary>The player's autonomous motion echoes, ignored as retail does.</summary>
    public long AutonomousEchoesIgnored { get; internal set; }

    /// <summary>Movements that failed (unknown words, missing bodies).</summary>
    public long MotionFailures { get; internal set; }

    /// <summary>Movements of objects without a motion table, which retail ignores.</summary>
    public long MotionsWithoutTable { get; internal set; }

    /// <summary>The player's server movements that found no player body with a movement manager.</summary>
    public long PlayerMotionsWithoutBody { get; internal set; }

    /// <summary>State changes.</summary>
    public long StateChanges { get; internal set; }

    /// <summary>Hidden changes (scripts played).</summary>
    public long HiddenChanges { get; internal set; }

    /// <summary>Parent events applied.</summary>
    public long ParentEvents { get; internal set; }

    /// <summary>Parent events that failed.</summary>
    public long ParentFailures { get; internal set; }

    /// <summary>Pickups.</summary>
    public long Pickups { get; internal set; }

    /// <summary>ObjDesc changes.</summary>
    public long DescChanges { get; internal set; }

    /// <summary>Appearance problems reported.</summary>
    public long AppearanceProblems { get; internal set; }

    /// <summary>Events for objects the set does not have.</summary>
    public long UnknownObjects { get; internal set; }

    /// <summary>Effect events handed to the sink.</summary>
    public long EffectEventsDelivered { get; internal set; }

    /// <summary>Effect events dropped for want of a sink (counted, not silently dropped).</summary>
    public long EffectEventsDropped { get; internal set; }

    /// <summary>F755 scripts played on a body: the type was in the body's PhysicsScriptTable and its script was queued (<c>CPhysicsObj::play_script</c>).</summary>
    public long ServerScriptsPlayed { get; internal set; }

    /// <summary>F755 scripts whose type the body's table does not have, or the body has no table: retail's <c>play_script</c> does nothing; here it is counted and logged once per table and type.</summary>
    public long ServerScriptsMissing { get; internal set; }

    /// <summary>F750 sounds mapped to a sound-table event on a body and queued for the effect sink (counted whether or not a sink exists: <see cref="EffectEventsDelivered"/> and <see cref="EffectEventsDropped"/> say what became of it).</summary>
    public long ServerSoundsPlayed { get; internal set; }

    /// <summary>
    /// F750 and F755 for an object the set does not hold, dropped and counted (never a crash). The reducer already parks a message for an object the session does not know until its CreateObject
    /// (<c>SmartBox::HandleSoundEvent</c> / <c>HandlePlayScriptType</c> park it in retail too), so what reaches the set names an object the session holds: this is an avatar the set lacks (its create
    /// failed, or it was deleted while the event waited in the adapter's queue).
    /// </summary>
    public long ServerEffectsDropped { get; internal set; }

    /// <summary>Cast windows opened (<see cref="AvatarSet.OpenCastWindow"/>): from the words or the first gesture of the player's cast to the cast's UseDone.</summary>
    public long CastWindows { get; internal set; }

    /// <summary>
    /// F755 scripts played (<see cref="ServerScriptsPlayed"/> sense: the type was in the body's table) on the player, or on the cast's target when the host named one
    /// (<see cref="AvatarSet.CastTarget"/>), while a cast window was open. M3 2.1 s2b; the judged counter of spec section 2 DoD 7.
    /// </summary>
    public long CastScripts { get; internal set; }

    /// <summary>
    /// Voices that <b>started</b> ("sounded", M3 1.3 fix B: <see cref="Lifestone.Client.Audio.ISoundVoiceCounter"/>, the router's hook and server voices) from the player's body, or the cast target's when the
    /// host named one, while a cast window was open: the gesture animations' sound hooks, the scripts' sound hooks and the server's F750 sounds. 0 when the effect sink counts no voices (no sound player).
    /// </summary>
    public long CastSounds { get; internal set; }

    /// <summary>Projectiles created: objects the server made in the Missile state (a spell's bolt), counted once each (M3 3.1 lane fc).</summary>
    public long ProjectilesCreated { get; internal set; }

    /// <summary>
    /// Projectiles shown on at least one frame, counted once each (3.1-R6: named "shown", not "drawn", because a projectile with no drawable part, like the Flame Bolt, counts without its particles being checked): the draw layer calls <see cref="AvatarSet.NoteDrawn"/> for every avatar it draws, so a projectile whose meshes were not ready is not counted until
    /// the frame it appears in (M3 3.1 lane fc).
    /// </summary>
    public long ProjectilesShown { get; internal set; }

    /// <summary>F755 Launch scripts the server played on a projectile (received for a projectile body, whether or not its table had the type: <see cref="ProjectileScriptsMissing"/> says how many it lacked).</summary>
    public long ProjectileLaunches { get; internal set; }

    /// <summary>F755 Explode scripts the server played on a projectile (received for a projectile body, whether or not its table had the type).</summary>
    public long ProjectileExplodes { get; internal set; }

    /// <summary>Launch and Explode scripts received for a projectile whose PhysicsScriptTable lacked the type (also counted in <see cref="ServerScriptsMissing"/>, and logged once per table and type).</summary>
    public long ProjectileScriptsMissing { get; internal set; }

    /// <summary>Times the projectile window opened (<see cref="AvatarSet.ProjectileWindowOpen"/>: the first projectile alive after none).</summary>
    public long ProjectileWindows { get; internal set; }

    /// <summary>Avatars stepped by the last advance.</summary>
    public int SteppedLastFrame { get; internal set; }

    /// <summary>Objects created with a motion table and a Setup without spheres (they cannot move; the census reports the count).</summary>
    public int SpherelessMotionObjects { get; internal set; }

    /// <summary>How many notices were dropped because the list was full (<see cref="AvatarSet.MaxNotices"/>).</summary>
    public int NoticesDropped { get; internal set; }

    /// <summary>Whether the last advance stepped every avatar before its deadline.</summary>
    public bool LastFrameComplete { get; internal set; } = true;
}

/// <summary>One avatar of the set: its body and what drawing it needs.</summary>
public sealed class Avatar
{
    private readonly AvatarSources _sources;
    private readonly Vector3[] _sortCenters;
    private int _sortVersion = -1;

    internal Avatar(AvatarBody body, AvatarCreate created, AvatarSources sources, bool isPlayer)
    {
        Body = body;
        Created = created;
        _sources = sources;
        IsPlayer = isPlayer;
        _sortCenters = new Vector3[body.Setup.Parts.Count];
    }

    /// <summary>The object id.</summary>
    public uint Guid => Body.Guid;

    /// <summary>The body.</summary>
    public AvatarBody Body { get; }

    /// <summary>The description it was created from.</summary>
    public AvatarCreate Created { get; internal set; }

    /// <summary>The player's own character.</summary>
    public bool IsPlayer { get; }

    /// <summary>True once a projectile avatar was drawn on a frame (<see cref="AvatarSet.NoteDrawn"/>).</summary>
    internal bool ProjectileDrawn { get; set; }

    /// <summary>The Setup light this avatar registered in <see cref="AvatarSet.DynamicLights"/> (a non-static object whose Setup has one), or null.</summary>
    internal ModelLight? SourceLight { get; set; }

    /// <summary>True while this avatar is counted in <see cref="AvatarSet.LiveProjectiles"/>.</summary>
    internal bool CountedLive { get; set; }

    /// <summary>A parent that was not there yet when the avatar was created or parented: it waits for it.</summary>
    internal (uint Parent, int Location, uint Placement)? PendingParent { get; set; }

    /// <summary>The sort centre of part <paramref name="part"/>'s level-0 GfxObj (cached per appearance version).</summary>
    internal Vector3 SortCenter(int part)
    {
        var version = Body.Appearance.Version;
        if (version != _sortVersion)
        {
            var parts = Body.Appearance.Parts;
            for (var i = 0; i < _sortCenters.Length && i < parts.Count; i++)
            {
                _sortCenters[i] = _sources.SortCenter(parts[i].Chain.NearestGfxObjId);
            }

            _sortVersion = version;
        }

        return _sortCenters[part];
    }
}

/// <summary>
/// Every server object as an avatar (AV8): creates, deletes, positions, motions, states, vectors, parents, pickups and
/// visual descriptions as the state track's reducer classified them, applied with retail's object semantics
/// (<c>SmartBox</c> and <c>CPhysicsObj</c>, re-expressed from the 11.4186 decompile; names and addresses cited, nothing
/// copied; addresses are 11.4186 / 11.6096), then stepped and drawn every frame (<see cref="Advance"/>,
/// <see cref="Frame"/>).
/// </summary>
/// <remarks>
/// <para><b>No queue, no stamps.</b> The reducer owns SmartBox's sequence gating and parking (m2-plan §1.4): every event
/// it publishes is newer and in order. The set applies them as they come. A message for an object the set does not
/// have is counted and logged (<see cref="AvatarApply.UnknownObject"/>). One wait is kept: an object whose parent is not
/// there yet waits, out of the world, for its parent's create (retail queues its create for the parent).</para>
/// <para><b>Create</b> (<c>SmartBox::HandleCreateObject</c>, 0x00454C80 / 0x00454D20, and
/// <c>ACCObjectMaint::CreateObject</c>, 0x00558870 / 0x005594B0): the Setup's parts (an unloadable part refuses the
/// object, as <c>CPartArray::InitParts</c> does), the resting placement and default animation and script
/// (<c>InitDefaults</c>), the description (<c>set_description</c>: motion table and interpreter, movement or placement,
/// state, scale, translucency, vectors, default script), the ObjDesc (<c>DoObjDescChanges</c>), the children it names and
/// its parent; then it enters the world at its position when it has one and no parent. A recreate replaces the avatar
/// and re-parents the children it names.</para>
/// <para><b>The player</b> (AV10) has an avatar like any object (appearance, children, Hidden, scripts, draw output);
/// its position and animation are the player body's (<see cref="PlayerBody"/>, the physics track's player object).
/// Until one is bound the set places the player at each of its accepted positions.</para>
/// <para>Not thread-safe; the frame thread owns it.</para>
/// </remarks>
public sealed class AvatarSet : ITargetWorld
{
    private const string LogCategory = "avatars";

    private readonly ILogSink _log;
    private readonly int _seed;
    private readonly Dictionary<uint, Avatar> _avatars = [];
    private readonly List<Avatar> _order = [];
    private readonly List<AvatarNotice> _notices = [];
    private readonly HashSet<uint> _spherelessSetupsLogged = [];
    private readonly HashSet<uint> _spherelessMissilesLogged = [];
    private bool _projectileCellLogged;
    private readonly HashSet<(uint Table, uint Type)> _missingScriptsLogged = [];
    private bool _droppedEffectLogged;
    private readonly List<EffectEvent> _events = [];
    private readonly List<string> _problems = [];
    private IPlayerBody? _playerBody;
    private int _cursor;
    private bool _lightSourcesFullLogged;
    private bool _multiLightLogged;

    /// <param name="sources">The dats.</param>
    /// <param name="log">The log, or none.</param>
    /// <param name="seed">Seeds each avatar's effect random generator (with its guid).</param>
    public AvatarSet(AvatarSources sources, ILogSink? log = null, int seed = 0)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _log = log ?? NullLogSink.Instance;
        _seed = seed;
    }

    /// <summary>The dats.</summary>
    public AvatarSources Sources { get; }

    /// <summary>
    /// The dynamic light list (M3 4.1 s0): the viewer light and the Setup lights of the non-static objects here (a projectile's, a creature's), kept by
    /// <see cref="Advance"/> (<c>Render::add_dynamic_light</c>, <c>SmartBox::set_viewer</c>). Set <see cref="DynamicLightList.ViewerLightEnabled"/> from <c>--viewer-light</c>.
    /// </summary>
    public DynamicLightList DynamicLights { get; } = new();

    /// <summary>The list as of the last <see cref="Advance"/>: what the Godot layer reads, nearest the viewer first.</summary>
    public DynamicLightFrame DynamicLightFrame { get; } = new();

    /// <summary>The player's guid (from its create), or 0.</summary>
    public uint PlayerGuid { get; private set; }

    /// <summary>The player's avatar, or null before its create.</summary>
    public PlayerAvatar? Player { get; private set; }

    /// <summary>
    /// The player body (the physics track's player object, or <see cref="KinematicPlayerBody"/>), or null: it moves and
    /// animates the player's avatar, takes the player's server movements when it is an
    /// <see cref="IPlayerMovementBody"/>, and is the player other objects target when it is an
    /// <see cref="ITargetable"/>.
    /// </summary>
    public IPlayerBody? PlayerBody
    {
        get => _playerBody;
        set
        {
            _playerBody = value;
            Player?.Bind(value);
        }
    }

    /// <summary>
    /// Clamps grounded objects to the ground under them (owner decision D24, <c>--remote-ground-clamp</c>); null is off.
    /// </summary>
    public IGroundProbe? GroundProbe { get; set; }

    /// <summary>Receives the avatars' effect events (sounds); without one they are counted as dropped.</summary>
    public IEffectEventSink? EffectEvents { get; set; }

    /// <summary>
    /// Called with the PhysicsScriptTable (0x34xxxxxx) of every body created that has one, so the host can prepare the table's particle meshes off the frame thread
    /// before the server plays a script from it (M3 1.3 s1); null does nothing. Not per frame.
    /// </summary>
    public Action<uint>? ScriptTableSeen { get; set; }

    /// <summary>
    /// Called with the sound table (0x20xxxxxx) of the player's body when it is made, so the host can prefetch the table's waves off the frame thread before the arrival
    /// plays them (M3 1.3 s1); null does nothing. Not per frame.
    /// </summary>
    public Action<uint>? PlayerSoundTableSeen { get; set; }

    /// <summary>
    /// True while the player's cast is in flight (<see cref="OpenCastWindow"/> to <see cref="CloseCastWindow"/>, which takes effect after the next <see cref="Advance"/> has delivered the frame's effects, so a
    /// script or sound the cast's last message started is still counted). <see cref="AvatarStats.CastScripts"/> and <see cref="AvatarStats.CastSounds"/> count only inside it.
    /// </summary>
    public bool CastWindowOpen { get; private set; }

    /// <summary>
    /// The object the open cast was aimed at, or 0 (a self cast, or a host that does not say): the scripts and voices of its avatar count in the cast window beside the player's. Set by the host that
    /// starts the cast at a target (the avatar adapter, which sees only the bus, never sets it).
    /// </summary>
    public uint CastTarget { get; set; }

    /// <summary>Counters.</summary>
    public AvatarStats Stats { get; } = new();

    /// <summary>
    /// Opens the cast window (M3 2.1 s2b): the player's words (HearSpeech, chat type Spellcasting) or its first cast gesture (ACE's UpdateMotion in the Magic stance) came in. Opening an open window only
    /// cancels a pending close.
    /// </summary>
    public void OpenCastWindow()
    {
        if (!CastWindowOpen)
        {
            CastWindowOpen = true;
            Stats.CastWindows++;
        }

        _castClosing = false;
    }

    /// <summary>Closes the cast window once the next <see cref="Advance"/> has delivered its effects: the cast's UseDone came in.</summary>
    public void CloseCastWindow()
    {
        if (CastWindowOpen)
        {
            _castClosing = true;
        }
    }

    private bool _castClosing;

    private int _liveProjectiles;

    /// <summary>
    /// True while any projectile lives (M3 3.1 lane fc): from the CreateObject of an object in the Missile state to its F747, the five seconds ACE keeps a bolt after its hit included. Beside
    /// <see cref="CastWindowOpen"/>: the fight leg samples its worst frame over both.
    /// </summary>
    public bool ProjectileWindowOpen => _liveProjectiles > 0;

    /// <summary>Projectiles alive now.</summary>
    public int LiveProjectiles => _liveProjectiles;

    /// <summary>The guid of the projectile created last, or 0 (checks follow a bolt by it).</summary>
    public uint LastProjectileGuid { get; private set; }

    /// <summary>
    /// True for <c>--retail-sphereless-missile</c>: a projectile whose Setup has no spheres never moves, as retail's sphereless branch (<see cref="AvatarBody.RetailSpherelessMissile"/>). Default false: it flies on its
    /// velocity like any other (a glitch fix; no retail projectile Setup lacks a sphere). Read when a body is made.
    /// </summary>
    public bool RetailSpherelessMissile { get; set; }

    /// <summary>The cell lookup a flying projectile follows its cells with (<see cref="ProjectileCellFollower"/>, over the loaded interiors; null keeps a projectile in its creation cell). Read when a body is made.</summary>
    public IAvatarCellFollower? CellFollower { get; set; }

    /// <summary>Where sounds are heard from (<see cref="IAvatarListener"/>: the camera, as retail's <c>SmartBox::set_viewer</c> sets it); null, or a listener with none this frame, uses the viewer's position. Read once per <see cref="Advance"/>.</summary>
    public IAvatarListener? Listener { get; set; }

    private CellFrame _listener;

    /// <summary>
    /// The draw layer showed <paramref name="avatar"/> this frame (all its meshes ready; a projectile with no drawable part is shown too, and what is seen of it is its scripts' particles): the first time a projectile is, <see cref="AvatarStats.ProjectilesShown"/> counts it. Nothing for any other avatar; allocates nothing.
    /// </summary>
    public void NoteDrawn(Avatar avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        if (avatar.Body.IsProjectile && !avatar.ProjectileDrawn)
        {
            avatar.ProjectileDrawn = true;
            Stats.ProjectilesShown++;
        }
    }

    private bool InCastWindow(uint guid, bool isPlayer) => CastWindowOpen && (isPlayer || (CastTarget != 0 && guid == CastTarget));

    /// <summary>The draw output of the last <see cref="Advance"/>.</summary>
    public AvatarFrame Frame { get; } = new();

    /// <summary>
    /// True for <c>--retail-unhide-flash</c>: retail's one opaque frame after an unhide that follows a re-hide mid-fade is drawn (the older fade hook left translucency 0 behind,
    /// and the new fade's hook first applies a frame later). Default false: that frame is drawn as translucency 1 (docs/avatars.md section 17, <see cref="AvatarBody.ConsumeUnhideHold"/>).
    /// </summary>
    public bool RetailUnhideFlash { get; set; }

    /// <summary>The number of avatars.</summary>
    public int Count => _avatars.Count;

    /// <summary>Every avatar, in creation order.</summary>
    public IReadOnlyList<Avatar> All => _order;

    /// <summary>The avatar of <paramref name="guid"/>.</summary>
    public bool TryGet(uint guid, [NotNullWhen(true)] out Avatar? avatar) => _avatars.TryGetValue(guid, out avatar);

    /// <summary>
    /// The object a target id names: the player body when it can be targeted (so reports reach its own target manager),
    /// else the avatar's body.
    /// </summary>
    public ITargetable? FindObject(uint objectId)
    {
        if (objectId != 0 && objectId == PlayerGuid && _playerBody is ITargetable body)
        {
            return body;
        }

        return _avatars.TryGetValue(objectId, out var avatar) ? avatar.Body : null;
    }

    /// <summary>Moves the notices since the last call into <paramref name="into"/>; returns how many.</summary>
    public int DrainNotices(List<AvatarNotice> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var count = _notices.Count;
        into.AddRange(_notices);
        _notices.Clear();
        return count;
    }

    // ----- the session's classified events -----

    /// <summary>
    /// <see cref="ObjectCreated"/>: a new object, or a recreate (F7DB, a newer instance), from <paramref name="create"/>
    /// (<see cref="AvatarWire.FromWorldObject"/>).
    /// </summary>
    public AvatarApply Apply(in ObjectCreated e, AvatarCreate create, double now)
    {
        ArgumentNullException.ThrowIfNull(create);
        return Create(create with { Guid = e.Guid, IsSelf = e.IsSelf }, now, e.Recreated);
    }

    /// <summary>
    /// <see cref="ObjectUpdated"/>: a CreateObject for an object already here refreshed its descriptions; only a changed
    /// visual description applies (<c>SmartBox::UpdateVisualDesc</c>, 0x00451F40 / 0x00451F80, through
    /// <c>ACCObjectMaint::SetVisualDesc</c>); the rest arrives as its own events.
    /// </summary>
    public AvatarApply Apply(in ObjectUpdated e, AvatarCreate create, double now)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (!Find(e.Guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        avatar.Created = create;
        return (e.Changes & ObjectChanges.Model) != 0 ? SetVisualDesc(e.Guid, create.ObjDesc) : AvatarApply.Applied;
    }

    /// <summary><see cref="ObjectDeleted"/>.</summary>
    public AvatarApply Apply(in ObjectDeleted e) => Delete(e.Guid);

    /// <summary>
    /// <see cref="ObjectMoved"/>: a <see cref="PositionVerdict.Remote"/> position goes to <see cref="Move"/>; the
    /// player's own are the player body's (the physics track's <c>ServerPositionPolicy</c>), and place the player's
    /// avatar only while no body is bound.
    /// </summary>
    public AvatarApply Apply(in ObjectMoved e, double now)
    {
        if (e.Verdict != PositionVerdict.Remote)
        {
            if (_playerBody is not null)
            {
                Stats.SelfMovesLeftToBody++;
                return AvatarApply.Ignored;
            }

            if (!Find(e.Guid, out var self))
            {
                return AvatarApply.UnknownObject;
            }

            self.Body.Place(e.Position.Normalized());
            return AvatarApply.Applied;
        }

        return Move(e.Guid, e.Position, e.TeleportAdvanced, e.HasContact, e.Placement, now);
    }

    /// <summary>
    /// <see cref="ObjectMotionChanged"/> (<c>CPhysics::SetObjectMovement</c>, 0x00509690 / 0x0050A160): the player's
    /// autonomous echo is ignored; the player's server movements go to the player body; everything else to the object.
    /// The set itself allocates nothing (up to 16 actions on the stack); each motion the movement queues in the motion
    /// table allocates its pending entry in <see cref="Data.Animation.MotionTableManager"/>
    /// (<see cref="Data.Animation.MotionPlayer.MotionsQueued"/>; the allocation tests measure the bytes per queued motion
    /// until that queue is allocation-free).
    /// </summary>
    public AvatarApply Apply(in ObjectMotionChanged e, double now)
    {
        if (e.IsSelf && e.Autonomous)
        {
            Stats.AutonomousEchoesIgnored++;
            return AvatarApply.Ignored;
        }

        var motion = e.Motion;
        Span<WireMotionItem> actions = stackalloc WireMotionItem[16];
        var count = AvatarWire.CopyActions(motion, actions);
        if (count > actions.Length)
        {
            actions = new WireMotionItem[count];
            AvatarWire.CopyActions(motion, actions);
        }

        return SetMovement(e.Guid, e.IsSelf, e.Autonomous, AvatarWire.ToMovement(motion), actions[..count]);
    }

    /// <summary><see cref="ObjectVectorChanged"/> (<c>SmartBox::DoVectorUpdate</c>, 0x004521C0 / 0x00452200).</summary>
    public AvatarApply Apply(in ObjectVectorChanged e) => SetVectors(e.Guid, e.Velocity, e.Omega);

    /// <summary><see cref="ObjectStateChanged"/> (<c>SmartBox::DoSetState</c>, 0x004520D0 / 0x00452110).</summary>
    public AvatarApply Apply(in ObjectStateChanged e) => SetState(e.Guid, (uint)e.State);

    /// <summary><see cref="ObjectParented"/> (<c>SmartBox::DoParentEvent</c>, 0x00452290 / 0x004522D0).</summary>
    public AvatarApply Apply(in ObjectParented e) => SetParent(e.Child, e.Parent, e.Location, (uint)e.Placement);

    /// <summary><see cref="ObjectPickedUp"/> (<c>SmartBox::DoPickupEvent</c>, 0x00452240 / 0x00452280).</summary>
    public AvatarApply Apply(in ObjectPickedUp e) => PickUp(e.Guid);

    /// <summary>F755 PlayEffect (<see cref="PlayScript"/>).</summary>
    public AvatarApply Apply(in ObjectScriptPlayed e) => PlayScript(e.Guid, e.ScriptType, e.Speed);

    /// <summary>F750 Sound (<see cref="PlaySound"/>).</summary>
    public AvatarApply Apply(in ObjectSoundPlayed e) => PlaySound(e.Guid, e.SoundId, e.Volume);

    /// <summary><see cref="ObjectDescChanged"/> (<c>SmartBox::HandleObjDescEvent</c>, 0x00453340 / 0x00453380).</summary>
    public AvatarApply Apply(in ObjectDescChanged e) => SetVisualDesc(e.Guid, AvatarWire.ToObjDesc(e.Model));

    // ----- the operations, in Lifestone.Data terms -----

    /// <summary>Creates (or, with <paramref name="recreate"/>, replaces) the avatar of <paramref name="create"/>; see the class remarks.</summary>
    public AvatarApply Create(AvatarCreate create, double now, bool recreate = false)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (_avatars.TryGetValue(create.Guid, out var old))
        {
            Remove(old);
            Stats.Recreated++;
        }

        var setup = Sources.GetSetup(create.SetupId);
        if (setup is null)
        {
            return CreateFailed(create.Guid, $"Setup 0x{create.SetupId:X8} does not exist");
        }

        var appearance = PartArrayAppearance.Create(setup, Sources.Appearance);
        if (!appearance.IsLoadable)
        {
            return CreateFailed(create.Guid, $"Setup 0x{create.SetupId:X8} has a part that does not load (CPartArray::InitParts)");
        }

        if (create.IsSelf)
        {
            PlayerGuid = create.Guid;
        }

        var random = new RetailRandom(unchecked(_seed ^ (int)create.Guid));
        var body = new AvatarBody(create.Guid, setup, appearance, Sources, this, random, create.IsCreature, now);
        var avatar = new Avatar(body, create, Sources, create.IsSelf);
        _problems.Clear();
        body.RetailSpherelessMissile = RetailSpherelessMissile;
        body.CellFollower = CellFollower;
        body.SetContact(create.HasContact);

        // An object created Hidden plays its Hidden script here (set_description applies the state through set_state,
        // after the script table: the portal track's HiddenTransition).
        body.Describe(create, now, withInterpreter: !create.IsSelf, _problems);
        _avatars[create.Guid] = avatar;
        _order.Add(avatar);
        if (body.IsProjectile)
        {
            avatar.CountedLive = true;
            if (_liveProjectiles++ == 0)
            {
                Stats.ProjectileWindows++;
            }

            Stats.ProjectilesCreated++;
            LastProjectileGuid = create.Guid;
            if (CellFollower is null && !_projectileCellLogged && create.Position is { } at && Lifestone.Data.World.LandDefs.IsEnvCell(at.CellId))
            {
                _projectileCellLogged = true;
                Log(
                    LogSeverity.Info,
                    create.Guid,
                    "TODO(m3-bolt-cell-tracking): no cell follower is set (the host gives the set a ProjectileCellFollower over the interiors), so a projectile created in a dungeon cell keeps that cell while it flies (no collision transition for a server object, D24); a bolt that crosses into another cell is lit and shown by its creation cell until the server's hit messages; retail's transition would hand it from cell to cell");
            }

            if (setup.Spheres.Count == 0 && !RetailSpherelessMissile && _spherelessMissilesLogged.Add(create.SetupId))
            {
                Log(
                    LogSeverity.Warning,
                    create.Guid,
                    $"glitch fix (--retail-sphereless-missile restores retail): projectile Setup 0x{create.SetupId:X8} has no spheres; retail's sphereless branch (CPhysicsObj::UpdateObjectInternal, 11.6096 0x005161B0, 11.4186 0x005156B0; CPartArray::GetNumSphere 11.6096 0x00518B90, 11.4186 0x00518060) would keep its origin so it never flies; it flies on its velocity here");
            }
        }

        if (body.Effects.PhysicsScriptTableId != 0)
        {
            ScriptTableSeen?.Invoke(body.Effects.PhysicsScriptTableId);
        }

        if (create.IsSelf && body.SoundTableId != 0)
        {
            PlayerSoundTableSeen?.Invoke(body.SoundTableId);
        }

        var result = body.Appearance.DoObjDescChanges(create.ObjDesc);
        if (!result.AllApplied)
        {
            _problems.AddRange(result.Problems);
        }

        foreach (var (childGuid, location) in create.Children)
        {
            if (_avatars.TryGetValue(childGuid, out var child))
            {
                child.Body.SetParent(body, location, placement: null);
            }
        }

        if (create.ParentGuid != 0)
        {
            if (_avatars.TryGetValue(create.ParentGuid, out var parent))
            {
                if (!body.SetParent(parent.Body, create.ParentLocation, create.Placement))
                {
                    ParentFailed(create.Guid, create.ParentGuid, create.ParentLocation);
                }
            }
            else
            {
                avatar.PendingParent = (create.ParentGuid, create.ParentLocation, create.Placement);
            }
        }
        else if (create.Position is { CellId: not 0 } position)
        {
            body.Place(position.Normalized());
        }

        AttachWaitingChildren(avatar);
        RegisterLight(avatar, create);
        if (create.IsSelf)
        {
            Player = new PlayerAvatar(avatar);
            Player.Bind(_playerBody);
        }

        Stats.Created++;
        if (create.MotionTableId != 0 && setup.Spheres.Count == 0)
        {
            // A creature or object with a motion table and no spheres never moves (AvatarBody: CPartArray::GetNumSphere 0 keeps the old origin), so its
            // walking would not show. Counted for every one, named once per Setup (dod-6-7-10).
            Stats.SpherelessMotionObjects++;
            if (_spherelessSetupsLogged.Add(create.SetupId))
            {
                Log(
                    LogSeverity.Warning,
                    create.Guid,
                    $"TODO(avatars): Setup 0x{create.SetupId:X8} has a motion table (0x{create.MotionTableId:X8}) and no spheres, so retail's sphereless branch keeps its origin and it will not move when the server moves it; check it against a retail screenshot (creature: {create.IsCreature})");
            }
        }

        if (_problems.Count > 0)
        {
            Stats.AppearanceProblems += _problems.Count;
            var detail = string.Join("; ", _problems);
            Log(LogSeverity.Warning, create.Guid, "created with problems: " + detail);
            AddNotice(new AvatarNotice(AvatarNoticeKind.AppearanceProblem, create.Guid, detail));
        }

        AddNotice(new AvatarNotice(recreate || old is not null ? AvatarNoticeKind.Recreated : AvatarNoticeKind.Created, create.Guid));
        return AvatarApply.Applied;
    }

    /// <summary>
    /// Retail <c>SmartBox::HandleDeleteObject</c> (0x00451EA0 / 0x00451EE0) after the instance check: the object exits
    /// the world, its children are let go (out of the world) and it is removed. The player is never deleted (the
    /// reducer drops that).
    /// </summary>
    public AvatarApply Delete(uint guid)
    {
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        Remove(avatar);
        Stats.Deleted++;
        AddNotice(new AvatarNotice(AvatarNoticeKind.Deleted, guid));
        return AvatarApply.Applied;
    }

    /// <summary>
    /// A newer server position of a server object (<c>SmartBox::HandleReceivedPosition</c>, 0x00453FD0 / 0x00454070,
    /// for objects other than the player): <see cref="AvatarBody.MoveOrTeleport"/>.
    /// </summary>
    public AvatarApply Move(uint guid, in CellFrame position, bool teleportAdvanced, bool hasContact, uint? placement, double now)
    {
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        avatar.PendingParent = null;
        if (!avatar.Body.MoveOrTeleport(position, teleportAdvanced, hasContact, placement))
        {
            Stats.MovesWithoutContact++;
            return AvatarApply.Ignored;
        }

        Stats.Moves++;
        return AvatarApply.Applied;
    }

    /// <summary>A movement (see <see cref="Apply(in ObjectMotionChanged, double)"/>).</summary>
    public AvatarApply SetMovement(uint guid, bool isSelf, bool autonomous, in WireMovement movement, ReadOnlySpan<WireMotionItem> actions)
    {
        if (isSelf && autonomous)
        {
            Stats.AutonomousEchoesIgnored++;
            return AvatarApply.Ignored;
        }

        MovementUnpackResult result;
        WireMotionFailure failure;
        if (isSelf)
        {
            if (_playerBody is IPlayerMovementBody movementBody)
            {
                result = movementBody.ApplyServerMovement(movement, actions, autonomous, out failure);
            }
            else
            {
                Stats.PlayerMotionsWithoutBody++;
                return AvatarApply.Ignored;
            }
        }
        else
        {
            if (!Find(guid, out var avatar))
            {
                return AvatarApply.UnknownObject;
            }

            if (avatar.Body.Movement is null)
            {
                Stats.MotionsWithoutTable++;
                return AvatarApply.Ignored;
            }

            result = avatar.Body.ApplyMovement(movement, actions, autonomous, out failure);
        }

        switch (result)
        {
            case MovementUnpackResult.Applied:
                Stats.Motions++;
                return AvatarApply.Applied;
            default:
                Stats.MotionFailures++;
                var detail = result == MovementUnpackResult.UnknownCommand
                    ? failure.Message
                    : string.Create(CultureInfo.InvariantCulture, $"movement type {(int)movement.Type}: {result}");
                Log(LogSeverity.Warning, guid, "movement not applied: " + detail);
                AddNotice(new AvatarNotice(AvatarNoticeKind.MotionFailed, guid, detail));
                return AvatarApply.Failed;
        }
    }

    /// <summary>A vector update: velocity and angular velocity (the player's are its body's).</summary>
    public AvatarApply SetVectors(uint guid, Vector3 velocity, Vector3 omega)
    {
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        if (avatar.IsPlayer && _playerBody is not null)
        {
            return AvatarApply.Ignored;
        }

        avatar.Body.SetVectors(velocity, omega);
        return AvatarApply.Applied;
    }

    /// <summary>
    /// A state change (<see cref="AvatarBody.SetState"/>): the player's hidden changes are noticed. A change of the Hidden
    /// bit goes through the portal track's <see cref="HiddenTransition"/>.
    /// </summary>
    public AvatarApply SetState(uint guid, uint state)
    {
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        Stats.StateChanges++;
        var change = avatar.Body.SetState(state);
        if (change != HiddenChange.None)
        {
            Stats.HiddenChanges++;
            if (avatar.IsPlayer)
            {
                AddNotice(new AvatarNotice(AvatarNoticeKind.PlayerHiddenChanged, guid, change == HiddenChange.Hide ? "hide" : "unhide"));
            }
        }

        return AvatarApply.Applied;
    }

    /// <summary>
    /// A parent event: the child is held at the parent's location (<see cref="AvatarBody.SetParent"/>). A parent the set
    /// does not have yet is waited for.
    /// </summary>
    public AvatarApply SetParent(uint childGuid, uint parentGuid, int location, uint placement)
    {
        if (!Find(childGuid, out var child))
        {
            return AvatarApply.UnknownObject;
        }

        if (!_avatars.TryGetValue(parentGuid, out var parent))
        {
            child.PendingParent = (parentGuid, location, placement);
            child.Body.PickUp();
            return AvatarApply.Applied;
        }

        child.PendingParent = null;
        if (!child.Body.SetParent(parent.Body, location, placement))
        {
            ParentFailed(childGuid, parentGuid, location);
            return AvatarApply.Failed;
        }

        Stats.ParentEvents++;
        return AvatarApply.Applied;
    }

    /// <summary>
    /// F755 PlayEffect (<c>SmartBox::HandlePlayScriptType</c>, 11.6096 0x004520B0, then <c>CPhysicsObj::play_script</c>): plays the script <paramref name="scriptType"/> of the body's PhysicsScriptTable at
    /// intensity <paramref name="speed"/> on the body's <see cref="AvatarBody.Effects"/>: its particles and its own hooks run in the frames that follow, as the M1 effect layer runs them. A type the
    /// table lacks (or a body without a table) plays nothing, as retail's does, but is counted (<see cref="AvatarStats.ServerScriptsMissing"/>) and logged once per table and type.
    /// </summary>
    public AvatarApply PlayScript(uint guid, uint scriptType, float speed)
    {
        if (!_avatars.TryGetValue(guid, out var avatar))
        {
            return DropServerEffect(guid, "F755 script");
        }

        var effects = avatar.Body.Effects;
        var projectile = avatar.Body.IsProjectile;
        if (projectile)
        {
            if (scriptType == (uint)DatReaderWriter.Enums.PlayScript.Launch)
            {
                Stats.ProjectileLaunches++;
            }
            else if (scriptType == (uint)DatReaderWriter.Enums.PlayScript.Explode)
            {
                Stats.ProjectileExplodes++;
            }
            else
            {
                projectile = false;
            }
        }

        if (effects.PlayScript((DatReaderWriter.Enums.PlayScript)scriptType, speed) != 0)
        {
            Stats.ServerScriptsPlayed++;
            if (InCastWindow(guid, avatar.IsPlayer))
            {
                Stats.CastScripts++;
            }

            return AvatarApply.Applied;
        }

        Stats.ServerScriptsMissing++;
        if (projectile)
        {
            Stats.ProjectileScriptsMissing++;
        }

        if (_missingScriptsLogged.Add((effects.PhysicsScriptTableId, scriptType)) && _log.IsEnabled(LogSeverity.Warning))
        {
            Log(
                LogSeverity.Warning,
                guid,
                string.Create(CultureInfo.InvariantCulture, $"F755 script type {scriptType} (intensity {speed}) is not in the body's PhysicsScriptTable 0x{effects.PhysicsScriptTableId:X8}: nothing plays (retail's play_script is a silent no-op); logged once per table and type"));
        }

        return AvatarApply.Ignored;
    }

    /// <summary>
    /// F750 Sound (<c>SmartBox::HandleSoundEvent</c>, 11.6096 0x00452000, then <c>CPhysicsObj::play_sound</c>): queues a sound-table event of <paramref name="soundType"/> at
    /// <paramref name="volume"/> on the body (<see cref="ServerSoundTableHook"/>, with the body's sound table); the next <see cref="Advance"/> hands it to <see cref="EffectEvents"/> with the object's other
    /// hook events, or counts it dropped when there is no sink. Counted as played when it is mapped.
    /// </summary>
    public AvatarApply PlaySound(uint guid, uint soundType, float volume)
    {
        if (!_avatars.TryGetValue(guid, out var avatar))
        {
            return DropServerEffect(guid, "F750 sound");
        }

        avatar.Body.QueueServerSound(soundType, volume);
        Stats.ServerSoundsPlayed++;
        return AvatarApply.Applied;
    }

    private AvatarApply DropServerEffect(uint guid, string what)
    {
        Stats.ServerEffectsDropped++;
        if (!_droppedEffectLogged)
        {
            _droppedEffectLogged = true;
            Log(LogSeverity.Warning, guid, what + " for an object the avatar set does not have: dropped and counted (the reducer parks a message for an object the session does not know; this is an avatar the set lacks); logged once");
        }

        return AvatarApply.UnknownObject;
    }

    /// <summary>A pickup: out of the parent and the world.</summary>
    public AvatarApply PickUp(uint guid)
    {
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        avatar.PendingParent = null;
        avatar.Body.PickUp();
        Stats.Pickups++;
        return AvatarApply.Applied;
    }

    /// <summary>A visual description change (<see cref="AvatarBody.SetVisualDesc"/>); problems are logged and noticed.</summary>
    public AvatarApply SetVisualDesc(uint guid, ObjDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (!Find(guid, out var avatar))
        {
            return AvatarApply.UnknownObject;
        }

        Stats.DescChanges++;
        var result = avatar.Body.SetVisualDesc(desc);
        if (!result.AllApplied)
        {
            Stats.AppearanceProblems += result.Problems.Count;
            var detail = string.Join("; ", result.Problems);
            Log(LogSeverity.Warning, guid, "ObjDesc change failed in part: " + detail);
            AddNotice(new AvatarNotice(AvatarNoticeKind.AppearanceProblem, guid, detail));
        }

        return AvatarApply.Applied;
    }

    // ----- the frame -----

    /// <summary>
    /// Steps the avatars to <paramref name="now"/> (the frame clock) and fills <see cref="Frame"/>: the player first,
    /// then the others round-robin from where the last call stopped, until <paramref name="deadlineTimestamp"/>
    /// (<see cref="Stopwatch.GetTimestamp"/> units; <see cref="long.MaxValue"/> for no budget). An avatar skipped by the
    /// budget catches up on its next turn (its clock steps the whole gap, up to 2 s). Then every avatar in a cell is drawn
    /// from its latest pose, and the effect events go to <see cref="EffectEvents"/>. An advance that queues no motion
    /// allocates nothing once warm (a steadily walking crowd: the allocation tests pin it at 0 bytes); motions queued
    /// during it (a MoveTo's steering) allocate their pending entries in the motion table, as
    /// <see cref="Apply(in ObjectMotionChanged, double)"/> describes.
    /// </summary>
    public void Advance(double now, in AvatarViewer viewer, long deadlineTimestamp)
    {
        var step = new AvatarStep(Player?.Position, GroundProbe);
        Player?.Advance(now, step);
        var complete = true;
        var stepped = 0;
        var count = _order.Count;
        for (var n = 0; n < count; n++)
        {
            if ((n & 7) == 0 && n > 0 && Stopwatch.GetTimestamp() > deadlineTimestamp)
            {
                complete = false;
                break;
            }

            var avatar = _order[(_cursor + n) % count];
            if (!avatar.IsPlayer)
            {
                avatar.Body.Advance(now, step);
            }

            stepped++;
        }

        _cursor = complete || count == 0 ? 0 : (_cursor + stepped) % count;
        Stats.SteppedLastFrame = stepped;
        Stats.LastFrameComplete = complete;

        _listener = Listener is { } listener && listener.TryGetListener(out var heard) ? heard : viewer.Position;
        UpdateDynamicLights(viewer);
        Frame.Clear();
        for (var i = 0; i < _order.Count; i++)
        {
            var avatar = _order[i];
            if (avatar.Body.InCell)
            {
                var camera = Player is { CameraTranslucency: > 0f } fading && (avatar.IsPlayer || avatar.Body.TopLevelId == fading.Guid) ? fading.CameraTranslucency : 0f;
                Frame.Add(avatar, viewer, avatar.IsPlayer && Player is { FirstPerson: true }, RetailUnhideFlash, camera);
            }

            DeliverEvents(avatar.Body, avatar.IsPlayer, avatar.Guid);
        }

        if (_castClosing)
        {
            _castClosing = false;
            CastWindowOpen = false;
        }
    }

    private void DeliverEvents(AvatarBody body, bool isPlayer, uint guid)
    {
        if (body.Effects.PendingEvents.Count == 0 && body.PendingServerSounds == 0)
        {
            return;
        }

        if (EffectEvents is not { } sink)
        {
            Stats.EffectEventsDropped += body.Effects.DiscardEvents() + body.DiscardServerSounds();
            return;
        }

        _events.Clear();
        body.Effects.DrainEvents(_events);
        body.DrainServerSounds(_events);
        var origin = SoundOriginOf(body, _listener);
        var soundTable = body.SoundTableId;
        var voices = InCastWindow(guid, isPlayer) ? sink as ISoundVoiceCounter : null;
        var voicesBefore = voices?.VoicesStarted ?? 0;
        for (var i = 0; i < _events.Count; i++)
        {
            sink.OnEffectEvent(_events[i], origin, soundTable, isPlayer);
        }

        if (voices is not null)
        {
            Stats.CastSounds += voices.VoicesStarted - voicesBefore;
        }

        Stats.EffectEventsDelivered += _events.Count;
        _events.Clear();
    }

    /// <summary>
    /// Where a body's sound comes from, relative to the viewer: <see cref="SoundOrigin.Between"/>, which carries retail's
    /// bearing (the compass heading from the <b>source to the listener</b> minus the listener's heading:
    /// <c>SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)</c> 11.6096 0x00550D80 over
    /// <c>Position::heading(source, listener)</c> 0x005AA5D0). The M3 1.3 s1 fix: this method used to measure the
    /// listener-to-source bearing, which mirrored every positioned sound left to right.
    /// </summary>
    private static SoundOrigin SoundOriginOf(AvatarBody body, in CellFrame listener)
    {
        if (listener.CellId == 0 || body.Position.CellId == 0)
        {
            return SoundOrigin.Center;
        }

        return SoundOrigin.Between(listener, body.Position);
    }

    // ----- dynamic lights (M3 4.1 s0) -----

    /// <summary>
    /// A non-static object whose Setup has a light adds a dynamic entry (<c>CPartArray::InitLights</c> 0x00519730 gives it a LIGHTLIST; a Static one's lights are static and belong
    /// to its cell, M2). Colour, intensity and falloff are the Setup's and never change (<c>SetLightHook::Execute</c> 0x00527BD0 only toggles).
    /// </summary>
    private void RegisterLight(Avatar avatar, AvatarCreate create)
    {
        var lights = avatar.Body.Setup.Lights;
        if (lights.Count == 0 || (create.PhysicsState & AvatarStateBits.Static) != 0)
        {
            return;
        }

        if (lights.Count > 1 && !_multiLightLogged)
        {
            _multiLightLogged = true;
            Log(LogSeverity.Warning, create.Guid, $"TODO(m3-setup-multi-light): Setup 0x{create.SetupId:X8} has {lights.Count} lights; only the first is a dynamic light (every end-of-retail lit Setup has exactly one)");
        }

        avatar.SourceLight = lights[0];
        SyncLight(avatar, lights[0]);
    }

    /// <summary>
    /// Whether the body's lights are in a cell. An object on its own: in the world. A held object (M4 4.1 v1): while it is drawn, because retail adds a child's lights to the cell it is changed
    /// into (<c>CPhysicsObj::set_parent</c> 11.6096 0x00516590 -> <c>change_cell</c> 0x00513E90 -> <c>enter_cell</c> 0x005119A0 -> <c>CPartArray::AddLightsToCell</c> 0x005189D0; the holder's
    /// <c>enter_cell</c> does the same for each child) and moves them with it (<c>UpdateChild</c> 0x00513850 -> <c>set_frame</c> -> <c>LIGHTLIST::set_frame</c> 0x00518790), and takes them out
    /// at <c>leave_cell</c> 0x00511A20 (<c>RemoveLightsFromCell</c> 0x00518A20). It goes dark when its holder is Hidden (the child gets NoDraw); the first-person fade does not put it out, since
    /// that is a translucency and not a cell change.
    /// </summary>
    private static bool LightIsPlaced(AvatarBody body) =>
        body.Parent is null ? body.InWorld : body.IsDrawn && body.Position.CellId != 0;

    private void SyncLight(Avatar avatar, ModelLight light)
    {
        var body = avatar.Body;
        var on = LightIsPlaced(body) && body.LightsOn && body.Effects.Visual.LightsOn;
        var position = body.Position;
        var at = position.Origin + (position.CellId == 0 ? light.Offset.Origin : Vector3.Transform(light.Offset.Origin, position.Orientation));
        if (!DynamicLights.Contains(avatar.Guid))
        {
            if (!DynamicLights.Upsert(avatar.Guid, position.CellId, at, light.ColorRgb, light.Intensity, light.Falloff, on) && !_lightSourcesFullLogged)
            {
                _lightSourcesFullLogged = true;
                Log(LogSeverity.Warning, avatar.Guid, $"TODO(m3-dynamic-light-sources): more than {DynamicLightList.MaxSources} objects with lights; this one is not a dynamic light");
            }

            return;
        }

        DynamicLights.Move(avatar.Guid, position.CellId, at);
        DynamicLights.SetOn(avatar.Guid, on);
    }

    /// <summary>
    /// Once per <see cref="Advance"/>: follows every light source with its object's frame and lights flag, rebuilds the list from the viewer
    /// (<c>SmartBox::set_viewer</c> 0x00452C80 runs every drawn frame from <c>SmartBox::update_viewer</c>, so the list is rebuilt per frame) and fills
    /// <see cref="DynamicLightFrame"/>. Allocates nothing.
    /// </summary>
    private void UpdateDynamicLights(in AvatarViewer viewer)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            var avatar = _order[i];
            if (avatar.SourceLight is { } light)
            {
                SyncLight(avatar, light);
            }
        }

        CellFrame? player = Player is { } p && p.Position.CellId != 0 ? p.Position : null;
        DynamicLights.Update(viewer.Position, player);
        DynamicLightFrame.Fill(DynamicLights);
    }

    // ----- helpers -----

    private bool Find(uint guid, [NotNullWhen(true)] out Avatar? avatar)
    {
        if (_avatars.TryGetValue(guid, out avatar))
        {
            return true;
        }

        Stats.UnknownObjects++;
        Log(LogSeverity.Warning, guid, "event for an object the avatar set does not have");
        AddNotice(new AvatarNotice(AvatarNoticeKind.UnknownObject, guid));
        return false;
    }

    private void Remove(Avatar avatar)
    {
        foreach (var child in avatar.Body.Children.ToArray())
        {
            avatar.Body.RemoveChild(child);
            child.LeaveWorld();
            child.StopEmitters();
        }

        avatar.Body.ExitWorld();
        if (avatar.SourceLight is not null)
        {
            avatar.SourceLight = null;
            DynamicLights.Remove(avatar.Guid);
        }

        if (avatar.CountedLive)
        {
            avatar.CountedLive = false;
            _liveProjectiles--;
        }

        _avatars.Remove(avatar.Guid);
        _order.Remove(avatar);
        if (ReferenceEquals(Player?.Avatar, avatar))
        {
            Player = null;
        }
    }

    private void AttachWaitingChildren(Avatar parent)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            var child = _order[i];
            if (child.PendingParent is { } pending && pending.Parent == parent.Guid)
            {
                child.PendingParent = null;
                if (!child.Body.SetParent(parent.Body, pending.Location, pending.Placement))
                {
                    ParentFailed(child.Guid, parent.Guid, pending.Location);
                }
            }
        }
    }

    private AvatarApply CreateFailed(uint guid, string why)
    {
        Stats.CreateFailures++;
        Log(LogSeverity.Error, guid, "create failed: " + why);
        AddNotice(new AvatarNotice(AvatarNoticeKind.CreateFailed, guid, why));
        return AvatarApply.Failed;
    }

    private void ParentFailed(uint child, uint parent, int location)
    {
        Stats.ParentFailures++;
        var detail = string.Create(CultureInfo.InvariantCulture, $"parent 0x{parent:X8} has no holding location {location}");
        Log(LogSeverity.Warning, child, detail);
        AddNotice(new AvatarNotice(AvatarNoticeKind.ParentFailed, child, detail));
    }

    /// <summary>The most notices kept for <see cref="DrainNotices"/>; the oldest are dropped past it (no host drains them every frame).</summary>
    public const int MaxNotices = 1024;

    private bool _loggedNoticeOverflow;

    private void AddNotice(AvatarNotice notice)
    {
        if (_notices.Count >= MaxNotices)
        {
            var drop = _notices.Count - MaxNotices + 1;
            _notices.RemoveRange(0, drop);
            Stats.NoticesDropped += drop;
            if (!_loggedNoticeOverflow)
            {
                _loggedNoticeOverflow = true;
                _log.Write(LogSeverity.Warning, LogCategory, $"the notice list is full ({MaxNotices}); the oldest notices are dropped (counted in the stats; nothing drains them every frame in play mode)", []);
            }
        }

        _notices.Add(notice);
    }

    private void Log(LogSeverity severity, uint guid, string message)
    {
        if (_log.IsEnabled(severity))
        {
            _log.Write(severity, LogCategory, message, [new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{guid:X8}"))]);
        }
    }
}
