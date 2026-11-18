using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health drinks; a death of the day stood at mana 0 with the emergency on and nothing to cast): the emergency's first question over the scripted caster. With the mana gone it drinks the biggest drink that
/// fits, logs the health before and after and what ACE's line restored; while the mana pays a heal the drinks are kept; a drink that would overflow is never drunk; the next hit that kills is answered by a drink; a refused drink is named and
/// two in a row set the drinks aside.
/// </summary>
public sealed class FightHealthDrinkTests
{
    private const uint Philtre = 27318, Tincture = HealthDrinks.TinctureWcid, Potion = FighterKit.PotionWcid, Draught = VitalsMeans.HealthDraughtWcid;

    private static FightManaTests.Rig Rig(uint mana, uint health, params (uint Wcid, uint Count)[] drinks)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: 200, knowsDrain: true, knowsDance: false, healthDrinks: drinks) { HitsToKill = 400 };
        r.Caster.HealthPerCast = 20;
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(40);
        r.Caster.SetHealth(health);
        return r;
    }

    private static int Lines(FightManaTests.Rig r, string text) => r.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    private static bool Healed(FightManaTests.Rig r) => r.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell) || DrainPicker.Ids.Contains(a.Spell));

    [Fact]
    public void With_the_mana_gone_the_emergency_drinks_the_biggest_drink_that_fits_and_never_one_that_would_overflow()
    {
        var r = Rig(mana: 0, health: 80, (Tincture, 2), (Potion, 3), (Draught, 4)); // 80 of the rig's 170: under the emergency's floor of 60% (102), 90 missing
        var scenario = r.Scenario();

        r.Run(scenario, 30, 0.1);

        // 80 -> 130 by a tincture (90 missing takes it); 40 missing: a tincture would overflow, so a Potion of Healing -> 155, over the safe line of 85% (144.5): the emergency ends.
        Assert.Equal([(Tincture, 50u), (Potion, 25u)], r.Caster.HealthDrinksDrunk);
        Assert.Equal((1u, 2u, 4u), (r.Caster.HealthDrinksLeft(0), r.Caster.HealthDrinksLeft(1), r.Caster.HealthDrinksLeft(2)));
        Assert.False(Healed(r)); // nothing is paid for at mana 0
        Assert.Equal((2, 75L), (scenario.EmergencyHealthDrinks, scenario.EmergencyHealthRestored));
        Assert.Equal(1, Lines(r, "emergency: potion: check before: Health Tincture 0x80000270 (+50, 2 held of 9 health drink(s)) at health 80 of 170 (90 missing: the biggest that fits), mana 0"));
        Assert.Equal(1, Lines(r, "emergency: potion: check after: Confirmed: the Health Tincture restored 50 health (ACE's line); health 80 then 130 of 170"));
        Assert.Equal(1, Lines(r, "emergency: potion: check before: Potion of Healing 0x80000271 (+25, 3 held of 8 health drink(s)) at health 130 of 170 (40 missing: the biggest that fits), mana 0"));
        Assert.Equal(1, Lines(r, "the Potion of Healing restored 25 health (ACE's line); health 130 then 155 of 170"));
        Assert.Equal(2, Lines(r, "the mana pays no heal: the drink is the heal"));
    }

    [Fact]
    public void While_the_mana_pays_a_heal_the_heal_goes_first_and_the_drinks_are_kept()
    {
        var r = Rig(mana: 400, health: 80, (Tincture, 2), (Potion, 3));
        var scenario = r.Scenario();

        r.Run(scenario, 30, 0.1, () => r.Caster.Health >= 145);

        Assert.True(Healed(r));
        Assert.Empty(r.Caster.HealthDrinksDrunk);
        Assert.Equal(0, scenario.EmergencyHealthDrinks);
        Assert.Equal(1, Lines(r, "emergency: potion: none drunk at health 80 of 170 (90 missing), mana 400, 5 health drink(s) held: the mana pays a heal, the next hit does not kill and she outlasts one heal's cast: the heal goes first, the drinks are kept")); // once, not a line a frame
    }

    [Fact]
    public void A_drink_that_would_overflow_is_not_drunk_even_with_the_mana_gone()
    {
        var r = Rig(mana: 0, health: 80, (Philtre, 2)); // 90 missing, a philtre restores 100
        var scenario = r.Scenario();

        r.Run(scenario, 10, 0.1);

        Assert.Empty(r.Caster.HealthDrinksDrunk);
        Assert.Equal(1, Lines(r, "emergency: potion: none drunk at health 80 of 170 (90 missing), mana 0, 2 health drink(s) held: every drink held would overflow"));
    }

    [Fact]
    public void When_the_next_hit_kills_a_drink_goes_before_the_heal_the_mana_pays()
    {
        var r = Rig(mana: 400, health: 170, (Potion, 3));
        var scenario = r.Scenario();
        r.Run(scenario, 1, 0.1);
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 31, 4, false, 0));
        r.Run(scenario, 0.2, 0.1);
        r.Caster.SetHealth(20); // under the biggest hit seen (31): the next hit kills

        r.Run(scenario, 30, 0.1, () => r.Caster.HealthDrinksDrunk.Count >= 1);

        Assert.Equal((Potion, 25u), r.Caster.HealthDrinksDrunk[0]);
        Assert.Equal(1, Lines(r, "emergency: potion: check before: Potion of Healing 0x80000270 (+25, 3 held of 3 health drink(s)) at health 20 of 170 (150 missing: the biggest that fits), mana "));
        Assert.True(Lines(r, "the cheapest heal offered costs 10, incoming ") >= 1); // the mana (over 380) pays the heal: the drink goes first all the same
        Assert.Equal(1, Lines(r, " a second: the next hit kills: a drink neither fizzles nor is resisted"));
        Assert.Equal(1, Lines(r, "health 20 then 45 of 170"));
    }

    [Fact]
    public void When_the_hits_would_kill_her_before_one_heal_lands_a_drink_goes_first_and_otherwise_the_heal_does()
    {
        Assert.Equal(5, HealthDrinks.DesperateSeconds); // one heal's cast (2.5 s) and one drink (2.5 s)
        Assert.True(HealthDrinks.IsDesperate(60, 12)); // 12 a second takes 60 in 5 s
        Assert.False(HealthDrinks.IsDesperate(61, 12));
        Assert.False(HealthDrinks.IsDesperate(60, 0)); // no hits: never

        var r = Rig(mana: 400, health: 170, (Tincture, 2), (Potion, 3));
        var scenario = r.Scenario();
        r.Run(scenario, 1, 0.1);
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 31, 4, false, 0));
        r.Run(scenario, 0.2, 0.1);
        r.Caster.SetHealth(60); // over the biggest hit (31), so the next hit does not kill; the rig's one hit reads as over 12 a second

        r.Run(scenario, 30, 0.1, () => r.Caster.HealthDrinksDrunk.Count >= 1);

        Assert.Equal((Tincture, 50u), r.Caster.HealthDrinksDrunk[0]); // with both kinds held the pick is still the biggest that fits
        Assert.Equal(1, Lines(r, " a second: the hits of the last seconds would kill her before one heal could land: a drink before the heal"));
    }

    [Fact]
    public void A_refused_drink_is_named_and_two_in_a_row_set_the_drinks_aside_for_ten_seconds()
    {
        var r = Rig(mana: 0, health: 80, (Potion, 3));
        r.Caster.BusyHealthDrinks = 2;
        var scenario = r.Scenario();

        r.Run(scenario, 6, 0.1);

        Assert.Empty(r.Caster.HealthDrinksDrunk);
        Assert.Equal(2, Lines(r, "the Potion of Healing restored nothing"));
        Assert.Equal(1, Lines(r, "1 in a row: the heal goes now and the drink is tried again"));
        Assert.Equal(1, Lines(r, "2 in a row: the health drinks are set aside 10 s and the heal decides alone"));

        r.Run(scenario, 20, 0.1, () => r.Caster.HealthDrinksDrunk.Count >= 1);

        Assert.Equal((Potion, 25u), r.Caster.HealthDrinksDrunk[0]); // tried again after the ten seconds: never a loop, never given up
    }

    [Fact]
    public void An_open_corpse_is_closed_and_checked_before_the_emergency_drinks_M6_14_recall_unblock()
    {
        // M6 14 recall unblock (live 2026-11-14 17:31:54 UTC: the Health Tonic waited its turn behind the Use lane and a cast's hold): the emergency's drink goes through the way out's preemption: the open corpse is closed first and
        // the server's answer checked, then the drink.
        const uint Corpse = 0x80000F73;
        var r = Rig(mana: 0, health: 80, (Tincture, 2));
        var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker Slave");
        r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis, isUpdate: false);
        r.R.H.R.Reducer.OnViewContents(r.R.H.R.Context, new GameEventHeader(Lifestone.Client.Tests.Session.WorldActionRig.Player, 3000, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, []));
        Assert.Equal(Corpse, r.R.H.State.Containers.ViewedGuid);
        var scenario = r.Scenario();
        var answered = 0;
        uint stamp = 3000;

        r.Run(scenario, 30, 0.1, () =>
        {
            var closes = r.R.H.Net.Of(Lifestone.Client.Tests.Session.FakeSendKind.NoLongerViewingContents);
            for (; answered < closes.Count; answered++)
            {
                r.R.H.R.Reducer.OnCloseGroundContainer(r.R.H.R.Context, new GameEventHeader(Lifestone.Client.Tests.Session.WorldActionRig.Player, ++stamp, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(closes[answered].Guid));
            }

            return r.Caster.HealthDrinksDrunk.Count >= 1;
        });

        var sends = r.R.H.Net.Sends;
        var close = sends.FindIndex(s => s.Kind == Lifestone.Client.Tests.Session.FakeSendKind.NoLongerViewingContents && s.Guid == Corpse);
        var drink = sends.FindIndex(s => s.Kind == Lifestone.Client.Tests.Session.FakeSendKind.Use && s.Guid == ScriptedCaster.HealthDrinkGuid);
        Assert.True(close >= 0 && drink > close, $"the close (send {close}) goes before the drink (send {drink})");
        Assert.Equal([(Tincture, 50u)], r.Caster.HealthDrinksDrunk);
        Assert.Equal(0u, r.R.H.State.Containers.ViewedGuid);
        Assert.Equal(1, Lines(r, "emergency: preempt: check before: the ground container 0x80000F73 is open"));
        Assert.Equal(1, Lines(r, "the view is closed, the Use lane its open held is free: the emergency's drink or heal goes now"));
    }
}
