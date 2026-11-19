// Follows ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Magic.cs (CalculateManaUsage: a fizzle costs 5 and a cast whose rolled cost is over the mana held is refused, 566-588; the effect at the cast's end),
// Player_Combat.cs (the stamina an attack thrown at her costs in a combat mode: an evade 302-334 with Endurance's chance to spare it when the defence is trained, a hit 512-518) and Creature_Vitals.cs (VitalHeartBeat) (AGPL-3.0):
// her fight's sequence re-expressed around calls into ACE's own functions on the real objects. No ACE source is copied (docs/third-party/fight-sim.md).
//
// Her in-fight policy (docs/fight-sim.md, "With her heals"): one to N creatures on her at once, every cast chosen by the client's own rules (InFightRules.cs) and played through ACE: her bolt's damage drawn from ACE's sampled
// strikes (as Commands.Kills draws it), each heal, drain and press cast by ACE's TryCastSpell on her and the creature with their vitals as the fight has left them (a drain takes its share of what the creature holds NOW), each
// cast's mana rolled by ACE's CalculateManaUsage, her regeneration ACE's VitalHeartBeat on her every 5 s, the creatures' attacks the race's sampled fights (Blows.Sample) landing at their own times until the creature falls.

using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.FightSim;

/// <summary>Her side of the fight: the player object and the spells the rules may choose, each with its ACE spell.</summary>
internal sealed class HerFight
{
    private readonly Dictionary<uint, Spell> _ace = [];

    private HerFight(Player her) => Her = her;

    /// <summary>The player.</summary>
    public Player Her { get; }

    /// <summary>The Heal Self level the fight casts (the highest known whose cast chance reaches <see cref="BuffOptions.DefaultCastChance"/>, VitalsMeans.Pick), or null.</summary>
    public FightSpell? HealSelf { get; private set; }

    /// <summary>The Drain Health Other levels she knows, lowest first.</summary>
    public IReadOnlyList<FightSpell> HealthDrains { get; private set; } = [];

    /// <summary>The Stamina to Mana Self levels she knows, lowest first.</summary>
    public IReadOnlyList<FightSpell> Presses { get; private set; } = [];

    /// <summary>The Drain Stamina Other level the fight casts (the highest known whose cast chance reaches <see cref="BuffOptions.DefaultCastChance"/>), or null.</summary>
    public FightSpell? StaminaDrain { get; private set; }

    /// <summary>Her regeneration a second in magic stance (ACE's heartbeat on her, from empty: <see cref="Economy.RegenPerTick"/>).</summary>
    public (double Health, double Stamina, double Mana) Regen { get; private set; }

    /// <summary>Her Mana Conversion as the rules read it (0: untrained).</summary>
    public uint ManaConversion { get; private set; }

    /// <summary>The chance an evaded attack costs her no stamina (Player_Combat.cs:302-334: her base Endurance's curve, only with the defence trained), melee and missile.</summary>
    public (double Melee, double Missile) SparedOnEvade { get; private set; }

    /// <summary>The seconds the client's loop adds to each cast (<c>--cast-overhead</c>).</summary>
    public double Overhead { get; private set; }

    /// <summary>The spells the fight treats as known though her book lacks them (<c>--with</c>), marked <c>*</c> in the report.</summary>
    public IReadOnlySet<uint> Tried { get; private set; } = new HashSet<uint>();

    /// <summary>The ACE spell of <paramref name="spell"/>.</summary>
    /// <param name="spell">The rules' spell.</param>
    public Spell Ace(FightSpell spell) => _ace[spell.Id];

    /// <summary>
    /// The rules' figures of one of her spells: ACE's cast seconds (Strikes.CastSeconds) and cast chance; its level and transfer row the client's own when given (<paramref name="level"/>, <paramref name="row"/>), else the spell
    /// table's.
    /// </summary>
    public FightSpell Rule(Spell spell, int level = 0, TransferRow? row = null)
    {
        _ace[spell.Id] = spell;
        var transfer = row ?? (spell.MetaSpellType == SpellType.Transfer
            ? new TransferRow(spell.Id, (uint)spell.Source, (uint)spell.Destination, spell.Proportion, spell.LossPercent, spell.TransferCap)
            : default);
        return new FightSpell(spell.Id, spell.Name, level > 0 ? level : (int)spell.Level, spell.BaseMana, spell.Power, Strikes.CastSeconds(Her, spell) + Overhead, Strikes.CastChance(Her, spell), transfer);
    }

