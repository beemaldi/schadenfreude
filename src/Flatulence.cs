using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BadLuck;

/// <summary>
/// Mechanic 7: vegetables make you bloated. Every serving eaten queues up farts, spread randomly over
/// the next few minutes. The older the vegetable (its freshness curve), the more farts, the longer
/// they last and the further the stink reaches. A fart draws aggressive creatures to the spot and
/// makes the player easier to notice for a while.
/// Only predators and hostiles are drawn in (see IsLured).
/// </summary>
public static class Flatulence
{
    class PendingFart
    {
        public long DueMs;
        public float Age;
    }

    /// <summary>A lingering fart cloud: anyone who walks into it gets the mushroom trip</summary>
    class GasCloudState
    {
        public Vec3d Center;
        public float Age;
        public long UntilMs;
        public long NextPuffMs;
        public string SourceUid;

        /// <summary>Until then the culprit is spared - keep walking and you get away with it</summary>
        public long SelfSafeUntilMs;
        public readonly HashSet<string> Affected = new();
    }

    /// <summary>A huge meal must not cripple the player for minutes on end</summary>
    const int MaxFartsPerMeal = 25;

    /// <summary>For this long your own cloud cannot get you - stand still and it will anyway</summary>
    const long SelfSafeMs = 2000;

    static readonly SeekRangeBoost notice = new("badluck-fart");

    // Below seeking/attacking (mostly from 1.45 up), above wandering (which cancels at 1.35)
    const float InvestigatePriority = 1.44f;

    static readonly AssetLocation[] FartSounds = Enumerable.Range(1, 6)
        .Select(i => new AssetLocation(BadLuckModSystem.ModId, "sounds/fart/fart" + i))
        .ToArray();

    static ICoreServerAPI sapi;
    static AssetLocation[] vegetablePatterns = [];
    /// <summary>
    /// A bit of trip or dizziness this mod added. The game wears both off by only 0.005 per second, which
    /// would keep a single cloud going for minutes - so the mod takes its own share back after a while.
    /// </summary>
    class TimedEffect
    {
        public string Uid;
        public string Stat;
        public float Amount;
        public long UntilMs;
    }

    static readonly Dictionary<string, List<PendingFart>> pending = new();
    static readonly List<GasCloudState> clouds = new();
    static readonly List<TimedEffect> effects = new();
    static readonly Dictionary<string, bool> luredCache = new();
    static TagSetFast? lureTagSet;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        pending.Clear();
        notice.Clear();
        clouds.Clear();
        effects.Clear();
        luredCache.Clear();
        lureTagSet = null;
        AiTaskRegistry.Register<AiTaskInvestigateSmell>(AiTaskInvestigateSmell.TaskCode);

