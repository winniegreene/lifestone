using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 combine3: the autopilot's reads of a hunt-until count the hunted item's equivalents along the quest's no-roll chain, as the fight's until and the quest station do (<see cref="QuestCombineChain.HuntHeld"/>): the errand's
/// "a hunt that gained" (<c>AutopilotScenario.JobNeeds.HuntHeld</c>) and the sell-first room (<see cref="ErrandRoomRule.GainOf"/>), whose room is the chain's combined end state and the most objects on the way, not the loose count.
/// </summary>
public sealed class ErrandRoomCombineTests
{
    private const uint Mote = 6353, Sliver = 6355, Nugget = 6354, Bar = 6329;

    private static int HeldBarAndNugget(uint wcid) => wcid switch { Bar => 1, Nugget => 1, _ => 0 };

    [Fact]
    public void A_held_bar_and_nugget_read_twelve_of_the_Atlan_wands_sixteen_motes()
    {
        var quest = QuestCatalog.Get("atlan-wand");
        var hunt = quest.Steps.Single(s => s.Kind == QuestStepKind.HuntUntil);

        Assert.Equal(12, QuestCombineChain.HuntHeld(quest, hunt, HeldBarAndNugget)); // 8 + 4: the read JobNeeds' "a hunt that gained" now makes; the loose count read 0
        Assert.Equal(0, QuestCombineChain.HuntHeld(quest, hunt, w => w == Sliver ? 0 : 0));
        Assert.Equal(16, QuestCombineChain.HuntHeld(quest, hunt, w => w == Mote ? 16 : 0)); // loose motes still count one each
    }

    [Fact]
    public void The_sell_first_room_for_the_four_still_wanted_is_the_combined_end_and_its_peak_not_sixteen_loose()
    {
        var quest = QuestCatalog.Get("atlan-wand");

        var gain = ErrandRoomRule.GainOf(quest, null, HeldBarAndNugget, _ => (1, 1));

        // 4 motes still wanted, combined as they come: 1 bar and 1 nugget now, 2 bars at the end; on the way at most 5 objects (a mote, a sliver, the nugget, the bar and the new mote before its combine), 3 over the end state;
        // the three no-roll combine steps add nothing (the hunt made their end), the ingot's roll takes the 2 bars and gives 1
        Assert.Equal(3, gain.Slots);
        Assert.Equal(3, gain.Items);
        Assert.Contains("hunt-until 16 Pyreal Mote (6353) (12 held as equivalents; combined as it goes: 2 object(s) now, 2 at its end, at most 5 on the way)", gain.What, StringComparison.Ordinal);

        var (end, peak) = QuestCombineChain.Simulate(QuestCombineChain.Of(quest, Mote), HeldBarAndNugget, 16);
        Assert.Equal([0, 0, 0, 2], end);
        Assert.Equal(5, peak);

        var loose = ErrandRoomRule.GainOf(quest, null, _ => 0, _ => (1, 1));
        Assert.True(loose.Slots < 16, $"from nothing the hunt holds at most one unpaired item a link and the new one, not 16 loose motes: {loose.Slots}");
    }
}
