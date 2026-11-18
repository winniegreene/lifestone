using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Threat;

/// <summary>What a creature's health in the <see cref="HealthPicture"/> stands on.</summary>
public enum HealthBasis : byte
{
    /// <summary>Not known: no maximum (the bestiary does not hold its weenie class and no appraisal told one), or no creature of that guid.</summary>
    Unknown,

    /// <summary>Never told and nothing dealt: its maximum, the plan's rule (known, never assumed: the maximum is the world database's).</summary>
    Full,

    /// <summary>Its maximum less the damage her own lines report, plus what it regenerated since the first of them.</summary>
    Counted,

    /// <summary>The server's last health update (UpdateHealth, GameEvent 0x01C0: a share of its maximum), less what she dealt it since, plus what it regenerated since.</summary>
    Reported,

    /// <summary>Its last appraisal (its health and its maximum in points, the server's own), less what she dealt it since, plus what it regenerated since.</summary>
    Appraised,
}

/// <summary>Where a creature's maximum health in the <see cref="HealthPicture"/> comes from.</summary>
public enum HealthMaxSource : byte
{
    /// <summary>Nowhere.</summary>
    None,

    /// <summary>The bestiary (<see cref="Bestiary.Find"/>: a ground profile's <c>max_health</c> first, then the world database's table, as ACE computes it).</summary>
    Bestiary,

    /// <summary>This creature's own appraisal (the server's word for this guid, enchantments included).</summary>
    Appraisal,
}

/// <summary>One read of the <see cref="HealthPicture"/>. A value: no allocation.</summary>
/// <param name="Basis">What the health stands on.</param>
/// <param name="Health">Its health now, in points (at least 1 while it stands, at most <paramref name="Max"/>); 0 when unknown.</param>
/// <param name="Max">Its maximum health, or 0 when unknown.</param>
/// <param name="MaxSource">Where the maximum comes from.</param>
/// <param name="Base">The value the count started from: the server's last word in points, or the maximum.</param>
/// <param name="BaseAge">The seconds since that base was set (infinity for a creature never told and never hit).</param>
/// <param name="Dealt">What her lines took from it since the base.</param>
/// <param name="Regen">What it regenerated since the base, by its kind's rate.</param>
/// <param name="PerSecond">Its kind's health regeneration, points a second (0: none, or not recorded).</param>
/// <param name="Tick">Its kind's health regeneration a heartbeat, the size of one of ACE's steps.</param>
public readonly record struct HealthRead(HealthBasis Basis, double Health, int Max, HealthMaxSource MaxSource, double Base, double BaseAge, double Dealt, double Regen, double PerSecond, double Tick)
{
    /// <summary>True when the health is known.</summary>
    public bool Known => Basis != HealthBasis.Unknown;

    /// <summary>True when the base is the server's own word (an update or an appraisal).</summary>
    public bool Told => Basis is HealthBasis.Reported or HealthBasis.Appraised;
}

/// <summary>The creature the <see cref="HealthPicture"/> forgot last (its death or its removal), with its health just before.</summary>
/// <param name="Guid">Its guid, or 0 when none was forgotten yet.</param>
/// <param name="Health">Its health as the picture held it just before.</param>
/// <param name="Max">Its maximum.</param>
/// <param name="At">The session time it was forgotten at.</param>
public readonly record struct HealthForgotten(uint Guid, double Health, int Max, double At);

