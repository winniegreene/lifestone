using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Cast(uint, ActionSource)"/> and <see cref="IActionApi.Cast(uint, uint, ActionSource)"/>: GameAction 0x0048 (CM_Magic::Event_CastUntargetedSpell, 11.4186
/// 0x006A3150, 11.6096 0x006A3F70) and 0x004A (Event_CastTargetedSpell, 0x006A3040 / 0x006A3E60). ACE (Player_Magic.cs, ACEmulator/ACE@95ef426d) answers a cast with its effect, then
/// UseDone 0x01C7 once the cast and its recoil are over; a refusal is a UseDone with the code (0x0400 components, 0x0401 mana, 0x001D busy, ...); a fizzle is a WeenieError 0x028A
/// 0x0402 and a UseDone.
/// </summary>
/// <remarks>
/// <para>What confirms depends on the spell (SpellTable 0x0E00000E; the host gives it to the Action API, <see cref="ActionApi.SpellTable"/>):
/// <list type="bullet">
/// <item>a projectile spell (<see cref="SpellVisualResolver.IsProjectileMetaType"/>: <c>SpellInfo.MetaSpellType</c> Projectile, LifeProjectile or EnchantmentProjectile, ACE's
/// <c>NumProjectiles &gt; 0</c>) is confirmed by the projectile's CreateObject. The wire does not say who cast it (ACE keeps <c>ProjectileSource</c> server-side), so it is the first object
/// in the Missile physics state created while the cast is in flight that <see cref="RefuseProjectile"/> lets through (M3 1.2 fix R2: its origin within <see cref="ProjectileRangeMetres"/> of the player, its velocity
/// away from the player and within <see cref="ProjectileAimToleranceDegrees"/> of the aim, its Setup the spell's where the host knows it; an unknown player position refuses, and a self spell is never confirmed
/// by a projectile, with or without a spell table): <c>TODO(m3-cast-projectile-attribution)</c>, named in the outcome with what is still not proved;</item>
/// <item>a self spell (<c>MetaSpellType</c> Enchantment or FellowEnchantment, cast with no target, at the player, or with <c>SpellFlags.SelfTargeted</c>) is confirmed by the
/// <see cref="EnchantmentChanged"/> that adds or replaces its spell id on the player (the 0x02C2);</item>
/// <item>any other spell (a buff or debuff on another target, a heal, a portal, a transfer) has no proof on the wire (the target's enchantments and the spell's chat line are not
/// ours to read): the cast completes as <see cref="ActionOutcomeKind.Sent"/> when ACE ends it (UseDone 0), <c>TODO(m3-cast-other-target-proof)</c>;</item>
/// <item>without a spell table the kind is not known: the first of the enchantment and the projectile confirms, and UseDone 0 completes as Sent (<c>TODO(m3-cast-no-spell-table)</c>).</item>
/// </list></para>
/// <para>A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/> with that code; so is a fizzle (0x0402); so is a UseDone 0 that ends a self or projectile cast
/// without its effect (ACE's invalid target and its already-stronger buff end that way). The lane stays busy after the outcome until the UseDone: ACE is busy until then, and a
/// cast sent sooner is answered "You're too busy" (<see cref="HoldsLane"/>).</para>
/// </remarks>
internal sealed class CastTracker(ActionHandle handle, uint spellId, uint target) : ActionTracker(handle, ActionLane.Magic, ActionPolicy.CastSeconds)
{
    /// <summary>The WeenieError of a fizzle (ACE <c>WeenieError.YourSpellFizzled</c>).</summary>
    internal const uint FizzleCode = 0x0402;

    /// <summary>The WeenieError ACE's UseDone carries for a cast without the components (<c>YouDontHaveAllTheComponents</c>).</summary>
    internal const uint ComponentsCode = 0x0400;

    /// <summary>The WeenieError ACE's UseDone carries for a cast without the mana (<c>YouDontHaveEnoughManaToCast</c>).</summary>
    internal const uint ManaCode = 0x0401;

    /// <summary>How far from the player a projectile may appear and still be taken for the player's: ACE spawns it a body's width in front of the caster, so a few metres, not the ten of the first cut (M3 1.2 fix R2).</summary>
    internal const float ProjectileRangeMetres = 5f;

    /// <summary>How far, in degrees, a projectile's velocity may point away from the aim (the target's direction, or the player's heading for an untargeted cast) and still be taken for the cast's.</summary>
    internal const double ProjectileAimToleranceDegrees = 60;

    // What a Missile object was refused for while the cast was in flight (named in the outcome of a cast that ends without its projectile).
    private readonly List<string> _rejected = [];

    private Expectation _expect = Expectation.Unknown;
    private uint _self;
    private bool _finished;

    /// <summary>What a spell's outcome is looked for in.</summary>
    internal enum Expectation : byte
    {
        /// <summary>No spell table: the enchantment and the projectile both count.</summary>
        Unknown,

