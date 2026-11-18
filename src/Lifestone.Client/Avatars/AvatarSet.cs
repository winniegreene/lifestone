using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Physics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
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
/// <param name="Name">The object's name for a Created, Recreated or Deleted notice (M5 7 F17: the host's object lines), else null.</param>
/// <param name="Distance">The metres from the player at that moment for those notices, or <see cref="UnknownDistance"/> (no player yet, or the object has no place).</param>
public readonly record struct AvatarNotice(AvatarNoticeKind Kind, uint Guid, string? Detail = null, string? Name = null, float Distance = AvatarNotice.UnknownDistance)
{
    /// <summary>The distance of a notice that has none.</summary>
    public const float UnknownDistance = -1f;
}

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

    /// <summary>
    /// Server objects within 40 m of the player drawn more than a metre from where they were the frame before without a teleport or a far placement, or whose pose jumped (<see cref="AvatarJumpWatch"/>): the
    /// first <see cref="AvatarSet.MaxJumpLogs"/> are logged with the state of their position and motion managers.
    /// </summary>
    public long CreatureJumps { get; internal set; }

    /// <summary>Server objects drawn somewhere and back on consecutive frames, by position or by pose (the hero takes' flicker, <see cref="AvatarJumpWatch"/>).</summary>
    public long CreatureFlips { get; internal set; }

    /// <summary>Blips (the interpolation manager gave up on a server position and the body was placed there) caused by a player's object holding the body back (M5 item 5 F4; the blip counters by cause).</summary>
    public long BlipsByPlayer { get; internal set; }

    /// <summary>Blips after another creature held the body back.</summary>
    public long BlipsByCreature { get; internal set; }

    /// <summary>Blips after another object (a door, a lifestone) held the body back.</summary>
    public long BlipsByObject { get; internal set; }

    /// <summary>Blips after the environment (terrain, a building, a cell's walls) held the body back.</summary>
    public long BlipsByEnvironment { get; internal set; }

    /// <summary>Blips after only a refused step-up or edge slide was recorded.</summary>
    public long BlipsByStep { get; internal set; }

    /// <summary>Blips with no short transition before them (the body was not held back by anything the transitions met: unhosted, or slow for another reason).</summary>
    public long BlipsUnexplained { get; internal set; }

    /// <summary>Held-back reports written (<c>TODO(m5-creature-held-back)</c>, at most <see cref="AvatarSet.MaxHeldBackLogs"/>).</summary>
    public long HeldBackLogged { get; internal set; }

    /// <summary>Held-back reports only counted: past the session's limit, inside a creature's 5 s, or with debug logging off.</summary>
    public long HeldBackCounted { get; internal set; }

    /// <summary>The blip counters by cause in one read.</summary>
    public AvatarBlipCauses BlipCauses => new(BlipsByPlayer, BlipsByCreature, BlipsByObject, BlipsByEnvironment, BlipsByStep, BlipsUnexplained);

    /// <summary>Avatars stepped by the last advance.</summary>
    public int SteppedLastFrame { get; internal set; }

    /// <summary>Objects created with a motion table and a Setup without spheres (they cannot move; the census reports the count).</summary>
    public int SpherelessMotionObjects { get; internal set; }

    /// <summary>How many notices were dropped because the list was full (<see cref="AvatarSet.MaxNotices"/>).</summary>
    public int NoticesDropped { get; internal set; }

    /// <summary>Whether the last advance stepped every avatar before its deadline.</summary>
    public bool LastFrameComplete { get; internal set; } = true;

    /// <summary>Server-object transitions run by the last advance (M5 item 5, D45: one per hosted object whose origin moved, per step).</summary>
    public int TransitionsLastFrame { get; internal set; }

    /// <summary>Server-object transitions run since the set was made.</summary>
    public long Transitions { get; internal set; }

    /// <summary>The time the last advance spent in server-object transitions, in <see cref="Stopwatch"/> ticks (summed over the avatars).</summary>
    public long TransitionTicksLastFrame { get; internal set; }

    /// <summary>The most any one advance spent in server-object transitions, in <see cref="Stopwatch"/> ticks (a running maximum).</summary>
    public long TransitionTicksMax { get; internal set; }

    /// <summary>The most server-object transitions any one advance ran.</summary>
    public int TransitionsMax { get; internal set; }

    /// <summary>The time the last advance spent in server-object transitions, in milliseconds.</summary>
    public double TransitionMsLastFrame => TransitionTicksLastFrame * 1000.0 / Stopwatch.Frequency;

    /// <summary>The most any one advance spent in server-object transitions, in milliseconds.</summary>
    public double TransitionMsMax => TransitionTicksMax * 1000.0 / Stopwatch.Frequency;

    /// <summary>Placements (a teleport, a far position, a blip) the host's object found no valid position for: the avatar stayed where it was, as retail's does.</summary>
    public long PlacementsRefused { get; internal set; }

    /// <summary>Advances run since the set was made (the frames the means of <see cref="PhysicsCost"/> divide by).</summary>
    public long Advances { get; internal set; }

    /// <summary>The time all advances spent in server-object transitions, in <see cref="Stopwatch"/> ticks.</summary>
    public long TransitionTicks { get; internal set; }

    /// <summary>
    /// Sliding placements through the host's objects in the last advance (M5 item 5 F1, 5-R6): the avatars' teleports, far positions and blips
    /// (refused ones too: a blocked creature's blip is asked again every frame, as retail's <c>InterpolationManager::UseTime</c> asks), and the creates passed to the host (its
    /// <c>enter_world</c>, or nothing for an object without collision) since the advance before.
    /// </summary>
    public int PlacementsLastFrame { get; internal set; }

    /// <summary>Sliding placements since the set was made.</summary>
    public long Placements { get; internal set; }

    /// <summary>The time the last advance spent in sliding placements, in <see cref="Stopwatch"/> ticks.</summary>
    public long PlacementTicksLastFrame { get; internal set; }

    /// <summary>The most any one advance spent in sliding placements, in <see cref="Stopwatch"/> ticks (a running maximum).</summary>
    public long PlacementTicksMax { get; internal set; }

    /// <summary>The time all advances spent in sliding placements, in <see cref="Stopwatch"/> ticks.</summary>
    public long PlacementTicks { get; internal set; }

    /// <summary>The time the last advance spent in sliding placements, in milliseconds.</summary>
    public double PlacementMsLastFrame => PlacementTicksLastFrame * 1000.0 / Stopwatch.Frequency;

    /// <summary>The most any one advance spent in sliding placements, in milliseconds.</summary>
    public double PlacementMsMax => PlacementTicksMax * 1000.0 / Stopwatch.Frequency;

    /// <summary>The frame's physics cost and jump counts in one read (for a host's report line; a copy, allocates nothing).</summary>
    public AvatarPhysicsCost PhysicsCost => new(
        TransitionsLastFrame,
        TransitionMsLastFrame,
        TransitionMsMax,
        Advances > 0 ? TransitionTicks * 1000.0 / Stopwatch.Frequency / Advances : 0.0,
        PlacementsLastFrame,
        PlacementMsLastFrame,
        PlacementMsMax,
        Advances > 0 ? PlacementTicks * 1000.0 / Stopwatch.Frequency / Advances : 0.0,
        PlacementsRefused,
        CreatureJumps,
        CreatureFlips,
        BlipCauses);
}

