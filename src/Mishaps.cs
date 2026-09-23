using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 14 (cliff) and 15 (stumbling). The server rolls and writes the result into the player
/// WatchedAttributes; the client (ClientMishaps) acts on it, because in Vintage Story it is the
/// client that computes player movement.
/// </summary>
public static class Mishaps
{
    public const string TripCounterKey = "schadenfreude-trip-n";
    public const string TripSecondsKey = "schadenfreude-trip-sec";

    const string StatKey = "schadenfreude-trip";

    /// <summary>The game animation for lying down (sleeping in a bed uses the same one)</summary>
    const string LieAnimation = "sleep";
    const double ShoveStrength = 0.14;

    /// <summary>How far in front of the feet the drop is probed for</summary>
    const double ProbeDistance = 0.55;
    const double ShoveDistance = 0.8;

    static ICoreServerAPI sapi;
    static readonly Dictionary<string, long> tripUntilMs = new();

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        tripUntilMs.Clear();
        api.Event.RegisterGameTickListener(OnTick, 1000);
        api.Event.PlayerDisconnect += player => EndTrip(player.Entity, player.PlayerUID);
        api.Event.PlayerDeath += (player, damageSource) => EndTrip(player.Entity, player.PlayerUID);
    }

    static void OnTick(float dt)
    {
        long now = sapi.World.ElapsedMilliseconds;

        foreach (IPlayer player in sapi.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer splayer || splayer.ConnectionState != EnumClientState.Playing) continue;
            EntityPlayer eplr = player.Entity;
            if (eplr == null || !eplr.Alive) continue;

            if (tripUntilMs.TryGetValue(player.PlayerUID, out long until))
            {
                if (now >= until) EndTrip(eplr, player.PlayerUID);
                continue;
            }

            if (!SchadenfreudeModSystem.Affects(player) || eplr.MountedOn != null || eplr.Controls.IsFlying) continue;

            TryCliffSlip(eplr);
            TryStumble(eplr);
        }
    }

    // --- Cliff ----------------------------------------------------------------------------------

    static void TryCliffSlip(EntityPlayer eplr)
    {
        CliffConfig cfg = SchadenfreudeModSystem.Config.Cliff;
        if (!cfg.Enabled || !eplr.Controls.Sneak || !StandsOnSomething(eplr)) return;
        if (!FindEdge(eplr, cfg.MinDropBlocks, out BlockFacing edge)) return;
        if (!SchadenfreudeModSystem.Roll(sapi.World, cfg.ChancePercentPerSecond)) return;

        Slip(eplr, edge);
    }

    /// <summary>Slip in this direction (the test command takes the view direction)</summary>
    public static void Slip(EntityPlayer eplr, BlockFacing edge)
    {
        edge ??= BlockFacing.HorizontalFromAngle(eplr.Pos.Yaw);

        StupidSounds.Play(sapi.World, StupidSounds.Slip, eplr, 24);
        ShoveOverEdge(eplr, edge);
        SchadenfreudeModSystem.Chat(eplr.Player, "schadenfreude:cliff-message");
    }

    /// <summary>
    /// Actually get the player over the edge. The client computes player movement, so a push alone
    /// is not enough: first the knockback of a normal hit (which the game itself applies), then a
    /// short teleport so the player is guaranteed to hang in the air.
    /// </summary>
    static void ShoveOverEdge(EntityPlayer eplr, BlockFacing edge)
    {
        var attrs = eplr.WatchedAttributes;
        double dx = edge.Normali.X, dz = edge.Normali.Z;

        // The game knockback channel (kbdir + onHurt): the client adds this to its own movement.
        // onHurt below 0.05 means no damage, no hit flash, just the shove.
        attrs.SetFloat("onHurtDir", (float)Math.Atan2(dx, dz));
        attrs.SetDouble("kbdirX", dx * ShoveStrength);
        attrs.SetDouble("kbdirY", -0.02);
        attrs.SetDouble("kbdirZ", dz * ShoveStrength);
        attrs.SetInt("onHurtCounter", attrs.GetInt("onHurtCounter") + 1);
        attrs.SetFloat("onHurt", 0.01f);

        // 0.8 blocks sideways: the player stands at most 0.45 from the edge, so afterwards their
        // whole hull (half width 0.3) hangs over the drop.
        eplr.TeleportToDouble(eplr.Pos.X + dx * ShoveDistance, eplr.Pos.Y, eplr.Pos.Z + dz * ShoveDistance);
    }

    static bool StandsOnSomething(EntityPlayer eplr)
    {
        var below = new BlockPos((int)Math.Floor(eplr.Pos.X), (int)Math.Floor(eplr.Pos.Y - 0.05), (int)Math.Floor(eplr.Pos.Z), eplr.Pos.Dimension);
        return SchadenfreudeModSystem.IsSolid(sapi.World.BlockAccessor, below);
    }

    /// <summary>
    /// An edge beside the player with a drop of at least minDrop blocks behind it.
    /// Probed half a step (0.55) in every direction - a condition on the distance to the block border
    /// was too strict: someone sneaking rarely stands exactly on the rim.
    /// </summary>
    static bool FindEdge(EntityPlayer eplr, int minDrop, out BlockFacing edge)
    {
        edge = null;
        int feetY = (int)Math.Floor(eplr.Pos.Y + 0.05);
        int ownX = (int)Math.Floor(eplr.Pos.X);
        int ownZ = (int)Math.Floor(eplr.Pos.Z);
        double best = double.MaxValue;

        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            int nx = (int)Math.Floor(eplr.Pos.X + face.Normali.X * ProbeDistance);
            int nz = (int)Math.Floor(eplr.Pos.Z + face.Normali.Z * ProbeDistance);
            if (nx == ownX && nz == ownZ) continue;

            var cell = new BlockPos(nx, feetY, nz, eplr.Pos.Dimension);
            if (SchadenfreudeModSystem.IsSolid(sapi.World.BlockAccessor, cell) || SchadenfreudeModSystem.IsSolid(sapi.World.BlockAccessor, cell.UpCopy())) continue;

            bool deep = true;
            for (int i = 1; i <= minDrop; i++)
            {
                if (SchadenfreudeModSystem.IsSolid(sapi.World.BlockAccessor, cell.DownCopy(i))) { deep = false; break; }
            }
            if (!deep) continue;

            // The nearest edge wins
            double distToBorder = face.Normali.X != 0
                ? Math.Abs(eplr.Pos.X - (face.Normali.X > 0 ? ownX + 1 : ownX))
                : Math.Abs(eplr.Pos.Z - (face.Normali.Z > 0 ? ownZ + 1 : ownZ));
            if (distToBorder >= best) continue;

            best = distToBorder;
            edge = face;
        }
        return edge != null;
    }

    /// <summary>Look for an edge the same way as in the game, but with a report (test command)</summary>
    public static string DescribeEdge(EntityPlayer eplr, out BlockFacing edge)
    {
        CliffConfig cfg = SchadenfreudeModSystem.Config.Cliff;
        bool sneaking = eplr.Controls.Sneak;
        bool onGround = StandsOnSomething(eplr);
        bool found = FindEdge(eplr, cfg.MinDropBlocks, out edge);

        return $"sneaking={sneaking}, standing on ground={onGround}, "
            + $"edge within {cfg.MinDropBlocks} blocks of drop: {(found ? edge.Code : "none")}";
    }

    // --- Stumbling ------------------------------------------------------------------------------

    static void TryStumble(EntityPlayer eplr)
    {
        StumbleConfig cfg = SchadenfreudeModSystem.Config.Stumble;
        if (!cfg.Enabled || !eplr.Controls.TriesToMove || eplr.Controls.Sneak || eplr.Controls.FloorSitting) return;
        if (cfg.OnlyWhenSprinting && !eplr.Controls.Sprint) return;
        if (!StandsOnSomething(eplr) || eplr.Swimming) return;

        int reasons = 0;
        if (cfg.WhenDrunk && eplr.WatchedAttributes.GetFloat("intoxication") >= cfg.MinIntoxication) reasons++;
        if (cfg.WhenChased && IsChased(eplr, cfg.ChaseRange)) reasons++;
        if (reasons == 0 || !SchadenfreudeModSystem.Roll(sapi.World, cfg.ChancePercentPerSecond * reasons)) return;

        Trip(eplr);
    }

    /// <summary>Fall over and stay down for a while</summary>
    public static void Trip(EntityPlayer eplr)
    {
        StumbleConfig cfg = SchadenfreudeModSystem.Config.Stumble;
        double seconds = Math.Max(0.5, cfg.LieSeconds);
        tripUntilMs[eplr.PlayerUID] = sapi.World.ElapsedMilliseconds + (long)(seconds * 1000);

        // No movement, no jumping, while you are down
        eplr.Stats.Set("walkspeed", StatKey, -1f, false);
        eplr.Stats.Set("jumpHeightMul", StatKey, -1f, false);

        // Lying down: the server starts the animation so the other players see it too
        eplr.AnimManager.StartAnimation(LieAnimation);

        StupidSounds.Play(sapi.World, StupidSounds.Falling, eplr);

        var attrs = eplr.WatchedAttributes;
        attrs.SetFloat(TripSecondsKey, (float)seconds);
        attrs.SetInt(TripCounterKey, attrs.GetInt(TripCounterKey) + 1);
        SchadenfreudeModSystem.Chat(eplr.Player, "schadenfreude:stumble-message");
    }

    static void EndTrip(EntityPlayer eplr, string uid)
    {
        tripUntilMs.Remove(uid);
        eplr?.Stats.Remove("walkspeed", StatKey);
        eplr?.Stats.Remove("jumpHeightMul", StatKey);
        eplr?.AnimManager.StopAnimation(LieAnimation);
    }

    /// <summary>A creature is hunting or attacking this player right now (seek/attack task active)</summary>
    static bool IsChased(EntityPlayer eplr, float range)
    {
        Entity[] around = sapi.World.GetEntitiesAround(eplr.Pos.XYZ, range, range / 2, e => e is EntityAgent && e is not EntityPlayer && e.Alive);
        foreach (Entity entity in around)
        {
            var taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
            if (taskAi == null) continue;

            foreach (IAiTask task in taskAi.TaskManager.ActiveTasksBySlot)
            {
                Entity target = task switch
                {
                    AiTaskSeekEntity t => t.TargetEntity,
                    AiTaskMeleeAttack t => t.TargetEntity,
                    AiTaskSeekEntityR t => t.TargetEntity,
                    AiTaskMeleeAttackR t => t.TargetEntity,
                    AiTaskShootAtEntityR t => t.TargetEntity,
                    _ => null
                };
                if (target == eplr) return true;
            }
        }
        return false;
    }
}
