# Pack rules

M6 item 14. What the climber does with every kind of item that can be in her pack, and why. The supply trip
(`ResupplyRun`) reads the same rules from code: `SaleVerdict` (src/Lifestone.Client/Session/Quartermaster/SaleVerdict.cs)
for the order, `PackRuleBook` (src/Lifestone.Client/Session/Mage/PackRuleBook.cs) for the rows and the classes,
`FociComponents` (same folder) for the components, and `CoinFloat` (ResupplyStation.PackRules.cs) for the coin.
`PackRuleBookTests` fails when a rule in the code is missing from the table below.

Each verdict is logged once a trip with its rule name, for example:
`pack rule components-pea: sell 'Silver Pea' x2 0x80001F5F: a pea (a concentrated scarab): ...`.

ACE sources are at the pinned commit 95ef426d (`vendor-eval/ACE/Source`).

## The table

The rules are tried in this order. The first rule that matches decides. The table is the whole answer (M6 16 E5c): the town
trip offers an item for sale when its row says sell, and for no other reason (`SaleVerdict.Of`,
src/Lifestone.Client/Session/Quartermaster/SaleVerdict.cs; `ResupplyRun.SaleItems` selects by it). What a row needs from
the trip is one object, `SaleContext`: the ground's stock, town stock and kit lines, the casters and stones her gear rules
keep, the quest files' items, the scroll keeps, the session's sale memory and the pack layout's memory.

### 1. The sale's rows

These were the trip's own filters before the table was asked. A caster asks `gear-caster-kept` and then goes straight to
`mana-stones`.

| Class | Rule | Does | Buyer or place | Reason |
|---|---|---|---|---|
| Containers | `side-pack-held` | keep | | Inside a side pack and not a component, a healing kit or an essence. The sale reads the main pack and, in the side packs, those three kinds alone, so the kit pack's and the review pack's pieces stay where the layout put them. |
| Gear | `gear-caster-kept` | keep | | A caster her gear rules need (`CasterKeep`: the wielded, the best for each element she casts, the floor's spare, the charged spare, a useful great cantrip, the spend's target, an unread one). Any other caster goes on as loot. A caster asks none of the next five rows. |
| Kept aside | `kept-aside-match` | keep | | The keep-aside rule matches its appraisal (`KeepAsideRule`: a useful great cantrip, or the bonus the rule names). Worth a look; never offered. |
| Quest | `quest-item` | keep | | An item a quest file names: a hunt-until, expected or done item. It is kept whether quests are on or off, so a quest that comes due finds it. |
| Scrolls | `scrolls-kept` | keep | | A scroll the scroll rule keeps (`ScrollKeepRule`: her book can use it at her rung), or one the every-spell rule bought, was refused or wants. |
| Gear | `gear-review` | keep until the next visit | the review pack | Armour, clothing or jewellery the wear pick keeps for the next visit's review: not appraised yet (never sold blind), its cantrips worth wearing behind a requirement she does not meet yet, or a swap waiting for peace mode. Judged again at the next gear phase, then worn, kept again or sold (below). |
| Kept aside | `kept-aside-noted` | keep | | Noted by the loot step (`SaleMemory.KeptAside`): a find kept aside in the field, a scroll kept to read, a quest's want. The note lasts the session. |
| Mana stones | `mana-stones` | keep | | The keep (`ItemManaKeeper`): charged stones refill her items, and the keep's empty stones capture mana from junk. |
| Gear | `gear-rotation` | keep | the kit pack | Rotation gear: a piece the self-equip took off for an upgrade (`PackMemory.Rotation`), wherever it landed. Never a caster: the caster rule decides those. |
| Gear | `gear-leather-set` | keep | | A piece of the leather set while the swap is still to come (she wears a piece the set replaces). |
| Drinks | `drinks-surplus` | sell | Shopkeeper Renald the Elder (Misc 0x80) | A stack of a drink she uses over the pack budget's cut keep (`m6-pack-room`): what she does not drink between trips. Only when ACE would take it: not worn, with a Value, not attuned or retained. |
| Drinks | `drinks-in-use` | keep | | A health, mana or stamina drink the vitals rules drink, whatever the ground's stock lists. An empty flask is a trade good (Grocer). |
| Components | `components-focus-missing` | keep all | | A school she casts lacks its focus, so that school casts the account's full formula. Everything is kept until the focus is bought back. |
| Components | `components-book-unread` | keep all | | Her spellbook is not read yet. Nothing is sold on a guess. |
| Components | `components-burned` | keep | | With every focus held, the foci formula of a spell in her book burns it: the scarab of each level in her book, and the Prismatic Taper. |
| Components | `components-stocked` | keep | | A scarab or taper the ground's stock or town stock buys. Selling it would loop with the buy. |
| Components | `components-unknown` | keep, named | | A component outside the component map that is not a pea. `TODO(m6-pack-rule-unknown)`. |
| Unknown | `dropped-before` | keep | | Dropped before, or its drop was tried, or two trips' vendors refused it (`SaleMemory.Dropped`). Never offered again this session. |
| Supplies | `supplies-stock` | keep | | A stock line or a town stock line of the ground. The trip buys it, so selling it would loop with the buy. |
| Supplies | `supplies-kit` | keep | | A kit line of the ground: her kit, never loot. |

