using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 the quest kill (live ladder64, 2026-11-17 18:50:10 to 18:53:48 UTC, the Atlan wand's step 22 in landblock 0xA66C): a Gout (weenie 21164, ResistFire 0 in the world database's table) attacked her walk; the station's kill cast Ilservian's Flame
/// for 0 points 50 times at "health ?", her mana ran down to 25 to 34 of 412 and waited with stamina 138 of 309 and no dance, until <c>kill: timeout</c> ended the errand for the run. The station's kill now takes the decisions the travel
/// fight-back takes, from the same shared pieces, each with one line:
/// <list type="bullet">
/// <item><b>The element</b>: the bolt is chosen again for each target (<see cref="WarBoltPick.ChooseCastableFor"/>, the line that hits its weenie hardest, the travel fight-back's pick).</item>
/// <item><b>The mana dance</b> (M5 D88, the owner's dance): a bolt she cannot pay is paid by the stamina dance first (<see cref="StaminaDance.Next"/>: Revitalize Self, then Stamina to Mana Self, the round as her live state reads it, cast
/// through <see cref="CastAttempt"/> as the fire dance casts it); the wait for the mana stands only when the dance cannot run, by name.</item>
/// <item><b>Life Magic when the bolts do not hurt</b>: <see cref="FightScenario.WallHitsBeforeBlock"/> bolts in a row at one target that come to nothing (no line, or a damage line of 0 points) give its bolts up, and Harm Other takes
/// it (<see cref="LifeStrike.Choose"/>, no projectile, the creature's own Magic Defense and drain resistance).</item>
/// <item><b>The run past</b> (D137): a target neither her bolts nor a Harm Other hurts (<see cref="FightScenario.LifeThroughMissesBeforeAside"/> life casts that come to nothing, or none to cast) is passed: no longer an attacker
/// to stop for, a target, or a walk's reason to stop; one line names it.</item>
/// <item><b>The known health</b>: a target's health is its maximum from the creature facts (<see cref="CreatureFacts.MaxHealthOf"/>) times the share the server reported, full while none came.</item>
/// </list>
/// </summary>
public abstract partial class QuestStepLeg
{
    /// <summary>The dance's casts toward one bolt at most (Lifestone's own bound: three rounds of a Revitalize and a press); past it the wait for the mana stands.</summary>
    public const int DanceCastsPerBolt = 6;

    private readonly Dictionary<uint, int> _noHurt = [];
    private readonly Dictionary<uint, int> _lifeMisses = [];
    private readonly HashSet<uint> _lifeOnly = [];
    private readonly HashSet<uint> _passed = [];
    private CastAttempt? _danceCast;
    private CastChatProof? _danceProof;
    private LegKindRead? _kinds;

    /// <summary>M6 16 E1e: the leg's read of a creature kind's maximum health and Magic Defense, naming once a kind what the bestiary does not hold (<see cref="LegKindRead"/>).</summary>
    private LegKindRead Kinds => _kinds ??= new LegKindRead("kill", text => Context.Log.Warn("station", string.Create(CultureInfo.InvariantCulture, $"[live {Name} t={S.Clock.Now:0.0}] {text}")));

    /// <summary>The creatures this leg ran past (the tests read it).</summary>
    internal IReadOnlyCollection<uint> Passed => _passed;

    /// <summary>The dance casts this leg sent (report-only).</summary>
    protected int DanceCasts { get; private set; }

    /// <summary>The target's health as data: its maximum from the creature facts times the server's reported share (full while none came), or the share alone when its maximum is not known.</summary>
    protected string KnownHealthOf(uint guid)
    {
        if (!S.State.Objects.TryGet(guid, out var o))
        {
            return "?";
        }

        var share = o.Health;
        if (CreatureFacts.All.MaxHealthOf(o.Weenie.WeenieClassId, out var source) is not { } max)
        {
            return share is { } f ? f.ToString("0.00", CultureInfo.InvariantCulture) + " (its maximum not known: " + source + ")" : "? (its maximum not known: " + source + ")";
        }

        var points = (int)Math.Ceiling((share ?? 1f) * max);
        return string.Create(CultureInfo.InvariantCulture, $"{points}/{max}{(share is null ? " (no report yet: full)" : string.Empty)}");
    }

