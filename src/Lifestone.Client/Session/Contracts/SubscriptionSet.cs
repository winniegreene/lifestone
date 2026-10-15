namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Several subscriptions (or any disposables) released together, newest first: what a consumer that subscribes to many
/// events keeps, and disposes when it goes away (a Godot node in <c>_ExitTree</c>, a scenario at its end). Lifestone's
/// own helper. Game thread only, like the bus.
/// </summary>
/// <example>
/// <code>
/// _subscriptions.Add(session.Events.Subscribe&lt;ObjectCreated&gt;(OnCreated));
/// _subscriptions.Add(session.Events.Subscribe&lt;ObjectDeleted&gt;(OnDeleted));
/// // later
/// _subscriptions.Dispose();
/// </code>
/// </example>
public sealed class SubscriptionSet : IDisposable
{
    private readonly List<IDisposable> _items = [];

    /// <summary>The subscriptions held.</summary>
    public int Count => _items.Count;

    /// <summary>True once <see cref="Dispose"/> ran; adding then disposes the item at once.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Holds <paramref name="subscription"/> until <see cref="Dispose"/>, and returns it.</summary>
    /// <typeparam name="T">The subscription's type.</typeparam>
    /// <param name="subscription">The subscription.</param>
    public T Add<T>(T subscription)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (IsDisposed)
        {
            subscription.Dispose();
        }
        else
        {
            _items.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Disposes every subscription held, newest first. All of them are disposed even when one throws; the first exception
    /// is rethrown afterwards (several become an <see cref="AggregateException"/>). Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        List<Exception>? failures = null;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            try
            {
                _items[i].Dispose();
            }
            catch (Exception e)
            {
                (failures ??= []).Add(e);
            }
        }

        _items.Clear();
        if (failures is { Count: 1 })
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failures[0]);
        }

        if (failures is not null)
        {
            throw new AggregateException("several subscriptions failed to dispose", failures);
        }
    }
}
