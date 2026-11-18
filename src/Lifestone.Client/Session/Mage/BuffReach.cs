using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using DatReaderWriter.Enums;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>One landed step of the bootstrap: the line, its level and the Creature Enchantment skill before and after it.</summary>
/// <param name="Line">The line's name.</param>
/// <param name="Level">The level chosen (1 to 4 from the catalog; to <see cref="BuffReach.BookTop"/> from her book).</param>
/// <param name="Before">The Creature Enchantment skill the level was chosen on.</param>
/// <param name="After">The skill the step leaves.</param>
public sealed record BuffReachStep(string Line, int Level, uint Before, uint After);

/// <summary>What the bootstrap reaches (<see cref="BuffReach.Walk"/>).</summary>
/// <param name="Steps">The steps, in the order the pass casts them.</param>
/// <param name="Focus">The Focus Self level reached (0: none).</param>
/// <param name="Willpower">The Willpower Self level reached.</param>
/// <param name="CreatureMastery">The Creature Enchantment Mastery Self level reached.</param>
/// <param name="Creature">The Creature Enchantment skill at the end.</param>
/// <param name="Life">The Life Magic skill with its mastery at the level the end skill casts.</param>
/// <param name="War">The War Magic skill likewise.</param>
/// <param name="Item">The Item Enchantment skill likewise.</param>
public sealed record BuffReachResult(IReadOnlyList<BuffReachStep> Steps, int Focus, int Willpower, int CreatureMastery, uint Creature, uint Life, uint War, uint Item)
{
    /// <summary>The skill the bootstrap reaches in <paramref name="school"/>.</summary>
    /// <param name="school">The school.</param>
    public uint SkillOf(MagicSchool school) => school switch
    {
        MagicSchool.CreatureEnchantment => Creature,
        MagicSchool.LifeMagic => Life,
        MagicSchool.WarMagic => War,
        MagicSchool.ItemEnchantment => Item,
        _ => 0,
    };
}

/// <summary>One step of the experience push (<see cref="BuffReach.Push"/>): the skill, the ranks the plan asks and the numbers in words.</summary>
/// <param name="Skill">The skill id.</param>
/// <param name="Ranks">The target ranks.</param>
/// <param name="Why">The goal, for the log line.</param>
public sealed record SkillPush(uint Skill, int Ranks, string Why);

/// <summary>
/// M6 7 AK (the owner, 2026-11-06: buff everything, and bootstrap; 0.75 is the bar during buffing, 0.95 stays for combat; a mage keeps pushing for stronger buffs). Pure. What the pass reaches: Focus Self, Willpower Self and Creature Enchantment Mastery Self are walked in turn, each at the
/// highest level held whose chance on the skill standing then reaches the bar, until a sweep changes nothing; every school's mastery is then the highest level the end skill casts, and each school's skill is read as ACE reads it: <c>CreatureSkill.Current</c>
/// (Entity/CreatureSkill.cs:154-195 @95ef426d): the formula over the BUFFED attributes plus InitLevel plus ranks, scaled by the vitae, then the mastery added unscaled. The cast chance is <c>GetMagicSkillChance</c> (0.75 is power + 16; 0.95 is power + 43).
/// </summary>
public static class BuffReach
{
    /// <summary>The highest level the catalog sells of the creature self lines, and the level the experience push brings her item lines under the bar for (<c>TODO(m6-self-buff-lines-v-vi)</c>). M6 14 (<c>m6-banes</c>): the catalog's item armor lines run to VI now and the plan buys V and VI once her standing skill casts them; the push still asks no ranks for them.</summary>
    public const int TopLevel = 4;

    /// <summary>M6 item 14 S3: the level the buff bot's lines stand at (VII), as <see cref="Walk"/> reports the bootstrap when the bot covers it.</summary>
    public const int BotLevel = 7;

    /// <summary>
    /// M6 14 own buffs: the highest level of a bootstrap or mastery line the walk reads from her book (VI: the scrivener stock's top; VII is the loot's). The every-spell rule buys V and VI of her own lines (the owner, 2026-11-17: she relies
    /// on her self-buffs), so a book that holds them stands on them; the plan's catalog sells these lines to IV, so a plan's walk over the catalog is unchanged.
    /// </summary>
    public const int BookTop = 6;

