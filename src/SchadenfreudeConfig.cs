using System;

namespace Schadenfreude;

/// <summary>
/// Contents of ModConfig/schadenfreude.json. Edited in game through the Integrated Mod Manager
/// (assets/schadenfreude/config/imm.json). All chances are percentages.
/// </summary>
public class SchadenfreudeConfig
{
    public bool OnlyInSurvival = true;

    /// <summary>A notification on joining the world, so you can tell the mod is running</summary>
    public bool WelcomeMessage = true;

    /// <summary>Silly sound effects instead of the serious ones (bear door, fart, snake, falling ...)</summary>
    public bool StupidSoundEffects = true;

    public RunningPlantConfig RunningPlant = new();
    public EatStoneConfig EatStone = new();
    public FireAntsConfig FireAnts = new();
    public HornetsConfig Hornets = new();
    public ExplodingStoneConfig ExplodingStone = new();
    public StickBreakConfig StickBreak = new();
    public FlatulenceConfig Flatulence = new();
    public SplinterConfig Splinter = new();
    public BearDoorsConfig BearDoors = new();
    public SnakeConfig Snake = new();
    public EyeChipConfig EyeChip = new();
    public DoorHingesConfig DoorHinges = new();
    public ToolFlyConfig ToolFly = new();
    public TorchFireConfig TorchFire = new();
    public CliffConfig Cliff = new();
    public StumbleConfig Stumble = new();
    public ChokingConfig Choking = new();
    public MimicConfig Mimic = new();
    public BoomerangConfig Boomerang = new();

