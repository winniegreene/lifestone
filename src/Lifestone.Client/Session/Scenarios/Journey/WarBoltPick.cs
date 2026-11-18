using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 6 E (<c>m6-fight-back-war-spell</c>): the one pick of a War Magic bolt for the journey's fights outside the hunt (the run leg's fight-back, <see cref="TravelLeg"/>; the quest steps' kills, <see cref="QuestStepLeg"/>). Of the War Magic projectiles
/// the book knows and the SpellTable holds, the highest power whose cast chance on the current War Magic skill (<see cref="GearCasts.Chance"/>, ACE's pre-check logistic) is at least the floor; when none reaches it (or the skill is not read), the
/// lowest power. Ties go to the lower spell id. The lead's ruling (item 6's notes, "The contract"): both callers pass the run leg's floor, 0.5. A book with no War Magic projectile is null: each caller's named stop stays its own.
/// </summary>
public static class WarBoltPick
{
    /// <summary>The cast chance floor both callers pass (Lifestone's own, the run leg's since M6 4 F6; the buff cycle's and the hunt's plan chance are their own).</summary>
    public const double DefaultMinChance = 0.5;

    /// <summary>Picks the bolt; <paramref name="why"/> says which rule chose it (logged by the caller), <c>none</c> with no War Magic projectile known.</summary>
    /// <param name="table">The SpellTable (the journey's <c>Spells</c>).</param>
    /// <param name="known">The spell ids the book holds.</param>
    /// <param name="warSkill">The current War Magic skill, or null when it is not read (the lowest power is then the pick).</param>
    /// <param name="minChance">The chance a pick must reach on <paramref name="warSkill"/>.</param>
    /// <param name="why">The choice in words.</param>
    public static SpellInfo? Choose(IReadOnlyDictionary<uint, SpellInfo> table, IEnumerable<uint> known, uint? warSkill, double minChance, out string why)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(known);
        SpellInfo? best = null;
        SpellInfo? lowest = null;
        foreach (var id in known)
        {
            if (!table.TryGetValue(id, out var info) || info.School != MagicSchool.WarMagic || info.MetaSpellType != SpellType.Projectile)
            {
                continue;
            }

            if (lowest is null || info.Power < lowest.Power || (info.Power == lowest.Power && info.Id < lowest.Id))
            {
                lowest = info;
            }

            if (warSkill is { } s && GearCasts.Chance(s, info.Power) >= minChance && (best is null || info.Power > best.Power || (info.Power == best.Power && info.Id < best.Id)))
            {
                best = info;
            }
        }

