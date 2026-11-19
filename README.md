# Lifestone

Asheron's Call, just for you, on your own computer. And if you like, it plays itself while you watch.

## What this is

Asheron's Call closed its doors in January 2017. If you were there, some of it has probably stayed with you: the road
out of Holtburg, the purple swirl of a portal, a drudge that turned out to be much tougher than it looked, the long
walk back to your corpse.

Lifestone is a way to go back. It's a new game client that draws Dereth from your own copy of the game's files and
plays on a small private server that lives on your computer. There's no account and no subscription, and nobody else
is online. It's just the world, the way it was.

Most of us don't have the hours we had back then, so Lifestone adds one thing the old game never had: you can hand
your character to an autopilot. Make a new mage, tell her to go, and she takes it from there. She learns her first
spells at the Training Academy, hunts the fields around Holtburg, and works her way down into the tusker dungeons.
She sells her loot, buys new spells, does a few quests and keeps her buffs up. When she dies, she goes back for her
corpse like the rest of us had to.

You can leave her running and look in now and then to see what level she's reached, or take the controls back
whenever you feel like playing yourself.

She plays fair, too. There are no cheats: she only does what a player at the keyboard could do, casting from her
spell bar and drinking from her hotbar.

## What you can do today

- **Wander.** Fly or walk anywhere in Dereth at any time of day, into the buildings and down into the dungeons.
- **Play.** Log in through the old character screens and play in the interface you remember, with its chat, vitals,
  inventory, spellbook and vendors. Fight with magic, a sword or a bow, and loot what you kill.
- **Watch.** Start a brand-new mage and let her set off from level 1 for level 80 by herself. If you want a say, type
  her an order in the chat.

It's still early days. Most quests and NPC conversations aren't in yet, and neither are fellowships, crafting or
housing. A few windows still say "Not in Lifestone".

## What you need

- A Windows PC.
- Your own copy of Asheron's Call, from the end of its run. Lifestone doesn't include any of the game's files. It
  reads yours and never changes them.
- A little patience. There's no installer yet, so setting up means a few free tools and a handful of commands. The
  [technical guide](TECHNICAL.md#quick-start) walks you through it step by step.

Once that's done, this takes a look around Holtburg:

```powershell
pwsh -File tools\godot.ps1 -Run
```

And this logs you in to play:

```powershell
pwsh -File tools\play.ps1
```

## A few honest notes

- This is a fan project. It isn't official, and it isn't affiliated with or endorsed by the game's owners. Asheron's
  Call and its data files belong to them.
- It's single-player. Everything runs on your own computer and nothing goes online, so you can't play with friends.
- There are no screenshots here, on purpose. Every picture of the game shows the original art, and that isn't ours
  to hand out.
- It's free software under the GNU Affero General Public License v3.0 ([LICENSE](LICENSE)), and it comes with no
  warranty.

## Want the details?

The [technical guide](TECHNICAL.md) has the rest: where the project stands, the full setup, the controls, how it all
fits together, how to build and test it, and the licences of everything it builds on. The design notes are in
[docs/](docs/).

## Thank you

Lifestone only exists because the Asheron's Call community never let the game go. Thank you to:

- the [ACEmulator](https://github.com/ACEmulator/ACE) team, for the server, the
  [world database](https://github.com/ACEmulator/ACE-World-16PY-Patches) and years of patient work recovering how the
  game behaved;
- [Chorizite](https://github.com/Chorizite), for [DatReaderWriter](https://github.com/Chorizite/DatReaderWriter) and
  [WorldBuilder](https://github.com/Chorizite/WorldBuilder);
- [OpenAC](https://github.com/eriknihlen/OpenAC) and [ACViewer](https://github.com/ACEmulator/ACViewer);
- [Mag-nus/acclient-AI-RE](https://github.com/Mag-nus/acclient-AI-RE), for documenting the original client;
- the [Godot Engine](https://godotengine.org) contributors;
- and Turbine's original team, for Dereth.
