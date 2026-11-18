using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores her mana quickly, with no potion at all): whether the stamina dance covers her mana
/// (<see cref="StaminaDance.Covers"/>, which sells every drink and suspends the drink stock lines when it holds). What these prove, at her level-68 shape (Life Magic 354, Mana Conversion 264, stamina 287, mana 391; the pack holds the
/// Foci of Verdancy, so the level floor reads the foci formula: each level's scarab and its Prismatic Tapers): with Revitalize Self and Stamina to Mana Self I to VI in the book and the pack paying them, a round nets mana and the dance covers
/// her; with Mana Conversion untrained it does not; with no Revitalize Self in the book, or only a level the pack cannot pay, it does not.
/// </summary>
public sealed class StaminaDanceTests(ITestOutputHelper output)
{
    private static readonly uint[] Presses = [.. VitalsMeans.StaminaToManaSelf.Take(6)];
    private static readonly uint[] Revitalizes = [.. VitalsMeans.RevitalizeSelf.Take(6)];
    private static readonly uint[] Powers = [1, 50, 100, 150, 200, 250];

    /// <summary>The spell table: each level's base mana (the SpellTable's, pinned in <c>BuffStatMods</c>) and power, and a base formula of its level's scarab (component 1 Lead to 6 Pyreal), so the foci formula is that scarab and its tapers.</summary>
    internal static Dictionary<uint, SpellInfo> Table()
    {
        var table = new Dictionary<uint, SpellInfo>();
        for (var i = 0; i < 6; i++)
        {
            table[Presses[i]] = Life(Presses[i], "Stamina to Mana Self " + LevelFloor.Roman[i], SpellType.Transfer, BuffStatMods.StaminaToManaBaseMana[i], Powers[i], i + 1);
            table[Revitalizes[i]] = Life(Revitalizes[i], "Revitalize Self " + LevelFloor.Roman[i], SpellType.Boost, BuffStatMods.RevitalizeBaseMana[i], Powers[i], i + 1);
        }

        return table;
    }

    private static SpellInfo Life(uint id, string name, SpellType meta, uint baseMana, uint power, int level) =>
        WorldActionRig.Spell(id, meta) with { Name = name, School = MagicSchool.LifeMagic, BaseMana = baseMana, Power = power, Formula = [(uint)level, 0, 0, 0, 0, 0, 0, 0] };

