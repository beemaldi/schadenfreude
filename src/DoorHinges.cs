using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 11: opening or closing can tear a door (gate or trapdoor too) off its hinges, and it
/// lands as an item a little way off.
/// </summary>
public static class DoorHinges
{
    static readonly AssetLocation FallbackSound = new("game", "sounds/block/planks");
    const float SearchRadius = 4;

    /// <summary>
    /// The player has just opened or closed it. The door only flies off afterwards, so that the mod
    /// never cancels the game code - otherwise other mods would not get their turn at the same door.
    /// </summary>
    public static void AfterToggle(ICoreAPI api, BlockPos pos, IPlayer byPlayer, bool opening)
    {
        if (api?.Side != EnumAppSide.Server || byPlayer?.Entity == null || !SchadenfreudeModSystem.Affects(byPlayer)) return;
        TryFlyOff(api.World, pos, byPlayer.Entity.Pos.XYZ, byPlayer, byBear: false, opening: opening);
    }

    public static bool TryFlyOff(IWorldAccessor world, BlockPos pos, Vec3d from, IPlayer notify, bool byBear, bool opening)
    {
        DoorHingesConfig cfg = SchadenfreudeModSystem.Config.DoorHinges;
        if (!cfg.Enabled || world.Side != EnumAppSide.Server) return false;
        if (byBear ? !cfg.OnBear : (opening ? !cfg.OnOpen : !cfg.OnClose)) return false;
        if (!SchadenfreudeModSystem.Roll(world, cfg.ChancePercent)) return false;

        FlyOff(world, pos, from);
        if (notify != null) SchadenfreudeModSystem.Chat(notify, "schadenfreude:door-flyoff");
        return true;
    }

    /// <summary>
    /// Break the door the normal way (the game decides what drops - it differs per door type) and
    /// fling exactly those newly created item entities away from whoever caused it.
    /// </summary>
    public static void FlyOff(IWorldAccessor world, BlockPos pos, Vec3d from)
    {
        IBlockAccessor ba = world.BlockAccessor;
        Block block = ba.GetBlock(pos);
        Vec3d center = pos.ToVec3d().Add(0.5, 0.5, 0.5);

        var existing = new HashSet<long>();
        foreach (Entity e in world.GetEntitiesAround(center, SearchRadius, SearchRadius, e => e is EntityItem)) existing.Add(e.EntityId);

        ba.BreakBlock(pos, null);
        if (block.Sounds?.Break != null) world.PlaySoundAt(block.Sounds.Break, pos, 0, null);
        else world.PlaySoundAt(FallbackSound, pos, 0, null);

        double dx = center.X - from.X, dz = center.Z - from.Z;
        double len = System.Math.Max(0.001, System.Math.Sqrt(dx * dx + dz * dz));
        var velocity = new Vec3d(dx / len * 0.35, 0.22, dz / len * 0.35);

        foreach (Entity e in world.GetEntitiesAround(center, SearchRadius, SearchRadius, e => e is EntityItem))
        {
            if (existing.Contains(e.EntityId)) continue;
            e.Pos.SetPos(center.X, center.Y + 0.3, center.Z);
            e.Pos.Motion.Set(velocity);
        }
    }
}

[HarmonyPatch(typeof(BEBehaviorDoor), nameof(BEBehaviorDoor.ToggleDoorState))]
static class DoorToggleHingesPatch
{
    static void Postfix(BEBehaviorDoor __instance, IPlayer byPlayer, bool opened)
    {
        Guard.Run("door hinges", () => DoorHinges.AfterToggle(__instance.Api, __instance.Pos, byPlayer, opened));
    }
}

[HarmonyPatch(typeof(BEBehaviorTrapDoor), nameof(BEBehaviorTrapDoor.ToggleDoorState))]
static class TrapdoorToggleHingesPatch
{
    static void Postfix(BEBehaviorTrapDoor __instance, IPlayer byPlayer, bool opened)
    {
        Guard.Run("trapdoor hinges", () => DoorHinges.AfterToggle(__instance.Api, __instance.Pos, byPlayer, opened));
    }
}

/// <summary>Fence gates and legacy doors</summary>
[HarmonyPatch(typeof(BlockBaseDoor), nameof(BlockBaseDoor.OnBlockInteractStart))]
static class BaseDoorHingesPatch
{
    static void Postfix(BlockBaseDoor __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, bool __result)
    {
        if (!__result || world.Side != EnumAppSide.Server || blockSel == null) return;

        // __instance is still the old block (closed); the new state belongs to the block that
        // stands at the position now
        bool opening = world.BlockAccessor.GetBlock(blockSel.Position) is not BlockBaseDoor now || now.IsOpened();
        Guard.Run("gate hinges", () => DoorHinges.AfterToggle(world.Api, blockSel.Position, byPlayer, opening));
    }
}
