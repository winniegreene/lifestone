using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Vitals;

/// <summary>The bolt the crossing is fought with (<see cref="ApproachCost"/>): the fight's live bolt cap on the rung's line.</summary>
/// <param name="Name">The spell's name.</param>
/// <param name="Element">The rung's element (a key of <see cref="GroundApproachHostile.Resist"/>).</param>
/// <param name="Row">Its projectile row (<see cref="WarSpellData"/>).</param>
/// <param name="Power">Its power.</param>
/// <param name="BaseMana">Its base mana.</param>
/// <param name="Seconds">Its cast's seconds (the ladder's measured table, <see cref="LadderVitalsPolicy.BoltSeconds"/>).</param>
public sealed record CrossingBolt(string Name, string Element, WarSpellRow Row, uint Power, uint BaseMana, double Seconds);

/// <summary>What the crossing's cost reads of her (<see cref="ApproachCost.Judge"/>): every number live from her character or the ladder's facts.</summary>
/// <param name="War">Her current War Magic (buffed).</param>
/// <param name="ManaConversion">Her current Mana Conversion (0: untrained).</param>
/// <param name="ManaStart">The mana she starts the run with: her maximum (the top-up before the hostile drop fills her where nothing is in reach, <c>RungChangeStation.Recharge</c>).</param>
/// <param name="ManaRegen">Her mana regeneration a second (<see cref="VitalsPolicy.Regen"/>, her enchantments in it).</param>
/// <param name="Reserve">The way out's reserve (<see cref="ManaWayOut.LineOf"/>: Lifestone Recall's cost and a heal's), the line the run leg's fight-back leaves at.</param>
/// <param name="RunMetresPerSecond">Her run speed.</param>
/// <param name="Bolt">Her bolt.</param>
/// <param name="CycleSeconds">A cycle's seconds (the buffs' own cycle, <see cref="BuffOptions.OneBuffCycleSeconds"/>).</param>
/// <param name="RatePrevious">The previous rung's planned experience an hour (<see cref="ApproachCost.RungRate"/>), or null when not known.</param>
/// <param name="RateRung">The rung's own planned experience an hour, or null when not known.</param>
/// <param name="RatesWhy">The rates' sources in words.</param>
public sealed record CrossingInput(uint War, uint ManaConversion, uint ManaStart, double ManaRegen, uint Reserve, double RunMetresPerSecond, CrossingBolt Bolt, double CycleSeconds, double? RatePrevious, double? RateRung, string RatesWhy)
{
    /// <summary>The held caster's damage on her bolts (its ElementalDamageMod and Spirit Drinker's add on the rung's element, as the caster pick scores it; 1: none or off element).</summary>
    public double CasterDamage { get; init; } = 1.0;

    /// <summary>
    /// M6 14 run past (D137): each hostile's run speed (<see cref="CreaturePace.Of"/>: its facts and the dats), or null for one not known; null (the default): no pace is read, so every creature met is a fight (the crossing as fought,
    /// the model before the run past).
    /// </summary>
    public Func<GroundApproachHostile, Pace?>? PaceOf { get; init; }

    /// <summary>M6 14 run past: what reaches her of an unarmoured hit of the DamageType bits given (her armour times her live resistance, <see cref="LiveFightLine.FactorFor"/>); null: the hits are not priced.</summary>
    public Func<uint, double>? DamageFactor { get; init; }

    /// <summary>M6 14 run past: what reaches her of a war spell of the DamageType bits given (her live resistance only: a spell takes no armour); null: the spells are not priced.</summary>
    public Func<uint, double>? SpellFactor { get; init; }

    /// <summary>M6 14 attack timing: each hostile's attack timing by weenie and name (<see cref="CreatureAttackTimes.Of"/>), or null: the named 2.75 s fallback (the model before).</summary>
    public Func<uint, string, AttackTiming>? TimingOf { get; init; }

    /// <summary>M6 14 run past: her maximum health (0: unread).</summary>
    public uint HealthMax { get; init; }

    /// <summary>M6 14 run past: her health regeneration a second while she runs.</summary>
    public double HealthRegen { get; init; }

    /// <summary>M6 14 run past: the mana a point of health costs her by her best Heal Self (its expected cost after Mana Conversion over its mean heal), or NaN when her book holds none.</summary>
    public double HealManaPerPoint { get; init; } = double.NaN;

    /// <summary>M6 14 run past: the seconds a point of health costs her by that heal (its cast seconds over its mean heal), 0 when none.</summary>
    public double HealSecondsPerPoint { get; init; }

    /// <summary>M6 14 run past: her heal in words (the best Heal Self, its cost and its mean), or why there is none.</summary>
    public string HealWhy { get; init; } = "no heal read";

    /// <summary>
    /// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions): the mana one round of the stamina dance nets her (Revitalize Self, then the best-net Stamina to Mana Self press, less Revitalize's
    /// cost: <c>StaminaDance.Round</c>); 0 (the default): the dance does not cover her, or it is not read, so the crossing's mana comes back only at her regeneration.
    /// </summary>
    public double DanceNet { get; init; }

    /// <summary>M6 14 walk dance: a round's seconds (its two casts at the ladder's measured self seconds); 0: not read.</summary>
    public double DanceSeconds { get; init; }

    /// <summary>M6 14 walk dance: the round in words when it covers her, or why the dance does not cover her; null: the dance was not read (the line says nothing of it).</summary>
    public string? DanceWhy { get; init; }

    /// <summary>True when the dance covers her and is priced: a round nets mana in a known number of seconds.</summary>
    public bool DanceCovers => DanceNet > 0 && DanceSeconds > 0 && double.IsFinite(DanceNet) && double.IsFinite(DanceSeconds);
}