        /// <summary>The player gains the spell's enchantment.</summary>
        SelfEnchantment,

        /// <summary>A projectile appears.</summary>
        Projectile,

        /// <summary>Nothing on the wire proves it: UseDone 0 completes it as Sent.</summary>
        Other,
    }

    /// <summary>What the tracker looked for (known after the send).</summary>
    internal Expectation Expected => _expect;

    /// <inheritdoc/>
    internal override string WouldSend => target == 0
        ? "GameAction 0x0048 CastUntargetedSpell " + spellId
        : "GameAction 0x004A CastTargetedSpell " + spellId + " at " + ActionText.Hex(target);

    /// <inheritdoc/>
    internal override bool HoldsLane => !_finished;

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <summary>What the outcome of <paramref name="info"/> cast at <paramref name="at"/> (0: no target) is looked for in (a pure rule, tested).</summary>
    internal static Expectation Classify(SpellInfo? info, uint at, uint self)
    {
        if (info is null)
        {
            return Expectation.Unknown;
        }

        if (SpellVisualResolver.IsProjectileMetaType(info))
        {
            return Expectation.Projectile;
        }

        var enchantment = info.MetaSpellType is DatReaderWriter.Enums.SpellType.Enchantment or DatReaderWriter.Enums.SpellType.FellowEnchantment;
        if (enchantment && info.School == DatReaderWriter.Enums.MagicSchool.ItemEnchantment && (info.Flags & SpellFlags.SelfTargeted) == 0)
        {
            // M3 2.1 s0b (plan D23): an Item Enchantment that is not SelfTargeted (Impenetrability, Bludgeon Bane: NonComponentTargetType Vestements) cast with the caster as the target is redirected by ACE
            // to every worn clothing piece and shield (WorldObject_Magic.cs 2067-2121, TryCastItemEnchantment_WithRedirects; Player_Magic.cs 1391-1393 accepts a creature for Vestements). Each piece
            // gets "You cast <spell> on <item>" in the Magic chat (WorldObject_Magic.cs 430-452) and the player gets no GameEvent 0x02C2: only the cast's UseDone 0 ends it, whatever the target.
            return Expectation.Other;
        }

        var onSelf = at == 0 || at == self || (info.Flags & SpellFlags.SelfTargeted) != 0;
        return enchantment && onSelf ? Expectation.SelfEnchantment : Expectation.Other;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        // The stance guard (M3 4.2 p3, D40): retail's UI cannot send a cast outside the magic combat mode (the spell window and the cast keys exist only there, RegisterInputMaps 0x0056B760) and its client makes
        // no check of its own (ClientMagicSystem::CastSpell 0x00568DE0), so ACE refuses it with a silent UseDone. Checked here, at the lane's turn, after any SetCombatMode ahead of it in the Magic lane has been confirmed.
        // A Lifestone guard; --retail-cast-any-mode sends it as retail's client would.
        if (!api.RetailCastAnyMode && api.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            return new ActionOutcome(ActionOutcomeKind.Refused, 0, StanceGuards.CastRefusal);
        }

        _self = api.State.Self.Guid;
        _expect = Classify(api.FindSpell(spellId), target, _self);
        if (target == 0)
        {
            session!.SendCastUntargetedSpell(spellId);
        }
        else
        {
            session!.SendCastTargetedSpell(target, spellId);
        }

        return null;
    }

    /// <inheritdoc/>
    internal override void OnEnchantment(in EnchantmentChanged e)
    {
        if (_expect is not (Expectation.SelfEnchantment or Expectation.Unknown) || e.Change is not (EnchantmentChange.Added or EnchantmentChange.Replaced))
        {
            return;
        }

        foreach (var spell in e.Spells)
        {
            if (spell.SpellId == spellId)
            {
                Resolve(new ActionOutcome(
                    ActionOutcomeKind.Confirmed,
                    0,
                    "the player " + (e.Change == EnchantmentChange.Added ? "gained" : "refreshed") + " the enchantment (spell " + spellId + ", layer " + spell.Layer + ")"));
                return;
            }
        }
    }

