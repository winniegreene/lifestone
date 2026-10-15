using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

/// <summary>
/// F7E5 DDD_Interrogation (docs/protocol.md §5.2): the server asks for the client's dat iterations. The session answers
/// with F7E6 from <see cref="Session.DddIterations"/>, including the high-res dat only when
/// <see cref="AllowsHighRes"/>.
/// </summary>
/// <param name="ServersRegion">The region (1).</param>
/// <param name="NameRuleLanguage">The name-rule language (1).</param>
/// <param name="ProductId">The product id: bit 0x1 always, bit 0x4 when the server allows the high-res dat.</param>
/// <param name="SupportedLanguages">The supported languages (ACE: 0 Invalid, 1 English).</param>
public sealed record DddInterrogationMsg(uint ServersRegion, uint NameRuleLanguage, uint ProductId, ImmutableArray<uint> SupportedLanguages)
{
    /// <summary>The product-id bit that allows reporting the high-res dat.</summary>
    public const uint HighResProductBit = 0x4;

    /// <summary>True when the server allows the high-res dat (<see cref="HighResProductBit"/>).</summary>
    public bool AllowsHighRes => (ProductId & HighResProductBit) != 0;
}
