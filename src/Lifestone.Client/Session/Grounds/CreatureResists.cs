using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 14 the element line (closes <c>TODO(m6-fight-element-line)</c>): every creature weenie's resistance to the seven war elements, from the world database (<c>creature-resists.json</c>, embedded; its <c>sources</c> name the one
/// read-only SELECT). ACE multiplies a war projectile's damage by the target's <c>ResistX ?? 1.0</c> (<c>Creature.GetResistanceMod(ResistanceType, ...)</c>, Creature_Properties.cs:185-205 @95ef426d), so a creature that records
/// none takes every element whole. The keys are <see cref="Mage.SpellPlan.BoltLineNames"/>' (fire, cold, acid, electric, pierce, bludgeon, slash).
/// <para>
/// M6 16 E1e: one weenie states a resistance under 0 (the Master of the Herd, weenie 11981 of WeenieType.Cow, ResistFire -0.2; it came into the table with the cattle). Where ACE deals damage it floors the modifier at 0
/// (<c>SpellProjectile.CalculateDamage</c>, SpellProjectile.cs:486 and 544; <c>DamageEvent.DoCalculateDamage</c>, DamageEvent.cs:341): a fire bolt takes nothing from it (<c>tools/fight-sim.ps1 matchup 11981</c>, 2026-11-17:
/// "Fire -0.2", every flame spell's hit 0). The row keeps what the weenie states (<see cref="RowOf"/>); the read every decision asks (<see cref="Of"/>) gives ACE's floor.
/// </para>
/// </summary>
public static class CreatureResists
{
    /// <summary>The embedded resource's logical name.</summary>
    public const string ResourceName = "Lifestone.Client.Session.Grounds.creature-resists.json";

    /// <summary>The columns after the wcid, in the file's order (weenie_properties_float 64 to 70).</summary>
    public static IReadOnlyList<string> Elements { get; } = ["slash", "pierce", "bludgeon", "fire", "cold", "acid", "electric"];

    private static readonly Lazy<FrozenDictionary<uint, double[]>> Rows = new(Load);

    /// <summary>How many creature weenies record a resistance.</summary>
    public static int Count => Rows.Value.Count;

    /// <summary>True when the world database records a resistance of <paramref name="wcid"/> (else every element is ACE's 1).</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool Records(uint wcid) => Rows.Value.ContainsKey(wcid);

    /// <summary>
    /// M6 16 E1a (the bestiary's facade): the seven resistances of <paramref name="wcid"/> in the order of <see cref="Elements"/> as the weenie states them (one is under 0: see the class), or null when the world database
    /// records none (every element is ACE's 1). The table's own row, not a copy.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    internal static IReadOnlyList<double>? RowOf(uint wcid) => Rows.Value.TryGetValue(wcid, out var row) ? row : null;

    /// <summary>M6 16 E1a: the weenie classes that record a resistance.</summary>
    internal static IEnumerable<uint> Wcids => Rows.Value.Keys;

    /// <summary>
    /// The resistance of <paramref name="wcid"/> to <paramref name="element"/> (a key of <see cref="Elements"/>) as ACE's damage reads it: the row's, never under 0 (see the class), or ACE's 1 when the weenie records none.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="element">The element.</param>
    public static double Of(uint wcid, string element)
    {
        var index = IndexOf(element);
        return Rows.Value.TryGetValue(wcid, out var row) ? Math.Max(0.0, row[index]) : 1.0;
    }

    /// <summary>The seven resistances of <paramref name="wcid"/> as one text, for a log line.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static string Text(uint wcid) =>
        Records(wcid)
            ? string.Join(" ", Elements.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e} {Of(wcid, e):0.###}")))
            : "none recorded (every element 1)";

    private static int IndexOf(string element)
    {
        for (var i = 0; i < Elements.Count; i++)
        {
            if (string.Equals(Elements[i], element, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException($"'{element}' is not a war element ({string.Join(", ", Elements)})", nameof(element));
    }

    /// <summary>Parses the table's JSON (<c>columns</c> and <c>resists</c> rows).</summary>
    /// <param name="json">The document.</param>
    internal static FrozenDictionary<uint, double[]> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        string[] expected = ["wcid", .. Elements];
        if (!columns.SequenceEqual(expected))
        {
            throw new InvalidDataException($"creature-resists.json: the columns are [{string.Join(", ", columns)}], not [{string.Join(", ", expected)}]");
        }

        var rows = new Dictionary<uint, double[]>();
        foreach (var r in doc.RootElement.GetProperty("resists").EnumerateArray())
        {
            var values = new double[Elements.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = r[i + 1].GetDouble();
                if (!(values[i] >= -1 && values[i] <= 1000))
                {
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"creature-resists.json: weenie {r[0]}'s {Elements[i]} is {values[i]}, outside -1 to 1,000 (the table's largest is 500, weenie 12046, and its least -0.2, weenie 11981)"));
                }
            }

            rows[r[0].GetUInt32()] = values;
        }

        return rows.ToFrozenDictionary();
    }

    private static FrozenDictionary<uint, double[]> Load()
    {
        using var stream = typeof(CreatureResists).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded {ResourceName} is missing from the client assembly");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