        api.Event.RegisterGameTickListener(OnTick, 250);
        api.Event.PlayerDisconnect += player => Forget(player);
        api.Event.PlayerDeath += (player, damageSource) => Forget(player);
    }

    public static void OnConfigLoaded()
    {
        vegetablePatterns = (BadLuckModSystem.Config.Flatulence.VegetableCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new AssetLocation(code.Trim()))
            .ToArray();

        // The lure lists may have changed
        luredCache.Clear();
        lureTagSet = null;
    }

    public static bool IsVegetable(ItemStack stack)
    {
        AssetLocation code = stack?.Collectible?.Code;
        if (code == null) return false;
        foreach (AssetLocation pattern in vegetablePatterns)
        {
            if (WildcardUtil.Match(pattern, code)) return true;
        }
        return false;
    }

    /// <summary>0 = freshly harvested, 1 = just short of rotting (the way the game works it out)</summary>
    public static float PerishProgress(IWorldAccessor world, ItemSlot slot)
    {
        TransitionState state = slot?.Itemstack?.Collectible?.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state == null) return 0;
        float total = state.FreshHours + state.TransitionHours;
        return total <= 0 ? 0 : GameMath.Clamp(state.TransitionedHours / total, 0, 1);
    }

    /// <summary>Turn eaten vegetable servings into farts and spread them out over time</summary>
    public static void AddGas(EntityPlayer eplr, float portions, float age)
    {
        FlatulenceConfig cfg = BadLuckModSystem.Config.Flatulence;
        if (!cfg.Enabled || eplr?.Player == null || portions <= 0 || !BadLuckModSystem.Affects(eplr.Player)) return;

        IWorldAccessor world = eplr.World;
        age = GameMath.Clamp(age, 0, 1);

        float perPortion = GameMath.Lerp(cfg.FartsPerVegetableFresh, cfg.FartsPerVegetableOld, age);
        int count = Math.Min(MaxFartsPerMeal, GameMath.RoundRandom(world.Rand, portions * perPortion));
        if (count <= 0) return;

        double durationMs = GameMath.Lerp((float)cfg.MinutesFresh, (float)cfg.MinutesOld, age) * 60000;
        long now = world.ElapsedMilliseconds;

        string uid = eplr.PlayerUID;
        if (!pending.TryGetValue(uid, out List<PendingFart> list))
        {
            pending[uid] = list = new List<PendingFart>();
        }
        for (int i = 0; i < count; i++)
        {
            list.Add(new PendingFart { DueMs = now + (long)(world.Rand.NextDouble() * durationMs), Age = age });
        }
    }

    static void OnTick(float dt)
    {
        long now = sapi.World.ElapsedMilliseconds;

        foreach (string uid in pending.Keys.ToArray())
        {
            List<PendingFart> list = pending[uid];
            EntityPlayer eplr = sapi.World.PlayerByUid(uid)?.Entity;
            if (eplr == null || !eplr.Alive)
            {
                pending.Remove(uid);
                continue;
            }

            // At most one fart per tick, so that several due at once do not go off together
            int index = list.FindIndex(fart => fart.DueMs <= now);
            if (index >= 0)
            {
                float age = list[index].Age;
                list.RemoveAt(index);
                Fart(eplr, age);
            }
            if (list.Count == 0) pending.Remove(uid);
        }

        UpdateClouds(now);
        WearOffEffects(now);
        notice.RemoveExpired(sapi);
    }

    /// <summary>Fart right now (test command)</summary>
    public static void ForceFart(EntityPlayer eplr, float age) => Fart(eplr, age);

    static void Fart(EntityPlayer eplr, float age)
    {
        FlatulenceConfig cfg = BadLuckModSystem.Config.Flatulence;
        if (!cfg.Enabled) return;

        IWorldAccessor world = eplr.World;
        float radius = GameMath.Lerp(cfg.RadiusFresh, cfg.RadiusOld, age);

        // The older it is, the longer/deeper the variant (1-2 fresh, 3-4 middling, 5-6 old)
        int band = age < 1 / 3f ? 0 : age < 2 / 3f ? 2 : 4;
        AssetLocation sound = FartSounds[band + world.Rand.Next(2)];
        StupidSounds.PlayOr(world, StupidSounds.Fart, sound, eplr, Math.Max(16, radius), 0.6f + 0.4f * age);

        BadLuckModSystem.Chat(eplr.Player, band == 0 ? "badluck:fart-small" : band == 2 ? "badluck:fart-medium" : "badluck:fart-large");
        StartCloud(eplr, age, cfg);
        MakeNeighboursDizzy(eplr, cfg);
        notice.Apply(eplr, GameMath.Lerp(cfg.NoticeBonusFresh, cfg.NoticeBonusOld, age), cfg.NoticeSeconds);
        LureCreatures(eplr, radius);
    }

    static void StartCloud(EntityPlayer eplr, float age, FlatulenceConfig cfg)
    {
        // Behind the player at hip height
        Vec3f view = eplr.Pos.GetViewVector();
        double len = Math.Max(0.001, Math.Sqrt(view.X * view.X + view.Z * view.Z));
        Vec3d center = eplr.Pos.XYZ.Add(-view.X / len * 0.35, 0.8, -view.Z / len * 0.35);

        long now = eplr.World.ElapsedMilliseconds;
        clouds.Add(new GasCloudState
        {
            Center = center,
            Age = age,
            UntilMs = now + (long)(Math.Max(0.5, cfg.CloudSeconds) * 1000),
            NextPuffMs = now,
            SourceUid = eplr.PlayerUID,
            SelfSafeUntilMs = now + SelfSafeMs
        });
    }

    /// <summary>Let the clouds drift on and check who walks into them</summary>
    static void UpdateClouds(long now)
    {
        FlatulenceConfig cfg = BadLuckModSystem.Config.Flatulence;
        for (int i = clouds.Count - 1; i >= 0; i--)
        {
            GasCloudState cloud = clouds[i];
            if (now >= cloud.UntilMs)
            {
                clouds.RemoveAt(i);
                continue;
            }

            if (cfg.GasCloud && now >= cloud.NextPuffMs)
            {
                SpawnCloudParticles(cloud.Center, cloud.Age);
                cloud.NextPuffMs = now + 1000;
            }

            if (!cfg.CloudPsychedelic) continue;

            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                EntityPlayer other = player.Entity;
                if (other == null || !other.Alive || cloud.Affected.Contains(player.PlayerUID)) continue;

                // For the first moment the culprit is still standing in it: a short grace period to walk off
                if (player.PlayerUID == cloud.SourceUid && now < cloud.SelfSafeUntilMs) continue;
                if (!BadLuckModSystem.Affects(player)) continue;

                Vec3d chest = other.Pos.XYZ.Add(0, 1, 0);
                double dx = chest.X - cloud.Center.X, dz = chest.Z - cloud.Center.Z;
                if (Math.Sqrt(dx * dx + dz * dz) > cfg.CloudRadius || Math.Abs(chest.Y - cloud.Center.Y) > cfg.CloudRadius) continue;

                cloud.Affected.Add(player.PlayerUID);
                AddEffect(other, "psychedelic", GameMath.Lerp(cfg.PsychedelicFresh, cfg.PsychedelicOld, cloud.Age), 2f, now);
                BadLuckModSystem.Chat(player, "badluck:fartcloud-message");
            }
        }
    }

    static void SpawnCloudParticles(Vec3d center, float age)
    {
        float count = 10 + 20 * age;
        var cloud = new SimpleParticleProperties(
            count, count * 1.5f,
            ColorUtil.ToRgba(110, 135, 175, 60),
            center.AddCopy(-0.4, -0.3, -0.4),
            center.AddCopy(0.4, 0.4, 0.4),
            new Vec3f(-0.08f, 0f, -0.08f),
            new Vec3f(0.08f, 0.06f, 0.08f),
            2.5f + age, -0.005f, 0.8f, 1.6f + age,
            EnumParticleModel.Quad
        );
        cloud.SelfPropelled = true;
        cloud.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -60);
        cloud.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.2f);
        sapi.World.SpawnParticles(cloud);
    }

    static void MakeNeighboursDizzy(EntityPlayer eplr, FlatulenceConfig cfg)
    {
        if (cfg.DizzyRadius <= 0 || cfg.DizzyAmount <= 0) return;

        foreach (IPlayer other in eplr.World.AllOnlinePlayers)
        {
            EntityPlayer otherEntity = other.Entity;
            if (otherEntity == null || otherEntity == eplr || !otherEntity.Alive) continue;
            if (otherEntity.Pos.Dimension != eplr.Pos.Dimension) continue;
            if (otherEntity.Pos.DistanceTo(eplr.Pos) > cfg.DizzyRadius) continue;

            AddEffect(otherEntity, "intoxication", cfg.DizzyAmount, 1.1f, eplr.World.ElapsedMilliseconds);
        }
    }

    static void LureCreatures(EntityPlayer eplr, float radius)
    {
        // Respect the world setting for creature hostility
        if (sapi.World.Config.GetString("creatureHostility", "aggressive") != "aggressive") return;

        Vec3d spot = eplr.Pos.XYZ;
        Entity[] around = eplr.World.GetEntitiesAround(spot, radius, radius, e => e is EntityAgent && e is not EntityPlayer && e.Alive);
        int lured = 0;
        foreach (Entity entity in around)
        {
            if (!IsLured(entity.Properties)) continue;
            CreatureMovement.Style style = CreatureMovement.Of(entity.Properties);
            AiTaskInvestigateSmell.Send((EntityAgent)entity, spot, InvestigatePriority, style.MoveSpeed, style.Animation, style.AnimationSpeed);
            lured++;
        }

        // Otherwise nobody would notice that the stink did anything
        if (lured > 0) BadLuckModSystem.Chat(eplr.Player, "badluck:fart-lure");
    }

    /// <summary>
    /// Drawn in is whoever carries one of the game tags (predators "ferocious", hostiles
    /// "rust-creature") or is listed by code, except for the excluded codes. Cached per entity type.
    /// </summary>
    static bool IsLured(EntityProperties type)
    {
        string key = type.Code.ToString();
        if (luredCache.TryGetValue(key, out bool cached)) return cached;

        FlatulenceConfig cfg = BadLuckModSystem.Config.Flatulence;
        bool lured = !MatchesAny(cfg.LureExcludeCodes, type.Code)
            && (type.Tags.Overlaps(LureTagSet()) || MatchesAny(cfg.LureCodes, type.Code));

        luredCache[key] = lured;
        return lured;
    }

    static TagSetFast LureTagSet()
    {
        if (lureTagSet != null) return lureTagSet.Value;

        TagSetFast set = TagSetFast.Empty;
        foreach (string tag in BadLuckModSystem.Config.Flatulence.LureTags ?? [])
        {
            // Simply skip unknown tags (e.g. from mods that are not installed)
            if (!string.IsNullOrWhiteSpace(tag) && sapi.EntityTagRegistry.TryCreateTagSet(out TagSetFast one, tag.Trim()) == TagRegistryError.None)
            {
                set |= one;
            }
        }
        lureTagSet = set;
        return set;
    }

    static bool MatchesAny(string[] patterns, AssetLocation code)
    {
        foreach (string pattern in patterns ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pattern) && WildcardUtil.Match(new AssetLocation(pattern.Trim()), code)) return true;
        }
        return false;
    }

    /// <summary>Raise a game stat (trip, dizziness) and remember how much, so it can be taken back later</summary>
    static void AddEffect(EntityPlayer eplr, string stat, float amount, float max, long now)
    {
        float before = eplr.WatchedAttributes.GetFloat(stat);
        float after = Math.Min(max, before + amount);
        eplr.WatchedAttributes.SetFloat(stat, after);

        double seconds = BadLuckModSystem.Config.Flatulence.PsychedelicSeconds;
        effects.Add(new TimedEffect { Uid = eplr.PlayerUID, Stat = stat, Amount = after - before, UntilMs = now + (long)(seconds * 1000) });
    }

    static void WearOffEffects(long now)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            if (now < effects[i].UntilMs) continue;
            WearOff(effects[i]);
            effects.RemoveAt(i);
        }
    }

    /// <summary>Only this mod's share - a mushroom eaten in between keeps working</summary>
    static void WearOff(TimedEffect effect)
    {
        EntityPlayer eplr = sapi.World.PlayerByUid(effect.Uid)?.Entity;
        if (eplr == null) return;

        float value = eplr.WatchedAttributes.GetFloat(effect.Stat);
        eplr.WatchedAttributes.SetFloat(effect.Stat, Math.Max(0, value - effect.Amount));
    }

    static void Forget(IServerPlayer player)
    {
        pending.Remove(player.PlayerUID);
        notice.Remove(player);

        // Leaving or dying ends the effect at once - otherwise it would be saved with the player
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            if (effects[i].Uid != player.PlayerUID) continue;
            WearOff(effects[i]);
            effects.RemoveAt(i);
        }
    }
}

