namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The one-answer gate every reply goes through (server/mod/Lifestone.ServerMod/Core/OneAnswer.cs, compiled here by
/// link): the first answer wins on any thread, every later one is dropped, so a client never sees two replies to one
/// request.
/// </summary>
public sealed class OneAnswerTests
{
    [Fact]
    public void Only_the_first_answer_takes_the_gate()
    {
        var gate = new OneAnswer();
        Assert.False(gate.IsTaken);
        Assert.True(gate.TryTake());
        Assert.True(gate.IsTaken);
        Assert.False(gate.TryTake());
        Assert.False(gate.TryTake());
        Assert.True(gate.IsTaken);
    }

    [Fact]
    public void Answers_racing_on_many_threads_take_the_gate_once()
    {
        // A verb answering from the action queue or a task while an exception handler answers too: exactly one wins.
        const int Threads = 16;
        for (var round = 0; round < 50; round++)
        {
            var gate = new OneAnswer();
            using var start = new Barrier(Threads);
            var wins = 0;
            var racers = Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                if (gate.TryTake())
                {
                    Interlocked.Increment(ref wins);
                }
            })).ToArray();
            foreach (var racer in racers)
            {
                racer.Start();
            }

            foreach (var racer in racers)
            {
                racer.Join();
            }

            Assert.Equal(1, wins);
            Assert.True(gate.IsTaken);
        }
    }
}
