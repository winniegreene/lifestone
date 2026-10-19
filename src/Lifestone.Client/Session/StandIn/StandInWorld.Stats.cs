using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's answers to the stat actions (M3 s1b): a raise or a train is answered as ACE answers it (<c>Player_Attributes.cs</c>, <c>Player_Vitals.cs</c>, <c>Player_Skills.cs</c>,
/// <c>Player_Xp.SpendXP</c>, ACEmulator/ACE@95ef426d): the spent experience (PrivateUpdatePropertyInt64 0x02CF, AvailableExperience), then the stat's private update (0x02E3, 0x02E7 or 0x02DD)
/// with the rank the XpTable gives for the experience spent in total (ranks count from the creation value), a train's skill update then its credits (0x02CD); the cases ACE refuses with a
/// line get the line, and the ones it drops get nothing. The server's state is the session's own qualities (the stand-in has no other), so the numbers are the description's
/// (<see cref="StandInPlayerStats"/>). Placeholders, each named: the "Your base X is now N!" line is not sent (<c>TODO(m3-standin-raise-chat)</c>), a train costs what the request says (ACE
/// checks it against the SkillTable), and a skill is named by its id in a refusal line (<c>TODO(m3-standin-skill-names)</c>).
/// </summary>
internal sealed partial class StandInWorld
{
    private const uint TrainedClass = 2;
    private const uint SpecializedClass = 3;
    private const uint UntrainedClass = 1;
    private readonly Dictionary<(PrivateUpdateKind Kind, uint Key), byte> _updateSequences = [];

    private static readonly string[] AttributeNames = ["Strength", "Endurance", "Coordination", "Quickness", "Focus", "Self"];

    /// <summary>A RaiseAttribute request (GameAction 0x0045).</summary>
    internal OutboundTicket RaiseAttribute(uint attribute, uint xp)
    {
        Later(sink => Raise(sink, StatKind.Attribute, attribute, xp));
        return Ticket();
    }

    /// <summary>A RaiseVital request (GameAction 0x0044).</summary>
    internal OutboundTicket RaiseVital(uint vital, uint xp)
    {
        Later(sink => Raise(sink, StatKind.Vital, vital, xp));
        return Ticket();
    }

    /// <summary>A RaiseSkill request (GameAction 0x0046).</summary>
    internal OutboundTicket RaiseSkill(uint skill, uint xp)
    {
        Later(sink => Raise(sink, StatKind.Skill, skill, xp));
        return Ticket();
    }

    /// <summary>A TrainSkill request (GameAction 0x0047).</summary>
    internal OutboundTicket TrainSkill(uint skill, int credits)
    {
        Later(sink => Train(sink, skill, credits));
        return Ticket();
    }

    private enum StatKind
    {
        Attribute,
        Vital,
        Skill,
    }

    private XpTableData? XpTable => _visit.XpTable;

