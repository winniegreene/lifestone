using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M6 item 16, the one engine (docs/automation-engine.md; m6-plan.md D139): the automation's decisions were built once
/// per process (the fight, the travel legs, the quest steps, the trips, the death handler). Each concern below is to
/// have ONE owner; until its stage lands, this test counts the lines that still decide it outside an owner and holds
/// every file at its ceiling (<c>engine-ratchets.json</c>, beside this file). A new copy fails here; a stage that removes
/// copies lowers the ceilings it earned. The pattern is <c>RungOwnerRepositoryTests</c>' (one owner of the rung, D93).
/// </summary>
/// <remarks>
/// The count is by line, as that test counts: a line whose first characters are <c>//</c> is skipped, every other line
/// that matches is one. The ceilings are the larger of each file's count on m6/14, m6/15 and m6/long-run when the ratchet
/// was written (2026-11-17), so the three branches pass as they stand. The melee and archer paths (<c>HuntScenario</c>
/// and its helpers) stay as they are (the owner, 2026-11-17): their lines are counted and stay at their ceilings.
/// </remarks>
public sealed class EngineRatchetTests
{
    private const string PinsPath = "tests/Lifestone.Client.Tests/Repository/engine-ratchets.json";
    private const string Session = "src/Lifestone.Client/Session/";

    /// <summary>One concern: what to scan, the lines that count, and where the one owner is to live.</summary>
    private sealed record Ratchet(string Scope, Regex Lines, string Stage, string Owner);

    private static readonly Dictionary<string, Ratchet> Ratchets = new(StringComparer.Ordinal)
    {
        ["ThreatDetectors"] = new("src/", Rx(@"CombatEventKind\.Defense(Hit|Evaded)|new (AttackWatch|HostileWatch)\("), "E2", "the threat board: one read of who attacks her and who is in reach"),
        ["HealDeciders"] = new("src/", Rx(@"HealPick\.Choose(Now)?\(|new FightEmergency\("), "E3", "the survival layer's heal decision"),
        ["ManaDeciders"] = new("src/", Rx(@"ManaOrder\.Decide\(|ManaLoopRule\.|RechargeRule\.|VitalsPolicy\.Plan\("), "E3", "the survival layer's mana decision"),
        ["WayOutLine"] = new("src/", Rx(@"ManaWayOut\.LineOf\("), "E3", "the survival layer's one way-out line"),
        ["BuffPasses"] = new("src/", Rx(@"new BuffCycle\("), "E4", "the one owner of a buff pass"),
        ["BoltPickers"] = new("src/", Rx(@"WarBoltPick\.Choose\w*\(|WarSpellPicker\.|FightPicker\."), "E4", "the one bolt pick"),
        ["VendorSends"] = new(Session, Rx(@"Actions\.(Buy|Sell|OpenVendor)\("), "E5", "the quartermaster's vendor counter"),
        ["WieldSends"] = new(Session, Rx(@"Actions\.(Wield|Unwield)\("), "E5", "the quartermaster's wear service"),
        ["EquipJudges"] = new("src/", Rx(@"new SelfEquipJudge\("), "E5", "the quartermaster's one judge factory"),
        ["TripVerdict"] = new("src/", Rx(@"new LogisticsCounters\("), "E5", "the quartermaster's one trip verdict"),
        ["PackViews"] = new(Session, Rx(@"FreeSlots\(|CoinProperty|CoinValue|EncumbranceCapacity\("), "E5", "the quartermaster's views (counts, purse, free slots, capacity)"),
        // M6 16 E5c: the lines that still answer "may this be sold or dropped" outside the verdict: the table asked by itself, the wire's unsellable test as a sale filter, the sale's old filters and the field's and the trigger's own lists
        ["SaleDeciders"] = new(Session, Rx(@"PackRuleBook\.Classify\(|VendorStock\.Unsellable\(|\bNotSellable\(|\bIsKeptAside\(|\bWhyKept\(|_notForSale\b|\bSaleItemsBefore\("), "E5", "the quartermaster's sale verdict (SaleVerdict.Of): the one answer to what may be sold or dropped"),
        ["CreatureReaders"] = new("src/", Rx(@"Creature(Table|Resists|Skills|Awareness|Behaviour)\."), "E1", "the bestiary's one reader"),
        ["NameAsMode"] = new("src/", Rx(@"IsClimber\b|ClimberName|PicksByNet\("), "E6", "the context object: policy is data on the character, never her name"),
        ["ContextSideChannels"] = new("src/", Rx(@"Facts\.(RunOnly|FightBackReserve|FightBackTarget|AbandonApproach|WearJudge|LoreWanted)\b|Arguments\.(ContainsKey|TryGetValue)\("), "E6", "the context object and the combat brief"),
    };

