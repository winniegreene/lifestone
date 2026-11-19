using System.Globalization;
using System.Numerics;

namespace Lifestone.Client.Session.Quests;

/// <summary>
/// The quest step vocabulary (M6 spec DoD 7; the ladder note section 12.2): the words a quest file's <c>kind</c> may hold. M6 item 0
/// implements the five the Training Academy and the Pathwarden chest need (<see cref="Go"/>, <see cref="Talk"/>, <see cref="Use"/>,
/// <see cref="Give"/>, <see cref="PickUp"/>); item 5 the six of the kill task, the trophy turn-in and the errands (<see cref="Kill"/>,
/// <see cref="HuntUntil"/>, <see cref="Buy"/>, <see cref="Recall"/>, <see cref="Confirm"/>, <see cref="Wear"/>); item 8 the two of the craft chain
/// (<see cref="Combine"/>, <see cref="Apply"/>, one reader: <see cref="RecipeOutcomes"/>). A word added to the vocabulary and not yet built fails at load, loudly, naming the step and the item
/// that builds it (<see cref="QuestStepKinds.IsImplemented"/>). A key used on a chest (the note's <c>use-key</c>) is a <see cref="Use"/> step with an <see cref="QuestStep.Item"/>.
/// </summary>
public enum QuestStepKind
{
    /// <summary><c>go</c>: walk or travel to a place (a cell and a point) or to an object in view.</summary>
    Go = 0,

    /// <summary><c>talk</c>: use an NPC and read its reply (the reply, or the stamp its script records, is the step's check).</summary>
    Talk = 1,

    /// <summary><c>use</c>: use an object (a portal, a door), or an item on it (a key on a chest: the ladder note's <c>use-key</c>).</summary>
    Use = 2,

    /// <summary><c>give</c>: give an item to an NPC and read what comes back (the reply, the item arriving, the stamp).</summary>
    Give = 3,

    /// <summary><c>pick-up</c>: take an item from the floor, a chest or a corpse into the pack.</summary>
    PickUp = 4,

    /// <summary><c>hunt-until</c>: hunt a ground until N of an item have arrived in the pack (the trophy turn-in shape; item 5).</summary>
    HuntUntil = 5,

    /// <summary><c>kill</c>: kill N of a creature on a ground until the server's kill counter says the task is complete (the kill-task shape; item 5).</summary>
    Kill = 6,

    /// <summary><c>buy</c>: buy N of an item from a vendor (item 5).</summary>
    Buy = 7,

    /// <summary><c>combine</c>: use one held item on another and read the recipe's outcome from the items (the craft-chain shape; item 8; two of the same weenie class are two different objects).</summary>
    Combine = 8,

    /// <summary><c>apply</c>: use a tool or stone on a held item, which may carry a skill check, and read the outcome from the items (the craft-chain shape; item 8; the file names the recipe's fail line).</summary>
    Apply = 9,

    /// <summary><c>recall</c>: cast a recall spell and arrive at its place (item 5).</summary>
    Recall = 10,

    /// <summary><c>confirm</c>: answer the server's Yes/No whose text the step names (spec DoD 7; item 5; <see cref="QuestConfirmRule"/>).</summary>
    Confirm = 11,

    /// <summary><c>wear</c>: wear or wield an item from the pack (spec DoD 2's pieces are worn by item 0's station a3, not by a step; item 5).</summary>
    Wear = 12,
}

/// <summary>The step vocabulary's words and which kinds are implemented.</summary>
public static class QuestStepKinds
{
    private static readonly (QuestStepKind Kind, string Word, string Item)[] Table =
    [
        (QuestStepKind.Go, "go", "0"),
        (QuestStepKind.Talk, "talk", "0"),
        (QuestStepKind.Use, "use", "0"),
        (QuestStepKind.Give, "give", "0"),
        (QuestStepKind.PickUp, "pick-up", "0"),
        (QuestStepKind.HuntUntil, "hunt-until", "5"),
        (QuestStepKind.Kill, "kill", "5"),
        (QuestStepKind.Buy, "buy", "5"),
        (QuestStepKind.Combine, "combine", "8"),
        (QuestStepKind.Apply, "apply", "8"),
        (QuestStepKind.Recall, "recall", "5"),
        (QuestStepKind.Confirm, "confirm", "5"),
        (QuestStepKind.Wear, "wear", "5"),
    ];