    /// <summary>The levels of a creature self line's boost (the world's spell rows, stat_Mod_Val, <see cref="BuffStatMods"/>: Focus Self V 30, VI 35): index = level.</summary>
    private static readonly int[] Boost = [0, 10, 15, 20, 25, 30, 35];

    /// <summary>The powers of levels I to VI of these lines (ArenaProfile's).</summary>
    private static readonly uint[] Powers = [0, 1, 50, 100, 150, 200, 250];

    /// <summary>The bootstrap's lines, in cast order.</summary>
    public static IReadOnlyList<string> Bootstrap { get; } = ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self"];

    /// <summary>The bootstrap on <paramref name="q"/>: its raw attributes, its ranks and vitae, never the buffs that stand now (a death leaves none, a pass rebuilds them).</summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="bar">The chance a level must reach (<see cref="Ladder.PlanBuffChance"/>).</param>
    /// <param name="held">Whether a level of a line may be cast: the book's when the pass is planned, every level the catalog sells when the plan asks what to buy.</param>
    /// <param name="bot">
    /// M6 item 14 S3: true while the buff bot covers its lines (<see cref="BuffBotCover.On"/>): the bootstrap is the bot's, so no step of her own is walked (Creature Enchantment may be untrained) and every school's skill is read with the bot's
    /// level VII Focus, Willpower and mastery lines up (<see cref="BuffBotCover.Boost"/> each; the levels read <see cref="BotLevel"/>). False (the default): her own bootstrap.
    /// </param>
    /// <returns>The result, or null when Focus or Self is not read.</returns>
    public static BuffReachResult? Walk(PlayerQualities q, double bar, Func<string, int, bool> held, bool bot = false)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(held);
        if (!q.TryGetAttribute(MageTemplate.Focus, out var focus) || !q.TryGetAttribute(MageTemplate.Self, out var self))
        {
            return null;
        }

        if (bot)
        {
            var scale = q.TryGetVitae(out var botVitae) ? botVitae.Enchantment.StatModValue : 1.0;
            var raw = Formula(focus.Start + focus.Ranks, self.Start + self.Ranks);
            var lifted = Formula(focus.Start + focus.Ranks + BuffBotCover.Boost, self.Start + self.Ranks + BuffBotCover.Boost);
            uint With(uint id) => (uint)Math.Max(0, Math.Round(((lifted + Rest(q, id, raw)) * scale) + BuffBotCover.Boost, MidpointRounding.AwayFromZero));
            return new BuffReachResult([], BotLevel, BotLevel, BotLevel, With(MageTemplate.CreatureEnchantment), With(MageTemplate.LifeMagic), With(MageTemplate.WarMagic), With(MageTemplate.ItemEnchantment));
        }

        if (!q.TryGetSkill(MageTemplate.CreatureEnchantment, out var creature) || creature.AdvancementClass < SkillRules.TrainedClass)
        {
            held = static (_, _) => false; // no Creature Enchantment: no bootstrap to count on
        }

