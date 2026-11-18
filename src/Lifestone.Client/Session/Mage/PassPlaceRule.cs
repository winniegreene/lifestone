using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where a pass runs (<see cref="PassPlaceRule.Decide"/>).</summary>
public enum PassPlace
{
    /// <summary>Where she stands.</summary>
    Here,

    /// <summary>She walks home first, by name, and the pass runs there.</summary>
    WalkHome,
}

/// <summary>
/// M6 7 AW (live, 2026-11-06, stretch 12: a pass of 901 s, 13 of 18 lines, in cell 0xA8B50034 on the outskirts with a Mite Scion in the quiet distance; stretch 13's pass in Holtburg lay down at 1.8 to 2.5 stamina a second): a full pass runs at
/// home or wherever nothing that can attack is within the quiet distance, since a hunting ground always has a creature near. More than the ladder's <see cref="LadderVitals.PassHomeOverLines"/> lines to cast with a blocker near her walks her home
/// first; a short pass stays and M6 7 AT's rule shoots the blocker. One walk a pass: a blocker at home too keeps her there, never a loop. Pure.
/// </summary>
public static class PassPlaceRule
{
    /// <summary>Where the pass runs, and why in words.</summary>
    /// <param name="lines">The lines to cast.</param>
    /// <param name="overLines">The ladder's <see cref="LadderVitals.PassHomeOverLines"/>.</param>
    /// <param name="block">What stands between her and lying down, within the quiet distance.</param>
    /// <param name="atHome">True when she already stands at home (<see cref="AtHome"/>).</param>
    /// <param name="walked">True when she already walked home for this pass.</param>
    /// <param name="why">The reason.</param>
    public static PassPlace Decide(int lines, int overLines, RestBlock block, bool atHome, bool walked, out string why)
    {
        var c = CultureInfo.InvariantCulture;
        var count = string.Create(c, $"{lines} line(s) to cast, the line for a walk is over {overLines}");
        if (block == RestBlock.None)
        {
            why = count + ": nothing that can attack is near, so she lies down here";
            return PassPlace.Here;
        }

        if (lines <= overLines)
        {
            why = count + ": a short pass stays, and a blocker in reach is shot first";
            return PassPlace.Here;
        }

        if (atHome || walked)
        {
            why = count + (atHome ? ": she is home already" : ": she walked home once for this pass") + ", so it runs here and a blocker in reach is shot first";
            return PassPlace.Here;
        }

        why = count + ": a creature is near and the pass needs her lying down: home first";
        return PassPlace.WalkHome;
    }

    /// <summary>True when <paramref name="here"/> is within the quiet distance of the home point (<see cref="GroundHome.LifestonePoint"/>, in the lifestone's landblock).</summary>
    /// <param name="here">Where she stands.</param>
    /// <param name="home">The ground's home.</param>
    public static bool AtHome(in CellFrame here, GroundHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return here.CellId >> 16 == home.LifestoneCell >> 16 && System.Numerics.Vector3.Distance(here.Origin, home.LifestonePoint) <= VitalsKeeper.QuietMetres;
    }
}
