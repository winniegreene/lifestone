using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.RetailUi;

/// <summary>
/// M4 4.1 t1 and t2 (D23, D26, <c>m4-standin-tusker-retire</c>): no play-check, panel shot or <c>--play-*</c> path places or fights a stand-in creature, and the stand-in has none to place (t2 removed <c>StandInTusker</c> and its health, blows and kill). Lifestone.Game has no test project, so the rule is pinned in the sources of the
/// Godot side that draw: the play-check files, the panel shots and the panel-frame plan. A creature's behaviour is proved by scripted unit tests that draw nothing and live by the lead (the bsd-fight, bsd-death and hunt legs).
/// </summary>
public sealed class NoStandInCreatureInDrawnRunsTests
{
    private static IEnumerable<string> DrawnRunSources() =>
        CheckoutFiles.CSharpUnder("src/Lifestone.Game/scripts/Session")
            .Where(static p => p.Contains("/PlayChecks", StringComparison.Ordinal) || p.Contains("/PlayMode.PanelShots", StringComparison.Ordinal))
            .Append("src/Lifestone.Client/Play/PanelFramesPlan.cs");

    [Fact]
    public void No_play_check_panel_shot_or_panel_frame_source_places_a_stand_in_tusker_or_an_attacking_creature()
    {
        var sources = DrawnRunSources().ToList();
        Assert.Contains("src/Lifestone.Game/scripts/Session/PlayChecks.cs", sources);
        Assert.Contains("src/Lifestone.Game/scripts/Session/PlayChecks.Containers.cs", sources);
        Assert.Contains("src/Lifestone.Game/scripts/Session/PlayMode.PanelShots.cs", sources);
        Assert.DoesNotContain(sources, static p => p.EndsWith("PlayChecks.Fight.cs", StringComparison.Ordinal) || p.EndsWith("PlayChecks.Attack.cs", StringComparison.Ordinal) || p.EndsWith("PlayChecks.Death.cs", StringComparison.Ordinal)); // the three files of the tusker rows are gone

        foreach (var path in sources)
        {
            var code = CheckoutFiles.ReadCSharpCodeAndStrings(path);
            foreach (var banned in new[] { "StandInTusker", "PlaceAttackTusker", "PlaceAttackFrameTusker", "AttackFrameTusker", "attacks: true", "Attacks: true", "WithholdCreatureCorpses" })
            {
                Assert.DoesNotContain(banned, code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_container_rows_open_stand_in_chests_and_the_panel_shots_finder_expects_the_chest_not_a_corpse()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings("src/Lifestone.Game/scripts/Session/PlayChecks.Containers.cs");
        Assert.Contains("server.PlaceChest(OpenChestGuid,", code, StringComparison.Ordinal);
        Assert.Contains("server.PlaceChest(LootBoxGuid,", code, StringComparison.Ordinal);
        Assert.Contains("server.PlaceChest(LootChestGuid,", code, StringComparison.Ordinal);
        Assert.Contains("nearestObject.Name == StandInChest.Name", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Corpse of ", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCombatMode(CombatMode.Melee", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Actions.Attack(", code, StringComparison.Ordinal);
        Assert.Equal("container-window", PanelFramesPlan.ContainerWindowFrame);
        Assert.DoesNotContain("corpse", PanelFramesPlan.OfflineContentFrames);
    }

    /// <summary>The argument text of every <c>new StandInObject(...)</c> in <paramref name="code"/> (parenthesis-matched).</summary>
    private static IEnumerable<string> StandInObjectArguments(string code)
    {
        const string marker = "new StandInObject(";
        for (var at = code.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = code.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            var depth = 1;
            var i = at + marker.Length;
            while (i < code.Length && depth > 0)
            {
                depth += code[i] == '(' ? 1 : code[i] == ')' ? -1 : 0;
                i++;
            }

            yield return code[(at + marker.Length)..(i - 1)];
        }
    }

    private static uint ParseFlags(string expression)
    {
        uint flags = 0;
        foreach (var part in expression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            flags |= part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? uint.Parse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : uint.Parse(part, CultureInfo.InvariantCulture);
        }

        return flags;
    }

    [Fact]
    public void No_stand_in_object_a_play_check_panel_shot_or_visit_places_has_a_creature_item_type_or_an_attackable_flag_whatever_its_name()
    {
        // M4 4.1 t2 (finding 4.1-R1, D23): the guard, the drudge and the creature of the visit are gone, and so is any object that would read as a creature. The two stand-in vendors (StandInWorld.VendorObjects, not placed by a
        // check) are the named exception: a vendor is a person, a Creature by ItemType.
        var seen = 0;
        foreach (var path in DrawnRunSources())
        {
            var code = CheckoutFiles.ReadCSharpCodeAndStrings(path);
            foreach (var arguments in StandInObjectArguments(code))
            {
                seen++;
                var flagsMatch = Regex.Match(arguments, @"DescriptionFlags:\s*([0-9A-Fa-fxX| ]+?)\s*(?:,|$)");
                var flags = flagsMatch.Success ? ParseFlags(flagsMatch.Groups[1].Value) : 0u;
                var wcidMatch = Regex.Match(arguments, @"WeenieClassId:\s*(\d+)");
                var obj = new StandInObject(1, "x", default, DescriptionFlags: flags, WeenieClassId: wcidMatch.Success ? uint.Parse(wcidMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 2);
                Assert.True((flags & 0x10) == 0, path + ": an attackable stand-in object is placed: " + arguments);
                Assert.True((StandInSessions.ItemTypeOf(obj) & StandInSessions.CreatureItemType) == 0, path + ": a stand-in object with a creature ItemType is placed: " + arguments);
                Assert.DoesNotContain("Guard", arguments, StringComparison.Ordinal);
                Assert.DoesNotContain("Drudge", arguments, StringComparison.Ordinal);
                Assert.DoesNotContain("Creature", arguments, StringComparison.Ordinal);
            }

            // Nothing but a chest is placed by a check: no other object, and no creature of any name.
            foreach (Match call in Regex.Matches(code, @"[sS]erver\.(Place|PlaceNear)\("))
            {
                Assert.Fail(path + ": a check places an object other than a chest (" + call.Value + ")");
            }
        }

        Assert.True(seen >= 7, "the visit's objects were found: " + seen);
    }

    [Fact]
    public void Only_a_person_a_player_or_a_vendor_is_a_creature_by_item_type_and_a_lifestone_a_chest_a_portal_and_a_statue_are_not()
    {
        Assert.Equal(StandInSessions.CreatureItemType, StandInSessions.ItemTypeOf(new StandInObject(1, "Vendor", default, DescriptionFlags: StandInWorldFlags.Vendor)));
        Assert.Equal(StandInSessions.CreatureItemType, StandInSessions.ItemTypeOf(new StandInObject(1, "Player", default, DescriptionFlags: StandInSessions.PlayerFlag)));
        Assert.Equal(StandInSessions.MiscItemType, StandInSessions.ItemTypeOf(new StandInObject(1, "Statue", default)));
        Assert.Equal(StandInSessions.MiscItemType, StandInSessions.ItemTypeOf(StandInLifestone.Make(1)));
        Assert.Equal(StandInSessions.MiscItemType, StandInSessions.ItemTypeOf(new StandInObject(1, "Portal", default, PortalTo: new StandInTeleport(0, 0x860201AD, default))));
        Assert.Equal(0x200u, StandInSessions.ItemTypeOf(StandInChest.Make(1)));
    }

    private static class StandInWorldFlags
    {
        public const uint Vendor = 0x200;
    }
}
