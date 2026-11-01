using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>M5 7 P7j (D143, D141): why a spell of the ground's learn list was not bought or not learned on a trip.</summary>
public enum LearnReason
{
    /// <summary>Nothing stands in the way.</summary>
    None,

    /// <summary>The spellbook holds the spell: the scroll is never bought again.</summary>
    Known,

    /// <summary>No PlayerDescription has filled the spellbook yet: nothing is bought on an assumption.</summary>
    SpellbookUnread,

    /// <summary>The spell's school is not trained (ACE: "You are not trained in ...!").</summary>
    Untrained,

    /// <summary>The school's current skill is under the spell's power less ACE's margin of 50 (ACE: "You are not skilled enough in ... to learn this spell.").</summary>
    SkillShort,

    /// <summary>The scroll is not in the pack when it is to be read (not bought: the coin, the pack or the vendor said no).</summary>
    NoScroll,

    /// <summary>The use was refused (a UseDone with an error, or no answer).</summary>
    UseRefused,

    /// <summary>The use was confirmed but the spellbook did not gain the spell within the wait.</summary>
    NotLearned,

    /// <summary>A refusal earlier in the session: not tried again until the session ends (no loop).</summary>
    RefusedBefore,
}

/// <summary>
/// M5 7 P7j (D143, the owner: learned by play): the resupply's learn step. For each spell of the ground's learn list (<see cref="HuntingGround.Learn"/>) the spellbook lacks, the trip buys one scroll at the profile's vendor on its walk
/// (before: the spellbook is read, the spell is not in it, the school meets ACE's requirement; the buy's own checks then hold the coin and a free slot) and reads it after the walk (before: the scroll is in the pack and the skill still
/// meets the requirement; act: Use; after: the spellbook holds the spell from the server's MagicUpdateSpell, and ACE's "You learn the ... spell." line). A spell the spellbook holds is never bought again; a refusal is logged with its
/// <see cref="LearnReason"/> and remembered in <see cref="SaleMemory.LearnRefused"/>, so no trip of the session tries it again (no loop).
/// </summary>
/// <remarks>
/// ACE (@95ef426d): <c>Scroll.ActOnUse</c> (Scroll.cs 45-127) sets the player to peace, plays the reading motion, waits 1 s, then answers "You already know that spell!" (86-90), or, when <c>Player.CanReadScroll</c> (Player_Skills.cs
/// 458-471) fails, "You are not trained in X!" or "You are not skilled enough in X to learn this spell." (96-105), else consumes the scroll and calls <c>LearnSpellWithNetworking</c> (Player_Spells.cs 48-78: GameEvent 0x02C1
/// MagicUpdateSpell and "You learn the X spell."), then "The scroll is destroyed." (112). The UseDone follows the whole motion (Player_Use.cs 223-238), with no error either way: the chat line and the spellbook are the proof.
/// </remarks>
public sealed partial class ResupplyRun
{
    /// <summary>ACE's margin under a scroll spell's power that the school's current skill must reach (<c>magicSkillCheckMargin</c>, Player_Skills.cs 456 @95ef426d).</summary>
    public const uint ScrollSkillMargin = 50;

    /// <summary>The longest wait for the spellbook to gain the spell after the use (ACE's reading motion, the 1 s read and the ready motion take about 4 s).</summary>
    public const double LearnSeconds = 15;

    private readonly List<LearnLine> _toRead = [];

    /// <summary>The spells learned on this trip.</summary>
    public int Learned { get; private set; }

    /// <summary>The scrolls bought for the learn list on this trip.</summary>
    public int ScrollsBought { get; private set; }

    /// <summary>The learn list's refusals on this trip, one per spell, as "spell:reason".</summary>
    public List<string> LearnRefusals { get; } = [];

    /// <summary>The learn step's line for the trip's end (the lead's grep: <c>resupply: learn:</c>).</summary>
    public string LearnFields => string.Create(CultureInfo.InvariantCulture, $"learned={Learned} scrolls_bought={ScrollsBought} learn_refused={(LearnRefusals.Count == 0 ? "none" : string.Join('+', LearnRefusals))}");

