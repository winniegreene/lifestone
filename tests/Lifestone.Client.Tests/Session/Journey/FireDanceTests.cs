using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 fire dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions, and dancing under fire is ordinary retail play; live <c>cache/m6/14/live/ladder23</c> 02:51:52 UTC: the Reedshark Ravagers gave her no lull and
/// she recalled on the mana line, "Ilservian's Flame (35 mana) would take the mana 151 under the way out's reserve line=120", health 185 of 204, hit about 2 a blow). What these prove, at her level-68 shape
/// (<see cref="StaminaDanceTests.Her"/>): at the live numbers she dances under fire (Revitalize Self VI first, its expected cost within the 31 over the line; then the press, after which the bolt clears the line) and the way out holds
/// the leaving for the round; losing, she leaves; with no Revitalize Self the dance does not cover her and she leaves as before; two barren rounds, or the stamina and the mana both spent, she leaves, each by name.
/// </summary>
public sealed class FireDanceTests(ITestOutputHelper output)
{
    private static readonly WayOutLine Line = ManaWayOut.LineOf(50, 70); // Lifestone Recall 50 + Heal Self VI 70: the live line=120

    private const uint Bolt = 35; // Ilservian's Flame

    private static GameState At(Dictionary<uint, Lifestone.Data.Magic.SpellInfo> table, uint mana, uint stamina, int[]? revitalize = null)
    {
        var state = StaminaDanceTests.Her(table, revitalize: revitalize);
        var q = state.Self.Qualities;
        q.Vitals[MageTemplate.MaxMana] = new VitalDesc(MageTemplate.MaxMana, 0, 291, 0, mana); // her maximum 391 (+ Self 100)
        q.Vitals[MageTemplate.MaxStamina] = new VitalDesc(MageTemplate.MaxStamina, 0, 287, 0, stamina);
        return state;
    }