    /// <summary>
    /// Her spells and figures, read once from the player. The levels the fight offers are the client's own lists, each level's number its place in the list: Drain Health Other I to VII (<see cref="DrainPicker.Ids"/>, its rows;
    /// the incantation has no row and is not offered, <c>TODO(m5-drain-viii-row)</c>), Stamina to Mana Self I to VIII (<see cref="BuffStatMods.StaminaToMana"/>), Heal Self I to VII (<see cref="VitalsMeans.HealSelf"/>, the highest
    /// cast at <see cref="BuffOptions.DefaultCastChance"/>, as <c>FightDrain.Fill</c> reads it); Drain Stamina Other by the spell table (a target source and a caster destination, stamina to stamina), the highest cast at that chance.
    /// </summary>
    /// <param name="her">The player.</param>
    /// <param name="castOverhead">The client's loop overhead a cast.</param>
    /// <param name="tried">
    /// Spells to treat as known though her book lacks them (<c>--with</c>, "what would a scroll buy"): a Stamina to Mana Self, Drain Health Other, Heal Self or Drain Stamina Other level joins the levels the rules may
    /// choose, cast through ACE with her own skill exactly as a learned one (the book is not changed). Other spells in the list change nothing here (the bolt is the race's).
    /// </param>
    public static HerFight Of(Player her, double castOverhead, IReadOnlyCollection<uint>? tried = null)
    {
        var fight = new HerFight(her) { Overhead = castOverhead };
        var known = (her.Biota.PropertiesSpellBook?.Keys ?? []).Select(id => (uint)id).ToHashSet();
        fight.Tried = (tried ?? []).Where(id => !known.Contains(id)).ToHashSet();
        known.UnionWith(fight.Tried);
        IEnumerable<(Spell Spell, int Level)> Listed(IEnumerable<uint> ids) => ids.Select((id, i) => (Id: id, Level: i + 1)).Where(x => known.Contains(x.Id)).Select(x => (new Spell(x.Id), x.Level)).Where(x => !x.Item1.NotFound);
        fight.HealthDrains = [.. Listed(DrainPicker.Ids).Select(x => fight.Rule(x.Spell, x.Level, DrainPicker.RowOf(x.Spell.Id)))];
        fight.Presses = [.. Listed(BuffStatMods.StaminaToMana.Select(r => r.Id)).Select(x => fight.Rule(x.Spell, x.Level, BuffStatMods.StaminaToMana[x.Level - 1]))];
        fight.HealSelf = Listed(VitalsMeans.HealSelf).Select(x => fight.Rule(x.Spell, x.Level)).LastOrDefault(s => s.CastChance >= BuffOptions.DefaultCastChance);
        var book = known.Select(id => new Spell(id)).Where(s => !s.NotFound && s.School == MagicSchool.LifeMagic).OrderBy(s => s.Power).ToList();
        fight.StaminaDrain = book.Where(s => s.MetaSpellType == SpellType.Transfer && !s.IsSelfTargeted && s.Source == PropertyAttribute2nd.Stamina && s.Destination == PropertyAttribute2nd.Stamina
            && s.TransferFlags.HasFlag(TransferFlags.TargetSource) && s.TransferFlags.HasFlag(TransferFlags.CasterDestination)).Select(s => fight.Rule(s)).LastOrDefault(s => s.CastChance >= BuffOptions.DefaultCastChance);
        fight.Regen = (Economy.RegenPerTick(her, her.Health) / 5, Economy.RegenPerTick(her, her.Stamina) / 5, Economy.RegenPerTick(her, her.Mana) / 5);
        var conversion = her.GetCreatureSkill(Skill.ManaConversion);
        fight.ManaConversion = conversion.AdvancementClass >= SkillAdvancementClass.Trained ? conversion.Current : 0;
        fight.SparedOnEvade = (Spared(her, Skill.MeleeDefense), Spared(her, Skill.MissileDefense));
        return fight;
    }

