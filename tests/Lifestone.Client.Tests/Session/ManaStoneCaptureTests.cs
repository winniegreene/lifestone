using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 7 S2c, from the rehearsal of 2026-10-29 (cache/soak/20261029T135811Z-rehearsal, 14:07:39-14:07:42 UTC). (1) The capture's line, "The Mana Stone drains 5,868 points of mana from the Acid Sceptre.\nThe Acid Sceptre is destroyed." (kind System,
/// type 0; ManaStone.cs:108-110), is the use's success, not "nothing happened"; the stone took the Greater Mana Stone's efficiency 2.0 times the 2,934 the appraisal read, which is the figure the drain-vs-sell weighing uses. (2) The caster pick is the
/// single best of every caster held: it swaps the Fire Sceptre in, never the Electric Sceptre, and a caster off the plan's element never wins a tie.
/// </summary>
public sealed class ManaStoneCaptureTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Stone = 0x80000F3A;
    private const uint AcidSceptre = 0x80004224;
    private const string RehearsalLine = "The Mana Stone drains 5,868 points of mana from the Acid Sceptre.\nThe Acid Sceptre is destroyed.";

    [Fact]
    public void The_rehearsals_capture_line_confirms_the_use_with_the_mana_drained()
    {
        var r = new WorldActionRig();
        r.Describe([new ContentDesc(Stone, 0), new ContentDesc(AcidSceptre, 0)], []);
        r.Item(Stone, Player);
        r.Item(AcidSceptre, Player);
        var handle = r.Api.UseManaStone(Stone, AcidSceptre);
        r.Tick();

        r.H.ServerMessage(RehearsalLine, 0); // ChatMessageType.Broadcast, as the rehearsal logged it (kind=System type=0)
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Equal("drained 5868 mana from the Acid Sceptre into the stone; the Acid Sceptre is destroyed", outcome.Detail);
    }

    [Fact]
    public void The_weighing_uses_the_mana_the_stone_takes_which_is_the_line_the_server_sends()
    {
        var greater = new HeldStone(Stone, "Greater Mana Stone", null, ItemManaKeeper.EfficiencyByWcid[2436]);
        var acid = new DrainTarget(AcidSceptre, "Acid Sceptre", 2934, 2934, 7030, Payout: 6327); // the appraisal's ItemCurMana, the rehearsal's plan line; 0.9 x 7,030

        var weighed = ItemManaKeeper.Weigh(greater, acid);

        Assert.Equal(5868, weighed.Captured); // round(2.0 x 2,934) (ManaStone.cs:108): the server's "drains 5,868", not a half read
        Assert.Equal(10562, weighed.Worth);
        Assert.True(weighed.Drain);
        Assert.Equal("gear mana: weigh 'Acid Sceptre' 0x80004224: 2934 mana -> 5868 in the stone, worth 10562 pyreals at 1.8 a point, against a payout of 6327: drain", weighed.ToString());
    }

    private static Appraisal Caster(uint guid, uint element, double bonus, double manaConversion) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable,
            true,
            new Dictionary<uint, int> { [45] = (int)element, [158] = 2, [159] = 34, [160] = 290 }.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            new Dictionary<uint, double> { [AppraisalIds.ElementalDamageMod] = bonus, [144] = manaConversion }.ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    private sealed class Mage : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => 330;

        public int? Base(uint skill) => 311;
    }

    private static GearCandidate Held(uint guid, string name, uint element, double bonus, double mc) => new(guid, name, SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, 50, Caster(guid, element, bonus, mc));

    [Fact]
    public void The_pick_is_the_one_best_caster_held_and_the_rehearsals_Electric_Sceptre_never_wins_a_tie_off_element()
    {
        var judge = new SelfEquipJudge([], SelfEquipJudge.ElementOf("Flame Bolt"), _ => null);
        var wand = new WornPiece(0x80004907, "Wand", SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, Caster(0x80004907, 0, 1.0, 0));
        GearCandidate[] held =
        [
            Held(0x80000808, "Electric Sceptre", 0x40, 1.01, 0.04), // swapped in at 14:07:39.8 on mana conversion alone
            Held(0x80004CCE, "Frost Sceptre", 0x8, 1.03, 0.05),
            Held(0x8000178B, "Fire Sceptre", 0x10, 1.03, 0.03),
            Held(0x80003EB7, "Fire Sceptre", 0x10, 1.03, 0.04), // fire x1.03 and the better mana conversion of the two
        ];

        var choice = judge.BestCaster(held, [wand], new Mage(), out var why);

        Assert.NotNull(choice);
        Assert.Equal(SelfEquipKind.Swap, choice.Kind);
        Assert.Equal(0x80003EB7u, choice.Guid);
        Assert.Equal(0x80004907u, choice.Replaces);
        Assert.Equal("gear: caster pick: 'Fire Sceptre' 0x80003EB7, the best of 4 usable caster(s) held, over 'Wand' 0x80004907", why);

        Assert.False(judge.Beats(held[0].Appraisal, wand.Appraisal)); // off the plan's element: no win on a tie, whatever its mana conversion
        Assert.Null(judge.BestCaster([held[0], held[1]], [wand], new Mage(), out var none)); // neither is fire: the Wand is kept
        Assert.StartsWith("gear: caster pick: keep 'Wand' 0x80004907", none, StringComparison.Ordinal);

        var electricHeld = new WornPiece(0x80000808, "Electric Sceptre", SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, held[0].Appraisal);
        Assert.Equal(0x80003EB7u, judge.BestCaster(held[1..], [electricHeld], new Mage(), out _)!.Guid); // from the rehearsal's end state, the next visit puts the fire sceptre in
    }
}
