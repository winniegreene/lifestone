// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Command/Handlers/AdminCommands.cs HandleAddSpell (AGPL-3.0)
// and Source/ACE.Server/WorldObjects/Player_Spells.cs LearnSpellWithNetworking with changes: the call sequence only
// (the spell table check, SpellIsKnown, Player.LearnSpellWithNetworking), at Player level for the requesting player's own
// character; a list of ids, bounded by the shared parser; one typed outcome per id (learned, known, unknown) read from the
// spell table and the spellbook around ACE's call instead of ACE's chat lines (LearnSpellWithNetworking returns nothing);
// the learn message is the quiet one (uiOutput false: the spellbook update, no play-script and chat line per spell);
// one machine-readable reply.
using ACE.DatLoader;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary><c>@lifestone spell learn &lt;id&gt;[,&lt;id&gt;...]</c>: teaches the player spells (<see cref="SpellLearnData"/>).</summary>
/// <remarks>
/// <para>ACE's <c>@addspell</c> (AdminCommands.cs:1086-1093) calls Player.LearnSpellWithNetworking (Player_Spells.cs:48-83), which
/// answers an id missing from the portal dat's SpellTable (:50-55) and a spell the player already knows (:57-65) with a chat line and
/// returns, and otherwise adds the spell to the spellbook (AddKnownSpell, :30-39), sends GameEventMagicUpdateSpell and a message. It
/// returns nothing, so the mod reads the table and the spellbook (SpellIsKnown) first and classifies each id
/// (<see cref="SpellOutcomes.Classify"/>, unit tested); after a spell classified as learned it reads the spellbook again, and a spell
/// ACE did not add is a loud failure, never a guessed outcome.</para>
/// <para>The command runs on ACE's world thread like <c>@addspell</c>, one id after another, to completion before the reply, so the
/// outcomes are this request's alone. An id above 65,535 is unknown (the update event carries a u16, Player_Spells.cs:67). The learn
/// message is the quiet one (uiOutput false), so a forty-spell list does not play forty effects; the client's spellbook learns each spell
/// from the update event either way. ACE saves the character on its own schedule; the mod writes nothing itself.</para>
/// </remarks>
internal static class SpellVerb
{
    /// <summary>Teaches <paramref name="player"/> the spells in <paramref name="request"/> and answers once with an outcome per id.</summary>
    public static void Learn(ToolCall call, Player player, SpellLearnRequest request)
    {
        if (player.IsLoggingOut)
        {
            call.Fail(ToolErrors.Busy, "you are logging out");
            return;
        }

        var table = DatManager.PortalDat.SpellTable.Spells;
        var outcomes = new List<SpellOutcome>(request.SpellIds.Count);
        foreach (var spellId in request.SpellIds)
        {
            var inTable = table.TryGetValue(spellId, out var spell) && spellId <= ushort.MaxValue;
            var outcome = SpellOutcomes.Classify(inTable, inTable && player.SpellIsKnown(spellId));
            if (outcome == SpellOutcomes.Learned)
            {
                player.LearnSpellWithNetworking(spellId, false);
                if (!player.SpellIsKnown(spellId))
                {
                    throw new InvalidOperationException("spell learn: ACE did not add spell " + spellId + " to the spellbook");
                }
            }

            outcomes.Add(new SpellOutcome(spellId, outcome, inTable ? spell?.Name : null));
        }

        var data = SpellLearnData.Of(outcomes);
        call.Succeed(request, data, data.Describe());
    }
}