    /// <summary>Every word of the vocabulary, in enum order.</summary>
    public static IReadOnlyList<string> Words { get; } = Table.Select(t => t.Word).ToArray();

    /// <summary>The kinds M6 items 0, 5 and 8 implement: all thirteen words (go, talk, use, give, pick-up, hunt-until, kill, buy, recall, confirm, wear, combine and apply).</summary>
    public static IReadOnlyList<QuestStepKind> Implemented { get; } = Table.Where(t => t.Item is "0" or "5" or "8").Select(t => t.Kind).ToArray();

    /// <summary>The file word of <paramref name="kind"/> (<c>pick-up</c> for <see cref="QuestStepKind.PickUp"/>).</summary>
    /// <param name="kind">The kind.</param>
    public static string Word(QuestStepKind kind) => Table.First(t => t.Kind == kind).Word;

    /// <summary>The kind a file word names, or null when the word is not in the vocabulary.</summary>
    /// <param name="word">The word, exactly as the file spells it.</param>
    public static QuestStepKind? Parse(string word)
    {
        foreach (var t in Table)
        {
            if (string.Equals(t.Word, word, StringComparison.Ordinal))
            {
                return t.Kind;
            }
        }

        return null;
    }

    /// <summary>True for the kinds items 0, 5 and 8 implement (<see cref="Implemented"/>).</summary>
    /// <param name="kind">The kind.</param>
    public static bool IsImplemented(QuestStepKind kind) => Implemented.Contains(kind);

    /// <summary>The M6 item that implements <paramref name="kind"/> (<c>"0"</c> for the five item 0 has).</summary>
    /// <param name="kind">The kind.</param>
    public static string BuiltBy(QuestStepKind kind) => Table.First(t => t.Kind == kind).Item;
}

/// <summary>
/// An object a quest names: an NPC, a portal, a chest or an item. <see cref="Wcid"/> is the weenie class id the runner matches (the
/// world database's, cited in the file's sources); it is null only when the facts did not establish it, and then the step names the
/// <c>TODO</c> that will (the loader refuses a null wcid on a step without one). <see cref="Name"/> is the label the logs print.
/// </summary>
/// <param name="Wcid">The weenie class id, or null when not established.</param>
/// <param name="Name">The label.</param>
public sealed record QuestThing(uint? Wcid, string Name)
{
    /// <summary>The label and the wcid as the logs print them: <c>Pathwarden Thorolf (33596)</c>, or <c>... (wcid unknown)</c>.</summary>
    public override string ToString() => Wcid is { } w ? string.Create(CultureInfo.InvariantCulture, $"{Name} ({w})") : $"{Name} (wcid unknown)";
}

/// <summary>A place a quest names: a cell, a point in it when known, and a label.</summary>
/// <param name="Cell">The cell id (<c>0xLLLLCCCC</c>).</param>
/// <param name="Point">The point in the cell's frame, or null when only the cell is known.</param>
/// <param name="Name">The label.</param>
public sealed record QuestPlace(uint Cell, Vector3? Point, string Name)
{
    /// <summary>The label and the cell: <c>the exit portal's cell (0x86020169)</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} (0x{Cell:X8})");
}

/// <summary>
/// What the client can see after a step or at a quest's end (the spec section 4: "the NPC's reply, the item arriving, the experience
/// line"): a reply the NPC tells (a substring of the chat line, case sensitive), an item that arrives in the pack, and the quest stamp the
/// server's script records. The stamp is the server's registry name: with <c>quest_info_enabled</c> false the client does not see it, so a
/// check reads the reply or the item, and the stamp is what the tools' <c>quest has</c> verb confirms on the loopback server.
/// </summary>
/// <param name="Reply">A substring of the NPC's reply, or null.</param>
/// <param name="Stamp">The quest stamp the server records, or null.</param>
/// <param name="Item">The item that arrives, or null.</param>
public sealed record QuestExpect(string? Reply, string? Stamp, QuestThing? Item)
{
    /// <summary>True when nothing is expected.</summary>
    public bool IsEmpty => Reply is null && Stamp is null && Item is null;