    /// <summary>Player_Combat.cs:302-334: with the defence trained, an evade costs no stamina at (E² x 0.000005 + E x 0.00124 - 0.07) on her base Endurance E, from 0 to 0.75; untrained, it always costs one.</summary>
    private static double Spared(Player her, Skill defence)
    {
        if (her.GetCreatureSkill(defence).AdvancementClass < SkillAdvancementClass.Trained)
        {
            return 0;
        }

        double e = her.Endurance.Base;
        return Math.Clamp((e * e * 0.000005) + (e * 0.00124) - 0.07, 0, 0.75);
    }

    /// <summary>Her spells in words, for the report.</summary>
    public string Describe()
    {
        var tried = Tried;
        string Of(FightSpell s) => string.Create(CultureInfo.InvariantCulture, $"{s.Name}{(tried.Contains(s.Id) ? " *" : string.Empty)} ({s.CastChance:P0}, {s.Seconds:0.00} s, base mana {s.BaseMana})");
        return string.Create(CultureInfo.InvariantCulture,
            $"Heal Self: {(HealSelf is null ? "none at a cast chance of " + BuffOptions.DefaultCastChance.ToString("P0", CultureInfo.InvariantCulture) : Of(HealSelf))}; Drain Health Other: {(HealthDrains.Count == 0 ? "none" : string.Join(", ", HealthDrains.Select(Of)))}; Stamina to Mana: {(Presses.Count == 0 ? "none" : string.Join(", ", Presses.Select(Of)))}; Drain Stamina Other: {(StaminaDrain is null ? "none" : Of(StaminaDrain))}; Mana Conversion {ManaConversion}; regeneration a second health {Regen.Health:0.00}, stamina {Regen.Stamina:0.00}, mana {Regen.Mana:0.00}");
    }
}

/// <summary>A kind of creature in the fight: the object, her bolt on it, its sampled attacks, its regeneration, and the rules' figures.</summary>
internal sealed class FoeFight
{
    /// <summary>The creature object (one a kind: its vitals are set to the fight's before each of her casts on it).</summary>
    public required Creature Monster { get; init; }

    /// <summary>The rules' figures.</summary>
    public required FoeKind Rules { get; init; }

    /// <summary>Her bolt on it, sampled through ACE.</summary>
    public required Strike Strike { get; init; }

    /// <summary>Its attacks on her, by sampled fight.</summary>
    public required BlowLog Log { get; init; }

    /// <summary>Its health regained each 5 s heartbeat (Commands.RegenPerTick).</summary>
    public required double HealthPerTick { get; init; }

    /// <summary>Its stamina regained each heartbeat (ACE's VitalHeartBeat on it, from empty).</summary>
    public required double StaminaPerTick { get; init; }

    /// <summary>The kind, from her bolt on it and its attacks: every figure ACE's.</summary>
    /// <param name="her">Her side.</param>
    /// <param name="monster">The creature.</param>
    /// <param name="strike">Her fastest-killing bolt on it.</param>
    /// <param name="blows">Its attacks on her (the race's: its debuffs staying on her when it has any).</param>
    public static FoeFight Of(HerFight her, Creature monster, Strike strike, BlowReport blows)
    {
        var plain = strike.Plain.DefaultIfEmpty(0).ToList();
        WorldObject.MagicDefenseCheck(her.Her.GetCreatureSkill(Skill.LifeMagic).Current, monster.GetEffectiveMagicDefense(), out var resist);
        var seconds = strike.CastSeconds + her.Overhead;
        var bolt = new FightSpell(strike.Spell.Id, strike.Spell.Name, (int)strike.Spell.Level, strike.Spell.BaseMana, strike.Spell.Power, seconds, strike.CastChance, default);
        var rules = new FoeKind(monster.Name, monster.Health.MaxValue, monster.Stamina.MaxValue, blows.DamagePerSecond, bolt, strike.MeanLanded, plain.Max() - plain.Min(), strike.CastChance * strike.LandChance,
            seconds > 0 ? strike.MeanPerCast * strike.CastChance / seconds : 0, 1 - resist, monster.GetResistanceMod(ResistanceType.HealthDrain));
        return new FoeFight { Monster = monster, Rules = rules, Strike = strike, Log = BlowLog.Of(blows), HealthPerTick = Commands.RegenPerTick(monster), StaminaPerTick = PerTick(monster, monster.Stamina) };
    }

