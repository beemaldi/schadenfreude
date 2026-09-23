using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 8: bears open doors - every closed door nearby, with or without a player around.
/// The bear walks over, looks at the player (if there is one), pushes against it with its paw and
/// moves on. The task ranks below seeking: with a player in its sights, the bear goes for the player
/// first. If the hunt gets blocked, the task briefly rises above it.
/// Attached to every bear (see Attach), not only once one is blocked.
/// </summary>
public class AiTaskOpenDoor : AiTaskBase
{
    public const string TaskCode = "schadenfreude-opendoor";

    // Above wandering/idling (which cancels at 1.35), below hunting a player (1.45)
    const float IdlePriority = 1.38f;

    // If the bear gets stuck while hunting, the door comes first: above seeking (1.6), below attacking (1.65)
    const float BlockedPriority = 1.62f;

    const string PawAnimation = "opendoor";

    const long GoalValidMs = 15000;
    const long CheckIntervalMs = 2000;
    const long RetryCooldownMs = 3000;
    const long FailedDoorMs = 60000;
    const long WalkTimeoutMs = 20000;
    const long PawAnimationMs = 1333;
    const long OpenAtMs = 700;
    const long AfterPawMs = 300;
    const float TurnRadPerSec = 4f;

    /// <summary>How the bear wants to stand: nose against the door</summary>
    const double CloseRange = 1.7;

    /// <summary>Only when it really cannot get any closer (a 1-block-wide opening) does this do too</summary>
    const double OpenRange = 3.2;

    /// <summary>The path may end this many blocks short of the target: a bear does not fit through a 1-block gap</summary>
    const int PathTolerance = 2;
    const float LookForPlayerRange = 16f;

    static readonly MethodInfo BaseDoorOpen = AccessTools.Method(typeof(BlockBaseDoor), "Open");
    static readonly MethodInfo BaseDoorOpenConnected = AccessTools.Method(typeof(BlockBaseDoor), "TryOpenConnectedDoor");
    static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");

    enum Phase { Walk, Stare, Paw, Done }

    // Only set when the bear cannot get through while hunting
    Vec3d goalPos;
    Entity blockedTarget;
    Action resume;
    long goalSetMs;

    long nextCheckMs;
    readonly Dictionary<BlockPos, long> failedDoors = new();

    // The attempt currently under way
    BlockPos doorPos;
    Vec3d standPos;
    Entity lookTarget;
    Phase phase;
    long phaseStartMs;
    bool doorOpened;
    bool walkFailed;
    bool triedStraight;
    AnimationMetaData runAnim;

    public AiTaskOpenDoor(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig) { }

