using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Recording;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>A recording the host can open (Lifestone.Client does no file I/O: the host lists <c>cache/sessions</c> and supplies the streams).</summary>
/// <param name="Name">The recording's file name (no folder: a path is never logged).</param>
/// <param name="Open">Opens the recording for reading (the loader disposes the stream).</param>
public sealed record RecordingSource(string Name, Func<Stream> Open);

/// <summary>
/// The real PlayerDescription of a graduated test character, read from a session recording under <c>cache/sessions</c> (M3 1.2 s5, the live-shaped rehearsal of docs/milestones/m3-plan.md section 5):
/// the stand-in sends it as its player's description, so <c>--play-checks</c> sees the live costs of the arrival (the real enchantment registry, the spellbook, the contained items) instead of the
/// synthetic 138 of <see cref="StandInPlayerStats.LiveShapedEnchantments"/>. Recordings are never committed (M2 D14): without one, <see cref="TryFind"/> says so once and the stand-in keeps its synthetic description.
/// </summary>
/// <remarks>
/// <para>The recording is of another character (another guid). The PlayerDescriptionMsg holds no guid of its own (the game event's header names the player, and the stand-in writes its own), so the description is
/// sent as read. The contained items the description lists (the main pack, the equipped objects) are replayed too, from the CreateObjects the recording holds for them, with the one rewrite that must be made: the
/// recorded player's guid in a weenie's container or wielder and in a physics parent becomes the stand-in player's. They are replayed so that every object the description names exists in the object table
/// (the rule of retail's <c>CPlayerSystem::SendLoginCompleteNotification</c>, 11.6096 0x00563C30, which waits for them). A listed object the recording holds no CreateObject for is named in <see cref="MissingItems"/>
/// and left out; the description's lists still name it, so the portal wait gives it the safety cap.</para>
/// <para>Only the objects the description lists are replayed. The contents of a side pack arrive by ViewContents (0x0196) in ACE, one per side pack right after the description (capture.md C1); the recording does not hold them
/// (<see cref="SidePacks"/>): a recorded pack is announced empty, and when the description carries no pack at all the stand-in adds a labelled one with one item (<c>TODO(m4-standin-side-pack-item)</c>; closes <c>TODO(m3-standin-side-pack-contents)</c>).
/// Their models are the recording's (the stand-in has the same dats): placeholder objects as far as the stand-in is concerned, not authored by it.</para>
/// </remarks>
public sealed class RecordedDescription
{
    private RecordedDescription(string fileName, PlayerDescriptionMsg description, uint recordedPlayer, ImmutableArray<CreateObjectMsg> items, ImmutableArray<uint> missingItems)
    {
        FileName = fileName;
        Description = description;
        RecordedPlayer = recordedPlayer;
        Items = items;
        MissingItems = missingItems;
    }

    /// <summary>A description made from parts (tests).</summary>
    internal static RecordedDescription Of(string fileName, PlayerDescriptionMsg description, uint recordedPlayer, ImmutableArray<CreateObjectMsg> items) =>
        new(fileName, description, recordedPlayer, items, []);

    /// <summary>The recording's file name (no folder: a path is not logged).</summary>
    public string FileName { get; }

    /// <summary>The PlayerDescription as the server sent it.</summary>
    public PlayerDescriptionMsg Description { get; }

    /// <summary>The guid of the character the recording belongs to (the one the stand-in's rewrite replaces).</summary>
    public uint RecordedPlayer { get; }

    /// <summary>The CreateObjects of the objects the description lists (pack and equipped), as recorded.</summary>
    public ImmutableArray<CreateObjectMsg> Items { get; }

    /// <summary>The listed objects the recording holds no CreateObject for.</summary>
    public ImmutableArray<uint> MissingItems { get; }

    /// <summary>How many enchantments the description's registry holds.</summary>
    public int EnchantmentCount => Description.Enchantments.IsDefault ? 0 : Description.Enchantments.Length;

    /// <summary>The guid of the Pack the stand-in adds to a description that carries none (a placeholder; <c>TODO(m4-standin-side-pack-item)</c>).</summary>
    internal const uint PlaceholderPackGuid = 0x80050000;

