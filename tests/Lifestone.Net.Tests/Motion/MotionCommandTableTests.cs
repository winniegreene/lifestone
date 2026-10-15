using System.Reflection;
using Lifestone.Data.Animation;
using DrwMotionCommand = DatReaderWriter.Enums.MotionCommand;

namespace Lifestone.Net.Tests.Motion;

/// <summary>
/// <see cref="MotionCommandTable"/>, the 11.6096 u16 → u32 command table (docs/protocol.md §7): every low word is its
/// own ordinal, the pinned 11.6096 ids hold, it is DatReaderWriter 2.1.9's table with exactly the documented 2015
/// corrections, ACE's stale ids lose to it, and (N9) every row of the user's own exe table matches it when the dump is in
/// cache/protocol/.
/// </summary>
public sealed class MotionCommandTableTests
{
    /// <summary>The ordinals where the table departs from DatReaderWriter's enum (MotionCommandTable.cs's header).</summary>
    private static readonly Dictionary<ushort, (uint Id, string Name)> Corrections = new()
    {
        [0x000] = (0x80000000, "Invalid"),
        [0x110] = (0x10000110, "CombatEat"),
        [0x111] = (0x10000111, "CombatDrink"),
        [0x112] = (0x09000112, "NextMonster"),
        [0x113] = (0x09000113, "PreviousMonster"),
        [0x114] = (0x09000114, "ClosestMonster"),
        [0x115] = (0x09000115, "NextPlayer"),
        [0x116] = (0x09000116, "PreviousPlayer"),
        [0x117] = (0x09000117, "ClosestPlayer"),
        [0x161] = (0x09000161, "StretchUI"),
        [0x19B] = (0x1000019B, "AI_TelegraphCast"),
    };

    [Fact]
    public void Every_low_word_is_its_own_ordinal_and_names_one_command()
    {
        Assert.Equal(412, MotionCommandTable.Count);
        var seen = new HashSet<uint>();
        for (var i = 0; i < MotionCommandTable.Count; i++)
        {
            Assert.True(MotionCommandTable.TryFromWire((ushort)i, out var command));
            Assert.Equal(i, MotionCommandTable.ToWire(command));
            Assert.True(seen.Add(command), $"0x{command:X8} appears twice");
            Assert.True(MotionCommandTable.Contains(command));
            Assert.False(string.IsNullOrEmpty(MotionCommandTable.NameOf(command)));
            Assert.True(MotionCommandTable.TryGetName((ushort)i, out var name));
            Assert.Equal(MotionCommandTable.NameOf(command), name);
        }
    }

    [Theory]
    [InlineData(0x80000000u, "Invalid")]
    [InlineData(0x41000003u, "Ready")]
    [InlineData(0x45000005u, "WalkForward")]
    [InlineData(0x44000007u, "RunForward")]
    [InlineData(0x8000003Du, "NonCombat")]
    [InlineData(0x1000010Fu, "SkillHealOther")]
    [InlineData(0x10000110u, "CombatEat")]
    [InlineData(0x10000111u, "CombatDrink")]
    [InlineData(0x09000112u, "NextMonster")]
    [InlineData(0x43000118u, "SnowAngelState")]
    [InlineData(0x4300011Bu, "AFKState")]
    [InlineData(0x4300011Cu, "MeditateState")]
    [InlineData(0x1000011Eu, "LogOut")]
    [InlineData(0x09000161u, "StretchUI")]
    [InlineData(0x10000165u, "Fishing")]
    [InlineData(0x1000018Du, "AttackMed6")]
    [InlineData(0x1000019Bu, "AI_TelegraphCast")]
    public void The_pinned_11_6096_ids_hold(uint command, string name)
    {
        Assert.Equal(command, MotionCommandTable.FromWire(MotionCommandTable.ToWire(command)));
        Assert.Equal(name, MotionCommandTable.NameOf(command));
    }

