using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Server command "/schadenfreude test &lt;mechanic&gt;". Most mechanics have a chance of 1 % and are hard
/// to pin down in game - this triggers any of them on demand.
/// </summary>
public static class Commands
{
    static ICoreServerAPI sapi;

    /// <summary>Name -> what happens. Returns the message sent back to the caller</summary>
    static readonly Dictionary<string, System.Func<IServerPlayer, string>> Tests = new()
    {
        ["cliff"] = player =>
        {
            string report = Mishaps.DescribeEdge(player.Entity, out var edge);
            Mishaps.Slip(player.Entity, edge);
            return report + (edge == null ? " -> pushed the way you are looking" : " -> pushed over that edge");
        },
        ["stumble"] = player => { Mishaps.Trip(player.Entity); return "tripped"; },
        ["fart"] = player => { Flatulence.ForceFart(player.Entity, 1f); return "farted (ancient vegetable, largest radius)"; },
        ["choke"] = player => Choking.ForceChoke(player) ? "choked" : "still coughing from the last fit",
        ["eye"] = player => EyeChip.Hit(player) ? "stone chip in the eye" : "both eyes are already shut",
        ["splinter"] = player => { Splinter.Catch(player); return "splinter caught"; },
        ["toolfly"] = player => ToolFly.ForceFly(player.Entity) ? "tool thrown" : "nothing in your right hand",
        ["snake"] = player => { Snake.Drop(player.Entity); return "snake dropped"; },
        ["ants"] = player => { GroundHazards.ForceFireAnts(player.Entity); return "fire ants"; },
        ["hornets"] = player => { GroundHazards.ForceHornets(player.Entity); return "hornets"; },
        ["torch"] = player => { player.Entity.Ignite(); return "set on fire"; },
        ["stone"] = Stone,
        ["boomerang"] = ThrowReturningStone,
        ["door"] = Door
    };

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        api.ChatCommands.Create("schadenfreude")
            .WithDescription("Schadenfreude mod")
            .RequiresPrivilege(Privilege.controlserver)
            .RequiresPlayer()
            .BeginSubCommand("test")
                .WithDescription("Trigger one mechanic right now: " + string.Join(", ", Tests.Keys))
                .WithArgs(api.ChatCommands.Parsers.Word("mechanic", [.. Tests.Keys]))
                .HandleWith(OnTest)
            .EndSubCommand();
    }

    static TextCommandResult OnTest(TextCommandCallingArgs args)
    {
        var player = (IServerPlayer)args.Caller.Player;
        if (player.Entity == null) return TextCommandResult.Error("no player entity");

        string name = (args[0] as string ?? "").ToLowerInvariant();
        if (!Tests.TryGetValue(name, out var test))
        {
            return TextCommandResult.Error("unknown mechanic, try: " + string.Join(", ", Tests.Keys));
        }

        try
        {
            return TextCommandResult.Success(name + ": " + test(player));
        }
        catch (Exception e)
        {
            SchadenfreudeModSystem.Logger?.Error("schadenfreude test {0} failed: {1}", name, e);
            return TextCommandResult.Error(name + " failed: " + e.Message);
        }
    }

    /// <summary>Throw a stone that is guaranteed to come back</summary>
    static string ThrowReturningStone(IServerPlayer player)
    {
        EntityPlayer eplr = player.Entity;
        EntityProperties type = sapi.World.GetEntityType(new AssetLocation("game:thrownitem"));
        if (type == null || sapi.World.ClassRegistry.CreateEntity(type) is not EntityThrownItem stone) return "no thrownitem entity";

        Item item = sapi.World.GetItem(new AssetLocation("stone-granite"));
        stone.ProjectileStack = new ItemStack(item);
        stone.FiredBy = eplr;
        stone.Collectible = true;

        Vec3f view = eplr.Pos.GetViewVector();
        stone.Pos.SetPos(eplr.Pos.XYZ.Add(eplr.LocalEyePos.X, eplr.LocalEyePos.Y, eplr.LocalEyePos.Z).Add(view.X, view.Y, view.Z));
        stone.Pos.Dimension = eplr.Pos.Dimension;
        stone.Pos.Motion.Set(view.X * 0.4, view.Y * 0.4 + 0.1, view.Z * 0.4);
        sapi.World.SpawnEntity(stone);

        // Send it on its way back right away
        stone.Attributes.SetBool("schadenfreude-boomerang-rolled", true);
        stone.Attributes.SetDouble("schadenfreude-boomerang-at", sapi.World.ElapsedMilliseconds + SchadenfreudeModSystem.Config.Boomerang.ReturnAfterSeconds * 1000);
        return "stone thrown, it will come back";
    }

    /// <summary>Explosion three blocks in front of the player</summary>
    static string Stone(IServerPlayer player)
    {
        EntityPlayer eplr = player.Entity;
        Vec3f view = eplr.Pos.GetViewVector();
        Vec3d center = eplr.Pos.XYZ.Add(eplr.LocalEyePos.X, eplr.LocalEyePos.Y, eplr.LocalEyePos.Z)
            .Add(view.X * 3, view.Y * 3, view.Z * 3);

        ExplodingStone.ForceExplosion(sapi.World, center);
        SchadenfreudeModSystem.Chat(player, "schadenfreude:explodingstone-message");
        return "stone exploded 3 blocks ahead";
    }

    /// <summary>Tear the door the player is looking at off its hinges</summary>
    static string Door(IServerPlayer player)
    {
        BlockSelection selection = player.CurrentBlockSelection;
        if (selection == null) return "look at a door first";

        DoorHinges.FlyOff(sapi.World, selection.Position, player.Entity.Pos.XYZ);
        SchadenfreudeModSystem.Chat(player, "schadenfreude:door-flyoff");
        return "block torn out of its hinges at " + selection.Position;
    }
}