    /// <summary>
    /// Keeps values from the config file within sane limits. Guards against typos (negative chances)
    /// and against a huge radius slowing the server down.
    /// </summary>
    public void ClampToSaneValues()
    {
        static double Pct(double v) => Math.Clamp(v, 0, 100);
        static double Sec(double v, double max) => Math.Clamp(v, 0, max);
        static float Rad(float v, float max) => Math.Clamp(v, 0, max);

        RunningPlant.ChancePercent = Pct(RunningPlant.ChancePercent);
        RunningPlant.ReplantSeconds = Sec(RunningPlant.ReplantSeconds, 3600);

        EatStone.ChancePercent = Pct(EatStone.ChancePercent);
        EatStone.Damage = Math.Clamp(EatStone.Damage, 0, 100);

        FireAnts.ChancePercent = Pct(FireAnts.ChancePercent);
        FireAnts.DamagePerSecond = Math.Clamp(FireAnts.DamagePerSecond, 0, 100);
        FireAnts.DurationSeconds = Math.Clamp(FireAnts.DurationSeconds, 1, 600);

        Hornets.ChancePercent = Pct(Hornets.ChancePercent);
        Hornets.MinSwarms = Math.Clamp(Hornets.MinSwarms, 0, 20);
        Hornets.MaxSwarms = Math.Clamp(Hornets.MaxSwarms, Hornets.MinSwarms, 20);

        ExplodingStone.ChancePercent = Pct(ExplodingStone.ChancePercent);
        ExplodingStone.Radius = Rad(ExplodingStone.Radius, 32);
        ExplodingStone.MaxDamage = Math.Clamp(ExplodingStone.MaxDamage, 0, 1000);
        ExplodingStone.BlockRadius = Rad(ExplodingStone.BlockRadius, 12);
        ExplodingStone.BlockDropChance = Math.Clamp(ExplodingStone.BlockDropChance, 0, 1);

        StickBreak.ChancePercent = Pct(StickBreak.ChancePercent);

        Splinter.StingChancePercent = Pct(Splinter.StingChancePercent);
        Splinter.CatchFromToolsPercent = Pct(Splinter.CatchFromToolsPercent);
        Splinter.DurationMinutes = Sec(Splinter.DurationMinutes, 24 * 60);
        Splinter.Damage = Math.Clamp(Splinter.Damage, 0, 100);

        Flatulence.FartsPerVegetableFresh = Math.Clamp(Flatulence.FartsPerVegetableFresh, 0, 20);
        Flatulence.FartsPerVegetableOld = Math.Clamp(Flatulence.FartsPerVegetableOld, 0, 20);
        Flatulence.MinutesFresh = Sec(Flatulence.MinutesFresh, 600);
        Flatulence.MinutesOld = Sec(Flatulence.MinutesOld, 600);
        Flatulence.RadiusFresh = Rad(Flatulence.RadiusFresh, 64);
        Flatulence.RadiusOld = Rad(Flatulence.RadiusOld, 64);
        Flatulence.NoticeSeconds = Sec(Flatulence.NoticeSeconds, 600);
        Flatulence.CloudSeconds = Sec(Flatulence.CloudSeconds, 600);
        Flatulence.PsychedelicSeconds = Sec(Flatulence.PsychedelicSeconds, 600);
        Flatulence.CloudRadius = Rad(Flatulence.CloudRadius, 16);
        Flatulence.PsychedelicFresh = Math.Clamp(Flatulence.PsychedelicFresh, 0, 2);
        Flatulence.PsychedelicOld = Math.Clamp(Flatulence.PsychedelicOld, 0, 2);
        Flatulence.DizzyRadius = Rad(Flatulence.DizzyRadius, 32);
        Flatulence.DizzyAmount = Math.Clamp(Flatulence.DizzyAmount, 0, 1.1f);

        BearDoors.StareSeconds = Sec(BearDoors.StareSeconds, 60);
        BearDoors.SearchRadius = Rad(BearDoors.SearchRadius, 24);
        BearDoors.RetreatSeconds = Sec(BearDoors.RetreatSeconds, 600);
        BearDoors.RetreatBlocks = Rad(BearDoors.RetreatBlocks, 128);
        BearDoors.RetreatAfterDoorSeconds = Sec(BearDoors.RetreatAfterDoorSeconds, 600);

        Snake.ChancePercent = Pct(Snake.ChancePercent);
        Snake.BiteDamage = Math.Clamp(Snake.BiteDamage, 0, 100);
        Snake.PoisonDamage = Math.Clamp(Snake.PoisonDamage, 0, 100);
        Snake.PoisonSeconds = Sec(Snake.PoisonSeconds, 600);
        Snake.DespawnSeconds = Sec(Snake.DespawnSeconds, 3600);

        EyeChip.ChancePercent = Pct(EyeChip.ChancePercent);
        EyeChip.KnappingChancePercent = Pct(EyeChip.KnappingChancePercent);
        EyeChip.DurationSeconds = Sec(EyeChip.DurationSeconds, 300);

        DoorHinges.ChancePercent = Pct(DoorHinges.ChancePercent);

        ToolFly.ChancePercent = Pct(ToolFly.ChancePercent);
        ToolFly.HitDamage = Math.Clamp(ToolFly.HitDamage, 0, 100);
        ToolFly.MaxDistanceBlocks = Math.Clamp(ToolFly.MaxDistanceBlocks, 0.5, 32);

        TorchFire.ChancePercentPerSecond = Pct(TorchFire.ChancePercentPerSecond);
        TorchFire.MinWindSpeed = Math.Clamp(TorchFire.MinWindSpeed, 0, 10);
        TorchFire.MaxAngleDegrees = Math.Clamp(TorchFire.MaxAngleDegrees, 0, 180);
        TorchFire.MaxClothingConditionPercent = Pct(TorchFire.MaxClothingConditionPercent);

        Cliff.ChancePercentPerSecond = Pct(Cliff.ChancePercentPerSecond);
        Cliff.MinDropBlocks = Math.Clamp(Cliff.MinDropBlocks, 1, 64);

        Stumble.ChancePercentPerSecond = Pct(Stumble.ChancePercentPerSecond);
        Stumble.MinIntoxication = Math.Clamp(Stumble.MinIntoxication, 0, 1.1f);
        Stumble.ChaseRange = Rad(Stumble.ChaseRange, 64);
        Stumble.LieSeconds = Sec(Stumble.LieSeconds, 60);

        Choking.ChancePercent = Pct(Choking.ChancePercent);
        Choking.Coughs = Math.Clamp(Choking.Coughs, 1, 20);
        Choking.FitSeconds = Sec(Choking.FitSeconds, 120);
        Choking.CoughGapSeconds = Sec(Choking.CoughGapSeconds, 60);
        Choking.NoticeBonus = Math.Clamp(Choking.NoticeBonus, 0, 20);
        Choking.NoticeSeconds = Sec(Choking.NoticeSeconds, 600);

        Mimic.ChancePercent = Pct(Mimic.ChancePercent);

        Boomerang.ChancePercent = Pct(Boomerang.ChancePercent);
        Boomerang.Damage = Math.Clamp(Boomerang.Damage, 0, 100);
        Boomerang.ReturnAfterSeconds = Sec(Boomerang.ReturnAfterSeconds, 10);
    }
}