    [Fact]
    public void It_is_not_the_2013_table()
    {
        // In the 2013 table AFKState was 0x43000118 and LogOut 0x1000011B; 11.6096 moved both by +3.
        Assert.Equal(0x4300011Bu, MotionCommandTable.FromWire(0x011B));
        Assert.Equal("SnowAngelState", MotionCommandTable.NameOf(0x43000118));
        Assert.Null(MotionCommandTable.NameOf(0x1000011B));
        Assert.Null(MotionCommandTable.NameOf(0x0900010F)); // the 2013 NextMonster
    }

    [Fact]
    public void Low_words_past_the_table_and_foreign_ids_are_unknown()
    {
        Assert.False(MotionCommandTable.TryFromWire(412, out var command));
        Assert.Equal(0u, command);
        Assert.False(MotionCommandTable.TryFromWire(ushort.MaxValue, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => MotionCommandTable.FromWire(412));
        Assert.False(MotionCommandTable.TryGetName(412, out var name));
        Assert.Null(name);
        Assert.Null(MotionCommandTable.NameOf(0)); // ACE's and DatReaderWriter's Invalid; retail's is 0x80000000
        Assert.False(MotionCommandTable.Contains(0x41000004));
    }

    [Fact]
    public void It_is_DatReaderWriters_table_with_exactly_the_documented_corrections()
    {
        var names = Enum.GetNames<DrwMotionCommand>();
        Assert.Equal(409, names.Length);
        foreach (var drwName in names)
        {
            var drw = (uint)Enum.Parse<DrwMotionCommand>(drwName);
            var lowWord = (ushort)drw;
            if (Corrections.ContainsKey(lowWord))
            {
                continue;
            }

            Assert.Equal(drw, MotionCommandTable.FromWire(lowWord));
            Assert.Equal(drwName, MotionCommandTable.NameOf(drw));
        }

        foreach (var (lowWord, expected) in Corrections)
        {
            Assert.Equal(expected.Id, MotionCommandTable.FromWire(lowWord));
            Assert.Equal(expected.Name, MotionCommandTable.NameOf(expected.Id));
        }

        var drwLowWords = names.Select(n => (ushort)(uint)Enum.Parse<DrwMotionCommand>(n)).ToHashSet();
        Assert.Equal(new[] { 0x115, 0x116, 0x117 }, Enumerable.Range(0, MotionCommandTable.Count).Where(i => !drwLowWords.Contains((ushort)i)));
    }

    [Fact]
    public void Every_named_command_constant_is_in_the_table()
    {
        var constants = typeof(MotionCommands).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(uint) && !f.Name.EndsWith("Mask", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(constants);
        foreach (var field in constants)
        {
            var command = (uint)field.GetRawConstantValue()!;
            Assert.True(MotionCommandTable.Contains(command), $"MotionCommands.{field.Name} 0x{command:X8}");
        }
    }

    /// <summary>
    /// ACE's <c>ACE.Entity/Enum/MotionCommand.cs</c> at 95ef426d (409 values, the same as DatReaderWriter's apart from
    /// two names) keeps five UI commands at their pre-2015 ids, 0x09000110-0x09000114, whose low words 11.6096 gave to
    /// CombatEat, CombatDrink, NextMonster, PreviousMonster and ClosestMonster (docs/protocol.md §7). The 11.6096 table
    /// wins: those five ACE ids are not in it, and their low words decode to the 2015 commands. ACE's Invalid is 0;
    /// retail's is 0x80000000. Checked once against the ACE source when N9 landed; pinned here as data, since the tests
    /// do not read vendor-eval/.
    /// </summary>
    [Theory]
    [InlineData(0x09000110u, "PreviousMonster", 0x10000110u, "CombatEat")]
    [InlineData(0x09000111u, "ClosestMonster", 0x10000111u, "CombatDrink")]
    [InlineData(0x09000112u, "NextPlayer", 0x09000112u, "NextMonster")]
    [InlineData(0x09000113u, "PreviousPlayer", 0x09000113u, "PreviousMonster")]
    [InlineData(0x09000114u, "ClosestPlayer", 0x09000114u, "ClosestMonster")]
    [InlineData(0x00000000u, "Invalid", 0x80000000u, "Invalid")]
    public void Aces_stale_ids_lose_to_the_11_6096_table(uint aceId, string aceName, uint retailId, string retailName)
    {
        var lowWord = MotionCommandTable.ToWire(aceId);
        Assert.Equal(retailId, MotionCommandTable.FromWire(lowWord));
        Assert.Equal(retailName, MotionCommandTable.NameOf(retailId));
        if (aceId != retailId || aceName != retailName)
        {
            Assert.False(MotionCommandTable.NameOf(aceId) == aceName, $"ACE's 0x{aceId:X8} {aceName} is in the 11.6096 table");
        }

        // ACE's NextPlayer..ClosestPlayer moved up by 3 in 11.6096.
        if (aceName is "NextPlayer" or "PreviousPlayer" or "ClosestPlayer")
        {
            var shifted = aceId + 3;
            Assert.Equal(aceName, MotionCommandTable.NameOf(shifted));
        }
    }

    /// <summary>
    /// The user's own 11.6096 acclient.exe is the oracle (docs/protocol.md §7, §9.4): every row of its command table,
    /// dumped by <c>tools/protocol/dump_command_table.py</c> into <c>cache/protocol/</c> (never committed), has this
    /// table's id at its ordinal, and the same name apart from three the exe spells differently. Skipped, with the command
    /// that makes the dump, when the dump is absent.
    /// </summary>
    [CommandTableDumpFact]
    public void Every_row_of_the_exe_table_matches()
    {
        var path = CommandTableDumpFactAttribute.Find()!;
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();

        Assert.Equal(MotionCommandTable.Count, rows.Count);
        var spellings = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            Assert.Equal(3, row.Length);
            Assert.Equal(i, int.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture));
            var id = Convert.ToUInt32(row[1], 16);
            Assert.True(MotionCommandTable.FromWire((ushort)i) == id, $"ordinal 0x{i:X3}: the exe has 0x{id:X8}, the table 0x{MotionCommandTable.FromWire((ushort)i):X8}");
            var name = row[2];
            if (ExeSpellings.TryGetValue(name, out var spelled))
            {
                spellings.Add(name);
                name = spelled;
            }

            Assert.True(MotionCommandTable.NameOf(id) == name, $"0x{id:X8}: the exe names it {row[2]}, the table {MotionCommandTable.NameOf(id)}");
        }

        Assert.Equal(ExeSpellings.Keys.Order(), spellings.Order());
    }

