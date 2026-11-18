using System.Globalization;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 the Zefir (the owner's ruling, 2026-11-16: a target her bolts cannot hurt is harmed with Life Magic; live ladder42 20:17:14-20:18:30 UTC, the Crater Pathway 0x01A7: 25 Ilservian's Flames at a Kilif Zefir 0x80006D61 8.7 m away, each projectile
/// created 1.8 m from her and deleted 5.1 s later 3.2 to 3.4 m from her, no damage or resist line, until the fight-back's 180 s timeout recalled her). ACE destroys a spell projectile 5.0 s after it impacts
/// (<c>SpellProjectile.ProjectileImpact</c>, SpellProjectile.cs:209-243 @95ef426d, called by <c>OnCollideEnvironment</c>, :250-261), so each bolt struck the world about 1.5 m out: the creature stood behind a wall from her.
/// The travel fight-back now counts the bolts at one target that resolve with no line (no damage, drain or resist line within the wait); at <see cref="FightScenario.WallHitsBeforeBlock"/> in a row (the hunting fight's own count of wall
/// hits) its bolts are given up and Harm Other takes it (<see cref="LifeStrike.Choose"/>, no projectile, no line asked), one line with the reason; <see cref="FightScenario.LifeThroughMissesBeforeAside"/> life casts in a row that come to
/// nothing, or no Harm Other to cast, leave it (the walk goes on; the safety lines still read its hits). A line resets both counts.
/// <para>M6 16 E1e: the target's maximum health and Magic Defense are the bestiary's, read through the host's named read (<see cref="LegKindRead"/>): where the bestiary holds no maximum health for the kind, or does not hold
/// the kind, the life cast still takes a large target (<see cref="FightScenario.LifeThroughUnknownHealth"/>) and a Magic Defense of 0, and one warning a kind a leg says so. Before, both were taken without a word.</para>
/// <para>M6 16 Q5 (<c>TODO(engine-health-flip)</c>): the target's health is the session's one picture's (<see cref="HealthPicture"/>: the server's last word, or its maximum, less what her lines dealt it since, plus what it
/// regenerated), rounded up as before; where the picture holds no maximum for it, the old read (the server's last share of the bestiary's maximum, else that maximum or the large target). Before, none of her damage was counted.
/// It reaches every host of the skirmish: the travel leg, the quest steps and the harm-heal station.</para>
/// </summary>
internal sealed partial class Skirmish
{
    private readonly HashSet<uint> _boltBlocked = [];
    private readonly Dictionary<uint, int> _noLineRun = [];
    private readonly Dictionary<uint, int> _lifeMissRun = [];
    private string _lifeWhy = string.Empty;

    /// <summary>The attackers whose bolts came to nothing on this leg (the tests read it).</summary>
    internal IReadOnlyCollection<uint> BoltBlocked => _boltBlocked;

    /// <summary>The Harm Other to cast at <paramref name="guid"/>, or null with <see cref="_lifeWhy"/> naming why none.</summary>
    private LifeStrikePick? LifeStrikeAt(uint guid, uint wcid, double metres)
    {
        var q = S.State.Self.Qualities;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out var why))
        {
            _lifeWhy = why;
            return null;
        }

        var initPlusRanks = q.TryGetSkill(MageTemplate.LifeMagic, out var skill) ? skill.Init + skill.Ranks : 0u;
        var seen = S.State.Objects.TryGet(guid, out var o);
        var kind = host.Kinds.Of(wcid, seen && !string.IsNullOrEmpty(o!.Name) ? o.Name! : "a creature");
        var max = kind.MaxHealth ?? (int)FightScenario.LifeThroughUnknownHealth; // the old fallback, named once a kind a leg (LegKindRead)
        var read = ThreatBoard.For(S).Health.Read(guid, S.Clock.Now); // M6 16 Q5: the one picture
        var health = read.Known ? (uint)Math.Ceiling(read.Health) : seen && o!.Health is { } share ? (uint)Math.Ceiling(share * max) : (uint)max;
        var book = S.State.Self.Spellbook;
        var pick = LifeStrike.Choose(Context.Spells, book.Knows, _boltsRefused, life, initPlusRanks, FightBackChance, in kind, metres, Math.Max(1u, health), out _lifeWhy);
        return pick;
    }

    /// <summary>A cast at <paramref name="guid"/> resolved with no line (<paramref name="resolved"/> false) or with one: the counts, the switch to Life Magic and the leaving, each one line.</summary>
    /// <param name="guid">The target.</param>
    /// <param name="name">Its name.</param>
    /// <param name="spell">The spell cast.</param>
    /// <param name="lifeCast">True for a life cast (Harm Other), false for a bolt.</param>
    /// <param name="resolved">True when a damage, drain or resist line came.</param>
    /// <param name="flew">The cast was confirmed (a bolt's projectile created, a life spell sent and answered): a cast the server ended otherwise (a refusal, no projectile) is the cast policy's and the refusal rules', not a wall's.</param>
    private void NoteNoLine(uint guid, string name, string spell, bool lifeCast, bool resolved, bool flew)
    {
        var c = CultureInfo.InvariantCulture;
        if (resolved)
        {
            _noLineRun.Remove(guid);
            _lifeMissRun.Remove(guid);
            return;
        }

        if (!flew && !lifeCast)
        {
            return; // no projectile flew: nothing struck a wall
        }

        if (!lifeCast)
        {
            var n = _noLineRun[guid] = _noLineRun.GetValueOrDefault(guid) + 1;
            if (n >= FightScenario.WallHitsBeforeBlock && _boltBlocked.Add(guid))
            {
                Log(string.Create(c, $"fight-back: '{name}' 0x{guid:X8}: {n} bolts in a row came to nothing (no damage or resist line: a projectile that strikes the world is destroyed 5 s after, SpellProjectile.ProjectileImpact @95ef426d): its bolts are given up and Life Magic takes it, Harm Other needing no line (M6 14 the Zefir, the owner's rule: m6-life-through on the road)"));
            }

            return;
        }

        var misses = _lifeMissRun[guid] = _lifeMissRun.GetValueOrDefault(guid) + 1;
        Log(string.Create(c, $"fight-back: {spell} at '{name}' 0x{guid:X8} came to nothing ({misses} in a row, {FightScenario.LifeThroughMissesBeforeAside} leave it)"));
    }

    /// <summary>True when the life casts at <paramref name="guid"/> have come to nothing <see cref="FightScenario.LifeThroughMissesBeforeAside"/> times in a row.</summary>
    private bool LifeSpent(uint guid) => _lifeMissRun.GetValueOrDefault(guid) >= FightScenario.LifeThroughMissesBeforeAside;
}