### 2. The classes

An item that is no component and is a note of the ground's purse, a focus, money or a container is kept here by
`money-notes`, `foci-held`, `money-coin` or `containers-in-use`, before the rows below it. Four rows of part 1 stand
again at their own places in this order: `quest-item` after `gear-worn` (a caster that is a quest file's item is kept
there), `drinks-in-use` after `travel-gem`, `mana-stones` after `essences`, and `gear-review` before `gear-loot`. A
component's row stands after `drinks-in-use`.

| Class | Rule | Does | Buyer or place | Reason |
|---|---|---|---|---|
| Foci | `foci-held` | keep, one of each | | A focus casts its school on the foci formula (`Player.HasFoci`, Player_Spells.cs:460-488). If one is lost, the trip buys it back (below). |
| Money | `money-coin` | keep the float | | The coin over the float becomes trade notes, but only when that frees a slot (below). |
| Money | `money-notes` | keep | | Trade notes are the purse. 250 share a slot, and a death takes none of them. One is changed back at 1.0 when the coin is short, from whichever pack it lies in (below). |
| Containers | `containers-in-use` | keep | | A side pack the layout uses (components, kit, review), or any pack that holds something. |
| Containers | `containers-spare` | sell | Fispur Ansel the Grocer (Container 0x200) | An empty pack beyond the three the layout wants. ACE refuses to buy a container that is not empty (Player_Commerce.cs:285-290). |
| Gear | `gear-worn` | keep | | Worn or wielded. |
| Travel | `travel-gem` | keep | | A portal gem is one use of travel: the Facility Hub's gem, or the Aphus Lassel gem for the island hop (TravelRoutes). |
| Components | `components-pea` | sell | Archmage Cindrue | No formula names a pea, and ACE never lets one stand in for a scarab (below). |
| Components | `components-scarab-unused` | sell | Archmage Cindrue | A scarab of a level her book does not hold. |
| Components | `components-account-only` | sell | Archmage Cindrue (SpellComponents 0x1000) | Coloured tapers, herbs, powders, potions and talismans are burned only by the account formula. While every focus is held, no spell burns them. |
| Value 0 | `self-buff-gem` | keep | | An eternal gem that casts a lasting buff on her when used (`SelfBuffGems`): kept whatever its value or attunement, never sold or dropped, and used at home whenever its spell is absent or has under 2 h left (the owner, 2026-11-15). Example: Blackmoor's Favor (weenie 31000, spell 3811: +50 natural armour for 24 h, layered with the others). |
| Value 0 | `value-zero-attuned` | keep | | ACE refuses both its sale (Player_Commerce.cs:275-280) and its drop (Player_Inventory.cs:1389-1393). Example: Blackmoor's Favor (attuned, bonded). |
| Value 0 | `value-zero-gear` | keep | | A piece of gear with no value, from a quest set such as the Pathwarden's. The gear judge may wear it, and the return pass gives it back to its NPC for the reward. |
| Value 0 | `value-zero-drop` | drop at the home | the ground | No buyer pays for it, and no rule needs it. Dropping is a normal player action. ACE leaves the item on the ground, where it rots in 5 minutes (WorldObject_Decay.cs:18, 57-61). Example: Oil of Rendering, the Academy's. |
| Healing kits | `healing-kits` | sell | Shopkeeper Renald the Elder (Misc 0x80) | No autopilot rule uses a kit. Her Healing is untrained; she heals by spell and drink. A kit that a ground's kit or stock lists is kept by that line. |
| Essences | `essences` | sell | Shopkeeper Renald the Elder (Misc 0x80) | A summoning essence, and she has no Summoning. |
| Mana stones | `mana-stones-shed` | sell | Archmage Cindrue (ManaStone 0x80000) | An empty stone beyond the keep. |
| Tinkering | `tinkering-tool` | drop at the home | the ground | Ust (TinkeringTool 0x20000000). She does not salvage, and no Holtburg vendor's mask takes the type (SELECT 1 below). |
| Tinkering | `tinkering-salvage` | sell | Shopkeeper Renald the Elder (TinkeringMaterial 0x40000000) | She does not tinker. |
| Scrolls | `scrolls` | sell | the Grocer or Archmage Cindrue (Writable 0x2000) | `ScrollKeepRule` first keeps any scroll her book can use at her rung. |
| Gear | `gear-loot` | sell | Renald (armour, clothing, weapons, drinking vessels), Monyra the Jeweler (jewellery), Cindrue (casters) | First kept, each named by its own line: the floor's two casters (the gear score's best), the worn set, an attuned piece (kept to return to its NPC), a kept-aside find, and the rotation. |
| Trade goods | `trade-goods` | sell | Monyra the Jeweler (Gem 0x800), the Grocer (Food, CraftCookingBase), Renald (the rest of his mask) | A good a home buyer's mask takes. |
| Unknown | `unknown` | keep, named once | | No rule names it, and no home buyer's mask takes its type. `TODO(m6-pack-rule-unknown)`: never dropped on a guess. |

