using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="FightTally"/> (M3 3.1 s0): the spell-damage lines in the forms ACE's <c>SpellProjectile.cs</c> writes (lines 810-845, chat type Magic 0x07), the
/// kills, hits taken, evades and the experience delta, all through the real reducer so the chat type the tally reads is the one the reducer keeps.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class FightTallyTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const long BaseXp = 1_000_000;
    private static uint _sequence;

    [Fact]
    public void A_plain_hit_is_a_damage_line_dealt()
    {
        var (h, tally) = Started();
        Magic(h, "You scorch Tusker Slave for 87 points with Flame Bolt VII.");

        Assert.Equal(87, tally.DamageDealt);
        Assert.Equal(1, tally.DamageLinesDealt);
        Assert.Equal(0, tally.CriticalsDealt);
        Assert.Equal(0, tally.UnparsedMagicLines);
    }

    [Theory]
    [InlineData("Critical hit! You burn Tusker Guard for 250 points with Flame Bolt VII.", 250, 1)]
    [InlineData("Overpower! You hit Tusker Slave for 40 points with Flame Bolt I.", 40, 0)]
    [InlineData("Critical hit! Overpower! Sneak Attack! You incinerate Drudge Ravener for 301 points with Flame Bolt VII.", 301, 1)]
    [InlineData("You hit Tusker Slave for 12 points with Flame Bolt II. Your critical hit was avoided with their augmentation!", 12, 0)]
    public void Prefixes_and_the_crit_protection_suffix_are_read_around_the_amount(string line, long damage, int crits)
    {
        var (h, tally) = Started();
        Magic(h, line);

        Assert.Equal(damage, tally.DamageDealt);
        Assert.Equal(1, tally.DamageLinesDealt);
        Assert.Equal(crits, tally.CriticalsDealt);
        Assert.Equal(0, tally.UnparsedMagicLines);
    }

    [Fact]
    public void A_drain_is_counted_apart_from_damage()
    {
        var (h, tally) = Started();
        Magic(h, "With Stamina Drain I you drain 30 points of stamina from Tusker Slave.");

        Assert.Equal(30, tally.DrainDealt);
        Assert.Equal(0, tally.DamageDealt);
        Assert.Equal(0, tally.DamageLinesDealt);
        Assert.Equal(0, tally.UnparsedMagicLines);
    }

    [Fact]
    public void The_defender_forms_are_damage_and_drain_taken()
    {
        var (h, tally) = Started();
        Magic(h, "Drudge Ravener hits you for 22 points with Acid Streak V.");
        Magic(h, "Critical hit! Virindi Servant scorches you for 41 points with Flame Bolt IV. Your augmentation allows you to avoid a critical hit!");
        Magic(h, "Virindi Master casts Mana Drain III and drains 18 points of your mana.");

        Assert.Equal(63, tally.SpellDamageTaken);
        Assert.Equal(63, tally.DamageTaken);
        Assert.Equal(18, tally.DrainTaken);
        Assert.Equal(0, tally.DamageDealt);
        Assert.Equal(0, tally.UnparsedMagicLines);
    }

    [Theory]
    [InlineData("You cast Flame Bolt VII on Tusker Slave")]
    [InlineData("Your movement disrupted spell casting!")]
    [InlineData("The spell Strength Self VII on Mage has expired.")]
    [InlineData("You hit Tusker Slave for many points with Flame Bolt.")]
    [InlineData("You hit Tusker Slave for 12 points with Flame Bolt")]
    [InlineData("")]
    public void An_unrelated_magic_line_is_counted_and_never_guessed(string line)
    {
        var (h, tally) = Started();
        Magic(h, line);

        Assert.Equal(1, tally.UnparsedMagicLines);
        Assert.Equal(0, tally.DamageDealt);
        Assert.Equal(0, tally.SpellDamageTaken);
        Assert.Equal(0, tally.DrainDealt);
        Assert.Equal(0, tally.DrainTaken);
    }

    [Theory]
    [InlineData("Tusker Slave resists your spell")]
    [InlineData("You resist the spell cast by Tusker Slave")]
    public void A_resist_line_is_counted_as_a_resist_and_not_as_unparsed(string line)
    {
        Assert.Equal(MagicLineKind.Resist, FightTally.Classify(line, out var amount, out _));
        Assert.Equal(0, amount);

        var (h, tally) = Started();
        Magic(h, line);
        Assert.Equal((1, 0), (tally.Resists, tally.UnparsedMagicLines));
        tally.Reset();
        Assert.Equal(0, tally.Resists);
    }

    [Theory]
    [InlineData(" resists your spell")]
    [InlineData("You resist the spell cast by ")]
    [InlineData("Someone resists your spells")]
    public void A_line_that_only_looks_like_a_resist_stays_unparsed(string line)
    {
        Assert.Equal(MagicLineKind.Unparsed, FightTally.Classify(line, out _, out _));
    }

    [Fact]
    public void A_line_the_client_composed_is_not_read_as_the_servers_magic_line()
    {
        var (h, tally) = Started();
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("x", 7)); // a server line that matches nothing: counted
        Assert.Equal(1, tally.UnparsedMagicLines);
        // The reducer's expiry notice is ChatKind.Notice in type 7: the tally ignores it (ReducerEnchantmentTests pins that the line is made).
        var before = tally.UnparsedMagicLines;
        h.Bus.Publish(new ChatReceived(new ChatLine(0, ChatKind.Notice, 7, null, 0, "Strength Self VII has expired.")));
        Assert.Equal(before, tally.UnparsedMagicLines);
    }

    [Fact]
    public void Only_the_magic_chat_type_is_read()
    {
        var (h, tally) = Started();
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("You scorch Tusker Slave for 87 points with Flame Bolt VII.", 0x16));
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("You scorch Tusker Slave for 87 points with Flame Bolt VII.", 0));

        Assert.Equal(0, tally.DamageDealt);
        Assert.Equal(0, tally.UnparsedMagicLines);
        Assert.Equal(7u, FightTally.MagicChatType);
    }

    [Fact]
    public void A_kill_counts_and_keeps_its_text()
    {
        var (h, tally) = Started();
        Assert.Null(tally.LastKillText);
        Send(h, AceM3Writers.KillerNotification(Player, ++_sequence, "You flatten Tusker Slave's body with the force of your assault!"));
        Send(h, AceM3Writers.KillerNotification(Player, ++_sequence, "You have killed Tusker Guard!"));

        Assert.Equal(2, tally.Kills);
        Assert.Equal("You have killed Tusker Guard!", tally.LastKillText);
    }

    [Fact]
    public void Hits_taken_add_their_damage_and_evades_count()
    {
        var (h, tally) = Started();
        Send(h, AceM3Writers.DefenderNotification(Player, ++_sequence, "Tusker Slave", 4, 0.1, 35, 4, false, 0));
        Send(h, AceM3Writers.DefenderNotification(Player, ++_sequence, "Tusker Slave", 4, 0.1, 25, 4, true, 0));
        Send(h, AceM3Writers.EvasionDefenderNotification(Player, ++_sequence, "Tusker Slave"));
        Magic(h, "Drudge Ravener hits you for 22 points with Acid Streak V.");

        Assert.Equal(2, tally.HitsTaken);
        Assert.Equal(1, tally.Evades);
        Assert.Equal(60 + 22, tally.DamageTaken);
        Assert.Equal(22, tally.SpellDamageTaken);
    }

    [Fact]
    public void Experience_is_the_total_experience_delta_from_the_baseline()
    {
        var (h, tally) = Started();
        Assert.Equal(0, tally.XpGained);

        Update(h, 1, BaseXp + 30_000);
        Assert.Equal(30_000, tally.XpGained);
        Update(h, 2, BaseXp + 60_000);
        Assert.Equal(60_000, tally.XpGained);

        // The available experience is another quality and does not move it.
        h.Reducer.OnPrivateUpdate(h.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt64, 3, PlayerQualities.AvailableExperienceProperty, 5, default, default, default));
        Assert.Equal(60_000, tally.XpGained);
    }

    [Fact]
    public void A_baseline_taken_before_the_description_is_the_first_value_seen()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        using var tally = new FightTally();
        tally.Start(new View(h));

        Describe(h);
        Assert.Equal(0, tally.XpGained);
        Update(h, 1, BaseXp + 500);
        Assert.Equal(500, tally.XpGained);
    }

    [Fact]
    public void Reset_zeroes_everything_and_retakes_the_baseline_and_dispose_unsubscribes()
    {
        var (h, tally) = Started();
        tally.NoteCast();
        tally.NoteCast();
        tally.NoteMiss();
        Magic(h, "You scorch Tusker Slave for 87 points with Flame Bolt VII.");
        Magic(h, "Tusker Slave resists your spell");
        Send(h, AceM3Writers.KillerNotification(Player, ++_sequence, "You have killed Tusker Guard!"));
        Update(h, 1, BaseXp + 1_000);
        Assert.Equal((2, 1), (tally.Casts, tally.Misses));

        tally.Reset();

        Assert.Equal(0, tally.Casts + tally.Misses + tally.Kills + tally.DamageLinesDealt + tally.UnparsedMagicLines + tally.HitsTaken + tally.Evades);
        Assert.Equal(0, tally.DamageDealt + tally.DamageTaken + tally.XpGained);
        Assert.Null(tally.LastKillText);
        Update(h, 2, BaseXp + 1_300);
        Assert.Equal(300, tally.XpGained);

        tally.Dispose();
        Assert.False(tally.Running);
        Magic(h, "You scorch Tusker Slave for 87 points with Flame Bolt VII.");
        Assert.Equal(0, tally.DamageDealt);
    }

    [Fact]
    public void Classifying_a_line_allocates_nothing()
    {
        string[] lines =
        [
            "Critical hit! You scorch Tusker Slave for 87 points with Flame Bolt VII.",
            "Drudge Ravener hits you for 22 points with Acid Streak V.",
            "With Stamina Drain I you drain 30 points of stamina from Tusker Slave.",
            "Tusker Slave resists your spell",
        ];
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                foreach (var line in lines)
                {
                    FightTally.Classify(line, out _, out _);
                }
            }
        });
    }

    private static (ReducerHarness H, FightTally Tally) Started()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        Describe(h);
        var tally = new FightTally();
        tally.Start(new View(h));
        return (h, tally);
    }

    private static void Describe(ReducerHarness h) =>
        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(1, BaseXp).Add(2, 10) });

    private static void Magic(ReducerHarness h, string text) => h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg(text, 7));

    private static void Update(ReducerHarness h, byte sequence, long total) =>
        h.Reducer.OnPrivateUpdate(h.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt64, sequence, PlayerQualities.TotalExperienceProperty, total, default, default, default));

    private static void Send(ReducerHarness h, byte[] message) =>
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(h.Context, message, h.Reducer).Outcome);

    private sealed class View(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => null!;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog { get; } = new();

        public LinkStats Link => default;
    }
}
