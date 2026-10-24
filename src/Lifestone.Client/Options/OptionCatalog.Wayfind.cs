namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The wayfinder's headless scenario (P5a S6), read by the headless host's <c>--scenario=wayfind</c> and parsed by src/Lifestone.Client/Session/Scenarios/WayfindOptions.cs
    /// (<c>WayfindOptions.FromArguments</c>). The character is the fighter template of <c>--account</c> (test-melee: Holtburg Brawler, test-archer: Holtburg Archer, test: Holtburg Hunter).
    /// </summary>
    public static OptionGroup Wayfind() => new("Wayfinder", "headless host",
    [
        new("--to", "CELL[@x,y,z]", "Where --scenario=wayfind runs to (required by that scenario): a cell id in hex, 0xA5B00032, optionally followed by @x,y,z in landblock-local metres (an outdoor cell without a position means its centre; an indoor cell needs one). The run is planned by the wayfinder over tiles the headless streamer builds ahead of the player and walked with the human's keys through IActionApi.Move; it prints 'wayfind leg=out|back ok=<true|false> route_m=<m> run_s=<s> speed_mps=<v> plans=<n> replans=<n> rungs=<n> blocked=<0|1> stuck=<n> corrections=<n> where_max_m=<m> teleports=<n> landblocks_peak=<n>' for each leg and 'wayfind done ok=<true|false>' at the end (report-only in its first gate). The default limit is sized from the straight-line distance at 4.8 m/s plus 300 s."),
        new("--from", "CELL[@x,y,z]", "Where --scenario=wayfind starts (default: where the login left the player). A player more than 5 m from it is placed there by one server-mod 'place' (counted teleports=1) and the run starts from the position the server confirmed; this is the point --wayfind-back returns to."),
        new("--wayfind-back", "on|off", "After the run out, --scenario=wayfind runs back to the point it started from (default off). Each leg prints its own 'wayfind leg=' line.", ValueOptional: true),
    ], OptionScenes.Headless);
}
