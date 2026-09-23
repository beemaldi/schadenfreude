using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace BadLuck;

public class BadLuckModSystem : ModSystem
{
    public const string ModId = "badluck";
    const string ConfigFileName = "badluck.json";
    const int WelcomeDelayMs = 3000;

    public static BadLuckConfig Config { get; private set; } = new();
    public static ILogger Logger { get; private set; }

    Harmony harmony;
    ICoreServerAPI sapi;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("badluck.runningplant", typeof(EntityBehaviorRunningPlant));
        api.RegisterEntityBehaviorClass("badluck.snake", typeof(EntityBehaviorSnake));
        api.RegisterEntityBehaviorClass("badluck.mimic", typeof(EntityBehaviorMimic));

        // In single player client and server share one process: patch only once.
        if (!Harmony.HasAnyPatches(ModId))
        {
            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(BadLuckModSystem).Assembly);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Logger = Mod.Logger;
        LoadConfig();

        // The Integrated Mod Manager reports changed settings through this event
        api.Event.RegisterEventBusListener(OnSettingsChanged, filterByEventName: "imm." + ModId);

        RunningPlant.Register(api);
        EatStone.Register(api);
        GroundHazards.Register(api);
        Flatulence.Register(api);
        Splinter.Register(api);
        AiTaskOpenDoor.Register(api);
        EyeChip.Register(api);
        TorchFire.Register(api);
        Mishaps.Register(api);
        Choking.Register(api);
        Mimic.Register(api);
        StickBreak.Register(api);
        Commands.Register(api);
        api.Event.PlayerNowPlaying += Greet;
        ToolFly.Reset();
        CreatureMovement.Reset();
        api.Event.PlayerDisconnect += ToolFly.Forget;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.RegisterRenderer(new EyeChipRenderer(api), EnumRenderStage.Ortho, "badluck-eyechip");
        new ClientMishaps(api);
    }

    /// <summary>
    /// Show a notification shortly after joining the world, so you can tell at once that the mod is
    /// running. Delayed, so it does not get buried under the server greeting.
    /// </summary>
    void Greet(IServerPlayer player)
    {
        if (!Config.WelcomeMessage) return;

        sapi.Event.RegisterCallback(_ =>
        {
            if (player.ConnectionState != EnumClientState.Playing) return;
            player.SendMessage(GlobalConstants.AllChatGroups, Lang.GetL("en", "badluck:welcome-message"), EnumChatType.Notification);
        }, WelcomeDelayMs);
    }

    void LoadConfig()
    {
        BadLuckConfig loaded = null;
        bool readFailed = false;
        try
        {
            loaded = sapi.LoadModConfig<BadLuckConfig>(ConfigFileName);
        }
        catch (Exception e)
        {
            readFailed = true;
            Mod.Logger.Error("Could not read ModConfig/{0}, using defaults: {1}", ConfigFileName, e.Message);
        }

        Config = loaded ?? new BadLuckConfig();
        Config.ClampToSaneValues();

        // Re-read the patterns, the values may have changed
        CodePatterns.Clear();
        Flatulence.OnConfigLoaded();

        // Fill in missing values; never overwrite a broken file with defaults
        if (!readFailed) sapi.StoreModConfig(Config, ConfigFileName);
    }

    void OnSettingsChanged(string eventName, ref EnumHandling handling, IAttribute data)
    {
        LoadConfig();
        EatStone.RerollAll(sapi);
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(ModId);
        harmony = null;
    }

    /// <summary>Does the bad luck apply to this player (game mode)?</summary>
    public static bool Affects(IPlayer player)
    {
        if (player == null) return false;
        return !Config.OnlyInSurvival || player.WorldData?.CurrentGameMode == EnumGameMode.Survival;
    }

    /// <summary>Chat messages are always English (the author wants it that way), whatever language the player uses</summary>
    public static void Chat(IPlayer player, string langKey)
    {
        if (player is IServerPlayer splayer)
        {
            splayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL("en", langKey), EnumChatType.Notification);
        }
    }

    /// <summary>Does the code match one of the patterns (with domain, * as a wildcard)?</summary>
    public static bool CodeMatches(string[] patterns, AssetLocation code)
    {
        return CodePatterns.Matches(patterns, code, Logger);
    }

    /// <summary>Is there anything at this position you could stand on?</summary>
    public static bool IsSolid(IBlockAccessor ba, BlockPos pos)
    {
        Cuboidf[] boxes = ba.GetBlock(pos).GetCollisionBoxes(ba, pos);
        return boxes != null && boxes.Length > 0;
    }

    public static bool Roll(IWorldAccessor world, double percent)
    {
        return percent > 0 && world.Rand.NextDouble() * 100 < percent;
    }
}