### 3. The guards on a row that would sell

When a row of part 2 would sell the item, the sale's guards are asked in this order: worn (`gear-worn`), no value, attuned,
retained, a class ACE refuses (`unsellable-class`), and a caster her gear rules keep (`gear-caster-kept`). The first that
stands keeps the item. A row of part 2 that keeps or drops is not changed by a guard.

| Class | Rule | Does | Buyer or place | Reason |
|---|---|---|---|---|
| Value 0 | `value-zero-kept` | keep | | No value, on a row that would sell it (a component the rule sells). ACE refuses the sale of an item without a value (Player_Commerce.cs:275-280), and no rule drops it. |
| Gear | `attuned-kept` | keep | | Attuned (PropertyInt 114), on a row that would sell it. ACE buys an attuned piece and only stops the vendor reselling it (Vendor.cs:626-627); it is kept back so the return pass can give it to its NPC for the reward. |
| Gear | `retained-kept` | keep | | Retained (PropertyBool 91, or the description's Retained bit), on a row that would sell it. ACE refuses its sale (Player_Commerce.cs:268-273). |
| Trade goods | `unsellable-class` | keep | | A weenie class ACE answers "is unsellable" whatever the vendor's mask: IsSellable 0 (WorldObject_Properties.cs:1460-1464; the sale's check, Player_Commerce.cs:268-273). Two classes are listed, each refused live and confirmed by a read-only SELECT of the world copy (`VendorStock.UnsellableClass`): Bread 259 and Arrow 31717. The wire does not carry IsSellable, so a class joins the list per confirmed refusal (`TODO(m4-vendor-issellable-wire)`). No sale takes it, so it is no sale value and the field never counts it as sell loot. |

**The one asker that does not ask `unsellable-class`** (M6 16 E5c2): the town trip's own sale list, while its old
filters stand beside the verdict (below). They offer a Bread or an Arrow, ACE refuses it, and two trips' refusals stop
the offers (`dropped-before`). The trip's context says so by name (`SaleContext.OffersRefusedClasses`), so the list is
the list it was; the flag goes with the old list.

### Who asks the table

Every place that decides whether an item may be sold or dropped asks `SaleVerdict.Of` (M6 16 E5c2). Each asks it over a
context of the session as it stands (`SaleContext.For`); a trip names its own parts over that.