    private static FireDanceStep Decide(GameState state, Dictionary<uint, Lifestone.Data.Magic.SpellInfo> table, uint health, double incoming, bool afterRevitalize = false, int barren = 0)
    {
        var round = StaminaDance.Round(state, table, out var why);
        var q = state.Self.Qualities;
        q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax);
        q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out var staminaMax);
        var input = new FireDanceInput(Line, Bolt, mana, manaMax, stamina, staminaMax, health, incoming, afterRevitalize, barren);
        return FireDance.Decide(in input, round, why, StaminaPress.Levels(state, table));
    }

    /// <summary>The live shape: mana 151, stamina 29 (10% after the run), health 185, the Ravagers' 6.9 a second measured: she dances (Revitalize, then the press), the way out holds, and after the round the bolt goes.</summary>
    [Fact]
    public void At_the_Ravagers_live_shape_she_dances_under_fire_and_bolts_on_with_no_retreat()
    {
        var table = StaminaDanceTests.Table();
        Assert.False(ManaWayOut.Spend(new WayOutSpend(WayOutSpendKind.Bolt, 151, Bolt, Line, Attackers: true)).Yes); // the live refusal: 151 < 35 + 120

        var first = Decide(At(table, 151, 29), table, 185, 6.9);
        output.WriteLine(first.Why);
        Assert.Equal((FireDanceMove.Revitalize, FireDance.GoRevitalize, 5), (first.Move, first.Token, first.Index)); // Revitalize Self VI
        Assert.Contains("within the 31 over the line", first.Why, StringComparison.Ordinal);

        // the way out holds the leaving for the round at the line, as for a press
        var held = ManaWayOut.Decide(new WayOutInput(true, 120, Line, Attackers: true, KillPending: false, Press: false, Refused: true, Dance: true));
        Assert.Equal((false, "hold:dance"), (held.Yes, held.Why));

        // Revitalize landed: stamina 29 + 120, mana 151 less its cost: the round's press, at a level whose most leaves the recall paid
        var round = StaminaDance.Round(At(table, 137, 149), table, out _)!.Value;
        var press = Decide(At(table, 137, 149), table, 181, 6.9, afterRevitalize: true);
        output.WriteLine(press.Why);
        Assert.Equal((FireDanceMove.Press, FireDance.GoPress), (press.Move, press.Token));
        Assert.True(StaminaPress.Levels(At(table, 137, 149), table)[press.Index].BaseMana + Line.RecallCost <= 137);
        Assert.True(137 + press.Gives >= Bolt + Line.Line, $"{press.Gives}: the round lifts the mana over the bolt and the line");
        Assert.True(ManaWayOut.Spend(new WayOutSpend(WayOutSpendKind.Bolt, 137 + (uint)press.Gives, Bolt, Line, Attackers: true)).Yes); // then she bolts on
        Assert.InRange(round.Cost, 10, 20); // Revitalize Self VI's expected cost on Mana Conversion 264: about the live 14
    }

    /// <summary>Losing (the last seconds' hits kill her within 5 s at her health), she leaves: the dance holds no leaving then.</summary>
    [Fact]
    public void In_a_fight_she_is_losing_she_leaves_by_name()
    {
        var table = StaminaDanceTests.Table();
        var losing = Decide(At(table, 151, 29), table, 30, 7);
        Assert.Equal((FireDanceMove.Leave, FireDance.Losing), (losing.Move, losing.Token));
        Assert.Contains("the hits win: 7 a second over 5 s is 35 against her health 30", losing.Why, StringComparison.Ordinal);
        var d = ManaWayOut.Decide(new WayOutInput(true, 120, Line, Attackers: true, KillPending: false, Press: false, Refused: true, Losing: true, Dance: true));
        Assert.Equal((true, ManaWayOut.Leave), (d.Yes, d.Why));
    }

    /// <summary>No Revitalize Self in the book: the dance does not cover her, and she leaves as before, the dance's why named.</summary>
    [Fact]
    public void A_dance_that_does_not_cover_her_leaves_as_before()
    {
        var table = StaminaDanceTests.Table();
        var none = Decide(At(table, 151, 29, revitalize: []), table, 185, 6.9);
        Assert.Equal((FireDanceMove.Leave, FireDance.Uncovered), (none.Move, none.Token));
        Assert.Equal("the stamina dance does not cover her: no Revitalize Self level the book holds and the pack pays: the potions stay", none.Why);
        Assert.True(ManaWayOut.Decide(new WayOutInput(true, 120, Line, Attackers: true, KillPending: false, Press: false, Refused: true)).Yes);
    }

    /// <summary>Two barren rounds in a row, or the stamina and the mana both run out (no stamina to press, and the Revitalize's expected cost over the 5 over the line), she leaves, each by name.</summary>
    [Fact]
    public void Two_barren_rounds_or_the_stamina_and_mana_spent_she_leaves_by_name()
    {
        var table = StaminaDanceTests.Table();
        var barren = Decide(At(table, 151, 29), table, 185, 2, barren: FireDance.BarrenRounds);
        Assert.Equal((FireDanceMove.Leave, FireDance.Barren), (barren.Move, barren.Token));
        Assert.StartsWith("2 rounds in a row landed nothing or netted no mana", barren.Why, StringComparison.Ordinal);

        var spent = Decide(At(table, 125, 0), table, 185, 2);
        output.WriteLine(spent.Why);
        Assert.Equal((FireDanceMove.Leave, FireDance.Spent), (spent.Move, spent.Token));
        Assert.Contains("the line refuses it", spent.Why, StringComparison.Ordinal);
        Assert.EndsWith("the stamina and the mana have run out", spent.Why, StringComparison.Ordinal);

        // over the line by 5 with stamina held: the press from the stamina held instead of the Revitalize the line refuses
        var held = Decide(At(table, 125, 200), table, 185, 2);
        Assert.Equal(FireDanceMove.Press, held.Move);
    }

    /// <summary>
    /// M6 14 top-off (the owner, 2026-11-15: a top-off before the run resumes): the mana first, a round at a time, to her maximum; at her maximum the stamina last, a Revitalize when a whole one fits (its round for the stamina);
    /// done when the stamina is within one Revitalize of her maximum and no press nets.
    /// </summary>
    [Fact]
    public void The_top_off_fills_the_mana_then_the_stamina_and_ends_near_full()
    {
        var table = StaminaDanceTests.Table();
        var state = At(table, 391, 287);
        var round = StaminaDance.Round(state, table, out _)!.Value;
        var presses = StaminaPress.Levels(state, table);
        var low = StaminaDance.TopOff(in round, presses, 250, 391, 40, 287, afterRevitalize: false);
        Assert.Equal((WalkDanceMove.Revitalize, false), (low.Move, low.ForStamina)); // a mana round
        Assert.Equal(WalkDanceMove.Press, StaminaDance.TopOff(in round, presses, 236, 391, 160, 287, afterRevitalize: true).Move);
        var last = StaminaDance.TopOff(in round, presses, 391, 391, 100, 287, afterRevitalize: false);
        Assert.Equal((WalkDanceMove.Revitalize, true), (last.Move, last.ForStamina)); // the stamina last
        Assert.StartsWith("the stamina last: Revitalize Self VI", last.Why, StringComparison.Ordinal);
        var full = StaminaDance.TopOff(in round, presses, 391, 391, 250, 287, afterRevitalize: false);
        Assert.Equal(WalkDanceMove.Done, full.Move); // 37 missing: a whole Revitalize of 120 would overflow
        Assert.StartsWith("topped off: mana 391 of 391", full.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 walk drains (the owner, 2026-11-15: the server's database holds every creature): a creature in no ground profile resolves from the world database's table, its maximum health by the profiles' formula and
    /// its health-drain resistance; an Armored Tusker's resistance is 0.
    /// </summary>
    [Fact]
    public void A_creature_in_no_profile_resolves_from_the_world_databases_table()
    {
        Assert.True(CreatureTable.Count > 7000, CreatureTable.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(CreatureFacts.All.Recorded(8052)); // Scrawed Grievver: in no profile
        Assert.StartsWith("the world database's creature table", CreatureFacts.All.Find(8052)!.Where, StringComparison.Ordinal); // M6 14 the losing race: its attack from the table, never unknown
        Assert.Equal(80 + 0 + 70, CreatureFacts.All.MaxHealthOf(8052, out var source)); // health 80 + 0 ranks + Endurance 140 / 2
        Assert.Equal("the world database's creature table", source);
        Assert.Equal(0.15, DrainResist.Of(8052), 6);
        var row = CreatureTable.Find(22746)!;
        Assert.Equal(("Reedshark Ravager", 100, 375, 1.0), (row.Name, row.Level, row.MaxHealth, row.ResistHealthDrain));
        Assert.Equal(0.0, DrainResist.Of(11540)); // Armored Tusker: never drained
        Assert.Null(CreatureFacts.All.MaxHealthOf(0xFFFFFF, out var none));
        Assert.StartsWith("TODO(m6-creature-health)", none, StringComparison.Ordinal);
    }
}
