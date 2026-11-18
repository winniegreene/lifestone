using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.WorldObjects.Entity;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>One of her recovery spells cast through ACE on the real objects.</summary>
/// <param name="Spell">The spell.</param>
/// <param name="Land">The chance it is not resisted (1 for a spell on herself).</param>
/// <param name="CastChance">Her chance the cast does not fizzle.</param>
/// <param name="CastSeconds">The server's seconds for one cast.</param>
/// <param name="Mana">The mean mana a cast costs her.</param>
/// <param name="HerHealth">Her mean health change a landed cast.</param>
/// <param name="HerStamina">Her mean stamina change.</param>
/// <param name="HerMana">Her mean mana change from the spell's effect (the cast's cost is <paramref name="Mana"/>).</param>
/// <param name="ItsHealth">The creature's mean health change.</param>
internal sealed record Recovery(Spell Spell, double Land, double CastChance, double CastSeconds, double Mana, double HerHealth, double HerStamina, double HerMana, double ItsHealth)
{
    /// <summary>Her health gained a second of casting it, resists and fizzles counted.</summary>
    public double HealthPerSecond => HerHealth * Land * CastChance / CastSeconds;
}

/// <summary>The stamina-to-mana dance at its best threshold.</summary>
/// <param name="StaminaToMana">The transfer spell.</param>
/// <param name="Revitalize">The stamina spell.</param>
/// <param name="Threshold">Revitalize is cast when her stamina is below this share of its maximum.</param>
/// <param name="ManaPerSecond">Her net mana a second of dancing (the transfers' gain less every cast's cost; her own regeneration not counted).</param>
internal sealed record Dance(Spell StaminaToMana, Spell Revitalize, double Threshold, double ManaPerSecond);

