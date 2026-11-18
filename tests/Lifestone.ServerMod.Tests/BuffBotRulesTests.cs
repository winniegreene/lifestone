using Lifestone.Client.Tests;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D124-D127, D138): the buff bot's rules and its shared facts (its day, casts and trade: <c>BuffBotDeskTests</c>). ACE's assemblies are not in this project, so the rules run here and the seam (<c>BuffBot.cs</c>) is read as
/// text; the live proof is the lead's: the <c>Lifestone buff bot: placed</c> line, one word told, and every line of the profile on the teller.
/// </summary>
public sealed class BuffBotRulesTests
{
    private static string Seam => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "BuffBot.cs"));

    private static string Body => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "BuffBotBody.cs"));

    private static string Watch => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "StartupWatch.cs"));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("on", true)]
    [InlineData("offf", true)]
    [InlineData("off", false)]
    [InlineData(" OFF ", false)]
    public void The_bot_is_placed_unless_the_flag_says_off(string? value, bool on) => Assert.Equal(on, BuffBotRules.IsOn(value));

    [Theory]
    [InlineData(false, true, true)] // not standing, a player in Holtburg
    [InlineData(false, false, false)] // nobody there: the landblock may not be loaded
    [InlineData(true, true, false)] // it stands
    [InlineData(true, false, false)]
    public void It_is_placed_when_it_does_not_stand_and_a_player_is_on_its_landblock(bool stands, bool playerOnBlock, bool place) =>
        Assert.Equal(place, BuffBotRules.PlaceNow(stands, playerOnBlock));

    [Fact]
    public void The_bots_id_is_a_static_id_of_Holtburg_past_the_world_databases_own_and_it_stands_in_the_cell_that_holds_its_point()
    {
        Assert.Equal(0x7A9B4u, BuffBotWire.Guid >> 12); // static (0x7.......), Holtburg's landblock 0xA9B4: ACE saves no static object that did not come from the database
        Assert.True(BuffBotWire.Guid > 0x7A9B46B1, "past the landblock instances of ACE-World-Database v0.9.296 (0x7A9B4000 to 0x7A9B46B1, read 2026-11-11)");
        Assert.Equal(BuffBotWire.Landblock, (ushort)(BuffBotWire.Cell >> 16));
        var cell = ((uint)Math.Floor(BuffBotWire.X / PlaceRules.CellLength) * PlaceRules.CellsPerSide) + (uint)Math.Floor(BuffBotWire.Y / PlaceRules.CellLength) + 1;
        Assert.Equal(BuffBotWire.Cell & 0xFFFF, cell);

        // Off the lifestone point every pass at home walks to (84, 7.7), and off the lifestone (81.3, 11.8) and Alcott (83.3, 14.0): five metres clear of each.
        (double X, double Y)[] keepClear = [(84.0, 7.7), (81.3, 11.8), (83.3, 14.0)];
        Assert.All(keepClear, p => Assert.True(Math.Sqrt(Math.Pow(p.X - BuffBotWire.X, 2) + Math.Pow(p.Y - BuffBotWire.Y, 2)) >= 5.0, $"clear of ({p.X}, {p.Y})"));
    }

    [Fact]
    public void A_word_asks_for_its_profile_whole_and_ignoring_case()
    {
        Assert.Same(BuffBotWire.Tusker, BuffBotWire.ProfileOf("tusker"));
        Assert.Same(BuffBotWire.Tusker, BuffBotWire.ProfileOf(" Tuskers "));
        Assert.Same(BuffBotWire.All, BuffBotWire.ProfileOf("all"));
        Assert.Same(BuffBotWire.All, BuffBotWire.ProfileOf(" ALL "));
        Assert.Null(BuffBotWire.ProfileOf("alls"));
        Assert.Null(BuffBotWire.ProfileOf("all please"));
        Assert.Equal([BuffBotWire.Tusker, BuffBotWire.All, BuffBotWire.Wasp], BuffBotWire.Profiles);
        Assert.Same(BuffBotWire.Wasp, BuffBotWire.ProfileOf("Wasps"));
        Assert.Same(BuffBotWire.Wasp, BuffBotWire.ProfileOf("phyntos"));
        Assert.Same(BuffBotWire.All, BuffBotWire.Default); // the owner, 2026-11-12: the client says the word all when no word is named
        Assert.Null(BuffBotWire.ProfileOf("tuskerz"));
        Assert.Null(BuffBotWire.ProfileOf("tusker please"));
        Assert.Null(BuffBotWire.ProfileOf(BuffBotWire.HelpWord));
        Assert.Null(BuffBotWire.ProfileOf(null));
        Assert.Null(BuffBotWire.ProfileOf("  "));

        var words = BuffBotWire.Profiles.SelectMany(p => p.Words).Append(BuffBotWire.HelpWord).Append(BuffBotWire.CreditWord).ToArray();
        Assert.Equal(words.Length, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(words, w => Assert.Equal(w.Trim().ToLowerInvariant(), w));
        Assert.All(BuffBotWire.Profiles, p => Assert.Equal(p.Key, p.Words[0]));
        Assert.All(BuffBotWire.Profiles.SelectMany(p => p.Words), w => Assert.Contains(w, BuffBotWire.Help, StringComparison.Ordinal));
    }

    [Fact]
    public void The_tuskers_profile_is_its_nineteen_lines_by_spell_and_name_each_once_and_none_is_cast_on_armour()
    {
        // The whole list, pinned: the order cast, each spell's id and name and the Self line it stands over (BuffBotSpellsDatTests proves each from the dat). A line added or moved changes this list and nothing else here.
        (uint SpellId, string Spell, string Line)[] pinned =
        [
            (2066, "Calming Gaze", "Focus Self"),
            (2090, "Bolstered Will", "Willpower Self"),
            (2086, "Might of the 5 Mules", "Strength Self"),
            (2060, "Temeritous Touch", "Endurance Self"),
            (2058, "Boon of Refinement", "Coordination Self"),
            (2080, "Ogfoot", "Quickness Self"),
            (2214, "Adja's Boon", "Creature Enchantment Mastery Self"),
            (2266, "Harlune's Boon", "Life Magic Mastery Self"),
            (2322, "Hieromancer's Boon", "War Magic Mastery Self"),
            (2248, "Celcynd's Boon", "Item Enchantment Mastery Self"),
            (2286, "Nuhmudira's Boon", "Mana Mastery Self"),
            (2244, "Web of Defense", "Invulnerability Self"),
            (2052, "Executor's Boon", "Armor Self"),
            (2152, "Boon of the Mace Turner", "Bludgeon Protection Self"),
            (2184, "Hydra's Head", "Regeneration Self"),
            (2186, "Tenaciousness", "Rejuvenation Self"),
            (2182, "Battlemage's Boon", "Mana Renewal Self"),
            (2300, "Saladur's Boon", "Sprint Self"), // Run
            (2256, "Jahannan's Boon", "Jumping Mastery Self"), // Jump
        ];
        var lines = BuffBotWire.Tusker.Lines;
        Assert.Equal(19, pinned.Length);
        Assert.Equal(pinned, lines.Select(l => (l.SpellId, l.Spell, l.Line)));
        Assert.Equal(lines.Count, lines.Select(l => l.SpellId).Distinct().Count());
        Assert.Equal(lines.Count, lines.Select(l => l.Line).Distinct(StringComparer.Ordinal).Count());
        Assert.All(lines, l => Assert.EndsWith(" Self", l.Line, StringComparison.Ordinal)); // the line it stands over, by the climber's own name for it (Run and Jump, which she holds no line of, by the Self spell's line)
        Assert.DoesNotContain(lines, l => l.Line.Contains("Impenetrability", StringComparison.Ordinal) || l.Line.Contains("Bane", StringComparison.Ordinal)); // D127: only a shield takes those from another's hand
    }

    [Fact]
    public void The_profile_all_is_the_tuskers_lines_first_then_the_other_six_protections_and_the_three_skills_each_once()
    {
        // The nine lines after the tuskers', pinned: each spell's id and name (the world copy's spell table, read 2026-11-12: the Other spell at level VII, one id under its Self spell) and the Self line it stands over.
        (uint SpellId, string Spell, string Line)[] added =
        [
            (2150, "Boon of the Blade Turner", "Blade Protection Self"),
            (2160, "Boon of the Arrow Turner", "Piercing Protection Self"),
            (2156, "Fiery Boon", "Fire Protection Self"),
            (2154, "Icy Boon", "Cold Protection Self"),
            (2148, "Caustic Boon", "Acid Protection Self"),
            (2158, "Storm's Boon", "Lightning Protection Self"),
            (2280, "Web of Resistance", "Magic Resistance Self"), // Magic Defense
            (2242, "Web of Deflection", "Impregnability Self"), // Missile Defense
            (2194, "Aliester's Boon", "Arcane Enlightenment Self"), // Arcane Lore
        ];
        var tusker = BuffBotWire.Tusker.Lines;
        var all = BuffBotWire.All.Lines;
        Assert.Equal(("all", "every ground"), (BuffBotWire.All.Key, BuffBotWire.All.Purpose));
        Assert.Equal(["all"], BuffBotWire.All.Words);
        Assert.Equal(28, all.Count);

        // A superset, the tuskers' lines first and in their order: whatever reads a line of the tuskers' (the rung gates, the Strength floor, the start's read) finds it at the same place.
        Assert.Equal(tusker, all.Take(tusker.Count));
        Assert.Equal(added, all.Skip(tusker.Count).Select(l => (l.SpellId, l.Spell, l.Line)));
        Assert.Equal(all.Count, all.Select(l => l.SpellId).Distinct().Count());
        Assert.Equal(all.Count, all.Select(l => l.Line).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, l => Assert.EndsWith(" Self", l.Line, StringComparison.Ordinal));
        Assert.DoesNotContain(all, l => l.Line.Contains("Impenetrability", StringComparison.Ordinal) || l.Line.Contains("Bane", StringComparison.Ordinal)); // D127 holds for every profile
        Assert.Equal(7, all.Count(l => l.Line.EndsWith(" Protection Self", StringComparison.Ordinal))); // every damage type: the tuskers' Bludgeoning and the six others
        Assert.DoesNotContain(all, l => l.Line.Contains("Deception", StringComparison.Ordinal)); // left out by name: no rule of hers reads it

        // A line has one spell in every profile that holds it, and LineNamed finds it.
        foreach (var line in BuffBotWire.Profiles.SelectMany(p => p.Lines))
        {
            Assert.Equal(line, BuffBotWire.LineNamed(line.Line));
        }

        Assert.Equal(2086u, BuffBotWire.LineNamed("Strength Self")!.SpellId);
        Assert.Null(BuffBotWire.LineNamed("Deception Mastery Self"));
        Assert.Null(BuffBotWire.LineNamed("strength self")); // by the exact name
    }

    [Fact]
    public void The_wasps_profile_is_the_tuskers_lines_then_blade_piercing_cold_and_acid_protection_magic_resistance_and_arcane_enlightenment()
    {
        // The owner, 2026-11-14: a word for the Phyntos Menace. The White Phyntos Wasp bites (Pierce) and stings (Slash), casts Frost Bolt and Frost Volley (Cold); the hotspots burn Acid.
        Assert.Equal(("wasp", "the wasps"), (BuffBotWire.Wasp.Key, BuffBotWire.Wasp.Purpose));
        Assert.Equal(["wasp", "wasps", "phyntos"], BuffBotWire.Wasp.Words);
        var tusker = BuffBotWire.Tusker.Lines.ToList();
        var wasp = BuffBotWire.Wasp.Lines.ToList();
        Assert.Equal(25, wasp.Count);
        Assert.Equal(tusker, wasp.Take(tusker.Count));
        Assert.Equal(["Blade Protection Self", "Piercing Protection Self", "Cold Protection Self", "Acid Protection Self", "Magic Resistance Self", "Arcane Enlightenment Self"], wasp.Skip(tusker.Count).Select(l => l.Line));
        Assert.All(wasp, l => Assert.Contains(l, BuffBotWire.All.Lines)); // the same spell as 'all' for every line: LineNamed finds one
        Assert.DoesNotContain(wasp, l => l.Line is "Fire Protection Self" or "Lightning Protection Self" or "Impregnability Self");
        Assert.Equal(wasp.Count, wasp.Select(l => l.SpellId).Distinct().Count());
    }

    [Fact]
    public void Each_profile_says_its_own_count_and_purpose_and_no_profiles_last_line_is_anothers()
    {
        Assert.Equal("Stand by me: 19 spells for the tuskers, an hour each.", BuffBotWire.Begins(BuffBotWire.Tusker)); // the tuskers' two lines read as before the second profile
        Assert.Equal("Done: 19 spells for the tuskers. Come back within the hour.", BuffBotWire.Done(BuffBotWire.Tusker));
        Assert.Equal("Stand by me: 28 spells for every ground, an hour each.", BuffBotWire.Begins(BuffBotWire.All));
        Assert.Equal("Done: 28 spells for every ground. Come back within the hour.", BuffBotWire.Done(BuffBotWire.All));

        // The station reads a profile's last line as its sign that the bot is done: no profile's line is in another's.
        var done = BuffBotWire.Profiles.Select(BuffBotWire.Done).ToArray();
        Assert.Equal(done.Length, done.Distinct(StringComparer.Ordinal).Count());
        Assert.All(done, d => Assert.Single(done, other => other.Contains(d, StringComparison.Ordinal)));
        Assert.Equal("Stand by me: 25 spells for the wasps, an hour each.", BuffBotWire.Begins(BuffBotWire.Wasp));
        Assert.Equal("Done: 25 spells for the wasps. Come back within the hour.", BuffBotWire.Done(BuffBotWire.Wasp));
        Assert.Equal("Tell me one of these words and I will buff you: tusker, tuskers, all, wasp, wasps, phyntos. From level 60 a round costs a credit: hand me a Platinum Scarab for each. Tell me credits for your count. I do not cast on armour: only a shield takes a bane from another's hand.", BuffBotWire.Help);
        Assert.StartsWith(BuffBotWire.HelpStart, BuffBotWire.Help, StringComparison.Ordinal); // the client's sign of a word the bot did not know: the start of the line, whatever words it lists
        Assert.All(BuffBotWire.Profiles, p => Assert.DoesNotContain(BuffBotWire.HelpStart, BuffBotWire.Begins(p) + BuffBotWire.Done(p), StringComparison.Ordinal));
    }

    [Fact]
    public void The_answers_are_each_word_then_credits_then_help_then_the_fallback_and_the_give_and_each_hands_its_hook_to_the_bots_code()
    {
        var answers = BuffBotRules.Answers();
        Assert.Equal(["tusker", "tuskers", "all", "wasp", "wasps", "phyntos", BuffBotWire.CreditWord, BuffBotWire.HelpWord, null, null], answers.Select(a => a.Word));
        Assert.Equal(0u, answers[^2].GiveWcid); // the fallback for any other text, the last talk answer (ACE takes the first match and a set with no word matches every text)
        Assert.Equal(BuffBotWire.FeeWcid, answers[^1].GiveWcid); // the give of a Platinum Scarab: its own category, matched by the ware
        Assert.Single(answers, a => a.Word is null && a.GiveWcid == 0);

        // Every word stamps its profile's key on the bot's own registry, then activates the bot itself, whose code reads the key back (D138: no patch; ACE calls the mod's OnActivate).
        foreach (var profile in BuffBotWire.Profiles)
        {
            IReadOnlyList<BuffBotStep> hook = [new(BuffBotStepKind.Stamp, BuffBotRules.WordKey(profile)), new(BuffBotStepKind.Activate)];
            Assert.All(profile.Words, w => Assert.Equal(hook, answers.Single(a => a.Word == w).Steps));
            Assert.Equal(new BuffBotHook(BuffBotHookKind.Word, profile), BuffBotRules.ReadHook([BuffBotRules.WordKey(profile)], out _));
        }

        Assert.Equal([new BuffBotStep(BuffBotStepKind.Stamp, BuffBotRules.CreditsKey), new BuffBotStep(BuffBotStepKind.Activate)], answers.Single(a => a.Word == BuffBotWire.CreditWord).Steps);
        Assert.Equal([new BuffBotStep(BuffBotStepKind.Stamp, BuffBotRules.GiveKey), new BuffBotStep(BuffBotStepKind.Activate)], answers[^1].Steps);
        Assert.Equal(new BuffBotHook(BuffBotHookKind.Credits), BuffBotRules.ReadHook(["SomeOtherQuest", BuffBotRules.CreditsKey], out _));
        Assert.Equal(new BuffBotHook(BuffBotHookKind.Give), BuffBotRules.ReadHook([BuffBotRules.GiveKey.ToUpperInvariant()], out _)); // ACE matches quest names ignoring case
        foreach (var plain in answers.Where(a => a.Word == BuffBotWire.HelpWord || (a.Word is null && a.GiveWcid == 0)))
        {
            Assert.Equal([new BuffBotStep(BuffBotStepKind.Turn), new BuffBotStep(BuffBotStepKind.Tell, BuffBotWire.Help)], plain.Steps);
        }

        // No key, two keys or a key of no hook: nothing is answered, by name.
        Assert.Null(BuffBotRules.ReadHook([], out var none));
        Assert.Contains("0 hook keys", none, StringComparison.Ordinal);
        Assert.Null(BuffBotRules.ReadHook([BuffBotRules.CreditsKey, BuffBotRules.GiveKey], out var two));
        Assert.Contains("2 hook keys", two, StringComparison.Ordinal);
        Assert.Null(BuffBotRules.ReadHook([BuffBotRules.HookPrefix + "word_olthoi"], out var unknown));
        Assert.Contains("names no hook", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_seam_makes_the_mods_own_creature_with_a_static_id_on_the_landblocks_queue_and_writes_no_database()
    {
        var code = string.Join(Environment.NewLine, (Seam + "\n" + Body).Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Contains("DatabaseManager.World.GetCachedWeenie(BuffBotRules.BodyWcid)", code, StringComparison.Ordinal);
        Assert.Contains("new BuffBotBody(WeenieOf(body), new ObjectGuid(BuffBotWire.Guid), this)", code, StringComparison.Ordinal);
        Assert.Contains("internal sealed class BuffBotBody : Creature", code, StringComparison.Ordinal);
        Assert.Contains("public override void OnActivate(WorldObject activator)", code, StringComparison.Ordinal);
        Assert.Contains("block!.EnqueueAction(new ActionEventDelegate(Place));", code, StringComparison.Ordinal);
        Assert.Contains("BuffBotRules.PlaceNow(stands, block is not null)", code, StringComparison.Ordinal);
        Assert.Contains("Category = answer.GiveWcid != 0 ? EmoteCategory.Give : EmoteCategory.ReceiveTalkDirect", code, StringComparison.Ordinal);
        Assert.Contains("[PropertyBool.Attackable] = false", code, StringComparison.Ordinal);
        Assert.Contains("[PropertyBool.AllowGive] = true", code, StringComparison.Ordinal);
        Assert.Contains("bools.Remove(PropertyBool.AiAcceptEverything)", code, StringComparison.Ordinal); // one scarab of a stack a give (Player_Inventory.cs 3423-3425)
        Assert.Contains("[PropertyInstanceId.ActivationTarget] = BuffBotWire.Guid", code, StringComparison.Ordinal);

        // A player's cast: ACE's own mana cost, the fizzle and burn rules, the wind-up, ACE's own cast and the fizzle's script; the walk is ACE's creature move-to; the away and back lines a say in local chat.
        Assert.Contains("CalculateManaUsage(this, spell, target)", code, StringComparison.Ordinal);
        Assert.Contains("BuffBotCasting.Attempt(Desk.Ledger, slots, skill, spell.Power, spell.ComponentLoss, Mana.Current, cost,", code, StringComparison.Ordinal);
        Assert.Contains("spell.Formula.GetFociFormula()", code, StringComparison.Ordinal);
        Assert.Contains("UpdateVitalDelta(Mana, -(int)result.Mana)", code, StringComparison.Ordinal);
        Assert.Contains("TryCastSpell_WithRedirects(spell, on, this)", code, StringComparison.Ordinal);
        Assert.Contains("PlayScript.Fizzle", code, StringComparison.Ordinal);
        Assert.Contains("MoveTo(new Position(p.Cell, p.X, p.Y, p.Z, 0f, 0f, 0f, 1f), GetRunRate())", code, StringComparison.Ordinal);
        Assert.Contains("new GameMessageHearSpeech(act.Text, Name, Guid.Full, ChatMessageType.Speech)", code, StringComparison.Ordinal);

        // A cached weenie shares its create list and its emote list with every object made from it: the bot's are new lists, and the body's are never read or changed.
        Assert.DoesNotContain("body.PropertiesCreateList", code, StringComparison.Ordinal);
        Assert.DoesNotContain("body.PropertiesEmote", code, StringComparison.Ordinal);
        Assert.DoesNotContain("body.PropertiesSpellBook", code, StringComparison.Ordinal);

        // No database write, no patch, nothing put in a player's pack, no player moved.
        string[] forbidden = ["DatabaseManager.Shard", "SaveBiota", "SaveCharacter", "ExecuteSql", "Harmony", "NewDynamicGuid", "GiveObject", "TryCreateInInventory", ".Teleport(", "Tools.Place"];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, code, StringComparison.Ordinal));
    }

    [Fact]
    public void The_watch_starts_the_bot_after_the_world_opens_and_stops_it_with_the_mod()
    {
        var started = Watch.IndexOf("buffBot = BuffBot.Start();", StringComparison.Ordinal);
        var opened = Watch.IndexOf("WorldManager.WorldStatus != WorldManager.WorldStatusState.Open", StringComparison.Ordinal);
        var ready = Watch.IndexOf("{ModLog.Ready}", StringComparison.Ordinal);
        Assert.True(0 <= opened && opened < started && started < ready, "the bot starts after the world opens and before the ready line");
        Assert.Contains("buffBot?.Stop();", Watch, StringComparison.Ordinal);
    }
}