| Asker | Its question | What it adds to the verdict |
|---|---|---|
| The town trip's sale (`ResupplyRun.SaleItems`) | which items are offered | nothing; it does not ask the guard `unsellable-class` while its old filters stand beside it (part 3) |
| The junk drop after the sale (`ResupplyRun.DropJunk`) | which items are dropped at the home | nothing: the verdict is drop |
| The field's make-room rule (`CantripRoom.SellLoot`) | which main-pack items may be dropped for a great cantrip piece or a better find; their burden is what the Strength floor leaves out | the field's keeps (below) |
| The trip trigger's sale estimate (`SaleEstimate`, for `LogisticsCounters.CarriedSaleValue`) | the Value and the payout of what a sale would sell | a buyer of the ground takes the type (`SaleVerdict.WillSell`) |
| The start-up audit's sale gate (`StartupAudit.SaleOffers`) | can the sale change the purse at all | the same |
| The item mana drain's never-list (`ResupplyRun.DrainCandidates`) | which loot with mana an empty stone may drain | an item the verdict drops at the home may still be drained |

Outside the verdict, by name: the melee and archer paths (`Fighter/SellRules.cs`, `ShopStations.cs`), and the load the
wear pick reads as kept after a sale (`ResupplyRun.KeptBurden`, on the verdict since M6 16 Q10d).

### The field's keeps

The make-room rule drops only what the verdict sells, and of that never (`CantripRoom.FieldKeep`, the one place):

1. **A caster or a wearable she has not appraised.** It may cast a great cantrip, and a drop cannot be taken back. The
   table sells it (`gear-loot`) because the town trip appraises every such piece before its sale; the field has no such
   step.
2. **A piece that casts a useful great cantrip, of any family**, whatever keep-aside rule is in force on the ground. The
   verdict's keep-aside row reads the ground's rule, and is not asked of a caster.
3. **A drink she uses**, even a stack the pack budget would sell (`drinks-surplus`). Outside a trip the budget is
   measured part of the way between two visits, so it under-reads what she drinks before the next one.

What the field may drop that it kept before M6 16 E5c2: a component the table sells (a pea, a scarab of a level her book
does not hold, an account-formula component while every focus is held), and a leather-set piece once the swap is done.
What it keeps that it could drop before: a scroll the every-spell rule keeps, an item whose drop was tried before, a
Bread or an Arrow.

### The sale estimate

The trip trigger calls a sale trip when the loot carried is worth the ground's `sale_trip_value`, a stock line or a
scroll is wanted, and the coin cannot pay it. The estimate is the Value of the items the verdict sells that a buyer of
the ground takes, and their payout at the best buyer's rate.

- **It is cached.** A check asks it every second of a fight and allocates nothing: the estimate is worked out afresh
  only when a stamp changes (the counts of the main pack, the side packs and all she holds, the coin, the worn count, the
  counts of the sale and pack memories, the spellbook's count, her level). An appraisal of an item she already holds and
  the pack budget's measure are not stamps; they move the answer at the next change of one.
- **What a recompute costs**, measured in the unit rig on the development machine on 2026-11-18: 30,216 bytes and about
  0.45 ms for a pack of 8 with a caster and a mana stone; 165,944 bytes and 1.4 to 2.3 ms for a pack of 146 plain items.
- **What it cannot know.** A wearable not yet appraised reads as loot; the trip appraises it first, and a piece its wear
  pick then wears or keeps for review is not sold. A drink stack the trip would split is counted by its whole stacks.

### The guard on the sale (removed)

Through the verdict's first live gates the trip's old filters stood beside it in production and in the tests. Five live
town trips read `sale_parity=agree` with no `sale-parity:` error line, and the tests' parity check never found a
difference, so M6 16 Q11 removed the old list, the guard and the `sale_parity` field. The trip's list is the verdict's
alone. One part of the old arrangement stays on purpose: the trip's context offers the classes ACE refuses (a Bread or
an Arrow, `SaleContext.OffersRefusedClasses`), because the second trip's refusal drops such an item (`NoteRefusal`);
every other asker reads the row `unsellable-class`.

### What the book reads outside a trip

M6 16 E5c put a report-only shadow beside the four older askers; M6 16 E5c2 moved them onto the verdict and removed the
shadow (its `sale-shadow:` lines and its `sale_shadow_agree=` and `sale_shadow_differ=` fields are gone).

The session's context (`SaleContext.For`) reads the two parts only a trip could say, each from the place the trip reads
it:

