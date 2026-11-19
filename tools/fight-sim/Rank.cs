using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>Her against one creature class, in the figures a ranking needs.</summary>
/// <param name="Wcid">The creature's weenie class.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level.</param>
/// <param name="Health">Its health.</param>
/// <param name="MagicDefense">Its effective Magic Defense.</param>
/// <param name="Xp">The experience a kill pays her (her modifier and the server's applied).</param>
/// <param name="Spell">Her fastest-killing spell on it.</param>
/// <param name="Land">The chance that spell is not resisted.</param>
/// <param name="CastsPerKill">Mean cast attempts a kill.</param>
/// <param name="SecondsPerKill">Mean seconds of casting a kill.</param>
/// <param name="ManaPerKill">Mean mana a kill.</param>
/// <param name="Incoming">Her mean health loss a second over a fight with one of them on her (its debuffs staying on her as they land).</param>
/// <param name="LossPerKill">Her mean health loss over one kill with one fresh one on her.</param>
/// <param name="BiggestHit">The largest single loss sampled.</param>
/// <param name="Caster">Whether it has a spell book.</param>
/// <param name="Death">Her chance of dying with one to six on her at once, from full health with no healing (Commands.DeathChance); index 0 is one on her.</param>
/// <param name="Notes">Gaps, each named.</param>
/// <param name="DiesPackHealed">Her chance of dying against its own pack size with her heals in the fight (InFight.cs; NaN: not played).</param>
/// <param name="OutOfManaPack">Her chance of standing out of mana with some of that pack alive, with her heals in the fight (NaN: not played).</param>
internal sealed record Verdict(
    uint Wcid, string Name, int Level, int Health, uint MagicDefense, double Xp, string Spell, double Land, double CastsPerKill, double SecondsPerKill, double ManaPerKill, double Incoming, double LossPerKill, int BiggestHit, bool Caster,
    double[] Death, IReadOnlyList<string> Notes, double DiesPackHealed = double.NaN, double OutOfManaPack = double.NaN)
{
    /// <summary>Her chance of a fight against its own pack that is not won with her heals: a death or out of mana (NaN: not played).</summary>
    public double NotWonHealed => DiesPackHealed + OutOfManaPack;

    /// <summary>Her chance of dying with one on her.</summary>
    public double Death1 => Death[0];

    /// <summary>Her chance of dying when <paramref name="company"/> come at once (a mean pack size: between two whole sizes the chance is taken in proportion; past six, six).</summary>
    public double DeathAt(double company)
    {
        var size = Math.Clamp(company, 1, Death.Length);
        var low = (int)Math.Floor(size);
        var high = Math.Min(Death.Length, low + 1);
        return Death[low - 1] + ((Death[high - 1] - Death[low - 1]) * (size - low));
    }

    /// <summary>Experience a second of casting at it.</summary>
    public double XpPerSecond => SecondsPerKill > 0 && !double.IsInfinity(SecondsPerKill) ? Xp / SecondsPerKill : 0;
}

/// <summary>Every placed creature ranked for her.</summary>
internal static partial class Ranking
{
    /// <summary>The largest pack the healed fights of rank play: a mean pack over it is played at it (Lifestone's own bound on the run's time).</summary>
    public const int MaxHealedPack = 12;

    /// <summary>The fights of each pack size with her heals in rank (<c>--healed-fights</c>).</summary>
    public const int DefaultHealedFights = 300;

