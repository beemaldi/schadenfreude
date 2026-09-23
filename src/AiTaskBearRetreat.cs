using System;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Safeguard for mechanic 8: once a bear has opened a door and then killed the player, it withdraws.
/// Otherwise it would still be standing in the house when the player respawns and kill them again.
/// Sits above seeking (1.6) and below fleeing (1.7) so nothing cuts in between.
/// </summary>
public class AiTaskBearRetreat : AiTaskBase
{
    public const string TaskCode = "schadenfreude-bearretreat";

    const float TaskPriority = 1.68f;

    Vec3d fleeFrom;
    long untilMs;
    bool done;

    public AiTaskBearRetreat(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig) { }

    /// <summary>Sends the bear away from this spot</summary>
    public static void Send(EntityAgent entity, Vec3d from, double seconds, float distance)
    {
        var taskAi = entity?.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi?.TaskManager == null || taskAi.PathTraverser == null) return;

        AiTaskBearRetreat task = taskAi.TaskManager.GetTask<AiTaskBearRetreat>();
        if (task == null)
        {
            var cfg = new JObject { ["code"] = TaskCode, ["priority"] = TaskPriority, ["mincooldown"] = 0, ["maxcooldown"] = 0 };
            task = new AiTaskBearRetreat(entity, new JsonObject(cfg), new JsonObject(new JObject()));
            taskAi.TaskManager.AddTask(task);
            task.AfterInitialize();
        }

        task.fleeFrom = from.Clone();
        task.untilMs = entity.World.ElapsedMilliseconds + (long)(seconds * 1000);
        task.distance = Math.Max(4f, distance);
        task.done = false;
    }

    float distance = 30f;

    public override bool ShouldExecute()
    {
        return fleeFrom != null && world.ElapsedMilliseconds < untilMs;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        done = false;

        CreatureMovement.Style style = CreatureMovement.Of(entity.Properties);
        if (!pathTraverser.Flee_Async(fleeFrom.Clone(), distance, style.MoveSpeed, OnArrived, OnArrived, OnArrived))
        {
            // No path: at least head stubbornly the other way
            Vec3d away = entity.Pos.XYZ.Add((entity.Pos.X - fleeFrom.X) * 2, 0, (entity.Pos.Z - fleeFrom.Z) * 2);
            pathTraverser.WalkTowards(away, style.MoveSpeed, 2f, OnArrived, OnArrived);
        }
    }

    public override bool ContinueExecute(float dt)
    {
        if (done || world.ElapsedMilliseconds >= untilMs) return false;

        // Far enough away is far enough
        return entity.Pos.XYZ.HorizontalSquareDistanceTo(fleeFrom.X, fleeFrom.Z) < distance * distance;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
        fleeFrom = null;
    }

    void OnArrived() => done = true;
}
