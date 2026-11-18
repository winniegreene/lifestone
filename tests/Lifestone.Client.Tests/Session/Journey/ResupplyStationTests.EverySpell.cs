using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 (the owner, 2026-11-16: she buys every spell, eventually when affordable): the every-spell step on a real trip against a scripted ACE. The level-6 climber of the learn tests (War Magic 106 standing: level II of a War line at
/// 0.95) on Holtburg's outskirts, the rule on: beyond the plan's Flame Bolt II she buys Frost Bolt II and Acid Stream II (250 pyreals each, the Apprentice Scrivener of Item and War Magic 20216) and reads them, check, act, check: the book
/// gains Frost Bolt II; ACE refuses Acid Stream II (the script's "not skilled enough"), so it is named loud, kept from the sale and not bought on the next trip.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint FrostBoltII = 70;
    private const uint AcidStreamII = 59;

    /// <summary>The SpellTable's rows of two War lines, I to VI (Frost Bolt 28, 70-74; Acid Stream 58-63; powers 1, 50, 100, 150, 200, 250).</summary>
    private static Dictionary<uint, SpellInfo> TwoWarLines()
    {
        uint[] powers = [1, 50, 100, 150, 200, 250];
        var spells = new Dictionary<uint, SpellInfo>();
        foreach (var line in new[] { "Frost Bolt", "Acid Stream" })
        {
            foreach (var spell in ScrivenerStock.Default.LineSpells(line))
            {
                spells[spell.SpellId] = WorldActionRig.Spell(spell.SpellId, SpellType.Projectile) with { Name = spell.Name, School = MagicSchool.WarMagic, Power = powers[spell.Level - 1] };
            }
        }

        return spells;
    }

    /// <summary>ACE's answers to a scroll's use (Scroll.ActOnUse, Scroll.cs 45-127 @95ef426d): any stock scroll is learned, Acid Stream II is refused as a skill short read, its scroll kept in the pack.</summary>
    /// <param name="seen">The sends already answered (a later trip's server starts after the earlier trip's).</param>
    private static Action EverySpellServer(Home home, List<uint> learned, List<uint> refused, int seen = 0)
    {
        uint sequence = 60;
        return () =>
        {
            home.Shops.Pump();
            var sends = home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                var send = sends[seen];
                if (send.Kind != FakeSendKind.Use || !home.Vendor.State.Objects.TryGet(send.Guid, out var item) || ScrivenerStock.Default.ByScroll(item.Weenie.WeenieClassId) is not { } spell)
                {
                    continue;
                }

                if (spell.SpellId == AcidStreamII)
                {
                    refused.Add(spell.SpellId);
                    home.Vendor.H.ServerMessage("You are not skilled enough in War Magic to learn this spell.\n");
                    home.Vendor.UseDone();
                    continue;
                }

                learned.Add(spell.SpellId);
                home.Vendor.R.Raw(AceM3Writers.MagicUpdateSpell(VendorRig.Player, ++sequence, (ushort)spell.SpellId));
                home.Vendor.H.ServerMessage($"You learn the {spell.Name} spell.\n");
                home.Vendor.Remove(send.Guid);
                home.Vendor.H.ServerMessage("The scroll is destroyed.");
                home.Vendor.UseDone();
            }
        };
    }

    [Fact]
    public void M6_14_every_spell_buys_and_reads_the_other_war_lines_and_a_scroll_ACE_refuses_is_kept_named_and_never_bought_again()
    {
        var home = OutskirtsHome(warRanks: 56); // War Magic 106 standing: II (power 50) at 0.981
        var spells = TwoWarLines();
        var acid = ScrivenerStock.Default.Spell(AcidStreamII)!;
        var learned = new List<uint>();
        var refused = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: home.Memory), spells, facts: f => f.EverySpell = true);

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 900, pump: EverySpellServer(home, learned, refused)));

        var lines = string.Join(" | ", home.Lines.Where(l => l.Contains("every_spell", StringComparison.Ordinal)));
        Assert.True(home.Logged("every_spell: check before: purse "), lines);
        Assert.True(home.Logged("every_spell: reserve "), lines);
        Assert.True(home.Logged("every_spell: damage Frost Bolt II (70): buy: WarMagic skill 106 (standing), the combat bar 0.95: target Frost Bolt II (power 50, chance 0.981)"), lines);
        Assert.True(home.Logged("every_spell: batch home 20216: 2 scroll(s), 500 pyreals: Acid Stream II, Frost Bolt II"), lines); // one vendor, one batch
        Assert.Contains(run.Visits, v => v.Vendor.Wcid == Apprentice && v.Buys.Any(b => b.Line.Wcid == acid.ScrollWcid));
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FlameBoltII)); // the plan's own line, as before
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FrostBoltII)); // the after-check: the server's update
        Assert.True(home.Logged("every_spell: check after: Frost Bolt II (70) is in the spellbook (the server's update): learned"), lines);
        Assert.Equal([AcidStreamII], refused);
        Assert.True(home.Logged("every_spell: TODO(m6-every-spell-unreadable): Acid Stream II (59): ACE did not teach it (skill-short"), lines); // loud
        Assert.Contains(AcidStreamII, EverySpellMemory.For(home.Memory).Unreadable.Keys);

        var scroll = home.Vendor.State.Objects.All.Single(o => o.Weenie.WeenieClassId == acid.ScrollWcid);
        Assert.Contains("ACE refused its read this session", run.ScrollKeepReason(scroll), StringComparison.Ordinal); // the sale never offers it

        var again = home.Start(new ResupplyOptions(Memory: home.Memory), spells, facts: f => f.EverySpell = true);
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 900, pump: EverySpellServer(home, learned, refused, home.Vendor.H.Net.Sends.Count)));
        Assert.DoesNotContain(again.Visits, v => v.Buys.Any(b => b.Line.Wcid == acid.ScrollWcid)); // never bought twice
        Assert.True(home.Logged("every_spell: damage Acid Stream II (59): refused-before:"), lines);
        Assert.Equal([AcidStreamII], refused); // and not read again this session
    }

    [Fact]
    public void M6_14_with_the_rule_off_a_trip_buys_only_the_plans_scroll()
    {
        var home = OutskirtsHome(warRanks: 56);
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()), TwoWarLines());

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: EverySpellServer(home, [], [])));

        Assert.Equal(FlameBoltIIScroll, run.Visits.Single(v => v.Vendor.Wcid == Apprentice).Buys.Single().Line.Wcid);
        Assert.DoesNotContain(home.Lines, l => l.StartsWith("every_spell", StringComparison.Ordinal));
    }
}