    /// <summary>
    /// Her against <paramref name="monster"/>: her bolts screened with a few casts, the best two sampled in full; its attacks over the simulated stretch; with <paramref name="herFight"/>, the fights with her heals at
    /// <paramref name="company"/> on her (the census's mean pack: the two whole sizes around it, taken in proportion, at most <see cref="MaxHealedPack"/>).
    /// </summary>
    public static Verdict Evaluate(Player her, Creature monster, IReadOnlyList<ACE.Server.Entity.Spell> bolts, Options options, double xpModifier, HerFight? herFight = null, double company = 0)
    {
        var health = (int)monster.Health.MaxValue;
        var regen = Commands.RegenPerTick(monster);
        var screening = bolts.Select(b => Strikes.Of(her, monster, b, 200)).ToList();
        var screened = screening.OrderByDescending(s => s.DamagePerSecond).Take(2).ToList();
        // With her heals the fight bolts with the client's war pick (InFightRules.KillPrice): the screen's best by its price a cast is sampled in full too when it is not one of the two.
        var priced = herFight is null ? null : screening.OrderByDescending(s => s.MeanPerCast * s.CastChance / Math.Max(1e-9, InFightRules.KillPrice(s.CastSeconds + options.CastOverhead, 1, s.ManaPerAttempt))).First();
        (Strike Strike, KillStats Kill)? best = null;
        (Strike Strike, KillStats Kill)? pick = null;
        foreach (var candidate in priced is null || screened.Any(s => s.Spell.Id == priced.Spell.Id) ? screened : [.. screened, priced])
        {
            var strike = Strikes.Of(her, monster, candidate.Spell, options.Casts);
            var kill = Commands.Kills(strike, health, regen, options.CastOverhead, 1500);
            if (screened.Contains(candidate) && (best is null || kill.MeanSeconds < best.Value.Kill.MeanSeconds))
            {
                best = (strike, kill);
            }

            if (pick is null || InFightRules.KillPrice(kill.MeanSeconds, kill.MeanCasts, kill.MeanMana) < InFightRules.KillPrice(pick.Value.Kill.MeanSeconds, pick.Value.Kill.MeanCasts, pick.Value.Kill.MeanMana))
            {
                pick = (strike, kill);
            }
        }

        var blows = Blows.Sample(monster, her, options.Fights, options.FightSeconds, applyDebuffs: true);
        var herHealth = (int)her.Health.MaxValue;
        var death = new double[6];
        var loss1 = double.PositiveInfinity;
        for (var pack = 1; pack <= death.Length; pack++)
        {
            var (dies, loss) = best is null ? (1.0, double.PositiveInfinity) : Commands.DeathChance(best.Value.Strike, health, regen, options.CastOverhead, blows, herHealth, pack, 1000);
            death[pack - 1] = dies;
            if (pack == 1)
            {
                loss1 = loss;
            }
        }

        var (diesHealed, outOfMana) = (double.NaN, double.NaN);
        if (herFight is not null && pick is not null && company >= 1)
        {
            // With her heals (InFight.cs): the client's war pick and the same sampled attacks, at the pack's two whole sizes.
            var size = Math.Min(company, MaxHealedPack);
            var low = (int)Math.Floor(size);
            var high = Math.Min(MaxHealedPack, low + 1);
            var foe = FoeFight.Of(herFight, monster, pick.Value.Strike, blows);
            var fights = options.HealedFights ?? DefaultHealedFights;
            var atLow = InFight.Run(herFight, Enumerable.Repeat(foe, low).ToList(), FightMode.Heals, fights, 20261119 + low);
            var atHigh = size > low ? InFight.Run(herFight, Enumerable.Repeat(foe, high).ToList(), FightMode.Heals, fights, 20261119 + high) : atLow;
            (diesHealed, outOfMana) = PackVerdict.Between(atLow, atHigh, size);
        }

        return new Verdict(
            monster.WeenieClassId, monster.Name, monster.Level ?? 0, health, monster.GetEffectiveMagicDefense(), (monster.XpOverride ?? 0) * xpModifier, best?.Strike.Spell.Name ?? "none",
            best?.Strike.LandChance ?? 0, best?.Kill.MeanCasts ?? double.PositiveInfinity, best?.Kill.MeanSeconds ?? double.PositiveInfinity, best?.Kill.MeanMana ?? double.PositiveInfinity, blows.DamagePerSecond, loss1,
            blows.Blows.Select(b => b.Damage).DefaultIfEmpty(0).Max(), monster.Biota.PropertiesSpellBook is { Count: > 0 }, death, blows.Notes, diesHealed, outOfMana);
    }

