using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Session.Fighter;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The fighters' gear against the bundled world database (M4 P5a C6, the lead's D41 note c): every pinned weenie still exists in ace_world with its ItemDifficulty (PropertyInt 109), its wield requirements (PropertyInt 158-160, 270-278), its ItemCurMana (107),
/// its ValidLocations (9) and its spells, so a later shard or database change fails loudly. Read-only SELECTs through the existing <c>mariadb-ace.cnf</c> and the bundled client; it needs the backend's database up, so the lead runs it (Loopback tier);
/// it skips when the client or the cnf is missing. It proves the data in <see cref="FighterGearSet"/> equals the database's, nothing about a live character.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")] // read-only SELECTs: no character is entered
public sealed class FighterGearWorldDataLoopbackTests(ITestOutputHelper output)
{
    private static string Cnf => Path.Combine(BackendProbe.MainCheckoutFiles.Checkout, "server", "runtime", "mariadb-ace.cnf");

    private static string Client => Path.Combine(BackendProbe.MainCheckoutFiles.Checkout, "server", "runtime", "mariadb", "bin", "mariadb.exe");

    private static List<string[]> Select(string sql)
    {
        var info = new ProcessStartInfo(Client) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--defaults-file=" + Cnf);
        info.ArgumentList.Add("-N");
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add(sql);
        using var process = Process.Start(info)!;
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Split('\t'))];
    }

    [LoopbackFact]
    public void Every_pinned_piece_exists_in_ace_world_with_its_difficulty_requirements_mana_slots_and_spells()
    {
        if (!File.Exists(Client) || !File.Exists(Cnf))
        {
            output.WriteLine("skipped: the bundled MariaDB client or its cnf is missing");
            return;
        }

        foreach (var set in new[] { FighterGearSet.Brawler, FighterGearSet.Archer })
        {
            foreach (var piece in set.Pieces)
            {
                var id = piece.Wcid.ToString(CultureInfo.InvariantCulture);
                var weenie = Select($"SELECT class_Name, type FROM ace_world.weenie WHERE class_Id = {id}");
                Assert.True(weenie.Count == 1, $"{piece.Name} ({id}) is no longer a weenie");
                Assert.Equal(piece.ClassName, weenie[0][0]);
                Assert.Equal(piece.WeenieType.ToString(CultureInfo.InvariantCulture), weenie[0][1]);

                var ints = Select($"SELECT type, value FROM ace_world.weenie_properties_int WHERE object_Id = {id} AND type IN (1, 5, 9, 19, 33, 107, 109, 114, 158, 159, 160, 270, 271, 272, 273, 274, 275, 276, 277, 278)")
                    .ToDictionary(r => int.Parse(r[0], CultureInfo.InvariantCulture), r => int.Parse(r[1], CultureInfo.InvariantCulture));
                Assert.Equal((int)piece.ItemType, ints[1]);
                Assert.Equal(piece.Burden, ints.GetValueOrDefault(5));
                Assert.Equal((int)piece.Locations, ints[9]);
                Assert.Equal(piece.ItemDifficulty, ints.GetValueOrDefault(109)); // the Arcane Lore ACE compares (WorldObject_Use.cs:205-209)
                Assert.Equal(piece.ItemMana, ints[107]);
                Assert.False(ints.ContainsKey(114) && ints[114] != 0, piece.Name + " became attuned");
                Assert.False(ints.ContainsKey(33) && ints[33] != 0, piece.Name + " became bonded");
                Assert.Equal(piece.Requirements.Count, new[] { 158, 270, 273, 276 }.Count(ints.ContainsKey));
                foreach (var (req, i) in piece.Requirements.Select((r, i) => (r, i)))
                {
                    var keys = new[] { (158, 159, 160), (270, 271, 272), (273, 274, 275), (276, 277, 278) }[i];
                    Assert.Equal((req.Requirement, req.Type, req.Difficulty), (ints[keys.Item1], ints.GetValueOrDefault(keys.Item2), ints.GetValueOrDefault(keys.Item3)));
                }

                var rate = Select($"SELECT value FROM ace_world.weenie_properties_float WHERE object_Id = {id} AND type = 5");
                Assert.Equal(piece.ManaRate, double.Parse(rate.Single()[0], CultureInfo.InvariantCulture), 6);

                var book = Select($"SELECT spell FROM ace_world.weenie_properties_spell_book WHERE object_Id = {id}").Select(r => uint.Parse(r[0], CultureInfo.InvariantCulture)).Order().ToArray();
                Assert.Equal(piece.Spells.Select(s => s.Id).Order(), book);
                output.WriteLine($"{set.Key}: {piece.Name} ({id}) matches ace_world: difficulty {piece.ItemDifficulty}, {piece.Spells.Count} spells, mana {piece.ItemMana}");
            }
        }
    }
}