    private static double PerTick(Creature monster, CreatureVital vital)
    {
        var saved = vital.Current;
        monster.UpdateVital(vital, 1);
        const int Ticks = 12;
        for (var i = 0; i < Ticks; i++)
        {
            monster.VitalHeartBeat(vital);
        }

        var gained = (double)(vital.Current - 1) / Ticks;
        monster.UpdateVital(vital, saved);
        return gained;
    }
}

/// <summary>A creature's sampled attacks, fight by fight, in time order (from <see cref="BlowReport"/>).</summary>
internal sealed class BlowLog
{
    private BlowLog(double length, double[][] times, int[][] damage, byte[][] kinds, int[] secondHalf)
    {
        (Length, Times, Damage, Kinds, SecondHalf) = (length, times, damage, kinds, secondHalf);
    }

    /// <summary>A blow that is a melee swing.</summary>
    public const byte Melee = 1;

    /// <summary>A blow that is a missile.</summary>
    public const byte Missile = 2;

    /// <summary>A blow that is a spell.</summary>
    public const byte SpellBlow = 3;

    /// <summary>Each sampled fight's length, seconds.</summary>
    public double Length { get; }

    /// <summary>The fights: each blow's time.</summary>
    public double[][] Times { get; }

    /// <summary>Each blow's damage to her.</summary>
    public int[][] Damage { get; }

    /// <summary>Each blow's kind (<see cref="Melee"/>, <see cref="Missile"/>, <see cref="SpellBlow"/>).</summary>
    public byte[][] Kinds { get; }

    /// <summary>Each fight's first blow at or after half its length (where a chained segment starts).</summary>
    public int[] SecondHalf { get; }

    /// <summary>The log of <paramref name="report"/>: every melee and missile attack (a hit or an evade: each costs her stamina) and every spell that took health.</summary>
    /// <param name="report">The sampled attacks.</param>
    public static BlowLog Of(BlowReport report)
    {
        var fights = Math.Max(1, report.Fights);
        var times = new double[fights][];
        var damage = new int[fights][];
        var kinds = new byte[fights][];
        var half = new int[fights];
        var byFight = report.Blows.Where(b => b.Kind is BlowKind.Melee or BlowKind.Missile || (b.Damage > 0 && b.Kind is BlowKind.SpellProjectile or BlowKind.SpellDirect)).GroupBy(b => b.Fight).ToDictionary(g => g.Key, g => g.OrderBy(b => b.Seconds).ToArray());
        for (var f = 0; f < fights; f++)
        {
            var blows = byFight.GetValueOrDefault(f, []);
            times[f] = [.. blows.Select(b => b.Seconds)];
            damage[f] = [.. blows.Select(b => b.Damage)];
            kinds[f] = [.. blows.Select(b => b.Kind switch { BlowKind.Melee => Melee, BlowKind.Missile => Missile, _ => SpellBlow })];
            var at = Array.BinarySearch(times[f], report.FightSeconds / 2);
            half[f] = at < 0 ? ~at : at;
            while (half[f] > 0 && times[f][half[f] - 1] >= report.FightSeconds / 2)
            {
                half[f]--;
            }
        }

        return new BlowLog(report.FightSeconds, times, damage, kinds, half);
    }
}

/// <summary>
/// One creature's attacks through a fight: a sampled fight drawn at random from its start; past that fight's end, the second half of another drawn fight, and so on (the creature's steady state: its opening, a caster's mana among
/// it, is not played again).
/// </summary>
internal sealed class BlowCursor
{
    private readonly BlowLog _log;
    private readonly Random _random;
    private int _fight;
    private int _index;
    private double _offset;
    private double _segmentEnd;

    /// <summary>A cursor at the fight's start.</summary>
    /// <param name="log">The creature's log.</param>
    /// <param name="random">The fight's random source.</param>
    public BlowCursor(BlowLog log, Random random)
    {
        (_log, _random) = (log, random);
        _fight = random.Next(log.Times.Length);
        _segmentEnd = log.Length;
    }