- **The every-spell rule's scroll keeps** (`EverySpellKeep`): a scroll ACE refused to teach, one the rule bought, one that
  is the target of its line. On while the job's switch is on. The job states who she is, the SpellTable and its facts to
  the session's sale desk once a session (`SaleDesk.StateJob`); a station that runs with no job states its own. One
  difference from a trip: whether her gear asks more Arcane Lore than her own is read from what she wears alone (the held
  pieces' wants need the wear judge a trip publishes, `TODO(engine-wear-terms)`).
- **The drinks a trip's budget would sell** (`PackBudgetRead.SurplusNow`): the stamina dance read now, her use and the
  loot since the last visit measured to now, the budget planned from them, and of each drink it cuts the whole stacks
  that fit over the cut keep. The part of a stack a trip would split off is not in it. Before the session's first visit
  nothing is measured, so no drink is surplus.

## Armour and jewellery: the wear pick

The gear phase runs at the start of every home visit, before the sale. It appraises the worn set and the held gear (at
most 40 a visit), then judges every held armour, clothing and jewellery piece by its score in percent of her
experience an hour (`WearScore`, the ladder's `wear_score`). The score is the set she would wear with the piece on,
less the set she wears now:

- **Wear** in a free slot (an empty ring, bracelet or necklace slot, or a free coverage) when the score is above 0.
- **Replace** the pieces it displaces when it beats them by the swap margin (0.1% of the hour). The pieces taken off go
  to the main pack, and this table decides them at the sale: `gear-loot` sells them; an attuned one stays, and so does a
  keeper (`kept-aside-match`, by the keep-aside in force for her).
- **Keep for review** (`gear-review`): an unread piece, one whose cantrips would be worth wearing but a wield or
  activation requirement is unmet, or a swap that waits for peace mode.
- **Sell** (`gear-loot`): everything else, after it was read and judged.

**A piece is scored on its own numbers** (`ArmourBase`, src/Lifestone.Client/Session/Mage/ArmourBase.cs). ACE sends an
appraisal's armour level and protections with the enchantments on the piece already in them (`AppraiseInfo.cs` 407-408,
`ArmorProfile.cs` 40-59 @95ef426d) and lists those enchantments beside them. The score takes them out again (the
strongest of each spell family, the families summed, by the rows of `ArmourSpellRows`) before it adds her own lines, so
a piece she wears and has cast on is judged as the same piece is in the pack. Before, the worn piece counted its
Impenetrability and its bane twice and won every comparison: in runs 5 and 6 of 2026-11-19 the Pathwarden Robe went
back on over the leather set a quarter of a second after each swap. The armour report and the fight's own armour line
read the same numbers (`ArmorReport.Read`).

One guard stands over the pick (M6 16 E5e2, the owner's standing rule): a worn piece that casts a great cantrip useful to
her (`CantripJudge`: Major and above, a basic attribute or a skill she holds or her plan trains) is never taken off for
a piece that casts none, whatever the score says. The judge is the session's one (`Wardrobe.JudgeOn`,
src/Lifestone.Client/Session/Quartermaster/Wardrobe.cs): the trip, the worn restorer, the fight's loot step and the
autopilot's caster pick ask the same one.

**A keeper she cannot light, and what she wears** (M6 16 E5e5, the owner's decision of 2026-11-18). A piece with a useful
great cantrip that she cannot activate is kept: never sold, never dropped. It holds no worn slot against a piece whose
magic works for her: she wears what works. So the one question is two, each asked by name:

- **Is this a keeper** (`CantripRoom.IsUsefulGreatPiece`): it casts a great cantrip useful to her, whether or not she can
  activate it. The sale (`kept-aside-match`), the make-room rule, the junk drop, the pack sorter (the review pack) and the
  villa's store list ask this, as before.
- **Does its great cantrip count for her on her body** (`GreatOnBody`,
  src/Lifestone.Client/Session/Mage/GreatOnBody.cs): a keeper whose spells are on her, or that ACE would light at a wield
  now, or that cannot light *yet*. The guard above asks this of the worn piece and of the piece in her hand, and so does
  the pick's choice between two ring or bracelet sides worth the same.

"Cannot yet" is told from "can never" by what her build moves. *Never*: the piece's magic names a skill she neither
holds trained nor has in her plan (loot armour's own limit, on Melee Defense or Missile Defense: `LootGenerationFactory_Magic.cs`
177-203), or another heritage. *Not yet*: everything else that keeps its spells off: her Arcane Lore above all (the lore
spend raises it toward such a piece: `LoreSpend`), her level, a skill she holds or plans, an attribute, a vital, the item's
mana. A "not yet" piece is kept worn, or wanted, as before. A "never" piece that she wears is judged on what it does give
(its armour, with its spells off) and is replaced by a piece that beats it; the verdict's line names it, and taken off it
goes to the pack as a keeper, not to the sale. The answer is read at each ask and never remembered: an untrained defence
still has a value (Quickness and Coordination, her buffs in), and the day it meets the limit the piece lights and holds
its slot again. A skill whose value is not read is never called "never".

Nothing puts such a piece back on for its dead cantrip. The pick gives a piece in the review pack no credit for spells
that would stay off: it stays there, kept. The worn restorer wears into free slots alone, and its memory is the set she
wore last, which no longer holds a piece the pick took off.

**A piece whose spells cannot light is judged on its armour** (M6 16 E5e5), in the rule of before the pick as in the pick.
Worn, it is scored on the armour it gives with its spells off, never as nothing, so weaker armour does not take it off.
Found, it is a candidate on its armour alone, no better and no worse than a plain piece of that armour: worn in a free
coverage, or in place of a worn piece it beats; the line says its spells stay off. Only a wield requirement she does not
meet scores a piece 0: she cannot put it on. The attuned return pass (`GearReturn`) compares on the same score, with one
guard (M6 16 E5e6, `gear-return-unlit`): an attuned piece that only a worn piece with its spells off for her scores above
is kept, not given back to its NPC, and named once a visit, for a Give cannot be undone.

**What a worn piece's spells do for her** (M6 16 E5e4, read in ACE at 95ef426d):

- A piece's spells are cast on her when she wields it, and only if she can activate it at that moment: her Arcane Lore
  meets its ItemDifficulty, and, where the piece carries a skill limit (loot armour can: `LootGenerationFactory_Magic.cs`
  177-203), that skill of hers meets the limit (`WorldObject_Use.cs` 214-228). An appraisal carries the limit's skill
  as PropertyInt 176 and its level as PropertyInt 115. A mage meets no Missile Defense or Melee Defense limit, so such a
  piece's spells and cantrips never light on her, and the pick gives her no credit for them.
- A buff spell of a piece that is weaker than her own standing spell of the same category still sits in her registry,
  on a layer of its own with the piece as its caster (`EnchantmentManager.cs` 134-203); the stronger one is the one that
  applies. So such a piece reads as lit (`WearScore.LitOn`, `GearLight.Read`), and it adds nothing while hers stands.
- A cantrip is never under one of her buffs: no cantrip the pick reads shares a category with a buff of its stat
  (`GearLightDatTests`, over the retail SpellTable). A lit cantrip always counts.
- A piece is taken off and put on again to light it only when that can work: one of its spells is absent from her
  registry, nothing stronger of hers stands in its place, and she meets the lore and the limit now. Otherwise it stays
  on, and the log says why once a piece a run.

A swap happens only at a home visit and in peace mode. Each piece comes off and goes on with a check after, and her
skills and vitals are read again and logged. After the sale the equip pass judges again with her load read again.

So a looted piece with a cantrip is never sold blind. Either the visit appraises it and the pick judges it before the
sale, or the pick keeps it for the next visit.

## Components and the foci

When she holds a school's focus, ACE casts that school on the foci formula (`SpellFormula.GetCurrentFormula`,
SpellFormula.cs:383-386). The foci formula is the base formula's scarabs and chorizite, plus 1 to 4 Prismatic Tapers
by spell power (`GetFociFormula`, 340-381).

A cast checks and burns only the formula's own weenies:

- `GetRequiredComps` (388-402) maps each slot to its one weenie (`Spell.GetComponentWCID`, Spell.cs:226-236).
- `HasComponentsForSpell` (Player_Magic.cs:1227-1247) counts that weenie.
- `TryBurnComponents` (1181-1222) consumes it.

**The peas.** A pea never stands in for its scarab. No spell formula names a pea (the dat's components 113 to 191, the
weenies 8284 to 8357, 20963 and 25729), and the count above reads the scarab's own weenie. So the Copper, Iron and
Silver Peas are sold.

**Every level in her book.** She keeps the scarab of every level her book holds, not only the top level. The casting
rules step down a line's levels when a higher one cannot be cast (`LevelFloor`, the buff bootstrap's levels II and
III). Her book holds Flame Bolt I and Heal Self II, so the Lead Scarab stays: one slot.

