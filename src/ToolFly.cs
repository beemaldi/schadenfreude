using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>The game is clearing a whole tree right now (ItemAxe.OnBlockBrokenWith)</summary>
[HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith))]
static class AxeFellingToolFlyPatch
{
    static void Prefix(IWorldAccessor world)
    {
        if (world.Side == EnumAppSide.Server) ToolFly.BeginFelling();
    }

    static void Postfix(IWorldAccessor world)
    {
        if (world.Side == EnumAppSide.Server) Guard.Run("tool flying after felling", ToolFly.EndFelling);
    }
}

/// <summary>
/// Mechanic 12: a tool or weapon slips out of your hand while you use it and flies off in a random
/// direction. It travels as the game own projectile ("thrownitem"): it hits whatever is in the way
/// and can be picked up again afterwards.
/// </summary>
public static class ToolFly
{
    /// <summary>How far in front of the player the tool starts its flight</summary>
    const double StartOffset = 0.25;

    /// <summary>Share of the speed that goes upwards - a flat arc, not a throw into the sky</summary>
    const double UpwardRatio = 1.65;
    static readonly AssetLocation ProjectileCode = new("game", "thrownitem");
    static readonly AssetLocation SlipSound = new("game", "sounds/player/throw");
    static readonly ActionThrottle throttle = new();

    public static void OnToolUsed(EntityPlayer eplr, ItemSlot slot)
    {
        ToolFlyConfig cfg = SchadenfreudeModSystem.Config.ToolFly;
        IPlayer player = eplr?.Player;
        if (!cfg.Enabled || player == null || !SchadenfreudeModSystem.Affects(player)) return;

        // Only what is held in the hand (armour loses durability too, but must not fly off)
        if (slot == null || slot != eplr.RightHandItemSlot || slot.Empty) return;
        CollectibleObject item = slot.Itemstack.Collectible;
        if (item.Tool == null && item.Durability <= 0) return;

        // Felling wears the axe down once per trunk block: roll only once per action
        if (!throttle.IsNewAction(player, eplr.World.ElapsedMilliseconds)) return;
        if (!SchadenfreudeModSystem.Roll(eplr.World, cfg.ChancePercent)) return;

        if (felling)
        {
            deferredPlayer = eplr;
            deferredSlot = slot;
            return;
        }
        Launch(eplr, slot, cfg);
    }

    public static void Forget(IPlayer player) => throttle.Forget(player);

    public static void Reset()
    {
        throttle.Clear();
        felling = false;
        deferredPlayer = null;
        deferredSlot = null;
    }

    // While the game clears a whole tree, the axe must not disappear from the hand: the game aborts
    // the felling as soon as the tool slot is empty. So only throw it once that is done.
    static bool felling;
    static EntityPlayer deferredPlayer;
    static ItemSlot deferredSlot;

    public static void BeginFelling()
    {
        felling = true;
        deferredPlayer = null;
        deferredSlot = null;
    }

    public static void EndFelling()
    {
        felling = false;
        EntityPlayer eplr = deferredPlayer;
        ItemSlot slot = deferredSlot;
        deferredPlayer = null;
        deferredSlot = null;

        if (eplr != null && slot?.Empty == false) Launch(eplr, slot, SchadenfreudeModSystem.Config.ToolFly);
    }

    /// <summary>Throw whatever is in the right hand right now (test command)</summary>
    public static bool ForceFly(EntityPlayer eplr)
    {
        ItemSlot slot = eplr?.RightHandItemSlot;
        if (slot == null || slot.Empty) return false;
        Launch(eplr, slot, SchadenfreudeModSystem.Config.ToolFly);
        return true;
    }

    static void Launch(EntityPlayer eplr, ItemSlot slot, ToolFlyConfig cfg)
    {
        IWorldAccessor world = eplr.World;
        EntityProperties type = world.GetEntityType(ProjectileCode);
        if (type == null || world.ClassRegistry.CreateEntity(type) is not EntityThrownItem projectile) return;

        ItemStack stack = slot.TakeOutWhole();
        slot.MarkDirty();

        projectile.FiredBy = eplr;
        projectile.ProjectileStack = stack;
        projectile.Damage = cfg.HitDamage;
        projectile.DamageType = EnumDamageType.BluntAttack;
        projectile.DropOnImpactChance = 1;         // never breaks on impact
        projectile.VerticalImpactBreakChance = 0;
        projectile.Collectible = true;

        // Random direction, short arc: between 55 % and 100 % of the allowed distance
        double angle = world.Rand.NextDouble() * GameMath.TWOPI;
        double wanted = Math.Max(0.3, cfg.MaxDistanceBlocks - StartOffset) * (0.55 + world.Rand.NextDouble() * 0.45);

        // Derived from the game trajectory: throw distance ~ 211 * speed^1.5
        double speed = Math.Pow(wanted / 211.0, 2.0 / 3.0);
        var motion = new Vec3d(Math.Sin(angle) * speed, speed * UpwardRatio, Math.Cos(angle) * speed);

        Vec3d start = eplr.Pos.XYZ.Add(eplr.LocalEyePos.X, eplr.LocalEyePos.Y - 0.3, eplr.LocalEyePos.Z)
            .Add(Math.Sin(angle) * StartOffset, 0, Math.Cos(angle) * StartOffset);
        projectile.Pos.SetPos(start);
        projectile.Pos.Dimension = eplr.Pos.Dimension;
        projectile.Pos.Motion.Set(motion);
        world.SpawnEntity(projectile);

        StupidSounds.PlayOr(world, StupidSounds.ToolFly, SlipSound, eplr, 24);

        if (eplr.Player is IServerPlayer splayer)
        {
            string name = stack.GetName();
            string text = Lang.GetL("en", "schadenfreude:toolfly-message", name);
            splayer.SendMessage(GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);
        }
    }
}