    /// <summary>The expectation as one log fragment.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Reply is not null)
        {
            parts.Add($"reply \"{Reply}\"");
        }

        if (Stamp is not null)
        {
            parts.Add($"stamp {Stamp}");
        }

        if (Item is not null)
        {
            parts.Add($"item {Item}");
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}

/// <summary>
/// One step of a quest (immutable). The fields a kind uses (the loader checks them, <see cref="QuestCatalog"/>):
/// <list type="bullet">
/// <item><c>go</c>: <see cref="Place"/> or <see cref="Target"/> (the place to reach, or the object to walk to); no <see cref="Item"/>.</item>
/// <item><c>talk</c>: <see cref="Target"/> (the NPC); no <see cref="Item"/>; a check in <see cref="Expect"/>.</item>
/// <item><c>use</c>: <see cref="Target"/> (the object used); <see cref="Item"/> when an item is used on it (a key on a chest);
/// <see cref="Place"/> when the use leads somewhere (a portal's arrival); a check in <see cref="Expect"/> or <see cref="Place"/>.</item>
/// <item><c>give</c>: <see cref="Target"/> (the NPC) and <see cref="Item"/> (what is given); a check in <see cref="Expect"/>.</item>
/// <item><c>pick-up</c>: <see cref="Item"/> (what is taken; its arrival in the pack is the check); <see cref="Target"/> when it is taken
/// from a container (a chest, a corpse).</item>
/// <item><c>kill</c> (item 5): <see cref="Target"/> (the creature), <see cref="Count"/>, <see cref="Ground"/> and <see cref="QuestExpect.Reply"/>, the
/// server's kill counter's complete text (<c>Your task is complete!</c>, <see cref="QuestReads.ParseKillCounter"/>); no item, no place.</item>
/// <item><c>hunt-until</c> (item 5): <see cref="Item"/>, <see cref="Count"/> and <see cref="Ground"/>; <see cref="Target"/> (the carrier) optional; no
/// place. The check is the item's count arriving in the pack.</item>
/// <item><c>buy</c> (item 5): <see cref="Target"/> (the vendor), <see cref="Item"/> and <see cref="Count"/>; no place (a go step reaches the vendor).</item>
/// <item><c>recall</c> (item 5): <see cref="Spell"/> and <see cref="Place"/> (the arrival); no target, no item.</item>
/// <item><c>confirm</c> (item 5): <see cref="QuestExpect.Reply"/> (a substring of the question's text) and <see cref="Answer"/>; no target, item or
/// place.</item>
/// <item><c>wear</c> (item 5): <see cref="Item"/>; no target, no place.</item>
/// <item><c>combine</c> and <c>apply</c> (item 8; one runner, one reader, <see cref="RecipeOutcomes"/>): <see cref="Item"/> (the source) and <see cref="Target"/> (the target), each a held item by wcid; <see cref="QuestExpect.Item"/> the result's wcid and
/// <see cref="QuestExpect.Reply"/> the recipe row's whole success line, both required (the items decide, the line supports: recipe 6068's two lines are one text, and recipe 789's differ only in "of high quality", so a line is compared whole, never as a part);
/// <see cref="Count"/> how many times the step repeats (1 when absent); <see cref="FailItem"/> and <see cref="FailReply"/> the recipe row's fail result and whole fail line. An <c>apply</c> names <see cref="FailReply"/> (it may roll); a <c>combine</c> may, and one
/// with a roll (recipe 789) does; a <see cref="FailItem"/> needs its <see cref="FailReply"/>. No place, no stamp (the check is the items).</item>
/// </list>
/// The item-5 fields <see cref="Count"/>, <see cref="Ground"/>, <see cref="Spell"/> and <see cref="Answer"/> and item 8's <see cref="FailItem"/> and <see cref="FailReply"/> are optional in the file and refused on a
/// kind that does not use them. An expected item (<see cref="QuestExpect.Item"/>, <see cref="QuestDone.Item"/>) means "arrives": its count in the pack
/// rises over the step, never "is held" (item 5's contract between the slices).
/// A step whose check or wcid the facts did not establish carries <see cref="Todo"/>, a named <c>TODO(name): ...</c>; the loader refuses
/// a missing check or a null wcid on a step without one.
/// </summary>
public sealed record QuestStep
{
    /// <summary>The step's index in the quest (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>The step's kind (one of <see cref="QuestStepKinds.Implemented"/>; the loader refuses the rest).</summary>
    public required QuestStepKind Kind { get; init; }

