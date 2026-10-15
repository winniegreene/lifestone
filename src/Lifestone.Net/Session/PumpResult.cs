namespace Lifestone.Net.Session;

/// <summary>What one <see cref="IAcSession.Pump"/> did.</summary>
/// <param name="Datagrams">Datagrams admitted from the socket (every waiting one is admitted, whatever the budget).</param>
/// <param name="Messages">Messages dispatched to the sink within the budget.</param>
/// <param name="Pending">Complete messages left for the next pump because the budget ran out.</param>
/// <param name="BudgetExhausted">True when dispatching stopped because the budget was spent.</param>
public readonly record struct PumpResult(int Datagrams, int Messages, int Pending, bool BudgetExhausted);