**The account-formula backup.** This is the profiles' keep-2 lines and the long-run branch's every-colour backup.
While every focus is held, it is not kept:

- `LogisticsCounters.IsSuspended` takes the lines' stop, trip and buy away, and the buy list, the startup audit and
  the status line skip them.
- The sale sells what is held.
- A lost focus brings the lines back (`components-focus-missing`).

## A lost focus is bought back

A death can drop a focus. The next trip buys it back at Holtburg's scrivener, through `FociPlan`, the trip's
`foci:` step. The scrivener catalog lists all four foci:

| Focus | School | Seller (Holtburg, landblock 0xA9B3) | Create-list row | Price |
|---|---|---|---|---|
| Foci of Enchantment 15268 | Creature Enchantment | Apprentice Scrivener of Creature Magic 20205 | 76977 | 25,000 |
| Foci of Artifice 15269 | Item Enchantment | Apprentice Scrivener of Item and War Magic 20215 | 77964 | 25,000 |
| Foci of Verdancy 15270 | Life Magic | Apprentice Scrivener of Life Magic 20217 | 33572 | 25,000 |
| Foci of Strife 15271 | War Magic | Apprentice Scrivener of Item and War Magic 20215 | 77965 | 25,000 |

Each price is Value 500 times SellPrice 50 (`Vendor.GetSellCost`, Vendor.cs:577-585). The coin for it comes from the
whole purse: notes are changed at 1.0 when the coin is short (`FociWcids` in the buy list).

