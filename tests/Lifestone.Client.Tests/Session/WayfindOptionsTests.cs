using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>The wayfind scenario's options (P5a S6): <c>--to</c>, <c>--from</c> and <c>--wayfind-back</c> parse, bad values are reported without the value, the group is registered for the headless host, and the corridor helper names the landblocks beyond the ring.</summary>
public sealed class WayfindOptionsTests
{
    private static WayfindOptions Parse(params string[] args) => WayfindOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_defaults_are_no_places_and_no_return()
    {
        var o = Parse();
        Assert.Empty(o.Errors);
        Assert.Null(o.To);
        Assert.Null(o.From);
        Assert.False(o.Back);
    }

    [Fact]
    public void Every_option_is_read()
    {
        var o = Parse("--to=0xA5B00032", "--from=0xA9B40019@84,7.7,94", "--wayfind-back");
        Assert.Empty(o.Errors);
        Assert.Equal(new WayfindPlace(0xA5B00032, null), o.To);
        Assert.Equal(new WayfindPlace(0xA9B40019, new Vector3(84f, 7.7f, 94f)), o.From);
        Assert.True(o.Back);
        Assert.Equal("0xA9B40019@84,7.7,94", o.From!.Value.ToString());
        Assert.False(Parse("--wayfind-back=off").Back);
        Assert.Equal(new WayfindPlace(0x9FB50039, null), Parse("--to=9FB50039").To); // the 0x is optional
        Assert.Equal(0xA9B4013Fu, Parse("--to=0xA9B4013F@10,20.5,-3").To!.Value.CellId); // an indoor cell with its position
    }

    [Theory]
    [InlineData("--to=0xA5B0003", "--to: needs a cell id of eight hex digits (0xA5B00032), optionally followed by @x,y,z; the value is not shown")]
    [InlineData("--to=north", "--to: needs a cell id of eight hex digits (0xA5B00032), optionally followed by @x,y,z; the value is not shown")]
    [InlineData("--from=0xA5B00000", "--from: is not an outdoor cell (0001 to 0040 in the low word) or an EnvCell (0100 to FFFD); the value is not shown")]
    [InlineData("--to=0xFFB00032", "--to: names a landblock off the map; the value is not shown")]
    [InlineData("--to=0xA5B00032@1,2", "--to: needs three numbers after the @ (x,y,z in landblock-local metres); the value is not shown")]
    [InlineData("--from=0xA9B4013F", "--from: is an indoor cell and needs a position (0xA9B4013F@x,y,z); the scenario does not guess a room's floor (TODO(wayfind-indoor-default)); the value is not shown")]
    public void Bad_places_are_reported_without_the_value_and_keep_the_default(string argument, string expected)
    {
        var o = Parse(argument);
        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Null(o.To);
        Assert.Null(o.From);
    }

    [Fact]
    public void A_bad_switch_is_reported()
    {
        var o = Parse("--wayfind-back=maybe");
        Assert.Single(o.Errors);
        Assert.False(o.Back);
    }

    [Fact]
    public void The_wayfinder_options_are_registered_for_the_headless_host_only()
    {
        string[] names = ["--to", "--from", "--wayfind-back"];
        var group = Assert.Single(OptionCatalog.Registry.Groups, g => g.Feature == "Wayfinder");
        Assert.Equal(names, group.Options.Select(o => o.Name));
        Assert.All(names, n => Assert.Equal(OptionScenes.Headless, OptionCatalog.Registry.ScenesOf(n)));
        var scenario = Assert.Single(OptionCatalog.Registry.Groups.SelectMany(g => g.Options), o => o.Name == "--scenario" && o.Value is not null && o.Value.Contains("holtburg-laps", StringComparison.Ordinal));
        Assert.EndsWith("|wayfind", scenario.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_limit_is_sized_from_the_straight_line_at_4_8_metres_a_second_and_doubles_with_the_return()
    {
        var one = WayfindScenario.QuitAfterSeconds(Parse("--to=0xA5B00032"));
        var back = WayfindScenario.QuitAfterSeconds(Parse("--to=0xA5B00032", "--wayfind-back"));
        // Holtburg's lifestone to the Low B belt is about 1 km in a straight line: 1.5 x 1,000 m / 4.8 = 312 s plus 300 s a leg and 180 s for the login.
        Assert.InRange(one, 700, 900);
        Assert.Equal(WayfindScenario.LoginMarginSeconds + (2 * (one - WayfindScenario.LoginMarginSeconds)), back, 3);
        var far = WayfindScenario.QuitAfterSeconds(Parse("--to=0x9FB50039", "--wayfind-back"));
        Assert.True(far > back);
        Assert.InRange(Math.Abs(WayfindScenario.QuitAfterSeconds(Parse("--to=0xA5B00032", "--from=0xA9B40019@84,7.7,94")) - one), 0, 5); // the lifestone is where the default starts
    }

    [Fact]
    public void The_corridor_names_the_next_landblocks_beyond_the_ring_along_the_route_and_on_to_the_goal()
    {
        // A body in block (10, 10) heading east with a route that stops at the horizon, 250 m on; the goal is 2 km east.
        var body = new Vector2((10 * 192f) + 96f, (10 * 192f) + 96f);
        var waypoints = new[] { body, body + new Vector2(250f, 0f) };
        var goal = body + new Vector2(2000f, 0f);
        var ahead = HeadlessStreamer.AheadBlocks(body, waypoints, goal, 2);
        Assert.Equal([(12u << 24) | (10u << 16), (13u << 24) | (10u << 16)], ahead); // blocks 11 is in the ring; 12 and 13 are named
        Assert.Empty(HeadlessStreamer.AheadBlocks(body, waypoints, goal, 0));
        Assert.Equal([(12u << 24) | (10u << 16)], HeadlessStreamer.AheadBlocks(body, waypoints, goal, 1));
        // A goal inside the ring names nothing; no waypoints still walks the straight line to the goal.
        Assert.Empty(HeadlessStreamer.AheadBlocks(body, waypoints, body + new Vector2(100f, 0f), 2));
        Assert.Equal(2, HeadlessStreamer.AheadBlocks(body, [], goal, 2).Count);
        // A diagonal route: the first blocks outside the ring in order of travel.
        var diagonal = HeadlessStreamer.AheadBlocks(body, [], body - new Vector2(900f, 900f), 2);
        Assert.Equal([(8u << 24) | (8u << 16), (7u << 24) | (7u << 16)], diagonal);
    }
}