/// <summary>What her recovery spells give, by ACE's own cast on the real objects (WorldObject_Magic.cs HandleCastSpell_Boost and HandleCastSpell_Transfer through TryCastSpell).</summary>
internal static class Economy
{
    /// <summary>
    /// Her Life spells that give her a vital: a boost on herself (Heal Self, Revitalize Self) or a transfer whose destination is the caster (a drain, Stamina to Mana Self). Of each name family, the highest level she
    /// knows and, where she knows more, the lowest (the cheap one).
    /// </summary>
    public static IReadOnlyList<Spell> RecoverySpells(Player her)
    {
        var families = new Dictionary<string, List<Spell>>(StringComparer.Ordinal);
        foreach (var id in her.Biota.PropertiesSpellBook?.Keys ?? [])
        {
            var spell = new Spell((uint)id);
            if (spell.NotFound || spell.School != MagicSchool.LifeMagic)
            {
                continue;
            }

            var gives = spell.MetaSpellType == SpellType.Boost
                ? spell.IsSelfTargeted && spell.IsBeneficial
                : spell.MetaSpellType == SpellType.Transfer && spell.TransferFlags.HasFlag(TransferFlags.CasterDestination);
            if (gives)
            {
                var family = Strikes.Family(spell.Name);
                (families.TryGetValue(family, out var list) ? list : families[family] = []).Add(spell);
            }
        }

        var chosen = new List<Spell>();
        foreach (var family in families.Values)
        {
            var ordered = family.OrderByDescending(s => s.Power).ToList();
            chosen.Add(ordered[0]);
            if (ordered.Count > 1)
            {
                chosen.Add(ordered[^1]);
            }
        }

        return [.. chosen.OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Casts <paramref name="spell"/> <paramref name="casts"/> times with the resist roll skipped (it is reported apart): on herself when the spell is self-targeted, else on <paramref name="monster"/>. Before each cast
    /// the creature is fresh, her source vitals are full and the vital the spell fills is empty, so the figure is the most one cast gives; a transfer takes a share of what its source holds now (HandleCastSpell_Transfer:
    /// current times the spell's proportion, capped), so it gives less from a drained source.
    /// </summary>
    public static Recovery Sample(Player her, Creature monster, Spell spell, int casts = 200)
    {
        var (health, stamina, mana) = (her.Health.Current, her.Stamina.Current, her.Mana.Current);
        var self = spell.IsSelfTargeted;
        double herHealth = 0, herStamina = 0, herMana = 0, itsHealth = 0, cost = 0;
        for (var i = 0; i < casts; i++)
        {
            monster.SetMaxVitals();
            her.UpdateVital(her.Health, her.Health.MaxValue);
            her.UpdateVital(her.Stamina, her.Stamina.MaxValue);
            her.UpdateVital(her.Mana, her.Mana.MaxValue);
            var fills = spell.MetaSpellType == SpellType.Boost ? VitalOf(spell.VitalDamageType) : spell.Destination;
            switch (fills)
            {
                case ACE.Entity.Enum.Properties.PropertyAttribute2nd.Mana:
                    her.UpdateVital(her.Mana, 1);
                    break;
                case ACE.Entity.Enum.Properties.PropertyAttribute2nd.Stamina:
                    her.UpdateVital(her.Stamina, 1);
                    break;
                default:
                    her.UpdateVital(her.Health, 1);
                    break;
            }

            var before = (H: (int)her.Health.Current, S: (int)her.Stamina.Current, M: (int)her.Mana.Current, I: (int)monster.Health.Current);
            her.TryCastSpell(spell, self ? her : monster, tryResist: false);
            herHealth += (int)her.Health.Current - before.H;
            herStamina += (int)her.Stamina.Current - before.S;
            herMana += (int)her.Mana.Current - before.M;
            itsHealth += (int)monster.Health.Current - before.I;
            cost += her.CalculateManaUsage(her, spell, self ? her : monster);
        }

        monster.SetMaxVitals();
        her.UpdateVital(her.Health, health);
        her.UpdateVital(her.Stamina, stamina);
        her.UpdateVital(her.Mana, mana);
        return new Recovery(spell, self ? 1.0 : Strikes.LandChance(her, monster, spell), Strikes.CastChance(her, spell), Strikes.CastSeconds(her, spell), cost / casts, herHealth / casts, herStamina / casts,
            herMana / casts, itsHealth / casts);
    }

    private static ACE.Entity.Enum.Properties.PropertyAttribute2nd VitalOf(DamageType type) => type switch
    {
        DamageType.Mana => ACE.Entity.Enum.Properties.PropertyAttribute2nd.Mana,
        DamageType.Stamina => ACE.Entity.Enum.Properties.PropertyAttribute2nd.Stamina,
        _ => ACE.Entity.Enum.Properties.PropertyAttribute2nd.Health,
    };

    /// <summary>
    /// The dance played on the real object for each threshold from a tenth to nine tenths of her stamina: Revitalize when her stamina is under the threshold, else Stamina to Mana; each cast takes its server seconds and
    /// costs its mana (Mana Conversion rolled by ACE); her stamina regenerates on ACE's heartbeat. The best threshold by net mana a second is returned; null when she lacks either spell.
    /// </summary>
    public static Dance? BestDance(Player her)
    {
        var spells = RecoverySpells(her);
        var toMana = spells.Where(s => s.MetaSpellType == SpellType.Transfer && s.IsSelfTargeted && s.Source == ACE.Entity.Enum.Properties.PropertyAttribute2nd.Stamina
            && s.Destination == ACE.Entity.Enum.Properties.PropertyAttribute2nd.Mana).OrderByDescending(s => s.Power).FirstOrDefault();
        var revitalize = spells.Where(s => s.MetaSpellType == SpellType.Boost && s.VitalDamageType == DamageType.Stamina).OrderByDescending(s => s.Power).FirstOrDefault();
        if (toMana is null || revitalize is null)
        {
            return null;
        }

        var (health, stamina, mana) = (her.Health.Current, her.Stamina.Current, her.Mana.Current);
        var (toManaSeconds, revitalizeSeconds) = (Strikes.CastSeconds(her, toMana), Strikes.CastSeconds(her, revitalize));
        Dance? best = null;
        for (var tenth = 1; tenth <= 9; tenth++)
        {
            var threshold = tenth / 10.0;
            her.UpdateVital(her.Stamina, her.Stamina.MaxValue);
            double net = 0, time = 0, nextHeartbeat = 5;
            for (var cast = 0; cast < 400; cast++)
            {
                her.UpdateVital(her.Mana, 1); // the ledger holds her mana: the pool is emptied so that no cast is capped by a full pool
                var spell = her.Stamina.Current < threshold * her.Stamina.MaxValue ? revitalize : toMana;
                var before = (int)her.Mana.Current;
                her.TryCastSpell(spell, her, tryResist: false);
                net += (int)her.Mana.Current - before;
                net -= her.CalculateManaUsage(her, spell, her);
                time += spell == revitalize ? revitalizeSeconds : toManaSeconds;
                while (nextHeartbeat <= time)
                {
                    her.VitalHeartBeat(her.Stamina);
                    nextHeartbeat += 5;
                }
            }

            if (best is null || net / time > best.ManaPerSecond)
            {
                best = new Dance(toMana, revitalize, threshold, net / time);
            }
        }

        her.UpdateVital(her.Health, health);
        her.UpdateVital(her.Stamina, stamina);
        her.UpdateVital(her.Mana, mana);
        return best;
    }

    /// <summary>What a vital of hers regains on one of ACE's 5 s heartbeats, measured on the object from empty (Creature_Vitals.cs VitalHeartBeat: her attributes, her stance and her enchantments).</summary>
    public static double RegenPerTick(Player her, CreatureVital vital)
    {
        var saved = vital.Current;
        her.UpdateVital(vital, 1);
        const int Ticks = 12;
        for (var i = 0; i < Ticks; i++)
        {
            her.VitalHeartBeat(vital);
        }

        var gained = (double)(vital.Current - 1) / Ticks;
        her.UpdateVital(vital, saved);
        return gained;
    }

    /// <summary>Her recovery against <paramref name="monster"/>, as lines, and the figures the all-in rate needs.</summary>
    public static (string Text, double HealthPerSecond, double ManaPerHealth, double ManaPerSecond) Report(Player her, Creature monster)
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(string.Create(culture, $"HER RECOVERY   each spell cast through ACE on her and on a fresh {monster.Name}; the figure is the most one cast gives (the vital it fills empty, its source full)"));
        text.AppendLine(string.Create(culture, $"  {"spell",-26} {"casts",6} {"lands",6} {"her health",11} {"her stamina",12} {"her mana",9} {"its health",11} {"mana cost",10} {"cast s",7} {"health/s",9}"));
        var rows = RecoverySpells(her).Select(s => Sample(her, monster, s)).ToList();
        foreach (var r in rows)
        {
            text.AppendLine(string.Create(culture, $"  {r.Spell.Name,-26} {r.CastChance,6:P0} {r.Land,6:P0} {r.HerHealth,11:+0.0;-0.0;0} {r.HerStamina,12:+0.0;-0.0;0} {r.HerMana,9:+0.0;-0.0;0} {r.ItsHealth,11:+0.0;-0.0;0} {r.Mana,10:0.0} {r.CastSeconds,7:0.00} {(r.HerHealth > 0 ? r.HealthPerSecond.ToString("0.0", culture) : string.Empty),9}"));
        }

        var regen = (Health: RegenPerTick(her, her.Health) / 5, Stamina: RegenPerTick(her, her.Stamina) / 5, Mana: RegenPerTick(her, her.Mana) / 5);
        text.AppendLine(string.Create(culture, $"  her own regeneration in magic stance: {regen.Health:0.00} health, {regen.Stamina:0.00} stamina and {regen.Mana:0.00} mana a second (ACE's heartbeat on her, from empty)"));
        var dance = BestDance(her);
        if (dance is null)
        {
            text.AppendLine("  the stamina-to-mana dance: she lacks Stamina to Mana Self or Revitalize Self");
        }
        else
        {
            text.AppendLine(string.Create(culture, $"  the dance ({dance.StaminaToMana.Name} with {dance.Revitalize.Name}): best when she revitalizes below {dance.Threshold:P0} stamina: {dance.ManaPerSecond:0.00} mana a second net of every cast's cost"));
        }

        var heal = rows.Where(r => r.HerHealth > 0).OrderByDescending(r => r.HealthPerSecond).FirstOrDefault();
        if (heal is not null)
        {
            text.AppendLine(string.Create(culture, $"  her fastest heal here: {heal.Spell.Name}, {heal.HealthPerSecond:0.0} health a second at {heal.Mana / Math.Max(1, heal.HerHealth * heal.Land):0.00} mana a point of health"));
        }

        return (text.ToString(), heal?.HealthPerSecond ?? 0, heal is null ? 0 : heal.Mana / Math.Max(1, heal.HerHealth * heal.Land), (dance?.ManaPerSecond ?? 0) + regen.Mana);
    }
}
