using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>A kill of one creature by one spell, simulated from the sampled casts.</summary>
/// <param name="MeanCasts">The mean cast attempts a kill takes (fizzles and resists included).</param>
/// <param name="P90Casts">Nine kills in ten take no more attempts than this.</param>
/// <param name="MeanSeconds">The mean seconds of casting a kill takes.</param>
/// <param name="MeanMana">The mean mana a kill costs her.</param>
internal sealed record KillStats(double MeanCasts, int P90Casts, double MeanSeconds, double MeanMana);

/// <summary>The commands.</summary>
internal static class Commands
{
    private static readonly Skill[] HerSkills =
    [
        Skill.WarMagic, Skill.LifeMagic, Skill.CreatureEnchantment, Skill.ItemEnchantment, Skill.ManaConversion, Skill.MagicDefense, Skill.MeleeDefense, Skill.MissileDefense, Skill.ArcaneLore, Skill.Run,
    ];

    private static readonly DamageType[] Elements =
    [
        DamageType.Slash, DamageType.Pierce, DamageType.Bludgeon, DamageType.Fire, DamageType.Cold, DamageType.Acid, DamageType.Electric, DamageType.Nether,
    ];

    /// <summary>Her sheet as the server last saved her.</summary>
    public static int Her(Options options)
    {
        var her = World.RestoreHer(options.Her, options.Wield, options.Bars);
        var text = new StringBuilder();
        text.AppendLine(Line($"{her.Name}, level {her.Level}, as the server last saved her (ACE saves a player every {PropertyManager.GetLong("player_save_interval").Item} s and at logout)"));
        text.AppendLine(Line($"  health {her.Health.Current}/{her.Health.MaxValue}  stamina {her.Stamina.Current}/{her.Stamina.MaxValue}  mana {her.Mana.Current}/{her.Mana.MaxValue}"));
        text.AppendLine("  attributes (base / with enchantments): " + string.Join(", ", her.Attributes.Select(a => Line($"{a.Key} {a.Value.Base}/{a.Value.Current}"))));
        text.AppendLine("  skills (base / with enchantments):");
        foreach (var skill in HerSkills)
        {
            var s = her.GetCreatureSkill(skill);
            text.AppendLine(Line($"    {skill,-20} {s.Base,4} / {s.Current,4}  {s.AdvancementClass}"));
        }

        text.AppendLine("  wields: " + string.Join("; ", her.EquippedObjects.Values.Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal)));
        var wand = her.GetEquippedWand();
        if (wand != null)
        {
            text.AppendLine(Line($"  caster: {wand.Name}, elemental damage x{wand.ElementalDamageMod ?? 1.0:0.###} on {wand.W_DamageType}, mana conversion x{1 + (wand.ManaConversionMod ?? 0):0.###}, melee defence x{wand.WeaponDefense ?? 1.0:0.###}"));

            // M6 16 E5e3: a quest weapon's fixed resistance cleaving (WorldObject_Weapon.cs:461-486 GetWeaponResistanceModifier @95ef426d: 1 + ResistanceModifier on its ResistanceModifierType, which a target's own
            // vulnerability to that type replaces only when it is the larger, Creature_Properties.cs:132-135) and its slayer (GetWeaponCreatureSlayerModifier): the Atlan weapons carry the first
            if (wand.ResistanceModifierType is { } cleaves)
            {
                text.AppendLine(Line($"    resistance cleaving: a {cleaves} spell strikes as if its target had a vulnerability of x{1.0 + (wand.ResistanceModifier ?? 1.0):0.###} to it (ResistanceModifier {wand.ResistanceModifier?.ToString("0.###", CultureInfo.InvariantCulture) ?? "absent: ACE's 1"})"));
            }

            if (wand.SlayerCreatureType is { } slays)
            {
                text.AppendLine(Line($"    slayer: x{wand.SlayerDamageBonus ?? 1.0:0.###} on {slays}"));
            }

            text.AppendLine(Line($"    masterable: {(wand.IsMasterable ? "yes" : "no (its description says it is tough to master)")}; her spells' rating with it x{(her.GetHeritageBonus(wand) ? 1.05 : 1.0):0.##} (SpellProjectile.cs:735 with Player_Skills.cs:646-659: the server's universal_masteries is {PropertyManager.GetBool("universal_masteries").Item})"));
            text.AppendLine(Line($"    with her enchantments as ACE reads them in a fight: elemental x{WorldObject.GetCasterElementalDamageModifier(wand, her, null, wand.W_DamageType):0.###} on {wand.W_DamageType} against a monster, her Mana Conversion x{WorldObject.GetWeaponManaConversionModifier(her):0.####}, melee defence x{WorldObject.GetWeaponMeleeDefenseModifier(her):0.###}"));
        }

        text.AppendLine(Line($"  enchantments on her at the save: {her.Biota.PropertiesEnchantmentRegistry?.Count ?? 0}"));
        foreach (var e in (her.Biota.PropertiesEnchantmentRegistry ?? []).OrderBy(e => e.SpellId))
        {
            var spell = new Spell((uint)e.SpellId);
            var left = e.Duration < 0 ? "while worn" : Line($"{Math.Max(0, e.Duration + e.StartTime):0} s left");
            text.AppendLine(Line($"    {(spell.NotFound ? "spell " + e.SpellId : spell.Name),-40} {(spell.NotFound || !spell.IsHarmful ? string.Empty : "HARMFUL "),-8} {left}"));
        }

        text.AppendLine("  her single-projectile war spells (the highest she knows of each): " + string.Join(", ", Strikes.BoltsOf(her).Select(s => s.Name)));
        Console.Write(text);
        return 0;
    }

    /// <summary>One of her spells: its cast as the server times it, its fizzle chance and its mana.</summary>
    public static int Cast(Options options)
    {
        var her = World.RestoreHer(options.Her, options.Wield, options.Bars);
        var subject = options.Subject ?? throw new ArgumentException("cast needs a spell's name or id");
        var spells = (her.Biota.PropertiesSpellBook?.Keys ?? []).Select(id => new Spell((uint)id)).Where(s => !s.NotFound).ToList();
        var spell = uint.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? new Spell(id)
            : spells.FirstOrDefault(s => string.Equals(s.Name, subject, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"her spell book holds no spell named '{subject}'");
        if (spell.NotFound)
        {
            throw new ArgumentException($"the spell table holds no spell {subject}");
        }

        var text = new StringBuilder();
        text.AppendLine(Line($"{spell.Name} (spell {spell.Id}), {spell.School}, level {spell.Level}, power {spell.Power}, base mana {spell.BaseMana}{(spells.Any(s => s.Id == spell.Id) ? string.Empty : "  (NOT in her spell book)")}"));
        text.AppendLine(Line($"  her {spell.School} {her.GetCreatureSkill(spell.School).Current}: the cast succeeds {Strikes.CastChance(her, spell):P1} of the time (Player_Magic.cs GetCastingPreCheckStatus)"));
        text.Append(Strikes.CastWorking(her, spell));
        Console.Write(text);
        return 0;
    }

    /// <summary>A creature as the server builds it.</summary>
    public static int Creature(Options options)
    {
        var monster = World.BuildCreature(options.Subject ?? throw new ArgumentException("creature needs a name or a weenie class id"));
        Console.Write(CreatureSheet(monster));
        return 0;
    }

    private static string CreatureSheet(Creature monster)
    {
        var text = new StringBuilder();
        text.AppendLine(Line($"{monster.Name} (weenie {monster.WeenieClassId}), level {monster.Level}, experience {monster.XpOverride ?? 0:N0} a kill"));
        text.AppendLine(Line($"  health {monster.Health.MaxValue}  stamina {monster.Stamina.MaxValue}  mana {monster.Mana.MaxValue}  ({RegenText(monster)})"));
        // The bestiary's behaviour columns, as the server's own objects have them (M6 item 16 E1d: the proof of the client's reader against ACE's code). The two ranges are before the server's mob_awareness_range.
        var sees = monster.VisualAwarenessRange ?? ACE.Server.WorldObjects.Creature.VisualAwarenessRange_Default;
        text.AppendLine(Line($"  Run {monster.GetCreatureSkill(Skill.Run).Current}  scale {monster.ObjScale ?? 1.0f:0.###}  sees a player at {sees:0.###} m, hears its kind call at {monster.AuralAwarenessRange ?? sees:0.###} m  tolerance {(int)monster.Tolerance}"));
        text.AppendLine(Line($"  Magic Defense {monster.GetEffectiveMagicDefense()}  Melee Defense {monster.GetCreatureSkill(Skill.MeleeDefense).Current}  Missile Defense {monster.GetCreatureSkill(Skill.MissileDefense).Current}"));
        text.AppendLine("  damage it takes by type (1 = full): " + string.Join("  ", Elements.Select(d => Line($"{d} {monster.GetResistanceMod(ACE.Server.WorldObjects.Creature.GetResistanceType(d)):0.##}"))));
        var weapon = monster.GetEquippedWeapon();
        text.AppendLine(Line($"  this spawn wields: {(weapon == null ? "nothing" : weapon.Name)} (wielded treasure is rolled for each spawn)"));
        var parts = monster.Biota.PropertiesBodyPart?.Where(p => p.Value.DVal != 0).Select(p => Line($"{p.Key} {p.Value.DType} up to {p.Value.DVal} (variance {p.Value.DVar:0.##})")) ?? [];
        text.AppendLine("  body parts that attack: " + string.Join("; ", parts));
        var book = monster.Biota.PropertiesSpellBook;
        if (book is { Count: > 0 })
        {
            text.AppendLine(Line($"  spell book (the chance each is tried when it picks an attack, in order; Monster_Magic.cs TryRollSpell), magic delay {monster.GetProperty(PropertyFloat.AiUseMagicDelay) ?? 3.0:0.##} s:"));
            foreach (var entry in book)
            {
                var spell = new Spell((uint)entry.Key);
                var chance = entry.Value > 2.0f ? entry.Value - 2.0f : entry.Value / 100.0f;
                text.AppendLine(Line($"    {chance,6:P1}  {(spell.NotFound ? "spell " + entry.Key : spell.Name)}"));
            }
        }
        else
        {
            text.AppendLine("  no spell book: it never casts");
        }

        text.AppendLine(Line($"  pause after an attack: 0 to {monster.PowerupTime ?? 1.0:0.##} s (PowerupTime)"));
        return text.ToString();
    }

    /// <summary>Her against one creature.</summary>
    public static int Matchup(Options options)
    {
        var monster = World.BuildCreature(options.Subject ?? throw new ArgumentException("matchup needs a creature's name or weenie class id"));
        var her = World.RestoreHer(options.Her, options.Wield, options.Bars);
        Console.Write(MatchupReport(her, monster, options));
        return 0;
    }

    private static string MatchupReport(Player her, Creature monster, Options options)
    {
        var text = new StringBuilder();
        text.AppendLine(Line($"{her.Name.ToUpperInvariant()} (level {her.Level}) AGAINST {monster.Name.ToUpperInvariant()} (weenie {monster.WeenieClassId}, level {monster.Level})"));
        text.AppendLine("Every number is ACE's own code run offline on these two objects: nothing here is an estimate.");
        text.AppendLine();

        // Her spells on it.
        var health = (int)monster.Health.MaxValue;
        var regen = RegenPerTick(monster);
        text.AppendLine(Line($"HER SPELLS ON IT   its health {health}, Magic Defense {monster.GetEffectiveMagicDefense()}, regenerating {regen:0.##} every {HeartbeatOf(monster):0.###} s"));
        if (HeartbeatOf(monster) != DefaultHeartbeat)
        {
            // TODO(fight-sim-heartbeat): Kills, DeathChance and Blows tick a creature's vitals every 5 s whatever its own HeartbeatInterval (139 creature weenies of the world database state another, M6 item 16 E1e)
            text.AppendLine(Line($"  its heartbeat is {HeartbeatOf(monster):0.###} s, not ACE's default {DefaultHeartbeat:0} s: the kill times and the race below still tick its regeneration every {DefaultHeartbeat:0} s (TODO(fight-sim-heartbeat))"));
        }
        foreach (var school in new[] { MagicSchool.WarMagic, MagicSchool.LifeMagic, MagicSchool.CreatureEnchantment, MagicSchool.ItemEnchantment })
        {
            var skill = her.GetCreatureSkill(school).Current;
            WorldObject.MagicDefenseCheck(skill, monster.GetEffectiveMagicDefense(), out var resist);
            text.AppendLine(Line($"  her {school} {skill}: a resistable spell of that school lands {1 - resist:P1} of the time"));
        }

        text.AppendLine();
        text.AppendLine(Line($"  {"spell",-22} {"casts",6} {"lands",6} {"plain hit: least mean most",26} {"critical: share mean",21} {"a cast",7} {"casts a kill",13} {"s a kill",9} {"mana a kill",12} {"damage/s",9}"));
        var strikes = new List<(Strike Strike, KillStats Kill)>();
        var known = Strikes.BoltsOf(her);
        var tried = options.With is null ? [] : World.SpellsNamed(options.With).Where(s => s.NumProjectiles == 1 && known.All(k => k.Id != s.Id)).ToList();
        foreach (var spell in known.Concat(tried))
        {
            var strike = Strikes.Of(her, monster, spell, options.Casts);
            var kill = Kills(strike, health, regen, options.CastOverhead);
            strikes.Add((strike, kill));
        }

        foreach (var (strike, kill) in strikes.OrderBy(s => s.Kill.MeanSeconds))
        {
            var plain = strike.Plain.DefaultIfEmpty(0).ToList();
            var label = strike.Spell.Name + (tried.Any(t => t.Id == strike.Spell.Id) ? " *" : string.Empty);
            text.AppendLine(Line($"  {label,-22} {strike.CastChance,6:P0} {strike.LandChance,6:P1} {plain.Min(),10} {plain.Average(),7:0.0} {plain.Max(),7} {strike.CriticalShare,12:P1} {strike.Criticals.DefaultIfEmpty(0).Average(),8:0.0} {strike.MeanPerCast,7:0.0} {kill.MeanCasts,6:0.00} (90% <={kill.P90Casts,2}) {kill.MeanSeconds,8:0.0} {kill.MeanMana,11:0.0} {strike.DamagePerSecond,9:0.0}"));
        }

        if (tried.Count > 0)
        {
            text.AppendLine("  * not in her spell book: tried as if she knew it (--with)");
        }

        if (strikes.Count == 0)
        {
            text.AppendLine("  she knows no single-projectile war spell");
        }
        else
        {
            var cast = strikes.OrderBy(s => s.Kill.MeanSeconds).First().Strike.CastSeconds;
            text.AppendLine(Line($"  one cast of the fastest killer takes {cast:0.00} s on the server (windup, cast gesture, return to ready: fight-sim cast shows the pieces){(options.CastOverhead > 0 ? Line($"; plus {options.CastOverhead:0.00} s a cast of the client's loop, as given") : "; the client's fight loop adds its own overhead (--cast-overhead, from a live log)")}"));
        }

        // Its attacks on her.
        text.AppendLine();
        text.AppendLine(Line($"ITS ATTACKS ON HER   her health {her.Health.MaxValue}, Melee Defense {her.GetCreatureSkill(Skill.MeleeDefense).Current}, Missile Defense {her.GetCreatureSkill(Skill.MissileDefense).Current}, Magic Defense {her.GetEffectiveMagicDefense()}"));
        var blows = Blows.Sample(monster, her, options.Fights, options.FightSeconds, applyDebuffs: false);
        text.Append(BlowTable(blows));
        var debuffs = blows.Blows.Where(b => b.Kind == BlowKind.SpellDebuff).Select(b => b.What).Distinct(StringComparer.Ordinal).ToList();
        BlowReport? debuffed = null;
        if (debuffs.Count > 0)
        {
            debuffed = Blows.Sample(monster, her, options.Fights, options.FightSeconds, applyDebuffs: true);
            text.AppendLine(Line($"  with its debuffs staying on her for the rest of the fight as they land ({string.Join(", ", debuffs)}): {debuffed.DamagePerSecond:0.00} health a second, biggest single loss {debuffed.Blows.Select(b => b.Damage).DefaultIfEmpty(0).Max()}"));
        }

        if (options.Explain)
        {
            text.AppendLine("  ACE's working for its first physical attacks (DamageEvent's own fields):");
            foreach (var blow in blows.Blows.Where(b => b.Working.Length > 0).Take(6))
            {
                text.AppendLine("    " + blow.Working);
            }

            var physical = blows.Blows.Where(b => b.Kind is BlowKind.Melee or BlowKind.Missile).ToList();
            var struck = physical.Where(b => b.Landed).ToList();
            text.AppendLine(Line($"  of {physical.Count} physical attacks: {physical.Count - struck.Count} evaded or void ({1 - (struck.Count / (double)Math.Max(1, physical.Count)):P1}), {struck.Count(b => b.Critical)} critical ({struck.Count(b => b.Critical) / (double)Math.Max(1, struck.Count):P1} of those that struck)"));
            foreach (var part in struck.Where(b => !b.Critical).GroupBy(b => b.Part).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                text.AppendLine(Line($"    {part.Key,-12} {part.Count(),6} hits, mean {part.Average(b => b.Damage):0.00}: {string.Join(" ", part.GroupBy(b => b.Damage).OrderBy(g => g.Key).Select(g => Line($"{g.Key}x{g.Count()}")))}"));
            }

            text.AppendLine("    critical: " + string.Join(" ", struck.Where(b => b.Critical).GroupBy(b => b.Damage).OrderBy(g => g.Key).Select(g => Line($"{g.Key}x{g.Count()}"))));
        }

        foreach (var note in blows.Notes)
        {
            text.AppendLine("  NOTE " + note);
        }

        // The race.
        text.AppendLine();
        if (strikes.Count > tried.Count)
        {
            var hers = strikes.Where(s => tried.All(t => t.Id != s.Strike.Spell.Id)).ToList();
            var (best, kill) = hers.OrderBy(s => s.Kill.MeanSeconds).First();
            var cheapest = hers.OrderBy(s => s.Kill.MeanMana).First();
            var race = debuffed ?? blows;
            var opening = race.MeanDamageWithin(kill.MeanSeconds);
            text.AppendLine(Line($"THE RACE   fastest kill: {best.Spell.Name}, {kill.MeanSeconds:0.0} s; cheapest kill: {cheapest.Strike.Spell.Name}, {cheapest.Kill.MeanMana:0.0} mana"));
            text.AppendLine(Line($"  in the {kill.MeanSeconds:0.0} s a kill takes, a fresh one takes {opening:0.0} health from her on average ({opening / her.Health.MaxValue:P0} of her {her.Health.MaxValue}); over a whole {race.FightSeconds:0} s fight it averages {race.DamagePerSecond:0.00} a second"));
            text.AppendLine(Line($"  several on her at once from full health ({her.Health.MaxValue}), killed one after another, with no healing at all (2,000 simulated fights each):"));
            for (var pack = 1; pack <= 6; pack++)
            {
                var (dies, lost) = DeathChance(best, health, regen, options.CastOverhead, race, (int)her.Health.MaxValue, pack);
                text.AppendLine(Line($"    {pack} on her: {kill.MeanSeconds * pack,6:0.0} s to clear, {lost,7:0.0} health lost on average, she dies in {dies,6:P1} of fights{(dies > 0 ? "  <- she must heal, drain or leave" : string.Empty)}"));
            }

            // With her heals: the same creature and the same sampled attacks, her bolt the client's war pick, every cast chosen by the client's own rules and played through ACE (InFight.cs).
            var herFight = HerFight.Of(her, options.CastOverhead, World.TriedIds(options.With));
            var picked = hers.OrderBy(s => InFightRules.KillPrice(s.Kill.MeanSeconds, s.Kill.MeanCasts, s.Kill.MeanMana)).First();
            var foe = FoeFight.Of(herFight, monster, picked.Strike, race);
            var fights = options.HealedFights ?? DefaultHealedFights;
            var sizes = Enumerable.Range(1, 6).Concat(options.AtOnce > 6 ? [options.AtOnce] : []).ToList();
            var traced = new StringBuilder();
            var healed = sizes.Select(n => (Label: Line($"{n} on her"),
                Healed: InFight.Run(herFight, Enumerable.Repeat(foe, n).ToList(), FightMode.Heals, fights, 20261119 + n, options.Trace ? s => traced.AppendLine(s) : null),
                BoltsOnly: (PackVerdict?)InFight.Run(herFight, Enumerable.Repeat(foe, n).ToList(), FightMode.BoltsOnly, fights, 20261119 + n))).ToList();
            text.AppendLine();
            text.Append(HealedHeader(her, fights));
            text.Append(InFight.Table(healed));
            text.AppendLine(InFight.Casts(healed));
            if (options.Trace)
            {
                text.AppendLine("  the first fight of each pack, step by step (--trace):");
                text.Append(traced);
            }
            text.AppendLine(Line($"  her bolt here: {picked.Strike.Spell.Name}, the client's war pick (WarSpellPicker: the least seconds a kill with each cast's overhead and its mana at the dance's seconds a point): {InFightRules.KillPrice(picked.Kill.MeanSeconds, picked.Kill.MeanCasts, picked.Kill.MeanMana):0.0} s a kill all in{(picked.Strike.Spell.Id == best.Spell.Id ? string.Empty : Line($"; the race above uses the fastest killer, {best.Spell.Name}, {InFightRules.KillPrice(kill.MeanSeconds, kill.MeanCasts, kill.MeanMana):0.0} s all in"))}"));
            text.AppendLine("  her means: " + herFight.Describe());
            text.AppendLine("  'bolts only' is the same engine with no heal, no fill and no mana count: the race's model above played again (a fight past a sampled fight's 60 s goes on with the second half of another, where the race stops counting)");
            text.AppendLine("  " + InFight.Gaps);

            var modifier = her.GetXPAndLuminanceModifier(XpType.Kill) * PropertyManager.GetDouble("xp_modifier").Item;
            var xp = (monster.XpOverride ?? 0) * modifier;
            text.AppendLine();
            text.AppendLine(Line($"EXPERIENCE   {monster.XpOverride ?? 0:N0} a kill x {modifier:0.###} (her modifier and the server's) = {xp:N0}"));
            text.AppendLine(Line($"  {xp / kill.MeanSeconds:N0} a second of casting; {xp / kill.MeanSeconds * 3600 / 1e6:0.00} M an hour if she did nothing but cast at these (no walking, looting, healing or waiting on mana: a ceiling)"));
            text.AppendLine(Line($"  {xp / kill.MeanMana:N0} experience a point of mana with {cheapest.Strike.Spell.Name}"));

            // Her recovery, and the rate with it paid for.
            text.AppendLine();
            var (recovery, healRate, manaPerHealth, manaRate) = Economy.Report(her, monster);
            text.Append(recovery);
            text.AppendLine();
            text.AppendLine(Line($"ALL IN   {best.Spell.Name} for the kills, then her fastest heal for the health lost and the dance (with her own regeneration) for the mana spent, before the next pull; walking and looting not counted"));
            text.AppendLine(Line($"  {"at once",8} {"fight s",8} {"health lost",12} {"heal s",7} {"mana spent",11} {"mana s",7} {"M xp/h",7} {"she dies (no healing in the fight)",36}"));
            for (var pack = 1; pack <= 6; pack++)
            {
                var (dies, lost) = DeathChance(best, health, regen, options.CastOverhead, race, (int)her.Health.MaxValue, pack);
                lost = Math.Min(lost, her.Health.MaxValue);
                var fight = kill.MeanSeconds * pack;
                var healSeconds = healRate > 0 ? lost / healRate : double.PositiveInfinity;
                var mana = (kill.MeanMana * pack) + (lost * manaPerHealth);
                var manaSeconds = manaRate > 0 ? mana / manaRate : double.PositiveInfinity;
                var rate = xp * pack / (fight + healSeconds + manaSeconds) * 3600 / 1e6;
                // Past one death in ten fights the pull is not a way to earn: the row keeps its death chance and drops its rate.
                var earned = dies <= 0.1 ? Line($"{rate,7:0.00}") : "      -";
                text.AppendLine(Line($"  {pack,8} {fight,8:0.0} {lost,12:0.0} {healSeconds,7:0.0} {mana,11:0.0} {manaSeconds,7:0.0} {earned} {dies,36:P1}"));
            }

            // The same with the fight played by her own rules: its seconds, and the health and mana it leaves missing, paid back between pulls as above.
            text.AppendLine();
            text.AppendLine(Line($"ALL IN WITH HER HEALS   the fight as WITH HER HEALS plays it (her heals, drains and presses inside it); then the health and the mana it leaves missing paid back before the next pull, as above"));
            text.AppendLine(Line($"  {"at once",-10} {"fight s",8} {"health lost",12} {"heal s",7} {"mana spent",11} {"mana s",7} {"M xp/h",7} {"she dies",9} {"out of mana",12}"));
            foreach (var (label, v, _) in healed)
            {
                var pack = int.Parse(label.Split(' ')[0], CultureInfo.InvariantCulture);
                var lost = double.IsNaN(v.EndHealthWon) ? double.NaN : her.Health.MaxValue - v.EndHealthWon;
                var healSeconds = healRate > 0 ? lost / healRate : double.PositiveInfinity;
                var mana = Math.Max(0, v.ManaNetWon) + (lost * manaPerHealth);
                var manaSeconds = manaRate > 0 ? mana / manaRate : double.PositiveInfinity;
                var rate = xp * pack / (v.ClearSeconds + healSeconds + manaSeconds) * 3600 / 1e6;
                // As above: past one fight in ten that is not won (a death, or out of mana with creatures alive) the pull is not a way to earn.
                var earned = v.NotWon <= 0.1 && !double.IsNaN(rate) ? Line($"{rate,7:0.00}") : "      -";
                text.AppendLine(Line($"  {label,-10} {Num(v.ClearSeconds),8} {Num(lost),12} {Num(healSeconds),7} {Num(mana),11} {Num(manaSeconds),7} {earned} {v.Dies,9:P1} {v.OutOfMana,12:P1}"));
            }
        }

        return text.ToString();
    }

    /// <summary>The fights of each pack with her heals in matchup (2,000, as the race's).</summary>
    public const int DefaultHealedFights = 2000;

    /// <summary>The header of a table of fights with her heals.</summary>
    private static string HealedHeader(Player her, int fights)
    {
        var text = new StringBuilder();
        text.AppendLine(Line($"WITH HER HEALS   the fight with every cast of hers chosen by the client's own rules and played through ACE on these objects ({fights:N0} fights each, from her full health {her.Health.MaxValue}, stamina {her.Stamina.MaxValue} and mana {her.Mana.MaxValue}):"));
        text.AppendLine("  the emergency's heal line and pick (FightEmergency, HealPick.ChooseNow: Heal Self or Drain Health Other at the fullest creature), then the climber's fills (VitalsPolicy.Decide on the ladder's lines: presses, stamina drains,");
        text.AppendLine("  health drains), then her bolt at the target the kill order keeps (TargetSwitch); a drain takes its share of what its target holds now; mana, stamina and health spent and regained as ACE does; the creatures attack throughout");
        text.AppendLine("  'out of mana' ends a fight with creatures alive and no bolt or press her mana lets her try: a flee-or-die moment, not a win; 'lowest health' counts a death as 0; 'heals cast' and 'mana spent' are a fight's");
        return text.ToString();
    }

    /// <summary>Her against a mixed pack on her at once (<c>matchup --pack</c>): with her heals, and with bolts only.</summary>
    public static int MatchupPack(Options options)
    {
        var her = World.RestoreHer(options.Her, options.Wield, options.Bars);
        var herFight = HerFight.Of(her, options.CastOverhead, World.TriedIds(options.With));
        var bolts = Strikes.BoltsOf(her);
        if (bolts.Count == 0)
        {
            throw new ArgumentException($"{her.Name} knows no single-projectile war spell: no fight to play");
        }

        var text = new StringBuilder();
        var entries = ParsePack(options.Pack ?? string.Empty);
        var pack = new List<FoeFight>();
        text.AppendLine(Line($"{her.Name.ToUpperInvariant()} (level {her.Level}) AGAINST A PACK OF {entries.Sum(e => e.Count)} AT ONCE"));
        text.AppendLine("Every number is ACE's own code run offline on these objects: nothing here is an estimate.");
        text.AppendLine();
        text.AppendLine(Line($"  {"creature",-28} {"wcid",6} {"lvl",4} {"count",5} {"health",6} {"her bolt",-20} {"s a kill",8} {"its hp/s on her",15} {"her Life lands",14} {"drain takes",11}"));
        foreach (var (subject, count) in entries)
        {
            var monster = World.BuildCreature(subject);
            var health = (int)monster.Health.MaxValue;
            var regen = RegenPerTick(monster);
            var (strike, kill) = bolts.Select(b => Strikes.Of(her, monster, b, options.Casts)).Select(s => (Strike: s, Kill: Kills(s, health, regen, options.CastOverhead)))
                .OrderBy(s => InFightRules.KillPrice(s.Kill.MeanSeconds, s.Kill.MeanCasts, s.Kill.MeanMana)).First(); // the client's war pick (WarSpellPicker), as matchup's fights with her heals
            var blows = Blows.Sample(monster, her, options.Fights, options.FightSeconds, applyDebuffs: false);
            if (blows.Blows.Any(b => b.Kind == BlowKind.SpellDebuff))
            {
                blows = Blows.Sample(monster, her, options.Fights, options.FightSeconds, applyDebuffs: true); // the race's: its debuffs staying on her
            }

            var foe = FoeFight.Of(herFight, monster, strike, blows);
            pack.AddRange(Enumerable.Repeat(foe, count));
            text.AppendLine(Line($"  {Clip(monster.Name, 28),-28} {monster.WeenieClassId,6} {monster.Level ?? 0,4} {count,5} {health,6} {Clip(strike.Spell.Name, 20),-20} {kill.MeanSeconds,8:0.0} {blows.DamagePerSecond,15:0.00} {foe.Rules.DrainLand,14:P1} {foe.Rules.DrainResist,11:P0}"));
            foreach (var note in blows.Notes)
            {
                text.AppendLine("    NOTE " + note);
            }
        }

        // The kill order at full health: TargetSwitch folded over the pack, each kill taking the next.
        var view = new FightView { Foes = [.. pack.Select(p => new Foe(p.Rules))] };
        var order = new List<string>();
        for (var target = InFightRules.ChooseTarget(view); target >= 0; target = InFightRules.ChooseTarget(view))
        {
            order.Add(view.Foes[target].Kind.Name);
            view.Foes[target].Health = 0;
        }

        text.AppendLine("  kill order at full health (TargetSwitch: the most damage a second on her removed for each second of casting): " + string.Join(", ", order));
        var fights = options.HealedFights ?? DefaultHealedFights;
        var label = Line($"{pack.Count} at once");
        var traced = new StringBuilder();
        var rows = new List<(string Label, PackVerdict Healed, PackVerdict? BoltsOnly)>
        {
            (label, InFight.Run(herFight, pack, FightMode.Heals, fights, 20261119, options.Trace ? s => traced.AppendLine(s) : null), InFight.Run(herFight, pack, FightMode.BoltsOnly, fights, 20261119)),
            (Line($"{pack.Count} no fills"), InFight.Run(herFight, pack, FightMode.EmergencyOnly, fights, 20261119), null),
        };
        text.AppendLine();
        text.Append(HealedHeader(her, fights));
        text.Append(InFight.Table(rows));
        text.AppendLine(InFight.Casts(rows));
        if (options.Trace)
        {
            text.AppendLine("  the first fight, step by step (--trace):");
            text.Append(traced);
        }
        var won = rows[0].Healed;
        text.AppendLine(Line($"  her mana: {her.Mana.MaxValue} at the start; her casts cost {won.ManaSpentMean:0.0} a fight; a won fight ends {(double.IsNaN(won.ManaNetWon) ? "-" : won.ManaNetWon.ToString("0.0", CultureInfo.InvariantCulture))} under its start (her presses and regeneration counted)"));
        text.AppendLine(Line($"  'no fills' is the same fight with the vitals policy's fills left out (the emergency's heals and the bolt; a press only when the bolt cannot be tried): not a rule of the client, a measure of what its fills add or cost"));
        text.AppendLine("  her means: " + herFight.Describe());
        text.AppendLine("  " + InFight.Gaps);
        Console.Write(text);
        return 0;
    }

    /// <summary>A mixed pack: "A x N" entries separated by commas, A a weenie class id or an exact creature name, N how many (1 when absent).</summary>
    /// <param name="pack">The entries.</param>
    public static IReadOnlyList<(string Subject, int Count)> ParsePack(string pack)
    {
        var entries = new List<(string, int)>();
        foreach (var entry in pack.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cut = entry.LastIndexOf('x');
            if (cut > 0 && int.TryParse(entry[(cut + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            {
                entries.Add((entry[..cut].Trim(), count));
            }
            else
            {
                entries.Add((entry, 1));
            }
        }

        return entries.Count == 0 ? throw new ArgumentException("--pack names no creature (\"WCID x N,WCID x N\")") : entries;
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "~";

    /// <summary>A figure to one decimal, or "-" where no fight gave one (none won).</summary>
    private static string Num(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string BlowTable(BlowReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(Line($"  {"attack",-34} {"a minute",9} {"lands",6} {"hit: least mean most",22} {"health/s",9}"));
        foreach (var group in report.Blows.GroupBy(b => (b.Kind, b.What)).OrderByDescending(g => g.Sum(b => b.Damage)))
        {
            var landed = group.Where(b => b.Landed).ToList();
            var hits = landed.Where(b => b.Damage > 0).Select(b => b.Damage).DefaultIfEmpty(0).ToList();
            text.AppendLine(Line($"  {Label(group.Key.Kind) + " " + group.Key.What,-34} {group.Count() * 60.0 / report.Seconds,9:0.0} {landed.Count / (double)group.Count(),6:P0} {hits.Min(),6} {hits.Average(),7:0.0} {hits.Max(),7} {group.Sum(b => (double)b.Damage) / report.Seconds,9:0.00}"));
        }

        text.AppendLine(Line($"  all together: {report.DamagePerSecond:0.00} health a second from one of them over a {report.FightSeconds:0} s fight ({report.Blows.Count} attacks in {report.Fights} fights, each begun with the creature fresh)"));
        return text.ToString();
    }

    private static string Label(BlowKind kind) => kind switch
    {
        BlowKind.Melee => "melee",
        BlowKind.Missile => "missile",
        BlowKind.SpellProjectile => "spell",
        BlowKind.SpellDirect => "spell (direct)",
        BlowKind.SpellDebuff => "debuff",
        _ => "spell (not on her)",
    };

    /// <summary>
    /// Kills of a creature of <paramref name="health"/> by one spell: each attempt takes the cast's seconds, fizzles by her cast chance (5 mana), else deals a sampled cast's damage (0 where resisted); the creature regains
    /// <paramref name="regenPerTick"/> every 5 s (Creature_Vitals.cs VitalHeartBeat, measured on the object), from a random phase.
    /// </summary>
    public static KillStats Kills(Strike strike, int health, double regenPerTick, double castOverhead, int kills = 4000)
    {
        if (strike.Damage.All(d => d == 0) || strike.CastChance <= 0)
        {
            return new KillStats(double.PositiveInfinity, int.MaxValue, double.PositiveInfinity, double.PositiveInfinity);
        }

        var random = new Random(20261115);
        var seconds = strike.CastSeconds + castOverhead;
        var casts = new int[kills];
        double mana = 0;
        for (var k = 0; k < kills; k++)
        {
            double left = health, time = 0, nextTick = random.NextDouble() * 5;
            while (left > 0 && casts[k] < 10000)
            {
                time += seconds;
                casts[k]++;
                while (nextTick <= time)
                {
                    left = Math.Min(health, left + regenPerTick);
                    nextTick += 5;
                }

                if (random.NextDouble() < strike.CastChance)
                {
                    mana += strike.ManaPerCast;
                    left -= strike.Damage[random.Next(strike.Damage.Length)];
                }
                else
                {
                    mana += 5;
                }
            }
        }

        Array.Sort(casts);
        return new KillStats(casts.Average(), casts[(int)(kills * 0.9)], casts.Average() * seconds, mana / kills);
    }

    /// <summary>
    /// Her chance of dying, and her mean health loss, with <paramref name="pack"/> of the creature on her at once, from full health, with no healing at all, killing them one after another with
    /// <paramref name="strike"/>: each creature's attacks are those of one of its simulated fights, drawn at random, from that fight's first second until the creature falls; each kill's length is drawn from the sampled
    /// casts; she dies when the damage taken before the last one falls reaches <paramref name="herHealth"/>. A plain measure of how hard the creature hits against how fast she kills it: her heals, drains and retreat are
    /// not in it, and a creature that has to walk to her is taken as already on her.
    /// </summary>
    public static (double Dies, double MeanLoss) DeathChance(Strike strike, int monsterHealth, double regenPerTick, double castOverhead, BlowReport blows, int herHealth, int pack, int fights = 2000)
    {
        if (strike.Damage.All(d => d == 0) || strike.CastChance <= 0)
        {
            return (1.0, double.PositiveInfinity);
        }

        var random = new Random(20261115 + pack);
        var seconds = strike.CastSeconds + castOverhead;
        var deaths = 0;
        double loss = 0;
        for (var fight = 0; fight < fights; fight++)
        {
            double clock = 0, taken = 0;
            for (var m = 0; m < pack; m++)
            {
                // This one's kill, as Kills draws it.
                double left = monsterHealth, nextTick = random.NextDouble() * 5, length = 0;
                for (var casts = 0; left > 0 && casts < 10000; casts++)
                {
                    length += seconds;
                    while (nextTick <= length)
                    {
                        left = Math.Min(monsterHealth, left + regenPerTick);
                        nextTick += 5;
                    }

                    if (random.NextDouble() < strike.CastChance)
                    {
                        left -= strike.Damage[random.Next(strike.Damage.Length)];
                    }
                }

                clock += length;

                // It attacked her from the fight's start until it fell (a fight record is no longer than its length: past that, the record's whole damage).
                taken += blows.DamageWithin(random.Next(blows.Fights), clock);
            }

            loss += taken;
            if (taken >= herHealth)
            {
                deaths++;
            }
        }

        return (deaths / (double)fights, loss / fights);
    }

    /// <summary>ACE's heartbeat for an object whose weenie states no HeartbeatInterval, seconds (WorldObject_Tick.cs:47-48).</summary>
    public const double DefaultHeartbeat = 5.0;

    /// <summary>
    /// The seconds between two of a creature's heartbeats, as the server set them when it built the object (WorldObject.InitializeHeartbeats, WorldObject_Tick.cs:44-48: its weenie's HeartbeatInterval, 5 when it states
    /// none). The bestiary's heartbeat_interval column is this number (M6 item 16 E1e).
    /// </summary>
    public static double HeartbeatOf(Creature monster) => monster.HeartbeatInterval ?? DefaultHeartbeat;

    /// <summary>A creature's health regeneration in words: what one heartbeat gives and how far apart its heartbeats are; a creature whose interval is 0 or less has no heartbeat (WorldObject_Tick.cs:56-68).</summary>
    public static string RegenText(Creature monster) => HeartbeatOf(monster) > 0
        ? Line($"health regeneration {RegenPerTick(monster):0.##} every {HeartbeatOf(monster):0.###} s")
        : Line($"no health regeneration: its heartbeat interval is {HeartbeatOf(monster):0.###}, so ACE gives it no heartbeat (one vital tick would give {RegenPerTick(monster):0.##})");

    /// <summary>The health a creature regains each heartbeat (<see cref="HeartbeatOf"/>: 5 s for most), measured by ACE's own VitalHeartBeat on the object from low health.</summary>
    public static double RegenPerTick(Creature monster)
    {
        var max = monster.Health.MaxValue;
        const int Ticks = 20;
        var ticks = 0;
        if (monster.Health.RegenRate < 0)
        {
            // M6 item 16 E1e: a creature whose HealthRate is under 0 loses health at each heartbeat (four weenies of the world database: 36766, 36963, 45705, 72636). Measured from full health and stopped before
            // the tick that would kill it: ACE's VitalHeartBeat ends such a creature (Creature_Vitals.cs:155-161), which an object outside a landblock cannot do.
            var loss = Math.Ceiling(-monster.Health.RegenRate) + 1;
            while (ticks < Ticks && monster.Health.Current > loss)
            {
                monster.VitalHeartBeat(monster.Health);
                ticks++;
            }

            var lost = ((double)monster.Health.Current - max) / Math.Max(1, ticks);
            monster.UpdateVital(monster.Health, max);
            return ticks == 0 ? monster.Health.RegenRate : lost;
        }

        monster.UpdateVital(monster.Health, 1);
        while (ticks < Ticks && monster.Health.Current < max)
        {
            monster.VitalHeartBeat(monster.Health);
            ticks++;
        }

        var gained = (double)(monster.Health.Current - 1) / Math.Max(1, ticks);
        monster.UpdateVital(monster.Health, max);
        return gained;
    }

    /// <summary>Every creature the world places, ranked for her (Rank.cs).</summary>
    public static int Rank(Options options) => Ranking.Run(options);

    private static string Line(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