    /// <summary>The guid of the item that Pack holds.</summary>
    internal const uint PlaceholderPackItemGuid = 0x80050001;

    /// <summary>The weenie class id of the Pack (capture.md C1: "Pack" 136).</summary>
    internal const uint PackWcid = 136;

    /// <summary>
    /// The side packs of the login burst (capture.md C1) for <paramref name="player"/>: a 0196 per side pack the description lists (ContainerType 1), in the pack's order, then the CreateObjects of the pack's contents. The recording holds no contents,
    /// so a recorded pack is announced empty. A description with no side pack gets one added, "Pack", holding one Pyreal stack (a placeholder): <see cref="SidePackBurst.Extra"/> and <see cref="SidePackBurst.Listed"/> carry its object and its
    /// place in the description, <see cref="SidePackBurst.Contents"/> its item.
    /// </summary>
    /// <param name="player">The stand-in player's guid.</param>
    internal SidePackBurst SidePacks(uint player)
    {
        var views = ImmutableArray.CreateBuilder<ViewContentsEvent>();
        foreach (var carried in Description.Inventory)
        {
            if (carried.ContainerType == 1)
            {
                views.Add(new ViewContentsEvent(carried.Guid, []));
            }
        }

        if (views.Count > 0)
        {
            return new SidePackBurst(views.ToImmutable(), [], [], [], false);
        }

        var pack = new CreateObjectMsg(
            PlaceholderPackGuid,
            ModelData.Empty,
            new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal, SetupId = StandInWeenieArt.SetupOr(PackWcid, 0x02000B77), Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) },
            StandInWeenieArt.Apply(new WeenieDesc { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.ItemsCapacity, Name = "Pack (stand-in placeholder)", WeenieClassId = PackWcid, IconId = StandInWeenieArt.PlaceholderIcon, ItemType = 0x200, ObjectDescriptionFlags = 0, ContainerId = player, ItemsCapacity = 24 }, null));
        var item = new CreateObjectMsg(
            PlaceholderPackItemGuid,
            ModelData.Empty,
            new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal, SetupId = 0x02000B77, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) },
            StandInWeenieArt.Apply(new WeenieDesc { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize, Name = "Pyreal", WeenieClassId = 273, IconId = StandInWeenieArt.PlaceholderIcon, ItemType = 0x40, ObjectDescriptionFlags = 0, ContainerId = PlaceholderPackGuid, Value = 7, StackSize = 7, MaxStackSize = 25000 }, null));
        return new SidePackBurst([new ViewContentsEvent(PlaceholderPackGuid, [new ViewContentsEntry(PlaceholderPackItemGuid, 0)])], [pack], [item], [new ContentDesc(PlaceholderPackGuid, 1)], true);
    }

    /// <summary>
    /// The items rewritten for <paramref name="player"/>: the recorded player's guid in a weenie's container or wielder and in a physics parent replaced by <paramref name="player"/>, nothing else.
    /// </summary>
    /// <param name="player">The stand-in player's guid.</param>
    public IEnumerable<CreateObjectMsg> ItemsFor(uint player)
    {
        foreach (var item in Items)
        {
            var weenie = item.Weenie;
            if (weenie.ContainerId == RecordedPlayer)
            {
                weenie = weenie with { ContainerId = player };
            }

            if (weenie.WielderId == RecordedPlayer)
            {
                weenie = weenie with { WielderId = player };
            }

            var physics = item.Physics;
            if (physics.Parent is { } parent && parent.Guid == RecordedPlayer)
            {
                physics = physics with { Parent = parent with { Guid = player } };
            }

            yield return item with { Weenie = weenie, Physics = physics };
        }
    }

    /// <summary>
    /// The recording among <paramref name="sources"/> whose PlayerDescription holds the most enchantments (the first found wins a tie), read; logs the file's name and the count at Info, or, when there is none
    /// (no recording, no readable one, no recording with a description), says once at Info that the stand-in keeps its synthetic description. Never throws.
    /// </summary>
    /// <param name="sources">The recordings the host found.</param>
    /// <param name="log">Where to log.</param>
    /// <returns>The description, or null.</returns>
    public static RecordedDescription? TryFind(IEnumerable<RecordingSource> sources, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(log);
        RecordedDescription? best = null;
        var unreadable = 0;
        var files = 0;
        foreach (var source in sources)
        {
            files++;
            var read = TryRead(source);
            if (read is null)
            {
                unreadable++;
            }
            else if (best is null || read.EnchantmentCount > best.EnchantmentCount)
            {
                best = read;
            }
        }

        if (best is null)
        {
            log.Info(
                SessionLog.Category,
                "no recorded PlayerDescription under cache/sessions (M2 D14: recordings are never committed): the stand-in sends its synthetic description",
                new LogField("recordings", files),
                new LogField("unreadable_or_without_description", unreadable),
                new LogField("synthetic_enchantments", StandInPlayerStats.LiveShapedEnchantments().Length));
            return null;
        }

        log.Info(
            SessionLog.Category,
            "the stand-in sends a recorded PlayerDescription",
            new LogField("file", best.FileName),
            new LogField("enchantments", best.EnchantmentCount),
            new LogField("spells", best.Description.Spells.Count),
            new LogField("pack", best.Description.Inventory.Length),
            new LogField("equipped", best.Description.Equipped.Length),
            new LogField("item_objects", best.Items.Length),
            new LogField("items_not_in_the_recording", best.MissingItems.Length));
        return best;
    }

    /// <summary>
    /// Reads <paramref name="source"/>: its first PlayerDescription and the CreateObjects of the objects that description lists. Null when the stream cannot be read, is not a recording, or holds no PlayerDescription.
    /// </summary>
    /// <param name="source">A recording the host opens.</param>
    public static RecordedDescription? TryRead(RecordingSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var stream = source.Open();
            using var reader = SessionRecordingReader.Open(stream, leaveOpen: true);
            var capture = new Capture();
            while (reader.TryRead(out var frame))
            {
                if (frame.Kind != RecordingFrameKind.Message || frame.Message.Direction != MessageDirection.Inbound || frame.Message.BodyOmitted)
                {
                    continue;
                }

                // Only the three messages the capture wants are decoded: the player's creation, the first description and the object creations.
                var body = frame.Message.Body.Span;
                var wanted = frame.Message.Opcode switch
                {
                    MessageOpcodes.PlayerCreate or MessageOpcodes.ObjectCreate => true,
                    MessageOpcodes.GameEvent => body.Length >= 16 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body[12..]) == GameEventTypes.PlayerDescription,
                    _ => false,
                };
                if (wanted)
                {
                    InboundDispatcher.Dispatch(new InboundContext(frame.Message.FragmentSequence, frame.Message.Queue, 0), body, capture);
                }

                if (capture.Complete)
                {
                    break;
                }
            }

            return capture.Description is null ? null : capture.Build(source.Name);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Collects the first description and the creations of the objects it lists, from a replay of the recording's server messages.</summary>
    private sealed class Capture : IInboundMessageHandler
    {
        private readonly Dictionary<uint, CreateObjectMsg> _created = [];
        private HashSet<uint>? _wanted;

        public PlayerDescriptionMsg? Description { get; private set; }

        public uint Player { get; private set; }

        /// <summary>True once the description is in and every object it lists has been created.</summary>
        public bool Complete => _wanted is not null && _wanted.All(_created.ContainsKey);

        public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message)
        {
            if (Player == 0)
            {
                Player = message.Guid;
            }
        }

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
        {
            if (Description is not null)
            {
                return;
            }

            Description = message;
            if (Player == 0)
            {
                Player = header.PlayerGuid;
            }

            _wanted = [.. message.Inventory.Select(c => c.Guid), .. message.Equipped.Select(e => e.Guid)];
        }

        public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
        {
            // Before the description, nothing is wanted yet: keep every creation of the first moments (the pack arrives after the description in ACE's order, but a recording may differ).
            if (!isUpdate && (_wanted is null || _wanted.Contains(message.Guid)) && !_created.ContainsKey(message.Guid))
            {
                _created.Add(message.Guid, message);
            }
        }

        public RecordedDescription Build(string fileName)
        {
            var description = Description!;
            var items = ImmutableArray.CreateBuilder<CreateObjectMsg>();
            var missing = ImmutableArray.CreateBuilder<uint>();
            foreach (var guid in description.Inventory.Select(c => c.Guid).Concat(description.Equipped.Select(e => e.Guid)).Distinct())
            {
                if (_created.TryGetValue(guid, out var created))
                {
                    items.Add(created);
                }
                else
                {
                    missing.Add(guid);
                }
            }

            return new RecordedDescription(fileName, description, Player, items.ToImmutable(), missing.ToImmutable());
        }
    }
}

