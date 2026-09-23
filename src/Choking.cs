using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace BadLuck;

/// <summary>
/// Mechanic 16: you choke on your food and cough. Coughing is loud - for a while afterwards creatures
/// notice the player from further away (the animalSeekingRange game stat).
/// </summary>
public static class Choking
{
    static readonly SeekRangeBoost notice = new("badluck-cough");

    static readonly AssetLocation[] CoughSounds =
    [
        new(BadLuckModSystem.ModId, "sounds/player/cough1"),
        new(BadLuckModSystem.ModId, "sounds/player/cough2"),
        new(BadLuckModSystem.ModId, "sounds/player/cough3")
    ];

    static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        notice.Clear();
        api.Event.RegisterGameTickListener(_ => notice.RemoveExpired(sapi), 1000);
        api.Event.PlayerDisconnect += player => notice.Remove(player);
    }

    /// <summary>A piece of food, or one serving of a meal, was eaten</summary>
    public static void OnAte(IServerPlayer player)
    {
        ChokingConfig cfg = BadLuckModSystem.Config.Choking;
        if (!cfg.Enabled || player?.Entity == null || !player.Entity.Alive) return;
        if (!BadLuckModSystem.Affects(player) || !BadLuckModSystem.Roll(sapi.World, cfg.ChancePercent)) return;

        BadLuckModSystem.Chat(player, "badluck:choking-message");
        Cough(player, cfg.Coughs, (int)(cfg.FitSeconds * 1000 / System.Math.Max(1, cfg.Coughs)));

        notice.Apply(player.Entity, cfg.NoticeBonus, cfg.NoticeSeconds);
    }

    /// <summary>Choke right now (test command)</summary>
    public static void ForceChoke(IServerPlayer player)
    {
        ChokingConfig cfg = BadLuckModSystem.Config.Choking;
        if (player?.Entity == null) return;

        BadLuckModSystem.Chat(player, "badluck:choking-message");
        Cough(player, cfg.Coughs, (int)(cfg.FitSeconds * 1000 / System.Math.Max(1, cfg.Coughs)));
    }

    static void Cough(IServerPlayer player, int left, int intervalMs)
    {
        if (left <= 0 || player.Entity == null || !player.Entity.Alive) return;

        AssetLocation sound = CoughSounds[sapi.World.Rand.Next(CoughSounds.Length)];
        sapi.World.PlaySoundAt(sound, player.Entity, null, true, 24);

        if (left > 1) sapi.Event.RegisterCallback(_ => Cough(player, left - 1, intervalMs), intervalMs);
    }
}
