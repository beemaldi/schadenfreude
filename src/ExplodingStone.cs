using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 5: a thrown stone explodes on its first impact.
/// The damage to living things is worked out here (own radius, own maximum damage); the hole in the
/// landscape is left to the game explosion, with its own smaller radius.
/// </summary>
public static class ExplodingStone
{
    const string ImpactKey = "schadenfreude-impactrolled";
    static readonly AssetLocation ExplosionSound = new("game", "sounds/effect/smallexplosion");

    /// <summary>Sling projectile: this is always a stone</summary>
    public static void OnImpact(EntityThrownStone stone)
    {
        OnImpact(stone, stone.FiredBy);
    }

    /// <summary>
    /// Thrown by hand (hold right click): the game uses "thrownitem" for that - the same entity that
    /// carries a tool which slipped out of your hand. So the contents have to match.
    /// </summary>
    public static void OnImpact(EntityThrownItem thrown)
    {
        ItemStack stack = thrown.ProjectileStack;
        if (!SchadenfreudeModSystem.CodeMatches(SchadenfreudeModSystem.Config.ExplodingStone.ThrownCodes, stack?.Collectible?.Code)) return;
        OnImpact(thrown, thrown.FiredBy);
    }

    /// <summary>First impact (block or living thing): roll once</summary>
    static void OnImpact(Entity projectile, Entity firedBy)
    {
        IWorldAccessor world = projectile.World;
        if (world == null || world.Side != EnumAppSide.Server) return;
        if (projectile.Attributes.GetBool(ImpactKey)) return;
        projectile.Attributes.SetBool(ImpactKey, true);

        ExplodingStoneConfig cfg = SchadenfreudeModSystem.Config.ExplodingStone;
        if (!cfg.Enabled) return;
        if (firedBy is EntityPlayer thrower && !SchadenfreudeModSystem.Affects(thrower.Player)) return;
        if (!SchadenfreudeModSystem.Roll(world, cfg.ChancePercent)) return;

        Explode(world, projectile.Pos.XYZ, projectile, firedBy, cfg);
        if (projectile.Alive) projectile.Die();
        if (firedBy is EntityPlayer thrower2) SchadenfreudeModSystem.Chat(thrower2.Player, "schadenfreude:explodingstone-message");
    }

    /// <summary>Set off an explosion at this position (test command)</summary>
    public static void ForceExplosion(IWorldAccessor world, Vec3d center)
    {
        Explode(world, center, null, null, SchadenfreudeModSystem.Config.ExplodingStone);
    }

    static void Explode(IWorldAccessor world, Vec3d center, Entity projectile, Entity firedBy, ExplodingStoneConfig cfg)
    {
        world.PlaySoundAt(ExplosionSound, center.X, center.Y, center.Z, null, true, 48);
        SpawnExplosionParticles(world, center, cfg.Radius);

        // The hole in the landscape is the game own work (blocks, debris, drops).
        // It must not injure anyone on the way - the damage is handed out below.
        if (cfg.DestroyBlocks && cfg.BlockRadius > 0 && world is IServerWorldAccessor serverWorld)
        {
            string uid = (firedBy as EntityPlayer)?.PlayerUID;
            serverWorld.CreateExplosion(center.AsBlockPos, EnumBlastType.RockBlast, cfg.BlockRadius, 0, cfg.BlockDropChance, uid);
        }

        float radius = System.Math.Max(0.5f, cfg.Radius);
        Entity[] hit = world.GetEntitiesAround(center, radius, radius, e => e.Alive && e is EntityAgent);
        foreach (Entity target in hit)
        {
            Vec3d targetCenter = target.Pos.XYZ.Add(0, target.CollisionBox.Y2 / 2, 0);
            double distance = targetCenter.DistanceTo(center);
            float damage = cfg.MaxDamage * (float)(1 - distance / radius);
            if (damage <= 0) continue;

            target.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Explosion,
                Type = EnumDamageType.BluntAttack,
                SourcePos = center.Clone(),
                SourceEntity = projectile,
                CauseEntity = firedBy,
                KnockbackStrength = 2f
            }, damage);
        }
    }

    static void SpawnExplosionParticles(IWorldAccessor world, Vec3d center, float radius)
    {
        float spread = System.Math.Max(0.5f, radius * 0.3f);

        var flash = new SimpleParticleProperties(
            40, 60,
            ColorUtil.ToRgba(255, 255, 160, 50),
            center.AddCopy(-0.3, -0.3, -0.3),
            center.AddCopy(0.3, 0.3, 0.3),
            new Vec3f(-spread * 2, -spread, -spread * 2),
            new Vec3f(spread * 2, spread * 2, spread * 2),
            0.4f, 0.2f, 0.5f, 1.2f,
            EnumParticleModel.Quad
        );
        flash.VertexFlags = 255;
        world.SpawnParticles(flash);

        var smoke = new SimpleParticleProperties(
            25, 40,
            ColorUtil.ToRgba(180, 70, 70, 70),
            center.AddCopy(-0.5, -0.3, -0.5),
            center.AddCopy(0.5, 0.5, 0.5),
            new Vec3f(-spread, 0.1f, -spread),
            new Vec3f(spread, spread, spread),
            2.5f, -0.05f, 1.5f, 3f,
            EnumParticleModel.Quad
        );
        smoke.SelfPropelled = true;
        world.SpawnParticles(smoke);
    }
}

[HarmonyPatch(typeof(EntityThrownStone), nameof(EntityThrownStone.OnCollided))]
static class ThrownStoneCollidedPatch
{
    static void Postfix(EntityThrownStone __instance)
    {
        ExplodingStone.OnImpact(__instance);
    }
}

[HarmonyPatch(typeof(EntityThrownStone), nameof(EntityThrownStone.OnGameTick))]
static class ThrownStoneTickPatch
{
    // Hit on a living thing: vanilla calls Die() from OnGameTick
    static void Postfix(EntityThrownStone __instance)
    {
        if (!__instance.Alive) ExplodingStone.OnImpact(__instance);
    }
}

// Stones thrown by hand fly as "thrownitem"
[HarmonyPatch(typeof(EntityThrownItem), nameof(EntityThrownItem.OnCollided))]
static class ThrownItemCollidedPatch
{
    static void Postfix(EntityThrownItem __instance)
    {
        ExplodingStone.OnImpact(__instance);
    }
}

[HarmonyPatch(typeof(EntityThrownItem), nameof(EntityThrownItem.OnGameTick))]
static class ThrownItemTickPatch
{
    static void Postfix(EntityThrownItem __instance)
    {
        if (!__instance.Alive) ExplodingStone.OnImpact(__instance);
    }
}