    public static bool IsOpener(Entity entity)
    {
        BearDoorsConfig cfg = SchadenfreudeModSystem.Config.BearDoors;
        if (!cfg.Enabled || entity?.Code == null) return false;
        foreach (string pattern in cfg.OpenerCodes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pattern) && WildcardUtil.Match(new AssetLocation(pattern.Trim()), entity.Code)) return true;
        }
        return false;
    }

    /// <summary>When this bear last opened a door (used by the retreat)</summary>
    const string OpenedDoorKey = "schadenfreude-opened-door-ms";

    /// <summary>Every bear gets the task when it appears, so it opens doors with no player around too</summary>
    public static void Register(ICoreServerAPI api)
    {
        AiTaskRegistry.Register<AiTaskOpenDoor>(TaskCode);
        AiTaskRegistry.Register<AiTaskBearRetreat>(AiTaskBearRetreat.TaskCode);
        api.Event.OnEntitySpawn += Attach;
        api.Event.OnEntityLoaded += Attach;
        api.Event.PlayerDeath += OnPlayerDeath;
    }

    /// <summary>
    /// If the bear has just come through a door and killed the player, it has to clear off -
    /// otherwise it waits in the house for the next attempt.
    /// </summary>
    static void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        BearDoorsConfig cfg = SchadenfreudeModSystem.Config.BearDoors;
        if (!cfg.Enabled || cfg.RetreatSeconds <= 0 || player?.Entity == null) return;

        Entity killer = damageSource?.SourceEntity ?? damageSource?.CauseEntity;
        if (killer is not EntityAgent bear || !bear.Alive || !IsOpener(bear)) return;

        // Only bears that really opened a door
        // A value ahead of the clock was saved during an earlier server run - the clock restarts at zero
        double openedMs = bear.Attributes.GetDouble(OpenedDoorKey);
        double sinceMs = bear.World.ElapsedMilliseconds - openedMs;
        if (openedMs <= 0 || sinceMs < 0 || sinceMs > cfg.RetreatAfterDoorSeconds * 1000) return;

        bear.Attributes.SetDouble(OpenedDoorKey, 0);
        AiTaskBearRetreat.Send(bear, player.Entity.Pos.XYZ, cfg.RetreatSeconds, cfg.RetreatBlocks);
    }

    static void Attach(Entity entity)
    {
        if (entity is EntityAgent agent && IsOpener(entity)) GetOrCreate(agent);
    }

    static AiTaskOpenDoor GetOrCreate(EntityAgent entity)
    {
        var taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null || taskAi.PathTraverser == null) return null;

        AiTaskOpenDoor task = taskAi.TaskManager.GetTask<AiTaskOpenDoor>();
        if (task != null) return task;

        var cfg = new JObject { ["code"] = TaskCode, ["priority"] = IdlePriority, ["mincooldown"] = 0, ["maxcooldown"] = 0 };
        task = new AiTaskOpenDoor(entity, new JsonObject(cfg), new JsonObject(new JObject()));
        taskAi.TaskManager.AddTask(task);
        task.AfterInitialize();
        return task;
    }

    /// <summary>The bear cannot reach its target: the door now comes before the hunt</summary>
    public static void ReportBlocked(EntityAgent entity, Vec3d goal, Entity lookTarget, Action resume)
    {
        if (entity == null || goal == null || !entity.Alive || !IsOpener(entity)) return;

        AiTaskOpenDoor task = GetOrCreate(entity);
        if (task == null) return;

        task.goalPos = goal.Clone();
        task.blockedTarget = lookTarget;
        task.resume = resume;
        task.goalSetMs = entity.World.ElapsedMilliseconds;
        task.nextCheckMs = 0;
        task.Priority = BlockedPriority;
    }

    public override bool ShouldExecute()
    {
        if (!IsOpener(entity)) return false;

        long now = world.ElapsedMilliseconds;
        if (goalPos != null && (now - goalSetMs > GoalValidMs || (blockedTarget != null && !blockedTarget.Alive)))
        {
            ClearBlocked();
        }
        if (now < nextCheckMs) return false;
        nextCheckMs = now + CheckIntervalMs;

        return FindDoor(out doorPos, out standPos);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        doorOpened = false;
        walkFailed = false;
        triedStraight = false;
        lookTarget = blockedTarget ?? NearestPlayer();
        SetPhase(Phase.Walk);

        if (DistanceToDoor() <= CloseRange)
        {
            BeginStare();
            return;
        }

        CreatureMovement.Style style = CreatureMovement.Of(entity.Properties);
        if (!string.IsNullOrEmpty(style.Animation))
        {
            runAnim = new AnimationMetaData { Code = style.Animation, Animation = style.Animation, AnimationSpeed = style.AnimationSpeed }.Init();
            entity.AnimManager.StartAnimation(runAnim);
        }

        // The path may end two blocks short - a bear does not fit into a 1-block-wide gap
        pathTraverser.NavigateTo_Async(standPos.Clone(), style.MoveSpeed, 0.5f, OnWalkArrived, OnWalkFailed, OnWalkFailed, 999, PathTolerance);
    }

    public override bool ContinueExecute(float dt)
    {
        long now = world.ElapsedMilliseconds;

        switch (phase)
        {
            case Phase.Walk:
                // Arrived right in front of the door
                if (DistanceToDoor() <= CloseRange)
                {
                    BeginStare();
                    return true;
                }
                if (walkFailed || now - phaseStartMs > WalkTimeoutMs)
                {
                    failedDoors[doorPos] = now;
                    return false;
                }
                return true;

            case Phase.Stare:
                FaceTarget(dt);
                if (now - phaseStartMs >= SchadenfreudeModSystem.Config.BearDoors.StareSeconds * 1000) BeginPaw();
                return true;

            case Phase.Paw:
                FaceTarget(dt);
                if (!doorOpened && now - phaseStartMs >= OpenAtMs)
                {
                    doorOpened = true;
                    OpenDoor();
                }
                if (now - phaseStartMs >= PawAnimationMs + AfterPawMs)
                {
                    SetPhase(Phase.Done);
                    return false;
                }
                return true;

            default:
                return false;
        }
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
        if (runAnim != null) entity.AnimManager.StopAnimation(runAnim.Code);
        entity.AnimManager.StopAnimation(PawAnimation);
        nextCheckMs = world.ElapsedMilliseconds + RetryCooldownMs;
        lookTarget = null;

        if (doorOpened)
        {
            // Back after the player - but only after this pass of the task manager
            Action next = resume;
            ClearBlocked();
            if (next != null) world.RegisterCallback(_ => next(), 0);
        }
    }

    void ClearBlocked()
    {
        goalPos = null;
        blockedTarget = null;
        resume = null;
        Priority = IdlePriority;
    }

    double DistanceToDoor()
    {
        return doorPos == null ? double.MaxValue : entity.Pos.XYZ.DistanceTo(doorPos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    Entity NearestPlayer()
    {
        return world.GetNearestEntity(entity.Pos.XYZ, LookForPlayerRange, LookForPlayerRange,
            e => e is EntityPlayer player && player.Alive && SchadenfreudeModSystem.Affects(player.Player));
    }

    void SetPhase(Phase next)
    {
        phase = next;
        phaseStartMs = world.ElapsedMilliseconds;
    }

    void BeginStare()
    {
        if (phase != Phase.Walk) return;
        pathTraverser.Stop();
        if (runAnim != null) entity.AnimManager.StopAnimation(runAnim.Code);
        SetPhase(Phase.Stare);
    }

    void BeginPaw()
    {
        SetPhase(Phase.Paw);

        // First the one-liner, then the door opens
        StupidSounds.Play(world, StupidSounds.BearDoor, doorPos);
        entity.AnimManager.StartAnimation(new AnimationMetaData
        {
            Code = PawAnimation,
            Animation = PawAnimation,
            AnimationSpeed = 1f,
            EaseInSpeed = 6f,
            EaseOutSpeed = 6f
        }.Init());
    }

    /// <summary>End of the path: only start if it really is standing at the door</summary>
    void OnWalkArrived()
    {
        if (phase != Phase.Walk) return;
        if (DistanceToDoor() <= CloseRange) BeginStare();
        else OnWalkFailed();
    }

    /// <summary>
    /// Cannot get close: head stubbornly straight for the door once. If that fails too, it opens the
    /// door at arm length - a bear simply does not step into a 1-block-wide doorway.
    /// </summary>
    void OnWalkFailed()
    {
        if (phase != Phase.Walk) return;

        if (!triedStraight)
        {
            triedStraight = true;
            CreatureMovement.Style style = CreatureMovement.Of(entity.Properties);
            pathTraverser.WalkTowards(doorPos.ToVec3d().Add(0.5, 0.5, 0.5), style.MoveSpeed, 0.5f, OnWalkArrived, OnWalkFailed);
            return;
        }

        if (DistanceToDoor() <= OpenRange) BeginStare();
        else walkFailed = true;
    }

    /// <summary>Look at the player, otherwise at the door</summary>
    void FaceTarget(float dt)
    {
        Vec3d target = lookTarget != null && lookTarget.Alive ? lookTarget.Pos.XYZ : doorPos?.ToVec3d().Add(0.5, 0.5, 0.5);
        if (target == null) return;

        float desiredYaw = (float)Math.Atan2(target.X - entity.Pos.X, target.Z - entity.Pos.Z);
        float yawDist = GameMath.AngleRadDistance(entity.Pos.Yaw, desiredYaw);
        entity.Pos.Yaw += GameMath.Clamp(yawDist, -TurnRadPerSec * dt, TurnRadPerSec * dt);
        entity.Pos.Yaw %= GameMath.TWOPI;
    }

    /// <summary>The nearest closed door around the bear that it can also stand in front of</summary>
    bool FindDoor(out BlockPos bestDoor, out Vec3d bestStand)
    {
        bestDoor = null;
        bestStand = null;

        IBlockAccessor ba = world.BlockAccessor;
        long now = world.ElapsedMilliseconds;
        int radius = (int)Math.Max(1, SchadenfreudeModSystem.Config.BearDoors.SearchRadius);
        var center = new BlockPos((int)Math.Floor(entity.Pos.X), (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension);
        double bestScore = double.MaxValue;

        var pos = new BlockPos(entity.Pos.Dimension);
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    pos.Set(center.X + dx, center.Y + dy, center.Z + dz);
                    Block block = ba.GetBlock(pos);
                    if (!IsDoor(block) || !IsClosed(ba, pos, block)) continue;
                    if (failedDoors.TryGetValue(pos, out long failedMs) && now - failedMs < FailedDoorMs) continue;

                    Vec3d doorCenter = pos.ToVec3d().Add(0.5, 0.5, 0.5);
                    double score = doorCenter.DistanceTo(entity.Pos.XYZ);

                    // When stuck, the door closest to the target is the one that counts
                    if (goalPos != null) score += doorCenter.DistanceTo(goalPos);
                    if (score >= bestScore) continue;
                    if (!TryStandPos(ba, pos, out Vec3d stand)) continue;

                    bestScore = score;
                    bestDoor = pos.Copy();
                    bestStand = stand;
                }
            }
        }
        return bestDoor != null;
    }

    static bool IsDoor(Block block)
    {
        return block is BlockBaseDoor || block.HasBehavior<BlockBehaviorDoor>() || block.HasBehavior<BlockBehaviorTrapDoor>();
    }

    static bool IsClosed(IBlockAccessor ba, BlockPos pos, Block block)
    {
        if (block is BlockBaseDoor baseDoor) return !baseDoor.IsOpened();

        BlockEntity be = ba.GetBlockEntity(pos);
        if (be?.GetBehavior<BEBehaviorDoor>() is BEBehaviorDoor door) return !door.Opened;
        if (be?.GetBehavior<BEBehaviorTrapDoor>() is BEBehaviorTrapDoor trapdoor) return !trapdoor.Opened;
        return false;
    }

    /// <summary>A free spot right next to the door, on the bear side, with ground underneath</summary>
    bool TryStandPos(IBlockAccessor ba, BlockPos doorPos, out Vec3d stand)
    {
        stand = null;
        double bestDist = double.MaxValue;

        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            BlockPos cell = doorPos.AddCopy(face);
            if (SchadenfreudeModSystem.IsSolid(ba, cell) || SchadenfreudeModSystem.IsSolid(ba, cell.UpCopy()) || !SchadenfreudeModSystem.IsSolid(ba, cell.DownCopy())) continue;

            Vec3d candidate = cell.ToVec3d().Add(0.5, 0, 0.5);
            double dist = candidate.DistanceTo(entity.Pos.XYZ);
            if (dist < bestDist)
            {
                bestDist = dist;
                stand = candidate;
            }
        }
        return stand != null;
    }

    void OpenDoor()
    {
        IBlockAccessor ba = world.BlockAccessor;
        Block block = ba.GetBlock(doorPos);
        if (!IsDoor(block) || !IsClosed(ba, doorPos, block)) return;

        entity.Attributes.SetDouble(OpenedDoorKey, world.ElapsedMilliseconds);

        // Fragile doors (e.g. the rough wooden one) get torn straight out by the bear
        IPlayer watcher = (lookTarget as EntityPlayer)?.Player;
        BearDoorsConfig cfg = SchadenfreudeModSystem.Config.BearDoors;
        bool fragile = block.Attributes?["breakOnTriggerChance"].AsFloat(0) > 0
            || SchadenfreudeModSystem.CodeMatches(cfg.TearOutCodes, block.Code);
        if (fragile)
        {
            ba.BreakBlock(doorPos, null);
            world.PlaySoundAt(BreakSound, doorPos, 0, null);
            SchadenfreudeModSystem.Chat(watcher, "schadenfreude:bear-door-torn");
            return;
        }

        // A bear can pull a door off its hinges too (mechanic 11)
        if (DoorHinges.TryFlyOff(world, doorPos, entity.Pos.XYZ, watcher, byBear: true, opening: true)) return;
        SchadenfreudeModSystem.Chat(watcher, "schadenfreude:bear-door-message");

        if (block is BlockBaseDoor baseDoor)
        {
            // Fence gates and legacy doors (locked ones too: the bear does not ask)
            BaseDoorOpen?.Invoke(baseDoor, [world, null, doorPos]);
            string sound = block.Attributes?["triggerSound"].AsString("sounds/block/door") ?? "sounds/block/door";
            world.PlaySoundAt(AssetLocation.Create(sound, block.Code.Domain), doorPos, 0, null);
            if (block.FirstCodePart() != "roughhewnfencegate") BaseDoorOpenConnected?.Invoke(baseDoor, [world, null, doorPos]);
            return;
        }

        BlockEntity be = ba.GetBlockEntity(doorPos);
        be?.GetBehavior<BEBehaviorDoor>()?.ToggleDoorState(null, true);
        be?.GetBehavior<BEBehaviorTrapDoor>()?.ToggleDoorState(null, true);
    }
}

/// <summary>A bear hunt for a player finds no path: move the door task up</summary>
[HarmonyPatch(typeof(AiTaskSeekEntity), "OnSeekUnable")]
static class SeekUnableOpenDoorPatch
{
    static void Prefix(AiTaskSeekEntity __instance)
    {
        Guard.Run("bear blocked while hunting", () => ReportBlocked(__instance));
    }

    static void ReportBlocked(AiTaskSeekEntity __instance)
    {
        if (__instance.TargetEntity is not EntityPlayer player || !player.Alive) return;
        if (!SchadenfreudeModSystem.Affects(player.Player)) return;

        EntityAgent bear = __instance.entity;
        if (!AiTaskOpenDoor.IsOpener(bear)) return;

        AiTaskManager taskManager = bear.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        AiTaskOpenDoor.ReportBlocked(bear, player.Pos.XYZ, player, () =>
        {
            if (player.Alive && bear.Alive) taskManager?.ExecuteTask(__instance, __instance.Slot);
        });
    }
}
