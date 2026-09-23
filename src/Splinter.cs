using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace BadLuck;

/// <summary>
/// A splinter from a broken stick or from a tool with a wooden handle: for a while it stings when
/// mining, attacking, crafting, building, using things and working with tools. The remaining time
/// sits on the player (and is saved), and only counts down while they are online. It never kills
/// (it stops at 1 HP). A bandage or poultice pulls it out.
/// </summary>
public static class Splinter
{
    const string RemainingKey = "badluck-splinter-seconds";

    static ICoreServerAPI sapi;
    static readonly ActionThrottle stingThrottle = new();
    static readonly ActionThrottle toolThrottle = new();
    static HashSet<string> stickTools;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        stingThrottle.Clear();
        toolThrottle.Clear();
        stickTools = null;
        api.Event.RegisterGameTickListener(OnTick, 1000);

        api.Event.DidBreakBlock += (player, oldBlockId, blockSel) =>
        {
            if (BadLuckModSystem.Config.Splinter.StingOnMining) Sting(player);
        };
        api.Event.DidPlaceBlock += (player, oldBlockId, blockSel, withItemStack) =>
        {
            if (BadLuckModSystem.Config.Splinter.StingOnBuildingAndUsing) Sting(player);
        };
        api.Event.DidUseBlock += (player, blockSel) =>
        {
            if (BadLuckModSystem.Config.Splinter.StingOnBuildingAndUsing) Sting(player);
        };
        api.Event.OnPlayerInteractEntity += OnInteractEntity;
        api.Event.RegisterEventBusListener(OnItemCrafted, filterByEventName: "onitemcrafted");
        api.Event.PlayerDeath += (player, damageSource) => Remove(player.Entity);
        api.Event.PlayerDisconnect += player =>
        {
            stingThrottle.Forget(player);
            toolThrottle.Forget(player);
        };
    }

    /// <summary>A new splinter; if one is already in there, its time is extended</summary>
    public static void Catch(IPlayer player)
    {
        SplinterConfig cfg = BadLuckModSystem.Config.Splinter;
        EntityPlayer entity = player?.Entity;
        if (!cfg.Enabled || entity == null || cfg.DurationMinutes <= 0) return;

        float remaining = entity.Attributes.GetFloat(RemainingKey);
        entity.Attributes.SetFloat(RemainingKey, remaining + (float)(cfg.DurationMinutes * 60));
        BadLuckModSystem.Chat(player, "badluck:splinter-caught");
    }

    public static void Remove(EntityPlayer entity)
    {
        entity?.Attributes.RemoveAttribute(RemainingKey);
    }

    static void OnTick(float dt)
    {
        foreach (IPlayer player in sapi.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer splayer || splayer.ConnectionState != EnumClientState.Playing) continue;
            EntityPlayer entity = player.Entity;
            if (entity == null) continue;

            float remaining = entity.Attributes.GetFloat(RemainingKey);
            if (remaining <= 0) continue;

            remaining -= dt;
            if (remaining <= 0) Remove(entity);
            else entity.Attributes.SetFloat(RemainingKey, remaining);
        }
    }

    static void OnInteractEntity(Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int mode, ref EnumHandling handling)
    {
        if (mode == (int)EnumInteractMode.Attack && BadLuckModSystem.Config.Splinter.StingOnAttacking)
        {
            Sting(byPlayer as IServerPlayer);
        }
    }

    /// <summary>Vanilla reports every take from the crafting output through this event</summary>
    static void OnItemCrafted(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (!BadLuckModSystem.Config.Splinter.StingOnCrafting || data is not ITreeAttribute tree) return;
        if ((tree.GetItemstack("itemstack")?.StackSize ?? 0) <= 0) return;

        var entity = sapi.World.GetEntityById(tree.GetLong("byentityid")) as EntityPlayer;
        Sting(entity?.Player as IServerPlayer);
    }

    /// <summary>A tool with a wooden handle was used (it lost durability)</summary>
    public static void OnToolAction(IServerPlayer player, CollectibleObject tool)
    {
        SplinterConfig cfg = BadLuckModSystem.Config.Splinter;
        if (!cfg.Enabled || player?.Entity == null || !IsStickTool(tool) || !BadLuckModSystem.Affects(player)) return;

        if (!toolThrottle.IsNewAction(player, player.Entity.World.ElapsedMilliseconds)) return;

        if (cfg.StingOnToolUse) Sting(player);
        if (BadLuckModSystem.Roll(player.Entity.World, cfg.CatchFromToolsPercent)) Catch(player);
    }

    /// <summary>Tools (with durability) whose crafting recipe contains a stick</summary>
    static bool IsStickTool(CollectibleObject tool)
    {
        if (tool?.Code == null) return false;
        if (stickTools == null)
        {
            stickTools = new HashSet<string>();
            Item stick = sapi.World.GetItem(new AssetLocation("game", "stick"));
            if (stick != null)
            {
                var stickStack = new ItemStack(stick);
                foreach (GridRecipe recipe in sapi.World.GridRecipes)
                {
                    CollectibleObject output = recipe.Output?.ResolvedItemStack?.Collectible;
                    if (output == null || (output.Tool == null && output.Durability <= 0) || recipe.ResolvedIngredients == null) continue;
                    foreach (CraftingRecipeIngredient ingredient in recipe.ResolvedIngredients)
                    {
                        if (ingredient != null && ingredient.SatisfiesAsIngredient(stickStack, false))
                        {
                            stickTools.Add(output.Code.ToString());
                            break;
                        }
                    }
                }
            }
        }
        return stickTools.Contains(tool.Code.ToString());
    }

    static void Sting(IServerPlayer player)
    {
        EntityPlayer entity = player?.Entity;
        if (entity == null || !entity.Alive || entity.Attributes.GetFloat(RemainingKey) <= 0) return;

        SplinterConfig cfg = BadLuckModSystem.Config.Splinter;
        if (!cfg.Enabled || !BadLuckModSystem.Affects(player)) return;

        if (!stingThrottle.IsNewAction(player, entity.World.ElapsedMilliseconds)) return;
        if (!BadLuckModSystem.Roll(entity.World, cfg.StingChancePercent)) return;

        // Never kills: down to 1 HP at most
        var health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health == null) return;
        float damage = Math.Min(cfg.Damage, health.Health - 1);
        if (damage <= 0) return;

        entity.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Injury,
            IgnoreInvFrames = true
        }, damage);
        BadLuckModSystem.Chat(player, "badluck:splinter-sting");
    }

}

