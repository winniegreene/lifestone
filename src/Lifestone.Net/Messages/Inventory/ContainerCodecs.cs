// Partly ported from eriknihlen/OpenAC@f22b048 src/AcDream.Core.Net/Messages/GameEvents.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only ParseViewContents and ParseCloseGroundContainer;
// namespace Lifestone.Net.Messages; internal; TryParse over WireReader on the whole F7B0 message (OpenAC takes the payload only
// and accepts a longer one) that reads every byte and never throws; the count is checked with WireReader.TryReadCount (a count
// beyond the body is malformed, as OpenAC's length check does, and no array is sized from an unchecked count); the entries are
// an array of a Lifestone record, decoupled from AcDream's ViewContents types; XML docs with the ACE and retail cites added.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>One item of a <see cref="ViewContentsEvent"/>.</summary>
/// <param name="Guid">The item's GUID.</param>
/// <param name="ContainerType">ACE's <c>ContainerType</c>: 0 NonContainer, 1 Container, 2 Foci (an item that needs a pack slot).</param>
public readonly record struct ViewContentsEntry(uint Guid, uint ContainerType);

/// <summary>
/// GameEvent 0x0196 ViewContents (ACE <c>GameEventViewContents</c>): the contents of a container the player opened, in
/// placement order.
/// </summary>
/// <param name="Container">The container's GUID.</param>
/// <param name="Entries">The items, in the server's PlacementPosition order.</param>
public readonly record struct ViewContentsEvent(uint Container, ViewContentsEntry[] Entries);

/// <summary>GameEvent 0x0052 CloseGroundContainer (ACE <c>GameEventCloseGroundContainer</c>): the server closed the container the player had open.</summary>
/// <param name="Container">The container's GUID.</param>
public readonly record struct CloseGroundContainerEvent(uint Container);

/// <summary>
/// The container game events (M4 2.1 s0), laid out as ACE writes them: 0x0196 {u32 container, u32 count, count x {u32 item,
/// u32 ContainerType}} (<c>GameEventViewContents.cs:13-26</c>; ContainerType is 1 for a <c>WeenieType.Container</c>, 2 (Foci)
/// for an item that <c>RequiresPackSlot</c>, else 0) and 0x0052 {u32 container} (<c>GameEventCloseGroundContainer.cs:9-13</c>).
/// Each parser takes the whole F7B0 message and accepts it only when every byte was read.
/// </summary>
internal static class ContainerCodecs
{
    /// <summary>The size of one 0x0196 entry: a GUID and a ContainerType.</summary>
    private const int EntrySize = 8;

    /// <summary>0x0196 ViewContents.</summary>
    public static bool TryParseViewContents(ReadOnlySpan<byte> message, out ViewContentsEvent viewContents)
    {
        viewContents = default;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var container) || !reader.TryReadCount(EntrySize, out var count))
        {
            return false;
        }

        var entries = new ViewContentsEntry[count];
        for (var i = 0; i < entries.Length; i++)
        {
            if (!reader.TryReadU32(out var guid) || !reader.TryReadU32(out var containerType))
            {
                return false;
            }

            entries[i] = new ViewContentsEntry(guid, containerType);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        viewContents = new ViewContentsEvent(container, entries);
        return true;
    }

    /// <summary>0x0052 CloseGroundContainer.</summary>
    public static bool TryParseCloseGroundContainer(ReadOnlySpan<byte> message, out CloseGroundContainerEvent closeGroundContainer)
    {
        closeGroundContainer = default;
        if (message.Length < GameEventEnvelope.HeaderSize)
        {
            return false;
        }

        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadU32(out var container) || !reader.IsAtEnd)
        {
            return false;
        }

        closeGroundContainer = new CloseGroundContainerEvent(container);
        return true;
    }
}