    /// <summary>The fight time of its next blow at or before <paramref name="until"/>, or +infinity.</summary>
    /// <param name="until">The latest time asked.</param>
    public double Next(double until)
    {
        while (true)
        {
            var times = _log.Times[_fight];
            if (_index < times.Length && times[_index] + _offset < _segmentEnd)
            {
                var at = times[_index] + _offset;
                return at <= until ? at : double.PositiveInfinity;
            }

            if (_segmentEnd > until || _log.Length <= 0)
            {
                return double.PositiveInfinity;
            }

            // The segment is spent: the second half of another sampled fight follows it.
            _fight = _random.Next(_log.Times.Length);
            _offset = _segmentEnd - (_log.Length / 2);
            _index = _log.SecondHalf[_fight];
            _segmentEnd += _log.Length / 2;
        }
    }

    /// <summary>The blow <see cref="Next"/> found, consumed.</summary>
    public (int Damage, byte Kind) Take()
    {
        var blow = (_log.Damage[_fight][_index], _log.Kinds[_fight][_index]);
        _index++;
        return blow;
    }
}

/// <summary>The fights.</summary>
internal static class InFight
{
    /// <summary>The longest fight played: past it a fight neither side has won is counted stalled (Lifestone's own bound, fifteen minutes).</summary>
    public const double LongestFight = 900;

    /// <summary>ACE's vital heartbeat for her and for a creature with no HeartbeatInterval (WorldObject_Tick.cs:47-48).</summary>
    private const double Heartbeat = Commands.DefaultHeartbeat;

    /// <summary>Refusals in a row (a rolled cost over the mana held: ACE refuses the cast, Player_Magic.cs:581-585) after which she is out of mana (Lifestone's own bound: at the try chance of 0.25 a row of 20 is 0.3%).</summary>
    private const int MaxRefusals = 20;

    /// <summary>What the fights do not play, said under every table of them.</summary>
    public const string Gaps =
        "not played: the finishing bolt over the emergency's heal (FightScenario.Emergency.cs FinishStep: one attacker, about to die; TODO(fight-sim-finishing-bolt)), the drain rule behind an emergency set aside after five casts "
        + "that did not land, health and mana potions, the way out's mana reserve and the leave, the buffs' upkeep, the components' level floor, Revitalize (the climber's policy never casts it), walking and arrival "
        + "(every creature attacks from the first second)";

    /// <summary>Plays <paramref name="fights"/> fights of <paramref name="pack"/> on her and sums them up. Her vitals and the creatures' are put back afterwards.</summary>
    /// <param name="her">Her side.</param>
    /// <param name="pack">The creatures on her at once (a kind may appear several times).</param>
    /// <param name="mode">How the fight is played.</param>
    /// <param name="fights">How many fights.</param>
    /// <param name="seed">The random seed.</param>
    /// <param name="trace">When given, every step of the first fight is written to it (<c>--trace</c>).</param>
    public static PackVerdict Run(HerFight her, IReadOnlyList<FoeFight> pack, FightMode mode, int fights, int seed, Action<string>? trace = null)
    {
        var player = her.Her;
        var saved = (player.Health.Current, player.Stamina.Current, player.Mana.Current);
        var monsters = pack.Select(p => p.Monster).Distinct().Select(m => (m, m.Health.Current, m.Stamina.Current, m.Mana.Current)).ToList();
        var random = new Random(seed);
        var results = new FightResult[fights];
        for (var f = 0; f < fights; f++)
        {
            results[f] = Fight(her, pack, mode, random, f == 0 ? trace : null);
        }

        player.UpdateVital(player.Health, saved.Item1);
        player.UpdateVital(player.Stamina, saved.Item2);
        player.UpdateVital(player.Mana, saved.Item3);
        foreach (var (m, health, stamina, mana) in monsters)
        {
            m.UpdateVital(m.Health, health);
            m.UpdateVital(m.Stamina, stamina);
            m.UpdateVital(m.Mana, mana);
        }

        return PackVerdict.Of(results);
    }