    private void Raise(IAcSessionSink sink, StatKind kind, uint id, uint xp)
    {
        var q = _reducer.State.Self.Qualities;
        string name;
        uint spent;
        uint ranks;
        IReadOnlyList<uint>? list;
        switch (kind)
        {
            case StatKind.Attribute:
                if (!q.TryGetAttribute(id, out var attribute))
                {
                    return; // ACE: "invalid attribute", no answer
                }

                name = id is >= 1 and <= 6 ? AttributeNames[id - 1] : "attribute " + id;
                (spent, ranks, list) = (attribute.Experience, attribute.Ranks, XpTable?.AttributeXpList);
                break;
            case StatKind.Vital:
                if (!q.TryGetVital(id, out var vital))
                {
                    return;
                }

                name = id switch { 1 => "Max Health", 3 => "Max Stamina", 5 => "Max Mana", _ => "vital " + id };
                (spent, ranks, list) = (vital.Experience, vital.Ranks, XpTable?.VitalXpList);
                break;
            default:
                if (!q.TryGetSkill(id, out var skill) || skill.AdvancementClass < TrainedClass)
                {
                    return; // ACE: "trained or specialized skill not found", no answer
                }

                name = "skill " + id;
                Placeholder("m3-standin-skill-names", "the stand-in names a skill by its id in a refusal line (no SkillTable names)");
                (spent, ranks) = (skill.Experience, skill.Ranks);
                list = skill.AdvancementClass == SpecializedClass ? XpTable?.SpecializedSkillXpList : XpTable?.TrainedSkillXpList;
                break;
        }

        if (list is null)
        {
            Line(sink, RaiseFailed(name) + " TODO(m3-standin-xp-table): the stand-in has no XpTable to price the raise by");
            return;
        }

        if (xp > q.AvailableExperience)
        {
            if (kind == StatKind.Vital)
            {
                Line(sink, RaiseFailed(name)); // ACE tells a vital (the client enables a 10-rank raise it cannot afford); the others stay silent
            }

            return;
        }

        var left = list[^1] - (long)spent;
        if (xp > left)
        {
            if (kind == StatKind.Attribute)
            {
                Line(sink, RaiseFailed(name));
            }

            return;
        }

        // The raise is answered with its updates only: ACE also tells "Your base X is now N!" (PlayerAttributes/Vitals/Skills raise).
        Placeholder("m3-standin-raise-chat", "the stand-in sends no 'Your base X is now N!' line after a raise");
        // SpendXP first (0x02CF), then the stat's own update, as ACE enqueues them.
        PrivateInt64(sink, PlayerQualities.AvailableExperienceProperty, q.AvailableExperience - xp);
        spent += xp;
        ranks = Rank(list, spent);
        switch (kind)
        {
            case StatKind.Attribute:
                PrivateUpdate(sink, PrivateUpdateKind.Attribute, id, attribute: q.Attributes[id] with { Experience = spent, Ranks = ranks });
                break;
            case StatKind.Vital:
                PrivateUpdate(sink, PrivateUpdateKind.Vital, id, vital: q.Vitals[id] with { Experience = spent, Ranks = ranks });
                break;
            default:
                PrivateUpdate(sink, PrivateUpdateKind.Skill, id, skill: q.Skills[id] with { Experience = spent, Ranks = (ushort)ranks });
                break;
        }
    }

    private void Train(IAcSessionSink sink, uint id, int credits)
    {
        var q = _reducer.State.Self.Qualities;
        if (credits > q.AvailableSkillCredits)
        {
            return; // ACE: not enough skill credits, no answer
        }

        var skill = q.TryGetSkill(id, out var known) ? known : new SkillDesc(id, 0, 1, UntrainedClass, 0, 0, 0, 0.0);
        if (skill.AdvancementClass >= TrainedClass)
        {
            Placeholder("m3-standin-skill-names", "the stand-in names a skill by its id in a refusal line (no SkillTable names)");
            Line(sink, "Failed to train skill " + id + "! You now have " + q.AvailableSkillCredits + " credits available.");
            return;
        }

        var left = q.AvailableSkillCredits - credits;
        PrivateUpdate(sink, PrivateUpdateKind.Skill, id, skill: skill with { AdvancementClass = TrainedClass, Ranks = 0, Experience = 0 });
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PlayerQualities.AvailableSkillCreditsProperty, value: left);
    }

    /// <summary>The highest rank whose cumulative cost the experience spent in total reaches (ACE's <c>CalcAttributeRank</c> and its siblings).</summary>
    private static uint Rank(IReadOnlyList<uint> list, uint spent)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (spent >= list[i])
            {
                return (uint)i;
            }
        }

        return 0;
    }

    /// <summary>An F7E0 line of chat type Broadcast (0), as ACE's <c>ChatPacket.SendServerMessage</c> sends a refusal.</summary>
    private static void Line(IAcSessionSink sink, string text) => sink.OnServerMessage(Context(sink), new ServerMessageMsg(text, 0));

    private static string RaiseFailed(string name) => "Your attempt to raise " + name + " has failed.";

    private void PrivateInt64(IAcSessionSink sink, uint key, long value) => PrivateUpdate(sink, PrivateUpdateKind.PropertyInt64, key, value: value);

    private void PrivateUpdate(IAcSessionSink sink, PrivateUpdateKind kind, uint key, long value = 0, AttributeDesc attribute = default, VitalDesc vital = default, SkillDesc skill = default)
    {
        _updateSequences.TryGetValue((kind, key), out var last);
        var sequence = unchecked((byte)(last + 1));
        _updateSequences[(kind, key)] = sequence;
        sink.OnPrivateUpdate(Context(sink), new PrivateUpdateMsg(kind, sequence, key, value, attribute, vital, skill));
    }
}
