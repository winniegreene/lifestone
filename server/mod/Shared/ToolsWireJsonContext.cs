using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lifestone.ServerTools;

/// <summary>
/// The System.Text.Json source-generated metadata for the reply JSON: camelCase names, nulls left out, NaN and
/// infinities written as strings rather than throwing, and the default encoder, which escapes every character outside
/// printable ASCII. So the JSON is ASCII and round-trips through ACE's code-page 1252 String16L
/// (ACE.Server/Network/Extensions.cs:12-21). No reflection-based serialization is used anywhere in the contract.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(ReplyEnvelope))]
[JsonSerializable(typeof(StatusData))]
[JsonSerializable(typeof(WhereData))]
[JsonSerializable(typeof(PlaceData))]
[JsonSerializable(typeof(XpData))]
[JsonSerializable(typeof(QuestData))]
[JsonSerializable(typeof(CharPurgeData))]
[JsonSerializable(typeof(SpellLearnData))]
[JsonSerializable(typeof(ItemData))]
[JsonSerializable(typeof(CorpsesData))]
[JsonSerializable(typeof(NetData))]
[JsonSerializable(typeof(PlacesData))]
internal sealed partial class ToolsWireJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The JSON object of a reply, field for field (<c>{"v":1,"id":...,"verb":...,"ok":...,"error":...,"message":...,
/// "data":...}</c>); <see cref="ToolReply"/> is its public face. Fields that are null are left out; unknown fields are
/// ignored when reading, so a later minor addition does not break an older reader.
/// </summary>
internal sealed class ReplyEnvelope
{
    /// <summary>The wire version.</summary>
    public int V { get; set; }

    /// <summary>The correlation id.</summary>
    public string? Id { get; set; }

    /// <summary>The verb.</summary>
    public string? Verb { get; set; }

    /// <summary>Success.</summary>
    public bool Ok { get; set; }

    /// <summary>The error code.</summary>
    public string? Error { get; set; }

    /// <summary>The error sentence or a note.</summary>
    public string? Message { get; set; }

    /// <summary>The verb's data.</summary>
    public JsonElement? Data { get; set; }
}