/// <summary>A bandage or poultice (the vanilla "HealingItem" behavior) pulls the splinter out</summary>
[HarmonyPatch(typeof(CollectibleBehaviorHealingItem), nameof(CollectibleBehaviorHealingItem.OnHeldInteractStop))]
static class HealingRemovesSplinterPatch
{
    public class State
    {
        public EntityPlayer Target;
        public ItemStack Stack;
        public int Size;
    }

    static readonly MethodInfo GetTargetEntity = AccessTools.Method(typeof(CollectibleBehaviorHealingItem), "GetTargetEntity");

    static void Prefix(CollectibleBehaviorHealingItem __instance, ItemSlot slot, EntityAgent byEntity, EntitySelection entitySel, out State __state)
    {
        __state = null;
        if (byEntity?.World.Side != EnumAppSide.Server || slot?.Itemstack == null) return;
        if (!BadLuckModSystem.Config.Splinter.BandageRemoves) return;

        if (GetTargetEntity?.Invoke(__instance, [slot, byEntity, entitySel]) is not EntityPlayer target) return;
        __state = new State { Target = target, Stack = slot.Itemstack, Size = slot.StackSize };
    }

    static void Postfix(ItemSlot slot, State __state)
    {
        if (__state == null) return;

        // Consumed = applied
        bool applied = slot.Itemstack != __state.Stack || slot.StackSize < __state.Size;
        if (applied) Splinter.Remove(__state.Target);
    }
}

/// <summary>Every tool use that costs durability (mining, felling, hoeing, hammering, hitting ...)</summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem))]
static class ToolUseSplinterPatch
{
    // The tool can break in the process - so remember beforehand what it was
    static void Prefix(ItemSlot itemSlot, out CollectibleObject __state)
    {
        // This method runs on every bit of tool wear - including that of other mods
        __state = itemSlot?.Itemstack?.Collectible;
    }

    static void Postfix(IWorldAccessor world, Entity byEntity, ItemSlot itemSlot, CollectibleObject __state)
    {
        if (__state == null || world.Side != EnumAppSide.Server || byEntity is not EntityPlayer eplr) return;
        Splinter.OnToolAction(eplr.Player as IServerPlayer, __state);
        ToolFly.OnToolUsed(eplr, itemSlot);   // mechanic 12
    }
}
