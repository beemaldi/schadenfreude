# Schadenfreude

A Vintage Story mod that makes ordinary survival actions occasionally go wrong.

Every routine action — mining, eating, crafting, walking, throwing — carries a small, configurable
chance to backfire. Nothing here is fatal on its own; it is there to make a quiet day in the world
a little less quiet.

Requires **Vintage Story 1.22** and the
[Integrated Mod Manager](https://mods.vintagestory.at/integratedmodmanager), which every setting is
exposed through.

## The mechanics

| # | What can happen |
|---|---|
| 1 | A ripe crop jumps out of the ground and runs away from you |
| 2 | You eat the stone instead of knapping it |
| 3 | Fire ants, when you sit down on soil |
| 4 | Hornets, when you sprint across the ground |
| 5 | A thrown stone explodes on impact — and can blow a crater |
| 6 | A stick snaps while you take the crafting output |
| 7 | A splinter from a stick or a wooden tool handle, stinging for minutes |
| 8 | Vegetables give you gas: farts that lure predators to you |
| 9 | Bears open every closed door they find, and smash fence gates |
| 10 | A snake drops out of a felled tree, bites and slithers off |
| 11 | A stone chip in the eye while mining or knapping — one eye shuts |
| 12 | A door comes off its hinges as you open or close it |
| 13 | A tool or weapon slips out of your hand and flies off |
| 14 | A torch held into the wind sets your worn-out clothes on fire |
| 15 | You slip off the edge while sneaking |
| 16 | You stumble and go down flat while running |
| 17 | You choke on your food, and the coughing carries |
| 18 | Chests in ruins and dungeons are sometimes mimics |
| 19 | A thrown stone comes back and bonks you on the head |

All chances default to around 1 % per action and can be set individually, turned off, or turned up
until the world becomes unplayable.

## Settings

Everything lives in `ModConfig/schadenfreude.json` and is edited in game through the Integrated Mod
Manager: first the probabilities, then an advanced block with durations, radii, damage values and
the block and creature codes each mechanic reacts to. Changes apply without a restart.

Chat messages are always English, whatever language the game runs in. The manager labels are
English and German.

### Silly sound effects

The `StupidSoundEffects` switch (on by default) swaps a handful of sounds for meme clips — the bear
announcing itself before it opens your door, and so on. Turn it off for a straight-faced game.

Those clips are not in this repository, since they are not mine to redistribute. Building from
source leaves those nine sounds silent until you drop your own mono `.ogg` files into
`resources/assets/schadenfreude/sounds/stupid/`: `alert`, `bonk`, `chew`, `fall`, `fart`, `slap`,
`slide`, `surprise`, `swoosh`. Mono matters — the game will not place a stereo file in space.

## Testing

Most mechanics sit at a 1 % chance, which makes them nearly impossible to verify by playing. With
the `controlserver` privilege:

```
/schadenfreude test <cliff|stumble|fart|choke|eye|splinter|toolfly|snake|ants|hornets|torch|stone|door|boomerang>
```

## Building

Needs the .NET 10 SDK and a Vintage Story installation.

```
set VINTAGE_STORY=C:\path\to\Vintagestory
dotnet build -c Release
```

The `PackageMod` target writes `schadenfreude_<version>.zip` to `ZipOutputDir`. Drop that in your `Mods`
folder.

## Compatibility

The mod hooks into game code with Harmony, and tries to do so gently: almost every patch is a
postfix, so other mods touching the same methods still get their turn. Every hook that hangs off a
frequent game event is wrapped in an error guard — a bug in Schadenfreude must never break doors, eating
or crafting for everyone.
