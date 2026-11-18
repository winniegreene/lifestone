using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 approach cost: the crossing's cost read on her live state (<see cref="ApproachCost.Judge"/>'s inputs), joined to the approach check's verdict (<see cref="ApproachCheck.Live"/>) for the ladder's way out, the trial switch and the corpse
/// trip, so the three read one verdict. Every number is hers now or the ladder's and the profiles' facts: her War Magic (buffed), the rung's live bolt cap on its element's line (<see cref="AutopilotScenario.BoltLevel"/>) stepped down to a bolt
/// her pack pays, the held caster's damage as the caster pick scores it, her Mana Conversion, maximum mana and regeneration, the way out's reserve (Lifestone Recall's cost and a heal's, as the run leg keeps it), her run speed (her Run
/// skill's rate), the route's run (<see cref="TravelRoutes.FirstAdmitted"/>) and the rungs' planned experience rates (<see cref="ApproachCost.RungRate"/>).
/// </summary>
public static class ApproachCrossing
{
    /// <summary>The Run skill's SkillTable formula: Quickness over 1 (retail's Run; the dats' SkillTable row the movement's <c>QualitiesWeenie</c> reads).</summary>
    private static readonly Lifestone.Data.CharacterCreation.SkillTableFormula RunFormula = new(0, 1, 0, 1, MageTemplate.Quickness, 0);

    /// <summary>
    /// The approach verdict for <paramref name="ground"/> on her live state with the crossing's cost in it, or null when the profile records no approach. A crossing given up this run (<paramref name="abandoned"/>) holds as it stands;
    /// a survivable approach is costed (<see cref="ApproachCost.Judge"/>), and a costly one carries what would change it (<see cref="ApproachCost.WhatChanges"/>). <paramref name="costWhy"/> says why no cost was read when none was.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="reader">The run's line reader.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="ladder">The ladder (its rungs, plan chance and bolt seconds).</param>
    /// <param name="spells">The spell table, or null.</param>
    /// <param name="routes">The travel routes.</param>
    /// <param name="abandoned">Why the run leg gave this ground's crossing up this run, or null.</param>
    /// <param name="costWhy">Why the cost was not read, or empty.</param>
    /// <param name="paces">M6 14 run past (D137): the creatures' paces (the dats' motion runs), or null: none read, so every creature met is priced as a fight.</param>
    public static ApproachVerdict? Judge(GameState state, HuntingGround ground, LiveFightLine reader, double now, Ladder ladder, IReadOnlyDictionary<uint, SpellInfo>? spells, TravelRoutes routes, string? abandoned, out string costWhy, CreaturePaces? paces = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(routes);
        costWhy = string.Empty;
        if (ApproachCheck.Live(state, ground, reader, now) is not { } verdict)
        {
            return null;
        }

        if (abandoned is not null)
        {
            costWhy = "the crossing was given up this run";
            return verdict with { Abandoned = abandoned };
        }

        if (!verdict.Survivable || ground.Approach is not { } approach)
        {
            costWhy = "not costed: the approach is not survivable";
            return verdict;
        }

        if (RunMetres(ground, state.Self.Qualities.Level, routes, out var routeWhy) is not { } metres)
        {
            costWhy = routeWhy;
            return verdict;
        }

        if (Read(state, ladder, ground, spells, out var inputWhy, reader, now, paces) is not { } read)
        {
            costWhy = inputWhy;
            return verdict;
        }

        if (ApproachCost.Judge(ground.Key, approach, metres, read.Input) is not { } cost)
        {
            costWhy = $"{ground.Key}'s approach carries no crossing facts (encounters, per_roll and each hostile's magic_defence, resist, awareness_m, per_roll and xp)";
            return verdict;
        }

        return verdict with { Cost = cost.Costly ? cost with { Change = ApproachCost.WhatChanges(ground.Key, approach, metres, read.Input, read.BoltAt) + "; " + read.BoltWhy } : cost };
    }

    /// <summary>The run on foot of the route the rung change takes to <paramref name="ground"/> at <paramref name="level"/> (the first admitted, as <c>RungRoute</c> plans it), or null with the reason.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="level">Her level.</param>
    /// <param name="routes">The travel routes.</param>
    /// <param name="why">The route, or why there is none.</param>
    public static double? RunMetres(HuntingGround ground, int level, TravelRoutes routes, out string why)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(routes);
        var choice = routes.FirstAdmitted(ground.Key, level, ground.Entry?.MinLevel, ground.Entry?.MaxLevel);
        if (choice is not { Outcome: TravelOutcome.Admitted, Route: { } route })
        {
            why = $"no route to {ground.Key} admits level {level} ({choice.Stop ?? choice.Outcome.ToString()})";
            return null;
        }

