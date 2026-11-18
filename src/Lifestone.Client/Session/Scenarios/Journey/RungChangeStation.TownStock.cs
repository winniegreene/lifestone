using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too): the stop in another town on the way out. After each hop of a way out the ground's town stock lines are read (<see cref="TownStock"/>): where the hop
/// dropped her in the place a vendor's town is reached from by one portal and left by one (the Town Network for Dryreach), and a line of his is due, the stop is made at once: the portal into the town, the buys at the vendor (the
/// resupply's own shop step, <see cref="ResupplyOptions.TownShop"/>), the portal back; the route then goes on from where she stands. No round trip is added: a supply trip, the way back after a death and the first way out all pass the
/// same door.
/// <para>Check, act, check, each step its own line. A line held back for the purse or the load is said once a run. A failure is named once and remembered for the run (<see cref="JourneyFacts.TownStockFailed"/>): a hop into the town that
/// fails with her still at the door costs the stop for every line of that vendor, a line the shop added nothing of costs that line, and the way out goes on without it, never tried again this run; only a failed portal back, which
/// leaves her in the town, fails the rung change by the hop's name, as any hop of a route does.</para>
/// <para>M6 item 14 (m6-goldscarab): the vendors are tried in the profile's order, each from where the stop before it left her (the Town Network: Dryreach's healer, then Uziz's archmage for the Gold and the Pyreal Scarab). A component line no
/// spell of the book burns is not bought and is said once a run (<c>reason=unused</c>, <see cref="TownStock.UnusedWhy"/>).</para>
/// One line a stop: <c>town_stock: town= vendor= wares= asked= bought= hops= ms= ok= reason=</c>.
/// </summary>
public sealed partial class RungChangeStation
{
    private sealed partial class Run
    {
        private IEnumerable<ActionHandle?> TownStockStops()
        {
            var ground = Context.Dungeon;
            if (direction != RungDirection.Out || stopAt is not null || ground.TownStock.Count == 0 || S.State.Self.Position is not { } here)
            {
                yield break; // a way home and a corpse's leg make no stop
            }

            var routes = Context.Routes;
            if (routes.PlaceOf(RungRoute.Hex(here.CellId >> 16)) is not { } place)
            {
                yield break;
            }

            foreach (var vendor in ground.TownStock.Select(l => l.Vendor.Guid).Distinct().ToList())
            {
                var lines = ground.TownStock.Where(l => l.Vendor.Guid == vendor).ToList();
                if (TownStock.EdgesOf(lines[0], place.Key, routes) is not { } edges)
                {
                    continue; // not this town's door
                }

                var c = CultureInfo.InvariantCulture;
                var town = lines[0].Town;
                var seller = lines[0].Vendor;
                // M6 item 14 (m6-goldscarab): a second vendor's stop starts where the first one's portal back set her down: check before that she still stands at the door (a stop whose portal in failed leaves her there; one whose portal back failed has ended the rung change by name)
                if (S.State.Self.Position is not { } now || !place.Landblocks.Contains((ushort)(now.CellId >> 16)))
                {
                    Log(string.Create(c, $"town_stock: check before: the player stands in 0x{S.State.Self.Position?.CellId ?? 0:X8}, no longer in {place.Key}: no stop at {seller.Name} in {town} on this way out"));
                    yield break;
                }

                here = now;
                var room = PackRoomMemory.For(S.State);
                for (var i = 0; i < lines.Count; i++)
                {
                    // M6 14 pack room (m6-pack-room): the last visit's burden budget cut this line's keep; the stop buys to it (never under the line's trip line), said once a run for each cut
                    if (room.KeepOf(lines[i].Wcid) is { } cut && Math.Max(lines[i].TripUnder, cut) is var keep && keep < lines[i].Keep)
                    {
                        if (Context.Facts.TownStockSayOnce(lines[i].Wcid, string.Create(c, $"pack-budget:{keep}")))
                        {
                            Log(string.Create(c, $"town_stock: {lines[i].Name} at {seller.Name} in {town}: the pack budget of the last visit cut its keep {lines[i].Keep} -> {keep} (m6-pack-room): the stop buys to {keep}"));
                        }

                        lines[i] = lines[i] with { Keep = keep };
                    }
                }

                var plan = TownStock.Plan(lines, ground, S.State, Context.Facts.TownStockGivenUp, Context.Spells); // M6 item 14 (m6-goldscarab): the spell table reads whether a component line is burned by a spell of the book
                var into = RungPortal.Of(edges.Into, routes);
                var back = RungPortal.Of(edges.Back, routes);
                var gate = into.Refusal(_level) ?? back.Refusal(_level);
                var orders = gate is null ? plan.Where(v => v.Kind == TownStockKind.Due).Select(v => new TownStockOrder(v.Line, v.Count)).ToList() : [];
                foreach (var v in plan)
                {
                    var token = v.Kind is TownStockKind.HeldBack or TownStockKind.Failed or TownStockKind.NotNeeded ? v.Token : v.Kind == TownStockKind.Due && gate is not null ? gate : null;
                    if (token is not null && Context.Facts.TownStockSayOnce(v.Line.Wcid, token))
                    {
                        Log(string.Create(c, $"town_stock: no {v.Line.Name} at {seller.Name} in {town} (reason={token}): {(v.Kind == TownStockKind.Due ? $"level {_level} does not pass the portals of the stop" : v.Why)}; said once a run, the way out goes on"));
                    }
                }

                if (orders.Count == 0)
                {
                    continue;
                }

                // M6 item 14 (m6-purse; live 2026-11-12: a main pack with 0 of 102 slots free): check before, the pack. The shop step refuses every ware with no free slot of the main pack (ResupplyRun.BuyLine: "the main pack is full"),
                // so the stop would pass two portals, buy nothing and count as a failed stop. Named once a run, no try counted; the next way out with room makes the stop.
                if (Quartermaster.PackView.MainPackFree(S.State) <= 0)
                {
                    if (Context.Facts.TownStockSayOnce(orders[0].Line.Wcid, "pack-full"))
                    {
                        Log(string.Create(c, $"town_stock: no {string.Join(", ", orders.Select(o => o.Line.Name))} at {seller.Name} in {town} (reason=pack-full): the main pack has no free slot and a ware bought needs one: no stop; said once a run, the way out goes on and the next way out with room makes the stop"));
                    }

                    continue;
                }

                var started = S.Clock.Now;
                var stage = Stage;
                Stage = "town-stock:" + town;
                var wares = string.Join('+', orders.Select(o => o.Line.Wcid.ToString(c)));
                var asked = string.Join('+', orders.Select(o => o.Count.ToString(c)));
                Log(string.Create(c, $"town_stock: check before: in {place.Key} (cell 0x{here.CellId:X8}): {string.Join("; ", plan.Where(v => v.Kind == TownStockKind.Due).Select(v => v.Why))}; {plan.Where(v => v.Kind == TownStockKind.Due).Sum(v => v.Cost)} pyreals in all; hops {into.Key},{back.Key}"));
                var bought = new int[orders.Count];
                string? failed = null;
                Context.Facts.TownStockStopBegins(); // M6 14: a failed stop is tried again on the next way out, JourneyFacts.TownStockTries stops a ware
                var hop = new PortalHopLeg(Context, into, string.Create(c, $"the stop at {seller.Name} in {town}, hop 1 of 2"));
                while (hop.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                {
                    yield return null;
                }

                if (hop.Failure is not null)
                {
                    failed = "hop:" + (hop.FailureName ?? into.Key) + ": " + hop.Failure;
                    foreach (var line in lines)
                    {
                        Context.Facts.TownStockFailed(line.Wcid, failed);
                    }

                    if (S.State.Self.Position is not { } still || !place.Landblocks.Contains((ushort)(still.CellId >> 16)))
                    {
                        Fail(hop.FailureName ?? into.Key + ":failed", string.Create(c, $"the stop's portal into {town} failed and the player is no longer in {place.Key}: {hop.Failure}"));
                    }
                }
                else
                {
                    _hops++;
                    var before = orders.Select(o => GearInventory.CountOf(S.State, [], o.Line.Wcid)).ToList();
                    var shop = new ResupplyStation(new ResupplyOptions(TownShop: orders)).Start(Context);
                    while (shop.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                    {
                        yield return null;
                    }

                    for (var i = 0; i < orders.Count; i++)
                    {
                        bought[i] = Math.Max(0, GearInventory.CountOf(S.State, [], orders[i].Line.Wcid) - before[i]);
                        room.BoughtAfterVisit(orders[i].Line.Wcid, bought[i]); // M6 14 pack room: the next visit's measure counts it as held from the departure, not as used
                        if (bought[i] == 0)
                        {
                            failed ??= "shop: " + shop.VerdictFields();
                            Context.Facts.TownStockFailed(orders[i].Line.Wcid, "shop: " + shop.VerdictFields());
                        }
                    }

                    var home = new PortalHopLeg(Context, back, string.Create(c, $"the stop at {seller.Name} in {town}, hop 2 of 2"));
                    while (home.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                    {
                        yield return null;
                    }

                    if (home.Failure is not null)
                    {
                        foreach (var line in lines)
                        {
                            Context.Facts.TownStockFailed(line.Wcid, "hop:" + (home.FailureName ?? back.Key) + ": " + home.Failure);
                        }

                        Log(string.Create(c, $"town_stock: town={town} vendor={seller.Wcid} wares={wares} asked={asked} bought={string.Join('+', bought)} hops={into.Key},{back.Key} ms={(S.Clock.Now - started) * 1000:0} ok=no reason=hop:{home.FailureName ?? back.Key}"));
                        Fail(home.FailureName ?? back.Key + ":failed", string.Create(c, $"the stop's portal back from {town} failed: {home.Failure}"));
                    }

                    _hops++;
                }

                Stage = stage;
                var verdict = string.Create(c, $"town_stock: town={town} vendor={seller.Wcid} wares={wares} asked={asked} bought={string.Join('+', bought)} hops={into.Key},{back.Key} ms={(S.Clock.Now - started) * 1000:0} ok={(failed is null ? "yes" : "no")} reason={(failed is null ? "bought" : failed.Split(':')[0])}");
                if (failed is null)
                {
                    Log(verdict);
                }
                else
                {
                    Context.Log.Warn("journey", $"[live {StationName}] {verdict}: {failed}; the way out goes on without it; {(lines.All(l => Context.Facts.TownStockGivenUp(l.Wcid) is not null) ? "it is not tried again this run" : "it is tried again on the next way out")} (at most {JourneyFacts.TownStockTries} failed stops a ware a run)");
                }
            }
        }
    }
}
