using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Schadenfreude;

/// <summary>
/// Mechanic 17: chests in ruins and dungeons can be mimics. Opening one makes it snap open and
/// attack; when the mimic dies, the real chest and its contents stand where it toppled over.
/// Whether a chest is a mimic is tied to its position (same chest - same answer, every time).
/// </summary>
public static class Mimic
{
    static readonly AssetLocation MimicCode = new(SchadenfreudeModSystem.ModId, "mimic");
    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.DidUseBlock += OnUseBlock;
    }

    static void OnUseBlock(IServerPlayer player, BlockSelection blockSel)
    {
        MimicConfig cfg = SchadenfreudeModSystem.Config.Mimic;
        if (!cfg.Enabled || blockSel == null || player?.Entity == null || !SchadenfreudeModSystem.Affects(player)) return;

        BlockPos pos = blockSel.Position;
        IWorldAccessor world = sapi.World;
        Block block = world.BlockAccessor.GetBlock(pos);
        if (!SchadenfreudeModSystem.CodeMatches(cfg.ChestCodes, block?.Code)) return;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityContainer container) return;
        // Multi-block chests (e.g. log chests) would leave remnants behind when removed
        if (block is BlockGenericTypedContainerTrunk) return;
        if (!InMatchingStructure(pos, cfg.StructureCodes)) return;
        if (!IsMimicPosition(pos, cfg.ChancePercent)) return;

        // Save the contents, close the chest dialog, remove the block without a trace
        var stacks = new List<ItemStack>();
        foreach (ItemSlot slot in container.Inventory)
        {
            if (!slot.Empty) stacks.Add(slot.Itemstack.Clone());
        }
        player.InventoryManager.CloseInventoryAndSync(container.Inventory);
        container.Inventory.Clear();
        world.BlockAccessor.SetBlock(0, pos);

        EntityProperties type = world.GetEntityType(MimicCode);
        if (type == null) return;

        Entity mimic = world.ClassRegistry.CreateEntity(type);
        mimic.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        mimic.Pos.Dimension = pos.dimension;
        mimic.Pos.Yaw = (float)Math.Atan2(player.Entity.Pos.X - pos.X - 0.5, player.Entity.Pos.Z - pos.Z - 0.5);
        EntityBehaviorMimic.SetChest(mimic, block, stacks);
        world.SpawnEntity(mimic);

        if (block.Sounds?.Break != null) world.PlaySoundAt(block.Sounds.Break, pos, 0, null);
        SchadenfreudeModSystem.Chat(player, "schadenfreude:mimic-message");
    }

    /// <summary>Does the chest stand inside a matching worldgen structure?</summary>
    static bool InMatchingStructure(BlockPos pos, string[] patterns)
    {
        bool found = false;
        sapi.World.BlockAccessor.WalkStructures(pos, structure =>
        {
            if (found || structure?.Code == null) return;
            foreach (string pattern in patterns ?? [])
            {
                if (!string.IsNullOrWhiteSpace(pattern) && WildcardUtil.Match(pattern.Trim(), structure.Code))
                {
                    found = true;
                    return;
                }
            }
        });
        return found;
    }

    /// <summary>Derived from position and world seed - the same chest is always (or never) a mimic</summary>
    static bool IsMimicPosition(BlockPos pos, double chancePercent)
    {
        int hash = GameMath.MurmurHash3(pos.X + sapi.World.Seed, pos.Y, pos.Z);
        double value = (hash & 0x7fffffff) / (double)int.MaxValue;
        return value * 100 < chancePercent;
    }
}

/// <summary>Carries the real chest around and puts it back down on death</summary>
public class EntityBehaviorMimic : EntityBehavior
{
    const string TreeKey = "schadenfreude-mimic";

    public EntityBehaviorMimic(Entity entity) : base(entity) { }

    public override string PropertyName() => "schadenfreude.mimic";

    public static void SetChest(Entity mimic, Block chest, List<ItemStack> stacks)
    {
        ITreeAttribute tree = mimic.Attributes.GetOrAddTreeAttribute(TreeKey);
        tree.SetString("block", chest.Code.ToString());

        ITreeAttribute contents = tree.GetOrAddTreeAttribute("contents");
        contents.SetInt("count", stacks.Count);
        for (int i = 0; i < stacks.Count; i++)
        {
            contents.SetItemstack("stack" + i, stacks[i]);
        }
    }

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        base.OnEntityDeath(damageSourceForDeath);
        if (entity.World.Side != EnumAppSide.Server) return;

        IWorldAccessor world = entity.World;
        ITreeAttribute tree = entity.Attributes.GetTreeAttribute(TreeKey);
        Block chest = tree == null ? null : world.GetBlock(new AssetLocation(tree.GetString("block")));
        List<ItemStack> stacks = StoredStacks(tree);

        if (chest == null || !TryPlaceChest(chest, stacks))
        {
            // No room for the chest: at least the contents end up on the ground
            foreach (ItemStack stack in stacks) world.SpawnItemEntity(stack, entity.Pos.XYZ.Add(0, 0.25, 0));
        }
    }

    bool TryPlaceChest(Block chest, List<ItemStack> stacks)
    {
        IBlockAccessor ba = entity.World.BlockAccessor;
        BlockPos start = entity.Pos.AsBlockPos;

        foreach (BlockPos pos in Candidates(start))
        {
            Block at = ba.GetBlock(pos);
            if (at.Id != 0 && !at.IsReplacableBy(chest)) continue;
            if (ba.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) continue;

            Cuboidf[] below = ba.GetBlock(pos.DownCopy()).GetCollisionBoxes(ba, pos.DownCopy());
            if (below == null || below.Length == 0) continue;

            ba.SetBlock(chest.BlockId, pos);
            if (ba.GetBlockEntity(pos) is BlockEntityContainer container)
            {
                int i = 0;
                foreach (ItemSlot slot in container.Inventory)
                {
                    if (i >= stacks.Count) break;
                    slot.Itemstack = stacks[i++];
                    slot.MarkDirty();
                }
                container.MarkDirty(true);

                // Whatever no longer fits drops beside it
                for (; i < stacks.Count; i++) entity.World.SpawnItemEntity(stacks[i], pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            ba.MarkBlockDirty(pos);
            return true;
        }
        return false;
    }

    static IEnumerable<BlockPos> Candidates(BlockPos start)
    {
        yield return start;
        foreach (BlockFacing face in BlockFacing.HORIZONTALS) yield return start.AddCopy(face);
        yield return start.UpCopy();
    }

    List<ItemStack> StoredStacks(ITreeAttribute tree)
    {
        var stacks = new List<ItemStack>();
        ITreeAttribute contents = tree?.GetTreeAttribute("contents");
        if (contents == null) return stacks;

        int count = contents.GetInt("count");
        for (int i = 0; i < count; i++)
        {
            ItemStack stack = contents.GetItemstack("stack" + i);
            if (stack == null) continue;
            stack.ResolveBlockOrItem(entity.World);
            if (stack.Collectible != null) stacks.Add(stack);
        }
        return stacks;
    }
}
