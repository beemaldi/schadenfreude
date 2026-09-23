using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 19: a thrown stone comes back. Shortly after the throw it turns around, homes in on the
/// thrower and bonks them on the head. Afterwards it lies there to be picked up.
/// Runs along in the projectile tick - the same two classes as the exploding stone.
/// </summary>
public static class Boomerang
{
    const string RolledKey = "schadenfreude-boomerang-rolled";
    const string ReturnAtKey = "schadenfreude-boomerang-at";

    /// <summary>From here on the stone counts as having arrived</summary>
    const double HitRange = 1.2;
    const double ReturnSpeed = 0.3;

    static readonly AssetLocation ThudSound = new("game", "sounds/thud");

    /// <summary>Sling projectile: always a stone</summary>
    public static void OnTick(EntityThrownStone stone)
    {
        OnTick(stone, stone.FiredBy, stone.ProjectileStack, true);
    }

    /// <summary>Thrown by hand: only if there really is a stone inside</summary>
    public static void OnTick(EntityThrownItem thrown)
    {
        // The pattern match is only worth it on the first tick - after that the roll has long been made
        ItemStack stack = thrown.ProjectileStack;
        bool isStone = thrown.Attributes.GetBool(RolledKey)
            || SchadenfreudeModSystem.CodeMatches(SchadenfreudeModSystem.Config.ExplodingStone.ThrownCodes, stack?.Collectible?.Code);
        OnTick(thrown, thrown.FiredBy, stack, isStone);
    }

    static void OnTick(Entity projectile, Entity firedBy, ItemStack stack, bool isStone)
    {
        IWorldAccessor world = projectile.World;
        if (world == null || world.Side != EnumAppSide.Server || !projectile.Alive) return;

        BoomerangConfig cfg = SchadenfreudeModSystem.Config.Boomerang;
        long now = world.ElapsedMilliseconds;

        // Roll once per stone, on the first tick
        if (!projectile.Attributes.GetBool(RolledKey))
        {
            projectile.Attributes.SetBool(RolledKey, true);
            if (!cfg.Enabled || !isStone) return;
            if (firedBy is not EntityPlayer thrower || !SchadenfreudeModSystem.Affects(thrower.Player)) return;
            if (!SchadenfreudeModSystem.Roll(world, cfg.ChancePercent)) return;

            projectile.Attributes.SetDouble(ReturnAtKey, now + cfg.ReturnAfterSeconds * 1000);
            return;
        }

        double returnAt = projectile.Attributes.GetDouble(ReturnAtKey);
        if (returnAt <= 0 || now < returnAt) return;
        if (firedBy is not EntityPlayer target || !target.Alive)
        {
            projectile.Attributes.SetDouble(ReturnAtKey, 0);
            return;
        }

        // Home in on the chest of the thrower
        Vec3d chest = target.Pos.XYZ.Add(0, target.LocalEyePos.Y * 0.7, 0);
        Vec3d delta = chest.SubCopy(projectile.Pos.XYZ);
        double distance = delta.Length();

        if (distance <= HitRange)
        {
            Hit(world, projectile, target, stack, cfg);
            return;
        }

        projectile.Pos.Motion.Set(delta.X / distance * ReturnSpeed, delta.Y / distance * ReturnSpeed, delta.Z / distance * ReturnSpeed);
    }

    static void Hit(IWorldAccessor world, Entity projectile, EntityPlayer target, ItemStack stack, BoomerangConfig cfg)
    {
        projectile.Attributes.SetDouble(ReturnAtKey, 0);

        target.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            Type = EnumDamageType.BluntAttack,
            SourceEntity = projectile,
            CauseEntity = target,
            SourcePos = projectile.Pos.XYZ.Clone()
        }, cfg.Damage);

        StupidSounds.PlayOr(world, StupidSounds.Bonk, ThudSound, target, 24);
        SchadenfreudeModSystem.Chat(target.Player, "schadenfreude:boomerang-message");

        // The stone drops to the ground instead of vanishing into thin air
        if (stack != null) world.SpawnItemEntity(stack.Clone(), projectile.Pos.XYZ.Clone());
        projectile.Die(EnumDespawnReason.Removed);
    }
}

[HarmonyPatch(typeof(EntityThrownStone), nameof(EntityThrownStone.OnGameTick))]
static class ThrownStoneBoomerangPatch
{
    static void Postfix(EntityThrownStone __instance) => Boomerang.OnTick(__instance);
}

[HarmonyPatch(typeof(EntityThrownItem), nameof(EntityThrownItem.OnGameTick))]
static class ThrownItemBoomerangPatch
{
    static void Postfix(EntityThrownItem __instance) => Boomerang.OnTick(__instance);
}
