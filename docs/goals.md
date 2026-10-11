# Lifestone: project goals

## 1. Faithful Asheron's Call, single player
- The real retail game, played solo and offline: quests, monsters, loot, spells and crafting as they were at end of retail.
- Game rules come from a bundled ACEmulator (ACE) server and the ACE-World database. It runs on localhost only and starts and stops with the game. AC was server-authoritative, so the rules were never in `acclient.exe`.
- The client is new and built in Godot 4.7.2 (.NET / C#). It renders the world from the user's own retail `.dat` files.

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

## 4. Data
- The retail `.dat` files are supplied by the user and never committed.
- A local, gitignored cache of DAT-derived data is allowed.
