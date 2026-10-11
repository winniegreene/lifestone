using Lifestone.Client.Objects;

namespace Lifestone.Client.Tests.Objects;

public sealed class InstanceBatchesTests
{
    [Fact]
    public void Random_moves_match_a_reference_model_and_mark_exactly_the_touched_batches()
    {
        const int batches = 7;
        const int instances = 200;
        var sut = new InstanceBatches(batches, instances);
        var reference = new int[instances];
        Array.Fill(reference, -1);
        var random = new Random(1234);
        for (var round = 0; round < 500; round++)
        {
            var touched = new HashSet<int>();
            for (var move = 0; move < random.Next(1, 30); move++)
            {
                var instance = random.Next(instances);
                var to = random.Next(-1, batches);
                var from = reference[instance];
                sut.Move(instance, to);
                if (from != to)
                {
                    if (from >= 0)
                    {
                        touched.Add(from);
                    }

                    if (to >= 0)
                    {
                        touched.Add(to);
                    }
                }

                reference[instance] = to;
            }

            Assert.Equal(touched.OrderBy(b => b), sut.TakeDirty().OrderBy(b => b));
            Assert.Equal(0, sut.DirtyCount);
            for (var b = 0; b < batches; b++)
            {
                var expected = Enumerable.Range(0, instances).Where(i => reference[i] == b).ToHashSet();
                Assert.Equal(expected.Count, sut.Members(b).Count);
                Assert.True(expected.SetEquals(sut.Members(b)));
            }

            for (var i = 0; i < instances; i++)
            {
                Assert.Equal(reference[i], sut.BatchOf(i));
            }
        }
    }

    [Fact]
    public void A_move_to_the_same_batch_changes_nothing_and_mark_dirty_is_idempotent()
    {
        var sut = new InstanceBatches(2, 3);
        sut.Move(0, 1);
        Assert.Equal([1], sut.TakeDirty());
        sut.Move(0, 1);
        Assert.Empty(sut.TakeDirty());
        sut.MarkDirty(0);
        sut.MarkDirty(0);
        Assert.Equal([0], sut.TakeDirty());
    }
}