/// <summary>
/// The side packs of a login burst (<see cref="RecordedDescription.SidePacks"/>).
/// </summary>
/// <param name="Views">The 0196 of each side pack, sent right after the description.</param>
/// <param name="Extra">The CreateObject of the Pack the stand-in added (empty when the description has its own).</param>
/// <param name="Contents">The CreateObjects of the added pack's contents (container = the pack).</param>
/// <param name="Listed">The description entries to add to its Inventory for the added Pack.</param>
/// <param name="Added">True when the stand-in added the Pack.</param>
internal readonly record struct SidePackBurst(ImmutableArray<ViewContentsEvent> Views, ImmutableArray<CreateObjectMsg> Extra, ImmutableArray<CreateObjectMsg> Contents, ImmutableArray<ContentDesc> Listed, bool Added);

/// <summary>
/// The shortcut bar the stand-in keeps for its player (M4 3.2 s3): ACE answers an AddShortCut 0x019C and a RemoveShortCut 0x019D with nothing and keeps the list for the next login's PlayerDescription (<c>Player_Character.cs</c> 245-280,
/// <c>CharacterExtensions.AddOrUpdateShortcut</c> and <c>TryRemoveShortcut</c>), so the stand-in changes the description it sends: a later description in the same visit, or a replayed login, carries the list. The list starts as the one the
/// first description carried (a recorded character's own). ACE stores only the object (the spell word of an AddShortCut is dropped: <c>TODO(m4-standin-shortcut-spell)</c>), so every entry is listed with spell 0, as the capture's relog showed.
/// </summary>
internal sealed class StandInShortcutList
{
    private Dictionary<uint, uint>? _items;
    private ImmutableArray<ShortcutDesc> _login = [];
    private bool _seeded;