/// <summary>
/// The avatars' physics cost (M5 item 5 F1, 5-R6 and the lead's #9): the server-object transitions and the sliding placements, the last frame's,
/// the most in any frame and the mean per frame (milliseconds), with the refused placements and the jump watch's counts, read in one call
/// (<see cref="AvatarStats.PhysicsCost"/>).
/// </summary>
/// <param name="Transitions">Transitions in the last advance.</param>
/// <param name="TransitionMs">Their time in the last advance.</param>
/// <param name="TransitionMsMax">The most in any advance.</param>
/// <param name="TransitionMsMean">The mean per advance.</param>
/// <param name="Placements">Sliding placements in the last advance (the creates since the advance before included).</param>
/// <param name="PlacementMs">Their time in the last advance.</param>
/// <param name="PlacementMsMax">The most in any advance.</param>
/// <param name="PlacementMsMean">The mean per advance.</param>
/// <param name="PlacementsRefused">Placements refused since the set was made.</param>
/// <param name="CreatureJumps">Jumps the watch counted (<see cref="AvatarStats.CreatureJumps"/>).</param>
/// <param name="CreatureFlips">Flips the watch counted (<see cref="AvatarStats.CreatureFlips"/>).</param>
/// <param name="Blips">Blips by what held the body back (<see cref="AvatarStats.BlipCauses"/>).</param>
public readonly record struct AvatarPhysicsCost(
    int Transitions,
    double TransitionMs,
    double TransitionMsMax,
    double TransitionMsMean,
    int Placements,
    double PlacementMs,
    double PlacementMsMax,
    double PlacementMsMean,
    long PlacementsRefused,
    long CreatureJumps,
    long CreatureFlips,
    AvatarBlipCauses Blips = default);

