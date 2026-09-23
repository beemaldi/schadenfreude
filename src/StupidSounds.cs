using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Schadenfreude;

/// <summary>
/// Silly sound effects (the "StupidSoundEffects" switch, on by default).
/// The files live in assets/schadenfreude/sounds/stupid/ and are mono - only then does the game place a
/// sound in a direction.
/// </summary>
public static class StupidSounds
{
    public static readonly AssetLocation BearDoor = new(SchadenfreudeModSystem.ModId, "sounds/stupid/surprise");
    public static readonly AssetLocation Fart = new(SchadenfreudeModSystem.ModId, "sounds/stupid/fart");
    public static readonly AssetLocation Snake = new(SchadenfreudeModSystem.ModId, "sounds/stupid/alert");
    public static readonly AssetLocation Bonk = new(SchadenfreudeModSystem.ModId, "sounds/stupid/bonk");
    public static readonly AssetLocation Falling = new(SchadenfreudeModSystem.ModId, "sounds/stupid/fall");
    public static readonly AssetLocation ToolFly = new(SchadenfreudeModSystem.ModId, "sounds/stupid/swoosh");
    public static readonly AssetLocation EatStone = new(SchadenfreudeModSystem.ModId, "sounds/stupid/chew");
    public static readonly AssetLocation EyeChip = new(SchadenfreudeModSystem.ModId, "sounds/stupid/slap");
    public static readonly AssetLocation Slip = new(SchadenfreudeModSystem.ModId, "sounds/stupid/slide");

    public static bool On => SchadenfreudeModSystem.Config.StupidSoundEffects;

    /// <summary>Plays the sound at this creature; false = the switch is off</summary>
    public static bool Play(IWorldAccessor world, AssetLocation sound, Entity at, float range = 32)
    {
        if (!On || at == null) return false;
        // No pitch randomization: a meme sound should sound like the original
        world.PlaySoundAt(sound, at, null, false, range);
        return true;
    }

    /// <summary>The silly sound, or the one the game ships, when it is switched off</summary>
    public static void PlayOr(IWorldAccessor world, AssetLocation stupid, AssetLocation normal, Entity at, float range = 32, float volume = 1)
    {
        if (!Play(world, stupid, at, range)) world.PlaySoundAt(normal, at, null, true, range, volume);
    }

    /// <summary>Plays the sound at this position; false = the switch is off</summary>
    public static bool Play(IWorldAccessor world, AssetLocation sound, BlockPos pos, float range = 32)
    {
        if (!On || pos == null) return false;
        world.PlaySoundAt(sound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, false, range);
        return true;
    }
}
