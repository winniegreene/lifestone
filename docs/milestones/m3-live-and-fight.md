# Milestone 3: live in it and fight (scope stub)

**Status: scope only.** The owner set this scope on 2026-10-14, while M2 was still in progress. The plan is written later,
in a fresh session, after M2's in-game shell (G4) and tools panel (G7) are merged, ideally while M2's wave 3 runs. It
follows the cost rules of [m2-plan.md](m2-plan.md) §6.4. M2 ([m2-log-in.md](m2-log-in.md)) stays the current milestone
until its DoD audit is done.

## Goal

A test character can be equipped, fully buffed in creature, life and item magic, and sent to fight monsters with war
magic on the bundled ACE, and it gains
experience, levels and skills from it. All of this runs in the retail UI and through the Action API.

**The primary consumer is automated test loops, not a person playing.** A loop spawns gear, equips it, buffs, fights,
and when the character dies, spawns fresh gear and goes out again. This is also the first step towards the built-in
automation goal ([goals.md](../goals.md) §2).

## In scope

- **Inventory:** the main pack; picking items up and dropping them; stacks and splitting; appraising an item (the ID
  panel).
- **Equipment:**
  - retail's paperdoll;
  - wielding casters (wands, orbs, staffs);
  - wearing armour and clothing, with the character's appearance changing (M2 already applies the server's ObjDesc
    updates);
  - burden.
- **Test gear:**
  - a Lifestone mod verb that spawns items into the caller's own pack, in the style of M2's `@lifestone quest` and
    `place` verbs;
  - preset kits (a caster's kit with armour);
  - later, a tools-panel button.
- **Fight, with war magic only:**
  - combat stances (peace and magic);
  - targeting;
  - the spellbook and spell bar, components and mana;
  - casting war projectiles at monsters, which fight back (ACE runs their AI);
  - the hit, evade, resist and damage messages;
  - health bars and vitals.
- **Buffs, before fight (owner decision 2026-10-14).** The character casts a full self-buff cycle in all three
  enchantment schools, the way the Decal-era buff scripts (VTank and its peers) did. Buffed properly, the character is
  nearly invulnerable against monsters of its level; unbuffed, retail's combat maths kills it almost at once.
  Buffing is built and proven before fighting.
  - **Creature:** the attributes (Strength, Endurance, Coordination, Quickness, Focus, Self) and the skills the
    character uses: the magic schools, Mana Conversion, Arcane Lore, Magic, Melee and Missile Defense.
  - **Life:** Armor Self, the seven protections (blade, pierce, bludgeon, fire, cold, acid, lightning), and
    regeneration of health, stamina and mana.
  - **Item:** on the worn armour, Impenetrability and the seven banes; on the wielded caster, its enchantments (for
    example Hermetic Link and Defender).
  - The cycle casts the highest level the character can, tracks every enchantment's remaining time, and re-buffs before
    they expire, all through the Action API, so the auto-buff goal reuses it.
- **Test arenas (owner decision 2026-10-14).** Fights are tested in specific dungeons, chosen so that a "full buff" for
  that place is short, which also shortens every test run. The first arena is the Black Spawn Den ("BSD"): a tusker
  dungeon whose tuskers deal only bludgeoning damage and are weak to fire. There, the life and
  item buffs shrink to the bludgeoning ones (Bludgeon Protection, Bludgeon Bane, with Armor Self and Impenetrability),
  and war magic needs only fire spells. A buff profile belongs to its arena (the damage it takes, the element it
  attacks with), the way buff-script profiles were set up per hunting ground.
- **Death, by retail's rules:**
  - vitae;
  - respawn at the lifestone;
  - ACE's item drop to the corpse stays on.

  Looting the corpse is out of scope: a loop spawns fresh gear instead.
- **Progression:** experience from kills; levels; spending experience on attributes and skills.
- **The test character: a combat mage, a legal character (owner decision 2026-10-14).**
  - Attributes: Focus 100, Self 100, Coordination 100. The rest of the creation points go to Strength, Endurance and
    Quickness, which is 10 each if the creation total is 330. Confirm it from CharGenData.
  - Specialised: War Magic and Melee Defense.
  - Trained, at creation or later with the credits levels give: Creature Enchantment, Item Enchantment, Life Magic,
    Arcane Lore and Mana Conversion.
  - Created through retail character creation (the Action API path), on the test account.
- **Effects and sounds on objects:** the server's PlayScript and Sound messages (F755, F750) for casts, hits and deaths,
  which M2 left as named TODOs.

## Split off to later milestones

- Melee and missile combat.
- Looting corpses, chests, side packs and other containers.
- Vendors and trading.
- Crafting, tinkering and salvage.
- Fellowships, allegiances, housing and player killing.

## Open for the planning session

- The exact buff list per level, taken from the user's own dats (the SpellTable and its spell sets, which Lifestone
  already decodes) and checked against what the Decal-era buff profiles cast. The list above is the starting point, not
  a verified table.
- The template's legal path: the creation build within the starting skill credits, and the order in which the trained
  skills are bought with the credits levels give, using CharGenData's real costs from the dats. Also the heritage,
  the character's name, the components and foci the gear kits must carry, and whether ACE's settings require
  components.
- The Black Spawn Den in ACE's world data: its landblock and entry portal, its creatures' damage types and
  resistances (to confirm bludgeoning only and weak to fire), and how a loop picks targets there. The test character
  fights there at level 126 (owner decision 2026-10-14). The planning session works out how it gets there (the mod's
  `xp` verb, then spending the experience on its template) and which buff and war spell levels a level-126 caster has.
- Later arenas for the other damage types, each with its own short profile.
- How much of automation's next phase (fight, buff and recover loops on the Action API) M3 includes.
- What M2's wave 3 changes in the shell, the Action API or the tools that M3 builds on.