    /// <summary>The three names the 11.6096 exe spells differently from DatReaderWriter (this table keeps DRW's).</summary>
    private static readonly Dictionary<string, string> ExeSpellings = new()
    {
        ["2HandedSwordCombat"] = "TwoHandedSwordCombat",
        ["2HandedStaffCombat"] = "TwoHandedStaffCombat",
        ["BreatheFlame_"] = "BreatheFlame",
    };
}

/// <summary>
/// A [Fact] that needs the user's exe command-table dump, <c>cache/protocol/command-table-11.6096.tsv</c>, in this
/// checkout or the main checkout around it; reported as skipped, with the command that makes it, when it is absent.
/// </summary>
public sealed class CommandTableDumpFactAttribute : FactAttribute
{
    /// <summary>The dump's path below a checkout.</summary>
    public static readonly string[] RelativePath = ["cache", "protocol", "command-table-11.6096.tsv"];

    public CommandTableDumpFactAttribute()
    {
        if (Find() is null)
        {
            Skip = "the 11.6096 command-table dump is not in cache/protocol/; make it with "
                + "'python -I tools/protocol/dump_command_table.py <acclient.exe> cache/protocol/command-table-11.6096.tsv' "
                + "(read-only; the exe's MD5 is checked first).";
        }
    }

    /// <summary>The dump in this checkout, else in the main checkout; null when neither has it.</summary>
    public static string? Find()
    {
        foreach (var root in new[] { Lifestone.Client.Tests.RepositoryFiles.Root, Lifestone.Client.Tests.RepositoryFiles.MainCheckout })
        {
            var path = Path.Combine([root, .. RelativePath]);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
