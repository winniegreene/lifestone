# Autopilot commands

Orders the owner gives the running autopilot (2026-11-17). One registry (`src/Lifestone.Client/Session/Autopilot/Commands/CommandRegistry.cs`) and one runner serve both ways in:

- **In-game chat:** type `/trip`, `/rebuff`, `/quest atlan-wand`, `/ladder` and so on. The replies appear in her chat window.
- **The steer desk:** `pwsh -File tools\order.ps1 -Run <run> <line>` drops an order in `cache\avatar\<run>\inbox` and prints its report as it moves. The run needs `--autopilot-steer-orders`, which `tools\climb.ps1 -SteerRun <run>` passes by default (`-NoOrders` turns it off).

Every order is checked before it acts and refused by name when a check fails. An order that moves her waits for the next **safe point**: the known state, or a lull in a fight with no hostile in reach, after every survival and stop trigger. She is never interrupted mid-cast, mid-portal, during a trip or during a death. Each order then runs an existing station: the logistics trip, the buff pass, the buff bot's stop or the quest station. It casts only from her spell bar and grants nothing. Each order logs a check before and a check after, and reports `done`, `failed:<why>` or `refused:<why>`.

Orders queue and run one after another. `stop`, `pause` and `resume` skip the queue. A person's `/stop` empties the queue. Adding `then idle` to an order makes her recall home and hold once its work is done. Without it, the loop resumes with its counts kept.

`/help`, `/status` and `/clear` were unbuilt names in retail's chat table. While an autopilot is wired, they answer the autopilot.

| What the owner types | What it does | Built |
|---|---|---|
| `/trip` | One logistics trip now: recall home, sell by the pack rules, the buys with the scroll plan, the supplies, then back. If it cuts a quest's hunt, the errand runs again from what she holds, so the hunt's count is kept | yes |
| `/sell` | The same trip. A sale-only visit is `TODO(m6-cmd-sell-only)` | yes (as the trip) |
| `/restock` | The same trip | yes (as the trip) |
| `/learn` or `/learn plan` | A trip whose buys follow the scroll plan. A single spell is `TODO(m6-cmd-learn-spell)` | yes (plan only) |
| `/buy <item> [n]` | Buys an item on the next trip | stub `TODO(m6-cmd-buy)` |
| `/rebuff` or `/rebuff self` | Her own pass now, from her spell bar. It renews every line due within a buff cycle, at home or on the ground | yes |
| `/rebuff bot` | The buff bot's stop at home. She goes home first when she is away. Refused `bot-off` when the bot does not apply | yes |
| `/buff <line>` | One buff line now | stub `TODO(m6-cmd-buff)` |
| `/heal` | Heals to full now | stub `TODO(m6-cmd-heal)` |
| `/rest` | Rests to full | stub `TODO(m6-cmd-rest)` |
| `/dance` | Revitalize, then Stamina to Mana | stub `TODO(m6-cmd-dance)` |
| `/home` or `/recall` | Recalls home and holds. The run ends at its next safe point and the game stays open | yes |
| `/go <place>` | Travels to a named place, town, ground or portal hop | stub `TODO(m6-cmd-go)` |
| `/walk <x y \| cell>` | Walks to a point | stub `TODO(m6-cmd-walk)` |
| `/ladder` | Back to the ladder's normal loop. From another task it re-seats with the counts carried; from off or done it starts the loop | yes |
| `/den` | The den run, once (its own task), then the ladder again (or idle) | yes |
| `/hunt <ground> [...]` | Hunts a named ground for a while | stub `TODO(m6-cmd-hunt)` |
| `/quest <key>` | Starts that quest's errand by the start-quest path, from home. Unknown keys, level gates, done-once, timers and the run's selection are refused by name. If the pack is at the load line or short of slots, a trip goes first | yes |
| `/quest-step <key> <n>` | The same, starting at step n (1 is the first) | yes |
| `/combine <item>` | Combines an item by its recipe | stub `TODO(m6-cmd-combine)` |
| `/equip <item\|best>` | The gear pass, or wields one item | stub `TODO(m6-cmd-equip)` |
| `/wear <item>` | Wears one item | stub `TODO(m6-cmd-wear)` |
| `/xp [what] [amount]` | Spends experience by the existing rules | stub `TODO(m6-cmd-xp)` |
| `/train <skill>` | Trains a skill | stub `TODO(m6-cmd-train)` |
| `/use <item>` | Uses an item | stub `TODO(m6-cmd-use)` |
| `/give <item> <npc>` | Gives an item to an NPC | stub `TODO(m6-cmd-give)` |
| `/pickup <item>` | Picks an item up | stub `TODO(m6-cmd-pickup)` |
| `/villa [step]` | The villa schedule's next step now | stub `TODO(m6-cmd-villa)` |
| `/stop` | A person stops her where she stands; only their own `/auto` gives control back. From the desk, a clean stop that recalls home. Either way the queue empties | yes |
| `/pause` | Stops where she stands at the next safe point | yes |
| `/resume` | Gives control back after a pause. A person's `/resume` is their `/auto` | yes |
| `/help [command]` | Lists the commands, or one command's usage, checks and report | yes |
| `/status` | Mode, task, stage, level, ground, the active order and the queue | yes |
| `/queue` | The active order and those queued | yes |
| `/cancel [n\|id]` | Cancels a queued order (the last one if none is named), or the active one before its safe point | yes |
| `/clear` | Cancels every queued order | yes |

The desk also takes the steer contract's `trip` order and `run_quest` order (with `args.key`). Both go to the same runner.