    /// <summary>The entries now (index to object), by index; the list the first description carried until the first change.</summary>
    internal IReadOnlyList<ShortcutDesc> Items => _items is null ? _login : [.. _items.OrderBy(i => i.Key).Select(i => new ShortcutDesc(i.Key, i.Value, 0))];

    /// <summary>True once an add or a remove has changed the list (the description then carries the stand-in's list in place of the recorded one).</summary>
    internal bool Changed => _items is not null;

    /// <summary>Adds the entry at <paramref name="index"/> or replaces the one there (<c>AddOrUpdateShortcut</c>).</summary>
    internal void AddOrUpdate(uint index, uint objectId) => Edit()[index] = objectId;

    /// <summary>Removes the entry at <paramref name="index"/> (<c>TryRemoveShortcut</c>); true when there was one.</summary>
    internal bool Remove(uint index) => Edit().Remove(index);

    private Dictionary<uint, uint> Edit() => _items ??= _login.ToDictionary(s => s.Index, s => s.ObjectId);

    /// <summary>The description with the stand-in's list, unchanged until the first add or remove; the first description it sees seeds the list.</summary>
    /// <param name="description">The description about to be sent.</param>
    internal PlayerDescriptionMsg Apply(PlayerDescriptionMsg description)
    {
        if (!_seeded)
        {
            _seeded = true;
            _login = description.Shortcuts.IsDefault ? [] : description.Shortcuts;
        }

        return _items is null ? description : description with { Shortcuts = [.. Items] };
    }
}