/// <summary>
/// M6 16 E2d (docs/automation-engine.md section 2.2 and stage E2, m6-plan.md D139): the one picture of a target's health, a session. The plan's rule: a creature's health is known, never assumed. Its maximum comes from the
/// world database (the bestiary), corrected by the creature's own appraisal; its health now is that maximum less the damage her own lines report, corrected by what the server tells (a health update, an appraisal), plus what
/// the creature regenerated in between by its kind's own rate and heartbeat. Today two models answer this, the fight's (<c>Mage/TargetHealthModel</c>) and the guard's and travel legs' (<c>Vitals/KnownTargetHealth</c>),
/// each with its own maximum, its own count of her damage and its own trust in the server's updates, and each dies with its owner. This picture is the threat board's (<see cref="ThreatBoard.Health"/>), so it lives as long
/// as the session and what one fight learned of a creature the next leg knows. In this slice it decides nothing: both models decide as before and the picture is asked the same beside them (<see cref="HealthShadow"/>).
/// </summary>
/// <remarks>
/// <para><b>The feed</b> (one subscription each, made once): the server's health updates (<see cref="HealthUpdated"/>), the appraisals (<see cref="AppraisalReceived"/>), her damage lines (<see cref="ChatReceived"/>, read by
/// the parsers both models read them with: <see cref="FightTally.Classify"/> and <see cref="FightScenario.DealtTarget"/> for a war spell's line, <see cref="FightTally.TryHarmDealt"/> for a Harm Other's), a creature's Dead
/// motion and <see cref="ObjectDeleted"/>. A Drain Health Other's take is told by the caster of the drain (<see cref="Took"/>): its gain line names her gain and the creature's name only, and the caster alone knows the guid
/// and the spell's loss and cap.</para>
/// <para><b>Whose line.</b> A damage line names its creature by name alone. M6 16 Q5: where the caster knows the creature its cast is at (the skirmish, <see cref="Aim"/>), a line naming that creature's name while the aim
/// holds is that creature's; else the picture gives it to the nearest living creature of that name where the client draws it, the fight's rule (the fight's own bolts tell no aim yet: two creatures of one name near her
/// in a fight can still trade damage until the server's next word on either, <c>TODO(engine-health-attribution)</c>).</para>
/// <para><b>The same blow.</b> ACE sends the selected target's new health as a blow lands and writes the damage line after it, so a line within <see cref="SameBlowSeconds"/> of the server's word is in that word and is not
/// taken off again (the fight's rule and its number).</para>
/// <para><b>Regeneration.</b> ACE adds a creature's HealthRate to its health once a heartbeat while it is under its maximum, with no other factor for a creature that is no player and bears no regeneration enchantment
/// (<c>Creature.VitalHeartBeat</c>, Creature_Vitals.cs:102-168; the attribute and stance factors are a player's alone, :174-177 and :210-213; the heartbeat is the weenie's HeartbeatInterval, 5 s where it states none,
/// WorldObject_Tick.cs:44-56; all @95ef426d). The client cannot see a creature's heartbeats, so the picture adds the rate a second (<see cref="CreatureVitals.HealthPerSecond"/>) over the time passed, up to the maximum: at
/// most one heartbeat's step from the server's count.</para>
/// <para><b>Forgetting.</b> A creature is forgotten at its death (its Dead motion, or a health update of 0) and at its removal; the last one forgotten is kept with its health just before (<see cref="LastForgotten"/>).</para>
/// <para><b>Allocation.</b> Game thread only. A read allocates nothing, except the one entry of a creature first read with a health the object table held from before the picture listened. The feed allocates one entry a
/// creature, once, and for a damage line what naming its creature allocates.</para>
/// </remarks>
public sealed class HealthPicture
{
    /// <summary>A damage line this many seconds after the server's word on its creature is taken as the same blow (<see cref="FightScenario.PushSameEventSeconds"/>: one number for both).</summary>
    public const double SameBlowSeconds = FightScenario.PushSameEventSeconds;

    /// <summary>The creatures held at most; past it the ones the object table no longer holds are dropped.</summary>
    public const int MaxEntries = 512;

    /// <summary>
    /// M6 16 Q5: the seconds an aim holds at most (<see cref="Aim"/>), Lifestone's own bound: a skirmish aims as it starts a cast and releases after its bolt's wait (2 s, <c>Skirmish.BoltWaitSeconds</c>), so an aim
    /// that outlives this was left by a caster that stopped mid-cast, and the nearest rule takes its lines again.
    /// </summary>
    public const double AimHoldSeconds = 10.0;

