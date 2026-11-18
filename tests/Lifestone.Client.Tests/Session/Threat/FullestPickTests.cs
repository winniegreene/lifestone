using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2d (docs/automation-engine.md stage E2, "one fullest-creature pick"): the one comparison behind the picks of the fullest creature. Each site that moved onto it picks the creature it picked before: here
/// each site's comparison as it stood before the move is kept word for word as the reference, and the site's pick is held against it over mixed creatures (shares that tie, lie inside the band and outside it,
/// blocked lines, the picked target among equals, creatures that hold nothing). The seeds are fixed, so a failure names its case.
/// </summary>
public sealed class FullestPickTests
{
    private const int Cases = 20_000;

    /// <summary>A share on a coarse grid with a fine jitter, so pairs tie exactly, lie a hair inside the hundredth and a hair outside it.</summary>
    private static float Share(Random random) => (float)Math.Clamp((random.Next(0, 21) * 0.05) + (random.Next(-2, 3) * 0.005) + (random.Next(-1, 2) * 0.0001), 0, 1);

    /// <summary>The emergency's and the drain rule's comparison as it stood (<c>FightEmergency.Fullest</c> before the move).</summary>
    private static int EmergencyBefore(ReadOnlySpan<float> healthFractions, ReadOnlySpan<float> distances, ReadOnlySpan<bool> excluded, int count)
    {
        var best = -1;
        for (var i = 0; i < count; i++)
        {
            if (i < excluded.Length && excluded[i])
            {
                continue;
            }

            if (best < 0 || healthFractions[i] > healthFractions[best] + 0.01f || (healthFractions[i] >= healthFractions[best] - 0.01f && distances[i] < distances[best]))
            {
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void The_emergency_and_the_drain_rule_pick_the_creature_they_picked_before()
    {
        var random = new Random(16_052);
        var (health, distance, excluded) = (new float[8], new float[8], new bool[8]);
        for (var n = 0; n < Cases; n++)
        {
            var count = random.Next(0, 9);
            for (var i = 0; i < count; i++)
            {
                (health[i], distance[i], excluded[i]) = (Share(random), random.Next(1, 13) * 2.5f, random.Next(0, 5) == 0);
            }

            ReadOnlySpan<bool> flags = random.Next(0, 4) == 0 ? [] : excluded.AsSpan(0, count); // the fight passes its blocked flags, or none
            if (EmergencyBefore(health, distance, flags, count) != FightEmergency.Fullest(health, distance, flags, count))
            {
                Assert.Fail($"case {n}: shares [{string.Join(", ", health.Take(count))}] at [{string.Join(", ", distance.Take(count))}] excluded [{string.Join(", ", flags.ToArray())}]"); // the words only for a failure: 20,000 cases build none
            }
        }
    }

    /// <summary>The ring anchor's comparison as it stood (<c>FightScenario.ChooseRingAnchor</c> before the move).</summary>
    private static int RingBefore(ReadOnlySpan<FightScenario.RingCandidate> candidates, uint current)
    {
        var best = -1;
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].Clear)
            {
                continue;
            }

            if (best < 0 || candidates[i].Health > candidates[best].Health || (candidates[i].Health == candidates[best].Health && candidates[i].Guid == current))
            {
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void The_rings_anchor_is_the_creature_it_was_before()
    {
        var random = new Random(16_053);
        var candidates = new FightScenario.RingCandidate[8];
        for (var n = 0; n < Cases; n++)
        {
            var count = random.Next(0, 9);
            for (var i = 0; i < count; i++)
            {
                candidates[i] = new FightScenario.RingCandidate(0x80000001u + (uint)i, Share(random), random.Next(0, 4) != 0);
            }

            var current = random.Next(0, 3) == 0 ? 0x80000099u : 0x80000001u + (uint)random.Next(0, 8); // the picked target, among them or not
            var span = candidates.AsSpan(0, count);
            if (RingBefore(span, current) != FightScenario.ChooseRingAnchor(span, current))
            {
                Assert.Fail($"case {n}: {string.Join(", ", span.ToArray())} current 0x{current:X8}");
            }
        }
    }

    [Fact]
    public void The_mana_loops_stamina_source_is_the_creature_it_was_before_and_the_same_lines_are_tested()
    {
        var random = new Random(16_054);
        var (held, clear) = (new uint[8], new bool[8]);
        var (askedBefore, askedNow) = (new List<int>(), new List<int>());
        for (var n = 0; n < Cases; n++)
        {
            var count = random.Next(0, 9);
            for (var i = 0; i < count; i++)
            {
                (held[i], clear[i]) = ((uint)(random.Next(0, 4) == 0 ? 0 : random.Next(0, 6) * 60), random.Next(0, 3) != 0); // some hold nothing, several hold the same
            }

            // as it stood (FightScenario.FullestStamina before the move): more than the best so far, and then a clear line
            var (bestBefore, staminaBefore) = (-1, 0u);
            askedBefore.Clear();
            for (var i = 0; i < count; i++)
            {
                if (held[i] > staminaBefore && Line(askedBefore, clear, i))
                {
                    (bestBefore, staminaBefore) = (i, held[i]);
                }
            }

            var pick = new FullestPick(FullestRule.StaminaDrain);
            var (bestNow, staminaNow) = (-1, 0u);
            askedNow.Clear();
            for (var i = 0; i < count; i++)
            {
                if (pick.Beats(0x80000001u + (uint)i, held[i]) && Line(askedNow, clear, i))
                {
                    pick.Take(i, 0x80000001u + (uint)i, held[i]);
                    (bestNow, staminaNow) = (i, held[i]);
                }
            }

            var same = (bestBefore, staminaBefore) == (bestNow, staminaNow) && askedBefore.Count == askedNow.Count && pick.Index == bestNow && (bestNow < 0 ? double.IsNaN(pick.Value) : pick.Value == staminaNow);
            for (var i = 0; same && i < askedNow.Count; i++)
            {
                same = askedBefore[i] == askedNow[i];
            }

            if (!same)
            {
                Assert.Fail($"case {n}: held [{string.Join(", ", held.Take(count))}] clear [{string.Join(", ", clear.Take(count))}]");
            }
        }

        static bool Line(List<int> asked, bool[] clear, int i)
        {
            asked.Add(i);
            return clear[i];
        }
    }

    [Fact]
    public void The_guards_drain_target_is_the_creature_it_was_before()
    {
        var random = new Random(16_055);
        var takes = new double[8];
        double[] resists = [1.0, 0.9, 0.5, 0.33, 0.1];
        for (var n = 0; n < Cases; n++)
        {
            var count = random.Next(0, 9);
            for (var i = 0; i < count; i++)
            {
                takes[i] = (uint)Math.Max(1, random.Next(0, 8) * 90) * resists[random.Next(0, resists.Length)]; // a health of at least 1 times a resistance over 0, as the guard offers it
            }

            // as it stood (AutopilotVitalsGuard.KnownFullest before the move): the first that takes more than the best so far
            var (bestBefore, bestTake) = (-1, -1.0);
            for (var i = 0; i < count; i++)
            {
                if (takes[i] <= bestTake)
                {
                    continue;
                }

                (bestBefore, bestTake) = (i, takes[i]);
            }

            var pick = new FullestPick(FullestRule.GuardDrain);
            for (var i = 0; i < count; i++)
            {
                pick.Offer(i, 0x80000001u + (uint)i, takes[i], 3 + i);
            }

            if (bestBefore != pick.Index || (bestBefore >= 0 && bestTake != pick.Value))
            {
                Assert.Fail($"case {n}: takes [{string.Join(", ", takes.Take(count))}]");
            }
        }
    }

    [Fact]
    public void The_rules_state_each_picks_differences()
    {
        Assert.Equal(new FullestRule(FullestVital.HealthShare, 0.01f, FullestTie.Nearest, double.NegativeInfinity, 1.0), FullestRule.EmergencyDrain);
        Assert.Equal(new FullestRule(FullestVital.HealthShare, 0f, FullestTie.Kept, double.NegativeInfinity, 1.0), FullestRule.RingAnchor);
        Assert.Equal((FullestVital.Stamina, 0f, FullestTie.FirstMet, 0.0), (FullestRule.StaminaDrain.Vital, FullestRule.StaminaDrain.Band, FullestRule.StaminaDrain.Tie, FullestRule.StaminaDrain.Above));
        Assert.Equal((FullestVital.HealthTake, 0f, FullestTie.FirstMet, -1.0), (FullestRule.GuardDrain.Vital, FullestRule.GuardDrain.Band, FullestRule.GuardDrain.Tie, FullestRule.GuardDrain.Above));
        Assert.True(double.IsNaN(FullestRule.StaminaDrain.Unknown) && double.IsNaN(FullestRule.GuardDrain.Unknown));
    }

    [Fact]
    public void An_unknown_vital_counts_as_the_rule_says()
    {
        // the fight's shares: a creature never told and never hit counts as full, so it is preferred to a hurt one and ties with a full one
        var emergency = new FullestPick(FullestRule.EmergencyDrain);
        Assert.True(emergency.Offer(0, 0x80000001, 0.8, 5));
        Assert.True(emergency.Offer(1, 0x80000002, double.NaN, 9));
        Assert.Equal((1, 1.0), (emergency.Index, emergency.Value));
        Assert.False(emergency.Offer(2, 0x80000003, 1.0, 12)); // as full, farther
        Assert.True(emergency.Offer(3, 0x80000004, double.NaN, 4)); // as full, nearer

        // the stamina source and the guard: an unknown is passed over
        var stamina = new FullestPick(FullestRule.StaminaDrain);
        Assert.False(stamina.Offer(0, 0x80000001, double.NaN));
        Assert.False(stamina.Offer(1, 0x80000002, 0)); // it holds nothing
        Assert.False(stamina.Any);
        Assert.Equal((-1, 0u), (stamina.Index, stamina.Guid));
        Assert.True(stamina.Offer(2, 0x80000003, 120));
        Assert.False(new FullestPick(FullestRule.GuardDrain).Offer(0, 0x80000001, double.NaN));
    }
}

/// <summary>M6 16 E2d: the fullest pick stands in the emergency's per-frame read, so it allocates nothing.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class FullestPickAllocationTests
{
    [Fact]
    public void A_pick_allocates_nothing()
    {
        float[] health = [0.4f, 1f, 0.75f, 1f, 0.2f, 0.995f];
        float[] distance = [5f, 12f, 3f, 6f, 9f, 2f];
        bool[] blocked = [false, false, false, true, false, false];
        FightScenario.RingCandidate[] ring = [new(0x80000001, 0.6f, true), new(0x80000002, 0.9f, false), new(0x80000003, 0.9f, true), new(0x80000004, 0.9f, true)];
        var sum = 0L;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                sum += FightEmergency.Fullest(health, distance, blocked, health.Length);
                sum += FightScenario.ChooseRingAnchor(ring, 0x80000004);
                var pick = new FullestPick(FullestRule.GuardDrain);
                pick.Offer(0, 0x80000001, 300 * 0.9, 4);
                pick.Offer(1, 0x80000002, 375, 8);
                sum += pick.Index;
            }
        });
        Assert.True(sum > 0);
    }
}