/// <summary>One kind of the approach as the crossing's cost judged it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Wcid">Its weenie class id (0: the unlisted creatures).</param>
/// <param name="Land">The chance a bolt lands: the cast's and its Magic Defense's (the resist roll).</param>
/// <param name="Mean">The bolt's mean landed damage on it (her War Magic's skill bonus, the critical's share and its resistance to the element in it).</param>
/// <param name="Casts">The casts a kill (the fight's kill model, <see cref="VitalsPolicy.BoltsToKill"/>).</param>
/// <param name="Mana">The mana a kill (the casts at the bolt's expected cost after Mana Conversion).</param>
/// <param name="Seconds">The seconds a kill (the casts at the bolt's seconds).</param>
/// <param name="Count">How many of it the crossing is expected to meet.</param>
public readonly record struct CrossingKind(string Name, uint Wcid, double Land, double Mean, double Casts, double Mana, double Seconds, double Count)
{
    /// <summary>M6 14 run past: how she meets it: <c>fight</c> (it keeps pace, or its pace is not known), <c>outrun</c> (a melee creature slower than her: no cost), <c>passing</c> (a slower thrower: its damage while she passes).</summary>
    public string Way { get; init; } = "fight";

    /// <summary>M6 14 run past: its run speed, or NaN when not known.</summary>
    public double Speed { get; init; } = double.NaN;

    /// <summary>M6 14 run past: the damage one of it is expected to deal her (in its fight, or while she passes it).</summary>
    public double Damage { get; init; }
}

/// <summary>The crossing's cost and its verdict (<see cref="ApproachCost.Judge"/>).</summary>
/// <param name="Costly">True when the crossing is expected to force a recall before the portal, or to cost more time than the rung's share of a cycle.</param>
/// <param name="Reason"><c>mana</c>, <c>time</c> or empty (not costly).</param>
/// <param name="Why">The verdict in words with its numbers.</param>
/// <param name="Creatures">The creatures the crossing is expected to meet.</param>
/// <param name="Casts">The casts it is expected to take.</param>
/// <param name="Mana">The mana those casts are expected to cost.</param>
/// <param name="AtPortal">The mana she is expected to hold at the portal: her start, the regeneration over the crossing, less the casts' mana (M6 14 walk dance: plus the dance's rounds when it covers her, <see cref="DanceRounds"/>).</param>
/// <param name="Seconds">The crossing's expected seconds: the run and the kills (M6 14 walk dance: and the dance's rounds, <see cref="DanceSeconds"/>).</param>
/// <param name="AllowedSeconds">The seconds the rung's experience rate allows it (NaN: not judged).</param>
/// <param name="Kinds">The kinds, the unlisted creatures last.</param>
public sealed record CrossingCost(bool Costly, string Reason, string Why, double Creatures, double Casts, double Mana, double AtPortal, double Seconds, double AllowedSeconds, IReadOnlyList<CrossingKind> Kinds)
{
    /// <summary>What would change the verdict (<see cref="ApproachCost.WhatChanges"/>), or null when not asked.</summary>
    public string? Change { get; init; }

    /// <summary>M6 14 run past: the fights she cannot avoid (the creatures that keep pace with her, or whose pace is not known).</summary>
    public double Fights { get; init; }

    /// <summary>M6 14 run past: the creatures she leaves behind or passes.</summary>
    public double Passed { get; init; }

    /// <summary>M6 14 run past: the damage the crossing is expected to deal her (the fights' hits and the throws while she passes).</summary>
    public double Damage { get; init; }

    /// <summary>M6 14 run past: the mana her heals of that damage are expected to cost (the regeneration over the crossing taken off first).</summary>
    public double HealMana { get; init; }

    /// <summary>M6 14 run past: the expected meetings with a creature that keeps pace and that she cannot kill on a full bar over the reserve (each a recall).</summary>
    public double Unwinnable { get; init; }

    /// <summary>M6 14 walk dance: the rounds of the stamina dance that make up the crossing's mana shortfall at the reserve (0: none needed, or the dance does not cover her).</summary>
    public int DanceRounds { get; init; }

    /// <summary>M6 14 walk dance: those rounds' seconds, in <see cref="CrossingCost.Seconds"/>.</summary>
    public double DanceSeconds { get; init; }
}

/// <summary>What the run leg measured on a rung change's crossing (<see cref="ApproachCost.Measured"/>).</summary>
/// <param name="Kills">The fight-backs' kills since the run began.</param>
/// <param name="Casts">Their casts.</param>
/// <param name="ManaStart">Her mana when the run began.</param>
/// <param name="ManaNow">Her mana now.</param>
/// <param name="Reserve">The way out's reserve line.</param>
/// <param name="MetresAtStart">The metres from where the run began to the entry's front.</param>
/// <param name="MetresNow">The metres from where she stands to the entry's front.</param>
/// <param name="SeedKills">The kills after which a measure stands (the fight's own, <see cref="LadderVitalsPolicy.SeedKills"/>).</param>
/// <param name="PriorPerMetre">
/// M6 14 crossing prior: the creatures a metre of the run meets by the approach's own facts (<see cref="ApproachCost.CreaturesPerMetre"/>), blended with the measured kills as if the whole run had been seen once at that rate; NaN: none
/// known, the measured rate alone.
/// </param>
/// <param name="StaminaMana">
/// M6 14 crossing stamina (live 2026-11-14 19:12, ladder14b): the mana one Stamina to Mana press nets from the stamina she holds now (capped at the mana she is missing), which the fight-backs' way out presses before it leaves for mana
/// (<see cref="ManaWayOut"/>: a press holds the leaving); 0 when she knows no press that lands or holds too little stamina.
/// </param>
public readonly record struct CrossingMeasure(int Kills, int Casts, uint ManaStart, uint ManaNow, uint Reserve, double MetresAtStart, double MetresNow, int SeedKills, double PriorPerMetre = double.NaN, double StaminaMana = 0)
{
    /// <summary>
    /// M6 14 walk dance (the owner, 2026-11-15): the mana one round of the stamina dance nets her (<c>StaminaDance.Round</c>); 0 (the default): the dance does not cover her or is not read, so a projection under the reserve gives the
    /// crossing up as before.
    /// </summary>
    public double DanceNet { get; init; }

    /// <summary>M6 14 walk dance: the mana the walk's dances in its lulls have added since the run began (the casts' net: the presses' gives less Revitalize's costs), so the fall a kill is the fights' alone.</summary>
    public double DancedMana { get; init; }

    /// <summary>M6 14 walk dance: why the dance cannot run or cannot keep pace on this run (its bounds, named), or null: it can still dance.</summary>
    public string? DanceStalled { get; init; }

    /// <summary>M6 14 walk dance: why the dance does not cover her, or null: not read (the line says nothing of it).</summary>
    public string? DanceWhy { get; init; }

    /// <summary>M6 14 walk dance: her maximum mana, the cap of the dance's line (0: not read, the line is the need alone).</summary>
    public uint ManaMax { get; init; }
}