    private const uint CreatureItemType = 0x10; // ACE ItemType.Creature

    private static readonly ConditionalWeakTable<GameState, HealthPicture> PerSession = new();

    private readonly IGameSessionView _session;
    private readonly Dictionary<uint, Entry> _entries = [];
    private readonly List<uint> _gone = [];
    private readonly Dictionary<string, (uint Guid, double At)> _aims = new(StringComparer.Ordinal);

    private HealthPicture(IGameSessionView session)
    {
        _session = session;
        HearingSince = session.Clock.Now;
        Shadow = new HealthShadow(this);
        var bus = session.Events; // the subscriptions live as long as the session: the picture is the session's
        bus.Subscribe<HealthUpdated>(OnHealth);
        bus.Subscribe<AppraisalReceived>(OnAppraisal);
        bus.Subscribe<ChatReceived>(OnChat);
        bus.Subscribe<ObjectMotionChanged>(OnMotion);
        bus.Subscribe<ObjectDeleted>(OnDeleted);
    }

    /// <summary>The picture of a session, made (and its feed begun) on first use. The session's threat board makes it with itself (<see cref="ThreatBoard.Health"/>).</summary>
    /// <param name="session">The session.</param>
    public static HealthPicture For(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        if (!PerSession.TryGetValue(state, out var picture))
        {
            picture = new HealthPicture(session);
            PerSession.Add(state, picture);
        }

        return picture;
    }

    /// <summary>The session time the picture began hearing at.</summary>
    public double HearingSince { get; }

    /// <summary>The shadow beside the two models of today (report-only).</summary>
    public HealthShadow Shadow { get; }

    /// <summary>The creatures held.</summary>
    public int Count => _entries.Count;

    /// <summary>The server's health updates taken this session.</summary>
    public long Reports { get; private set; }

    /// <summary>The appraisals taken this session.</summary>
    public long Appraisals { get; private set; }

    /// <summary>Her damage lines given to a creature this session.</summary>
    public long Lines { get; private set; }

    /// <summary>Her damage lines whose creature was not found alive in the object table (it died of the blow, or left).</summary>
    public long Unplaced { get; private set; }

    /// <summary>The drain takes told by their casters this session.</summary>
    public long Takes { get; private set; }

    /// <summary>M6 16 Q5: her damage lines given to the creature her caster aimed at (<see cref="Aim"/>), not by the nearest rule, this session.</summary>
    public long AimedLines { get; private set; }

    /// <summary>The creature forgotten last, with its health just before.</summary>
    public HealthForgotten LastForgotten { get; private set; }