    /// <inheritdoc/>
    internal override void OnObjectCreated(in ObjectCreated e, ActionApi api)
    {
        // A self spell is never confirmed by a projectile, even when no spell table says so: an untargeted or self-targeted cast of unknown kind is a buff, not a bolt.
        var selfCast = target == 0 || target == _self;
        if (_expect is not (Expectation.Projectile or Expectation.Unknown) || (_expect == Expectation.Unknown && selfCast) || e.IsSelf || !api.State.Objects.TryGet(e.Guid, out var obj)
            || (obj.State & PhysicsState.Missile) == 0)
        {
            return;
        }

        CellFrame? targetAt = target != 0 && target != _self && api.State.Objects.TryGet(target, out var victim) ? victim.Position : null;
        var refusal = RefuseProjectile(api.State.Self.Position, obj.Position, obj.Velocity, targetAt, api.ProjectileSetupLookup?.Invoke(spellId), obj.Physics.SetupId);
        if (refusal is not null)
        {
            _rejected.Add(ActionText.Hex(e.Guid) + ": " + refusal);
            return;
        }

        Resolve(new ActionOutcome(
            ActionOutcomeKind.Confirmed,
            0,
            ProjectileDetailPrefix + e.Guid.ToString("X8", CultureInfo.InvariantCulture) + " created (TODO(m3-cast-projectile-attribution): the wire does not name the caster; taken for ours by its origin within "
            + ActionText.Count((long)ProjectileRangeMetres) + " m of the player, its velocity away from the player and within " + ActionText.Count((long)ProjectileAimToleranceDegrees)
            + " degrees of the aim, and its Setup where the spell's is known; not proved: a missile another creature launched from the same spot along the same line)"));
    }

    /// <summary>The start of a projectile cast's Confirmed detail, <c>projectile 0x&lt;guid&gt; created (...)</c>: the outcome is where the tracker names the Missile object it took, and <see cref="TryReadProjectile"/> reads it back.</summary>
    internal const string ProjectileDetailPrefix = "projectile 0x";

    /// <summary>Reads the projectile's guid from a projectile cast's Confirmed outcome (<see cref="ProjectileDetailPrefix"/>); false for any other outcome. Allocation-free (a span parse).</summary>
    /// <param name="outcome">The cast's outcome.</param>
    /// <param name="guid">The projectile's guid.</param>
    internal static bool TryReadProjectile(in ActionOutcome outcome, out uint guid)
    {
        guid = 0;
        return outcome.Kind == ActionOutcomeKind.Confirmed
            && outcome.Detail is { } detail
            && detail.Length >= ProjectileDetailPrefix.Length + 8
            && detail.StartsWith(ProjectileDetailPrefix, StringComparison.Ordinal)
            && uint.TryParse(detail.AsSpan(ProjectileDetailPrefix.Length, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out guid);
    }

    /// <summary>
    /// Why a Missile object created during a cast is not the cast's projectile, or null when it may be (a pure rule, tested). ACE spawns the player's projectile just in front of the player
    /// and launches it along the aim; another creature's bolt starts at that creature and, aimed at the player, flies toward the player.
    /// </summary>
    /// <param name="player">The player's position, or null (unknown: refused, since nothing could be compared).</param>
    /// <param name="origin">The projectile's position, or null.</param>
    /// <param name="velocity">The projectile's velocity (AC axes, metres per second).</param>
    /// <param name="targetAt">The cast's target's position when it is known, else null (the aim is then the player's heading).</param>
    /// <param name="expectedSetup">The Setup the spell's projectile wears, or null when the host does not know it (not checked).</param>
    /// <param name="setup">The Setup the object wears.</param>
    internal static string? RefuseProjectile(CellFrame? player, CellFrame? origin, Vector3 velocity, CellFrame? targetAt, uint? expectedSetup, uint? setup)
    {
        if (player is not { } mine)
        {
            return "the player's position is unknown";
        }

        if (origin is not { } there)
        {
            return "it has no position";
        }

        if (expectedSetup is { } want && setup != want)
        {
            return "its Setup " + (setup is { } got ? ActionText.Hex(got) : "(none)") + " is not the spell's projectile " + ActionText.Hex(want);
        }

        var here = mine.ToGlobal();
        var from = there.ToGlobal();
        var offset = new Vector3((float)(from.X - here.X), (float)(from.Y - here.Y), (float)(from.Z - here.Z));
        if (offset.Length() > ProjectileRangeMetres)
        {
            return "it started " + offset.Length().ToString("F1", CultureInfo.InvariantCulture) + " m from the player";
        }

        if (velocity.LengthSquared() < 1e-6f)
        {
            return "it carries no velocity, so its heading is not provable";
        }

        var flat = new Vector2(offset.X, offset.Y);
        if (flat.Length() > 0.1f && Vector2.Dot(flat, new Vector2(velocity.X, velocity.Y)) < 0f)
        {
            return "it flies toward the player";
        }

        Vector3 aim;
        if (targetAt is { } victim)
        {
            var goal = victim.ToGlobal();
            aim = new Vector3((float)(goal.X - here.X), (float)(goal.Y - here.Y), (float)(goal.Z - here.Z));
        }
        else
        {
            aim = Vector3.Transform(Vector3.UnitY, mine.Orientation);
        }

        if (aim.LengthSquared() > 1e-6f)
        {
            var cos = Vector3.Dot(Vector3.Normalize(aim), Vector3.Normalize(velocity));
            if (cos < Math.Cos(ProjectileAimToleranceDegrees * Math.PI / 180.0))
            {
                return "its heading is " + (Math.Acos(Math.Clamp(cos, -1f, 1f)) * 180.0 / Math.PI).ToString("F0", CultureInfo.InvariantCulture)
                    + " degrees off the aim (" + (targetAt is null ? "the player's heading" : "the target") + ")";
            }
        }

        return null;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        if (e.Line.Kind != ChatKind.WeenieError || e.Line.ChatType != FizzleCode)
        {
            return false;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, FizzleCode, "the spell fizzled (WeenieError 0x0402)"));
        return true;
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        _finished = true;
        if (e.WeenieError != 0)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, e.WeenieError, "the server refused the cast: " + Describe(e.WeenieError)));
        }
        else if (_expect is Expectation.Other or Expectation.Unknown)
        {
            Resolve(new ActionOutcome(
                ActionOutcomeKind.Sent,
                0,
                _expect == Expectation.Other
                    ? "the server ended the cast; nothing on the wire proves its effect on another target (TODO(m3-cast-other-target-proof))"
                    : "the server ended the cast; the spell's kind is unknown without a spell table (TODO(m3-cast-no-spell-table))"));
        }

        // A UseDone 0 that ends a self or projectile cast without its effect is settled by Poll, after the tick's events.
        return true;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (!_finished || Resolution is not null)
        {
            return;
        }

        var hint = api.State.Self.Qualities.CombatMode != CombatModes.Magic ? " (the player is not in the magic combat mode)" : string.Empty;
        var seen = _rejected.Count == 0 ? string.Empty : "; not taken for ours: " + string.Join("; ", _rejected);
        Resolve(new ActionOutcome(
            ActionOutcomeKind.ServerError,
            0,
            "the server ended the cast without " + (_expect == Expectation.Projectile ? "a projectile" : "the enchantment") + hint + seen));
    }

    private static string Describe(uint code) => code switch
    {
        ComponentsCode => "the player lacks a spell component (WeenieError 0x0400)",
        ManaCode => "not enough mana (WeenieError 0x0401)",
        FizzleCode => "the spell fizzled (WeenieError 0x0402)",
        _ => "WeenieError " + ActionText.Hex(code),
    };
}