/// <summary>The measured crossing's answer (<see cref="ApproachCost.Measured"/>).</summary>
/// <param name="Abandon">True when the measured rate says she will not reach the portal above the reserve: she recalls now.</param>
/// <param name="Judged">False while fewer than the seed kills are measured.</param>
/// <param name="Why">The answer in words with its numbers.</param>
/// <param name="CastsPerKill">The measured casts a kill.</param>
/// <param name="ManaPerKill">The measured net mana a kill (the fall of her mana since the run began, the regeneration and the heals in it).</param>
/// <param name="RemainingKills">The kills the rest of the run is expected to take at the measured kills a metre.</param>
/// <param name="Needed">The mana those kills are expected to take.</param>
/// <param name="Above">Her mana over the reserve now.</param>
public readonly record struct CrossingMeasureAnswer(bool Abandon, bool Judged, string Why, double CastsPerKill, double ManaPerKill, double RemainingKills, double Needed, double Above)
{
    /// <summary>
    /// M6 14 walk dance: true when the dance covers her, can still dance, and her mana is under the dance's line (<see cref="DanceTo"/>; M6 14 top-off: her maximum): she dances at the next lull, and the crossing is measured again after.
    /// </summary>
    public bool DanceDue { get; init; }

    /// <summary>M6 14 walk dance: the dance's line: her maximum (M6 14 top-off), or the reserve plus what the rest of the crossing needs when her maximum is not read (0: no line).</summary>
    public uint DanceTo { get; init; }
}

/// <summary>
/// M6 14 approach cost (the owner, 2026-11-14, watching the climber cross the Tusker Island to the Holding: she was fighting things with high magic resistance, so she should recall and go back to the previous ground; live
/// <c>cache/m6/14/live/holdtrial2</c>, 12:54 to 13:03 UTC: at level 62 with War Magic 352 and Flame Bolt VI she ran 1,506 m from the Barracks drop toward the Holding's entry, fought four fight-backs (3 casts for 1 kill, 6 for 2, 20 for 4, 28 for
/// 3: a Reaper alone took 21 bolts, 11 of them resisted), and at 13:03:17 the mana line recalled her at 111 of 387 with an Assailer attacking, 590 m short of the portal). The approach check (<see cref="ApproachCheck"/>) asks only whether she
/// survives a recall against the worst camp; this rule asks what the crossing costs. Pure.
/// <list type="bullet">
/// <item><b>The camps she meets</b>: the approach's encounter generators spread over its landblocks (<see cref="GroundApproach.Encounters"/> over <see cref="GroundApproach.AreaSquareMetres"/>), the ones whose creatures notice her along the
/// route's run (the run's metres times twice a kind's VisualAwarenessRange, <see cref="GroundApproachHostile.AwarenessMetres"/>), times what one roll stands up of each kind (<see cref="GroundApproachHostile.PerRoll"/>). The creatures a roll
/// stands up that the approach does not list (<see cref="GroundApproach.PerRoll"/> less the listed) are met at the widest awareness and cost the listed kinds' mean, named.</item>
/// <item><b>A kill's cost</b>: the fight's own kill model (<see cref="VitalsPolicy.BoltsToKill"/>) over the kind's health, her bolt's mean landed damage (<see cref="WarSpellPicker.MeanHit"/>: her live War Magic's skill bonus, the critical's
/// share, the rating, and the kind's resistance to the rung's element, <see cref="GroundApproachHostile.Resist"/>) and the chance it lands: the cast (<see cref="GearCasts.Chance"/>) times the kind failing its Magic Defense against her War Magic
/// (ACE's 0.03 roll, <see cref="ManaConversionModel.SkillChance(uint, uint, float)"/>; <c>WorldObject_Magic.cs:104-113</c> @95ef426d). The mana a kill is those casts at the bolt's expected cost after her Mana Conversion
/// (<see cref="ManaConversionModel.ExpectedCost"/>); the seconds the casts at the ladder's measured bolt seconds.</item>
/// <item><b>The fills</b>: she starts at her maximum mana (the rung change's top-up before the hostile drop) and regenerates over the crossing's seconds; the run leg presses nothing and its heals are the vitals guard's, not in this prior
/// (the measured rule counts them). The way out's reserve is kept: the run leg's fight-back leaves at it (<see cref="ManaWayOut"/>).</item>
/// <item><b>M6 14 walk dance</b> (the owner, 2026-11-15: at her level the stamina dance replaces the potions): when the dance covers her (<see cref="CrossingInput.DanceNet"/>: <c>StaminaDance.Round</c> on her live state), the walk
/// dances in its lulls, so a shortfall at the reserve is made up at the dance's rate, a round's net over a round's seconds; those seconds are added to the crossing's, and the crossing is costly for mana only when the dance does not
/// cover her. With the dance it is held only for its time (or its health, or a creature she cannot kill). The line says <c>with N rounds of the stamina dance (S s)</c>.</item>
/// <item><b>M6 14 run past (D137)</b>: with the creatures' paces read (<see cref="CrossingInput.PaceOf"/>: its facts and the dats, <see cref="CreaturePace"/>), only a kind as fast as her (or whose pace is not known) is a fight she cannot avoid; a
/// slower melee kind is left behind for nothing; a slower thrower or caster is passed, its hits paid for her mean seconds in its range (<see cref="RunPast.MeanSecondsInRange"/>). A fight's hits are priced too (a camp hits while each of it
/// dies), and every hit is healed by her best Heal Self at its expected mana and seconds, her regeneration taken off first. A kind that keeps pace and that she cannot kill on a full bar over the reserve is a recall when met
/// (<c>unwinnable</c>), out of the sums; at <see cref="UnwinnableMeetings"/> expected meetings the crossing is held.</item>
/// <item><b>The verdict</b>: <c>mana</c> when the crossing is expected to bring her mana to the reserve line before the portal (a forced recall); <c>time</c> when its seconds exceed what the rung's experience rate allows within a cycle: moving
/// pays when the rung's rate over the cycle less the crossing, plus the crossing's own kills' experience, at least matches the previous rung's rate over the whole cycle
/// (<c>allowed = cycle x (1 - previous / rung) + crossing experience / rung</c>; not judged when a rate is not known).</item>
/// </list>
/// </summary>
public static class ApproachCost
{
    /// <summary>The token of a rung or trial held for its crossing's cost.</summary>
    public const string CostlyPrefix = "approach-costly";