        why = best is not null
            ? string.Create(CultureInfo.InvariantCulture, $"power {best.Power}, chance {GearCasts.Chance(warSkill!.Value, best.Power):0.00} on War Magic {warSkill}")
            : lowest is null ? "none"
            : warSkill is null ? "the lowest power: the War Magic skill is not read"
            : string.Create(CultureInfo.InvariantCulture, $"the lowest power: no bolt reaches {minChance:0.00} on War Magic {warSkill}");
        return best ?? lowest;
    }

    /// <summary>
    /// M6 6 R1F (live round 1: the run leg's fight-back cast Flame Bolt IV four times into ACE's 0x0400 "the player lacks a spell component" while a tusker beat the climber down): the fight-backs' one pick over the bolts the character can cast
    /// now, shared by the run leg (<see cref="TravelLeg"/>), the harm-heal station (<see cref="HarmHealRun"/>) and, M6 7 K, the fight (<see cref="FightScenario"/>, over its bolt line's levels). Of the War Magic projectiles the book knows and the table holds, a bolt ACE refused for a missing component on this leg
    /// (<paramref name="refused"/>) is dropped, and so is one the pack is short of a component for (<see cref="LevelFloor.ShortOf"/> over <paramref name="held"/>: the foci formula, or the account's formula with <paramref name="accountName"/> and no
    /// focus; ACE's <c>HasComponentsForSpell</c>, Player_Magic.cs:1227-1248 @95ef426d); <see cref="Choose"/> picks among the rest. <paramref name="held"/> null (the pack not read or not settled, or no War Magic focus and no account name:
    /// <see cref="LevelFloor.HeldForAccount"/>) judges no component before the cast, and says so: the refusal then drops the bolt.
    /// </summary>
    /// <param name="table">The SpellTable.</param>
    /// <param name="known">The spell ids the book holds.</param>
    /// <param name="warSkill">The current War Magic skill, or null when it is not read.</param>
    /// <param name="minChance">The chance a pick must reach on <paramref name="warSkill"/>.</param>
    /// <param name="held">What the pack holds by weenie class (<see cref="LevelFloor.HeldForAccount"/>), or null: not judged.</param>
    /// <param name="accountName">The account's name the server hashes, or null (the foci formula).</param>
    /// <param name="refused">The bolts ACE refused for a missing component on this leg.</param>
    /// <param name="why">The choice in words, with what was skipped; for a null pick, <c>none</c> or <c>no-components: ...</c>.</param>
    /// <param name="lack">Why the pick is null: <see cref="BoltLack.NoWarSpell"/> or <see cref="BoltLack.NoComponents"/>; <see cref="BoltLack.None"/> with a pick.</param>
    public static SpellInfo? ChooseCastable(
        IReadOnlyDictionary<uint, SpellInfo> table,
        IEnumerable<uint> known,
        uint? warSkill,
        double minChance,
        IReadOnlyDictionary<uint, int>? held,
        string? accountName,
        IReadOnlySet<uint> refused,
        out string why,
        out BoltLack lack)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(refused);
        var left = new List<uint>();
        var skipped = new List<string>();
        var any = false;
        foreach (var id in known.Distinct().Order())
        {
            if (!table.TryGetValue(id, out var info) || info.School != MagicSchool.WarMagic || info.MetaSpellType != SpellType.Projectile)
            {
                continue;
            }

            any = true;
            if (refused.Contains(id))
            {
                skipped.Add($"{info.Name} was refused for a missing component on this leg");
            }
            else if (held is not null && LevelFloor.ShortOf(held, info, out var need, accountName))
            {
                skipped.Add($"{info.Name} needs {need}");
            }
            else
            {
                left.Add(id);
            }
        }

        if (!any)
        {
            lack = BoltLack.NoWarSpell;
            why = "none";
            return null;
        }

        var judged = held is null ? "; the pack's components not judged before the cast (the pack is not read or settled, or it holds no War Magic focus and no account name is given)" : string.Empty;
        if (left.Count == 0)
        {
            lack = BoltLack.NoComponents;
            why = "no-components: " + string.Join("; ", skipped) + judged;
            return null;
        }

        var pick = Choose(table, left, warSkill, minChance, out why);
        lack = BoltLack.None;
        why += (skipped.Count == 0 ? string.Empty : "; skipped: " + string.Join("; ", skipped)) + judged;
        return pick;
    }

    /// <summary>
    /// M6 14 the element line on the road (the owner's ruling of 2026-11-16; live ladder42 20:15-20:18 UTC: the journey's fight-back cast only Ilservian's Flame): <see cref="ChooseCastable"/>, then, for a target of weenie class
    /// <paramref name="wcid"/>, the bolt line of the book that hits it hardest (<see cref="BoltElement.Lines"/>: the hunting fight's rule, the creature's resistances from the world database) among the same castable bolts; the castable pick
    /// stays when it hits as hard (a tie keeps it) or when no bolt line is castable, and with <paramref name="wcid"/> 0 (no target) or the War Magic skill not read.
    /// </summary>
    /// <param name="table">The SpellTable.</param>
    /// <param name="known">The spell ids the book holds.</param>
    /// <param name="warSkill">The current War Magic skill, or null.</param>
    /// <param name="minChance">The chance a pick must reach.</param>
    /// <param name="held">What the pack holds, or null: not judged.</param>
    /// <param name="accountName">The account's name, or null.</param>
    /// <param name="refused">The bolts refused for a component on this leg.</param>
    /// <param name="wcid">The target's weenie class, or 0.</param>
    /// <param name="why">The choice in words.</param>
    /// <param name="lack">Why the pick is null.</param>
    public static SpellInfo? ChooseCastableFor(
        IReadOnlyDictionary<uint, SpellInfo> table,
        IEnumerable<uint> known,
        uint? warSkill,
        double minChance,
        IReadOnlyDictionary<uint, int>? held,
        string? accountName,
        IReadOnlySet<uint> refused,
        uint wcid,
        out string why,
        out BoltLack lack)
    {
        var knownSet = known as IReadOnlySet<uint> ?? known.ToHashSet();
        var pick = ChooseCastable(table, knownSet, warSkill, minChance, held, accountName, refused, out why, out lack);
        if (pick is null || wcid == 0 || warSkill is not { } skill)
        {
            return pick;
        }

        bool Usable(uint id) => knownSet.Contains(id) && !refused.Contains(id) && table.TryGetValue(id, out var info) && (held is null || !LevelFloor.ShortOf(held, info, out _, accountName));
        var lines = BoltElement.Lines(wcid, id => table.TryGetValue(id, out var info) ? info : null, Usable, skill, minChance, Scenarios.FightScenario.Levels, BoltElement.ElementOfSpell(pick.Id)); // reads no caster in her hands: TODO(m6-caster-line-pair)
        if (lines.Count == 0 || lines[0].SpellId == pick.Id)
        {
            return pick;
        }

        var held_ = lines.FirstOrDefault(l => l.SpellId == pick.Id);
        if (held_ is not null && held_.Expected >= lines[0].Expected)
        {
            return pick;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"the line that hits weenie {wcid} hardest: {lines[0]} over {(held_ is null ? pick.Name : held_.ToString())} (M6 14 the element line; {string.Join(", ", lines.Skip(1).Take(2))})");
        return table[lines[0].SpellId];
    }

    /// <summary>M6 6 R1F: true when a cast was refused for a missing component (ACE's 0x0400 YouDontHaveAllTheComponents, <see cref="CastAttempt.ComponentsCode"/>): the chat's refusal code or the handle's server error.</summary>
    /// <param name="handle">The cast's handle, or null.</param>
    /// <param name="chatRefusal">The WeenieError the chat carried since the cast, or 0.</param>
    public static bool IsComponentRefusal(ActionHandle? handle, uint chatRefusal = 0) =>
        chatRefusal == CastAttempt.ComponentsCode
        || handle?.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: CastAttempt.ComponentsCode }
        || handle?.Outcome is { Reason: ActionReason.NoComponents };

    /// <summary>
    /// M6 6 R1F (live round 1: a fight-back cast was refused "the player is not in the magic combat mode" and the fight-back ended): the fight-backs' check before each cast, check, act, check. Check: the combat mode; magic needs nothing.
    /// Act: the magic mode asked for (<see cref="IActionApi.SetCombatMode"/>), one line. Check after: one line; the caller reads the mode (<see cref="CombatModes.Magic"/>, else its named stop) when this ends, at most
    /// <paramref name="maxSeconds"/> later.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="maxSeconds">How long the mode change may take.</param>
    /// <param name="log">The caller's log.</param>
    public static IEnumerable<ActionHandle?> MagicBeforeCast(IGameSessionView session, double maxSeconds, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        var before = session.State.Self.Qualities.CombatMode;
        if (before == CombatModes.Magic)
        {
            yield break;
        }

        log(string.Create(CultureInfo.InvariantCulture, $"fight-back: check before the cast: the combat mode is {before}, not magic ({CombatModes.Magic}): entering the magic mode (check, act, check)"));
        var mode = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        var until = session.Clock.Now + maxSeconds;
        while (!(mode.IsCompleted && session.State.Self.Qualities.CombatMode == CombatModes.Magic) && session.Clock.Now < until)
        {
            yield return null;
        }

        var after = session.State.Self.Qualities.CombatMode;
        log(string.Create(CultureInfo.InvariantCulture, $"fight-back: check after the mode change: the combat mode is {after} ({(after == CombatModes.Magic ? "magic" : "NOT magic")})"));
    }
}

/// <summary>M6 6 R1F: why <see cref="WarBoltPick.ChooseCastable"/> picked nothing.</summary>
public enum BoltLack
{
    /// <summary>A bolt was picked.</summary>
    None,

    /// <summary>The book holds no War Magic projectile the table knows.</summary>
    NoWarSpell,

    /// <summary>Every War Magic projectile the book holds lacks a component (the pack's check before, or ACE's refusal on this leg).</summary>
    NoComponents,
}
