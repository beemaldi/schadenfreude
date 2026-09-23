using System;
using System.Reflection;
using Cairo;
using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace BadLuck;

/// <summary>
/// Mechanic 10: mining rock and ore, or knapping, can send a chip into your eye - one eye stays shut
/// for a few seconds. The server decides and writes it into the player WatchedAttributes; the client
/// draws the overlay from there.
/// </summary>
public static class EyeChip
{
    public const string LeftCounterKey = "badluck-eyechip-l";
    public const string RightCounterKey = "badluck-eyechip-r";
    public const string DurationKey = "badluck-eyechip-sec";

    // Server only: until when an eye is already shut
    const string LeftUntilKey = "badluck-eyechip-l-until";
    const string RightUntilKey = "badluck-eyechip-r-until";

    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.DidBreakBlock += OnBreakBlock;
    }

    static void OnBreakBlock(IServerPlayer player, int oldBlockId, BlockSelection blockSel)
    {
        EyeChipConfig cfg = BadLuckModSystem.Config.EyeChip;
        if (!cfg.Enabled || player?.Entity == null || !BadLuckModSystem.Affects(player)) return;

        Block broken = sapi.World.GetBlock(oldBlockId);
        if (!BadLuckModSystem.CodeMatches(cfg.BlockCodes, broken?.Code)) return;
        if (!BadLuckModSystem.Roll(sapi.World, cfg.ChancePercent)) return;

        Hit(player);
    }

    /// <summary>One chip struck off a knapping surface - a roll per chip, so the chance is small</summary>
    public static void OnKnapped(IServerPlayer player)
    {
        EyeChipConfig cfg = BadLuckModSystem.Config.EyeChip;
        if (sapi == null || !cfg.Enabled || player?.Entity == null || !BadLuckModSystem.Affects(player)) return;
        if (!BadLuckModSystem.Roll(sapi.World, cfg.KnappingChancePercent)) return;

        Hit(player);
    }

    /// <summary>
    /// Chip in the eye. An eye that is already shut runs its time out undisturbed - the next chip
    /// hits the other one instead. If both are shut, nothing happens.
    /// </summary>
    public static bool Hit(IServerPlayer player)
    {
        EyeChipConfig cfg = BadLuckModSystem.Config.EyeChip;
        EntityPlayer eplr = player?.Entity;
        if (eplr == null) return false;

        long now = sapi.World.ElapsedMilliseconds;
        bool leftFree = eplr.Attributes.GetDouble(LeftUntilKey) <= now;
        bool rightFree = eplr.Attributes.GetDouble(RightUntilKey) <= now;
        if (!leftFree && !rightFree) return false;

        bool left = leftFree && (!rightFree || sapi.World.Rand.Next(2) == 0);
        double seconds = Math.Max(1, cfg.DurationSeconds);
        eplr.Attributes.SetDouble(left ? LeftUntilKey : RightUntilKey, now + seconds * 1000);

        var attrs = eplr.WatchedAttributes;
        string counterKey = left ? LeftCounterKey : RightCounterKey;
        attrs.SetFloat(DurationKey, (float)seconds);
        attrs.SetInt(counterKey, attrs.GetInt(counterKey) + 1);
        StupidSounds.Play(sapi.World, StupidSounds.EyeChip, eplr, 16);
        BadLuckModSystem.Chat(player, "badluck:eyechip-message");
        return true;
    }
}

/// <summary>Draws the "squeezed shut eye": one half of the screen dark with a soft edge</summary>
public class EyeChipRenderer : IRenderer
{
    const float FadeInSeconds = 0.15f;
    const float MaxFadeOutSeconds = 3f;

    /// <summary>One eye on its own: both can be shut at once, each running its own timer</summary>
    class Eye
    {
        public int LastCounter = int.MinValue;
        public long StartMs = -1;
        public float Duration;
    }

    readonly ICoreClientAPI capi;
    int textureLeftClosed = -1;
    int textureRightClosed = -1;

    readonly Eye left = new();
    readonly Eye right = new();

    public double RenderOrder => 0.1;
    public int RenderRange => 0;

    public EyeChipRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var attrs = capi.World.Player?.Entity?.WatchedAttributes;
        if (attrs == null) return;

        float alphaLeft = Update(left, attrs, EyeChip.LeftCounterKey);
        float alphaRight = Update(right, attrs, EyeChip.RightCounterKey);
        if (alphaLeft <= 0 && alphaRight <= 0) return;

        // Only build the textures on first use - by then the graphics context is definitely up
        if (textureLeftClosed < 0)
        {
            textureLeftClosed = CreateTexture(true);
            textureRightClosed = CreateTexture(false);
        }

