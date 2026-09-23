using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace BadLuck;

/// <summary>
/// Mechanic 1: a ripe plant jumps out of the ground when harvested and runs away.
/// </summary>
public static class RunningPlant
{
    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.BreakBlock += OnBreakBlock;
    }

    static void OnBreakBlock(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
    {
        RunningPlantConfig cfg = BadLuckModSystem.Config.RunningPlant;
        if (!cfg.Enabled || handling == EnumHandling.PreventDefault || !BadLuckModSystem.Affects(byPlayer)) return;

        IWorldAccessor world = sapi.World;
        BlockPos pos = blockSel.Position;
        if (world.BlockAccessor.GetBlock(pos) is not BlockCrop crop) return;
        if (crop.CropProps == null || crop.CurrentCropStage < crop.CropProps.GrowthStages) return;
        if (!BadLuckModSystem.Roll(world, cfg.ChancePercent)) return;

        EntityProperties type = world.GetEntityType(new AssetLocation(BadLuckModSystem.ModId, "runningplant"));
        if (type == null) return;

        // Capture the harvest now (it depends on the farmland) - the plant carries it along
        ItemStack[] drops = crop.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];

        var farmland = world.BlockAccessor.GetBlockEntity(pos.DownCopy()) as BlockEntityFarmland;
        world.BlockAccessor.SetBlock(0, pos);
        farmland?.OnCropBlockBroken();
        handling = EnumHandling.PreventDefault;

        if (crop.Sounds?.Break != null)
        {
            world.PlaySoundAt(crop.Sounds.Break, pos, -0.3, null);
        }

        Entity entity = world.ClassRegistry.CreateEntity(type);
        entity.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        entity.Pos.Dimension = pos.dimension;
        entity.Pos.Yaw = (float)(world.Rand.NextDouble() * GameMath.TWOPI);
        EntityBehaviorRunningPlant.Setup(entity, crop, pos, drops);
        world.SpawnEntity(entity);
        BadLuckModSystem.Chat(byPlayer, "badluck:runningplant-message");
    }
}

/// <summary>
/// Holds on to the harvest it took, plants itself again once its time is up,
/// and drops the harvest when killed.
/// </summary>
public class EntityBehaviorRunningPlant : EntityBehavior
{
    const string TreeKey = "badluck-runningplant";

    float secondsAlive;

    public EntityBehaviorRunningPlant(Entity entity) : base(entity) { }

    public override string PropertyName() => "badluck.runningplant";

    public static void Setup(Entity entity, Block crop, BlockPos origin, ItemStack[] drops)
    {
        ITreeAttribute tree = entity.Attributes.GetOrAddTreeAttribute(TreeKey);
        tree.SetString("crop", crop.Code.ToString());
        tree.SetInt("originX", origin.X);
        tree.SetInt("originY", origin.Y);
        tree.SetInt("originZ", origin.Z);
        tree.SetInt("originDim", origin.dimension);

        ITreeAttribute dropTree = tree.GetOrAddTreeAttribute("drops");
        dropTree.SetInt("count", drops.Length);
        for (int i = 0; i < drops.Length; i++)
        {
            dropTree.SetItemstack("stack" + i, drops[i]);
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

        secondsAlive += deltaTime;
        if (secondsAlive < BadLuckModSystem.Config.RunningPlant.ReplantSeconds) return;

        Replant();
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventSubsequent;
        return StoredDrops();
    }

    void Replant()
    {
        IWorldAccessor world = entity.World;
        ITreeAttribute tree = entity.Attributes.GetTreeAttribute(TreeKey);
        Block crop = tree == null ? null : world.GetBlock(new AssetLocation(tree.GetString("crop")));

        bool planted = false;
        if (crop != null)
        {
            // First where it stands right now - otherwise back to its old spot
            planted = TryPlant(entity.Pos.AsBlockPos, crop, true);
            if (!planted)
            {
                var origin = new BlockPos(tree.GetInt("originX"), tree.GetInt("originY"), tree.GetInt("originZ"), tree.GetInt("originDim"));
                planted = TryPlant(origin, crop, false);
            }
        }

        if (!planted)
        {
            // Nowhere to plant itself: do not lose the harvest
            foreach (ItemStack stack in StoredDrops())
            {
                world.SpawnItemEntity(stack, entity.Pos.XYZ.Add(0, 0.25, 0));
            }
        }

        entity.Die(EnumDespawnReason.Removed);
    }

    bool TryPlant(BlockPos pos, Block crop, bool mayTillSoil)
    {
        IWorldAccessor world = entity.World;
        IBlockAccessor ba = world.BlockAccessor;

        Block atPos = ba.GetBlock(pos);
        if (atPos.Id != 0 && !atPos.IsReplacableBy(crop)) return false;
        if (ba.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) return false;

        BlockPos below = pos.DownCopy();
        Block ground = ba.GetBlock(below);

        if (ground is not BlockFarmland)
        {
            if (!mayTillSoil || !ground.Code.PathStartsWith("soil")) return false;
            if (!TillSoil(below, ground)) return false;
        }

        ba.SetBlock(crop.BlockId, pos);
        ba.MarkBlockDirty(pos);
        if (crop.Sounds?.Place != null)
        {
            world.PlaySoundAt(crop.Sounds.Place, pos, -0.3, null);
        }
        return true;
    }

    /// <summary>Soil into farmland, the way the hoe does it</summary>
    bool TillSoil(BlockPos pos, Block soil)
    {
        IWorldAccessor world = entity.World;
        IBlockAccessor ba = world.BlockAccessor;

        string fertility = soil.LastCodePart(1);
        Block farmland = world.GetBlock(new AssetLocation("farmland-dry-" + fertility));
        if (farmland == null) return false;

        TreeAttribute prevData = null;
        if (ba.GetBlockEntity(pos) is BlockEntitySoilNutrition besn)
        {
            prevData = new TreeAttribute();
            besn.ToTreeAttributes(prevData);
        }

        ba.SetBlock(farmland.BlockId, pos);
        if (ba.GetBlockEntity(pos) is BlockEntityFarmland bef)
        {
            bef.OnCreatedFromSoil(soil, prevData);
        }
        ba.MarkBlockDirty(pos);
        return true;
    }

    ItemStack[] StoredDrops()
    {
        ITreeAttribute dropTree = entity.Attributes.GetTreeAttribute(TreeKey)?.GetTreeAttribute("drops");
        if (dropTree == null) return [];

        int count = dropTree.GetInt("count");
        var stacks = new System.Collections.Generic.List<ItemStack>(count);
        for (int i = 0; i < count; i++)
        {
            ItemStack stack = dropTree.GetItemstack("stack" + i);
            if (stack == null) continue;
            stack.ResolveBlockOrItem(entity.World);
            if (stack.Collectible != null) stacks.Add(stack);
        }
        return stacks.ToArray();
    }
}