/// <summary>
/// <see cref="IActionApi.SetCombatMode"/>: GameAction 0x0053, as retail's <c>CM_Combat::Event_ChangeCombatMode</c> sends it (11.4186 0x006A9A70, 11.6096 0x006AA9D0). ACE changes the
/// mode after the stance animation and tells it with the private int update 0x02CD for property 40 (Player_Combat.cs, <c>HandleActionChangeCombatMode</c>); the update
/// to the mode asked for (<see cref="CombatModeChanged"/>) confirms. A request for the mode the player is in completes at once as <see cref="ActionOutcomeKind.Local"/>, nothing sent.
/// ACE refuses a weapon mismatch (a melee mode with a wand wielded) with WeenieError ActionCancelled and puts the player back in peace: that is a
/// <see cref="ActionOutcomeKind.ServerError"/>; any other silence times out. Lane Magic: ACE fails a cast in progress when the mode changes, so the change waits for a cast's end.
/// </summary>
internal sealed class SetCombatModeTracker(ActionHandle handle, CombatMode mode) : ActionTracker(handle, ActionLane.Magic, ActionPolicy.CombatModeSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0053 ChangeCombatMode " + (uint)mode;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (api.State.Self.Qualities.CombatMode == (int)mode)
        {
            return new ActionOutcome(ActionOutcomeKind.Local, 0, "the player is already in combat mode " + (uint)mode);
        }

        session!.SendChangeCombatMode((uint)mode);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnCombatMode(in CombatModeChanged e)
    {
        if (e.Mode == (int)mode)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "combat mode " + e.Previous + " -> " + e.Mode));
        }
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        if (e.Line.Kind != ChatKind.WeenieError || e.Line.ChatType != (uint)Lifestone.Data.Motion.WeenieError.ActionCancelled)
        {
            return false;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, e.Line.ChatType, "the server cancelled the change (WeenieError ActionCancelled): the wielded weapon does not fit the mode"));
        return true;
    }
}

/// <summary>
/// Lifestone's stance guards on the Action API (M3 4.2 p3): the switch <c>--retail-cast-any-mode</c> is per session (<c>GameSession.RetailCastAnyMode</c>, read by <c>CastTracker.Send</c> through the Action API).
/// </summary>
public static class StanceGuards
{
    /// <summary>The refusal detail of a cast outside the magic combat mode.</summary>
    public const string CastRefusal = "the player is not in the magic combat mode: retail's UI cannot cast outside it (Lifestone guard; --retail-cast-any-mode sends it and leaves ACE to refuse)";
}
