using DatReaderWriter.Enums;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Tests;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 h1 (<c>m6-account-formula-callers</c>, the fight's side): the fight's floors read the account's full formula for a school whose focus the pack does not hold (a school trained from the level-up credits, docs/session.md
/// section 64), and the foci formula when the focus is held (BSD Mage's four, Winnie's War and Life); with no account name the foci formula for every school, as before. Also <c>f10-stamina-drain-row</c>: the stamina drain's row comes
/// from the picker's rows (levels I to VII), not the VI and VII table alone.
/// </summary>
public sealed class FightAccountFormulaTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Account = "formula-test-account";
    private const uint CreatureFocus = 15268;
    private const uint WarFocus = 15271;

    private static uint WcidOf(uint component) => ComponentWeenies.TryGetByComponent(component, out var w) ? w.Wcid : throw new InvalidOperationException("component " + component);

    /// <summary>A Creature Enchantment spell of the player-castable layout (scarab, accent taper, herb, accent taper, powder, potion, accent taper, talisman).</summary>
    private static SpellInfo Creature() =>
        Lifestone.Client.Tests.Session.WorldActionRig.Spell(1000, SpellType.Enchantment) with
        {
            School = MagicSchool.CreatureEnchantment,
            Formula = [6, 63, 7, 63, 25, 37, 63, 49],
            FormulaVersion = 1,
        };

    /// <summary>What the foci formula needs of the Creature spell: the Pyreal Scarab and four Prismatic Tapers.</summary>
    private static Dictionary<uint, int> FociKit() => new() { [WcidOf(6)] = 10, [WcidOf(SpellFormulaRules.PrismaticTaperId)] = 100 };

    /// <summary>What the account's full formula needs of it: the scarab, the herb, the powder, the potion, the talisman and the coloured tapers.</summary>
    private static Dictionary<uint, int> AccountKit() =>
        AccountFormula.Counts(Creature(), false, Account).ToDictionary(c => WcidOf(c.Key), c => c.Value * 10);

    [Fact]
    public void The_fights_refill_floor_counts_the_foci_formula_with_the_focus_held_and_the_accounts_full_formula_without_it()
    {
        var withAccount = new FightRefill(new Dictionary<uint, SpellInfo>(), new RecordingLogSink(), 0, Account);
        var noAccount = new FightRefill(new Dictionary<uint, SpellInfo>(), new RecordingLogSink(), 0);
        var info = Creature();

        // The Creature focus held (BSD Mage's pack): the foci formula, the scarab and Prismatic Tapers, pays; the account's herbs and the rest are not needed.
        var held = FociKit();
        held[CreatureFocus] = 1;
        Assert.True(withAccount.Payable(held, info, "Creature line", 0));

        // No Creature focus (the climber's pack after it trained Creature Enchantment): the same kit is short of the herb, powder, potion and talisman.
        var unfocused = FociKit();
        unfocused[WarFocus] = 1; // the War focus is not the Creature's
        Assert.False(withAccount.Payable(unfocused, info, "Creature line", 0));
        Assert.True(noAccount.Payable(unfocused, info, "Creature line", 0)); // no account name: today's foci formula for every school

        // A pack of the account's formula pays without the focus.
        Assert.True(withAccount.Payable(AccountKit(), info, "Creature line", 0));
    }

    [Fact]
    public void The_stamina_drain_takes_its_row_from_the_pickers_rows_so_a_level_under_VI_is_cast_f10_stamina_drain_row()
    {
        // Cap IV (--fight-max-level 4): the drain's level is IV, which FightDebuffs.Spells (VI and VII) does not hold; the picker's rows (DebuffRows, I to VII) do.
        var r = new FightManaTests.Rig(mana: 400, stamina: 20, knowsStaminaDrain: true) { Fight = new FightOptions { Kills = 1, MaxLevel = 4 } };
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("f10-stamina-drain-row", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("pick: Drain Stamina Other IV on", StringComparison.Ordinal)); // the IV was picked and its row found
        Assert.Contains(r.Log.Lines, l => l.Contains("fight stamina drain: landed after 1 tries (Drain Stamina Other IV", StringComparison.Ordinal));
        Assert.Null(FightDebuffs.Find(DebuffKind.DrainStaminaOther, 4)); // the table the old lookup read holds VI and VII alone
    }

    /// <summary>
    /// The den's kit holds the Life focus (and the other three): with the account passed, the buff cycle's mana loop still reads the foci formula for the Life line and presses exactly the Stamina to Mana levels the kit pays for
    /// (the same assertion as <c>StaminaToManaFloorCycleTests</c>, which passes no account): a character with all its schools' foci is unchanged.
    /// </summary>
    [RequiresDatsFact]
    public void With_the_account_passed_and_the_foci_held_the_mana_loop_presses_the_same_levels_as_without_it()
    {
        var real = SpellTableData.Load(fixture.Dats).Spells;
        var kit = GearKit.Of([.. GearKit.Build(real, "gear-test-account", ground: GroundCatalog.Get("black-spawn-den-aluvian")).Items.Where(i => i.Wcid is not (687 or 688))]); // no Silver or Gold Scarab: level V is short
        var held = kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus).GroupBy(i => i.Wcid).ToDictionary(g => g.Key, g => g.Sum(i => i.Stack));
        uint[] levels = [.. VitalsMeans.StaminaToManaSelf];
        var payable = levels.Where(id => !LevelFloor.ShortOf(held, real[id], out _)).ToList();
        Assert.DoesNotContain(1680u, payable);
        Assert.Equal(payable, levels.Where(id => !LevelFloor.ShortOf(held, real[id], out _, Account)).ToList()); // the Life focus is held: the account changes nothing

        var r = new WorldActionRig();
        var caster = new ScriptedCaster(r, 1, 275);
        foreach (var id in caster.Table.Keys.ToList())
        {
            if (real.TryGetValue(id, out var spell))
            {
                caster.Table[id] = caster.Table[id] with { Formula = spell.Formula, FormulaVersion = spell.FormulaVersion };
            }
        }

        var guid = 0x80000F00u;
        foreach (var item in kit.Items.Where(i => i.Kind is GearKind.Component or GearKind.Focus))
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: item.Name);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = item.Wcid, ItemType = 0x1000, StackSize = (ushort)item.Stack } }, isUpdate: false);
            var thisGuid = guid++;
            r.Server((player, sequence) => AceM3Writers.PutObjInContainer(player, sequence, thisGuid, WorldActionRig.Player, 0, 0));
        }

        var cycle = new BuffCycle(r.Api, r.H.State, caster.Table, new BuffOptions { AccountName = Account }, r.H.Log);
        Assert.True(cycle.Start(r.H.R.Clock.Now));
        var end = r.H.R.Clock.Now + 4000;
        var status = BuffStatus.Running;
        while (status == BuffStatus.Running && r.H.R.Clock.Now < end)
        {
            r.Tick(0.25);
            caster.Pump();
            r.Tick();
            status = cycle.Step(r.H.R.Clock.Now);
        }

        Assert.NotEqual(BuffStatus.Failed, status);
        Assert.All(caster.Answered.Select(a => a.Spell).Where(levels.Contains), id => Assert.Contains(id, payable));
        Assert.Contains(r.H.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.StartsWith("Stamina to Mana Self: level V needs Gold Scarab 687, which the pack does not hold; skipped", StringComparison.Ordinal));
    }
}
