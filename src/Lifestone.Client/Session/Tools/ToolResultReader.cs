using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Tools;

/// <summary>
/// Turns the server mod's reply data (ToolsWire's <see cref="StatusData"/>, <see cref="WhereData"/>,
/// <see cref="PlaceData"/>, <see cref="XpData"/>, <see cref="QuestData"/>, <see cref="CharPurgeData"/>, <see cref="SpellLearnData"/> and <see cref="ItemData"/>) into the session's tool results. Every reader throws
/// <see cref="ToolDataException"/> for data that does not have its verb's shape, which the tools report as a server
/// error rather than a result.
/// </summary>
internal static class ToolResultReader
{
    /// <summary>A <c>status</c> reply's result.</summary>
    internal static ToolStatus Status(ToolReply reply) => ToStatus(Data<StatusData>(reply));

    /// <summary>A <see cref="StatusData"/> as the session's <see cref="ToolStatus"/>.</summary>
    internal static ToolStatus ToStatus(StatusData d) =>
        new(d.Wire, d.Mod ?? string.Empty, d.Access ?? string.Empty, d.AutoPromotion, d.Account ?? string.Empty, d.Character, d.World ?? string.Empty, d.Verbs ?? []);

    /// <summary>A <c>where</c> reply's result.</summary>
    internal static WhereResult Where(ToolReply reply)
    {
        var d = Data<WhereData>(reply);
        return new WhereResult(Frame(d.Cell, d.X, d.Y, d.Z, d.Qw, d.Qx, d.Qy, d.Qz), d.Teleporting, d.ForcePositionSeq, d.TeleportSeq);
    }

    /// <summary>A <c>place</c> reply's result.</summary>
    internal static PlaceResult Place(ToolReply reply)
    {
        var d = Data<PlaceData>(reply);
        return new PlaceResult(d.Kind ?? string.Empty, d.Name, Frame(d.Cell, d.X, d.Y, d.Z, d.Qw, d.Qx, d.Qy, d.Qz));
    }

    /// <summary>An <c>xp</c> reply's result.</summary>
    internal static XpResult Xp(ToolReply reply)
    {
        var d = Data<XpData>(reply);
        return new XpResult(d.Requested, d.Added, d.Total, d.Available, d.Level);
    }

    /// <summary>A <c>quest</c> reply's result.</summary>
    internal static QuestResult Quest(ToolReply reply)
    {
        var d = Data<QuestData>(reply);
        return new QuestResult(d.Action ?? string.Empty, d.Name ?? string.Empty, d.HeldBefore, d.Before, d.Held, d.After);
    }

    /// <summary>A <c>char purge</c> reply's result.</summary>
    internal static CharPurgeResult CharPurge(ToolReply reply)
    {
        var d = Data<CharPurgeData>(reply);
        return new CharPurgeResult(d.Name ?? string.Empty, d.Characters, d.PlayerBiotas, d.Possessions);
    }

    /// <summary>A <c>spell learn</c> reply's result. An outcome word this client does not know is data of the wrong shape.</summary>
    internal static SpellLearnResult SpellLearn(ToolReply reply)
    {
        var d = Data<SpellLearnData>(reply);
        var spells = (d.Spells ?? []).Select(s => new SpellLearnEntry(s.Id, SpellOutcome(s.Outcome), s.Name)).ToArray();
        return new SpellLearnResult(
            spells,
            spells.Count(s => s.Outcome == SpellOutcomeKind.Learned),
            spells.Count(s => s.Outcome == SpellOutcomeKind.Known),
            spells.Count(s => s.Outcome == SpellOutcomeKind.Unknown));
    }

    /// <summary>An <c>item</c> reply's result.</summary>
    internal static ItemResult Item(ToolReply reply)
    {
        var d = Data<ItemData>(reply);
        return new ItemResult(d.Guid, d.Wcid, d.Name ?? string.Empty, d.Stack, d.Requested, d.MaxStack, d.Clamped);
    }

    private static SpellOutcomeKind SpellOutcome(string? word) => word switch
    {
        SpellOutcomes.Learned => SpellOutcomeKind.Learned,
        SpellOutcomes.Known => SpellOutcomeKind.Known,
        SpellOutcomes.Unknown => SpellOutcomeKind.Unknown,
        _ => throw new ToolDataException("the reply names a spell outcome this client does not know"),
    };

    private static T Data<T>(ToolReply reply)
        where T : class
    {
        try
        {
            return reply.DataAs<T>() ?? throw new ToolDataException("the reply carries no data");
        }
        catch (JsonException)
        {
            // The reader's message can quote the data; a fixed sentence is enough.
            throw new ToolDataException("the reply's data is not " + typeof(T).Name);
        }
    }

    private static CellFrame Frame(string? cell, float x, float y, float z, float qw, float qx, float qy, float qz) =>
        ToolsWire.TryParseCell(cell, out var id)
            ? CellFrame.FromWire(id, new Vector3(x, y, z), qw, qx, qy, qz)
            : throw new ToolDataException("the reply's cell is not a cell id");
}

/// <summary>A server-mod reply whose data does not have its verb's shape.</summary>
internal sealed class ToolDataException(string message) : Exception(message);