    /// <summary>
    /// Her state: the pack holds the Foci of Verdancy and what one cast of each level of <paramref name="paidLevels"/> burns; the book holds Stamina to Mana Self I to VI and, with <paramref name="revitalize"/>, the Revitalize Self levels named.
    /// </summary>
    internal static GameState Her(Dictionary<uint, SpellInfo> table, bool manaConversion = true, int[]? revitalize = null, int[]? paidLevels = null)
    {
        var rig = new VendorRig(0);
        var pack = new Dictionary<uint, int> { [LevelFloor.FocusOf(MagicSchool.LifeMagic)] = 1 };
        foreach (var level in paidLevels ?? [1, 2, 3, 4, 5, 6])
        {
            foreach (var (component, n) in GearKit.FociFormulaCounts(table[Revitalizes[level - 1]].Formula).Concat(GearKit.FociFormulaCounts(table[Presses[level - 1]].Formula)))
            {
                Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie), "component " + component);
                pack[weenie.Wcid] = Math.Max(pack.GetValueOrDefault(weenie.Wcid), n);
            }
        }

        var guid = 0x80007300u;
        foreach (var (wcid, count) in pack)
        {
            rig.Arrive(guid, "Pack stack " + wcid, wcid, 0x1000, 0, stackSize: (ushort)count);
            rig.Put(guid++);
        }

        var state = rig.R.H.State;
        state.Self.Name = MageTemplate.ClimberName; // the climber: the press's levels are those the pack pays too
        var q = state.Self.Qualities;
        q.Ints[PlayerQualities.LevelProperty] = 68;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 304, 1, 2u, 0, 0u, 0, 0); // 354: (100 + 100) / 4 + 304
        q.Skills[MageTemplate.ManaConversion] = new SkillDesc(MageTemplate.ManaConversion, 231, 1, manaConversion ? 2u : 1u, 0, 0u, 0, 0); // 264: (100 + 100) / 6 + 231
        q.Vitals[MageTemplate.MaxStamina] = new VitalDesc(MageTemplate.MaxStamina, 0, 287, 0, 287);
        q.Vitals[MageTemplate.MaxMana] = new VitalDesc(MageTemplate.MaxMana, 0, 291, 0, 391); // + Self 100
        foreach (var id in Presses)
        {
            state.Self.Spellbook.Learn(id);
        }

        foreach (var level in revitalize ?? [1, 2, 3, 4, 5, 6])
        {
            state.Self.Spellbook.Learn(Revitalizes[level - 1]);
        }

        return state;
    }

    [Fact]
    public void At_her_shape_with_Revitalize_Self_known_and_paid_the_dance_covers_her_mana()
    {
        var table = Table();
        var state = Her(table);
        Assert.True(ManaOrder.TryManaConversion(state.Self.Qualities, out var conversion) && conversion == 264);
        Assert.NotNull(LevelFloor.HeldFor(state, MagicSchool.LifeMagic)); // the focus is held: the level floor applies
        Assert.True(StaminaDance.Covers(state, table, out var why), why);
        output.WriteLine(why);
        Assert.Contains("the stamina dance pays: Revitalize Self VI restores 120 stamina", why, StringComparison.Ordinal);
        Assert.Contains("on Mana Conversion 264", why, StringComparison.Ordinal);
        Assert.Contains("no potion is bought or kept", why, StringComparison.Ordinal);
    }

    [Fact]
    public void With_Mana_Conversion_untrained_the_dance_does_not_cover_her()
    {
        var table = Table();
        Assert.False(StaminaDance.Covers(Her(table, manaConversion: false), table, out var why));
        output.WriteLine(why);
        Assert.StartsWith("Mana Conversion is not trained", why, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_Revitalize_Self_known_or_none_the_pack_pays_the_dance_does_not_cover_her()
    {
        var table = Table();
        Assert.False(StaminaDance.Covers(Her(table, revitalize: []), table, out var none));
        output.WriteLine(none);
        Assert.Equal("no Revitalize Self level the book holds and the pack pays: the potions stay", none); // the live line of 2026-11-15 00:09 UTC

        // VI alone in the book, and the pack holds no Pyreal Scarab (only levels I to V are paid): the level floor refuses it, so the dance does not cover her either
        Assert.False(StaminaDance.Covers(Her(table, revitalize: [6], paidLevels: [1, 2, 3, 4, 5]), table, out var unpaid));
        Assert.Equal(none, unpaid);
        Assert.True(StaminaDance.Covers(Her(table, revitalize: [6]), table, out _)); // paid, it does
    }

    /// <summary>
    /// M6 14 walk dance: a round as numbers at her shape: Revitalize Self VI restores 120 stamina for its expected cost, the best-net press from it is Stamina to Mana Self VI, the round nets the live reading's 63 or so, and its seconds are
    /// the two casts at the ladder's measured self seconds; the words carry them. Not covering her, no round.
    /// </summary>
    [Fact]
    public void A_round_of_the_dance_at_her_shape_reads_its_net_and_its_seconds()
    {
        var table = Table();
        var round = StaminaDance.Round(Her(table), table, out var why)!.Value;
        output.WriteLine(round.Words);
        var policy = Lifestone.Client.Session.Grounds.LadderLoader.WarMage.VitalsPolicy;
        Assert.Equal((Revitalizes[5], 5, 120u, Presses[5], 6), (round.RevitalizeId, round.RevitalizeIndex, round.Restored, round.PressId, round.PressLevel));
        Assert.InRange(round.Net, 55, 70);
        Assert.Equal(round.PressNet - round.Cost, round.Net, 9);
        Assert.Equal(policy.SelfSeconds(6) * 2, round.Seconds, 9);
        Assert.StartsWith("the stamina dance pays: Revitalize Self VI restores 120 stamina", why, StringComparison.Ordinal);
        Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"mana a round in {round.Seconds:0.#} s on Mana Conversion 264"), round.Words, StringComparison.Ordinal);
        Assert.Null(StaminaDance.Round(Her(table, manaConversion: false), table, out _));
    }

    /// <summary>
    /// M6 14 walk dance: the dance's next cast (pure). Low stamina: Revitalize Self VI first; after it lands, the round's press; a full stamina bar (a Revitalize would not fit whole): the press from the stamina held; at the line, done;
    /// with no press that lands on the mana held and no room for a Revitalize, a stop by name.
    /// </summary>
    [Fact]
    public void The_dance_casts_Revitalize_Self_then_the_press_and_ends_at_the_line()
    {
        var table = Table();
        var state = Her(table);
        var round = StaminaDance.Round(state, table, out _)!.Value;
        var presses = StaminaPress.Levels(state, table);
        var first = StaminaDance.Next(in round, presses, 200, 391, 50, 287, 352, afterRevitalize: false);
        Assert.Equal((WalkDanceMove.Revitalize, 5), (first.Move, first.Index));
        var second = StaminaDance.Next(in round, presses, 186, 391, 170, 287, 352, afterRevitalize: true);
        Assert.Equal(WalkDanceMove.Press, second.Move);
        Assert.Equal(Presses[5], presses[second.Index].SpellId); // the best net from 170 stamina: VI
        Assert.True(second.Gives > 0, second.Why);
        var full = StaminaDance.Next(in round, presses, 200, 391, 287, 287, 352, afterRevitalize: false);
        Assert.Equal(WalkDanceMove.Press, full.Move);
        Assert.Contains("a Revitalize of 120 would not fit whole", full.Why, StringComparison.Ordinal);
        Assert.Equal(WalkDanceMove.Done, StaminaDance.Next(in round, presses, 352, 391, 50, 287, 352, afterRevitalize: false).Move);
        Assert.Equal(WalkDanceMove.Done, StaminaDance.Next(in round, presses, 391, 391, 287, 287, 391, afterRevitalize: false).Move); // at her maximum, the line capped there

        // no level lands at the try chance on the mana held, and no room for a Revitalize: the dance cannot run, a stop by name
        var dry = StaminaDance.Next(in round, presses, 0, 391, 287, 287, 352, afterRevitalize: false);
        Assert.Equal(WalkDanceMove.Stop, dry.Move);
        Assert.StartsWith("no press lands at the try chance", dry.Why, StringComparison.Ordinal);
    }
}