        var vitae = q.TryGetVitae(out var v) ? v.Enchantment.StatModValue : 1.0;
        var (rawFocus, rawSelf) = ((double)(focus.Start + focus.Ranks), (double)(self.Start + self.Ranks));
        var formula = Formula(rawFocus, rawSelf);
        var creatureRest = Rest(q, MageTemplate.CreatureEnchantment, formula);
        var levels = new int[3];
        uint Skill(int rest, int mastery) => (uint)Math.Max(0, Math.Round(((Formula(rawFocus + Boost[levels[0]], rawSelf + Boost[levels[1]]) + rest) * vitae) + Boost[mastery], MidpointRounding.AwayFromZero));
        uint Creature() => Skill(creatureRest, levels[2]);
        var steps = new List<BuffReachStep>();
        for (var sweep = 0; sweep < BookTop; sweep++)
        {
            var moved = false;
            for (var i = 0; i < 3; i++)
            {
                var before = Creature();
                var level = TopAt(before, bar, Bootstrap[i], held);
                if (level > levels[i])
                {
                    levels[i] = level;
                    steps.Add(new BuffReachStep(Bootstrap[i], level, before, Creature()));
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        var end = Creature();
        uint School(uint id, MagicSchool school) => Skill(Rest(q, id, formula), TopAt(end, bar, MasteryOf(school), held));
        return new BuffReachResult(steps, levels[0], levels[1], levels[2], end, School(MageTemplate.LifeMagic, MagicSchool.LifeMagic), School(MageTemplate.WarMagic, MagicSchool.WarMagic), School(MageTemplate.ItemEnchantment, MagicSchool.ItemEnchantment));
    }

    /// <summary>
    /// The standing rule (the owner: a mage keeps pushing for stronger buffs): the ranks that bring the next buff level of a school under the bar with the bootstrap the book holds, cheapest step first: Item Enchantment to III, Creature Enchantment (the bootstrap school: it lifts every other)
    /// to the next level of the bootstrap, Item Enchantment to the level after. A step is asked only when the book holds the buffs it counts on, and a skill not trained asks nothing. Life Magic reaches IV with no ranks and War Magic's bar is combat's: neither is pushed.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="bar">The bar.</param>
    /// <param name="knows">Whether the book holds a line's level.</param>
    /// <param name="bot">
    /// M6 item 14 S3: true while the buff bot covers its lines (<see cref="BuffBotCover.On"/>): her book holds no bootstrap, and with none the rule above asks nothing, so her item lines would never be pushed. The bot's level VII lines stand in
    /// the bootstrap's place (<see cref="BuffBotCover.Boost"/> on Focus, on Self and on the mastery): Item Enchantment, the one buff school that is hers, is pushed to III and then to <see cref="TopLevel"/>; Creature Enchantment is never pushed
    /// (the bot casts its lines). A skill not trained still asks nothing.
    /// </param>
    public static IReadOnlyList<SkillPush> Push(PlayerQualities q, double bar, Func<string, int, bool> knows, bool bot = false)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(knows);
        var pushes = new List<SkillPush>();
        if (!q.TryGetAttribute(MageTemplate.Focus, out var focus) || !q.TryGetAttribute(MageTemplate.Self, out var self))
        {
            return pushes;
        }

        if (bot)
        {
            if (q.TryGetSkill(MageTemplate.ItemEnchantment, out var item) && item.AdvancementClass >= SkillRules.TrainedClass)
            {
                var raw = Formula(focus.Start + focus.Ranks, self.Start + self.Ranks);
                var lift = Formula(focus.Start + focus.Ranks + BuffBotCover.Boost, self.Start + self.Ranks + BuffBotCover.Boost) - raw;
                var rest = Rest(q, MageTemplate.ItemEnchantment, raw) - (int)item.Ranks;
                var scale = q.TryGetVitae(out var botVitae) ? botVitae.Enchantment.StatModValue : 1.0;
                var over = 0;
                while (SpellPlan.Chance(100 + (uint)over, 100) < bar)
                {
                    over++;
                }

                foreach (var goal in new[] { 3, TopLevel })
                {
                    var want = (int)Math.Ceiling(((int)Powers[goal] + over - BuffBotCover.Boost) / scale) - lift - raw - rest;
                    if (want > (int)item.Ranks)
                    {
                        pushes.Add(new SkillPush(MageTemplate.ItemEnchantment, want, string.Create(CultureInfo.InvariantCulture, $"Item Enchantment base {raw + rest + want} brings level {ScrivenerLearnPlan.Numeral(goal)} under the bar {bar:0.00} with the buff bot's level VII lines up")));
                    }
                }
            }

            return pushes;
        }

        var formula = Formula(focus.Start + focus.Ranks, self.Start + self.Ranks);
        var margin = 0;
        while (SpellPlan.Chance(100 + (uint)margin, 100) < bar)
        {
            margin++;
        }

        // The bootstrap level the book holds whole: what a step counts on.
        var trio = TopLevel;
        while (trio > 0 && !Bootstrap.All(l => knows(l, trio)))
        {
            trio--;
        }

        var itemMastery = TopLevel;
        while (itemMastery > 0 && !knows(MasteryOf(MagicSchool.ItemEnchantment), itemMastery))
        {
            itemMastery--;
        }

        int Attribute(int level) => Formula(focus.Start + focus.Ranks + Boost[level], self.Start + self.Ranks + Boost[level]) - formula;
        void Ask(uint id, int goal, int level, int mastery)
        {
            if (level <= 0 || goal > TopLevel || !q.TryGetSkill(id, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
            {
                return;
            }

            var rest = Rest(q, id, formula) - (int)skill.Ranks;
            var vitae = q.TryGetVitae(out var v) ? v.Enchantment.StatModValue : 1.0; // the vitae scales the base, never the mastery
            var want = (int)Math.Ceiling(((int)Powers[goal] + margin - Boost[mastery]) / vitae) - Attribute(level) - formula - rest;
            if (want > (int)skill.Ranks)
            {
                pushes.Add(new SkillPush(id, want, string.Create(CultureInfo.InvariantCulture, $"{MageTemplate.SkillName(id)} base {formula + rest + want} brings level {ScrivenerLearnPlan.Numeral(goal)} under the bar {bar:0.00} with the buffs at {ScrivenerLearnPlan.Numeral(level)}")));
            }
        }

        var itemLevel = Math.Min(trio, itemMastery);
        Ask(MageTemplate.ItemEnchantment, Math.Min(itemLevel + 1, 3), itemLevel, itemMastery); // the first step: cheap, and it is the robe's armor
        Ask(MageTemplate.CreatureEnchantment, trio + 1, trio, trio);
        if (itemLevel >= 3 && trio > 3)
        {
            Ask(MageTemplate.ItemEnchantment, itemLevel + 1, itemLevel, itemMastery);
        }

        return pushes;
    }

    /// <summary>
    /// M6 14 T: the points a creature self line she keeps standing adds to its attribute (Endurance Self to the Endurance under her stamina), read as <see cref="Walk"/> reads the bootstrap: never from the enchantments on her now. While the buff
    /// bot covers the line (<paramref name="bot"/>'s lines, <see cref="BuffBotCover.Covers"/>), the bot's level VII boost; else the boost of the highest level she holds (<paramref name="held"/>) that the Creature Enchantment skill her bootstrap
    /// reaches (<paramref name="standing"/>) casts at <paramref name="bar"/>; 0 with none.
    /// </summary>
    /// <param name="standing">The bootstrap she keeps standing (<see cref="Walk"/> over her own book), or null.</param>
    /// <param name="bar">The buff bar.</param>
    /// <param name="line">The line (<c>Endurance Self</c>).</param>
    /// <param name="held">Whether her book holds a level of a line.</param>
    /// <param name="bot">The bot's profile that covers her lines (<see cref="BuffBotCover.Covering"/>), or null while none does.</param>
    public static int StandingBoost(BuffReachResult? standing, double bar, string line, Func<string, int, bool> held, BuffBotProfile? bot = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(held);
        if (bot is not null && BuffBotCover.Covers(bot, line))
        {
            return BuffBotCover.Boost;
        }

        return standing is null ? 0 : Boost[TopAt(standing.Creature, bar, line, held)];
    }

    /// <summary>The mastery line of a school.</summary>
    private static string MasteryOf(MagicSchool school) => school switch
    {
        MagicSchool.LifeMagic => "Life Magic Mastery Self",
        MagicSchool.WarMagic => "War Magic Mastery Self",
        MagicSchool.ItemEnchantment => "Item Enchantment Mastery Self",
        _ => "Creature Enchantment Mastery Self",
    };

    /// <summary>The part of a school's skill that the attributes do not give: its InitLevel and ranks (0 when untrained).</summary>
    private static int Rest(PlayerQualities q, uint skillId, int formula) =>
        GearCasts.TryBaseSkill(q, skillId, out var value) ? (int)value - formula : 0;

    private static int Formula(double focus, double self) => (int)Math.Round((focus + self) / MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero);

    private static int TopAt(uint skill, double bar, string line, Func<string, int, bool> held)
    {
        for (var level = BookTop; level >= 1; level--)
        {
            if (held(line, level) && SpellPlan.Chance(skill, Powers[level]) >= bar)
            {
                return level;
            }
        }

        return 0;
    }
}
