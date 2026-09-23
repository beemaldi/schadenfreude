using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BadLuck;

/// <summary>
/// AI task for aggressive creatures: walk to the spot of a fart and sniff around there.
/// Ranks above wandering but below seeking/attacking - if the creature spots the player on the way,
/// its normal hunt takes over.
/// Attached to the creature at runtime, not through JSON.
/// </summary>
public class AiTaskInvestigateSmell : AiTaskBase
{
    public const string TaskCode = "badluck-investigatesmell";

    const long SniffMs = 6000;
    const long GiveUpMs = 60000;

    readonly float moveSpeed;
    Vec3d target;
    long targetSetMs;
    long arrivedMs;
    bool running;
    bool finished;
    bool triedStraight;

    public AiTaskInvestigateSmell(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.03f);
    }

    /// <summary>Sends the creature to the source of the smell (attaches the task on first use)</summary>
    public static void Send(EntityAgent entity, Vec3d spot, float priority, float moveSpeed, string animation, float animationSpeed)
    {
        var taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi == null) return;

        AiTaskInvestigateSmell task = taskAi.TaskManager.GetTask<AiTaskInvestigateSmell>();
        if (task == null)
        {
            var cfg = new JObject
            {
                ["code"] = TaskCode,
                ["priority"] = priority,
                ["movespeed"] = moveSpeed,
                ["mincooldown"] = 0,
                ["maxcooldown"] = 0
            };
            if (!string.IsNullOrEmpty(animation))
            {
                cfg["animation"] = animation;
                cfg["animationSpeed"] = animationSpeed;
            }
            task = new AiTaskInvestigateSmell(entity, new JsonObject(cfg), new JsonObject(new JObject()));
            taskAi.TaskManager.AddTask(task);
            task.AfterInitialize();
        }

        task.target = spot.Clone();
        task.targetSetMs = entity.World.ElapsedMilliseconds;
        if (task.running) task.Navigate();
    }

    /// <summary>Carry on to the same spot once a door has been opened</summary>
    public static void Resume(EntityAgent entity, Vec3d spot)
    {
        AiTaskInvestigateSmell task = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.GetTask<AiTaskInvestigateSmell>();
        if (task == null) return;
        task.target = spot.Clone();
        task.targetSetMs = entity.World.ElapsedMilliseconds;
    }

    /// <summary>Blocked: maybe the creature (a bear) can open a door</summary>
    void ReportBlocked()
    {
        if (target == null || !BadLuckModSystem.Config.BearDoors.WhileLured) return;

        Vec3d spot = target.Clone();
        Entity player = entity.World.GetNearestEntity(spot, 16, 8, e => e is EntityPlayer ep && ep.Alive);
        AiTaskOpenDoor.ReportBlocked(entity, spot, player, () => Resume(entity, spot));
    }

    public override bool ShouldExecute()
    {
        return target != null && entity.World.ElapsedMilliseconds - targetSetMs < GiveUpMs;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        running = true;
        Navigate();
    }

    void Navigate()
    {
        arrivedMs = 0;
        finished = false;
        triedStraight = false;
        pathTraverser.NavigateTo_Async(target.Clone(), moveSpeed, 1.5f, OnGoalReached, OnStuck, OnNoPath);
    }

    public override bool ContinueExecute(float dt)
    {
        if (finished || target == null) return false;

        long now = entity.World.ElapsedMilliseconds;
        if (now - targetSetMs > GiveUpMs) return false;
        if (arrivedMs > 0) return now - arrivedMs < SniffMs;
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
        running = false;
        target = null;
    }

    void OnGoalReached()
    {
        // Arrived: stand still and sniff
        arrivedMs = entity.World.ElapsedMilliseconds;
        pathTraverser.Stop();
        if (animMeta != null) entity.AnimManager.StopAnimation(animMeta.Code);
    }

    void OnStuck() => TryStraightLine();

    void OnNoPath() => TryStraightLine();

    /// <summary>No path or stuck: try straight ahead once, then give up</summary>
    void TryStraightLine()
    {
        if (triedStraight || target == null)
        {
            finished = true;
            ReportBlocked();
            return;
        }
        triedStraight = true;
        pathTraverser.WalkTowards(target.Clone(), moveSpeed, 1.5f, OnGoalReached, OnStuck);
    }
}