    /// <summary>
    /// ACE's <c>Player.CanReadScroll</c> (Player_Skills.cs 458-471 @95ef426d) over the read model: a spell of power under 50 or from 300 is read by anyone; else the school is trained and its current skill (with the buffs, as ACE's
    /// <c>Current</c>) is at least the power less <see cref="ScrollSkillMargin"/>. <paramref name="why"/> names the numbers.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="school">The spell's school.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="why">The numbers, or the reason the skill could not be read.</param>
    public static LearnReason CanRead(PlayerQualities q, MagicSchool school, uint power, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (power < 50 || power >= 300)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"power {power} needs no skill (ACE: under 50 or from 300)");
            return LearnReason.None;
        }

        if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(school), out var skill, out var noSkill))
        {
            why = noSkill;
            return LearnReason.Untrained;
        }

        var need = power - ScrollSkillMargin;
        why = string.Create(CultureInfo.InvariantCulture, $"{school} skill {skill}, power {power} needs {need}");
        return skill >= need ? LearnReason.None : LearnReason.SkillShort;
    }

    /// <summary>The learn list's scrolls to buy on this trip (each a one-ware buy at its vendor), every decision logged; the spells to read after the walk are kept.</summary>
    private List<ResupplyBuy> PlanLearns(HuntingGround ground)
    {
        var buys = new List<ResupplyBuy>();
        _toRead.Clear();
        foreach (var learn in ground.Learn)
        {
            var reason = LearnBefore(learn, out var why);
            if (reason != LearnReason.None)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}): {Token(reason)}: {why}"));
                continue;
            }

            _toRead.Add(learn);
            var held = InPack(learn.ScrollWcid).Count;
            if (held > 0)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}): not in the spellbook, {why}; a scroll (weenie {learn.ScrollWcid}) is in the pack already: no buy, read after the walk"));
                continue;
            }

            var vendor = ground.VendorOf(learn.VendorWcid);
            var ware = vendor?.Sells.FirstOrDefault(w => w.Wcid == learn.ScrollWcid);
            if (vendor is null || ware is null)
            {
                _toRead.Remove(learn);
                Log(Text($"learn: {learn.Name} ({learn.SpellId}): TODO(m5-learn-vendor): no profile vendor of weenie {learn.VendorWcid} lists the scroll {learn.ScrollWcid}; nothing is bought"));
                continue;
            }

            buys.Add(new ResupplyBuy(new StockLine(ware.Wcid, ware.Name, 1, 1, 1), vendor, 0, 1));
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): not in the spellbook, {why}: buy its scroll (weenie {ware.Wcid}, {ware.Price} pyreals in the profile) at {vendor.Name} on the walk, read it after"));
        }

        return buys;
    }

    /// <summary>The before-check of a learn: the spellbook is read and lacks the spell, no refusal earlier in the session, and the school meets ACE's requirement.</summary>
    private LearnReason LearnBefore(LearnLine learn, out string why)
    {
        var book = S.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            why = "the spellbook is not read yet (no PlayerDescription): nothing is bought on an assumption";
            return LearnReason.SpellbookUnread;
        }

        if (book.Knows(learn.SpellId))
        {
            why = "the spellbook holds it: the scroll is never bought again";
            return LearnReason.Known;
        }

        if (_memory.LearnRefused.TryGetValue(learn.SpellId, out var before))
        {
            why = "refused earlier this session (" + Token(before) + "): not tried again";
            return LearnReason.RefusedBefore;
        }

        if (!SpellOf(learn.SpellId, out var school, out var power))
        {
            why = "TODO(m5-learn-spell-data): neither the SpellTable given to the trip nor the profile holds the spell's school and power";
            return LearnReason.UseRefused;
        }

        return CanRead(S.State.Self.Qualities, school, power, out why);
    }

    /// <summary>The spell's school and power: the SpellTable the trip was given, else the profile's line (pinned against the dat by <c>BsdProfileSpellsTests</c>).</summary>
    private bool SpellOf(uint id, out MagicSchool school, out uint power)
    {
        if (Context.Spells.TryGetValue(id, out var info))
        {
            (school, power) = (info.School, info.Power);
            return true;
        }

        foreach (var line in ArenaProfile.Lines)
        {
            foreach (var spell in line.Spells)
            {
                if (spell.Id == id)
                {
                    (school, power) = (line.School, spell.Power);
                    return true;
                }
            }
        }

        (school, power) = (default, 0);
        return false;
    }

    /// <summary>Reads each scroll the plan kept, after the walk: before (held, the skill), the use, after (the spellbook holds the spell; the chat line). A refusal is remembered for the session.</summary>
    private IEnumerable<ActionHandle?> ReadScrolls()
    {
        foreach (var learn in _toRead)
        {
            Stage = "learn:" + learn.Name;
            var reason = LearnBefore(learn, out var why);
            if (reason == LearnReason.Known)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}) is in the spellbook already: no read"));
                continue;
            }

            if (reason != LearnReason.None)
            {
                RefuseLearn(learn, reason, why);
                continue;
            }

            var scroll = InPack(learn.ScrollWcid).FirstOrDefault();
            if (scroll == 0)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}): no scroll (weenie {learn.ScrollWcid}) is in the pack after the walk (the buy said why above): the next trip tries again"));
                LearnRefusals.Add(Text($"{learn.SpellId}:{Token(LearnReason.NoScroll)}"));
                continue;
            }

            var mark = S.State.Chat.Total;
            Log(Text($"learn: reading the scroll 0x{scroll:X8} of {learn.Name} ({learn.SpellId}): {why}"));
            var use = S.Actions.Use(scroll, ActionSource.Automation);
            yield return use;
            if (use.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
            {
                RefuseLearn(learn, LearnReason.UseRefused, "the use of the scroll ended " + Describe(use));
                continue;
            }

            string? answer = null;
            foreach (var h in Until(() => S.State.Self.Spellbook.Knows(learn.SpellId) || (answer = ScrollAnswer(mark)) is { } a && !a.StartsWith("You learn", StringComparison.Ordinal), LearnSeconds)) { yield return h; }

            answer ??= ScrollAnswer(mark);
            if (S.State.Self.Spellbook.Knows(learn.SpellId))
            {
                Learned++;
                Log(Text($"learn: learned {learn.Name} ({learn.SpellId}): the spellbook holds it (the server's update); ACE said: {answer ?? "(no line yet)"}; scrolls of it left in the pack {InPack(learn.ScrollWcid).Count}"));
                continue;
            }

            var refused = answer switch
            {
                { } t when t.Contains("not skilled enough", StringComparison.Ordinal) => LearnReason.SkillShort,
                { } t when t.Contains("not trained", StringComparison.Ordinal) => LearnReason.Untrained,
                { } t when t.Contains("already know", StringComparison.Ordinal) => LearnReason.Known,
                _ => LearnReason.NotLearned,
            };
            RefuseLearn(learn, refused, Text($"the spellbook does not hold it after the use (waited up to {LearnSeconds:0} s); ACE said: {answer ?? "nothing"}"));
        }
    }

    /// <summary>ACE's answer to a scroll's use in the chat since <paramref name="mark"/>: the learn line or a refusal line, else null.</summary>
    private string? ScrollAnswer(long mark)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var text = chat[i].Text;
            if (text.StartsWith("You learn the ", StringComparison.Ordinal) || text.Contains("to learn this spell", StringComparison.Ordinal) || text.StartsWith("You are not trained in", StringComparison.Ordinal) || text.StartsWith("You already know that spell", StringComparison.Ordinal))
            {
                return text.TrimEnd();
            }
        }

        return null;
    }

    private void RefuseLearn(LearnLine learn, LearnReason reason, string why)
    {
        _memory.LearnRefused[learn.SpellId] = reason;
        LearnRefusals.Add(Text($"{learn.SpellId}:{Token(reason)}"));
        Refuse("learn", Text($"{learn.Name} ({learn.SpellId}): {Token(reason)}: {why}; not tried again this session (no loop)"));
    }

    /// <summary>The reason as the log's token (kebab case).</summary>
    internal static string Token(LearnReason reason) => reason switch
    {
        LearnReason.None => "none",
        LearnReason.Known => "known",
        LearnReason.SpellbookUnread => "spellbook-unread",
        LearnReason.Untrained => "untrained",
        LearnReason.SkillShort => "skill-short",
        LearnReason.NoScroll => "no-scroll",
        LearnReason.UseRefused => "use-refused",
        LearnReason.NotLearned => "not-learned",
        LearnReason.RefusedBefore => "refused-before",
        _ => "other",
    };
}