    /// <summary>
    /// The health of <paramref name="guid"/> at <paramref name="now"/>: its base (the server's last word, else its maximum) less what she dealt it since, plus what it regenerated since. <see cref="HealthBasis.Unknown"/> for
    /// a guid that is no creature in the object table, or one whose maximum no source holds.
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="now">The session clock.</param>
    public HealthRead Read(uint guid, double now)
    {
        if (_entries.TryGetValue(guid, out var held))
        {
            return Of(held, now);
        }

        if (!_session.State.Objects.TryGet(guid, out var o) || (o.ItemType & CreatureItemType) == 0 || o.Guid == _session.State.Self.Guid || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
        {
            return default; // no creature, or one that lies dead: forgotten
        }

        if (o.Health is { } before and > 0f && EntryOf(guid, now) is { } seeded)
        {
            Rebase(seeded, HealthBasis.Reported, before * seeded.Max, now); // a value the object table held from before the picture listened: the server's last word, its age not known
            return Of(seeded, now);
        }

        var vitals = Bestiary.Find(o.Weenie.WeenieClassId)?.Vitals;
        return vitals?.MaxHealth is { } max and > 0
            ? new HealthRead(HealthBasis.Full, max, max, HealthMaxSource.Bestiary, max, double.PositiveInfinity, 0, 0, vitals.Value.HealthPerSecond ?? 0, vitals.Value.HealthRate ?? 0)
            : default;
    }

    /// <summary>
    /// M6 16 Q5: <see cref="Read"/>, or for the creature the picture forgot last (its death or its removal) its health just before (a read after a creature's death: the guard's killing blow).
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="now">The session clock.</param>
    public HealthRead ReadOrLast(uint guid, double now)
    {
        var last = LastForgotten;
        if (guid != 0 && guid == last.Guid && !_entries.ContainsKey(guid))
        {
            return last.Max <= 0 ? default : new HealthRead(HealthBasis.Counted, last.Health, last.Max, HealthMaxSource.Bestiary, last.Health, now - last.At, 0, 0, 0, 0);
        }

        return Read(guid, now);
    }

    /// <summary>
    /// M6 16 Q5 (<c>TODO(engine-health-attribution)</c>, decided for the casters that know their target): her cast is at <paramref name="guid"/>. Until <see cref="Release"/>, another aim at a creature of the same name, the
    /// creature's death or <see cref="AimHoldSeconds"/>, a damage line naming its name is its own, not the nearest creature's of that name. A caster that knows no target tells nothing, and the nearest rule stands.
    /// Allocates only for a name aimed at the first time.
    /// </summary>
    /// <param name="guid">The creature the cast is at.</param>
    public void Aim(uint guid)
    {
        if (guid != 0 && _session.State.Objects.TryGet(guid, out var o) && !string.IsNullOrEmpty(o.Name))
        {
            _aims[o.Name] = (guid, _session.Clock.Now);
        }
    }

    /// <summary>M6 16 Q5: the cast at <paramref name="guid"/> has resolved: its aim ends (the nearest rule takes a later line of its name).</summary>
    /// <param name="guid">The creature the cast was at.</param>
    public void Release(uint guid)
    {
        if (guid != 0 && _session.State.Objects.TryGet(guid, out var o) && !string.IsNullOrEmpty(o.Name) && _aims.TryGetValue(o.Name, out var aim) && aim.Guid == guid)
        {
            _aims.Remove(o.Name);
        }
    }

    /// <summary>
    /// A Drain Health Other of hers took <paramref name="points"/> from <paramref name="guid"/>: the drain's caster tells it (ACE's gain line names her gain; the caster turns it back into the creature's loss, the gain
    /// over 1 minus the spell's loss, cut to its cap: <c>WorldObject_Magic.cs:721-747</c> @95ef426d).
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="points">What the drain took.</param>
    public void Took(uint guid, double points)
    {
        var now = _session.Clock.Now;
        if (points > 0 && EntryOf(guid, now) is { } entry)
        {
            Takes++;
            Deal(entry, points, now);
        }
    }

    /// <summary>The weenie class the picture holds <paramref name="guid"/> as, or 0.</summary>
    /// <param name="guid">The creature.</param>
    public uint WcidOf(uint guid) => _entries.TryGetValue(guid, out var e) ? e.Wcid : 0;

    internal HealthShadowState? ShadowStateOf(uint guid, bool make, double now)
    {
        var entry = _entries.TryGetValue(guid, out var held) ? held : make ? EntryOf(guid, now) : null;
        return entry is null ? null : make ? entry.Shadow ??= new HealthShadowState() : entry.Shadow;
    }

    internal string NameOf(uint guid) =>
        _session.State.Objects.TryGet(guid, out var o) && !string.IsNullOrEmpty(o.Name) ? o.Name : _entries.TryGetValue(guid, out var e) ? e.Name : "creature";

    private static HealthRead Of(Entry e, double now)
    {
        if (e.Max <= 0)
        {
            return new HealthRead(HealthBasis.Unknown, 0, 0, HealthMaxSource.None, 0, now - e.BaseAt, e.Dealt, 0, e.PerSecond, e.Tick);
        }

        var (value, regen) = (e.Value, e.Regen);
        if (value < e.Max && e.PerSecond > 0 && now > e.ValueAt)
        {
            var grown = Math.Min(e.Max - value, e.PerSecond * (now - e.ValueAt));
            (value, regen) = (value + grown, regen + grown);
        }

        return new HealthRead(e.Basis, Math.Clamp(value, 1, e.Max), e.Max, e.MaxSource, e.BaseValue, e.Basis == HealthBasis.Full ? double.PositiveInfinity : now - e.BaseAt, e.Dealt, regen, e.PerSecond, e.Tick);
    }

    // ------------------------------------------------------------------------------------------------ the feed

    private void OnHealth(in HealthUpdated e)
    {
        if (e.Health <= 0f)
        {
            Forget(e.Guid); // the server says it is dead
            return;
        }

        var now = _session.Clock.Now;
        if (EntryOf(e.Guid, now) is { } entry)
        {
            Reports++;
            Rebase(entry, HealthBasis.Reported, Math.Clamp(e.Health, 0f, 1f) * entry.Max, now);
        }
    }

    private void OnAppraisal(in AppraisalReceived e)
    {
        var now = _session.Clock.Now;
        if (!e.Success || !_session.State.Appraisals.TryGet(e.Guid, out var answer) || answer.CreatureHealthMax is not { } max || max == 0 || answer.CreatureHealth is not { } health || health == 0
            || EntryOf(e.Guid, now) is not { } entry)
        {
            return;
        }

        Appraisals++;
        (entry.Max, entry.MaxSource) = ((int)max, HealthMaxSource.Appraisal);
        Rebase(entry, HealthBasis.Appraised, Math.Min(health, max), now);
    }

    private void OnChat(in ChatReceived e)
    {
        var line = e.Line;
        if (line.Kind != ChatKind.System || line.ChatType != FightTally.MagicChatType || string.IsNullOrEmpty(line.Text))
        {
            return;
        }

        string victim;
        long amount;
        if (FightTally.TryHarmDealt(line.Text, out amount, out _, out var harmed))
        {
            victim = harmed.ToString();
        }
        else if (FightTally.Classify(line.Text, out amount, out _) == MagicLineKind.DamageDealt)
        {
            victim = FightScenario.DealtTarget(line.Text);
        }
        else
        {
            return;
        }

        var state = _session.State;
        var now = _session.Clock.Now;
        if (amount > 0 && victim.Length > 0 && AimedAt(victim, now) is var aimed and not 0 && EntryOf(aimed, now) is { } mark)
        {
            Lines++;
            AimedLines++;
            Deal(mark, amount, now); // M6 16 Q5: the creature her caster aimed at
            return;
        }

        if (amount <= 0 || victim.Length == 0 || state.Self.Position is not { } here || !state.Objects.NearestLivingCreatureNamed(victim, here, state.Self.Guid, out var guid, _session.Live)
            || EntryOf(guid, now) is not { } entry)
        {
            Unplaced++;
            return;
        }

        Lines++;
        Deal(entry, amount, now);
    }

    /// <summary>The living creature aimed at under <paramref name="name"/> while its aim holds, or 0.</summary>
    private uint AimedAt(string name, double now)
    {
        if (_aims.Count == 0 || !_aims.TryGetValue(name, out var aim))
        {
            return 0;
        }

        if (now - aim.At > AimHoldSeconds || !_session.State.Objects.TryGet(aim.Guid, out var o) || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || o.Health is <= 0f)
        {
            _aims.Remove(name); // lapsed, or the creature is gone or dead: the nearest rule
            return 0;
        }

        return aim.Guid;
    }

    private void OnMotion(in ObjectMotionChanged e)
    {
        if (!e.IsSelf && e.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
        {
            Forget(e.Guid);
        }
    }

    private void OnDeleted(in ObjectDeleted e) => Forget(e.Guid);

    private void Forget(uint guid)
    {
        if (_entries.Remove(guid, out var entry))
        {
            var now = _session.Clock.Now;
            LastForgotten = new HealthForgotten(guid, Of(entry, now).Health, entry.Max, now);
        }
    }

    /// <summary>The server's word: the count starts again from it.</summary>
    private static void Rebase(Entry e, HealthBasis basis, double value, double now)
    {
        (e.Basis, e.Value, e.BaseValue, e.ValueAt, e.BaseAt, e.Dealt, e.Regen) = (basis, value, value, now, now, 0, 0);
    }

    /// <summary>Her damage: off the value, unless the server's word of the same blow already holds it.</summary>
    private static void Deal(Entry e, double points, double now)
    {
        if (e.Basis is HealthBasis.Reported or HealthBasis.Appraised && now - e.BaseAt <= SameBlowSeconds)
        {
            return;
        }

        if (e.Basis == HealthBasis.Full)
        {
            (e.Basis, e.BaseAt, e.ValueAt) = (HealthBasis.Counted, now, now); // until now it stood at its maximum: nothing to regenerate
        }
        else if (e.Value < e.Max && e.PerSecond > 0 && now > e.ValueAt)
        {
            var grown = Math.Min(e.Max - e.Value, e.PerSecond * (now - e.ValueAt));
            (e.Value, e.Regen) = (e.Value + grown, e.Regen + grown);
        }

        (e.Value, e.ValueAt, e.Dealt) = (Math.Max(0, e.Value - points), now, e.Dealt + points);
    }

    private Entry? EntryOf(uint guid, double now)
    {
        if (_entries.TryGetValue(guid, out var entry))
        {
            return entry;
        }

        var state = _session.State;
        if (guid == 0 || guid == state.Self.Guid || !state.Objects.TryGet(guid, out var o) || (o.ItemType & CreatureItemType) == 0 || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
        {
            return null; // no creature, or one that lies dead: never held again
        }

        if (_entries.Count >= MaxEntries)
        {
            Prune();
        }

        var wcid = o.Weenie.WeenieClassId;
        var vitals = Bestiary.Find(wcid)?.Vitals;
        var (max, source) = vitals?.MaxHealth is { } recorded and > 0 ? (recorded, HealthMaxSource.Bestiary) : (0, HealthMaxSource.None);
        if (state.Appraisals.TryGet(guid, out var answer) && answer.CreatureHealthMax is { } appraised and > 0)
        {
            (max, source) = ((int)appraised, HealthMaxSource.Appraisal); // its own maximum by an appraisal from before the picture held it; that appraisal's health is of no known age and is not taken
        }

        entry = new Entry(guid, wcid, o.Name ?? "creature")
        {
            Max = max,
            MaxSource = source,
            PerSecond = vitals?.HealthPerSecond ?? 0,
            Tick = vitals?.HealthRate ?? 0,
            Value = max,
            BaseValue = max,
            ValueAt = now,
            BaseAt = now,
        };
        _entries[guid] = entry;
        return entry;
    }

    private void Prune()
    {
        _gone.Clear();
        foreach (var guid in _entries.Keys)
        {
            if (!_session.State.Objects.TryGet(guid, out _))
            {
                _gone.Add(guid);
            }
        }

        foreach (var guid in _gone)
        {
            _entries.Remove(guid);
        }
    }

    private sealed class Entry(uint guid, uint wcid, string name)
    {
        public uint Guid { get; } = guid;

        public uint Wcid { get; } = wcid;

        public string Name { get; } = name;

        public int Max;
        public HealthMaxSource MaxSource;
        public double PerSecond;
        public double Tick;
        public HealthBasis Basis = HealthBasis.Full;
        public double Value;
        public double ValueAt;
        public double BaseValue;
        public double BaseAt;
        public double Dealt;
        public double Regen;
        public HealthShadowState? Shadow;
    }
}
