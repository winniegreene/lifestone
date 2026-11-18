using ACE.Server.Managers;
using ACE.Server.Mods;

namespace Lifestone.ServerMod;

/// <summary>
/// M6 villa, the owner's decision of 2026-11-16: buying a house needs no account age (<see cref="HouseAccountAgeRules"/> holds the why and the flag). ACE's public API only: <see cref="PropertyManager.ModifyBool"/> changes ACE's cached value, and
/// ACE's own five-minute worker saves it (Managers/PropertyManager.cs:369-385). The mod opens no connection to the database and writes no row itself, as <see cref="CorpseCoin"/>.
/// </summary>
internal static class HouseAccountAge
{
    /// <summary>Sets the property from the flag and reads it back; one line says which. False, with an error logged, when ACE refuses the key or the read-back differs: the caller then withholds the ready line.</summary>
    /// <remarks>Runs after the world opens, so PropertyManager.Initialize (Program.cs:263) has run; no player is in the world yet, because the ready line is what the backend's start waits for.</remarks>
    public static bool Apply()
    {
        var want = HouseAccountAgeRules.RequireFifteenDays(Environment.GetEnvironmentVariable(HouseAccountAgeRules.EnvironmentVariable));
        var was = PropertyManager.GetBool(HouseAccountAgeRules.Property).Item;
        if (!PropertyManager.ModifyBool(HouseAccountAgeRules.Property, want))
        {
            ModManager.Log($"{ModLog.HouseAccountAge}ACE has no property {HouseAccountAgeRules.Property}, so the house account-age rule was not set", ModManager.LogLevel.Error);
            return false;
        }

        var now = PropertyManager.GetBool(HouseAccountAgeRules.Property).Item;
        if (now != want)
        {
            ModManager.Log($"{ModLog.HouseAccountAge}{HouseAccountAgeRules.Property} read back {now} after setting {want}", ModManager.LogLevel.Error);
            return false;
        }

        var rule = want
            ? $"retail's 15-day account age for any dwelling but an apartment ({HouseAccountAgeRules.EnvironmentVariable}={HouseAccountAgeRules.Ace})"
            : $"no account age to buy a house, the owner's rule ({HouseAccountAgeRules.EnvironmentVariable}={HouseAccountAgeRules.Ace} restores the 15 days)";
        ModManager.Log($"{ModLog.HouseAccountAge}{HouseAccountAgeRules.Property} was {was}, now {now}: {rule}", want ? ModManager.LogLevel.Warn : ModManager.LogLevel.Info);
        return true;
    }
}
