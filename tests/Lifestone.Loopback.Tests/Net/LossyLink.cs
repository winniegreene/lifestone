using System.Globalization;
using System.Net;
using Lifestone.Net.Sockets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The seeded lossy link of the live soak (m2-plan N12): one <see cref="LossModel"/> per direction and the <see cref="LossyDatagramSocket"/> that
/// <see cref="Lifestone.Net.Session.AcSessionOptions.SocketFactory"/> builds over the real UDP socket to ACE. The socket and models are compiled into this project
/// from tests/Lifestone.Net.Tests/Support (the csproj link); they are the ones the virtual soak (<c>VirtualSoakTests</c>) runs in memory.
/// </summary>
/// <param name="seed">The seed of the client-to-server model; the other direction uses <c>seed ^ 0x5A5A5A</c>, as the virtual soak does.</param>
/// <param name="dropPercent">The percentage of datagrams dropped each way.</param>
/// <param name="swapPercent">The percentage of datagrams swapped with their successor each way.</param>
/// <param name="duplicatePercent">The percentage of datagrams delivered twice each way (0 in the soak; the oracle's positive control uses it).</param>
internal sealed class LossyLink(int seed, double dropPercent, double swapPercent, double duplicatePercent = 0)
{
    /// <summary>The model the session's sends pass through.</summary>
    public LossModel ClientToServer { get; } = new(seed, dropPercent, swapPercent, duplicatePercent);

    /// <summary>The model ACE's datagrams pass through.</summary>
    public LossModel ServerToClient { get; } = new(seed ^ 0x5A5A5A, dropPercent, swapPercent, duplicatePercent);

    /// <summary>The last socket the factory built (the session may build one per connection attempt), or null before the first.</summary>
    public LossyDatagramSocket? Socket { get; private set; }

    /// <summary>The factory for <see cref="Lifestone.Net.Session.AcSessionOptions.SocketFactory"/>: the real loopback UDP socket behind the lossy decorator.</summary>
    public Func<IPEndPoint, IDatagramSocket> SocketFactory =>
        server => Socket = new LossyDatagramSocket(new UdpLoopbackSocket(server, TimeProvider.System), TimeProvider.System, ClientToServer, ServerToClient);

    /// <summary>Both models' counts as one line, for a failure message and the test output.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"c2s [{Describe(ClientToServer)}], s2c [{Describe(ServerToClient)}], armed {Socket?.Armed}");

    private static string Describe(LossModel model) => string.Create(
        CultureInfo.InvariantCulture,
        $"{model.Submitted} submitted, {model.Dropped} dropped, {model.Swapped} swapped, {model.Duplicated} duplicated, {model.DuplicateEncryptedDeliveries} duplicate encrypted deliveries");
}