## Coin: the float

ACE pays a sale in new coin stacks of at most 25,000 each. It refuses the whole sale when those stacks do not fit
the free slots (Player_Commerce.cs:176-191, 302-318). A trade note costs 1.15 times its face and is changed back at
1.0 (`GetSellCost` and `GetBuyCost`, Vendor.cs:577-599). Up to 250 notes share one slot.

- **The float** is the trip's own spend at the profile's prices: every home stock line under its keep, bought up to
  its keep, plus every town stock line under its trip line, bought up to its keep. The account formula's lines are
  left out while every focus is held. The spend is rounded up to whole coin stacks of 25,000, at least one.
- **Why that much.** A stack takes one slot whether it holds 1 pyreal or 25,000, so the float holds the slots the
  spend needs anyway, and no more. The coin the trip spends stays coin, because a note changed back for a buy loses
  the 15% it cost. The coin over the float would become notes at the end of the trip anyway, so changing it first
  costs nothing more and gives its slots to the payout.
- **When.** The coin over the float is changed before every sale (it used to be only under three free slots), after
  the buys, and between the sale's rounds.
- **Only when it frees a slot.** It is changed only when the coin's stacks after the change, plus one stack for each
  new note denomination, are fewer than its stacks before. Otherwise a note costs 15% and frees nothing.
- **The den run.** The den run's order (`JourneyFacts.CoinToNotes`) keeps its floor of 0.
- **A note in a side pack** (M6 16 E5a2). The purse every judge counts is what the counter can pay with: the coin and every
  note she holds (`PurseView.CounterCanPayUnreadAsZero`). The counter (`VendorCounter`) changes a note where it lies: ACE's
  sale takes a guid from anywhere she holds it (`VerifySellItems` over `GetAllPossessions`, Player_Commerce.cs:232-298,
  Player_Inventory.cs:29-44). One note of a stack is split off first, into the main pack when it has a free slot, else
  into a side pack that has one: ACE adds the split to the container the action names and to no other
  (Player_Inventory.cs:2256, 2445). With no free slot anywhere the change is refused by name. A vendor whose mask is
  known to take no note (`NoteMasks`, from the profiles' `buys_item_types`) is judged on the coin alone; no listed
  vendor's mask lacks the note type today.
- **What it replaced.** The float replaces the profile's `purse.coin` (a restock from nothing: 66,000 at the Abode,
  three stacks, when a trip spends about one).

## The live pack

From a read-only SELECT of ace_shard (character Qwinnie, 2026-11-13, about 12:50 UTC):

- All four foci are held.
- The main pack is full: 102 of 102 slots. The components sack 0x800001A4 holds 24 of 24, the kit pack 24 of 24, and
  the review pack 3 of 24.
- 39 component stacks: 15 in the main pack and 24 in the sack.

