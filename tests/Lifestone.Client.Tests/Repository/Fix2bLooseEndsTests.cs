using Lifestone.Client.Session.Actions;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// M4 4.1 fix2b, the loose ends: the dead creature-corpse code is gone from the stand-in's containers (t2's note), ACE's 60 s session timeout is named once and read from the bundled config (review R12), the EnterWorld backstop outlasts the
/// session's own named failure (R13), the combat files carry their provenance (R16) and the third-party rows exist. Text and constant checks over the repository's own files; they draw nothing and need no dats.
/// </summary>
public sealed class Fix2bLooseEndsTests
{
    private static string Read(params string[] parts) => File.ReadAllText(RepositoryFiles.PathOf(parts));

    [Fact]
    public void The_stand_in_containers_hold_no_creature_corpse_path()
    {
        var code = Read("src", "Lifestone.Client", "Session", "StandIn", "StandInWorld.Containers.cs");

        foreach (var gone in new[] { "RegisterCorpse", "WithholdCreatureCorpses", "CorpseOpenDelaySeconds", "Corpse of", "chest: false" })
        {
            Assert.DoesNotContain(gone, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_default_wait_for_a_character_ACE_still_holds_is_the_bundled_session_timeout_plus_the_logout_allowance_and_the_config_says_60()
    {
        Assert.Equal(LoginRetryPolicy.BundledSessionTimeoutSeconds + LoginRetryPolicy.LogoutAllowanceSeconds, LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds);
        Assert.Equal(75, LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds);

        // The constant follows the bundled config: when server/templates/Config.template.js changes DefaultSessionTimeout, this fails until the constant is moved with it.
        var config = Read("server", "templates", "Config.template.js");
        var line = config.Split('\n').Single(l => l.Contains("\"DefaultSessionTimeout\"", StringComparison.Ordinal));
        var value = double.Parse(line.Split(':')[1].Trim().TrimEnd(',', '\r', ' '), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(LoginRetryPolicy.BundledSessionTimeoutSeconds, value);
    }

    [Fact]
    public void The_enter_world_backstop_outlasts_the_sessions_30_s_cap_and_75_s_wait_by_its_slack()
    {
        Assert.Equal(30 + LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds + ActionPolicy.EnterWorldSlackSeconds, ActionPolicy.EnterWorldSeconds);
        Assert.True(ActionPolicy.EnterWorldSlackSeconds > 0);
        Assert.Equal(110, ActionPolicy.EnterWorldSeconds);
    }

    [Fact]
    public void The_combat_files_carry_their_retail_addresses_and_ACE_provenance_and_the_third_party_rows_list_them()
    {
        var key = Read("src", "Lifestone.Client", "RetailUi", "Panels", "CombatToggleKey.cs");
        Assert.Contains("RegisterInputMaps</c> (11.4186 0x0056AA20, 11.6096 0x0056B760)", key, StringComparison.Ordinal);

        var stances = Read("src", "Lifestone.Client", "Session", "Contracts", "CombatStances.cs");
        Assert.StartsWith("// Re-expresses ACEmulator/ACE@95ef426d", stances, StringComparison.Ordinal);
        Assert.Contains("AGPL-3.0", stances[..stances.IndexOf("namespace", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("TODO(m4-standin-combat-style)", stances, StringComparison.Ordinal);
        Assert.Contains("Creature_Combat.cs</c> 335-337", stances, StringComparison.Ordinal); // the unarmed weapon's HandCombat, named (review R15)

        var effects = Read("docs", "third-party", "effects.md");
        Assert.Contains("`src/Lifestone.Client/Session/Contracts/CombatStances.cs`", effects, StringComparison.Ordinal);
        Assert.Contains("`src/Lifestone.Client/Session/StandIn/StandInWorld.Stance.cs`", effects, StringComparison.Ordinal);
        Assert.Contains("`Panels/CombatToggleKey.cs`", Read("docs", "third-party", "retail-ui.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_bare_refusal_line_and_the_chest_note_and_the_play_check_guard_say_what_they_are()
    {
        Assert.Contains("[Lifestone] You can't enter {ModeWord(requested)} mode\"", Read("src", "Lifestone.Client", "RetailUi", "Panels", "GameCombatPanel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("the human Setup", Read("src", "Lifestone.Client", "Session", "StandIn", "StandInServer.cs"), StringComparison.Ordinal);
        Assert.Contains("play checks: not run, --play-look-shots runs instead", Read("src", "Lifestone.Game", "scripts", "Session", "PlayChecks.cs"), StringComparison.Ordinal);
        Assert.Contains("TODO(m4-buff-ledger-hand-legs)", Read("src", "Lifestone.Client", "Session", "Mage", "RebuffHostLogic.cs"), StringComparison.Ordinal);
    }
}
