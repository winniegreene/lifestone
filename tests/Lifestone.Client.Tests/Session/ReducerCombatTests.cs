using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The combat and death notices (M3 1.2 lane s3): GameEvents 0x01B1 to 0x01B4, 0x01AC and 0x01AD, GameMessage 0x019E and the combat mode of 0x02CD, fed to the reducer as ACE writes them
/// and asserted against the line retail's <c>ClientCombatSystem</c> handlers compose (the retail wording is spelled out in each expectation).
/// </summary>
public sealed class ReducerCombatTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Slash = 0x1, Pierce = 0x2, Bludgeon = 0x4, Cold = 0x8, Fire = 0x10, Acid = 0x20, Electric = 0x40, Health = 0x80, Nether = 0x400;

    private static uint _sequence;

    private static ReducerHarness InWorld()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        Assert.Equal(Player, h.State.Self.Guid);
        h.ClearEvents();
        return h;
    }

    private static void Send(ReducerHarness h, byte[] message)
    {
        var result = InboundDispatcher.Dispatch(h.Context, message, h.Reducer);
        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
    }

    private static ChatLine LastLine(ReducerHarness h) => h.State.Chat[h.State.Chat.Count - 1];

    private static string Attack(double percent, uint type, uint damage = 5, bool critical = false, ulong conditions = 0, string name = "Drudge")
    {
        var h = InWorld();
        Send(h, AceM3Writers.AttackerNotification(Player, ++_sequence, name, type, percent, damage, critical, conditions));
        return LastLine(h).Text;
    }

    private static string Defend(double percent, uint type, uint damage = 5, uint location = 0, bool critical = false, ulong conditions = 0, string name = "Drudge")
    {
        var h = InWorld();
        Send(h, AceM3Writers.DefenderNotification(Player, ++_sequence, name, type, percent, damage, location, critical, conditions));
        return LastLine(h).Text;
    }

    // ---- 0x01B1 AttackerNotification

    [Fact]
    public void An_attacker_notification_is_the_you_verb_line_in_chat_type_0x16_and_a_combat_event()
    {
        var h = InWorld();

        Send(h, AceM3Writers.AttackerNotification(Player, 1, "Drudge Skulker", Slash, 0.3, 14, false, 0));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal("You slash Drudge Skulker for 14 points of slashing damage!", line.Text);
        Assert.Equal((ChatKind.System, 0x16u), (line.Kind, line.ChatType));
        Assert.Equal(line, LastLine(h));
        var e = Assert.Single(h.Of<CombatEvent>());
        Assert.Equal(new CombatEvent(CombatEventKind.AttackLanded, "Drudge Skulker", Slash, 14, 0.3, false, -1, false, 0, line.Text), e);
        Assert.Equal(["ChatReceived", "CombatEvent"], h.Names());
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.AttackerNotification));
    }

    [Theory]
    [InlineData(Slash, "scratch", "cut", "slash", "mangle", "slashing")]
    [InlineData(Pierce, "nick", "stab", "impale", "gore", "piercing")]
    [InlineData(Bludgeon, "graze", "bash", "smash", "crush", "bludgeoning")]
    [InlineData(Cold, "numb", "chill", "frost", "freeze", "cold")]
    [InlineData(Fire, "singe", "scorch", "burn", "incinerate", "fire")]
    [InlineData(Acid, "blister", "sear", "corrode", "dissolve", "acid")]
    [InlineData(Electric, "spark", "shock", "jolt", "blast", "electrical")]
    [InlineData(Nether, "scar", "twist", "wither", "eradicate", "nether")]
    public void The_attackers_verb_steps_up_with_the_fraction_of_health_taken(uint type, string v0, string v1, string v2, string v3, string typeName)
    {
        // The steps are retail's: above 0.1, above 0.25, above 0.5 (a fraction exactly on a step stays in the lower one).
        Assert.Equal($"You {v0} Drudge for 5 points of {typeName} damage!", Attack(0.0, type));
        Assert.Equal($"You {v0} Drudge for 5 points of {typeName} damage!", Attack(0.1, type));
        Assert.Equal($"You {v1} Drudge for 5 points of {typeName} damage!", Attack(0.11, type));
        Assert.Equal($"You {v1} Drudge for 5 points of {typeName} damage!", Attack(0.25, type));
        Assert.Equal($"You {v2} Drudge for 5 points of {typeName} damage!", Attack(0.26, type));
        Assert.Equal($"You {v2} Drudge for 5 points of {typeName} damage!", Attack(0.5, type));
        Assert.Equal($"You {v3} Drudge for 5 points of {typeName} damage!", Attack(0.51, type));
        Assert.Equal($"You {v3} Drudge for 5 points of {typeName} damage!", Attack(3.0, type));
    }

    [Theory]
    [InlineData(Health, 0.05, "drain")]
    [InlineData(Health, 0.2, "exhaust")]
    [InlineData(Health, 0.4, "siphon")]
    [InlineData(Health, 0.9, "deplete")]
    public void A_drain_has_verbs_but_no_type_name_so_the_line_reads_of_damage(uint type, double percent, string verb) =>
        Assert.Equal($"You {verb} Drudge for 5 points of damage!", Attack(percent, type));

    [Theory]
    [InlineData(0u, "")]
    [InlineData(0x100u, "")] // stamina: no verbs, no name
    [InlineData(0x200u, "")] // mana
    [InlineData(0x10000000u, "prismatic ")]
    [InlineData(Slash | Pierce, "slashing/piercing ")]
    [InlineData(Slash | Pierce | Bludgeon | Cold | Fire | Acid | Electric | Nether | 0x10000000, "")] // 73 bytes with the terminator: more than DamageTypeToString's 64-byte buffer holds, so the name stays empty
    public void A_type_retail_has_no_verbs_for_is_a_hit_and_the_type_name_joins_its_bits_with_a_slash(uint type, string typeName) =>
        Assert.Equal($"You hit Drudge for 5 points of {typeName}damage!", Attack(0.9, type));

    [Fact]
    public void A_single_point_has_no_s_and_zero_points_has_one()
    {
        Assert.Equal("You scratch Drudge for 1 point of slashing damage!", Attack(0.01, Slash, damage: 1));
        Assert.Equal("You scratch Drudge for 0 points of slashing damage!", Attack(0.0, Slash, damage: 0));
    }

    [Fact]
    public void The_attackers_critical_sneak_reckless_and_protection_pieces_come_in_retails_order_and_spacing()
    {
        Assert.Equal("Critical hit!  You mangle Drudge for 12 points of slashing damage!", Attack(0.7, Slash, 12, critical: true));
        Assert.Equal("Sneak Attack! You cut Drudge for 5 points of slashing damage!", Attack(0.2, Slash, conditions: 4));
        Assert.Equal("Recklessness! You cut Drudge for 5 points of slashing damage!", Attack(0.2, Slash, conditions: 2));
        Assert.Equal(
            "Critical hit!  Sneak Attack! Recklessness! You mangle Drudge for 12 points of slashing damage! Your target's Critical Protection augmentation allows them to avoid your critical hit!",
            Attack(0.7, Slash, 12, critical: true, conditions: 7));
        Assert.Equal(
            "You cut Drudge for 5 points of slashing damage! Your target's Critical Protection augmentation allows them to avoid your critical hit!",
            Attack(0.2, Slash, conditions: 1));
    }

    // ---- 0x01B2 DefenderNotification

    [Fact]
    public void A_defender_notification_names_the_attacker_the_verb_and_the_part_in_chat_type_0x15_and_is_a_combat_event()
    {
        var h = InWorld();

        Send(h, AceM3Writers.DefenderNotification(Player, 1, "Drudge", Slash, 0.3, 14, 4, true, 0));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal("Critical hit! Drudge slashes your lower arm for 14 points of slashing damage!", line.Text);
        Assert.Equal((ChatKind.System, 0x15u), (line.Kind, line.ChatType));
        Assert.Equal(new CombatEvent(CombatEventKind.DefenseHit, "Drudge", Slash, 14, 0.3, true, 4, false, 0, line.Text), Assert.Single(h.Of<CombatEvent>()));
        Assert.Equal(["ChatReceived", "CombatEvent"], h.Names());
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.DefenderNotification));
    }

    [Theory]
    [InlineData(Slash, "scratches", "cuts", "slashes", "mangles")]
    [InlineData(Pierce, "nicks", "stabs", "impales", "gores")]
    [InlineData(Bludgeon, "grazes", "bashes", "smashes", "crushes")]
    [InlineData(Cold, "numbs", "chills", "frosts", "freezes")]
    [InlineData(Fire, "singes", "scorches", "burns", "incinerates")]
    [InlineData(Acid, "blisters", "sears", "corrodes", "dissolves")]
    [InlineData(Electric, "sparks", "shocks", "jolts", "blasts")]
    [InlineData(Nether, "scars", "twists", "withers", "eradicates")]
    [InlineData(Health, "drains", "exhausts", "siphons", "depletes")]
    public void The_defenders_verb_is_the_plural_one_and_steps_up_as_the_attackers_does(uint type, string v0, string v1, string v2, string v3)
    {
        string Tail(string verb) => $"Drudge {verb} your head for 5 points of " + (type == Health ? string.Empty : TypeName(type) + " ") + "damage!";
        Assert.Equal(Tail(v0), Defend(0.05, type));
        Assert.Equal(Tail(v1), Defend(0.2, type));
        Assert.Equal(Tail(v2), Defend(0.4, type));
        Assert.Equal(Tail(v3), Defend(0.8, type));
    }

    private static string TypeName(uint type) => type switch
    {
        Slash => "slashing",
        Pierce => "piercing",
        Bludgeon => "bludgeoning",
        Cold => "cold",
        Fire => "fire",
        Acid => "acid",
        Electric => "electrical",
        Nether => "nether",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    [Fact]
    public void An_unlisted_type_hits()
    {
        Assert.Equal("Drudge hits your head for 3 points of damage!", Defend(0.9, 0, 3));
        Assert.Equal("Drudge hits your head for 1 point of prismatic damage!", Defend(0.9, 0x10000000, 1));
    }

    [Theory]
    [InlineData(0u, "head")]
    [InlineData(1u, "chest")]
    [InlineData(2u, "abdomen")]
    [InlineData(3u, "upper arm")]
    [InlineData(4u, "lower arm")]
    [InlineData(5u, "hand")]
    [InlineData(6u, "upper leg")]
    [InlineData(7u, "lower leg")]
    [InlineData(8u, "foot")]
    [InlineData(9u, "horn")]
    [InlineData(10u, "front leg")]
    [InlineData(11u, "unknown")] // a gap in retail's table
    [InlineData(12u, "front foot")]
    [InlineData(13u, "rear leg")]
    [InlineData(14u, "unknown")]
    [InlineData(15u, "rear foot")]
    [InlineData(16u, "torso")]
    [InlineData(17u, "tail")]
    [InlineData(18u, "arm")]
    [InlineData(19u, "leg")]
    [InlineData(20u, "claw")]
    [InlineData(21u, "wings")]
    [InlineData(22u, "breath")]
    [InlineData(23u, "tentacle")]
    [InlineData(24u, "upper tentacle")]
    [InlineData(25u, "lower tentacle")]
    [InlineData(26u, "cloak")]
    [InlineData(27u, "num")]
    [InlineData(28u, "unknown")]
    [InlineData(0xFFFFFFFFu, "undefined")] // -1
    public void The_body_part_is_retails_name_in_lower_case_with_the_underscores_as_spaces(uint location, string part) =>
        Assert.Equal($"Drudge slashes your {part} for 5 points of slashing damage!", Defend(0.3, Slash, location: location));

    [Fact]
    public void The_defenders_critical_sneak_reckless_and_protection_pieces()
    {
        Assert.Equal("Critical hit! Drudge crushes your upper arm for 20 points of bludgeoning damage!", Defend(0.6, Bludgeon, 20, 3, critical: true));
        Assert.Equal("Sneak Attack! Reckless! Drudge bashes your hand for 5 points of bludgeoning damage!", Defend(0.2, Bludgeon, 5, 5, conditions: 6));
        Assert.Equal(
            "Critical hit! Sneak Attack! Reckless! Drudge crushes your upper arm for 20 points of bludgeoning damage! Your Critical Protection augmentation allows you to avoid a critical hit!",
            Defend(0.6, Bludgeon, 20, 3, critical: true, conditions: 7));
        Assert.Equal(
            "Drudge bashes your head for 5 points of bludgeoning damage! Your Critical Protection augmentation allows you to avoid a critical hit!",
            Defend(0.2, Bludgeon, conditions: 1));
    }

    // ---- 0x01B3 and 0x01B4

    [Fact]
    public void An_evasion_attacker_notification_is_x_evaded_your_attack_in_chat_type_0x16()
    {
        var h = InWorld();

        Send(h, AceM3Writers.EvasionAttackerNotification(Player, 1, "Drudge"));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal("Drudge evaded your attack.", line.Text);
        Assert.Equal((ChatKind.System, 0x16u), (line.Kind, line.ChatType));
        Assert.Equal(new CombatEvent(CombatEventKind.AttackEvaded, "Drudge", 0, 0, 0, false, -1, true, 0, line.Text), Assert.Single(h.Of<CombatEvent>()));
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.EvasionAttackerNotification));
    }

    [Fact]
    public void An_evasion_defender_notification_is_you_evaded_x_in_chat_type_0x15()
    {
        var h = InWorld();

        Send(h, AceM3Writers.EvasionDefenderNotification(Player, 1, "Drudge"));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal("You evaded Drudge!", line.Text);
        Assert.Equal((ChatKind.System, 0x15u), (line.Kind, line.ChatType));
        Assert.Equal(new CombatEvent(CombatEventKind.DefenseEvaded, "Drudge", 0, 0, 0, false, -1, true, 0, line.Text), Assert.Single(h.Of<CombatEvent>()));
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.EvasionDefenderNotification));
    }

    // ---- 0x01AC, 0x01AD, 0x019E

    [Fact]
    public void A_victim_notification_shows_the_servers_text_as_chat_type_0_and_raises_player_died()
    {
        var h = InWorld();

        Send(h, AceM3Writers.VictimNotification(Player, 1, "You have been killed by a Drudge Skulker!"));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal((ChatKind.System, 0u, "You have been killed by a Drudge Skulker!"), (line.Kind, line.ChatType, line.Text));
        Assert.Equal(new PlayerDied("You have been killed by a Drudge Skulker!", 0, DeathNoticeSource.VictimNotification), Assert.Single(h.Of<PlayerDied>()));
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.VictimNotification));
    }

    [Fact]
    public void An_empty_death_text_adds_no_line_as_retail_tests_the_length()
    {
        var h = InWorld();

        Send(h, AceM3Writers.VictimNotification(Player, 1, string.Empty));
        Send(h, AceM3Writers.KillerNotification(Player, 2, string.Empty));

        Assert.Empty(h.Of<ChatReceived>());
        Assert.Single(h.Of<PlayerDied>());
        Assert.Single(h.Of<PlayerKilledSomething>());
    }

    [Fact]
    public void A_killer_notification_shows_the_text_and_raises_player_killed_something_and_logs_its_todo_once()
    {
        var h = InWorld();

        Send(h, AceM3Writers.KillerNotification(Player, 1, "You have killed Bob!"));
        Send(h, AceM3Writers.KillerNotification(Player, 2, "You have killed Carol!"));

        Assert.Equal(["You have killed Bob!", "You have killed Carol!"], h.Of<ChatReceived>().Select(c => c.Line.Text));
        Assert.All(h.Of<ChatReceived>(), c => Assert.Equal((ChatKind.System, 0u), (c.Line.Kind, c.Line.ChatType)));
        Assert.Equal(["You have killed Bob!", "You have killed Carol!"], h.Of<PlayerKilledSomething>().Select(e => e.Text));
        Assert.Empty(h.Of<PlayerDied>());
        Assert.Equal(1, h.Log.Entries.Count(entry => entry.Message.Contains("TODO(retail-handler-01AD)", StringComparison.Ordinal)));
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOfEvent(GameEventTypes.KillerNotification));
    }

    [Fact]
    public void The_broadcast_naming_another_player_shows_its_text_and_raises_nothing()
    {
        var h = InWorld();

        Send(h, AceM3Writers.PlayerKilled("Bob has been killed by Carol.", 0x50000002, 0x50000003));

        var line = Assert.Single(h.Of<ChatReceived>()).Line;
        Assert.Equal((ChatKind.System, 0u, "Bob has been killed by Carol."), (line.Kind, line.ChatType, line.Text));
        Assert.Empty(h.Of<PlayerDied>());
        Assert.Equal(0, h.Reducer.Counters.DecodedNotAppliedOf(MessageOpcodes.PlayerKilled));
    }

    [Fact]
    public void The_broadcast_naming_the_player_as_the_killer_shows_nothing_the_0x01AD_does()
    {
        var h = InWorld();

        Send(h, AceM3Writers.PlayerKilled("Bob has been killed by Tester.", 0x50000002, Player));

        Assert.Empty(h.Of<ChatReceived>());
        Assert.Empty(h.Of<PlayerDied>());
    }

    [Fact]
    public void The_broadcast_naming_the_player_as_the_victim_shows_no_line_retail_leaves_that_to_the_0x01AC()
    {
        var h = InWorld();

        Send(h, AceM3Writers.PlayerKilled("Tester has been killed by Drudge.", Player, 0x80000456));

        Assert.Empty(h.Of<ChatReceived>());
        Assert.Equal(new PlayerDied("Tester has been killed by Drudge.", 0x80000456, DeathNoticeSource.PlayerKilledBroadcast), Assert.Single(h.Of<PlayerDied>()));
    }

    [Fact]
    public void One_death_raises_player_died_once_when_the_0x01AC_comes_first_as_ACE_sends_it()
    {
        var h = InWorld();

        Send(h, AceM3Writers.VictimNotification(Player, 1, "You have been killed by Drudge."));
        Send(h, AceM3Writers.PlayerKilled("Tester has been killed by Drudge.", Player, 0x80000456));

        var died = Assert.Single(h.Of<PlayerDied>());
        Assert.Equal(DeathNoticeSource.VictimNotification, died.Source); // the 0x01AC wins: it is the victim's own message and the one retail shows
        Assert.Equal(["You have been killed by Drudge."], h.Of<ChatReceived>().Select(c => c.Line.Text));
    }

    [Fact]
    public void One_death_raises_player_died_once_when_the_0x019E_comes_first_and_the_0x01AC_text_still_shows()
    {
        var h = InWorld();

        Send(h, AceM3Writers.PlayerKilled("Tester has been killed by Drudge.", Player, 0x80000456));
        Send(h, AceM3Writers.VictimNotification(Player, 1, "You have been killed by Drudge."));

        Assert.Equal(DeathNoticeSource.PlayerKilledBroadcast, Assert.Single(h.Of<PlayerDied>()).Source);
        Assert.Equal(["You have been killed by Drudge."], h.Of<ChatReceived>().Select(c => c.Line.Text));
    }

    [Fact]
    public void A_second_death_raises_again_after_the_pair_window_and_two_of_one_kind_are_two_deaths()
    {
        var h = InWorld();

        Send(h, AceM3Writers.VictimNotification(Player, 1, "You died."));
        Send(h, AceM3Writers.PlayerKilled("Tester died.", Player, 0));
        Assert.Single(h.Of<PlayerDied>());

        // Straight after a completed pair, the next 0x01AC is a new death (the pair is spent), and so is a second of the same kind.
        Send(h, AceM3Writers.VictimNotification(Player, 2, "You died again."));
        Assert.Equal(2, h.Of<PlayerDied>().Count);
        Send(h, AceM3Writers.VictimNotification(Player, 3, "You died a third time."));
        Assert.Equal(3, h.Of<PlayerDied>().Count);

        // A lone 0x01AC whose 0x019E never came must not swallow the next death's broadcast once the window has passed.
        h.Time.Advance(TimeSpan.FromSeconds(DeathPairing.PairWindowSeconds + 1));
        Send(h, AceM3Writers.PlayerKilled("Tester died a fourth time.", Player, 0));
        Assert.Equal(4, h.Of<PlayerDied>().Count);
        Assert.Equal(DeathNoticeSource.PlayerKilledBroadcast, h.Of<PlayerDied>()[3].Source);
    }

    // ---- the auto-target (ClientCombatSystem::AutoTarget, 11.6096 0x0056C9D0): raised for 0x01B2 and 0x01B4 in melee or missile mode with the option on

    private static (ReducerHarness H, uint Drudge) WithDrudge(int? mode, uint options = PlayerQualities.AutoTargetOption)
    {
        var h = InWorld();
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Options1 = options, Options2 = 0 });
        const uint drudge = 0x80000B01;
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(drudge, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, 12, 20, 30), name: "Drudge"), isUpdate: false);
        if (mode is { } m)
        {
            SetMode(h, 5, m);
        }

        Assert.Equal((mode ?? 0, options), (h.State.Self.Qualities.CombatMode, h.State.Self.Qualities.CharacterOptions1));
        h.ClearEvents();
        return (h, drudge);
    }

    [Theory]
    [InlineData(CombatModes.Melee)]
    [InlineData(CombatModes.Missile)]
    public void A_defender_or_evasion_notice_in_a_melee_or_missile_mode_asks_for_the_attacker_to_be_auto_targeted(int mode)
    {
        var (h, drudge) = WithDrudge(mode);
        Send(h, AceM3Writers.DefenderNotification(Player, 10, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Equal(new AutoTargetRequested(drudge), Assert.Single(h.Of<AutoTargetRequested>()));
        h.ClearEvents();
        Send(h, AceM3Writers.EvasionDefenderNotification(Player, 11, "Drudge"));
        Assert.Equal(new AutoTargetRequested(drudge), Assert.Single(h.Of<AutoTargetRequested>()));
    }

    [Theory]
    [InlineData(CombatModes.NonCombat)]
    [InlineData(CombatModes.Magic)]
    public void The_auto_target_is_not_asked_for_in_peace_or_magic_mode_or_with_the_option_off(int mode)
    {
        var (h, _) = WithDrudge(mode);
        Send(h, AceM3Writers.DefenderNotification(Player, 10, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Empty(h.Of<AutoTargetRequested>());

        var (off, _) = WithDrudge(CombatModes.Melee, options: 0);
        Send(off, AceM3Writers.DefenderNotification(Player, 10, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Empty(off.Of<AutoTargetRequested>());
    }

    [Fact]
    public void An_attacker_the_table_does_not_know_asks_with_no_id_and_logs_the_selectnext_todo()
    {
        var (h, _) = WithDrudge(CombatModes.Melee);
        Send(h, AceM3Writers.DefenderNotification(Player, 10, "Unknown Thing", Slash, 0.3, 14, 4, false, 0));
        Assert.Equal(new AutoTargetRequested(0), Assert.Single(h.Of<AutoTargetRequested>()));
        Assert.Contains(h.Log.Lines, l => l.Contains("TODO(m3-autotarget-selectnext)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_selection_takes_the_auto_targeted_attacker_only_when_nothing_is_selected()
    {
        var (h, drudge) = WithDrudge(CombatModes.Melee);
        using var selection = new Lifestone.Client.Play.ObjectSelection();
        selection.Attach(h.Bus);
        Send(h, AceM3Writers.DefenderNotification(Player, 10, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Equal(drudge, selection.Selected); // combat mode, nothing selected: the attacker

        selection.Select(0x80000B02);
        Send(h, AceM3Writers.DefenderNotification(Player, 11, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Equal(0x80000B02u, selection.Selected); // something selected: no change

        var (peace, _) = WithDrudge(CombatModes.NonCombat);
        using var peaceSelection = new Lifestone.Client.Play.ObjectSelection();
        peaceSelection.Attach(peace.Bus);
        Send(peace, AceM3Writers.DefenderNotification(Player, 10, "Drudge", Slash, 0.3, 14, 4, false, 0));
        Assert.Equal(0u, peaceSelection.Selected); // peace: no select
    }

    // ---- 0x02CD for the combat mode

    private static void SetMode(ReducerHarness h, byte sequence, int mode) =>
        Send(h, AceWireWriters.PrivateUpdatePropertyInt(sequence, CombatModes.Property, mode));

    [Fact]
    public void The_combat_mode_reads_zero_until_the_server_sends_one_then_follows_the_0x02CD_and_raises_combat_mode_changed()
    {
        var h = InWorld();
        var q = h.State.Self.Qualities;
        Assert.Equal(0, q.CombatMode);

        SetMode(h, 1, CombatModes.NonCombat);
        SetMode(h, 2, CombatModes.Melee);
        SetMode(h, 3, CombatModes.Magic);

        Assert.Equal(CombatModes.Magic, q.CombatMode);
        Assert.Equal([new CombatModeChanged(0, 1), new CombatModeChanged(1, 2), new CombatModeChanged(2, 8)], h.Of<CombatModeChanged>());
        Assert.Equal(3, h.Of<QualitiesChanged>().Count(c => c is { Change: QualityChange.Int, Key: 40 })); // the usual quality event still follows each
    }

    [Fact]
    public void The_changed_event_is_raised_with_the_new_mode_already_stored_and_ahead_of_the_quality_event()
    {
        var h = InWorld();
        var seenInHandler = -1;
        h.Bus.Subscribe<CombatModeChanged>((in CombatModeChanged _) => seenInHandler = h.State.Self.Qualities.CombatMode);

        SetMode(h, 1, CombatModes.Missile);

        Assert.Equal(CombatModes.Missile, seenInHandler);
        Assert.Equal(["CombatModeChanged", "QualitiesChanged"], h.Names());
    }

    [Fact]
    public void A_repeat_of_the_same_mode_and_a_stale_update_raise_no_change()
    {
        var h = InWorld();
        SetMode(h, 5, CombatModes.Melee);
        h.ClearEvents();

        SetMode(h, 6, CombatModes.Melee); // the same mode: stored again, no change
        SetMode(h, 4, CombatModes.Magic); // an older sequence: dropped like any quality update
        Assert.Empty(h.Of<CombatModeChanged>());
        Assert.Equal(CombatModes.Melee, h.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void Another_int_property_raises_no_combat_mode_change_and_is_stored_as_before()
    {
        var h = InWorld();

        Send(h, AceWireWriters.PrivateUpdatePropertyInt(1, 25, 9));

        Assert.Empty(h.Of<CombatModeChanged>());
        Assert.Equal(9, h.State.Self.Qualities.Level);
        Assert.Equal(0, h.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_description_carrying_the_mode_sets_it_without_an_event_and_the_next_change_names_it_as_previous()
    {
        var h = InWorld();
        var description = new PlayerDescriptionMsg { IntProperties = ImmutableDictionary<uint, int>.Empty.Add(CombatModes.Property, CombatModes.NonCombat) };
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription), description);
        Assert.Equal(CombatModes.NonCombat, h.State.Self.Qualities.CombatMode);
        Assert.Empty(h.Of<CombatModeChanged>());

        SetMode(h, 1, CombatModes.Melee);

        Assert.Equal(new CombatModeChanged(CombatModes.NonCombat, CombatModes.Melee), Assert.Single(h.Of<CombatModeChanged>()));
    }

    // ---- the whole set

    [Fact]
    public void Every_combat_and_death_message_is_applied_none_is_counted_as_not_applied_none_unknown_or_dropped()
    {
        var h = InWorld();

        Send(h, AceM3Writers.VictimNotification(Player, 1, "You died."));
        Send(h, AceM3Writers.KillerNotification(Player, 2, "You killed it."));
        Send(h, AceM3Writers.AttackerNotification(Player, 3, "Drudge", 0x10, 0.5, 6, false, 4));
        Send(h, AceM3Writers.DefenderNotification(Player, 4, "Drudge", 0x10, 0.5, 6, 2, true, 4));
        Send(h, AceM3Writers.EvasionAttackerNotification(Player, 5, "Drudge"));
        Send(h, AceM3Writers.EvasionDefenderNotification(Player, 6, "Drudge"));
        Send(h, AceM3Writers.PlayerKilled("It died.", 0x50000009, 0x80000456));

        Assert.Equal(0, h.Reducer.Counters.DecodedNotApplied);
        Assert.Empty(h.Of<UnknownMessage>());
        Assert.Empty(h.Of<MessageDropped>());
        Assert.Equal(7, h.Of<ChatReceived>().Count);
    }
}