        if (alphaLeft > 0) Draw(textureLeftClosed, alphaLeft);
        if (alphaRight > 0) Draw(textureRightClosed, alphaRight);
    }

    void Draw(int texture, float alpha)
    {
        capi.Render.Render2DTexturePremultipliedAlpha(texture, 0f, 0f, capi.Render.FrameWidth, capi.Render.FrameHeight, 50, new Vec4f(alpha, alpha, alpha, alpha));
    }

    /// <summary>Current opacity of this eye, 0 = open</summary>
    float Update(Eye eye, ITreeAttribute attrs, string counterKey)
    {
        // On the first frame only remember the value, so nothing old plays back after logging in
        int counter = attrs.GetInt(counterKey);
        if (eye.LastCounter == int.MinValue) eye.LastCounter = counter;
        if (counter != eye.LastCounter)
        {
            eye.LastCounter = counter;
            eye.StartMs = capi.ElapsedMilliseconds;
            eye.Duration = attrs.GetFloat(EyeChip.DurationKey, 10);
        }
        if (eye.StartMs < 0) return 0;

        float t = (capi.ElapsedMilliseconds - eye.StartMs) / 1000f;
        if (t >= eye.Duration)
        {
            eye.StartMs = -1;
            return 0;
        }

        float alpha = t < FadeInSeconds ? t / FadeInSeconds : 1f;
        float fadeOut = Math.Min(MaxFadeOutSeconds, eye.Duration * 0.3f);
        if (t > eye.Duration - fadeOut) alpha *= (eye.Duration - t) / fadeOut;
        // A slight twitch of the eyelid
        return alpha * (0.92f + 0.08f * (float)Math.Sin(t * 5));
    }

    int CreateTexture(bool leftClosed)
    {
        const int width = 256, height = 128;
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(surface);

        // Closed eye: nearly black at the outer edge, fading out softly towards the middle
        double closedEdge = leftClosed ? 0 : width;
        double softEnd = leftClosed ? width * 0.62 : width * 0.38;
        using (var lid = new LinearGradient(closedEdge, 0, softEnd, 0))
        {
            lid.AddColorStop(0, new Color(0.02, 0.01, 0.01, 0.97));
            lid.AddColorStop(0.6, new Color(0.03, 0.02, 0.02, 0.85));
            lid.AddColorStop(1, new Color(0, 0, 0, 0));
            ctx.SetSource(lid);
            ctx.Paint();
        }

        // The open eye waters: a slight darkening at its edge
        double openX = leftClosed ? width * 0.75 : width * 0.25;
        using (var tears = new RadialGradient(openX, height / 2.0, height * 0.35, openX, height / 2.0, width * 0.7))
        {
            tears.AddColorStop(0, new Color(0, 0, 0, 0));
            tears.AddColorStop(1, new Color(0.02, 0.02, 0.02, 0.45));
            ctx.SetSource(tears);
            ctx.Paint();
        }

        return capi.Gui.LoadCairoTexture(surface, true);
    }

    public void Dispose()
    {
        if (textureLeftClosed >= 0) capi.Render.GLDeleteTexture(textureLeftClosed);
        if (textureRightClosed >= 0) capi.Render.GLDeleteTexture(textureRightClosed);
        textureLeftClosed = textureRightClosed = -1;
    }
}

/// <summary>
/// Knapping is the other way a chip can reach your eye. The block entity has no event for it, so the
/// method that knocks a voxel off is wrapped: fewer voxels afterwards means a chip really came loose.
/// The method is internal, hence TargetMethod instead of nameof.
/// </summary>
[HarmonyPatch]
static class KnappingEyeChipPatch
{
    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(BlockEntityKnappingSurface), "OnUseOver",
            [typeof(IPlayer), typeof(Vec3i), typeof(BlockFacing), typeof(bool)]);
    }

    static void Prefix(BlockEntityKnappingSurface __instance, out int __state)
    {
        __state = CountVoxels(__instance);
    }

    static void Postfix(BlockEntityKnappingSurface __instance, IPlayer byPlayer, int __state)
    {
        Guard.Run("knapping eye chip", () =>
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;
            if (CountVoxels(__instance) >= __state) return;

            EyeChip.OnKnapped(byPlayer as IServerPlayer);
        });
    }

    static int CountVoxels(BlockEntityKnappingSurface surface)
    {
        bool[,] voxels = surface?.Voxels;
        if (voxels == null) return 0;

        int count = 0;
        for (int x = 0; x < voxels.GetLength(0); x++)
        {
            for (int z = 0; z < voxels.GetLength(1); z++)
            {
                if (voxels[x, z]) count++;
            }
        }
        return count;
    }
}
