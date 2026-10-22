using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The loot rule's options (<c>--loot</c>, <c>--loot-types</c>, <c>--loot-min-value</c>, <c>--loot-max-burden</c>; M4 2.2 s0), parsed from the command line like <see cref="Scenarios.HuntOptions"/>, which carries them
/// (<see cref="Scenarios.HuntOptions.Loot"/>) so the headless host and the hunt leg read them with the hunt's own options. A value that does not parse is reported in <see cref="Errors"/> and the option keeps its default.
/// </summary>
public sealed record LootOptions
{
    /// <summary>
    /// The item types the shopkeeper Renald the Elder (weenie 718, "holtburgshopkeep") buys: his <c>MerchandiseItemTypes</c> 1208248231 = 0x480467A7 (the lead's read-only SELECT of the shard's weenie table, 2026-10-21):
    /// MeleeWeapon 0x1, Armor 0x2, Clothing 0x4, Food 0x20, Misc 0x80, MissileWeapon 0x100, Container 0x200, Useless 0x400, Writable 0x2000, Key 0x4000, PromissoryNote 0x40000, LifeStone 0x08000000 and Gameboard 0x40000000.
    /// Not in it: Jewelry 0x8, Creature 0x10, Money 0x40, Gem 0x800, SpellComponents 0x1000, Caster 0x8000, ManaStone 0x80000 and the crafting types.
    /// This is the shard's database value, the fallback; Renald's own 0062 mask (read once this session) replaces it in the default rule (<see cref="LootContext.VendorMask"/>, M4 3.2 s4a; capture C2 confirmed the two equal).
    /// </summary>
    public const uint ShopMask = 0x480467A7;

    /// <summary>The weenie class id of the vendor the loot rule follows: Shopkeeper Renald the Elder (the sale's vendor too). Another vendor's 0062 never changes the loot rule.</summary>
    public const uint ShopkeeperWcid = 718;

    /// <summary>Every item type (<c>--loot-types=all</c>): the type test is skipped, whatever the item's type bits are.</summary>
    public const uint AllTypes = uint.MaxValue;

    /// <summary>The default least value of a looted item (<c>--loot-min-value</c>): the vendor's DoD 8 rule, "Value above 0".</summary>
    public const int DefaultMinValue = 1;

    /// <summary>The largest <c>--loot-min-value</c>: the vendor's own most-valuable item (<see cref="LootRules.ShopMaxValue"/>).</summary>
    public const int MaxMinValue = 1_000_000;

    /// <summary>The default burden limit (<c>--loot-max-burden</c>): the whole capacity (100%, the inventory page's load).</summary>
    public const int DefaultMaxBurdenPercent = 100;

    /// <summary>The largest <c>--loot-max-burden</c>: 300% is the end of the inventory page's load meter.</summary>
    public const int MaxBurdenPercentLimit = 300;

    /// <summary>The default options.</summary>
    public static LootOptions Default { get; } = new();

    /// <summary>True (default) when the hunt loots its kills' corpses (<c>--loot</c>); off restores M4 2.1's hunt: open, list, close.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The item-type bits a looted item must have one of (<c>--loot-types</c>: <c>shop</c> = <see cref="ShopMask"/>, <c>all</c> = <see cref="AllTypes"/>, or a hex mask).</summary>
    public uint TypeMask { get; init; } = ShopMask;

    /// <summary>The least value of a looted item (<c>--loot-min-value</c>).</summary>
    public int MinValue { get; init; } = DefaultMinValue;

    /// <summary>The share of the capacity the burden may reach with the item taken, in percent (<c>--loot-max-burden</c>).</summary>
    public int MaxBurdenPercent { get; init; } = DefaultMaxBurdenPercent;

    /// <summary>Every problem found, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The rule these options describe.</summary>
    public LootRules Rules() => new(TypeMask, MinValue, LootRules.ShopMaxValue, MaxBurdenPercent);

    /// <summary>Reads the loot options out of the parsed arguments (the others are ignored).</summary>
    public static LootOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new LootOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            Apply(argument, errors, ref o);
        }

        return o with { Errors = errors };
    }

    /// <summary>
    /// Applies <paramref name="argument"/> to <paramref name="options"/> when it is a loot option; a bad value adds a line to <paramref name="errors"/> and leaves the option as it was.
    /// </summary>
    /// <returns>True when the argument is one of the loot options (good or bad).</returns>
    public static bool Apply(CommandLineArgument argument, List<string> errors, ref LootOptions options)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var known = errors.Count;
        switch (argument.Name)
        {
            case "--loot":
                options = options with { Enabled = OptionValues.Switch(argument, errors, options.Enabled) };
                return true;
            case "--loot-types":
                if (TryMask(argument.Value, out var mask))
                {
                    options = options with { TypeMask = mask };
                }
                else
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not shop, all or a non-zero hex mask");
                }

                return true;
            case "--loot-min-value":
                var min = OptionValues.Int(argument, errors, options.MinValue);
                if (errors.Count == known && min is >= 0 and <= MaxMinValue)
                {
                    options = options with { MinValue = min };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to {MaxMinValue}");
                }

                return true;
            case "--loot-max-burden":
                var burden = OptionValues.Int(argument, errors, options.MaxBurdenPercent);
                if (errors.Count == known && burden is >= 1 and <= MaxBurdenPercentLimit)
                {
                    options = options with { MaxBurdenPercent = burden };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxBurdenPercentLimit} percent");
                }

                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"loot={(Enabled ? "on" : "off")} types=0x{TypeMask:X8} min_value={MinValue} max_burden={MaxBurdenPercent}%");

    private static bool TryMask(string value, out uint mask)
    {
        switch (value)
        {
            case "shop":
                mask = ShopMask;
                return true;
            case "all":
                mask = AllTypes;
                return true;
        }

        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length > 0 && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out mask) && mask != 0)
        {
            return true;
        }

        mask = 0;
        return false;
    }
}
