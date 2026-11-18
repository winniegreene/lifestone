using ACE.Server.Managers;
using ACE.Server.Mods;

namespace Lifestone.ServerMod;

/// <summary>
/// M6 7 finish: a dead player's coin lies on the corpse, as in retail (<see cref="CorpseCoinRules"/> holds the why and the flag). ACE's public API only: <see cref="PropertyManager.ModifyBool"/> changes ACE's cached value, and ACE's own five-minute
/// worker saves it (Managers/PropertyManager.cs:369-385). The mod opens no connection to the database and writes no row itself.
/// </summary>
internal static class CorpseCoin
{
    /// <summary>Sets the property from the flag and reads it back; one line says which. False, with an error logged, when ACE refuses the key or the read-back differs: the caller then withholds the ready line.</summary>
    /// <remarks>Runs after the world opens, so PropertyManager.Initialize (Program.cs:263) has run; no player can have died yet, because the ready line is what the backend's start waits for.</remarks>
    public static bool Apply()
    {
        var want = CorpseCoinRules.DestroyCoin(Environment.GetEnvironmentVariable(CorpseCoinRules.EnvironmentVariable));
        var was = PropertyManager.GetBool(CorpseCoinRules.Property).Item;
        if (!PropertyManager.ModifyBool(CorpseCoinRules.Property, want))
        {
            ModManager.Log($"{ModLog.CorpseCoin}ACE has no property {CorpseCoinRules.Property}, so the coin rule was not set", ModManager.LogLevel.Error);
            return false;
        }

        var now = PropertyManager.GetBool(CorpseCoinRules.Property).Item;
        if (now != want)
        {
            ModManager.Log($"{ModLog.CorpseCoin}{CorpseCoinRules.Property} read back {now} after setting {want}", ModManager.LogLevel.Error);
            return false;
        }

        var rule = want
            ? $"ACE's destroy ({CorpseCoinRules.EnvironmentVariable}={CorpseCoinRules.Ace})"
            : $"retail, the coin stays on the corpse ({CorpseCoinRules.EnvironmentVariable}={CorpseCoinRules.Ace} restores ACE's destroy)";
        ModManager.Log($"{ModLog.CorpseCoin}{CorpseCoinRules.Property} was {was}, now {now}: {rule}", want ? ModManager.LogLevel.Warn : ModManager.LogLevel.Info);
        return true;
    }
}
