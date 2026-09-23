using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace BadLuck;

/// <summary>
/// Mechanic 6: taking the crafting output out breaks a stick.
/// Only the stick is lost, the other ingredients stay in the grid, and nothing is produced.
///
/// The server decides. The client has already predicted the craft and gets its inventories
/// re-sent by the server afterwards.
/// </summary>
public static class StickBreak
{
    const string CraftingOutputType = "Vintagestory.Common.ItemSlotCraftingOutput";
    static readonly AssetLocation StickCode = new("game", "stick");
    static readonly AssetLocation BreakSound = new("game", "sounds/block/stickbreak");

    /// <summary>Block follow-up attempts from the same click (shift-click tries several target slots)</summary>
    const long BlockFollowUpMs = 300;
    static readonly Dictionary<string, long> lastBreakMs = new();

    static FieldInfo hasLeftOversField;

    public static void Register(ICoreServerAPI api)
    {
        lastBreakMs.Clear();
        api.Event.PlayerDisconnect += player => lastBreakMs.Remove(player.PlayerUID);
    }

    public static MethodBase OutputMethod(string name, params System.Type[] parameters)
    {
        return AccessTools.Method(CraftingOutputType + ":" + name, parameters);
    }

    /// <returns>true = prevent the take</returns>
    public static bool TryBreak(ItemSlot outputSlot, IPlayer actingPlayer)
    {
        if (outputSlot.Inventory is not InventoryBasePlayer inv) return false;
        if (inv.Api.Side != EnumAppSide.Server || outputSlot.Empty) return false;

        IPlayer player = actingPlayer ?? inv.Player;
        if (player == null) return false;

        IWorldAccessor world = inv.Api.World;
        if (lastBreakMs.TryGetValue(player.PlayerUID, out long lastMs) && world.ElapsedMilliseconds - lastMs < BlockFollowUpMs)
        {
            return true;
        }

        // Leftover output from an earlier craft: the ingredients are already used up
        hasLeftOversField ??= AccessTools.Field(outputSlot.GetType(), "hasLeftOvers");
        if (hasLeftOversField?.GetValue(outputSlot) is true) return false;

        StickBreakConfig cfg = BadLuckModSystem.Config.StickBreak;
        if (!cfg.Enabled || !BadLuckModSystem.Affects(player)) return false;

        ItemSlot stickSlot = null;
        foreach (ItemSlot slot in inv)
        {
            if (slot != outputSlot && slot.Itemstack?.Collectible?.Code.Equals(StickCode) == true)
            {
                stickSlot = slot;
                break;
            }
        }
        if (stickSlot == null || !BadLuckModSystem.Roll(world, cfg.ChancePercent)) return false;

        lastBreakMs[player.PlayerUID] = world.ElapsedMilliseconds;

        // Clear the output right away: on a shift-click vanilla would otherwise throw a leftover
        // output on the ground. The game recalculates the recipe afterwards.
        outputSlot.Itemstack = null;
        outputSlot.MarkDirty();

        stickSlot.TakeOut(1);
        stickSlot.MarkDirty();

        ResyncInventories(player);

        StupidSounds.PlayOr(world, StupidSounds.Bonk, BreakSound, player.Entity, 16);
        BadLuckModSystem.Chat(player, "badluck:stickbreak-message");
        Splinter.Catch(player);
        return true;
    }

    /// <summary>Re-send every slot to the client, so that its prediction is thrown away</summary>
    static void ResyncInventories(IPlayer player)
    {
        foreach (IInventory inventory in player.InventoryManager.Inventories.Values)
        {
            if (inventory is not InventoryBase inv) continue;
            for (int i = 0; i < inv.Count; i++)
            {
                inv.MarkSlotDirty(i);
            }
        }
    }
}

[HarmonyPatch]
static class CraftingOutputTryPutIntoPatch
{
    static MethodBase TargetMethod() => StickBreak.OutputMethod("TryPutInto", typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType());

    static bool Prefix(ItemSlot __instance, ref ItemStackMoveOperation op, ref int __result)
    {
        IPlayer player = op?.ActingPlayer;
        if (!Guard.Run("stick break", () => StickBreak.TryBreak(__instance, player), onError: false)) return true;
        __result = 0;
        return false;
    }
}

[HarmonyPatch]
static class CraftingOutputFlipWithPatch
{
    static MethodBase TargetMethod() => StickBreak.OutputMethod("FlipWith", typeof(ItemSlot));

    static bool Prefix(ItemSlot __instance)
    {
        return !Guard.Run("stick break", () => StickBreak.TryBreak(__instance, null), onError: false);
    }
}