/// <summary>Blips since the set was made, by what held the body back in the transitions before it (M5 item 5 F4; a body blipped with no short transition before it is <paramref name="None"/>).</summary>
/// <param name="Player">A player's object.</param>
/// <param name="Creature">Another creature.</param>
/// <param name="Object">Another object (a door, a lifestone).</param>
/// <param name="Environment">Terrain, a building or a cell's walls.</param>
/// <param name="Step">Only a refused step or edge.</param>
/// <param name="None">No short transition before the blip.</param>
public readonly record struct AvatarBlipCauses(long Player, long Creature, long Object, long Environment, long Step, long None)
{
    /// <summary>All blips counted.</summary>
    public long Total => Player + Creature + Object + Environment + Step + None;

    /// <summary>The counts as one report token: <c>player=0 creature=2 object=0 env=3 step=0 none=1</c>.</summary>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"player={Player} creature={Creature} object={Object} env={Environment} step={Step} none={None}");
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
public sealed class AvatarSet : ITargetWorld, ILiveBodies
{
    private const string LogCategory = "avatars";

    private readonly ILogSink _log;
    private IServerObjectPhysics? _serverObjects;
    private bool _serverObjectTransitions = true;
    private bool _missileTransitionsLogged;
    private bool _remoteContactLogged;
    private long _createPlacementTicks;
    private int _createPlacements;
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
    private long _jumpsLogged;
    private long _heldBackLogged;

    /// <summary>How many jumps and flips <see cref="AvatarJumpWatch"/> logs before it only counts them (<see cref="AvatarStats.CreatureJumps"/>, <see cref="AvatarStats.CreatureFlips"/>).</summary>
    public const int MaxJumpLogs = 40;

    /// <summary>How many held-back reports are written in a session before they are only counted (<c>TODO(m5-creature-held-back)</c>).</summary>
    public const int MaxHeldBackLogs = 40;

    /// <summary>The least time between two held-back reports of one creature, in seconds.</summary>
    public const double HeldBackLogSeconds = 5.0;

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

    /// <summary>
    /// The server-object physics host the set feeds (M5 D70): creates, moves (the server's and the avatars' own steps),
    /// state changes, parenting, pickups and deletes, so doors, lifestones, NPCs and creatures stop the player's
    /// transitions; null is off (nothing is fed). Set before the first create.
    /// </summary>
    public IServerObjectPhysics? ServerObjects
    {
        get => _serverObjects;
        set
        {
            _serverObjects = value;
            UpdateTransitions();
        }
    }

    /// <summary>
    /// The avatars' steps and placements run retail's transitions through <see cref="ServerObjects"/> when it offers them
    /// (<see cref="IAvatarTransitions"/>, M5 item 5, D45), so creatures collide with each other, the player, doors and
    /// lifestones; false is the path without collision (D24), for before-and-after measures
    /// (<c>--server-object-transitions=off</c>, <see cref="AvatarsOptions.ServerObjectTransitions"/>; a diagnostics switch,
    /// not a glitch flag). Default on.
    /// </summary>
    public bool ServerObjectTransitions
    {
        get => _serverObjectTransitions;
        set
        {
            _serverObjectTransitions = value;
            UpdateTransitions();
        }
    }

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
    /// M6 item 14 (<c>m6-live-position</c>): the body of <paramref name="guid"/> as the screen draws it, for the session's <see cref="LivePositions"/>: its position, whether it stands in a cell of its own, whether it is
    /// Hidden, and whether it is known not to have taken the server's last position (<see cref="AvatarBody.ServerPositionPending"/>). A lookup; allocates nothing.
    /// </summary>
    public bool TryGetBody(uint guid, out LiveBody body)
    {
        if (!_avatars.TryGetValue(guid, out var avatar))
        {
            body = default;
            return false;
        }

        var b = avatar.Body;
        body = new LiveBody(b.Position, b.InWorld, b.IsHidden, b.ServerPositionPending);
        return true;
    }

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
            ServerObjects?.Remove(create.Guid);
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
        body.Transitions = TransitionsFor(body, create.IsSelf);
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

        if (ServerObjects is { } host)
        {
            var placeStart = Stopwatch.GetTimestamp();
            host.Create(
                new ServerObjectDescription(create.Guid, create.SetupId, create.Scale, create.PhysicsState, create.IsCreature, create.IsSelf, create.DescriptionFlags),
                body.InWorld ? body.Position : null);
            _createPlacementTicks += Stopwatch.GetTimestamp() - placeStart;
            _createPlacements++;

            // The create's sliding placement (enter_world) may have put the object beside another object: the body stands where its object does,
            // so its next step's transition starts there (and the feed does not force the object back onto what it slid off).
            if (body.Transitions is { } hosted && body.InWorld && hosted.TryGetPosition(create.Guid, out var placedAt) && !ServerObjectPhysicsHost.SamePlace(placedAt, body.Position))
            {
                body.Place(placedAt);
            }

            foreach (var (childGuid, _) in create.Children)
            {
                if (_avatars.TryGetValue(childGuid, out var child))
                {
                    FeedPosition(child);
                }
            }
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

        AddNotice(new AvatarNotice(recreate || old is not null ? AvatarNoticeKind.Recreated : AvatarNoticeKind.Created, create.Guid, null, create.Name, DistanceToPlayer(body)));
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

        var name = avatar.Created.Name;
        var distance = DistanceToPlayer(avatar.Body); // before the removal: the notice says how far the object was
        Remove(avatar);
        ServerObjects?.Remove(guid);
        Stats.Deleted++;
        AddNotice(new AvatarNotice(AvatarNoticeKind.Deleted, guid, null, name, distance));
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
        FeedPosition(avatar);
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
        ServerObjects?.SetState(guid, state);
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
            ServerObjects?.LeaveWorld(childGuid);
            return AvatarApply.Applied;
        }

        child.PendingParent = null;
        var parented = child.Body.SetParent(parent.Body, location, placement);
        FeedPosition(child);
        if (!parented)
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
        ServerObjects?.LeaveWorld(guid);
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
        long transitionTicks = 0;
        var transitionCount = 0;
        var placementTicks = _createPlacementTicks;
        var placementCount = _createPlacements;
        _createPlacementTicks = 0;
        _createPlacements = 0;
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
                var (ticks, transitions, refused, placeTicks, placements) = avatar.Body.TakeTransitionCost();
                transitionTicks += ticks;
                transitionCount += transitions;
                Stats.PlacementsRefused += refused;
                placementTicks += placeTicks;
                placementCount += placements;
                FeedPosition(avatar);
                if (avatar.Body.TakeBlip(out var blipCause))
                {
                    CountBlip(blipCause);
                }

                if (avatar.Body.TakeHeldBack())
                {
                    ReportHeldBack(avatar.Body, now);
                }

                if (avatar.Body.JumpWatch.Check(avatar.Body, avatar.Body.LastQuantum) is { } jump)
                {
                    ReportJump(avatar.Guid, jump);
                }
            }

            stepped++;
        }

        _cursor = complete || count == 0 ? 0 : (_cursor + stepped) % count;
        ServerObjects?.Tick();
        Stats.SteppedLastFrame = stepped;
        Stats.LastFrameComplete = complete;
        Stats.TransitionsLastFrame = transitionCount;
        Stats.Transitions += transitionCount;
        Stats.TransitionTicksLastFrame = transitionTicks;
        Stats.TransitionTicksMax = Math.Max(Stats.TransitionTicksMax, transitionTicks);
        Stats.TransitionsMax = Math.Max(Stats.TransitionsMax, transitionCount);
        Stats.TransitionTicks += transitionTicks;
        Stats.PlacementsLastFrame = placementCount;
        Stats.Placements += placementCount;
        Stats.PlacementTicksLastFrame = placementTicks;
        Stats.PlacementTicksMax = Math.Max(Stats.PlacementTicksMax, placementTicks);
        Stats.PlacementTicks += placementTicks;
        Stats.Advances++;
        if (transitionCount > 0 && !_remoteContactLogged)
        {
            _remoteContactLogged = true;
            if (_log.IsEnabled(LogSeverity.Info))
            {
                _log.Write(
                    LogSeverity.Info,
                    LogCategory,
                    "TODO(m5-remote-contact): a hosted body stepped; its contact is the server's Grounded flag carried into the transition, where retail takes the transition's contact with gravity (MoveOrTeleport 0x00516E30, UpdateObjectInternal 0x005161B0), not integrated here (D24, D72)",
                    []);
            }
        }

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

    /// <summary>
    /// The transitions a body runs through: the host's, while <see cref="ServerObjectTransitions"/> is on, for a body that is not
    /// the player's and not a projectile. A projectile keeps the path without collision (its flight and impact are the
    /// projectile track's: <c>TODO(m5-missile-transitions)</c>, logged once).
    /// </summary>
    private IAvatarTransitions? TransitionsFor(AvatarBody body, bool isSelf)
    {
        if (!_serverObjectTransitions || _serverObjects is not IAvatarTransitions transitions || isSelf)
        {
            return null;
        }

        if (body.IsProjectile)
        {
            if (!_missileTransitionsLogged && _log.IsEnabled(LogSeverity.Info))
            {
                _missileTransitionsLogged = true;
                _log.Write(LogSeverity.Info, LogCategory, "TODO(m5-missile-transitions): projectiles fly without the server-object transitions (retail runs a missile's transition with PathClipped)", []);
            }

            return null;
        }

        return transitions;
    }

    private void UpdateTransitions()
    {
        for (var i = 0; i < _order.Count; i++)
        {
            var avatar = _order[i];
            avatar.Body.Transitions = TransitionsFor(avatar.Body, avatar.IsPlayer);
        }
    }

    /// <summary>
    /// Feeds the host where the avatar is now: its position while it is in the world on its own, else out of the world
    /// (held, contained, or out of its cell). The host does nothing for an unchanged position or for an object it keeps
    /// nothing for, so a standing crowd costs a lookup each and allocates nothing.
    /// </summary>
    private void FeedPosition(Avatar avatar)
    {
        if (ServerObjects is not { } host || avatar.IsPlayer)
        {
            return;
        }

        if (avatar.Body.InWorld)
        {
            host.Move(avatar.Guid, avatar.Body.Position);
        }
        else
        {
            host.LeaveWorld(avatar.Guid);
        }
    }

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
                var parented = child.Body.SetParent(parent.Body, pending.Location, pending.Placement);
                FeedPosition(child);
                if (!parented)
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

    /// <summary>The most notices kept for <see cref="DrainNotices"/>; the oldest are dropped past it. A host drains them every frame (play mode does, M5 item 7: <c>AvatarNoticeTally</c>); a host that never does fills the list and gets one overflow warning.</summary>
    public const int MaxNotices = 1024;

    private bool _loggedNoticeOverflow;

    // The metres from the player's body to this one (M5 7 F17, the host's object lines), or the unknown distance when either has no place yet or the player is this body.
    private float DistanceToPlayer(AvatarBody body)
    {
        var player = _playerBody?.Position ?? Player?.Body.Position;
        return player is { CellId: not 0 } here && body.Parent is null && body.Position.CellId != 0 ? PositionFrames.Distance(here, body.Position) : AvatarNotice.UnknownDistance;
    }

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
                _log.Write(LogSeverity.Warning, LogCategory, $"the notice list is full ({MaxNotices}); the oldest notices are dropped (counted in the stats; the host is not draining them every frame)", []);
            }
        }

        _notices.Add(notice);
    }

    /// <summary>Counts a jump or flip and logs the first <see cref="MaxJumpLogs"/> with what moved the object (TODO(hero-creature-flicker)).</summary>
    private void ReportJump(uint guid, in AvatarJump jump)
    {
        if (jump.Kind is AvatarJumpKind.RootFlip or AvatarJumpKind.PoseFlip)
        {
            Stats.CreatureFlips++;
        }
        else
        {
            Stats.CreatureJumps++;
        }

        if (_jumpsLogged >= MaxJumpLogs || !_log.IsEnabled(LogSeverity.Warning))
        {
            return;
        }

        _jumpsLogged++;
        var c = CultureInfo.InvariantCulture;
        _log.Write(
            LogSeverity.Warning,
            LogCategory,
            "TODO(hero-creature-flicker): a server object was drawn far from its last frame (docs/milestones/m4-hero-visual-bugs.md); the fields name what moved it",
            [
                new LogField("guid", string.Create(c, $"0x{guid:X8}")),
                new LogField("kind", jump.Kind.ToString()),
                new LogField("metres", string.Create(c, $"{jump.Metres:0.00}")),
                new LogField("turn_deg", string.Create(c, $"{jump.Turn:0}")),
                new LogField("placed", jump.Placement.ToString()),
                new LogField("sticky", jump.StickyTarget == 0 ? "none" : string.Create(c, $"0x{jump.StickyTarget:X8}{(jump.StickyInitialized ? "" : " (no report yet)")}")),
                new LogField("interp_queued", jump.Interpolating),
                new LogField("interp_fails", jump.InterpolationFails),
                new LogField("moving_to", jump.MovingTo),
                new LogField("contact", jump.Contact),
                new LogField("forward", MotionCommandTable.NameOf(jump.Forward) ?? string.Create(c, $"0x{jump.Forward:X8}")),
                new LogField("pending_motions", jump.PendingMotions),
                new LogField("quantum_ms", string.Create(c, $"{jump.Quantum * 1000.0:0.0}")),
                new LogField("from_server_m", string.Create(c, $"{jump.ServerMetres:0.00}")),
                new LogField("from_player_m", string.Create(c, $"{jump.PlayerMetres:0.00}")),
                new LogField("short_transits", jump.ShortTransits),
                new LogField("blocked_by", jump.Obstacle.Describe()),
                new LogField("step_refused", jump.ShortTransits > 0 && jump.Obstacle.StepRefused),
                new LogField("behind_m", string.Create(c, $"{jump.BehindMetres:0.00}")),
            ]);
        if (_jumpsLogged == MaxJumpLogs)
        {
            _log.Write(LogSeverity.Warning, LogCategory, $"{MaxJumpLogs} server-object jumps or flips are logged; later ones are only counted (AvatarStats.CreatureJumps, CreatureFlips)", []);
        }
    }

    /// <summary>Counts a placed blip under what held the body back before it (<see cref="AvatarStats.BlipCauses"/>).</summary>
    private void CountBlip(AvatarObstacleKind cause)
    {
        switch (cause)
        {
            case AvatarObstacleKind.Player:
                Stats.BlipsByPlayer++;
                break;
            case AvatarObstacleKind.Creature:
                Stats.BlipsByCreature++;
                break;
            case AvatarObstacleKind.Object:
                Stats.BlipsByObject++;
                break;
            case AvatarObstacleKind.Environment:
                Stats.BlipsByEnvironment++;
                break;
            case AvatarObstacleKind.Step:
                Stats.BlipsByStep++;
                break;
            default:
                Stats.BlipsUnexplained++;
                break;
        }
    }

    /// <summary>
    /// <c>TODO(m5-creature-held-back)</c>, report only: a hosted creature's transitions ended short
    /// <see cref="AvatarBody.HeldBackFrames"/> frames in a row. At most one debug line per creature per <see cref="HeldBackLogSeconds"/> and
    /// <see cref="MaxHeldBackLogs"/> a session, then only counted.
    /// </summary>
    private void ReportHeldBack(AvatarBody body, double now)
    {
        if (_heldBackLogged >= MaxHeldBackLogs || now - body.HeldBackLoggedAt < HeldBackLogSeconds || !_log.IsEnabled(LogSeverity.Debug))
        {
            Stats.HeldBackCounted++;
            return;
        }

        _heldBackLogged++;
        Stats.HeldBackLogged++;
        body.HeldBackLoggedAt = now;
        var c = CultureInfo.InvariantCulture;
        var at = body.Position.Origin;
        var asked = body.LastShortRequested;
        var obstacle = body.LastObstacle;
        _log.Write(
            LogSeverity.Debug,
            LogCategory,
            "TODO(m5-creature-held-back): a hosted creature's transitions ended short of the requested position ten frames in a row (AvatarBody.HeldBackFrames); the fields name what held it back",
            [
                new LogField("guid", string.Create(c, $"0x{body.Guid:X8}")),
                new LogField("blocked_by", obstacle.Describe()),
                new LogField("step_refused", obstacle.StepRefused),
                new LogField("cell", string.Create(c, $"0x{body.Position.CellId:X8}")),
                new LogField("at", string.Create(c, $"{at.X:0.00},{at.Y:0.00},{at.Z:0.00}")),
                new LogField("asked", string.Create(c, $"{asked.X:0.00},{asked.Y:0.00},{asked.Z:0.00}")),
                new LogField("server", body.ServerPosition is { } server ? string.Create(c, $"0x{server.CellId:X8}:{server.Origin.X:0.00},{server.Origin.Y:0.00},{server.Origin.Z:0.00}") : "none"),
                new LogField("behind_m", string.Create(c, $"{body.BehindMetres:0.00}")),
                new LogField("short_run", body.ShortRun),
                new LogField("short_transits", body.ShortTransits),
            ]);
        if (_heldBackLogged == MaxHeldBackLogs)
        {
            _log.Write(LogSeverity.Debug, LogCategory, $"{MaxHeldBackLogs} held-back reports are logged; later ones are only counted (AvatarStats.HeldBackCounted)", []);
        }
    }

    private void Log(LogSeverity severity, uint guid, string message)
    {
        if (_log.IsEnabled(severity))
        {
            _log.Write(severity, LogCategory, message, [new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{guid:X8}"))]);
        }
    }
}