    /// <summary>What the step is for, in a few words (the logs print it).</summary>
    public required string What { get; init; }

    /// <summary>The place to reach, or the arrival of a use; null when the step has none.</summary>
    public required QuestPlace? Place { get; init; }

    /// <summary>The NPC or object acted on, or the container of a pick-up; null when the step has none.</summary>
    public required QuestThing? Target { get; init; }

    /// <summary>The item given, picked up, or used on the target; null when the step has none.</summary>
    public required QuestThing? Item { get; init; }

    /// <summary>What the client should see after the step.</summary>
    public required QuestExpect Expect { get; init; }

    /// <summary>Where the step's values come from (a SELECT, a session.md section, a ladder note section).</summary>
    public required string Source { get; init; }

    /// <summary>A named <c>TODO(name): ...</c> for what the facts did not establish, or null.</summary>
    public required string? Todo { get; init; }

    /// <summary>Item 5: how many (a kill's creatures, a hunt-until's items, a buy's items; item 8: a combine's or an apply's repeats; above 0), or null on the other kinds.</summary>
    public int? Count { get; init; }

    /// <summary>Item 5: the ground profile a kill or hunt-until hunts (a key <see cref="Grounds.GroundCatalog"/> knows), or null on the other kinds.</summary>
    public string? Ground { get; init; }

    /// <summary>Item 5: a recall's spell id (above 0), or null on the other kinds.</summary>
    public uint? Spell { get; init; }

    /// <summary>Item 5: a confirm's answer (true for the file's <c>yes</c>, false for <c>no</c>), or null on the other kinds.</summary>
    public bool? Answer { get; init; }

    /// <summary>M6 5 F1 (review 5-R7): a talk's reply that says the task is already armed (a kill task in progress: San Ming's "I see you haven't completed your task"), a substring of the NPC's line; accepted as the arm. Null on the other kinds and on a talk with none.</summary>
    public string? ArmedReply { get; init; }

    /// <summary>M6 5 F1 (review 5-R7): a talk's reply that says the task is on its timer (a done-recently wait reply), a substring of the NPC's line; the station stops <c>talk:on-timer</c> at it. Null on the other kinds and on a talk with none.</summary>
    public string? WaitReply { get; init; }

    /// <summary>M6 8 B2c: a go step's authored way, the points in order, the last the step's place (the room graph has no way through a door whose lane is stamped: the Sea Temple's), or null: the planner plans the walk. The walk is <see cref="Scenarios.Journey.AuthoredWaySource"/> (B1), which the physics still confirms.</summary>
    public IReadOnlyList<QuestPlace>? Way { get; init; }

    /// <summary>Item 8: a recipe step's fail result (the recipe row's fail wcid), or null: the row creates nothing on a failure, or the file names none.</summary>
    public QuestThing? FailItem { get; init; }

    /// <summary>Item 8: a recipe step's whole fail line (the recipe row's <c>fail_Message</c>), or null on the other kinds and on a combine with no roll.</summary>
    public string? FailReply { get; init; }

    /// <summary>
    /// M6 14 the Atlan wand (D136, <c>TODO(m6-credit-need)</c> closed): a recipe step's skill (the recipe row's <c>skill</c>, ACE's <c>Skill</c> id), present only with <see cref="RecipeDifficulty"/> on a recipe with a roll
    /// (<c>RecipeManager.HasDifficulty</c>: skill and difficulty both above 0); null otherwise. The job needs the skill trained (the credit order's need) and the station raises it before the roll (<see cref="RecipeChance"/>).
    /// </summary>
    public uint? RecipeSkill { get; init; }

