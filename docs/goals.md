# Lifestone: project goals

## 1. Faithful Asheron's Call, single player
- The real retail game, played solo and offline: quests, monsters, loot, spells and crafting as they were at end of retail.
- Game rules come from a bundled ACEmulator (ACE) server and the ACE-World database. It runs on localhost only and starts and stops with the game. AC was server-authoritative, so the rules were never in `acclient.exe`.
- The client is new and built in Godot 4.7.2 (.NET / C#). It renders the world from the user's own retail `.dat` files.
- Single-player conveniences are built in: teleporting to common places, item search and add, adding XP, respec and
  the like. They are tools of the client and of Lifestone's mod for the bundled server, so the game account never needs
  admin rights (`docs/milestones/m2-log-in.md` §7).

## 2. Built-in automation
Decal-era plugins become native features of the Lifestone client, not add-ons:
- **AFK dungeon fighting and looting** (VirindiTank-style): navigate, select targets, fight with melee, missile or magic, recover health/stamina/mana, loot corpses with loot rules, and handle death and recovery.
- **Auto-buffing**: maintain buff profiles, track enchantment durations, rebuff before expiry, and manage mana and components.
- Further convenience features in the same spirit (UtilityBelt-style). These are to be scoped in `docs/automation-vision.md`.

Design consequences:
- The client core is a **game-state model plus an action API** that does not depend on the engine.
- The UI, the built-in automation and automated tests all drive that same API.
- Automation only issues legitimate client actions to the server. ACE stays the authority on outcomes.
- The core can run headless, so automation and gameplay can be tested without rendering.

## 3. Machine safety (top priority)
- No admin installs, no services, no system changes.
- Servers listen on localhost only.
- Downloads are verified.
- Third-party code is read before it is built or run.

## 4. Later: LAN multiplayer
Friends on the same local network join one Lifestone server, after M3. ACE is a multiplayer server and the client speaks
the real protocol, so the base suits it. It is a deliberate change to §3 that a future milestone makes on purpose, with
the owner's go-ahead for each step. Until then, every rule in §3 stands. The change would cover:
- **The server and the client.** The server listens on a LAN address. The client's loopback policy becomes an
  allow-list of hosts the owner names.
- **The firewall.** A Windows Firewall rule that lets other machines reach the server. Adding it is an elevation prompt,
  so it needs the owner's explicit go-ahead.
- **Passwords.** The AC protocol does not encrypt logins, so passwords cross the LAN in clear text. The owner accepts
  that knowingly, or the milestone adds protection. ACE's packet log stays off.
- **Players and the cheat verbs.** One account per player. The single-player conveniences of §1 (the mod's verbs) get
  an access gate.
- **The dats.** Every machine supplies its own retail dats, which are never shipped.

Design rule meanwhile: network restrictions stay in one policy object, and the mod's verbs stay easy to gate, so the
change stays small.

## 5. Data
- The retail `.dat` files are supplied by the user and never committed.
- A local, gitignored cache of DAT-derived data is allowed.
