using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 16 Q9: how a character plays, as named policies on one small object, read for the session's character by <see cref="For"/>. A site that used to compare the character's name with the ladder's (<see cref="MageTemplate.IsClimber"/>)
/// now asks the policy it means, so a policy has one definition and a second character that plays as she does (the model's climber, <see cref="MageTemplate.ModelClimberName"/>) gets every one by being a climber, never by a new name test.
/// Three objects exist: the climber's, the model climber's (the climber's with her creation, 2026-11-18) and everyone else's (BSD Mage, the fighters and any other character); both are static, so a per-frame caller allocates nothing.
/// </summary>
public sealed class CharacterPolicy
{
    private CharacterPolicy(bool climber, bool enduranceForCoordination = false)
    {
        EnduranceForCoordination = enduranceForCoordination;
        // Every policy below is the climber's alone today; each is named for the decision it answers so a later character can differ in one without the others.
        ManaBySum = climber;
        WarLadder = climber;
        StarterFoci = climber;
        BuffsByPlay = climber;
        SuppliesScrolls = climber;
        CheatFree = climber;
        LiesToRest = climber;
        ScenarioMage = climber;
        KeepAsideDefault = climber ? KeepAsideRule.ClimberDefault : null;
    }

    /// <summary>The ladder's character and the model's climber: every policy on.</summary>
    public static CharacterPolicy Climber { get; } = new(true);

    /// <summary>The model's climber (<see cref="MageTemplate.ModelClimberName"/>): the climber's policies, created with Endurance 100 and Coordination 10 (<see cref="EnduranceForCoordination"/>).</summary>
    public static CharacterPolicy ModelClimber { get; } = new(true, enduranceForCoordination: true);

    /// <summary>Any other character (BSD Mage, the fighters, a name not known yet): every policy off.</summary>
    public static CharacterPolicy Other { get; } = new(false);

    /// <summary>The policy of the character named <paramref name="characterName"/>: the model climber's for hers, the climber's for every other name <see cref="MageTemplate.IsClimber"/> accepts, else <see cref="Other"/>.</summary>
    /// <param name="characterName">The character's name as the read model holds it, or null before it is known.</param>
    public static CharacterPolicy For(string? characterName) => !MageTemplate.IsClimber(characterName) ? Other : string.Equals(characterName, MageTemplate.ModelClimberName, StringComparison.OrdinalIgnoreCase) ? ModelClimber : Climber;

    /// <summary>
    /// The owner's ruling of 2026-11-18, for the sprint: the character is created with the traditional mage's Endurance and Coordination swapped,
    /// Endurance 100 and Coordination 10 (<see cref="MageTemplate.ClimberCreationValue"/>). The model's climber only; Winnie and every other mage keep the template.
    /// </summary>
    public bool EnduranceForCoordination { get; }

    /// <summary>
    /// Every press picks its level by the net (<see cref="Vitals.ManaOrder.PressLevelOf"/>) and the mana choices follow the sum of what a press nets, at any Mana Conversion (the owner's ruling, 2026-11-08: pick by the sum); the others keep
    /// the highest level that lands. <c>BSD Mage</c> and M5's pins are unchanged.
    /// </summary>
    public bool ManaBySum { get; }

    /// <summary>
    /// She is built by the war mage's ladder (<see cref="Grounds.LadderLoader.WarMage"/>): the experience spend follows it level by level (health first, <see cref="ClimberHealthRule"/>), the corpse run reads it for the level's gear, and the
    /// trip's spend counts the Strength that stands beside her ranks, the held caster's points and her Arcane Lore. Any other character spends by the plan it was given, with no ladder and no floor.
    /// </summary>
    public bool WarLadder { get; }

    /// <summary>
    /// Her starter pack holds the War and Life foci (<c>starterGear.json</c>, ACE's PlayerFactory.cs 225-300 @95ef426d), so the foci audit assumes them without a worn kit, the component floor counts no other school's focus formula, and the
    /// foci plan (the trip's, the counters' due check) buys the rest. Any other character holds the foci its ground's kit lists, and the fighters cast no foci formula.
    /// </summary>
    public bool StarterFoci { get; }

    /// <summary>
    /// Her buff profile is <see cref="BuffProfiles.Climber"/>: every line learned by play (her book holds only the levels she has earned), the armour banes among them. The buff cycle given no profile takes it, and the rebuff host's profile
    /// lookup returns it. Any other character has the profile its own table gives it (BSD Mage's arena profile, a fighter's) or the arena profile as before.
    /// </summary>
    public bool BuffsByPlay { get; }

    /// <summary>The learn trip buys the components of the scrolls she learns (the shopping list, the kits): BSD Mage's kit carries its own and the fighters learn no scrolls by trip.</summary>
    public bool SuppliesScrolls { get; }

    /// <summary>The keep-aside a ground without a block of its own gives her (<see cref="KeepAsideRule.ClimberDefault"/>: Major, Epic and Legendary cantrips of the listed lines and a War Magic bonus of three percent); null for any other character, whose ground states its own or none.</summary>
    public GroundKeepAside? KeepAsideDefault { get; }

    /// <summary>
    /// She is cheat-free (M6 D10, D34): the server mod never grants her an item, experience or a place (the one send path refuses a counted verb for her by name, unless the session opted in), a failed way home is a loud stop and never a recovery
    /// place, and a journey with no place in its budget reaches an outdoor ground on foot. Any other character may be granted and placed as its scenario says.
    /// </summary>
    public bool CheatFree { get; }

    /// <summary>She lies down to recharge, where nothing awake is within reach (the owner's ruling, 2026-11-06; <c>VitalsKeeper.CreatureAwakeNear</c>), unless the command line names a posture. Any other character rests as its options say.</summary>
    public bool LiesToRest { get; }

    /// <summary>
    /// The scenarios that act for a mage (the gear and cast-facts character check, the recall-home host) act for her as they do for BSD Mage, with the caster swap left out where she holds no swap kit; any other character (the fighters have their own
    /// hosts) is refused by name.
    /// </summary>
    public bool ScenarioMage { get; }
}