    /// <summary>M6 14: a recipe step's difficulty (the recipe row's <c>difficulty</c>), present only with <see cref="RecipeSkill"/>; null otherwise.</summary>
    public uint? RecipeDifficulty { get; init; }

    /// <summary>
    /// M6 14 want loot: a hunt-until's carriers, the creatures whose create list can hold the hunted item, each with its row's shade (the file's <c>carriers</c>, citing the pinned world SQL's rows); the fight walks to their corpses
    /// whatever the distance (<see cref="Scenarios.WantCarrier"/>). Null on the other kinds and on a hunt-until with none (TODO(m6-create-list-general-read)).
    /// </summary>
    public IReadOnlyList<QuestCarrier>? Carriers { get; init; }

    /// <summary>M6 14 want loot: a hunt-until's live kill rate of one carrier (the file's <c>kill_rate</c>), for the grind's expected length in the start line; null when none was measured.</summary>
    public QuestKillRate? KillRate { get; init; }

    /// <summary>The step as one log line: <c>step 3 give Pathwarden Token (33613) to Pathwarden Thorolf (33596); expect ...</c>.</summary>
    public override string ToString()
    {
        var what = Kind switch
        {
            QuestStepKind.Go => $"go to {(object?)Place ?? Target}",
            QuestStepKind.Talk => $"talk to {Target}",
            QuestStepKind.Use => Item is null ? $"use {Target}" : $"use {Item} on {Target}",
            QuestStepKind.Give => $"give {Item} to {Target}",
            QuestStepKind.PickUp => Target is null ? $"pick up {Item}" : $"pick up {Item} from {Target}",
            QuestStepKind.Kill => $"kill {Count} {Target} on {Ground}",
            QuestStepKind.HuntUntil => $"hunt {Ground} until {Count} {Item} arrived{(Target is null ? string.Empty : $" (from {Target})")}",
            QuestStepKind.Buy => $"buy {Count} {Item} from {Target}",
            QuestStepKind.Recall => $"recall by spell {Spell} to {Place}",
            QuestStepKind.Confirm => $"confirm {(Answer is true ? "yes" : "no")}",
            QuestStepKind.Wear => $"wear {Item}",
            QuestStepKind.Combine or QuestStepKind.Apply => $"{QuestStepKinds.Word(Kind)} {Item} on {Target}{(Count is { } times ? $" x{times}" : string.Empty)}",
            _ => QuestStepKinds.Word(Kind),
        };
        return string.Create(CultureInfo.InvariantCulture, $"step {Index} {what}; expect {Expect}{(Todo is null ? string.Empty : "; " + Todo)}");
    }
}

/// <summary>
/// How a quest is known to be done (immutable): the quest stamp the server records, and what the client can see (a reply, an item in the
/// pack). At least one is set. With <c>quest_info_enabled</c> false (this server) the client cannot read the stamp itself, so the runner
/// records a completion in <see cref="QuestTimers"/> from <see cref="Reply"/> or <see cref="Item"/>; the stamp is confirmed through the
/// tools' <c>quest has</c> verb where the loopback server allows it (<see cref="QuestTimers.Observe(Quest, Lifestone.Client.Session.Contracts.QuestResult, DateTimeOffset)"/>).
/// </summary>
/// <param name="Stamp">The server's quest stamp, or null (the Academy's exit erases its stamps).</param>
/// <param name="Reply">A substring of the reply or message that ends the quest, or null.</param>
/// <param name="Item">The item in the pack that ends the quest, or null.</param>
public sealed record QuestDone(string? Stamp, string? Reply, QuestThing? Item)
{
    /// <summary>The done condition as one log fragment.</summary>
    public override string ToString() => new QuestExpect(Reply, Stamp, Item).ToString();
}

/// <summary>
/// A quest (M6 DoD 7, D26: a shape is code, a quest is a file): one embedded file under <c>Session/Quests/quests/</c>, read once by
/// <see cref="QuestCatalog"/>. Immutable; every value is a file value with its source, never a constant in the code.
/// </summary>
public sealed record Quest
{
    /// <summary>The quest's key: its file name without <c>.json</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The quest's name as the logs and the menu print it.</summary>
    public required string Name { get; init; }