public class RunningPlantConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
    public double ReplantSeconds = 20;
}

public class EatStoneConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
    public float Damage = 2;
}

public class FireAntsConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
    public float DamagePerSecond = 0.5f;
    public int DurationSeconds = 6;
}

public class HornetsConfig
{
    public bool Enabled = true;
    public double ChancePercent = 0.1;

    /// <summary>Only someone running stamps hard enough</summary>
    public bool OnlyWhenSprinting = true;
    public int MinSwarms = 2;
    public int MaxSwarms = 3;
}

public class ExplodingStoneConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
    public float Radius = 5;
    public float MaxDamage = 6;

    /// <summary>Does the explosion tear a hole into the landscape as well?</summary>
    public bool DestroyBlocks = true;

    /// <summary>
    /// Crater radius. Measured in solid granite: 2 = 1 block, 3 = 8 blocks, 4 = 46 blocks
    /// (as big as a blasting bomb in the game).
    /// </summary>
    public float BlockRadius = 3;

    /// <summary>How much of the destroyed blocks can still be picked up (1 = everything)</summary>
    public float BlockDropChance = 1;

    /// <summary>Thrown by hand: what counts as a stone (sling stones always count)</summary>
    public string[] ThrownCodes = ["game:stone-*", "game:ore-*", "game:nugget-*", "game:flint"];
}

public class StickBreakConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
}

/// <summary>Splinter from a broken stick (mechanic 6)</summary>
public class SplinterConfig
{
    public bool Enabled = true;
    public double DurationMinutes = 10;
    public double StingChancePercent = 10;
    public float Damage = 1;

    public bool StingOnMining = true;
    public bool StingOnAttacking = true;
    public bool StingOnCrafting = true;
    public bool StingOnBuildingAndUsing = true;

    public bool BandageRemoves = true;

    /// <summary>Tools with a wooden handle (a stick in the recipe): every use that costs durability</summary>
    public double CatchFromToolsPercent = 0.5;
    public bool StingOnToolUse = true;
}

/// <summary>Mechanic 8: bears open doors</summary>
public class BearDoorsConfig
{
    public bool Enabled = true;
    public string[] OpenerCodes = ["game:bear-*"];
    public double StareSeconds = 3.3;

    /// <summary>After an opened door plus a dead player: this is how long the bear withdraws for</summary>
    public double RetreatSeconds = 90;

    /// <summary>This far away from the place of death</summary>
    public float RetreatBlocks = 40;

    /// <summary>Only if the door was opened no longer than this many seconds ago</summary>
    public double RetreatAfterDoorSeconds = 120;

    /// <summary>These gates always break when opened - they cannot take a bear</summary>
    public string[] TearOutCodes = ["game:woodenfencegate-*", "game:roughhewnfencegate-*", "game:wattlegate-*"];

    /// <summary>How far around itself the bear looks for closed doors</summary>
    public float SearchRadius = 8;
    public bool WhileLured = true;
}

/// <summary>
/// "Fresh" = just harvested, "Old" = just short of rotting. In between the two are blended linearly.
/// </summary>
public class FlatulenceConfig
{
    public bool Enabled = true;

    public float FartsPerVegetableFresh = 1;
    public float FartsPerVegetableOld = 5;

    public double MinutesFresh = 5;
    public double MinutesOld = 10;

    public float RadiusFresh = 24;
    public float RadiusOld = 64;

    /// <summary>Bonus on the detection range (0.5 = 50 % further)</summary>
    public float NoticeBonusFresh = 0.5f;
    public float NoticeBonusOld = 1.5f;
    public double NoticeSeconds = 30;

    public bool GasCloud = true;
    public double CloudSeconds = 10;
    public float CloudRadius = 1.8f;
    public bool CloudPsychedelic = true;
    /// <summary>The game wears the trip off far too slowly on its own; after this long it is taken away again</summary>
    public double PsychedelicSeconds = 10;

    /// <summary>Mushroom trip when walking into it (the "psychedelic" game stat: fly agaric 0.4 ... blue meanie 2)</summary>
    public float PsychedelicFresh = 0.4f;
    public float PsychedelicOld = 1.4f;
    public float DizzyRadius = 3;
    public float DizzyAmount = 0.25f;

    public string[] VegetableCodes = ["game:vegetable-*", "game:pickledvegetable-*", "game:rawcassava-*"];

