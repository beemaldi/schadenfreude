using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BadLuck;

/// <summary>
/// Mechanic 9: when a broadleaf tree comes down, a snake can drop out of the crown onto the player,
/// bite (damage + poison) and slither away. It despawns after a while.
/// </summary>
public static class Snake
{
    static readonly AssetLocation SnakeCode = new(BadLuckModSystem.ModId, "snake");

    /// <summary>A trunk block that felling the whole tree hangs on (leaves do not count)</summary>
    public static bool IsTreeLog(Block block)
    {
        return block?.BlockMaterial == EnumBlockMaterial.Wood && block.Attributes?["treeFellingGroupCode"].Exists == true;
    }

    public static void OnTreeFelled(EntityPlayer eplr, Block log)
    {
        SnakeConfig cfg = BadLuckModSystem.Config.Snake;
        if (!cfg.Enabled || eplr?.Player == null || !BadLuckModSystem.Affects(eplr.Player)) return;

        // Broadleaf only: conifers are excluded, and so are blocks without a wood type (bamboo, tree fern ...)
        string wood = log.Variant?["wood"];
        if (string.IsNullOrEmpty(wood) || Array.IndexOf(cfg.ExcludedWoods ?? [], wood) >= 0) return;
        if (!BadLuckModSystem.Roll(eplr.World, cfg.ChancePercent)) return;

        Drop(eplr);
    }

    /// <summary>A snake drops onto the player</summary>
    public static void Drop(EntityPlayer eplr)
    {
        IWorldAccessor world = eplr.World;
        EntityProperties type = world.GetEntityType(SnakeCode);
        if (type == null) return;

        // Just above the head - right at head height if there is a ceiling
        double y = eplr.Pos.Y + eplr.LocalEyePos.Y + 1.2;
        var above = new BlockPos((int)Math.Floor(eplr.Pos.X), (int)Math.Floor(y), (int)Math.Floor(eplr.Pos.Z), eplr.Pos.Dimension);
        if (world.BlockAccessor.GetBlock(above).CollisionBoxes != null) y = eplr.Pos.Y + eplr.LocalEyePos.Y;

        Entity snake = world.ClassRegistry.CreateEntity(type);
        snake.Pos.SetPos(eplr.Pos.X, y, eplr.Pos.Z);
        snake.Pos.Dimension = eplr.Pos.Dimension;
        snake.Pos.Yaw = (float)(world.Rand.NextDouble() * GameMath.TWOPI);
        EntityBehaviorSnake.SetVictim(snake, eplr);
        world.SpawnEntity(snake);
        StupidSounds.Play(world, StupidSounds.Snake, eplr);
        BadLuckModSystem.Chat(eplr.Player, "badluck:snake-message");
    }
}

/// <summary>Bites once (as soon as it lands on the player or the ground) and despawns after a while</summary>
public class EntityBehaviorSnake : EntityBehavior
{
    const string VictimKey = "badluck-snake-victim";
    const string BittenKey = "badluck-snake-bitten";
    const float MaxFallSeconds = 1.5f;
    const float BiteRange = 3f;
    static readonly AssetLocation HissSound = new(BadLuckModSystem.ModId, "sounds/snake/hiss");

    float age;

    public EntityBehaviorSnake(Entity entity) : base(entity) { }

    public override string PropertyName() => "badluck.snake";

    public static void SetVictim(Entity snake, EntityPlayer victim)
    {
        snake.Attributes.SetLong(VictimKey, victim.EntityId);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

        age += deltaTime;
        if (age >= BadLuckModSystem.Config.Snake.DespawnSeconds)
        {
            entity.Die(EnumDespawnReason.Removed);
            return;
        }

        if (entity.Attributes.GetBool(BittenKey)) return;

        var victim = entity.World.GetEntityById(entity.Attributes.GetLong(VictimKey)) as EntityPlayer;
        if (victim == null || !victim.Alive)
        {
            entity.Attributes.SetBool(BittenKey, true);
            return;
        }

        // Landed on the head, reached the ground, or fell for too long: now or never
        double headY = victim.Pos.Y + victim.LocalEyePos.Y;
        bool onHead = Math.Abs(entity.Pos.Y - headY) < 0.6 && entity.Pos.HorDistanceTo(victim.Pos) < 0.8;
        if (!onHead && !entity.OnGround && age < MaxFallSeconds) return;

        entity.Attributes.SetBool(BittenKey, true);
        if (entity.Pos.DistanceTo(victim.Pos) <= BiteRange) Bite(victim);
    }

    void Bite(EntityPlayer victim)
    {
        SnakeConfig cfg = BadLuckModSystem.Config.Snake;
        IWorldAccessor world = entity.World;

        world.PlaySoundAt(HissSound, entity, null, true, 16);
        entity.AnimManager.StartAnimation(new AnimationMetaData { Code = "bite", Animation = "bite", AnimationSpeed = 1.5f }.Init());

        victim.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            CauseEntity = entity,
            Type = EnumDamageType.PiercingAttack
        }, cfg.BiteDamage);

        if (cfg.PoisonDamage > 0 && cfg.PoisonSeconds > 0)
        {
            int seconds = Math.Max(1, (int)Math.Round(cfg.PoisonSeconds));
            victim.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Poison,
                Duration = TimeSpan.FromSeconds(seconds),
                TicksPerDuration = seconds,
                DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison
            }, cfg.PoisonDamage);
        }
    }
}

/// <summary>The axe fells a whole tree</summary>
[HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith))]
static class TreeFelledSnakePatch
{
    static void Prefix(IWorldAccessor world, BlockSelection blockSel, out Block __state)
    {
        __state = null;
        if (world.Side != EnumAppSide.Server || blockSel == null) return;

        Block block = world.BlockAccessor.GetBlock(blockSel.Position);
        if (Snake.IsTreeLog(block)) __state = block;
    }

    static void Postfix(Entity byEntity, Block __state)
    {
        if (__state == null || byEntity is not EntityPlayer eplr) return;
        Guard.Run("snake from a felled tree", () => Snake.OnTreeFelled(eplr, __state));
    }
}
