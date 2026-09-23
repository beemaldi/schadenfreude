using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 2: instead of laying out a knapping surface, the player eats the stone.
///
/// Client and server both run OnHeldInteractStart. To make both sides agree, the server rolls the
/// next attempt in advance and puts the result into the player WatchedAttributes (which are synced
/// to the client).
/// </summary>
public static class EatStone
{
    const string NextAttemptKey = "schadenfreude-eatstone-next";
    static readonly AssetLocation EatSound = new("game", "sounds/player/eat_crunchy");

    public static void Register(ICoreServerAPI api)
    {
        api.Event.PlayerNowPlaying += player => Reroll(player.Entity);
        api.Event.PlayerSwitchGameMode += player => Reroll(player.Entity);
    }

    public static void RerollAll(ICoreServerAPI api)
    {
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            Reroll(player.Entity);
        }
    }

    static void Reroll(EntityPlayer entity)
    {
        if (entity == null) return;
        // Everything only the server knows (config, game mode) is baked into this single value
        EatStoneConfig cfg = SchadenfreudeModSystem.Config.EatStone;
        bool eat = cfg.Enabled && SchadenfreudeModSystem.Affects(entity.Player) && SchadenfreudeModSystem.Roll(entity.World, cfg.ChancePercent);
        entity.WatchedAttributes.SetBool(NextAttemptKey, eat);
    }

    /// <returns>false = skip the vanilla code (the stone was eaten)</returns>
    public static bool Prefix(Item item, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling, bool isFlint)
    {
        if (blockSel == null || slot?.Itemstack == null) return true;
        if (byEntity is not EntityPlayer eplr || !byEntity.Controls.ShiftKey) return true;

        IWorldAccessor world = byEntity.World;
        if (!WouldStartKnapping(item, slot, eplr, blockSel, isFlint)) return true;

        bool eat = eplr.WatchedAttributes.GetBool(NextAttemptKey);

        if (world.Side == EnumAppSide.Server)
        {
            Reroll(eplr);
            if (eat) ServerEat(slot, eplr);
        }
        else if (eat)
        {
            ClientEat(eplr);
        }

        if (!eat) return true;

        handling = EnumHandHandling.PreventDefault;
        byEntity.Attributes.SetInt("aimingCancel", 1);
        return false;
    }

    /// <summary>The same conditions under which vanilla lays out a knapping surface</summary>
    static bool WouldStartKnapping(Item item, ItemSlot slot, EntityPlayer eplr, BlockSelection blockSel, bool isFlint)
    {
        IWorldAccessor world = eplr.World;
        IBlockAccessor ba = world.BlockAccessor;
        Block block = ba.GetBlock(blockSel.Position);

        Block knappingBlock = world.GetBlock(new AssetLocation("knappingsurface"));
        if (knappingBlock == null) return false;

        if (world.Claims.TestAccess(eplr.Player, blockSel.Position, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted) return false;

        if (isFlint)
        {
            if (!block.CanAttachBlockAt(ba, knappingBlock, blockSel.Position, BlockFacing.UP)) return false;
            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            return ba.GetBlock(pos).IsReplacableBy(knappingBlock);
        }

        bool knappable = slot.Itemstack.Collectible.Attributes?["knappable"].AsBool(false) == true;
        return knappable
            && block.Code.PathStartsWith("loosestones")
            && block.FirstCodePart(1) == item.FirstCodePart(1);
    }

    static void ServerEat(ItemSlot slot, EntityPlayer eplr)
    {
        IWorldAccessor world = eplr.World;
        ItemStack eaten = slot.TakeOut(1);
        slot.MarkDirty();

        Vec3d mouth = eplr.Pos.AheadCopy(0.4f).XYZ.Add(eplr.LocalEyePos.X, eplr.LocalEyePos.Y - 0.4, eplr.LocalEyePos.Z);
        world.SpawnCubeParticles(mouth, eaten, 0.3f, 8, 0.5f);
        StupidSounds.PlayOr(world, StupidSounds.EatStone, EatSound, eplr, 16);

        eplr.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Injury
        }, SchadenfreudeModSystem.Config.EatStone.Damage);
        SchadenfreudeModSystem.Chat(eplr.Player, "schadenfreude:eatstone-message");
    }

    static void ClientEat(EntityPlayer eplr)
    {
        eplr.AnimManager?.StartAnimation("eat");
        eplr.World.RegisterCallback(_ => eplr.AnimManager?.StopAnimation("eat"), 1000);
    }
}

[HarmonyPatch(typeof(ItemStone), nameof(ItemStone.OnHeldInteractStart))]
static class ItemStoneInteractPatch
{
    static bool Prefix(ItemStone __instance, ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
    {
        var handled = handling;
        bool result = Guard.Run("eat stone", () => EatStone.Prefix(__instance, itemslot, byEntity, blockSel, ref handled, false));
        handling = handled;
        return result;
    }
}

[HarmonyPatch(typeof(ItemFlint), nameof(ItemFlint.OnHeldInteractStart))]
static class ItemFlintInteractPatch
{
    static bool Prefix(ItemFlint __instance, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
    {
        var handled = handling;
        bool result = Guard.Run("eat flint", () => EatStone.Prefix(__instance, slot, byEntity, blockSel, ref handled, true));
        handling = handled;
        return result;
    }
}