    /// <summary>Creatures with one of these game tags or codes are drawn in, except the excluded ones</summary>
    public string[] LureTags = ["ferocious", "rust-creature"];
    public string[] LureCodes = ["game:locust-bronze", "game:locust-corrupt", "game:locust-corrupt-sawblade"];
    public string[] LureExcludeCodes = ["game:bear-panda-*", "game:bear-sun-*", "game:*-baby-*", "game:*-hacked"];
}

/// <summary>Mechanic 9: a snake drops down when a tree is felled</summary>
public class SnakeConfig
{
    public bool Enabled = true;
    public double ChancePercent = 5;
    public float BiteDamage = 2;
    public float PoisonDamage = 3;
    public double PoisonSeconds = 15;
    public double DespawnSeconds = 30;

    /// <summary>Conifers (wood type taken from the trunk code)</summary>
    public string[] ExcludedWoods = ["pine", "larch", "redwood", "baldcypress", "cypress"];
}

/// <summary>Mechanic 10: a stone chip flies into the eye while mining or knapping</summary>
public class EyeChipConfig
{
    public bool Enabled = true;
    public double ChancePercent = 1;
    /// <summary>Rolled per struck-off chip, so it has to be much lower than the mining chance</summary>
    public double KnappingChancePercent = 0.5;
    public double DurationSeconds = 10;
    public string[] BlockCodes = ["game:rock-*", "game:crackedrock-*", "game:ore-*"];
}

/// <summary>Mechanic 11: doors fly off their hinges</summary>
public class DoorHingesConfig
{
    public bool Enabled = true;
    public double ChancePercent = 0.5;
    public bool OnOpen = true;
    public bool OnClose = true;
    public bool OnBear = true;
}

/// <summary>Mechanic 12: a tool or weapon flies out of your hand</summary>
public class ToolFlyConfig
{
    public bool Enabled = true;
    public double ChancePercent = 0.5;
    public float HitDamage = 1;

    /// <summary>The furthest the tool flies away (in blocks)</summary>
    public double MaxDistanceBlocks = 2;
}

/// <summary>Mechanic 13: a torch held into the wind sets worn-out clothes on fire</summary>
public class TorchFireConfig
{
    public bool Enabled = true;
    public double ChancePercentPerSecond = 2;
    public double MinWindSpeed = 0.35;
    public double MaxAngleDegrees = 60;
    public double MaxClothingConditionPercent = 20;
    public string[] TorchCodes = ["game:torch-*-lit-*"];
}

/// <summary>Mechanic 14: slipping off the edge while sneaking</summary>
public class CliffConfig
{
    public bool Enabled = true;
    public double ChancePercentPerSecond = 2;
    public int MinDropBlocks = 3;
}

/// <summary>Mechanic 15: stumbling when drunk or chased</summary>
public class StumbleConfig
{
    public bool Enabled = true;

    /// <summary>You only stumble while running</summary>
    public bool OnlyWhenSprinting = true;
    public double ChancePercentPerSecond = 1;
    public bool WhenDrunk = true;
    public bool WhenChased = true;
    public float MinIntoxication = 0.2f;
    public float ChaseRange = 24;
    public double LieSeconds = 3;
}

/// <summary>Mechanic 16: choking on your food and coughing</summary>
public class ChokingConfig
{
    public bool Enabled = true;
    public double ChancePercent = 2;
    public int Coughs = 3;
    public double FitSeconds = 5;
    /// <summary>At least this long between two coughs - also blocks a new fit while one is running</summary>
    public double CoughGapSeconds = 4;

    /// <summary>Coughs are loud: a bonus on the range from which creatures notice the player</summary>
    public float NoticeBonus = 1f;
    public double NoticeSeconds = 8;
}

/// <summary>Mechanic 17: chests in ruins and dungeons are sometimes mimics</summary>
public class MimicConfig
{
    public bool Enabled = true;
    public double ChancePercent = 5;
    public string[] ChestCodes = ["game:chest-*"];

    /// <summary>Worldgen structures that contain mimics</summary>
    public string[] StructureCodes = ["*ruin*", "*dungeon*"];
}

/// <summary>Mechanic 19: the thrown stone comes back</summary>
public class BoomerangConfig
{
    public bool Enabled = true;
    public double ChancePercent = 5;
    public float Damage = 2;

    /// <summary>This is how long the stone flies normally before it turns around</summary>
    public double ReturnAfterSeconds = 0.6;
}