    /// <summary>The NPC who gives the quest, or null when it starts without one (the Academy starts at character creation).</summary>
    public required QuestThing? Giver { get; init; }

    /// <summary>Where the quest starts.</summary>
    public required QuestPlace Start { get; init; }

    /// <summary>The steps, in order (at least one).</summary>
    public required IReadOnlyList<QuestStep> Steps { get; init; }

    /// <summary>How the quest is known to be done.</summary>
    public required QuestDone Done { get; init; }

    /// <summary>The repeat timer in seconds (a 20-hour quest is 72000), or null when the quest is done once per character.</summary>
    public required int? TimerSeconds { get; init; }

    /// <summary>
    /// M6 audit (the owner's decision of 2026-11-19: every tusk when it is due): where <see cref="TimerSeconds"/> starts (the file's optional <c>timer_from</c>): <see cref="QuestTimerFrom.TurnIn"/>, the default, or
    /// <see cref="QuestTimerFrom.Pickup"/> when the server stamps the quest as the hunt-until step's item is picked up (the tusks' own Quest strings, ACE <c>EmoteManager.OnQuest</c>): the timer then runs from the moment the item was
    /// first seen in the pack (<see cref="TrophyPickups"/>), never earlier than the pickup, so never early.
    /// </summary>
    public QuestTimerFrom TimerFrom { get; init; } = QuestTimerFrom.TurnIn;

    /// <summary>Where the values come from;the facts not established are named <c>TODO(name): ...</c> entries (<see cref="Todos"/>).</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>M6 0 F8: the burden gate of the quest's pick-ups, or null for none (the file's optional <c>carry</c> object).</summary>
    public QuestCarry? Carry { get; init; }

    /// <summary>Item 5: the character levels the quest is for (the file's optional <c>level</c>; items 6 and 9 filter the menu and the round by it), or null.</summary>
    public QuestLevel? Level { get; init; }

    /// <summary>Item 5: the experience the quest awards (the file's optional <c>experience</c>, with its world row; items 6 and 9 show it beside each quest), or null.</summary>
    public QuestExperience? Experience { get; init; }

    /// <summary>Item 8 (D118): when the ladder wants the quest (the file's optional <c>ladder</c>), or null: a quest the climb never sends her on unprompted. <see cref="Level"/> stays what it is, the band the quest is for.</summary>
    public QuestLadder? Ladder { get; init; }

    /// <summary>The named TODOs of the quest: its sources' and its steps', in file order.</summary>
    public IEnumerable<string> Todos => Sources.Where(s => s.StartsWith("TODO(", StringComparison.Ordinal)).Concat(Steps.Where(s => s.Todo is not null).Select(s => s.Todo!));

    /// <summary>The quest as one log line: <c>quest key=pathwarden steps=8 timer=once done=stamp PathwardenComplete, ...</c>.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"quest key={Key} steps={Steps.Count} timer={(TimerSeconds is { } t ? t + " s" : "once")} done={Done}");
}

/// <summary>
/// M6 0 F8 (the first climb probe: a Strength 10 mage, capacity 1,500, was at 129% after one restock): the burden gate of a quest's pick-ups. A piece is taken only when the burden now, plus the piece's own burden, plus the reserve the
/// character's restock needs, stays at or under <paramref name="LinePercent"/> of the capacity (<c>EncumbranceSystem.EncumbranceCapacity</c>, 150 x Strength); a piece that does not fit is left in the chest, logged by name.
/// </summary>
/// <param name="LinePercent">The share of the capacity the burden may reach, in percent.</param>
/// <param name="ReserveBurden">The burden kept free for the restock (the ground profile's stock lines not already held).</param>
/// <param name="Always">M6 0 lead (live probe 3: a fresh climber stood at 164% before the chest, so the gate left every piece): the pieces taken over the line, the quest's named reward (the file's optional <c>always</c>
/// wcids); the burden they add is logged.</param>
public sealed record QuestCarry(int LinePercent, int ReserveBurden, IReadOnlyList<uint>? Always = null)
{
    /// <summary>True when the piece of <paramref name="wcid"/> is taken over the line.</summary>
    public bool TakesAlways(uint wcid) => Always is { } always && always.Contains(wcid);