| | Main pack | Sack | Total |
|---|---|---|---|
| Component stacks sold | 10 (Birch Talisman, Hawthorn, Blue, Green, Grey, Pink and White Tapers, Copper, Iron and Silver Peas) | 23 (every herb, powder, talisman, potion and coloured taper) | 33 |
| Kept | 5 (Copper, Gold, Silver and Pyreal Scarabs, Prismatic Taper) | 1 (Lead Scarab) | 6 |
| Healing kits sold | 2 (Gifted, Excellent) | 18 (12 Adept, 1 Gifted, 2 Excellent, 3 Peerless, in the kit pack) | 20 |
| Essences sold | 6 | | 6 |
| Dropped after the sale | 3 (2 Oil of Rendering, Ust) | | 3 |

After the sort, the 5 components kept in the main pack go into the emptied sack. The main pack then frees 26 of its
102 slots: 10 components, 5 moved to the sack, 2 kits, 6 essences and 3 dropped. That is before the loot sale. The sack
then holds 6 of 24, and the kit pack 6 of 24.

**Kept by name:**

- Blackmoor's Favor (`self-buff-gem`).
- The Facility Hub Portal Gem (`travel-gem`).
- The Mana Draught and the tinctures (`drinks-in-use`).
- The three side packs (`containers-in-use`).

**Coin.** She holds 52,094 pyreals in three stacks. At the Abode her float is 50,000:

- the Prismatic Taper to its keep, 569 x 30 = 17,070;
- the Mana Tincture of the town stock under its trip line, 21 x 775 = 16,275.

That leaves 2,094 over the float, under the smallest note's 11,500, so it stays coin, and the slots come from the
rules above.

## SELECTs (read-only, on the world copy, 2026-11-13)

1. **No Holtburg buyer of a tinkering tool.**
   `SELECT li.weenie_Class_Id, s.value, HEX(li.obj_Cell_Id), HEX(i.value) FROM landblock_instance li JOIN weenie_properties_int i ON i.object_Id = li.weenie_Class_Id AND i.type = 74 JOIN weenie_properties_string s ON s.object_Id = li.weenie_Class_Id AND s.type = 1 WHERE (i.value & 0x20000000) <> 0 AND (li.obj_Cell_Id >> 16) IN (0xA9B3, 0xA9B4, 0xAAB3, 0xAAB4, 0xA8B4, 0xA8B3, 0xA9B5, 0xAAB5)`
   returns no row. 13 vendors buy the type elsewhere in Dereth; none is on her trips.
2. **The foci sellers in Holtburg.**
   `SELECT cl.weenie_Class_Id, cl.object_Id, s.value, f.value, cl.id FROM weenie_properties_create_list cl JOIN weenie_properties_string s ON s.object_Id = cl.object_Id AND s.type = 1 LEFT JOIN weenie_properties_float f ON f.object_Id = cl.object_Id AND f.type = 38 JOIN landblock_instance li ON li.weenie_Class_Id = cl.object_Id WHERE cl.weenie_Class_Id IN (15268, 15269, 15270, 15271) AND cl.destination_Type = 4 AND (li.obj_Cell_Id >> 16) = 0xA9B3`
   gives the rows of the foci table above.
3. **Blackmoor's Favor 31000.** PropertyInt 19 Value 0, 33 Bonded 1, 114 Attuned 1. No NPC's emote takes it.

## Open

- `TODO(m6-pack-rule-unknown)`: an item no rule names stays in the pack, named. The live pack has none.
- A dropped item is not watched after it leaves the pack. ACE's rot timer is the record.
- The junk is dropped after the sale (`junk_dropped=` in the trip's verdict line). A pack that is full when the trip
  starts still opens its first slot with the sale's own rule: the cheapest item offered is dropped. A trip that walks
  nowhere (nothing to buy or sell) ends before its junk pass.
- Still beside the verdict (`TODO(engine-sale-copies)`, docs/automation-engine.md section 6): the trip's old sale list
  and its guard, and with them the flag that keeps a Bread or an Arrow on the trip's list.
- The loot-gear keeps (the gear rules' casters, the kept-aside rule, the rotation, the leather set) are rows of the
  table since M6 16 E5c (part 1). Their answers are the sale's existing ones, unchanged.
- The fight's loot step still takes armour and jewellery by value per burden under the load line. A piece left on a
  corpse because the pack was full is not appraised, so its cantrips are not read.
