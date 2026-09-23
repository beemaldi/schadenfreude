using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace BadLuck;

/// <summary>
/// Mechanic 3 (fire ants when sitting down) and mechanic 4 (hornets while running).
/// Both depend on the ground underfoot, so one shared pass over all players every 100 ms.
/// </summary>
public static class GroundHazards
{
    class PlayerState
    {
        public bool WasSitting;
        public bool HasBlock;
        public int BlockX, BlockZ;
    }

    static readonly AssetLocation AntSound = new("game", "sounds/creature/beesting");
    static readonly AssetLocation[] DirtSounds =
    [
        new("game", "sounds/block/dirt1"), new("game", "sounds/block/dirt2"),
        new("game", "sounds/block/dirt3"), new("game", "sounds/block/dirt4")
    ];

    static ICoreServerAPI sapi;
    static readonly Dictionary<string, PlayerState> states = new();

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        states.Clear();
        api.Event.RegisterGameTickListener(OnTick, 100);
        api.Event.PlayerDisconnect += player => states.Remove(player.PlayerUID);
    }

    static void OnTick(float dt)
    {
        foreach (IPlayer player in sapi.World.AllOnlinePlayers)
        {
            var splayer = player as IServerPlayer;
            EntityPlayer entity = player.Entity;
            if (splayer == null || splayer.ConnectionState != EnumClientState.Playing || entity == null || !entity.Alive) continue;

            if (!states.TryGetValue(player.PlayerUID, out PlayerState state))
            {
                states[player.PlayerUID] = state = new PlayerState();
            }

            bool affected = BadLuckModSystem.Affects(player) && entity.MountedOn == null;

            // Mechanic 3: the change from standing to sitting
            bool sitting = entity.Controls.FloorSitting;
            if (sitting && !state.WasSitting && affected)
            {
                FireAntsConfig ants = BadLuckModSystem.Config.FireAnts;
                if (ants.Enabled && StandsOnSoil(entity) && BadLuckModSystem.Roll(sapi.World, ants.ChancePercent))
                {
                    StartFireAnts(entity, ants);
                }
            }
            state.WasSitting = sitting;

            // Mechanic 4: a new block on the X/Z plane
            int bx = (int)Math.Floor(entity.Pos.X);
            int bz = (int)Math.Floor(entity.Pos.Z);
            if (state.HasBlock && (bx != state.BlockX || bz != state.BlockZ) && affected && !sitting)
            {
                HornetsConfig hornets = BadLuckModSystem.Config.Hornets;
                bool sprinting = !hornets.OnlyWhenSprinting || entity.Controls.Sprint;
                if (hornets.Enabled && sprinting && StandsOnSoil(entity) && BadLuckModSystem.Roll(sapi.World, hornets.ChancePercent))
                {
                    SpawnHornets(entity, hornets);
                }
            }
            state.BlockX = bx;
            state.BlockZ = bz;
            state.HasBlock = true;
        }
    }

    /// <summary>The block right under the feet is soil (grown over with grass counts too)</summary>
    static bool StandsOnSoil(EntityPlayer entity)
    {
        var pos = new BlockPos((int)Math.Floor(entity.Pos.X), (int)Math.Floor(entity.Pos.Y - 0.05), (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension);
        Block block = sapi.World.BlockAccessor.GetBlock(pos);
        return block.BlockMaterial == EnumBlockMaterial.Soil;
    }

    public static void ForceFireAnts(EntityPlayer entity) => StartFireAnts(entity, BadLuckModSystem.Config.FireAnts);
    public static void ForceHornets(EntityPlayer entity) => SpawnHornets(entity, BadLuckModSystem.Config.Hornets);

    static void StartFireAnts(EntityPlayer entity, FireAntsConfig cfg)
    {
        IWorldAccessor world = entity.World;
        int seconds = Math.Max(1, cfg.DurationSeconds);

        world.PlaySoundAt(DirtSounds[world.Rand.Next(DirtSounds.Length)], entity, null, true, 16);
        world.PlaySoundAt(AntSound, entity, null, true, 16);

        // Vanilla poison over time (same as poisonous food)
        entity.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Poison,
            Duration = TimeSpan.FromSeconds(seconds),
            TicksPerDuration = seconds,
            DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison
        }, cfg.DamagePerSecond * seconds);

        SpawnAntParticles(entity, seconds);
        BadLuckModSystem.Chat(entity.Player, "badluck:fireants-message");
    }

    static void SpawnAntParticles(EntityPlayer entity, int burstsLeft)
    {
        if (burstsLeft <= 0 || !entity.Alive) return;

        Vec3d feet = entity.Pos.XYZ;
        var props = new SimpleParticleProperties(
            12, 20,
            ColorUtil.ToRgba(255, 170, 45, 25),
            feet.AddCopy(-0.45, 0, -0.45),
            feet.AddCopy(0.45, 0.4, 0.45),
            new Vec3f(-0.4f, 0.1f, -0.4f),
            new Vec3f(0.4f, 0.6f, 0.4f),
            0.6f, 1f, 0.15f, 0.3f,
            EnumParticleModel.Cube
        );
        entity.World.SpawnParticles(props);

        entity.World.RegisterCallback(_ => SpawnAntParticles(entity, burstsLeft - 1), 1000);
    }

    static void SpawnHornets(EntityPlayer entity, HornetsConfig cfg)
    {
        IWorldAccessor world = entity.World;
        EntityProperties type = world.GetEntityType(new AssetLocation("game", "beemob"));
        if (type == null) return;

        int min = Math.Max(0, cfg.MinSwarms);
        int max = Math.Max(min, cfg.MaxSwarms);
        int count = min + world.Rand.Next(max - min + 1);

        // 1 block above the player; right at head height if there is a ceiling
        double y = entity.Pos.Y + entity.CollisionBox.Y2 + 1;
        var above = new BlockPos((int)Math.Floor(entity.Pos.X), (int)Math.Floor(y), (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension);
        if (world.BlockAccessor.GetBlock(above).CollisionBoxes != null) y = entity.Pos.Y + entity.LocalEyePos.Y;

        for (int i = 0; i < count; i++)
        {
            Entity swarm = world.ClassRegistry.CreateEntity(type);
            swarm.Pos.SetPos(
                entity.Pos.X + (world.Rand.NextDouble() - 0.5),
                y,
                entity.Pos.Z + (world.Rand.NextDouble() - 0.5));
            swarm.Pos.Dimension = entity.Pos.Dimension;
            swarm.Pos.Yaw = (float)(world.Rand.NextDouble() * GameMath.TWOPI);
            swarm.Attributes.SetString("origin", "badluck-hornets");
            world.SpawnEntity(swarm);
        }
        BadLuckModSystem.Chat(entity.Player, "badluck:hornets-message");
    }
}