    /// <summary>True when a piece of <paramref name="piece"/> burden fits at <paramref name="burden"/> of <paramref name="capacity"/>.</summary>
    public bool Fits(int burden, int capacity, int piece) => capacity > 0 && ((long)burden + piece + ReserveBurden) * 100 <= (long)capacity * LinePercent;
}

/// <summary>Item 5: the character levels a quest is for (the file's <c>level</c>: <c>min</c> at least 1, <c>max</c> at least <c>min</c>, or null for no top).</summary>
/// <param name="Min">The lowest level.</param>
/// <param name="Max">The highest level, or null for none.</param>
public sealed record QuestLevel(int Min, int? Max)
{
    /// <summary>True when <paramref name="level"/> is in the range.</summary>
    /// <param name="level">The character's level.</param>
    public bool Fits(int level) => level >= Min && (Max is not { } max || level <= max);

    /// <summary>The range as the logs print it: <c>1-40</c>, or <c>40+</c>.</summary>
    public override string ToString() => Max is { } max ? string.Create(CultureInfo.InvariantCulture, $"{Min}-{max}") : string.Create(CultureInfo.InvariantCulture, $"{Min}+");
}

/// <summary>
/// Item 8 (D118): when the ladder sends the climber on a quest, unprompted (the file's <c>ladder</c>): at <paramref name="FromLevel"/> or above and below <paramref name="BeforeLevel"/>. The limits are quest-file data, never constants (D26): the band is what keeps
/// her off a level-capped portal once she has outgrown it.
/// </summary>
/// <param name="FromLevel">The first level she runs it at (1 or more).</param>
/// <param name="BeforeLevel">The first level she no longer runs it at (above <paramref name="FromLevel"/>).</param>
/// <param name="Once">True: never again on this character once it is recorded done, even where the quest's timer would allow more (a one-time climbing errand).</param>
public sealed record QuestLadder(int FromLevel, int BeforeLevel, bool Once)
{
    /// <summary>True when <paramref name="level"/> is in the band.</summary>
    /// <param name="level">The character's level.</param>
    public bool Fits(int level) => level >= FromLevel && level < BeforeLevel;

    /// <summary>True when <paramref name="level"/> has passed the band.</summary>
    /// <param name="level">The character's level.</param>
    public bool Passed(int level) => level >= BeforeLevel;

    /// <summary>The band as the logs print it: <c>from 1 before 21 once</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"from {FromLevel} before {BeforeLevel}{(Once ? " once" : string.Empty)}");
}

/// <summary>Item 5: how a quest's experience is awarded (ACE's emote types; <c>cache/m6/5/facts.md</c> sections 1, 3 and 4).</summary>
public enum QuestExperienceKind
{
    /// <summary><c>fixed</c>: a set amount (AwardXP 2 and AwardNoShareXP 62: ACE @95ef426d <c>WorldObjects/Managers/EmoteManager.cs:142-147, 170-181</c>).</summary>
    Fixed = 0,

