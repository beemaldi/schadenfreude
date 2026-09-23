using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 13: look into the wind while holding a lit torch and the flame blows back at you - if the
/// clothes you wear are worn out on average, they catch fire (ordinary game fire).
/// </summary>
public static class TorchFire
{
    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.RegisterGameTickListener(OnTick, 1000);
    }

    static void OnTick(float dt)
    {
        TorchFireConfig cfg = SchadenfreudeModSystem.Config.TorchFire;
        if (!cfg.Enabled) return;

        foreach (IPlayer player in sapi.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer splayer || splayer.ConnectionState != EnumClientState.Playing) continue;
            EntityPlayer eplr = player.Entity;
            if (eplr == null || !eplr.Alive || eplr.IsOnFire || !SchadenfreudeModSystem.Affects(player)) continue;

            if (!HoldsLitTorch(eplr, cfg) || !FacesIntoWind(eplr, cfg)) continue;
            if (!ClothesWornOut(player, cfg)) continue;
            if (!SchadenfreudeModSystem.Roll(sapi.World, cfg.ChancePercentPerSecond)) continue;

            eplr.Ignite();
            SchadenfreudeModSystem.Chat(player, "schadenfreude:torchfire-message");
        }
    }

    static bool HoldsLitTorch(EntityPlayer eplr, TorchFireConfig cfg)
    {
        return SchadenfreudeModSystem.CodeMatches(cfg.TorchCodes, eplr.RightHandItemSlot?.Itemstack?.Collectible?.Code)
            || SchadenfreudeModSystem.CodeMatches(cfg.TorchCodes, eplr.LeftHandItemSlot?.Itemstack?.Collectible?.Code);
    }

    /// <summary>The wind blows into the player face (looking against the wind, within the angle)</summary>
    static bool FacesIntoWind(EntityPlayer eplr, TorchFireConfig cfg)
    {
        Vec3d wind = sapi.World.BlockAccessor.GetWindSpeedAt(eplr.Pos.XYZ);
        double windStrength = Math.Sqrt(wind.X * wind.X + wind.Z * wind.Z);
        if (windStrength < cfg.MinWindSpeed) return false;

        Vec3f view = eplr.Pos.GetViewVector();
        double viewLen = Math.Sqrt(view.X * view.X + view.Z * view.Z);
        if (viewLen < 0.001) return false;

        // Looking into the wind: the view direction points to where the wind comes from
        double dot = -(view.X * wind.X + view.Z * wind.Z) / (viewLen * windStrength);
        return dot >= Math.Cos(cfg.MaxAngleDegrees * GameMath.DEG2RAD);
    }

    /// <summary>Worn clothing (everything that warms - armour has no condition) below the limit on average</summary>
    static bool ClothesWornOut(IPlayer player, TorchFireConfig cfg)
    {
        IInventory character = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (character == null) return false;

        float sum = 0;
        int count = 0;
        foreach (ItemSlot slot in character)
        {
            if (slot.Empty) continue;
            var wearable = slot.Itemstack.Collectible.GetCollectibleBehavior<CollectibleBehaviorWearable>(true);
            if (wearable == null || wearable.GetMaxWarmth(slot) <= 0) continue;

            sum += slot.Itemstack.Attributes.GetFloat("condition", 1);
            count++;
        }

        // With no clothes on, nothing can catch fire
        return count > 0 && sum / count < cfg.MaxClothingConditionPercent / 100f;
    }
}