    /// <summary>The rank command.</summary>
    public static int Run(Options options)
    {
        var her = World.RestoreHer(options.Her, options.Wield, options.Bars);
        var bolts = Strikes.BoltsOf(her);
        if (bolts.Count == 0)
        {
            throw new ArgumentException($"{her.Name} knows no single-projectile war spell: nothing to rank");
        }

        var notes = new List<string>();
        Console.Error.WriteLine("census: reading every placement and rolling every generator...");
        var slots = Census.Build(notes);
        var names = GroundNames();
        var xpModifier = her.GetXPAndLuminanceModifier(XpType.Kill) * PropertyManager.GetDouble("xp_modifier").Item;
        var verdicts = new Dictionary<uint, Verdict>();
        var failed = 0;
        var wcids = slots.Select(s => s.Wcid).Distinct().ToList();
        var bySlots = slots.GroupBy(s => s.Wcid).ToDictionary(g => g.Key, g => (Count: g.Sum(s => s.Count), Blocks: g.Select(s => s.Landblock).Distinct().Count(), Company: g.Sum(s => s.Count * s.Company) / g.Sum(s => s.Count)));
        var herFight = HerFight.Of(her, options.CastOverhead, World.TriedIds(options.With)); // with her heals (InFight.cs): her spells and figures, read once
        Console.Error.WriteLine(Line($"census: {slots.Count} creature slots groups, {wcids.Count} creature classes placed, {slots.Select(s => s.Landblock).Distinct().Count()} landblocks"));
        var done = 0;
        var herState = HerState(her);
        foreach (var wcid in wcids)
        {
            if (++done % 200 == 0)
            {
                Console.Error.WriteLine(Line($"  {done} of {wcids.Count} creature classes..."));
            }

            try
            {
                var monster = World.BuildCreature(wcid);
                var level = monster.Level ?? 0;
                if (level < options.MinLevel || level > options.MaxLevel || (monster.XpOverride ?? 0) <= 0 || monster.Health.MaxValue == 0 || !(monster.GetProperty(PropertyBool.Attackable) ?? true))
                {
                    monster.Destroy(false);
                    continue;
                }

                verdicts[wcid] = Evaluate(her, monster, bolts, options, xpModifier, herFight, bySlots.GetValueOrDefault(wcid).Company);
                monster.Destroy(false);

                // Every creature is fought by the one restored her: a change to her that outlives a creature's fights is said, loudly, with the creature that made it (the creatures after it met the changed her).
                var now = HerState(her);
                if (now != herState)
                {
                    notes.Insert(0, Line($"TODO(fight-sim-rank-her-drift): her own state changed while weenie {wcid} ({monster.Name}) was fought offline: maximum health {herState.Health} -> {now.Health}, stamina {herState.Stamina} -> {now.Stamina}, mana {herState.Mana} -> {now.Mana}, enchantments {herState.Enchantments} -> {now.Enchantments}; every creature after it in this run met the changed her"));
                    herState = now;
                }
            }
            catch (Exception e) when (e is NullReferenceException or InvalidOperationException or ArgumentException or KeyNotFoundException or InvalidCastException)
            {
                failed++;
                notes.Add(Line($"TODO(fight-sim-creature-{wcid}): ACE could not fight weenie {wcid} offline ({e.GetType().Name}: {e.Message.Split('\n')[0]}); it is left out of the ranking"));
            }
        }

        var text = new StringBuilder();
        text.AppendLine(Line($"{her.Name.ToUpperInvariant()} (level {her.Level}), every placed creature of level {options.MinLevel} to {(options.MaxLevel == int.MaxValue ? "any" : options.MaxLevel.ToString(CultureInfo.InvariantCulture))}: {verdicts.Count} classes fought offline through ACE's own code"));
        text.AppendLine("xp/s is experience for a second of casting at it; 'loss a kill' is her health lost over one kill with ONE of them on her (its debuffs staying on her). Walking, looting, recovery and waits are not in these numbers.");
        text.AppendLine(Line($"'dies 1' is her chance of dying with one on her from full health ({her.Health.MaxValue}) if she never heals: how hard it hits against how fast she kills it. 'pack' is how many come when one is woken, by the server's alert rule"));
        text.AppendLine("and the placements (Census.cs), the mean over where it stands; 'dies pack' is her chance of dying against that many at once.");
        text.AppendLine(Line($"'dies pack healed' and 'oom pack' are the same pack with her heals in the fight (InFight.cs: the client's own rules choose each cast, {options.HealedFights ?? DefaultHealedFights} fights at each of the two whole sizes around the pack,"));
        text.AppendLine(Line($"taken in proportion, at most {MaxHealedPack} at once, not capped at six): her chance of dying, and of standing out of mana with some of them alive. {InFight.Gaps}."));
        text.AppendLine();
        text.AppendLine("CREATURES, by experience for her casting time");
        text.AppendLine(Line($"  {"creature",-34} {"wcid",6} {"lvl",4} {"health",6} {"MD",4} {"xp a kill",10} {"best spell",-20} {"lands",6} {"casts",6} {"s",6} {"mana",6} {"xp/s",7} {"hp/s in",8} {"loss a kill",11} {"biggest",7} {"dies 1",7} {"pack",5} {"dies pack",9} {"slots",6} {"blocks",6} {"dies pack healed",16} {"oom pack",8}"));
        foreach (var v in verdicts.Values.OrderByDescending(v => v.XpPerSecond).Take(options.Top))
        {
            var where = bySlots.GetValueOrDefault(v.Wcid);
            text.AppendLine(Line($"  {Clip(v.Name + (v.Caster ? " (casts)" : string.Empty), 34),-34} {v.Wcid,6} {v.Level,4} {v.Health,6} {v.MagicDefense,4} {v.Xp,10:N0} {Clip(v.Spell, 20),-20} {v.Land,6:P0} {v.CastsPerKill,6:0.0} {v.SecondsPerKill,6:0.0} {v.ManaPerKill,6:0} {v.XpPerSecond,7:N0} {v.Incoming,8:0.00} {v.LossPerKill,11:0.0} {v.BiggestHit,7} {v.Death1,7:P1} {where.Company,5:0.0} {v.DeathAt(where.Company),9:P1} {where.Count,6:0.#} {where.Blocks,6} {Share(v.DiesPackHealed),16} {Share(v.OutOfManaPack),8}"));
        }

        // Grounds: a landblock's whole population, cleared in turn and waited for.
        text.AppendLine();
        text.AppendLine("GROUNDS, by experience an hour of casting when she clears the whole landblock over and over (the cycle is the longer of her casting time for all of it and its respawn; walking, looting and recovery not counted)");
        text.AppendLine("'pack' is the mean number that come at once there; 'dies 1' and 'dies pack' are the slot-weighted chances over the landblock's creatures; 'way in' is the portal the world database places that leads there, with its level limits (outdoor land is walked to).");
        text.AppendLine(GroundsHeader());
        var reach = Reach(her.Level ?? 0);
        var grounds = new List<(double Rate, string Row, bool Open, double WorstDeath, string Tsv, double WorstHealed)>();
        foreach (var block in slots.GroupBy(s => s.Landblock))
        {
            var fought = block.Where(s => verdicts.ContainsKey(s.Wcid) && !double.IsInfinity(verdicts[s.Wcid].SecondsPerKill)).ToList();
            if (fought.Count == 0)
            {
                continue;
            }

            var count = fought.Sum(s => s.Count);
            var xp = fought.Sum(s => s.Count * verdicts[s.Wcid].Xp);
            var castTime = fought.Sum(s => s.Count * verdicts[s.Wcid].SecondsPerKill);
            var respawns = fought.Where(s => !double.IsNaN(s.Delay)).Select(s => s.Delay + (s.Tick / 2)).ToList();
            var respawn = respawns.Count == 0 ? double.NaN : respawns.Max();
            var cycle = double.IsNaN(respawn) ? castTime : Math.Max(castTime, respawn);
            var rate = xp / cycle * 3600 / 1e6;
            var incoming = fought.Sum(s => s.Count * verdicts[s.Wcid].Incoming) / count;
            var dies1 = fought.Sum(s => s.Count * verdicts[s.Wcid].Death1) / count;
            var pack = fought.Sum(s => s.Count * s.Company) / count;
            var diesPack = fought.Sum(s => s.Count * verdicts[s.Wcid].DeathAt(s.Company)) / count;
            var wayIn = block.Any(s => !s.Indoor) && !reach.ContainsKey(block.Key) ? "outdoor land" : reach.GetValueOrDefault(block.Key, "NO PLACED PORTAL");
            var open = !wayIn.StartsWith("SHUT", StringComparison.Ordinal) && !wayIn.StartsWith("UNPLACED", StringComparison.Ordinal) && !wayIn.StartsWith("NO PLACED", StringComparison.Ordinal)
                && !wayIn.StartsWith("QUEST FLAG", StringComparison.Ordinal);

            // The worst death chance at its own pack size among the creatures that make up at least a twentieth of the landblock.
            var worst = fought.GroupBy(s => s.Wcid).Where(g => g.Sum(s => s.Count) >= count / 20)
                .Select(g => verdicts[g.Key].DeathAt(g.Sum(s => s.Count * s.Company) / g.Sum(s => s.Count))).DefaultIfEmpty(0).Max();
            var mostly = string.Join(", ", fought.GroupBy(s => verdicts[s.Wcid].Name).OrderByDescending(g => g.Sum(s => s.Count)).Take(3).Select(g => Line($"{g.Sum(s => s.Count):0.#} {g.Key}")));
            // With her heals: slot-weighted as dies pack is, and the worst share of fights not won (dead or out of mana) among the creatures that make up a twentieth of it.
            var diesHealed = fought.Sum(s => s.Count * verdicts[s.Wcid].DiesPackHealed) / count;
            var outOfMana = fought.Sum(s => s.Count * verdicts[s.Wcid].OutOfManaPack) / count;
            var worstHealed = fought.GroupBy(s => s.Wcid).Where(g => g.Sum(s => s.Count) >= count / 20).Select(g => verdicts[g.Key].NotWonHealed).DefaultIfEmpty(0).Max();
            grounds.Add((rate, Line($"  0x{block.Key:X4}    {Clip(names.GetValueOrDefault(block.Key, string.Empty), 30),-30} {count,6:0.#} {xp,12:N0} {castTime,8:0} s {(double.IsNaN(respawn) ? "none" : Line($"{respawn:0} s")),8} {rate,7:0.00} {incoming,8:0.00} {pack,5:0.0} {dies1,7:P1} {diesPack,9:P1} {Clip(wayIn, 34),-34} {Clip(mostly, 70),-40} {Share(diesHealed),16} {Share(outOfMana),8}"), open, worst,
                Line($"0x{block.Key:X4}\t{names.GetValueOrDefault(block.Key, string.Empty)}\t{count:0.#}\t{xp:0}\t{castTime:0}\t{(double.IsNaN(respawn) ? string.Empty : Line($"{respawn:0}"))}\t{rate:0.00}\t{incoming:0.00}\t{pack:0.0}\t{dies1:0.000}\t{diesPack:0.000}\t{worst:0.000}\t{wayIn}\t{mostly}\t{diesHealed:0.000}\t{outOfMana:0.000}\t{worstHealed:0.000}"), worstHealed));
        }

        foreach (var ground in grounds.OrderByDescending(g => g.Rate).Take(options.Top))
        {
            text.AppendLine(ground.Row);
        }

        text.AppendLine();
        text.AppendLine(Line($"GROUNDS OPEN TO HER NOW (level {her.Level}) WHERE ITS PACKS DO NOT KILL HER: a placed portal lets her in at her level and asks no quest flag (or it is outdoor land), and no creature that makes up a twentieth of it killed her in more than 1 of 200 fights against its own pack size, from full health with no healing"));
        text.AppendLine(GroundsHeader());
        foreach (var ground in grounds.Where(g => g.Open && g.WorstDeath <= 0.005).OrderByDescending(g => g.Rate).Take(options.Top))
        {
            text.AppendLine(ground.Row);
        }

        text.AppendLine();
        text.AppendLine(Line($"GROUNDS OPEN TO HER NOW (level {her.Level}) WHERE ITS PACKS DO NOT KILL HER WITH HER HEALS: as above, with the fight played by her own rules (InFight.cs): no creature that makes up a twentieth of it ended more than 1 fight in 100 against its own pack with her dead or out of mana"));
        text.AppendLine(GroundsHeader());
        foreach (var ground in grounds.Where(g => g.Open && g.WorstHealed <= 0.01).OrderByDescending(g => g.Rate).Take(options.Top))
        {
            text.AppendLine(ground.Row);
        }

        if (failed > 0 || notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(Line($"NOT SIMULATED ({notes.Count} notes; the first 12):"));
            foreach (var note in notes.Take(12))
            {
                text.AppendLine("  " + note);
            }
        }

        var report = text.ToString();
        Console.Write(report);
        var outFile = options.Out ?? Path.Combine(Checkout.Innermost(), "cache", "fight-sim", Line($"rank-{her.Name}-{options.MinLevel}-{(options.MaxLevel == int.MaxValue ? "any" : options.MaxLevel.ToString(CultureInfo.InvariantCulture))}.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        File.WriteAllText(outFile, report);
        var table = new StringBuilder("wcid\tname\tlevel\thealth\tmagic_defense\txp\tspell\tland\tcasts\tseconds\tmana\txp_per_s\thp_per_s_in\tloss_a_kill\tbiggest\tdies_1\tdies_2\tdies_3\tdies_4\tdies_5\tdies_6\tpack\tdies_pack\tcaster\tslots\tlandblocks\tdies_pack_healed\tout_of_mana_pack\n");
        foreach (var v in verdicts.Values.OrderByDescending(v => v.XpPerSecond))
        {
            var where = bySlots.GetValueOrDefault(v.Wcid);
            table.AppendLine(Line($"{v.Wcid}\t{v.Name}\t{v.Level}\t{v.Health}\t{v.MagicDefense}\t{v.Xp:0}\t{v.Spell}\t{v.Land:0.000}\t{v.CastsPerKill:0.00}\t{v.SecondsPerKill:0.0}\t{v.ManaPerKill:0.0}\t{v.XpPerSecond:0}\t{v.Incoming:0.00}\t{v.LossPerKill:0.0}\t{v.BiggestHit}\t{string.Join("\t", v.Death.Select(d => d.ToString("0.000", CultureInfo.InvariantCulture)))}\t{where.Company:0.0}\t{v.DeathAt(where.Company):0.000}\t{v.Caster}\t{where.Count:0.#}\t{where.Blocks}\t{v.DiesPackHealed:0.000}\t{v.OutOfManaPack:0.000}"));
        }

        File.WriteAllText(Path.ChangeExtension(outFile, ".tsv"), table.ToString());
        File.WriteAllText(Path.ChangeExtension(outFile, ".grounds.tsv"),
            "landblock\tname\tslots\txp_a_clear\tcast_seconds\trespawn_seconds\tm_xp_per_h\thp_per_s_in\tpack\tdies_1\tdies_pack\tworst_dies_pack\tway_in\tmostly\tdies_pack_healed\tout_of_mana_pack\tworst_not_won_healed\n" + string.Join("\n", grounds.OrderByDescending(g => g.Rate).Select(g => g.Tsv)) + "\n");
        Console.Error.WriteLine($"written: {outFile}, its .tsv (every creature class) and its .grounds.tsv (every landblock)");
        return 0;
    }

    /// <summary>A name for each landblock a portal leads into (the portal's name without "Portal"), and the repository's ground profiles' names over them.</summary>
    private static Dictionary<ushort, string> GroundNames()
    {
        var names = new Dictionary<ushort, string>();
        using var world = new WorldDbContext();
        var destinations = from p in world.WeeniePropertiesPosition
                           where p.PositionType == (ushort)PositionType.Destination
                           join n in world.WeeniePropertiesString on p.ObjectId equals n.ObjectId
                           where n.Type == (ushort)PropertyString.Name
                           select new { p.ObjCellId, n.Value };
        foreach (var group in destinations.AsEnumerable().GroupBy(d => (ushort)(d.ObjCellId >> 16)))
        {
            var name = group.GroupBy(d => d.Value).OrderByDescending(g => g.Count()).First().Key;
            names[group.Key] = name.Replace(" Portal", string.Empty, StringComparison.Ordinal).Replace("Portal to ", string.Empty, StringComparison.Ordinal);
        }

        var profiles = Path.Combine(Checkout.Innermost(), "src", "Lifestone.Client", "Session", "Grounds", "profiles");
        if (Directory.Exists(profiles))
        {
            foreach (var file in Directory.EnumerateFiles(profiles, "*.json"))
            {
                var json = File.ReadAllText(file);
                var name = NameField().Match(json);
                var block = LandblockField().Match(json);
                if (name.Success && block.Success)
                {
                    names[ushort.Parse(block.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)] = name.Groups[1].Value + " (profile)";
                }
            }
        }

        return names;
    }

    /// <summary>
    /// For each landblock a portal leads into: the portal the world database places most often that she may use at <paramref name="level"/> (its MinLevel and MaxLevel, PropertyInt 86 and 87) and that asks no quest
    /// flag (QuestRestriction), else a placed portal that asks one (named: whether she holds it is not read), else the portal she may not use yet with its limits, else a portal that exists but is placed nowhere
    /// (reached by a gem, a summon or a quest, or not at all).
    /// </summary>
    private static Dictionary<ushort, string> Reach(int level)
    {
        using var world = new WorldDbContext();
        var destinations = world.WeeniePropertiesPosition.Where(p => p.PositionType == (ushort)PositionType.Destination).Select(p => new { p.ObjectId, p.ObjCellId }).ToList();
        var portalNames = world.WeeniePropertiesString.Where(n => n.Type == (ushort)PropertyString.Name).Select(n => new { n.ObjectId, n.Value }).ToList().GroupBy(n => n.ObjectId).ToDictionary(g => g.Key, g => g.First().Value);
        var limits = world.WeeniePropertiesInt.Where(i => i.Type == (ushort)PropertyInt.MinLevel || i.Type == (ushort)PropertyInt.MaxLevel).Select(i => new { i.ObjectId, i.Type, i.Value }).ToList();
        var min = limits.Where(l => l.Type == (ushort)PropertyInt.MinLevel).GroupBy(l => l.ObjectId).ToDictionary(g => g.Key, g => g.First().Value);
        var max = limits.Where(l => l.Type == (ushort)PropertyInt.MaxLevel).GroupBy(l => l.ObjectId).ToDictionary(g => g.Key, g => g.First().Value);
        var placed = world.LandblockInstance.GroupBy(i => i.WeenieClassId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(g => g.Key, g => g.Count);
        var quest = world.WeeniePropertiesString.Where(n => n.Type == (ushort)PropertyString.QuestRestriction).Select(n => new { n.ObjectId, n.Value }).ToList().GroupBy(n => n.ObjectId)
            .ToDictionary(g => g.Key, g => g.First().Value);
        var reach = new Dictionary<ushort, string>();
        foreach (var group in destinations.GroupBy(d => (ushort)(d.ObjCellId >> 16)))
        {
            var portals = group.Select(d => d.ObjectId).Distinct().Select(id => new
            {
                Name = portalNames.GetValueOrDefault(id, "portal " + id),
                Min = min.GetValueOrDefault(id),
                Max = max.GetValueOrDefault(id),
                Placed = placed.GetValueOrDefault(id),
                Quest = quest.GetValueOrDefault(id, string.Empty),
            }).ToList();
            string Limits(int lo, int hi) => (lo > 0, hi > 0) switch
            {
                (true, true) => Line($"level {lo}-{hi}"),
                (true, false) => Line($"level {lo}+"),
                (false, true) => Line($"to level {hi}"),
                _ => "any level",
            };
            var usable = portals.Where(p => p.Placed > 0 && level >= p.Min && (p.Max <= 0 || level <= p.Max)).OrderByDescending(p => p.Placed).ToList();
            var open = usable.FirstOrDefault(p => p.Quest.Length == 0);
            var flagged = usable.FirstOrDefault();
            var shut = portals.Where(p => p.Placed > 0).OrderBy(p => p.Min).FirstOrDefault();
            reach[group.Key] = open != null ? Line($"{open.Name} ({Limits(open.Min, open.Max)})")
                : flagged != null ? Line($"QUEST FLAG {flagged.Quest}: {flagged.Name} ({Limits(flagged.Min, flagged.Max)})")
                : shut != null ? Line($"SHUT TO HER: {shut.Name} ({Limits(shut.Min, shut.Max)})")
                : Line($"UNPLACED: {portals[0].Name} ({Limits(portals[0].Min, portals[0].Max)})");
        }

        return reach;
    }

    [GeneratedRegex("\"name\":\\s*\"([^\"]+)\"")]
    private static partial Regex NameField();

    [GeneratedRegex("landblock 0x([0-9A-Fa-f]{4})")]
    private static partial Regex LandblockField();

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "~";

    /// <summary>What a creature's fights must leave of her as they found it: her three maximums and how many enchantments she holds.</summary>
    private static (uint Health, uint Stamina, uint Mana, int Enchantments) HerState(Player her) =>
        (her.Health.MaxValue, her.Stamina.MaxValue, her.Mana.MaxValue, her.Biota.PropertiesEnchantmentRegistry?.Count ?? 0);

    /// <summary>A share in percent, or "-" when it was not played.</summary>
    private static string Share(double share) => double.IsNaN(share) ? "-" : share.ToString("P1", CultureInfo.InvariantCulture);

    /// <summary>The grounds tables' header.</summary>
    private static string GroundsHeader() =>
        Line($"  {"landblock",-9} {"name",-30} {"slots",6} {"xp a clear",12} {"cast time",10} {"respawn",8} {"M xp/h",7} {"hp/s in",8} {"pack",5} {"dies 1",7} {"dies pack",9} {"way in",-34} {"mostly",-40} {"dies pack healed",16} {"oom pack",8}");

    private static string Line(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The checkout the tool runs in.</summary>
internal static class Checkout
{
    /// <summary>The nearest enclosing folder that holds Lifestone.slnx (the worktree when run from one).</summary>
    public static string Innermost()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new ArgumentException("run it from inside a Lifestone checkout, or pass --out");
    }
}