        if (route.Run is not { } run)
        {
            why = $"the route {route.Key} to {ground.Key} has no run on foot";
            return null;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"route {route.Key}, run {run.Metres:0.#} m");
        return run.Metres;
    }

    /// <summary>What <see cref="Read"/> gives: her live input, her bolt at another War Magic (the "what would change it" scan), and her bolt's and caster's reads in words.</summary>
    /// <param name="Input">Her live numbers.</param>
    /// <param name="BoltAt">Her bolt at a War Magic (the cap rule and the pack's components as now), or null when none is castable.</param>
    /// <param name="BoltWhy">The bolt's choice and the caster's damage in words.</param>
    public sealed record CrossingRead(CrossingInput Input, Func<uint, CrossingBolt?> BoltAt, string BoltWhy);

    /// <summary>
    /// Her live numbers for the crossing to <paramref name="ground"/>, or null with the reason: the rung's element and its live bolt cap (<see cref="AutopilotScenario.BoltLevel"/> on her current War Magic), stepped down past a level her book
    /// lacks or her pack cannot pay the components of (<see cref="LevelFloor.ShortOf"/>: live, the climber held no Platinum Scarab for Ilservian's Flame, so her fight-backs cast Flame Bolt VI); the held caster's damage on the bolt as the caster
    /// pick scores it (<see cref="CasterDamage"/>), so a better caster through the spend changes the verdict by itself; the previous rung's and the rung's rates.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="ladder">The ladder.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The spell table, or null.</param>
    /// <param name="why">Why there are none.</param>
    /// <param name="reader">M6 14 run past: her armour and live resistance (the hits she takes are priced on them), or null: the hits are not priced.</param>
    /// <param name="now">The session clock (the reader's).</param>
    /// <param name="paces">M6 14 run past: the creatures' paces, or null: none read (every creature met is a fight).</param>
    public static CrossingRead? Read(GameState state, Ladder ladder, HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo>? spells, out string why, LiveFightLine? reader = null, double now = 0, CreaturePaces? paces = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(ground);
        var q = state.Self.Qualities;
        var rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.GroundKey, ground.Key, StringComparison.Ordinal) || r.Alternates.Any(a => string.Equals(a.GroundKey, ground.Key, StringComparison.Ordinal)));
        if (rung?.Element is not { } element)
        {
            why = $"{ground.Key} is no ground rung's ground (the crossing is costed on a rung's element and bolt)";
            return null;
        }

        if (!GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var war, out var warWhy))
        {
            why = "her War Magic is not read: " + warWhy;
            return null;
        }

        if (spells is null)
        {
            why = "no spell table";
            return null;
        }

        if (SpellPlan.BoltLine(element, spells, out var lineWhy) is not { } line)
        {
            why = $"the rung {rung.Key} names no bolt line: {lineWhy}";
            return null;
        }

        if (!ladder.VitalsPolicy.On || ladder.VitalsPolicy.CastSecondsBolt.Count == 0 || !q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var manaMax))
        {
            why = "the ladder's bolt seconds or her mana are not read";
            return null;
        }

        var held = LevelFloor.HeldForAccount(state, MagicSchool.WarMagic, null);
        if (BoltAt(state.Self.Spellbook, spells, held, ladder, rung, line, element, war, out var boltWhy) is not { } bolt)
        {
            why = boltWhy;
            return null;
        }

        ManaOrder.TryManaConversion(q, out var manaConversion);
        var regen = VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1.0, VitalsPolicy.RegenMultipliers(q).Mana);
        var run = SkillRules.TryCurrent(q, MageTemplate.Run, RunFormula, SkillRules.TrainedClass, out var runSkill) ? (int)runSkill : 0; // unread: Run 0, the slowest run (4 m/s)
        var speed = MovementSystem.GetRunRate(0f, run, 1f) * MotionInterpreter.RunAnimSpeed;
        var reserve = Reserve(state, spells);
        var previous = ladder.Rungs.LastOrDefault(r => r.Kind == LadderRungKind.Ground && r.Band.To < rung.Band.From);
        var previousWhy = $"no ground rung before {rung.Key}";
        double? previousRate = null;
        if (previous is not null)
        {
            previousRate = ApproachCost.RungRate(previous, previous.GroundKey is { } pk ? GroundCatalog.TryGet(pk) : null, out previousWhy);
        }

        var rungRate = ApproachCost.RungRate(rung, ground, out var rungWhy);
        var (casterDamage, casterWhy) = CasterDamage(state, element);
        why = string.Empty;
        var input = new CrossingInput(war, manaConversion, manaMax, regen, reserve, speed, bolt, BuffOptions.OneBuffCycleSeconds, previousRate, rungRate, $"the previous rung: {previousWhy}; the rung: {rungWhy}") { CasterDamage = casterDamage };
        input = RunPastInput(state, input, ladder, spells, manaConversion, reader, now, paces);
        input = DanceInput(state, spells, input, ladder);
        var book = state.Self.Spellbook;
        return new CrossingRead(input, skill => BoltAt(book, spells, held, ladder, rung, line, element, skill, out _), $"{boltWhy}; {casterWhy}");
    }

    /// <summary>
    /// M6 14 run past (D137): her side of the run past in the crossing's cost: the creatures' paces (<see cref="CreaturePace.Of"/> on the dats' runs, or none), what reaches her of a hit (her armour and live resistance by
    /// <paramref name="reader"/>, or not priced), her maximum health, her health regeneration while she runs (the human's rate at the run's stance factor and her regeneration enchantments; her attributes' factor not read, 1, the safe
    /// side), and her best heal: the highest Heal Self her book holds, its expected cost after her Mana Conversion over its mean heal (<see cref="MeasuredHeals.SelfHealSeed"/>), and its measured cast seconds (the ladder's self row).
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="input">The crossing's input so far.</param>
    /// <param name="ladder">The ladder (its self cast seconds).</param>
    /// <param name="spells">The spell table, or null.</param>
    /// <param name="manaConversion">Her Mana Conversion (0: untrained).</param>
    /// <param name="reader">Her armour and resistance reader, or null.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="paces">The creatures' paces, or null.</param>
    public static CrossingInput RunPastInput(GameState state, CrossingInput input, Ladder ladder, IReadOnlyDictionary<uint, SpellInfo>? spells, uint manaConversion, LiveFightLine? reader, double now, CreaturePaces? paces)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ladder);
        var c = CultureInfo.InvariantCulture;
        var q = state.Self.Qualities;
        var healthMax = q.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) ? max : 0u;
        var regen = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, VitalsPolicy.CombatStance, VitalsPolicy.RegenMultipliers(q).Health);
        var (perPoint, secondsPerPoint, healWhy) = (double.NaN, 0.0, "her book holds no Heal Self");
        for (var i = 0; spells is not null && i < VitalsMeans.HealSelf.Count && i < MeasuredHeals.SelfHealSeed.Length; i++)
        {
            if (!state.Self.Spellbook.Knows(VitalsMeans.HealSelf[i]) || !spells.TryGetValue(VitalsMeans.HealSelf[i], out var info))
            {
                continue;
            }

            var mean = MeasuredHeals.SelfHealSeed[i];
            var cost = ManaConversionModel.ExpectedCost(info.BaseMana, info.Power, manaConversion);
            var seconds = ladder.VitalsPolicy.On && ladder.VitalsPolicy.CastSecondsSelf.Count > 0 ? ladder.VitalsPolicy.SelfSeconds(i + 1) : 0;
            (perPoint, secondsPerPoint, healWhy) = (cost / mean, seconds / mean, string.Create(c, $"{info.Name}: {cost:0.#} mana and {seconds:0.#} s for {mean:0.#} health")); // the highest her book holds: the vitals guard's
        }

        return input with
        {
            PaceOf = paces is null ? null : h => CreaturePace.Of(h, paces.AnimOf),
            DamageFactor = reader is null ? null : types => reader.FactorFor(state, types, now, out _),
            SpellFactor = types => LiveFightLine.Resistance(q.Enchantments, types),
            TimingOf = reader is null ? null : reader.Times.Of,
            HealthMax = healthMax,
            HealthRegen = regen,
            HealManaPerPoint = perPoint,
            HealSecondsPerPoint = secondsPerPoint,
            HealWhy = healWhy,
        };
    }

    /// <summary>
    /// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions): the stamina dance in the crossing's input, read on her live state (<see cref="Scenarios.Journey.StaminaDance.Round"/>): a round's net
    /// (Revitalize Self at the highest level her book holds and her pack pays, then the best-net Stamina to Mana Self press from the stamina it restores, less Revitalize's expected cost on her Mana Conversion) and its seconds (the two casts
    /// at the ladder's measured self seconds). When it does not cover her (Mana Conversion untrained, no Revitalize Self, a round that nets nothing, the seconds not read), the input says why and carries no dance.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The spell table, or null.</param>
    /// <param name="input">The crossing's input so far.</param>
    /// <param name="ladder">The ladder (its self cast seconds).</param>
    public static CrossingInput DanceInput(GameState state, IReadOnlyDictionary<uint, SpellInfo>? spells, CrossingInput input, Ladder ladder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ladder);
        if (spells is null)
        {
            return input with { DanceNet = 0, DanceSeconds = 0, DanceWhy = "no spell table: the dance is not read" };
        }

        if (Scenarios.Journey.StaminaDance.Round(state, spells, out var why, ladder.VitalsPolicy) is not { } round)
        {
            return input with { DanceNet = 0, DanceSeconds = 0, DanceWhy = why };
        }

        return double.IsFinite(round.Seconds) && round.Seconds > 0
            ? input with { DanceNet = round.Net, DanceSeconds = round.Seconds, DanceWhy = round.Words }
            : input with { DanceNet = 0, DanceSeconds = 0, DanceWhy = round.Words + ": a round's seconds are not read, so the dance is not priced" };
    }

    /// <summary>Her bolt at <paramref name="skill"/>: the rung's cap level on the line, stepped down past a level the book lacks, the tables do not hold, or the pack cannot pay; null with the reason when none is left.</summary>
    private static CrossingBolt? BoltAt(Spellbook book, IReadOnlyDictionary<uint, SpellInfo> spells, Dictionary<uint, int>? held, Ladder ladder, LadderRung rung, IReadOnlyList<ProfileSpell> line, string element, uint skill, out string why)
    {
        var cap = AutopilotScenario.BoltLevel(rung, line, book.Knows, skill, ladder.PlanCastChance, out _, out var capWhy);
        var skipped = new List<string>();
        for (var level = cap; level >= 1; level--)
        {
            var spell = line[level - 1];
            if (!book.Knows(spell.Id) || !spells.TryGetValue(spell.Id, out var info) || !WarSpellData.TryGet(spell.Id, out var row))
            {
                continue;
            }

            if (held is not null && LevelFloor.ShortOf(held, info, out var need))
            {
                skipped.Add($"{info.Name} needs {need}, which the pack does not hold");
                continue;
            }

            why = $"her bolt: {info.Name} ({capWhy}{(skipped.Count == 0 ? string.Empty : "; skipped: " + string.Join("; ", skipped))})";
            return new CrossingBolt(info.Name, element, row, info.Power, info.BaseMana, ladder.VitalsPolicy.BoltSeconds(level));
        }

        why = $"no level of the rung {rung.Key}'s line is castable ({capWhy}{(skipped.Count == 0 ? string.Empty : "; skipped: " + string.Join("; ", skipped))})";
        return null;
    }

    /// <summary>
    /// The held caster's damage on a bolt of <paramref name="element"/>, as the caster pick scores it (<see cref="GearScore.Caster"/>): its ElementalDamageMod (PropertyFloat 152) plus Spirit Drinker's add (<see cref="CasterAuras.ElementalAdd"/>,
    /// the aura her book holds) when its DamageType (PropertyInt 45) is the element, else 1 (ACE adds nothing off element, WorldObject_Weapon.cs:382-401 @95ef426d); 1 with no caster held or none appraised.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="element">The rung's element.</param>
    public static (double Damage, string Why) CasterDamage(GameState state, string element)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (CombatModeGuard.Read(state).Caster is not { } caster)
        {
            return (1.0, "no caster held: her bolts' damage x1");
        }

        if (!state.Appraisals.TryGet(caster.Guid, out var a) || a.IsNotFound)
        {
            return (1.0, $"the held caster '{caster.Name}' is not appraised: x1");
        }

        if (!SelfEquipJudge.ElementBits.TryGetValue(element, out var bits) || a.Int(SelfEquipJudge.DamageTypeInt) is not { } type || (uint)type != bits)
        {
            return (1.0, $"the held caster '{caster.Name}' is not of the element {element}: x1");
        }

        var damage = (a.Float(AppraisalIds.ElementalDamageMod) ?? 1.0) + CasterAuras.Of(state.Self.Spellbook).ElementalAdd;
        return (damage, string.Create(CultureInfo.InvariantCulture, $"the held caster '{caster.Name}' on {element}: x{damage:0.###} (its ElementalDamageMod and Spirit Drinker)"));
    }

    /// <summary>The way out's reserve as the run leg keeps it: Lifestone Recall's cost and the most the highest Heal Self her book knows can take (<see cref="ManaWayOut.LineOf"/>); 0 when the book holds no Lifestone Recall.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The spell table, or null.</param>
    public static uint Reserve(GameState state, IReadOnlyDictionary<uint, SpellInfo>? spells)
    {
        ArgumentNullException.ThrowIfNull(state);
        var book = state.Self.Spellbook;
        if (spells is null || !book.Knows(Fighter.RecallHome.LifestoneRecallSpell) || !spells.TryGetValue(Fighter.RecallHome.LifestoneRecallSpell, out var recall))
        {
            return 0;
        }

        var heal = 0u;
        foreach (var id in VitalsMeans.HealSelf)
        {
            if (book.Knows(id) && spells.TryGetValue(id, out var h))
            {
                heal = Math.Max(heal, GearCasts.ManaCeiling(h, 0));
            }
        }

        return ManaWayOut.LineOf(recall.BaseMana, heal).Line;
    }
}