/// <summary>Raw and pickled vegetables (vanilla eating through CollectibleObject.tryEatStop)</summary>
[HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
static class EatStopFlatulencePatch
{
    public class State
    {
        public ItemStack Stack;
        public int Size;
        public float Age;
        public bool Vegetable;
    }

    static void Prefix(ItemSlot slot, EntityAgent byEntity, out State __state)
    {
        __state = null;
        if (byEntity?.World.Side != EnumAppSide.Server || byEntity is not EntityPlayer) return;

        ItemStack stack = slot?.Itemstack;
        if (stack == null) return;

        bool vegetable = Flatulence.IsVegetable(stack);
        __state = new State
        {
            Stack = stack,
            Size = stack.StackSize,
            Vegetable = vegetable,
            Age = vegetable ? Flatulence.PerishProgress(byEntity.World, slot) : 0
        };
    }

    static void Postfix(ItemSlot slot, EntityAgent byEntity, State __state)
    {
        if (__state == null) return;

        Guard.Run("flatulence after eating", () =>
        {
            bool eaten = slot.Itemstack != __state.Stack || slot.StackSize < __state.Size;
            if (!eaten) return;

            var eplr = (EntityPlayer)byEntity;
            if (__state.Vegetable) Flatulence.AddGas(eplr, 1, __state.Age);
            Choking.OnAte(eplr.Player as IServerPlayer);   // mechanic 16
        });
    }
}

/// <summary>Meals (stew, soup, pie): proportional to the vegetable servings and the servings eaten</summary>
[HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.Consume))]
static class MealConsumeFlatulencePatch
{
    public class State
    {
        public float VegetablesPerServing;
        public float Age;
    }

    static void Prefix(IWorldAccessor world, IPlayer eatingPlayer, ItemSlot inSlot, ItemStack[] contentStacks, bool mulwithStackSize, out State __state)
    {
        __state = null;
        if (world.Side != EnumAppSide.Server || eatingPlayer?.Entity == null || contentStacks == null) return;

        float perServing = 0;
        foreach (ItemStack stack in contentStacks)
        {
            if (stack != null && Flatulence.IsVegetable(stack)) perServing += mulwithStackSize ? stack.StackSize : 1;
        }

        __state = new State { VegetablesPerServing = perServing, Age = Flatulence.PerishProgress(world, inSlot) };
    }

    static void Postfix(IPlayer eatingPlayer, float remainingServings, float __result, State __state)
    {
        if (__state == null) return;

        Guard.Run("flatulence after a meal", () =>
        {
            float servingsEaten = remainingServings - __result;
            if (servingsEaten <= 0) return;

            if (__state.VegetablesPerServing > 0) Flatulence.AddGas(eatingPlayer.Entity, __state.VegetablesPerServing * servingsEaten, __state.Age);
            Choking.OnAte(eatingPlayer as IServerPlayer);   // mechanic 16
        });
    }
}