    /// <summary>The token of a crossing given up on the way, held for the rest of the run.</summary>
    public const string AbandonedPrefix = "approach-abandoned";

    /// <summary>The highest War Magic over her own the "what would change it" scan reads (Lifestone's own bound: past it the line is a different climb).</summary>
    public const int WarScanSpan = 400;

    /// <summary>The highest caster damage modifier the scan reads (a wand's elemental damage: Lifestone's own bound).</summary>
    public const double CasterScanTop = 3.0;

    /// <summary>
    /// M6 14 run past: the expected meetings with a creature she can neither outrun nor kill (each a recall) at which the crossing is held: more likely than not (Lifestone's own: the verdict is an expectation, as the mana's is).
    /// </summary>
    public const double UnwinnableMeetings = 0.5;

    /// <summary>The crossing's cost on <paramref name="approach"/>, or null when the approach carries no crossing facts (<see cref="GroundApproach.Encounters"/>; the caller names it).</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="approach">The approach's facts.</param>
    /// <param name="runMetres">The route's run on foot to the entry (<c>TravelRun.Metres</c>).</param>
    /// <param name="input">Her live numbers.</param>
    public static CrossingCost? Judge(string ground, GroundApproach approach, double runMetres, CrossingInput input)
    {
        ArgumentNullException.ThrowIfNull(approach);
        ArgumentNullException.ThrowIfNull(input);
        if (approach.Encounters is not { } encounters || approach.PerRoll is not { } perRoll)
        {
            return null;
        }

        var c = CultureInfo.InvariantCulture;
        var density = approach.AreaSquareMetres > 0 ? encounters / approach.AreaSquareMetres : 0;
        var kinds = new List<CrossingKind>(approach.Hostiles.Count + 1);
        var listedPerRoll = 0.0;
        var widest = 0.0;
        double creatures = 0, casts = 0, mana = 0, killSeconds = 0, xp = 0, fights = 0, damage = 0, unwinnable = 0;
        foreach (var h in approach.Hostiles)
        {
            if (!h.HasCrossingFacts || !h.Resist!.TryGetValue(input.Bolt.Element, out var resist))
            {
                continue;
            }

            listedPerRoll += h.PerRoll!.Value;
            widest = Math.Max(widest, h.AwarenessMetres!.Value);
            var count = density * runMetres * 2 * h.AwarenessMetres.Value * h.PerRoll.Value;
            var kind = RunPastKind(h, Kill(h.Name, h.Wcid, h.MaxHealth!.Value, h.MagicDefence!.Value, resist, count, input), input);
            if (kind.Way == "fight" && (!double.IsFinite(kind.Mana) || kind.Mana > (double)input.ManaStart - input.Reserve))
            {
                // M6 14 run past: a creature that keeps pace and whose kill takes more mana than a full bar holds over the reserve (or never ends: its resistance to her element or its Magic Defense): meeting it is a recall
                kind = kind with { Way = "unwinnable", Damage = 0 };
                unwinnable += count;
            }

            kinds.Add(kind);
            creatures += count;
            damage += count * kind.Damage;
            if (kind.Way == "fight")
            {
                // M6 14 run past: only a creature she cannot outrun is fought; its kill's casts, mana, seconds and experience count
                fights += count;
                casts += count * kind.Casts;
                mana += count * kind.Mana;
                killSeconds += count * kind.Seconds;
                xp += count * h.Xp!.Value;
            }
        }

        if (kinds.Count == 0)
        {
            return null;
        }

        // the creatures a roll stands up that the approach does not list: met at the widest awareness, at the listed kinds' mean a creature (named): their share of fights, casts, mana, seconds and damage
        var unlisted = Math.Max(0, perRoll - listedPerRoll) * density * runMetres * 2 * widest;
        if (unlisted > 0 && creatures > 0)
        {
            var (meanCasts, meanMana, meanSeconds, meanFights, meanDamage) = (casts / creatures, mana / creatures, killSeconds / creatures, fights / creatures, damage / creatures);
            kinds.Add(new CrossingKind("unlisted (the listed kinds' mean)", 0, double.NaN, double.NaN, meanCasts, meanMana, meanSeconds, unlisted) { Way = "mean", Damage = meanDamage });
            unwinnable += unlisted * (unwinnable / creatures);
            creatures += unlisted;
            fights += unlisted * meanFights;
            casts += unlisted * meanCasts;
            mana += unlisted * meanMana;
            killSeconds += unlisted * meanSeconds;
            damage += unlisted * meanDamage;
        }

        var runSeconds = input.RunMetresPerSecond > 0 ? runMetres / input.RunMetresPerSecond : double.PositiveInfinity;
        var heal = Heals(damage, runSeconds + killSeconds, input);
        var seconds = runSeconds + killSeconds + heal.Seconds;
        var atPortal = input.ManaStart + (input.ManaRegen * seconds) - mana - heal.Mana;
        var (danceRounds, danceSeconds, afterDance) = (0, 0.0, atPortal);
        var dance = string.Empty;
        if (atPortal < input.Reserve && double.IsFinite(atPortal))
        {
            if (input.DanceCovers)
            {
                // M6 14 walk dance (rule 1): the shortfall at the reserve is made up in the lulls at the dance's rate (a round's net over a round's seconds), and those seconds are the crossing's; the regeneration over them is not
                // counted (the safe side). The crossing is then costly for mana no more: only its time (or its health, or a creature she cannot kill) can hold it.
                danceRounds = (int)Math.Ceiling((input.Reserve - atPortal) / input.DanceNet);
                danceSeconds = danceRounds * input.DanceSeconds;
                afterDance = atPortal + (danceRounds * input.DanceNet);
                seconds += danceSeconds;
                dance = string.Create(c, $"; with {danceRounds} rounds of the stamina dance ({danceSeconds:0} s): {afterDance:0} at the portal ({input.DanceWhy ?? "the dance's round"})");
            }
            else if (input.DanceWhy is { } no)
            {
                dance = "; the stamina dance does not cover her: " + no;
            }
        }

        var forced = afterDance < input.Reserve;
        var unhealed = heal.Unhealed && input.HealthMax > 0 && heal.Points >= input.HealthMax;
        var allowed = double.NaN;
        var timeWhy = $"time not judged ({input.RatesWhy})";
        if (input.RatePrevious is { } previous && input.RateRung is { } rung && rung > 0)
        {
            allowed = (input.CycleSeconds * Math.Max(0, 1 - (previous / rung))) + (xp / (rung / 3600.0));
            timeWhy = string.Create(c, $"{seconds:0} s against the {allowed:0} s the rung's rate allows (cycle {input.CycleSeconds:0} s x (1 - {previous:0} / {rung:0} xp/h) + the crossing's {xp:0} xp at the rung's rate; {input.RatesWhy})");
        }

        var slow = !double.IsNaN(allowed) && seconds > allowed;
        var listed = kinds.Where(k => k.Wcid != 0).ToList();
        var worst = (listed.Any(k => k.Way == "fight") ? listed.Where(k => k.Way == "fight") : listed).MaxBy(k => (k.Count * k.Mana) + k.Damage);
        var passed = creatures - fights - unwinnable;
        var runPast = input.PaceOf is null
            ? "every creature met is fought (no pace read)"
            : string.Create(c, $"run past at {input.RunMetresPerSecond:0.0} m/s: {fights:0.#} fights she cannot avoid, {passed:0.#} left behind or passed ({string.Join(", ", listed.Select(k => string.Create(c, $"{k.Name} {(double.IsNaN(k.Speed) ? "pace not known" : k.Speed.ToString("0.0", c) + " m/s")} {k.Way}")))})");
        var hits = input.DamageFactor is null
            ? "the hits not priced"
            : string.Create(c, $"{damage:0} damage taken, {heal.Points:0} of it healed after {input.HealthRegen:0.##}/s of regeneration{(heal.Unhealed ? " with no heal in her book" : string.Create(c, $" for {heal.Mana:0} mana and {heal.Seconds:0} s ({input.HealWhy})"))}");
        var unwon = listed.Where(k => k.Way == "unwinnable").Select(k => k.Name).ToList();
        if (unwon.Count > 0)
        {
            hits += string.Create(c, $"; {unwinnable:0.###} expected meetings with one that keeps pace and that she cannot kill on a full bar over the reserve ({string.Join(", ", unwon)}): each a recall");
        }

        var held = unwinnable >= UnwinnableMeetings;
        var numbers = string.Create(
            c,
            $"{runMetres:0} m of {ground}'s approach ({encounters} generators over {approach.Landblocks.Count} landblocks): {creatures:0.#} creatures expected, {casts:0} casts of {input.Bolt.Name} on War Magic {input.War} for {mana:0} mana, {seconds:0} s; {runPast}; {hits}; she starts at {input.ManaStart}, regenerates {input.ManaRegen:0.##}/s, keeps the reserve {input.Reserve}: {atPortal:0} at the portal{dance}; the dearest:{worst.Name} ({worst.Count:0.#} x {worst.Casts:0.#} casts, land {worst.Land:0.##}, {worst.Mean:0} a hit, {worst.Damage:0} damage each, {worst.Way}); {timeWhy}");
        var reason = held ? "unwinnable" : unhealed ? "health" : forced ? "mana" : slow ? "time" : string.Empty;
        var why = held
            ? $"the crossing more likely than not meets a creature she cannot outrun or kill, and a recall: {numbers}"
            : unhealed
                ? $"the crossing's hits would take all her health with no heal in her book: {numbers}"
                : forced
                    ? $"the crossing would force a recall before the portal: {numbers}"
                    : slow
                        ? $"the crossing costs more time than the rung's share of a cycle: {numbers}"
                        : $"the crossing is paid: {numbers}";
        return new CrossingCost(held || unhealed || forced || slow, reason, why, creatures, casts, mana, afterDance, seconds, allowed, kinds) { Fights = fights, Passed = passed, Damage = damage, HealMana = heal.Mana, Unwinnable = unwinnable, DanceRounds = danceRounds, DanceSeconds = danceSeconds };
    }

    /// <summary>
    /// M6 14 run past (D137): how she meets one kind (see the class): a kind as fast as her, or whose pace is not known, is a fight (its kill's cost, and its hits on her: a camp of <see cref="GroundApproachHostile.Pack"/> met together
    /// hits while each dies, about (pack + 1) / 2 kills' seconds a creature, plus the swing it lands before she stops); a slower thrower is passed (its throw's hits for her mean seconds in its range,
    /// <see cref="RunPast.MeanSecondsInRange"/>); a slower melee creature is left behind (nothing). With no pace read (<see cref="CrossingInput.PaceOf"/> null) every kind is a fight. M6 14 caught on the run (2026-11-15): the run leg
    /// now fights a kind as fast as her at its first swing in melee (<c>caught</c>), which is the one swing this price counts before she stops; before, it was measured until a second swing.
    /// </summary>
    private static CrossingKind RunPastKind(GroundApproachHostile h, CrossingKind kill, CrossingInput input)
    {
        var pace = input.PaceOf?.Invoke(h);
        var speed = pace?.MetresPerSecond ?? double.NaN;
        var fight = input.PaceOf is null || pace is not { } p || RunPast.KeepsPace(p.MetresPerSecond, input.RunMetresPerSecond);
        if (fight)
        {
            // in the fight a caster's war spells take their share of its attacks, its melee the rest; a thrower stands in melee and swings its hardest attack. M6 14 attack timing: with its derived timing its physical hits come at
            // its own hits a second (the book's share of casts already out) and its spells at their own; without, the profile's share every fallback interval
            var timing = TimingOf(h, input);
            var melee = h.Hitter is { } hitter ? PerSecondOn(hitter, h.DamageTypes, input, timing) : 0;
            var perSecond = timing.Derived
                ? melee + (input.SpellFactor is { } resist ? timing.SpellPerSecond(resist) : 0)
                : h.Ranged is { Spell: true } spell ? ((1 - spell.Share) * melee) + RangedPerSecond(h, spell, input, timing) : melee;
            return kill with { Way = "fight", Speed = speed, Damage = perSecond * ((kill.Seconds * (Math.Max(1, h.Pack) + 1) / 2.0) + timing.Seconds) };
        }

        if (h.Ranged is { } thrown)
        {
            return kill with { Way = "passing", Speed = speed, Damage = RangedPerSecond(h, thrown, input, TimingOf(h, input)) * RunPast.MeanSecondsInRange(thrown.RangeMetres, h.AwarenessMetres!.Value, input.RunMetresPerSecond) };
        }

        return kill with { Way = "outrun", Speed = speed, Damage = 0 };
    }

    /// <summary>M6 14 attack timing: the hostile's attack timing (<see cref="CrossingInput.TimingOf"/>), or its named fallback.</summary>
    private static AttackTiming TimingOf(GroundApproachHostile h, CrossingInput input) => input.TimingOf?.Invoke(h.Wcid, h.Name) ?? AttackTiming.Underived(h.Wcid, h.Name, "no timing read for the crossing");

    /// <summary>The damage a second one creature's hit deals her: its mean uncritical hit times what reaches her of its damage type at its hits a second (<see cref="AttackTiming.HitsPerSecond"/>); 0 when the hits are not priced.</summary>
    private static double PerSecondOn(LadderHitter hitter, uint types, CrossingInput input, AttackTiming timing) => input.DamageFactor is { } factor ? hitter.Hit * factor(types) * timing.HitsPerSecond : 0;

    /// <summary>
    /// The damage a second of a creature's ranged attack: with its derived timing, a throw's hit through her armour and resistance at its hits a second, a caster's priced spells at their own (<see cref="AttackTiming.SpellPerSecond(Func{uint, double}?)"/>);
    /// without, its share of the attacks, its mean hit, and her armour and resistance to a throw or her resistance alone to a spell, every fallback interval.
    /// </summary>
    private static double RangedPerSecond(GroundApproachHostile h, GroundRanged ranged, CrossingInput input, AttackTiming timing)
    {
        var factor = ranged.Spell ? input.SpellFactor : input.DamageFactor;
        if (factor is null)
        {
            return 0;
        }

        if (timing.Derived)
        {
            return ranged.Spell ? timing.SpellPerSecond(factor) : ranged.Hitter(h.Name, h.Wcid).Hit * factor(ranged.DamageTypes) * timing.HitsPerSecond;
        }

        return ranged.Share * ranged.Hitter(h.Name, h.Wcid).Hit * factor(ranged.DamageTypes) / timing.Seconds;
    }

    /// <summary>The heals of <paramref name="damage"/>: the regeneration over <paramref name="seconds"/> taken off first, the rest at her heal's mana and seconds a point; unhealed when her book holds no heal.</summary>
    private static (double Points, double Mana, double Seconds, bool Unhealed) Heals(double damage, double seconds, CrossingInput input)
    {
        var points = Math.Max(0, damage - (Math.Max(0, input.HealthRegen) * (double.IsInfinity(seconds) ? 0 : seconds)));
        return double.IsNaN(input.HealManaPerPoint)
            ? (points, 0, 0, points > 0)
            : (points, points * input.HealManaPerPoint, points * input.HealSecondsPerPoint, false);
    }

    /// <summary>
    /// What would change a costly verdict, in words: the War Magic at which it passes (her own up to <see cref="WarScanSpan"/> over it), a caster's damage modifier on her bolts at which it passes (to <see cref="CasterScanTop"/>), and the
    /// longest run of another route that passes. Each "none" says the scan's bound.
    /// </summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="approach">The approach's facts.</param>
    /// <param name="runMetres">The route's run.</param>
    /// <param name="input">Her live numbers.</param>
    /// <param name="boltAt">Her bolt at a War Magic (the cap rule rises with it), or null: her bolt now at every War Magic.</param>
    public static string WhatChanges(string ground, GroundApproach approach, double runMetres, CrossingInput input, Func<uint, CrossingBolt?>? boltAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.InvariantCulture;
        string war = string.Create(c, $"none up to {input.War + WarScanSpan} (now {input.War}; her bolt {(boltAt?.Invoke(input.War + WarScanSpan) ?? input.Bolt).Name} at the top)");
        for (var w = input.War + 5; w <= input.War + WarScanSpan; w += 5)
        {
            var bolt = boltAt?.Invoke(w) ?? input.Bolt;
            if (Judge(ground, approach, runMetres, input with { War = w, Bolt = bolt }) is { Costly: false })
            {
                war = string.Create(c, $"{w} (now {input.War}; her bolt {bolt.Name})");
                break;
            }
        }

        string caster = string.Create(c, $"none up to x{CasterScanTop:0.0} on her bolts (now x{input.CasterDamage:0.##})");
        for (var m = Math.Round(input.CasterDamage, 2) + 0.05; m <= CasterScanTop + 1e-9; m += 0.05)
        {
            if (Judge(ground, approach, runMetres, input with { CasterDamage = m }) is { Costly: false })
            {
                caster = string.Create(c, $"x{m:0.##} on her bolts' element (now x{input.CasterDamage:0.##})");
                break;
            }
        }

        string route = "none: no run of any length passes";
        var (lo, hi) = (0.0, runMetres);
        if (Judge(ground, approach, 0, input) is { Costly: false })
        {
            for (var i = 0; i < 30; i++)
            {
                var mid = (lo + hi) / 2;
                (lo, hi) = Judge(ground, approach, mid, input) is { Costly: false } ? (mid, hi) : (lo, mid);
            }

            route = string.Create(c, $"a run of {lo:0} m or less (this one {runMetres:0} m)");
        }

        return $"what would change it: her War Magic {war}; a held caster {caster}; another route {route}";
    }

    /// <summary>One kind's kill: the land chance, the mean landed damage, the casts, mana and seconds a kill.</summary>
    private static CrossingKind Kill(string name, uint wcid, int health, int magicDefence, double resist, double count, CrossingInput input)
    {
        var bolt = input.Bolt;
        var option = new WarOption(bolt.Name, bolt.Name, bolt.Row, bolt.Power, bolt.BaseMana, false, 0, 0);
        var scale = resist * input.CasterDamage;
        var picker = new WarSpellPicker(new WarPickSettings { ResistMod = scale });
        var mean = picker.MeanHit(option, input.War);
        var land = GearCasts.Chance(input.War, bolt.Power) * ManaConversionModel.SkillChance(input.War, (uint)Math.Max(0, magicDefence));
        var facts = new VitalsPolicyFacts { BoltMeanDamage = mean, BoltSpread = bolt.Row.Variance * picker.Settings.DamageRating * scale, BoltLandChance = land };
        var casts = VitalsPolicy.BoltsToKill(in facts, health);
        var cost = ManaConversionModel.ExpectedCost(bolt.BaseMana, bolt.Power, input.ManaConversion);
        return new CrossingKind(name, wcid, land, mean, casts, casts * cost, casts * bolt.Seconds, count);
    }

    /// <summary>
    /// A rung's planned experience an hour: its kills-an-hour floor (<see cref="LadderMoveOn.KillsAnHourFloor"/>) times the mean experience of its ground's targets (<see cref="GroundTarget.Xp"/>), or null with the reason.
    /// </summary>
    /// <param name="rung">The rung.</param>
    /// <param name="ground">Its ground, or null when its profile is not known.</param>
    /// <param name="why">The rate's source, or why there is none.</param>
    public static double? RungRate(LadderRung rung, HuntingGround? ground, out string why)
    {
        ArgumentNullException.ThrowIfNull(rung);
        var c = CultureInfo.InvariantCulture;
        if (rung.MoveOn.KillsAnHourFloor is not { } kills)
        {
            why = $"the rung {rung.Key} has no kills-an-hour floor";
            return null;
        }

        var xp = ground?.Targets.Where(t => t.Role == GroundRole.Target && t.Xp is not null).Select(t => (double)t.Xp!.Value).ToList() ?? [];
        if (xp.Count == 0)
        {
            why = $"the rung {rung.Key}'s ground {rung.GroundKey ?? "none"} names no target's xp";
            return null;
        }

        var rate = kills * xp.Average();
        why = string.Create(c, $"{rung.Key} {kills} kills/h x {xp.Average():0} xp");
        return rate;
    }

    /// <summary>
    /// M6 14 crossing prior: the creatures a metre of a run across <paramref name="approach"/> meets by its own facts, as <see cref="Judge"/> counts them before its kill model: the encounter generators over the approach's area, each listed
    /// kind noticing her over twice its awareness range at its share of a roll, and the roll's unlisted share at the widest awareness; every listed kind counts, met or passed (the measure counts kills, so this errs toward more). Null when
    /// the approach records no encounters, no roll or no area.
    /// </summary>
    /// <param name="approach">The ground's approach.</param>
    public static double? CreaturesPerMetre(GroundApproach approach)
    {
        ArgumentNullException.ThrowIfNull(approach);
        if (approach.Encounters is not { } encounters || approach.PerRoll is not { } perRoll || approach.AreaSquareMetres <= 0)
        {
            return null;
        }

        var density = encounters / approach.AreaSquareMetres;
        double perMetre = 0, listed = 0, widest = 0;
        foreach (var h in approach.Hostiles)
        {
            if (h.AwarenessMetres is not { } aware || h.PerRoll is not { } roll)
            {
                continue;
            }

            listed += roll;
            widest = Math.Max(widest, aware);
            perMetre += density * 2 * aware * roll;
        }

        if (widest <= 0)
        {
            return null;
        }

        return perMetre + (Math.Max(0, perRoll - listed) * density * 2 * widest);
    }

    /// <summary>
    /// M6 14 approach cost (rule 2, check, act, check on the run leg): after the seed kills, the run's measured kills a metre (the kills over the metres gained toward the entry's front) and net mana a kill (the fall of her mana since the run
    /// began over the kills: the bolts, the heals and the regeneration all in it) project the rest of the run: the kills its remaining metres hold at that rate, at that mana each. When that is more than her mana over the reserve, she will not
    /// reach the portal above it: she recalls now, before the mana line forces it.
    /// </summary>
    /// <param name="m">The measure.</param>
    /// <remarks>
    /// M6 14 crossing prior: when <see cref="CrossingMeasure.PriorPerMetre"/> is known the rate is the approach's facts over the whole run plus the kills measured, over the whole run plus the metres gained. M6 14 walk dance (the owner,
    /// 2026-11-15): while the dance covers her (<see cref="CrossingMeasure.DanceNet"/>) and she can still dance (<see cref="CrossingMeasure.DanceStalled"/> null), a shortfall does not give the crossing up: the answer says the dance is
    /// due (<see cref="CrossingMeasureAnswer.DanceDue"/>) to her maximum (M6 14 top-off; the reserve plus the need when her maximum is not read) whenever her mana is under it, and the crossing is measured again after the dance; the dances' mana
    /// (<see cref="CrossingMeasure.DancedMana"/>) is taken out of the fall a kill. Given up, the line says which: the dance cannot keep pace (its bounds, named), or it does not cover her.
    /// </remarks>
    public static CrossingMeasureAnswer Measured(in CrossingMeasure m)
    {
        var c = CultureInfo.InvariantCulture;
        var press = double.IsFinite(m.StaminaMana) ? Math.Max(0, m.StaminaMana) : 0;
        var above = (double)m.ManaNow - m.Reserve + press; // M6 14 crossing stamina: a press from the stamina held is mana she has
        if (m.Kills < Math.Max(1, m.SeedKills))
        {
            return new CrossingMeasureAnswer(false, false, string.Create(c, $"not judged yet: {m.Kills} of the {m.SeedKills} seed kills"), double.NaN, double.NaN, double.NaN, double.NaN, above);
        }

        var castsPerKill = (double)m.Casts / m.Kills;
        var danced = double.IsFinite(m.DancedMana) ? m.DancedMana : 0;
        var manaPerKill = ((double)m.ManaStart + danced - m.ManaNow) / m.Kills; // M6 14 walk dance: the dances' mana is not the fights' fall
        var gained = Math.Max(1.0, m.MetresAtStart - m.MetresNow);
        var measuredRate = m.Kills / gained;
        var rate = measuredRate;
        var rateWhy = string.Create(c, $"{measuredRate * 100:0.##} kills a 100 m");
        if (double.IsFinite(m.PriorPerMetre) && m.PriorPerMetre >= 0 && m.MetresAtStart > 0)
        {
            // M6 14 crossing prior (live 2026-11-14 18:50, ladder13): two kills at one spot after three in 167 m took the measured rate from 1.79 to 2.99 a 100 m and gave up a crossing the way out had priced at 6.4 creatures in 345 m
            // with 172 mana to spare. The approach's own facts count as the whole run seen once at their rate, and the kills measured add to them: a few kills move the projection, a run that keeps meeting more moves it all the
            // way. The mana line (the fight-backs' recall and heal reserve) stays the backstop when the projection is too kind.
            rate = ((m.PriorPerMetre * m.MetresAtStart) + m.Kills) / (m.MetresAtStart + gained);
            rateWhy = string.Create(c, $"{rate * 100:0.##} kills a 100 m (the approach's facts' {m.PriorPerMetre * 100:0.##} over its {m.MetresAtStart:0} m with the {measuredRate * 100:0.##} measured over {gained:0} m)");
        }

        var remaining = Math.Max(0, m.MetresNow) * rate;
        var needed = Math.Max(0, manaPerKill) * remaining;
        var shortOf = needed > above;
        var dancedWord = danced != 0 ? string.Create(c, $", the dances' {danced:0} taken out") : string.Empty;
        var numbers = string.Create(c, $"{m.Kills} kills in {m.Casts} casts ({castsPerKill:0.#} a kill), mana {m.ManaStart} -> {m.ManaNow} ({manaPerKill:0.#} a kill, the heals and the regeneration in it{dancedWord}) over {gained:0} m gained; {m.MetresNow:0} m to the front at {rateWhy}: {remaining:0.#} kills, {needed:0} mana, against {above:0} over the reserve {m.Reserve}{(press > 0 ? string.Create(c, $" ({press:0} of it a Stamina to Mana press from the stamina she holds)") : string.Empty)}");

        // M6 14 walk dance (rule 2): while the dance covers her and she can still dance, a shortfall is no reason to give the crossing up: she dances at the next lull, and the crossing is measured again after. It is given up only when
        // the dance cannot run or cannot keep pace (its bounds on the run, named), or when it does not cover her. M6 14 top-off (the owner, 2026-11-15: a top-off before the run resumes): the dance's line is her maximum
        // (the stamina ends near full too); with her maximum not read, the line the rest needs (the reserve plus the need), as before.
        var covers = m.DanceNet > 0 && double.IsFinite(m.DanceNet);
        var canDance = covers && m.DanceStalled is null;
        var line = m.ManaMax > 0 ? m.ManaMax : (uint)Math.Ceiling(m.Reserve + needed);
        var due = canDance && m.ManaNow < line;
        var abandon = shortOf && !canDance;
        var rounds = covers && due ? Math.Ceiling((line - (double)m.ManaNow) / m.DanceNet) : 0;
        var danceWord = m.ManaMax > 0
            ? string.Create(c, $"the stamina dance at the next lull tops her off: mana to her maximum {line}, stamina near full (the rest needs {needed:0} over the reserve {m.Reserve}; about {rounds:0} rounds at {m.DanceNet:0.#} a round), then the crossing is measured again")
            : string.Create(c, $"the stamina dance at the next lull to {line} (the reserve {m.Reserve} and the {needed:0} the rest needs, her maximum not read; about {rounds:0} rounds at {m.DanceNet:0.#} a round), then the crossing is measured again");
        string why;
        if (abandon)
        {
            var whyNot = covers ? $"; the stamina dance cannot keep pace: {m.DanceStalled}" : m.DanceWhy is { } no ? "; the stamina dance does not cover her: " + no : string.Empty;
            why = "she will not reach the portal above the reserve: " + numbers + whyNot;
        }
        else if (shortOf)
        {
            why = due
                ? $"she will not reach the portal above the reserve on the mana she holds, but the stamina dance covers her: {danceWord}: {numbers}"
                : $"she will not reach the portal above the reserve on the mana she holds, but the stamina dance covers her: she holds her maximum, so she dances at the next lull after the next fight: {numbers}";
        }
        else
        {
            why = due ? $"the crossing pays so far, and her mana is under {(m.ManaMax > 0 ? "her maximum" : "the need")}: {danceWord}: {numbers}" : "the crossing pays so far: " + numbers;
        }

        return new CrossingMeasureAnswer(abandon, true, why, castsPerKill, manaPerKill, remaining, needed, above) { DanceDue = due, DanceTo = canDance ? line : 0 };
    }
}