    /// <summary>One fight, from her full vitals and every creature fresh and attacking.</summary>
    private static FightResult Fight(HerFight her, IReadOnlyList<FoeFight> pack, FightMode mode, Random random, Action<string>? trace)
    {
        var player = her.Her;
        var view = new FightView
        {
            Health = player.Health.MaxValue,
            HealthMax = player.Health.MaxValue,
            Stamina = player.Stamina.MaxValue,
            StaminaMax = player.Stamina.MaxValue,
            Mana = player.Mana.MaxValue,
            ManaMax = player.Mana.MaxValue,
            ManaConversion = her.ManaConversion,
            HealthRegen = her.Regen.Health,
            StaminaRegen = her.Regen.Stamina,
            ManaRegen = her.Regen.Mana,
            HealSelf = her.HealSelf,
            HealthDrains = her.HealthDrains,
            Presses = her.Presses,
            StaminaDrain = her.StaminaDrain,
            Foes = [.. pack.Select(p => new Foe(p.Rules))],
        };
        var foes = view.Foes;
        var cursors = pack.Select(p => new BlowCursor(p.Log, random)).ToArray();
        var beats = pack.Select(_ => random.NextDouble() * Heartbeat).ToArray(); // each creature's heartbeat from a random phase, as Commands.Kills takes it
        var herBeat = random.NextDouble() * Heartbeat;
        var (lowest, spent, heals, bolts, presses, staminaDrains, emergencyHeals, refusals) = ((double)view.HealthMax, 0.0, 0, 0, 0, 0, 0, 0);
        var startMana = (double)view.Mana;
        FightResult End(FightEnd end)
        {
            trace?.Invoke(Line($"    {view.Clock,6:0.00} s  health {view.Health}/{view.HealthMax} stamina {view.Stamina} mana {view.Mana}: the fight ends: {end}"));
            return new(end, view.Clock, end == FightEnd.Died ? 0 : lowest, view.Health, spent, startMana - view.Mana, heals, bolts, presses, staminaDrains, emergencyHeals, foes.Count(f => !f.Alive));
        }

        while (true)
        {
            if (foes.All(f => !f.Alive))
            {
                return End(FightEnd.Won);
            }

            if (view.Clock >= LongestFight)
            {
                return End(FightEnd.Stalled);
            }

            var move = InFightRules.Decide(view, mode);
            if (move.Kind == FightMoveKind.OutOfMana || move.Spell is null)
            {
                return End(FightEnd.OutOfMana);
            }

            var spell = move.Kind == FightMoveKind.Bolt ? pack[move.Foe].Strike.Spell : her.Ace(move.Spell);
            WorldObject target = move.Foe >= 0 ? pack[move.Foe].Monster : player;
            var fizzled = random.NextDouble() >= move.Spell.CastChance;
            if (mode != FightMode.BoltsOnly)
            {
                // Player_Magic.cs CalculateManaUsage: a fizzle costs 5, a cast its cost rolled by ACE (Mana Conversion); over the mana held the server refuses it and no time passes.
                var cost = fizzled ? 5u : player.CalculateManaUsage(player, spell, target);
                if (cost > view.Mana)
                {
                    if (++refusals > MaxRefusals)
                    {
                        return End(FightEnd.OutOfMana);
                    }

                    continue;
                }

                refusals = 0;
                view.Mana -= cost;
                spent += cost;
            }

            switch (move.Kind)
            {
                case FightMoveKind.Bolt: bolts++; break;
                case FightMoveKind.Press: presses++; break;
                case FightMoveKind.DrainStamina: staminaDrains++; break;
                default:
                    heals++;
                    emergencyHeals += move.Emergency ? 1 : 0;
                    break;
            }

            // The creatures attack, she and they regenerate, until the cast ends; the effect is the cast's end's.
            var before = trace is null ? string.Empty : Line($"    {view.Clock,6:0.00} s  health {view.Health}/{view.HealthMax} stamina {view.Stamina} mana {view.Mana} (after its cost): {move.Spell.Name}{(move.Foe >= 0 ? Line($" at {foes[move.Foe].Kind.Name} #{move.Foe} ({foes[move.Foe].Health:0} health)") : string.Empty)} ({move.Why})");
            var end = view.Clock + move.Spell.Seconds;
            if (Advance(view, her, pack, cursors, beats, ref herBeat, end, ref lowest, random))
            {
                return End(FightEnd.Died);
            }

            view.Clock = end;
            var landed = false;
            var resisted = false;
            if (!fizzled)
            {
                (landed, resisted) = Effect(view, her, pack, move, spell, random);
            }

            trace?.Invoke(before + Line($" -> {(fizzled ? "fizzled" : resisted ? "resisted" : landed ? "landed" : "no effect")}; at {view.Clock:0.00} s health {view.Health} stamina {view.Stamina} mana {view.Mana}{(move.Foe >= 0 ? Line($", it {foes[move.Foe].Health:0}") : string.Empty)}"));

            if (move.Emergency)
            {
                view.Emergency.Ended(landed, resisted, move.Spell.Seconds, view.Clock);
            }
        }
    }

    /// <summary>The effect of a cast that did not fizzle: true when it landed, and whether it was resisted.</summary>
    private static (bool Landed, bool Resisted) Effect(FightView view, HerFight her, IReadOnlyList<FoeFight> pack, FightMove move, Spell spell, Random random)
    {
        var player = her.Her;
        var foes = view.Foes;
        switch (move.Kind)
        {
            case FightMoveKind.Bolt:
            {
                var damage = pack[move.Foe].Strike.Damage;
                var hit = damage[random.Next(damage.Length)]; // ACE's sampled strike: 0 where it resisted
                foes[move.Foe].Health -= hit;
                return (hit > 0, hit == 0);
            }

            case FightMoveKind.DrainHealth:
            case FightMoveKind.DrainStamina:
            {
                var foe = foes[move.Foe];
                if (!foe.Alive || random.NextDouble() >= foe.Kind.DrainLand)
                {
                    return (false, foe.Alive); // resisted (WorldObject_Magic.cs MagicDefenseCheck), or the creature fell during the cast
                }

                var monster = pack[move.Foe].Monster;
                monster.UpdateVital(monster.Health, (uint)Math.Max(1, Math.Round(foe.Health)));
                monster.UpdateVital(monster.Stamina, (uint)Math.Max(0, Math.Round(foe.Stamina)));
                Push(player, view);
                player.TryCastSpell(spell, monster, tryResist: false);
                foe.Health = monster.Health.Current;
                foe.Stamina = monster.Stamina.Current;
                Pull(player, view);
                return (true, false);
            }

            default:
                Push(player, view);
                player.TryCastSpell(spell, player, tryResist: false);
                Pull(player, view);
                return (true, false);
        }
    }

    /// <summary>The creatures' blows and the heartbeats up to <paramref name="until"/>, in time order. True when she died.</summary>
    private static bool Advance(FightView view, HerFight her, IReadOnlyList<FoeFight> pack, BlowCursor[] cursors, double[] beats, ref double herBeat, double until, ref double lowest, Random random)
    {
        var foes = view.Foes;
        while (true)
        {
            // The earliest event: a living creature's blow, its heartbeat, or hers.
            var at = herBeat <= until ? herBeat : double.PositiveInfinity;
            var who = -1;
            var blow = false;
            for (var i = 0; i < foes.Count; i++)
            {
                if (!foes[i].Alive)
                {
                    continue;
                }

                var next = cursors[i].Next(until);
                if (next < at)
                {
                    (at, who, blow) = (next, i, true);
                }

                if (beats[i] <= until && beats[i] < at)
                {
                    (at, who, blow) = (beats[i], i, false);
                }
            }

            if (double.IsPositiveInfinity(at))
            {
                return false;
            }

            if (who < 0)
            {
                // Her heartbeat: ACE's VitalHeartBeat on her (Creature_Vitals.cs: her rates, her attributes, her stance, her enchantments).
                var player = her.Her;
                Push(player, view);
                player.VitalHeartBeat(player.Health);
                player.VitalHeartBeat(player.Stamina);
                player.VitalHeartBeat(player.Mana);
                Pull(player, view);
                herBeat += Heartbeat;
                continue;
            }

            if (!blow)
            {
                var foe = foes[who];
                foe.Health = Math.Min(foe.Kind.HealthMax, foe.Health + pack[who].HealthPerTick);
                foe.Stamina = Math.Min(foe.Kind.StaminaMax, foe.Stamina + pack[who].StaminaPerTick);
                beats[who] += Heartbeat;
                continue;
            }

            var (damage, kind) = cursors[who].Take();
            if (kind != BlowLog.SpellBlow)
            {
                // Player_Combat.cs: in a combat mode every attack thrown at her costs a point of stamina; an evade is spared it at Endurance's chance when her defence is trained.
                view.Attacks++;
                var spared = damage == 0 && random.NextDouble() < (kind == BlowLog.Missile ? her.SparedOnEvade.Missile : her.SparedOnEvade.Melee);
                if (!spared && view.Stamina > 0)
                {
                    view.Stamina--;
                }
            }
            else
            {
                view.Attacks++;
            }

            if (damage > 0)
            {
                view.Hits.Note(at, damage);
                view.Damage += damage;
                view.BiggestHit = Math.Max(view.BiggestHit, damage);
                view.Health = (uint)Math.Max(0, (int)view.Health - damage);
                lowest = Math.Min(lowest, view.Health);
                if (view.Health == 0)
                {
                    view.Clock = at;
                    return true;
                }
            }
        }
    }

    private static void Push(Player player, FightView view)
    {
        player.UpdateVital(player.Health, view.Health);
        player.UpdateVital(player.Stamina, view.Stamina);
        player.UpdateVital(player.Mana, view.Mana);
    }

    private static void Pull(Player player, FightView view)
    {
        view.Health = player.Health.Current;
        view.Stamina = player.Stamina.Current;
        view.Mana = player.Mana.Current;
    }

    /// <summary>The table of <paramref name="verdicts"/>, one row a pack.</summary>
    /// <param name="verdicts">Each pack's label, its fights with her heals, and the same fights with bolts only (null: not played).</param>
    public static string Table(IReadOnlyList<(string Label, PackVerdict Healed, PackVerdict? BoltsOnly)> verdicts)
    {
        var text = new StringBuilder();
        text.AppendLine(Line($"  {"at once",-12} {"dies",7} {"out of mana",12} {"stalled",8} {"lowest health: mean",20} {"10th pct",9} {"heals cast",11} {"mana spent",11} {"s to clear",11} {"bolts only: dies",17}"));
        foreach (var (label, v, bolts) in verdicts)
        {
            text.AppendLine(Line($"  {label,-12} {v.Dies,7:P1} {v.OutOfMana,12:P1} {v.Stalled,8:P1} {v.LowestMean,20:0.0} {v.LowestP10,9:0} {v.HealsMean,11:0.0} {v.ManaSpentMean,11:0.0} {(double.IsNaN(v.ClearSeconds) ? "-" : v.ClearSeconds.ToString("0.0", CultureInfo.InvariantCulture)),11} {(bolts is null ? "-" : bolts.Dies.ToString("P1", CultureInfo.InvariantCulture)),17}"));
        }

        return text.ToString();
    }

    /// <summary>What her casts were, a fight, by pack: the policy's work in numbers.</summary>
    /// <param name="verdicts">Each pack's label and its fights with her heals.</param>
    public static string Casts(IReadOnlyList<(string Label, PackVerdict Healed, PackVerdict? BoltsOnly)> verdicts) =>
        "  her casts a fight (with her heals): " + string.Join("; ", verdicts.Select(v => Line($"{v.Label}: {v.Healed.BoltsMean:0.0} bolts, {v.Healed.HealsMean:0.0} heals ({v.Healed.EmergencyShare:P0} of them the emergency's), {v.Healed.PressesMean:0.0} presses, {v.Healed.StaminaDrainsMean:0.0} stamina drains{(double.IsNaN(v.Healed.OutOfManaSeconds) ? string.Empty : Line($"; out of mana at {v.Healed.OutOfManaSeconds:0.0} s with {v.Healed.OutOfManaKills:0.0} of {v.Label.Split(' ')[0]} killed"))}")));

    private static string Line(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