    private static readonly Lazy<Dictionary<string, Dictionary<string, int>>> Pins = new(static () =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(CheckoutFiles.ReadText(PinsPath))
        ?? throw new InvalidOperationException($"{PinsPath} holds no ceilings."));

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    /// <summary>The concerns, for the theory's rows.</summary>
    public static TheoryData<string> Names() => [.. Ratchets.Keys];

    private static Dictionary<string, int> Count(Ratchet ratchet)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in CheckoutFiles.CSharpUnder(ratchet.Scope))
        {
            var n = 0;
            foreach (var line in CheckoutFiles.Lines(CheckoutFiles.ReadText(path)))
            {
                var code = line.TrimStart();
                if (!code.StartsWith("//", StringComparison.Ordinal) && ratchet.Lines.IsMatch(code))
                {
                    n++;
                }
            }

            if (n > 0)
            {
                found[path] = n;
            }
        }

        return found;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void No_file_decides_a_concern_more_often_than_its_ceiling(string name)
    {
        var ratchet = Ratchets[name];
        Assert.True(Pins.Value.TryGetValue(name, out var ceilings), $"{PinsPath} has no ceilings for {name}.");

        var over = Count(ratchet)
            .Where(f => f.Value > ceilings!.GetValueOrDefault(f.Key))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"  {f.Key}: {f.Value} line(s), ceiling {ceilings!.GetValueOrDefault(f.Key)}")
            .ToArray();

        Assert.True(
            over.Length == 0,
            $"{name}: a new copy of a decision that is to have one owner ({ratchet.Owner}; stage {ratchet.Stage} of docs/automation-engine.md).\n"
            + "Call the shared piece instead of deciding it here. Only a merge of work written before the ratchet raises a ceiling, by name in its message.\n"
            + string.Join("\n", over));
    }

    [Fact]
    public void Every_ceiling_belongs_to_a_concern_the_test_scans()
    {
        Assert.Equal(Ratchets.Keys.Order(StringComparer.Ordinal), Pins.Value.Keys.Order(StringComparer.Ordinal));
        Assert.All(Pins.Value, concern => Assert.All(concern.Value, file =>
        {
            Assert.StartsWith(Ratchets[concern.Key].Scope, file.Key, StringComparison.Ordinal);
            Assert.True(file.Value > 0, $"{concern.Key}: {file.Key} has a ceiling of {file.Value}; remove the row instead.");
        }));
    }

    [Fact]
    public void The_ceilings_only_go_down()
    {
        // The totals when the ratchet was written (2026-11-17, the larger of m6/14, m6/15 and m6/long-run). A stage that removes
        // copies lowers its files' ceilings and the number here; nothing raises one but a named merge of older work.
        var written = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // M6 16 E2a: 23 when written; the threat board's own feed is the one new reader of the defence notices, and its one read is counted (ThreatBoard.cs, 1 line): 24. Every
            // detector still reads for itself beside it (the board is in shadow), so nothing is lowered yet; the stage that moves the detectors onto the board lowers their files.
            ["ThreatDetectors"] = 24,
            ["HealDeciders"] = 5,
            ["ManaDeciders"] = 20, // M6 16 Q6: the survival layer's own file (SurvivalRule.ManaLoop.cs, 2: the fixed loop and the loop by the sum it decides by) is the one new row; the fight's loop 11 to 9 (a short-of-press read once, a comment's false match): 20 still
            ["WayOutLine"] = 3,
            ["BuffPasses"] = 9,
            ["BoltPickers"] = 10, // M6 16 Q8: 11; the harm-heal station's fight-back asks the skirmish's pick (HealStations.cs 1 to 0)
            ["VendorSends"] = 22, // M6 16 E5b: 25 when written; the town trip's buy and its note change, the quest step's, the buy-trip's and the lockpick's buy moved onto the vendor counter, whose own two sends (the buy, the note's sale) are counted
            ["WieldSends"] = 13, // M6 16 E5e1: 40 when written; the town trip's gear and leather passes, the worn items' rewear, the quest steps, the Pathwarden station, the recovery table, the worn restorer, the death handler, the fight's wear on loot and the bind leg send through the wear service, whose own two sends are counted (Quartermaster/Wardrobe.Wield.cs); E5e2: 15 to 13, the fight's replace in hand sends through it too (FightScenario.LootGear.cs, 2); the build scenarios and the fighters' files stay
            ["EquipJudges"] = 1, // M6 16 E5e1: 6 when written; the town trip's, the gear keeps', the worn restorer's and the three rearm judges (and the fight's own, which the scan never counted) ask the one factory, whose one construction is counted (Quartermaster/Wardrobe.cs)
            ["TripVerdict"] = 1, // M6 16 E5d: 7 when written; the outer loop, the fight's floors, the logistics station, the town trip, the buy-trip and the panel's two ask the session's trip desk, whose one construction is counted (Quartermaster/TripDesk.cs: the counters that decide and their reading twin are built by that one line)
            ["PackViews"] = 46, // M6 16 Q10b: 90 to 46, the free-slot reads of the stations, the loot rule, the trackers and the layout moved onto PackView (Quartermaster/PackView.cs), whose two calls and the layout's per-job room are not counted by name. M6 16 E5a1: 101 when written; the coin reads of the trip trigger, the learn holds, the town stock, the start-up audit and the villa schedule moved onto the quartermaster's purse read (PurseView), which reads the coin through the stations' one read and has no line of its own here
            // M6 16 E1a: 25 when written; the readers moved onto the bestiary's facade, whose own 9 reads of its sources are counted. M6 16 E1d: the behaviour table is a counted source too
            // (CreatureBehaviour); the facade's count rises by its one read of it and by the land chance the attack times asked the skill table for twice (9 to 11, AttackTiming 6 to 4): 19 still.
            ["CreatureReaders"] = 19,
            ["NameAsMode"] = 17, // the long run (m6/long-run, 2026-11-18): +6 for its own work written before the ratchet: the climber option (AutopilotOptions 3), the climber journeys (JourneyPlans 1 -> 3), the headless climber argument (Program.cs 1). M6 16 Q9: 59 on the pins (48 lines held); every policy a name decided is a read of CharacterPolicy.For(name); what is left is the name list itself (MageTemplate 6), the policy object's one read of it (CharacterPolicy, the owner's own file, a new row), the creation scenario's one named character, a log line's name, the buff profile's label and the Winnie plan's character
            ["SaleDeciders"] = 5, // M6 16 Q11: 13 to 5, the town trip's old sale list (SaleItemsBefore, IsKeptAside, its pack-rule read) and its guard went (ResupplyStation.cs 6 to 1, .SaleVerdict.cs 2 to 0, .PackRules.cs 1 to 0). Before that, M6 16 Q10d: the load line's reader asks the verdict (ResupplyStation.Gear.cs 1 to 0): 13. Before that, M6 16 E5c: written at 23, on m6/14 with E5a2 and E5c (the verdict's own one read of the table is counted; the melee and archer paths' two lines stay). M6 16 E5c2: the trip trigger's estimate asks the verdict (LogisticsCounters.cs 4 to 0), the start-up audit's sale gate too (StartupAudit.cs 1 to 0), the field's make-room rule too (CantripRoom.cs 3 to 0), the item mana drain's never-list too (ItemManaRefillStep.cs 1 to 0): 14
            ["ContextSideChannels"] = 66,
        };

        Assert.All(Pins.Value, concern => Assert.True(
            concern.Value.Values.Sum() <= written[concern.Key],
            $"{concern.Key}: the ceilings sum to {concern.Value.Values.Sum()}, over the {written[concern.Key]} the ratchet was written at."));
    }
}