    /// <summary>Its health in points for a pick (<see cref="KnownHealthOf"/>'s number), or null when its maximum is not known.</summary>
    private uint? HealthPoints(uint guid) =>
        S.State.Objects.TryGet(guid, out var o) && CreatureFacts.All.MaxHealthOf(o.Weenie.WeenieClassId, out _) is { } max ? (uint)Math.Max(1, Math.Ceiling((o.Health ?? 1f) * max)) : null;

    /// <summary>True for a creature this leg ran past, or (M6 16 Q8) one its skirmish left and has not taken back.</summary>
    private bool PassedBy(uint guid) => _passed.Contains(guid) || _left.ContainsKey(guid);

    /// <summary>She runs past <paramref name="guid"/> (see the class): one line, and it is neither a target nor an attacker to stop for again on this leg.</summary>
    private void RunPast(uint guid, string name, string why)
    {
        if (_passed.Add(guid))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"kill: {name} (0x{guid:X8}): {why}: she cannot hurt it, so she runs past it; it is no longer fought or stopped for on this leg (M6 14 run past, D137)"));
        }

        ForgetAttacker(name);
    }

    /// <summary>The Harm Other to cast at <paramref name="guid"/>, or null with <paramref name="why"/> (<see cref="LifeStrike.Choose"/> on her live Life Magic).</summary>
    private SpellInfo? LifeFor(uint guid, out string why)
    {
        var q = S.State.Self.Qualities;
        if (!GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var life, out why))
        {
            return null;
        }

        var initPlusRanks = q.TryGetSkill(MageTemplate.LifeMagic, out var skill) ? skill.Init + skill.Ranks : 0u;
        var kind = Kinds.Of(WcidOf(guid), NameOf(guid)); // M6 16 E1e: what the bestiary does not hold of the kind is named once a kind a leg
        var health = HealthPoints(guid) ?? (uint)FightScenario.LifeThroughUnknownHealth; // the old fallback for a maximum not known (the kind's read above names it)
        return LifeStrike.Choose(Context.Spells, S.State.Self.Spellbook.Knows, _boltsRefused, life, initPlusRanks, WarBoltPick.DefaultMinChance, in kind, Distance(guid), health, out why)?.Spell;
    }

    /// <summary>
    /// What the cast at <paramref name="guid"/> did (see the class): a damage or drain line of more than 0 points hurt it (the counts start again); a 0-point line or none after a confirmed cast came to nothing; a resist says nothing of it.
    /// At <see cref="FightScenario.WallHitsBeforeBlock"/> bolts that came to nothing Life Magic takes it; at <see cref="FightScenario.LifeThroughMissesBeforeAside"/> life casts that came to nothing she runs past it.
    /// </summary>
    private void NoteHurt(uint guid, string name, long mark, bool life, bool confirmed)
    {
        var (kind, amount) = HurtLineSince(mark, name);
        if (kind is MagicLineKind.DamageDealt or MagicLineKind.DrainDealt && amount > 0)
        {
            _noHurt.Remove(guid);
            _lifeMisses.Remove(guid);
            return;
        }

        if (kind == MagicLineKind.Resist || (kind == MagicLineKind.Unparsed && !confirmed))
        {
            return;
        }

        var words = kind == MagicLineKind.Unparsed ? "no line" : "0 points";
        if (life)
        {
            var misses = _lifeMisses[guid] = _lifeMisses.GetValueOrDefault(guid) + 1;
            Log(string.Create(CultureInfo.InvariantCulture, $"kill: the life cast at {name} (0x{guid:X8}) came to nothing ({words}; {misses} in a row, {FightScenario.LifeThroughMissesBeforeAside} pass it)"));
            if (misses >= FightScenario.LifeThroughMissesBeforeAside)
            {
                RunPast(guid, name, string.Create(CultureInfo.InvariantCulture, $"its bolts and {misses} life casts in a row came to nothing"));
            }

            return;
        }

        var n = _noHurt[guid] = _noHurt.GetValueOrDefault(guid) + 1;
        if (n >= FightScenario.WallHitsBeforeBlock && _lifeOnly.Add(guid))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"kill: {n} bolts in a row at {name} (0x{guid:X8}) came to nothing ({words}): its bolts are given up and Life Magic takes it (Harm Other, no projectile; M6 14 the quest kill)"));
        }
    }

    /// <summary>The first magic line since <paramref name="mark"/> naming <paramref name="name"/> that is her damage, a drain or a resist, with its points; <see cref="MagicLineKind.Unparsed"/> when none came.</summary>
    private (MagicLineKind Kind, long Amount) HurtLineSince(long mark, string name)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            if (chat[i].ChatType != FightTally.MagicChatType || chat[i].Kind != ChatKind.System || !chat[i].Text.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }

            var kind = FightTally.Classify(chat[i].Text, out var amount, out _);
            if (kind is MagicLineKind.DamageDealt or MagicLineKind.DrainDealt or MagicLineKind.Resist)
            {
                return (kind, amount);
            }
        }

        return (MagicLineKind.Unparsed, 0);
    }

    /// <summary>
    /// The stamina dance toward <paramref name="spell"/>'s mana (see the class): each cast check (<see cref="StaminaDance.Round"/> covers her; <see cref="StaminaDance.Next"/> on her live vitals toward the bolt's mana ceiling), act (the
    /// cast through <see cref="CastAttempt"/>), check (her vitals after), one line each; it ends at the line, when no cast nets, or at <see cref="DanceCastsPerBolt"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> DanceFor(SpellInfo spell)
    {
        var c = CultureInfo.InvariantCulture;
        var line = GearCasts.ManaCeiling(spell, 0);
        var afterRevitalize = false;
        for (var casts = 0; casts < DanceCastsPerBolt; casts++)
        {
            var q = S.State.Self.Qualities;
            if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || !q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax))
            {
                Log("kill: the dance: her mana or stamina is not read: no dance");
                yield break;
            }

            if (StaminaDance.Round(S.State, Context.Spells, out var coverWhy) is not { } round)
            {
                Log($"kill: the dance does not cover her mana: {coverWhy}");
                yield break;
            }

            var presses = StaminaPress.Levels(S.State, Context.Spells);
            var step = StaminaDance.Next(in round, presses, mana, manaMax, stamina, staminaMax, Math.Min(line, manaMax), afterRevitalize);
            if (step.Move is WalkDanceMove.Done or WalkDanceMove.Stop)
            {
                Log(string.Create(c, $"kill: the dance ends ({step.Move}): {step.Why}"));
                yield break;
            }

            var revitalize = step.Move == WalkDanceMove.Revitalize;
            var spellId = revitalize ? VitalsMeans.RevitalizeSelf[step.Index] : presses[step.Index].SpellId;
            _danceCast ??= new CastAttempt(S.Actions, S.State, Context.Log, Context.Spells);
            _danceProof ??= new CastChatProof(S.State, Context.Spells);
            var request = revitalize
                ? new CastRequest { Levels = VitalsMeans.RevitalizeSelf, Index = step.Index, Rules = CastRules.Utility(1), Purpose = "quest kill dance revitalize", Proof = _danceProof }
                : new CastRequest { Levels = VitalsMeans.StaminaToManaSelf, Index = LevelIndex(VitalsMeans.StaminaToManaSelf, spellId), Rules = CastRules.ManaDance(1), Purpose = "quest kill dance press", Proof = _danceProof };
            _danceCast.Start(request, S.Clock.Now);
            CastEnd end;
            while ((end = _danceCast.Step(S.Clock.Now)) == CastEnd.Running)
            {
                yield return null;
            }

            DanceCasts++;
            q.TryGetVitalLevels(MageTemplate.MaxMana, out var manaAfter, out _);
            q.TryGetVitalLevels(MageTemplate.MaxStamina, out var staminaAfter, out _);
            afterRevitalize = revitalize && staminaAfter > stamina;
            var name = Context.Spells.TryGetValue(spellId, out var info) ? info.Name : spellId.ToString(c);
            Log(string.Create(c, $"kill: the dance for {spell.Name} ({line} mana): {(revitalize ? "revitalize" : "press")} {name} ({end.Name()}): {step.Why}; stamina {stamina} -> {staminaAfter} of {staminaMax}, mana {mana} -> {manaAfter} of {manaMax}"));
        }
    }

    private static int LevelIndex(IReadOnlyList<uint> ids, uint id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// M6 14 the quest kill: a walk's fight-back whose kill fails is a failed walk (<c>go:walk-fight:...</c>, the errand's walk rule, <see cref="Autopilot.AutopilotScenario.LadderErrandWalkTries"/>), not the errand's end for the run.
    /// </summary>
    private static IEnumerable<ActionHandle?> AsWalkFailure(IEnumerable<ActionHandle?> inner)
    {
        using var e = inner.GetEnumerator();
        while (true)
        {
            bool more;
            try
            {
                more = e.MoveNext();
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("go: walk-fight: " + ex.Message, ex);
            }

            if (!more)
            {
                yield break;
            }

            yield return e.Current;
        }
    }
}
