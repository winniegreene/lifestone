namespace Lifestone.Net.Session;

/// <summary>How the session orders inbound messages before dispatching them.</summary>
public enum InboundOrder
{
    /// <summary>
    /// Lifestone's default (D18): globally by fragment sequence, holding later messages until the gap fills, with a
    /// gap-skip rule so a lost message cannot stall the session.
    /// </summary>
    FragmentSequence,

    /// <summary>Retail's: in the order messages complete reassembly (<c>--retail-net</c>).</summary>
    Arrival,
}
