using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Checkout;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M3 1.2 s5: what the checks expect of the description the stand-in sent. With no recording under <c>cache/sessions</c> it is the synthetic one (138 enchantments; health 52 of 75, stamina 120 of 160, mana 75 of 100:
/// the maximums with the registry's strongest layers applied). With a recording it is the recorded one: the enchantment count the recording holds, and the vitals the server sent at that login as the currents, and maxima derived from the recorded attributes, vitals,
/// gear property and registry (M3 1.2 fix R6; not assumed equal to the currents, and the verdict line says whether the recorded character was at full vitals).
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>
    /// The recorded description the visit sends (M3 1.2 s5): the recording under <c>cache/sessions</c> of this checkout or the main one whose PlayerDescription holds the most enchantments, or null (the synthetic one is
    /// sent; the loader logs which). The host lists the files here because <c>Lifestone.Client</c> does no file I/O.
    /// </summary>
    private static RecordedDescription? FindRecorded()
    {
        var sources = new List<RecordingSource>();
        foreach (var root in CheckoutLocator.EnclosingCheckouts(CheckoutLocator.DefaultSearchRoots()))
        {
            var folder = System.IO.Path.Combine(root, "cache", "sessions");
            if (!System.IO.Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in System.IO.Directory.GetFiles(folder, "*" + RecordingFormat.FileExtension).Order(StringComparer.OrdinalIgnoreCase))
            {
                var path = file;
                sources.Add(new RecordingSource(System.IO.Path.GetFileName(file), () => new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)));
            }
        }

        return RecordedDescription.TryFind(sources, GodotLogSink.Instance);
    }

    /// <summary>What the checks expect of the stand-in's description (<see cref="PlayExpectations.For"/> of <see cref="Visit"/>'s recording).</summary>
    private static PlayExpectations Expected => _expected ??= PlayExpectations.For(Visit.Recorded, Visit.Stats);

    private static PlayExpectations? _expected;

    /// <summary>The numbers the checks judge the stand-in's description by.</summary>
    /// <param name="Source">Where they come from, for the verdict lines.</param>
    /// <param name="Enchantments">The registry's count.</param>
    /// <param name="Health">The server's current health.</param>
    /// <param name="Stamina">The server's current stamina.</param>
    /// <param name="Mana">The server's current mana.</param>
    /// <param name="HealthMax">The expected maximum health.</param>
    /// <param name="StaminaMax">The expected maximum stamina.</param>
    /// <param name="ManaMax">The expected maximum mana.</param>
    private sealed record PlayExpectations(string Source, int Enchantments, uint Health, uint Stamina, uint Mana, uint HealthMax, uint StaminaMax, uint ManaMax)
    {
        /// <summary>
        /// The expectations for <paramref name="recorded"/>, or the synthetic description's when it is null, as the stand-in sends it with the visit's probe parts (<see cref="StandInSessions.WithProbe"/>: the harmful enchantment and the vitae of M3 2.2 fix B, which
        /// the vitae takes into the maxima).
        /// </summary>
        public static PlayExpectations For(RecordedDescription? recorded, StandInPlayerStats? stats)
        {
            // M3 1.2 fix R6: the currents are the sent ones, but the maxima are derived from the sent attributes, vitals, gear property and registry (the formula of retail's InqAttribute2nd over the
            // enchanted attributes, through EnchantmentRules), not assumed equal to the currents: a character recorded after damage, or a wrong maximum that happens to equal the current, is judged as it is.
            // M3 2.2 fix B: the synthetic description is derived the same way (its numbers were 52, 120, 75 over 75, 160, 100 before the vitae).
            var description = recorded is not null ? StandInSessions.WithProbe(recorded.Description, stats) : StandInSessions.SyntheticDescription(stats ?? new StandInPlayerStats());
            uint Current(uint id) => description.Vitals.FirstOrDefault(v => v.Id == id).Current;
            uint Attribute(uint id)
            {
                var a = description.Attributes.FirstOrDefault(x => x.Id == id);
                return EnchantmentRules.EnchantAttribute(description.Enchantments, id, a.Start + a.Ranks);
            }

            uint Maximum(uint id)
            {
                var vital = description.Vitals.FirstOrDefault(v => v.Id == id);
                var bonus = id switch
                {
                    1 => (Attribute(PlayerQualities.EnduranceAttributeId) + 1) / 2,
                    3 => Attribute(PlayerQualities.EnduranceAttributeId),
                    _ => Attribute(PlayerQualities.SelfAttributeId),
                };
                var gear = id == 1 && description.IntProperties.TryGetValue(PlayerQualities.GearMaxHealthProperty, out var g) ? g : 0;
                return EnchantmentRules.EnchantSecondaryAttribute(description.Enchantments, id, unchecked((uint)(vital.Start + vital.Ranks + bonus + gear)));
            }

            var (health, stamina, mana) = (Current(1), Current(3), Current(5));
            var (healthMax, staminaMax, manaMax) = (Maximum(1), Maximum(3), Maximum(5));
            var full = (health, stamina, mana) == (healthMax, staminaMax, manaMax);
            return new PlayExpectations(
                (recorded is null ? "synthetic description" : "recorded " + recorded.FileName) + " with the probe spellbook, a harmful enchantment and a vitae; maxima derived from the sent attributes, vitals and enchantments (" + (full ? "the character is at full vitals" : "the character is below its maxima") + ")",
                description.Enchantments.Length,
                health,
                stamina,
                mana,
                healthMax,
                staminaMax,
                manaMax);
        }
    }
}