    /// <summary><c>proportional</c>: a share of the experience to the next level, capped and floored (AwardLevelProportionalXP 49: ACE @95ef426d <c>WorldObjects/Player_Xp.cs:441-454</c>, <c>GrantLevelProportionalXp</c>).</summary>
    Proportional = 1,
}

/// <summary>
/// Item 5: the experience a quest awards, as its NPC's emote row holds it (the file's <c>experience</c>; every value with its row in <see cref="Source"/>, D28). A
/// <see cref="QuestExperienceKind.Fixed"/> award has <see cref="Amount"/> above 0 and no <see cref="Max"/> or <see cref="Percent"/>; a
/// <see cref="QuestExperienceKind.Proportional"/> one has <see cref="Percent"/> above 0, <see cref="Max"/> the cap (ACE's <c>max_64</c>, or null for none) and
/// <see cref="Amount"/> the floor (ACE's <c>min_64</c>, or null for none).
/// </summary>
/// <param name="Amount">The fixed amount, or a proportional award's floor (null for none).</param>
/// <param name="Kind">How it is awarded.</param>
/// <param name="Max">A proportional award's cap, or null.</param>
/// <param name="Percent">A proportional award's share of the next level's experience (1.33 is 133%), or null for a fixed one.</param>
/// <param name="Source">The emote row the values come from.</param>
public sealed record QuestExperience(long? Amount, QuestExperienceKind Kind, long? Max, double? Percent, string Source)
{
    /// <summary>
    /// The award for a character whose next level is <paramref name="nextLevelXp"/> experience away, by ACE's rule (<c>GrantLevelProportionalXp</c>, @95ef426d
    /// <c>WorldObjects/Player_Xp.cs:441-454</c>: the share rounded, then capped by a max above 0, then floored by a min above 0); a fixed award is its amount. The
    /// server's modifiers (<c>xp_modifier</c>, <c>quest_xp_modifier</c>, 1.0 by default; facts.md section 5) are not applied: the reward line says what was granted.
    /// </summary>
    /// <param name="nextLevelXp">The experience from the character's level to the next.</param>
    public long At(long nextLevelXp)
    {
        if (Kind == QuestExperienceKind.Fixed)
        {
            return Amount ?? 0;
        }

        var scaled = (long)Math.Round(nextLevelXp * (Percent ?? 0));
        if (Max is { } max && max > 0)
        {
            scaled = Math.Min(scaled, max);
        }

        if (Amount is { } min && min > 0)
        {
            scaled = Math.Max(scaled, min);
        }

        return scaled;
    }

    /// <summary>The award as one log fragment: <c>fixed 20,000,000</c>, or <c>proportional 133% max 120,000</c>.</summary>
    public override string ToString() => Kind == QuestExperienceKind.Fixed
        ? string.Create(CultureInfo.InvariantCulture, $"fixed {Amount:N0}")
        : string.Create(CultureInfo.InvariantCulture, $"proportional {Percent * 100:0.##}%{(Max is { } m ? $" max {m:N0}" : string.Empty)}{(Amount is { } a ? $" min {a:N0}" : string.Empty)}");
}

/// <summary>M6 14 want loot: a creature whose create list can hold a hunt-until's item (the quest file's <c>carriers</c>).</summary>
/// <param name="Wcid">The creature's weenie class id.</param>
/// <param name="Name">The creature's name (the creature table's).</param>
/// <param name="Shade">The create-list row's shade: the item's chance a kill (ACE's <c>CreateListSelect</c>, one roll a set).</param>
/// <param name="Row">The <c>weenie_properties_create_list</c> row's id in the pinned world SQL.</param>
public sealed record QuestCarrier(uint Wcid, string Name, double Shade, int Row);

/// <summary>M6 14 want loot: a carrier's kills measured live over some minutes (the quest file's <c>kill_rate</c>).</summary>
/// <param name="Wcid">The carrier killed.</param>
/// <param name="Kills">The kills counted.</param>
/// <param name="Minutes">Over how many minutes.</param>
/// <param name="Source">Where it was measured (the live log and its times).</param>
public sealed record QuestKillRate(uint Wcid, int Kills, double Minutes, string Source)
{
    /// <summary>The kills a minute.</summary>
    public double PerMinute => Kills / Minutes;
}

/// <summary>M6 audit: where a quest's repeat timer starts (<see cref="Quest.TimerFrom"/>; the file's <c>timer_from</c>).</summary>
public enum QuestTimerFrom
{
    /// <summary>At the turn-in, the default: the station records the completion when it reads the done evidence.</summary>
    TurnIn,

    /// <summary>At the pickup of the hunt-until step's item, whose own Quest string the server stamps then (the file's <c>"pickup"</c>).</summary>
    Pickup,
}
